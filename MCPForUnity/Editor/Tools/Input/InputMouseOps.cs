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
    /// 鼠标输入模拟：mouse_move、mouse_click、mouse_down、mouse_up、mouse_scroll。
    /// 坐标使用屏幕像素，与截屏输出一致。
    /// </summary>
    internal static class InputMouseOps
    {
#if MCP_HAS_INPUT_SYSTEM

        /// <summary>
        /// 移动鼠标到指定屏幕位置。
        /// </summary>
        internal static object MouseMove(JObject @params)
        {
            if (!InputHelpers.TryParseVector2(@params, "position", out var pos))
                return new ErrorResponse("'position' [x, y] is required for mouse_move.");

            var mouse = InputHelpers.EnsureMouse();
            InputState.Change(mouse.position, pos);

            return new SuccessResponse($"Mouse moved to ({pos.x}, {pos.y}).",
                new { position = new[] { pos.x, pos.y } });
        }

        /// <summary>
        /// 移动鼠标到指定位置并点击。支持 duration_ms 控制按住时长。
        /// </summary>
        internal static Task<object> MouseClick(JObject @params)
        {
            var p = new ToolParams(@params);
            string buttonName = p.Get("button", "left");
            int durationMs = p.GetInt("duration_ms") ?? 0;

            var mouse = InputHelpers.EnsureMouse();
            var buttonControl = InputHelpers.ResolveMouseButton(mouse, buttonName);
            if (buttonControl == null)
                return Task.FromResult<object>(new ErrorResponse(
                    $"Unknown mouse button: '{buttonName}'. Valid: left, right, middle, forward, back."));

            // 可选移动到目标位置
            if (InputHelpers.TryParseVector2(@params, "position", out var pos))
                InputState.Change(mouse.position, pos);

            int effectiveDuration = Math.Max(durationMs, 0);
            return InputHelpers.DelayedRelease(
                pressAction: () => InputHelpers.SetButtonState(buttonControl, 1.0f),
                releaseAction: () => InputHelpers.SetButtonState(buttonControl, 0.0f),
                durationMs: effectiveDuration,
                successMessage: $"Mouse '{buttonName}' button clicked.",
                successData: new { button = buttonName, duration_ms = effectiveDuration }
            );
        }

        /// <summary>
        /// 按住鼠标按钮不释放。
        /// </summary>
        internal static object MouseDown(JObject @params)
        {
            var p = new ToolParams(@params);
            string buttonName = p.Get("button", "left");

            var mouse = InputHelpers.EnsureMouse();
            var buttonControl = InputHelpers.ResolveMouseButton(mouse, buttonName);
            if (buttonControl == null)
                return new ErrorResponse(
                    $"Unknown mouse button: '{buttonName}'. Valid: left, right, middle, forward, back.");

            // 可选移动到目标位置
            if (InputHelpers.TryParseVector2(@params, "position", out var pos))
                InputState.Change(mouse.position, pos);

            InputHelpers.SetButtonState(buttonControl, 1.0f);
            return new SuccessResponse($"Mouse '{buttonName}' button pressed down. Call mouse_up to release.",
                new { button = buttonName });
        }

        /// <summary>
        /// 释放鼠标按钮。
        /// </summary>
        internal static object MouseUp(JObject @params)
        {
            var p = new ToolParams(@params);
            string buttonName = p.Get("button", "left");

            var mouse = InputHelpers.EnsureMouse();
            var buttonControl = InputHelpers.ResolveMouseButton(mouse, buttonName);
            if (buttonControl == null)
                return new ErrorResponse(
                    $"Unknown mouse button: '{buttonName}'. Valid: left, right, middle, forward, back.");

            // 可选移动到目标位置（拖拽场景：移到终点后释放）
            if (InputHelpers.TryParseVector2(@params, "position", out var pos))
                InputState.Change(mouse.position, pos);

            InputHelpers.SetButtonState(buttonControl, 0.0f);
            return new SuccessResponse($"Mouse '{buttonName}' button released.",
                new { button = buttonName });
        }

        /// <summary>
        /// 鼠标滚轮滚动。
        /// </summary>
        internal static object MouseScroll(JObject @params)
        {
            if (!InputHelpers.TryParseVector2(@params, "delta", out var delta))
                return new ErrorResponse("'delta' [x, y] is required for mouse_scroll. Typical: [0, 120] for scroll up, [0, -120] for scroll down.");

            var mouse = InputHelpers.EnsureMouse();
            InputState.Change(mouse.scroll, delta);

            return new SuccessResponse($"Mouse scrolled by ({delta.x}, {delta.y}).",
                new { delta = new[] { delta.x, delta.y } });
        }

#endif // MCP_HAS_INPUT_SYSTEM
    }
}
