using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Profiling;

namespace MCPForUnity.Editor.Tools.Profiler
{
    /// <summary>
    /// Reads recorded Profiler data from .raw files via ProfilerDriver (reflection)
    /// and HierarchyFrameDataView (public API, Unity 2021+).
    /// </summary>
    internal static class ProfilerDataOps
    {
        // --- Reflected ProfilerDriver members ---
        private static readonly Type ProfilerDriverType;
        private static readonly MethodInfo LoadProfileMethod;
        private static readonly PropertyInfo FirstFrameIndexProp;
        private static readonly PropertyInfo LastFrameIndexProp;
        private static readonly MethodInfo GetHierarchyViewMethod;
        private static readonly bool Available;
        private static readonly bool HierarchyAvailable;

        // --- Column indices for HierarchyFrameDataView ---
        private static readonly ColumnIndices Columns;

        private struct ColumnIndices
        {
            public int TotalPercent;
            public int TotalTime;
            public int SelfPercent;
            public int SelfTime;
            public int Calls;
            public int GcMemory;
            public bool Resolved;
        }

        // Max frames for get_hotspots aggregation to avoid long stalls
        private const int MaxHotspotFrames = 300;

        static ProfilerDataOps()
        {
            try
            {
                ProfilerDriverType = Type.GetType("UnityEditorInternal.ProfilerDriver, UnityEditor");
                if (ProfilerDriverType == null) return;

                var flags = BindingFlags.Public | BindingFlags.Static;

                // LoadProfile(string path)
                LoadProfileMethod = ProfilerDriverType.GetMethod("LoadProfile", flags, null, new[] { typeof(string) }, null)
                                 ?? ProfilerDriverType.GetMethod("Load", flags, null, new[] { typeof(string) }, null);

                // firstFrameIndex / lastFrameIndex
                FirstFrameIndexProp = ProfilerDriverType.GetProperty("firstFrameIndex", flags)
                                   ?? ProfilerDriverType.GetProperty("FirstFrameIndex", flags);
                LastFrameIndexProp = ProfilerDriverType.GetProperty("lastFrameIndex", flags)
                                  ?? ProfilerDriverType.GetProperty("LastFrameIndex", flags);

                // GetHierarchyFrameDataView(int frameIndex, int threadIndex, int viewMode, int sortColumn, bool sortAscending)
                GetHierarchyViewMethod = ProfilerDriverType.GetMethod("GetHierarchyFrameDataView", flags, null,
                    new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool) }, null);

                Available = LoadProfileMethod != null && FirstFrameIndexProp != null && LastFrameIndexProp != null;
                HierarchyAvailable = Available && GetHierarchyViewMethod != null;

