using UnityEditor;

namespace MCPForUnity.Editor.CompatCore
{
    /// <summary>
    /// Shared editor play mode capability that can be consumed by both
    /// MCPForUnity JObject adapters and UnityMCPBridge typed adapters.
    /// </summary>
    public static class PlayModeCapability
    {
        public sealed class PlayModeState
        {
            public bool IsPlaying { get; set; }
            public bool IsPaused { get; set; }
        }

        public sealed class PlayModeOperationResult
        {
            public bool Success { get; set; }
            public string Code { get; set; }
            public string Message { get; set; }
            public PlayModeState State { get; set; }
        }

        public static PlayModeState GetStatus()
        {
            return new PlayModeState
            {
                IsPlaying = EditorApplication.isPlaying,
                IsPaused = EditorApplication.isPaused,
            };
        }

        public static PlayModeOperationResult RequestPlay()
        {
            if (EditorApplication.isPlaying)
            {
                return BuildResult(true, "already_playing", "Already in play mode.");
            }

            if (EditorApplication.isCompiling)
            {
                return BuildResult(false, "compiling", "Cannot enter play mode while compiling.");
            }

            EditorApplication.isPlaying = true;
            return BuildResult(true, "play_requested", "Entered play mode.");
        }

        public static PlayModeOperationResult RequestStop()
        {
            if (!EditorApplication.isPlaying)
            {
                return BuildResult(true, "already_stopped", "Already stopped (not in play mode).");
            }

            EditorApplication.isPlaying = false;
            return BuildResult(true, "stop_requested", "Exited play mode.");
        }

        public static PlayModeOperationResult RequestPause(bool? paused = null)
        {
            if (!EditorApplication.isPlaying)
            {
                return BuildResult(false, "not_in_play_mode", "Cannot pause/resume: Not in play mode.");
            }

            EditorApplication.isPaused = paused ?? !EditorApplication.isPaused;
            return BuildResult(true, EditorApplication.isPaused ? "paused" : "resumed",
                EditorApplication.isPaused ? "Game paused." : "Game resumed.");
        }

        static PlayModeOperationResult BuildResult(bool success, string code, string message)
        {
            return new PlayModeOperationResult
            {
                Success = success,
                Code = code,
                Message = message,
                State = GetStatus(),
            };
        }
    }
}
