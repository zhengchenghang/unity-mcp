using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;

#if MCP_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace MCPForUnity.Editor.Tools.Input
{
    /// <summary>
    /// 键盘输入模拟：key_press（按下并释放）、key_down（按住）、key_up（释放）。
    /// </summary>
    internal static class InputKeyboardOps
    {
#if MCP_HAS_INPUT_SYSTEM

        /// <summary>
        /// 按下键并在 durationMs 后自动释放。durationMs=0 表示下一帧释放。
        /// </summary>
        internal static Task<object> KeyPress(JObject @params)
        {
            var p = new ToolParams(@params);
            var keyResult = p.GetRequired("key");
            if (!keyResult.IsSuccess)
                return Task.FromResult<object>(new ErrorResponse(keyResult.ErrorMessage));

            int durationMs = p.GetInt("duration_ms") ?? 0;
            string keyName = keyResult.Value;

            var keyboard = InputHelpers.EnsureKeyboard();
            var keyControl = InputHelpers.ResolveKey(keyboard, keyName);
            if (keyControl == null)
                return Task.FromResult<object>(new ErrorResponse(
                    $"Unknown key: '{keyName}'. Examples: w, a, s, d, space, leftShift, escape, enter, tab, f1-f12."));

            int effectiveDuration = Math.Max(durationMs, 0);
            return InputHelpers.DelayedRelease(
                pressAction: () => InputHelpers.SetButtonState(keyControl, 1.0f),
                releaseAction: () => InputHelpers.SetButtonState(keyControl, 0.0f),
                durationMs: effectiveDuration,
                successMessage: $"Key '{keyName}' pressed and released.",
                successData: new { key = keyName, duration_ms = effectiveDuration }
            );
        }

        /// <summary>
        /// 按住键不释放（需要后续调用 key_up 释放）。
        /// </summary>
        internal static object KeyDown(JObject @params)
        {
            var p = new ToolParams(@params);
            var keyResult = p.GetRequired("key");
            if (!keyResult.IsSuccess)
                return new ErrorResponse(keyResult.ErrorMessage);

            string keyName = keyResult.Value;
            var keyboard = InputHelpers.EnsureKeyboard();
            var keyControl = InputHelpers.ResolveKey(keyboard, keyName);
            if (keyControl == null)
                return new ErrorResponse(
                    $"Unknown key: '{keyName}'. Examples: w, a, s, d, space, leftShift, escape, enter, tab, f1-f12.");

            InputHelpers.SetButtonState(keyControl, 1.0f);
            return new SuccessResponse($"Key '{keyName}' pressed down (held). Call key_up to release.",
                new { key = keyName });
        }

        /// <summary>
        /// 释放之前按住的键。
        /// </summary>
        internal static object KeyUp(JObject @params)
        {
            var p = new ToolParams(@params);
            var keyResult = p.GetRequired("key");
            if (!keyResult.IsSuccess)
                return new ErrorResponse(keyResult.ErrorMessage);

            string keyName = keyResult.Value;
            var keyboard = InputHelpers.EnsureKeyboard();
            var keyControl = InputHelpers.ResolveKey(keyboard, keyName);
            if (keyControl == null)
                return new ErrorResponse(
                    $"Unknown key: '{keyName}'. Examples: w, a, s, d, space, leftShift, escape, enter, tab, f1-f12.");

            InputHelpers.SetButtonState(keyControl, 0.0f);
            return new SuccessResponse($"Key '{keyName}' released.",
                new { key = keyName });
        }

#endif // MCP_HAS_INPUT_SYSTEM
    }
}