                Columns = ResolveColumns();
            }
            catch
            {
                Available = false;
                HierarchyAvailable = false;
            }
        }

        #region Column Resolution

        private static ColumnIndices ResolveColumns()
        {
            return new ColumnIndices
            {
                TotalPercent = GetStaticIntField(typeof(HierarchyFrameDataView), "columnTotalPercent", 0),
                TotalTime = GetStaticIntField(typeof(HierarchyFrameDataView), "columnTotalTime", 1),
                SelfPercent = GetStaticIntField(typeof(HierarchyFrameDataView), "columnSelfPercent", 2),
                SelfTime = GetStaticIntField(typeof(HierarchyFrameDataView), "columnSelfTime", 3),
                Calls = GetStaticIntField(typeof(HierarchyFrameDataView), "columnCalls", 4),
                GcMemory = GetStaticIntField(typeof(HierarchyFrameDataView), "columnGcMemory", 5),
                Resolved = true,
            };
        }

        private static int GetStaticIntField(Type type, string name, int fallback)
        {
            try
            {
                var flags = BindingFlags.Public | BindingFlags.Static;
                var field = type.GetField(name, flags);
                if (field != null) return (int)field.GetValue(null);

                var prop = type.GetProperty(name, flags);
                if (prop != null) return (int)prop.GetValue(null);
            }
            catch { /* use fallback */ }
            return fallback;
        }

        private static int ResolveSortColumn(string sortBy)
        {
            switch ((sortBy ?? "total_time").ToLowerInvariant())
            {
                case "total_time": return Columns.TotalTime;
                case "self_time": return Columns.SelfTime;
                case "calls": return Columns.Calls;
                case "gc_alloc": return Columns.GcMemory;
                case "total_percent": return Columns.TotalPercent;
                case "self_percent": return Columns.SelfPercent;
                default: return Columns.TotalTime;
            }
        }

        #endregion

        #region Helpers

        private static int GetFirstFrame() => (int)FirstFrameIndexProp.GetValue(null);
        private static int GetLastFrame() => (int)LastFrameIndexProp.GetValue(null);

        /// <summary>
        /// Get a HierarchyFrameDataView via reflected ProfilerDriver. Caller must Dispose.
        /// viewMode 0 = MergeSamplesWithTreePath.
        /// </summary>
        private static HierarchyFrameDataView GetHierarchyView(int frameIndex, int threadIndex, int sortColumn)
        {
            var viewObj = GetHierarchyViewMethod.Invoke(null, new object[]
            {
                frameIndex, threadIndex, /*viewMode*/ 0, sortColumn, /*ascending*/ false
            });

            if (viewObj is HierarchyFrameDataView view)
                return view;

            // Unknown return type — dispose if possible
            (viewObj as IDisposable)?.Dispose();
            return null;
        }

        /// <summary>
        /// Check if a profile is loaded and frame range is valid.
        /// </summary>
        private static bool IsProfileLoaded(out int firstFrame, out int lastFrame)
        {
            firstFrame = GetFirstFrame();
            lastFrame = GetLastFrame();
            return lastFrame >= firstFrame && firstFrame >= 0;
        }

        private static object NotAvailableError()
        {
            return new ErrorResponse(
                "ProfilerDriver not available via reflection. This feature requires Unity 2021+.");
        }

        private static object HierarchyNotAvailableError()
        {
            return new ErrorResponse(
                "GetHierarchyFrameDataView not available. Profile reading requires Unity 2021+.");
        }

        private static object NoProfileError()
        {
            return new ErrorResponse(
                "No profile data loaded. Call load_profile first to load a .raw recording.");
        }

        #endregion

        #region Action: load_profile

        internal static object LoadProfile(JObject @params)
        {
            if (!Available)
                return NotAvailableError();

            var p = new ToolParams(@params);
            var filePathResult = p.GetRequired("file_path");
            if (!filePathResult.IsSuccess)
                return new ErrorResponse(filePathResult.ErrorMessage);

            string filePath = filePathResult.Value;
            if (!File.Exists(filePath))
                return new ErrorResponse($"File not found: {filePath}");

            try
            {
                LoadProfileMethod.Invoke(null, new object[] { filePath });
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to load profile: {ex.InnerException?.Message ?? ex.Message}");
            }

            int firstFrame = GetFirstFrame();
            int lastFrame = GetLastFrame();
            int frameCount = lastFrame >= firstFrame ? lastFrame - firstFrame + 1 : 0;

            string warning = null;
            if (EditorApplication.isPlaying)
                warning = "Profile loaded while in Play mode. Live profiling data is now disconnected.";

            return new SuccessResponse($"Profile loaded: {frameCount} frames.", new
            {
                file_path = filePath,
                first_frame = firstFrame,
                last_frame = lastFrame,
                frame_count = frameCount,
                hierarchy_available = HierarchyAvailable,
                warning,
            });
        }

        #endregion

        #region Action: get_profile_summary

        internal static object GetProfileSummary(JObject @params)
        {
            if (!HierarchyAvailable)
                return HierarchyNotAvailableError();

            if (!IsProfileLoaded(out int firstFrame, out int lastFrame))
                return NoProfileError();

            var p = new ToolParams(@params);
            int frameStart = p.GetInt("frame_start") ?? firstFrame;
            int frameEnd = p.GetInt("frame_end") ?? lastFrame;

            // Clamp to valid range
            frameStart = Math.Max(frameStart, firstFrame);
            frameEnd = Math.Min(frameEnd, lastFrame);
            if (frameEnd < frameStart)
                return new ErrorResponse($"Invalid frame range: {frameStart}-{frameEnd}.");

            int totalFrames = frameEnd - frameStart + 1;

            // Pass 1: lightweight stats — root total time for every frame
            var frameTimes = new double[totalFrames];
            double sumTime = 0, maxTime = 0, minTime = double.MaxValue;
            int maxTimeFrame = frameStart;
            int slowFrameCount = 0;

            for (int i = 0; i < totalFrames; i++)
            {
                int fi = frameStart + i;
                double totalTime = 0;

                using (var view = GetHierarchyView(fi, 0, Columns.TotalTime))
                {
                    if (view != null)
                    {
                        int rootId = view.GetRootItemID();
                        totalTime = view.GetItemColumnDataAsSingle(rootId, Columns.TotalTime);
                    }
                }

                frameTimes[i] = totalTime;
                sumTime += totalTime;

                if (totalTime > maxTime) { maxTime = totalTime; maxTimeFrame = fi; }
                if (totalTime < minTime) minTime = totalTime;
                if (totalTime > 33.33) slowFrameCount++;
            }

            if (minTime == double.MaxValue) minTime = 0;
            double avgTime = totalFrames > 0 ? sumTime / totalFrames : 0;

            // Pass 2: build paginated frame list with top function name
            var pagination = PaginationRequest.FromParams(@params);
            var allFrameSummaries = new List<object>(totalFrames);

            for (int i = 0; i < totalFrames; i++)
            {
                int fi = frameStart + i;
                string topFunction = null;

                // Only compute top function for the page range to save time
                if (i >= pagination.Cursor && i < pagination.Cursor + pagination.PageSize)
                {
                    topFunction = GetTopFunctionForFrame(fi);
                }

                allFrameSummaries.Add(new
                {
                    frame_index = fi,
                    total_time_ms = Math.Round(frameTimes[i], 3),
                    top_function = topFunction,
                });
            }

            var page = PaginationResponse<object>.Create(allFrameSummaries, pagination);

            return new SuccessResponse($"Profile summary: {totalFrames} frames, avg {avgTime:F1}ms.", new
            {
                first_frame = frameStart,
                last_frame = frameEnd,
                total_frames = totalFrames,
                avg_frame_time_ms = Math.Round(avgTime, 3),
                max_frame_time_ms = Math.Round(maxTime, 3),
                max_frame_index = maxTimeFrame,
                min_frame_time_ms = Math.Round(minTime, 3),
                slow_frame_count = slowFrameCount,
                frames = page.Items,
                cursor = page.Cursor,
                next_cursor = page.NextCursor,
                page_size = page.PageSize,
                total_count = page.TotalCount,
                has_more = page.HasMore,
            });
        }

        /// <summary>
        /// Find the child of root with the highest self time for a given frame.
        /// </summary>
        private static string GetTopFunctionForFrame(int frameIndex)
        {
            using (var view = GetHierarchyView(frameIndex, 0, Columns.SelfTime))
            {
                if (view == null) return null;

                int rootId = view.GetRootItemID();
                var children = new List<int>();
                view.GetItemChildren(rootId, children);

                if (children.Count == 0) return null;

                string topName = null;
                float topSelfTime = -1;

                foreach (int childId in children)
                {
                    float selfTime = view.GetItemColumnDataAsSingle(childId, Columns.SelfTime);
                    if (selfTime > topSelfTime)
                    {
                        topSelfTime = selfTime;
                        topName = view.GetItemName(childId);
                    }
                }

                return topName;
            }
        }

        #endregion

        #region Action: get_frame_hierarchy

        internal static object GetFrameHierarchy(JObject @params)
        {
            if (!HierarchyAvailable)
                return HierarchyNotAvailableError();

            if (!IsProfileLoaded(out int firstFrame, out int lastFrame))
                return NoProfileError();

            var p = new ToolParams(@params);
            var frameIndexResult = p.GetRequired("frame_index");
            if (!frameIndexResult.IsSuccess)
                return new ErrorResponse(frameIndexResult.ErrorMessage);

            int frameIndex = int.Parse(frameIndexResult.Value);
            if (frameIndex < firstFrame || frameIndex > lastFrame)
                return new ErrorResponse($"Frame {frameIndex} out of range [{firstFrame}, {lastFrame}].");

            int threadIndex = p.GetInt("thread_index") ?? 0;
            int maxDepth = p.GetInt("depth") ?? 5;
            float minTimeMs = p.GetFloat("min_time_ms") ?? 0f;
            string sortBy = p.Get("sort_by", "total_time");
            int sortColumn = ResolveSortColumn(sortBy);

            using (var view = GetHierarchyView(frameIndex, threadIndex, sortColumn))
            {
                if (view == null)
                    return new ErrorResponse($"Cannot get hierarchy for frame {frameIndex}, thread {threadIndex}.");

                int rootId = view.GetRootItemID();
                float frameTotalTime = view.GetItemColumnDataAsSingle(rootId, Columns.TotalTime);

                // DFS flatten the hierarchy
                var flatItems = new List<object>();
                FlattenHierarchy(view, rootId, 0, maxDepth, minTimeMs, flatItems);

                // Paginate
                var pagination = PaginationRequest.FromParams(@params);
                var page = PaginationResponse<object>.Create(flatItems, pagination);

                return new SuccessResponse(
                    $"Frame {frameIndex} hierarchy: {flatItems.Count} items, {frameTotalTime:F1}ms total.", new
                    {
                        frame_index = frameIndex,
                        thread_index = threadIndex,
                        frame_total_time_ms = Math.Round(frameTotalTime, 3),
                        items = page.Items,
                        cursor = page.Cursor,
                        next_cursor = page.NextCursor,
                        page_size = page.PageSize,
                        total_count = page.TotalCount,
                        has_more = page.HasMore,
                    });
            }
        }

        /// <summary>
        /// DFS traverse the hierarchy, flattening into a list with depth markers.
        /// </summary>
        private static void FlattenHierarchy(
            HierarchyFrameDataView view, int itemId,
            int currentDepth, int maxDepth, float minTimeMs,
            List<object> output)
        {
            float totalTime = view.GetItemColumnDataAsSingle(itemId, Columns.TotalTime);

            // Skip items below time threshold (but always include root at depth 0)
            if (currentDepth > 0 && totalTime < minTimeMs)
                return;

            var children = new List<int>();
            view.GetItemChildren(itemId, children);

            output.Add(new
            {
                name = view.GetItemName(itemId),
                total_time_ms = Math.Round(totalTime, 3),
                self_time_ms = Math.Round((double)view.GetItemColumnDataAsSingle(itemId, Columns.SelfTime), 3),
                total_percent = Math.Round((double)view.GetItemColumnDataAsSingle(itemId, Columns.TotalPercent), 2),
                self_percent = Math.Round((double)view.GetItemColumnDataAsSingle(itemId, Columns.SelfPercent), 2),
                calls = (int)view.GetItemColumnDataAsSingle(itemId, Columns.Calls),
                gc_alloc_bytes = (long)view.GetItemColumnDataAsSingle(itemId, Columns.GcMemory),
                depth = currentDepth,
                children_count = children.Count,
            });

            // Recurse into children if within depth limit
            if (currentDepth < maxDepth)
            {
                foreach (int childId in children)
                {
                    FlattenHierarchy(view, childId, currentDepth + 1, maxDepth, minTimeMs, output);
                }
            }
        }

        #endregion

        #region Action: get_hotspots

        /// <summary>
        /// Aggregated profiler sample for hotspot analysis.
        /// </summary>
        private class AggregatedSample
        {
            public string Name;
            public double TotalSelfTimeMs;
            public double TotalTotalTimeMs;
            public long TotalCalls;
            public long TotalGcAllocBytes;
            public int FrameCount;
            public double MaxSelfTimeMs;
            public int MaxSelfTimeFrame;
        }

        internal static object GetHotspots(JObject @params)
        {
            if (!HierarchyAvailable)
                return HierarchyNotAvailableError();

            if (!IsProfileLoaded(out int firstFrame, out int lastFrame))
                return NoProfileError();

            var p = new ToolParams(@params);
            int frameStart = p.GetInt("frame_start") ?? firstFrame;
            int frameEnd = p.GetInt("frame_end") ?? lastFrame;
            int topN = Math.Min(p.GetInt("top_n") ?? 20, 100);
            int threadIndex = p.GetInt("thread_index") ?? 0;
            string sortBy = p.Get("sort_by", "self_time");

            // Clamp to valid range
            frameStart = Math.Max(frameStart, firstFrame);
            frameEnd = Math.Min(frameEnd, lastFrame);
            int frameCount = frameEnd - frameStart + 1;

            if (frameCount > MaxHotspotFrames)
            {
                return new ErrorResponse(
                    $"Frame range too large ({frameCount} frames). Maximum is {MaxHotspotFrames}. "
                    + $"Try narrowing to frame_start={frameStart}, frame_end={frameStart + MaxHotspotFrames - 1}.");
            }

            // Aggregate across frames
            var aggregation = new Dictionary<string, AggregatedSample>();
            int sortColumn = ResolveSortColumn(sortBy);

            for (int fi = frameStart; fi <= frameEnd; fi++)
            {
                using (var view = GetHierarchyView(fi, threadIndex, sortColumn))
                {
                    if (view == null) continue;
                    int rootId = view.GetRootItemID();
                    AggregateItems(view, rootId, fi, aggregation, 0);
                }
            }

            // Sort and take top N
            IEnumerable<AggregatedSample> sorted;
            switch ((sortBy ?? "self_time").ToLowerInvariant())
            {
                case "total_time":
                    sorted = aggregation.Values.OrderByDescending(s => s.TotalTotalTimeMs);
                    break;
                case "calls":
                    sorted = aggregation.Values.OrderByDescending(s => s.TotalCalls);
                    break;
                case "gc_alloc":
                    sorted = aggregation.Values.OrderByDescending(s => s.TotalGcAllocBytes);
                    break;
                default: // self_time
                    sorted = aggregation.Values.OrderByDescending(s => s.TotalSelfTimeMs);
                    break;
            }

            var hotspots = sorted.Take(topN).Select(h => new
            {
                name = h.Name,
                total_self_time_ms = Math.Round(h.TotalSelfTimeMs, 3),
                avg_self_time_ms = h.FrameCount > 0 ? Math.Round(h.TotalSelfTimeMs / h.FrameCount, 3) : 0,
                max_self_time_ms = Math.Round(h.MaxSelfTimeMs, 3),
                max_self_time_frame = h.MaxSelfTimeFrame,
                total_total_time_ms = Math.Round(h.TotalTotalTimeMs, 3),
                total_calls = h.TotalCalls,
                total_gc_alloc_bytes = h.TotalGcAllocBytes,
                frame_count = h.FrameCount,
            }).ToList();

            return new SuccessResponse($"Top {hotspots.Count} hotspots across {frameCount} frames.", new
            {
                frame_range = new { start = frameStart, end = frameEnd, count = frameCount },
                sort_by = sortBy,
                hotspots,
                tip = "Use get_frame_hierarchy on specific slow frames for detailed call trees. "
                    + "Use manage_script action=read_script to read source code of hot functions.",
            });
        }

        /// <summary>
        /// Recursively aggregate all items in a frame into the shared dictionary.
        /// Max depth 20 to prevent pathological recursion.
        /// </summary>
        private static void AggregateItems(
            HierarchyFrameDataView view, int itemId, int frameIndex,
            Dictionary<string, AggregatedSample> aggregation, int depth)
        {
            if (depth > 20) return;

            string name = view.GetItemName(itemId);
            float selfTime = view.GetItemColumnDataAsSingle(itemId, Columns.SelfTime);
            float totalTime = view.GetItemColumnDataAsSingle(itemId, Columns.TotalTime);
            int calls = (int)view.GetItemColumnDataAsSingle(itemId, Columns.Calls);
            long gcAlloc = (long)view.GetItemColumnDataAsSingle(itemId, Columns.GcMemory);

            if (!aggregation.TryGetValue(name, out var sample))
            {
                sample = new AggregatedSample { Name = name };
                aggregation[name] = sample;
            }

            sample.TotalSelfTimeMs += selfTime;
            sample.TotalTotalTimeMs += totalTime;
            sample.TotalCalls += calls;
            sample.TotalGcAllocBytes += gcAlloc;
            sample.FrameCount++;

            if (selfTime > sample.MaxSelfTimeMs)
            {
                sample.MaxSelfTimeMs = selfTime;
                sample.MaxSelfTimeFrame = frameIndex;
            }

            // Recurse into children
            var children = new List<int>();
            view.GetItemChildren(itemId, children);
            foreach (int childId in children)
            {
                AggregateItems(view, childId, frameIndex, aggregation, depth + 1);
            }
        }

        #endregion
    }
}
