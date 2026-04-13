using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

#if MCP_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace MCPForUnity.Editor.Tools.Input
{
    /// <summary>
    /// 触屏输入模拟：touch_tap、touch_down、touch_move、touch_up。
    /// 坐标使用屏幕像素，与截屏输出一致。
    /// </summary>
    internal static class InputTouchOps
    {
#if MCP_HAS_INPUT_SYSTEM

        /// <summary>
        /// 点按屏幕：触摸开始 → 等待 durationMs → 触摸结束。
        /// </summary>
        internal static Task<object> TouchTap(JObject @params)
        {
            var p = new ToolParams(@params);
            int touchId = p.GetInt("touch_id") ?? 1;
            int durationMs = p.GetInt("duration_ms") ?? 0;

            if (!InputHelpers.TryParseVector2(@params, "position", out var pos))
                return Task.FromResult<object>(new ErrorResponse("'position' [x, y] is required for touch_tap."));

            var touchscreen = InputHelpers.EnsureTouchscreen();
            int effectiveDuration = Math.Max(durationMs, 0);

            return InputHelpers.DelayedRelease(
                pressAction: () => QueueTouchEvent(touchscreen, touchId, pos, UnityEngine.InputSystem.TouchPhase.Began),
                releaseAction: () => QueueTouchEvent(touchscreen, touchId, pos, UnityEngine.InputSystem.TouchPhase.Ended),
                durationMs: effectiveDuration,
                successMessage: $"Touch tap at ({pos.x}, {pos.y}).",
                successData: new { touch_id = touchId, position = new[] { pos.x, pos.y }, duration_ms = effectiveDuration }
            );
        }

        /// <summary>
        /// 开始触摸（不释放，用于拖拽等连续手势）。
        /// </summary>
        internal static object TouchDown(JObject @params)
        {
            var p = new ToolParams(@params);
            int touchId = p.GetInt("touch_id") ?? 1;

            if (!InputHelpers.TryParseVector2(@params, "position", out var pos))
                return new ErrorResponse("'position' [x, y] is required for touch_down.");

            var touchscreen = InputHelpers.EnsureTouchscreen();
            QueueTouchEvent(touchscreen, touchId, pos, UnityEngine.InputSystem.TouchPhase.Began);

            return new SuccessResponse($"Touch {touchId} began at ({pos.x}, {pos.y}). Call touch_move/touch_up to continue.",
                new { touch_id = touchId, position = new[] { pos.x, pos.y } });
        }

        /// <summary>
        /// 移动正在进行的触摸点。
        /// </summary>
        internal static object TouchMove(JObject @params)
        {
            var p = new ToolParams(@params);
            int touchId = p.GetInt("touch_id") ?? 1;

            if (!InputHelpers.TryParseVector2(@params, "position", out var pos))
                return new ErrorResponse("'position' [x, y] is required for touch_move.");

            var touchscreen = InputHelpers.EnsureTouchscreen();
            QueueTouchEvent(touchscreen, touchId, pos, UnityEngine.InputSystem.TouchPhase.Moved);

            return new SuccessResponse($"Touch {touchId} moved to ({pos.x}, {pos.y}).",
                new { touch_id = touchId, position = new[] { pos.x, pos.y } });
        }

        /// <summary>
        /// 结束触摸。
        /// </summary>
        internal static object TouchUp(JObject @params)
        {
            var p = new ToolParams(@params);
            int touchId = p.GetInt("touch_id") ?? 1;

            // position 可选，默认用最后位置
            InputHelpers.TryParseVector2(@params, "position", out var pos);

            var touchscreen = InputHelpers.EnsureTouchscreen();
            QueueTouchEvent(touchscreen, touchId, pos, UnityEngine.InputSystem.TouchPhase.Ended);

            return new SuccessResponse($"Touch {touchId} ended.",
                new { touch_id = touchId });
        }

        /// <summary>
        /// 向触屏设备排队一个触摸事件。
        /// </summary>
        private static void QueueTouchEvent(Touchscreen touchscreen, int touchId, Vector2 position,
            UnityEngine.InputSystem.TouchPhase phase)
        {
            // 触屏通过 QueueStateEvent 注入，因为 TouchControl 是复合状态
            var touchState = new TouchState
            {
                touchId = touchId,
                position = position,
                phase = phase,
            };

            // 写入到对应触摸槽位（touchId 从 1 开始，槽位从 0 开始）
            int slotIndex = Mathf.Clamp(touchId - 1, 0, touchscreen.touches.Count - 1);
            InputState.Change(touchscreen.touches[slotIndex], touchState);
        }

#endif // MCP_HAS_INPUT_SYSTEM
    }
}
