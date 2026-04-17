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
    /// 手柄输入模拟：gamepad_button、gamepad_stick、gamepad_trigger。
    /// </summary>
    internal static class InputGamepadOps
    {
#if MCP_HAS_INPUT_SYSTEM

        /// <summary>
        /// 按下手柄按钮并在 durationMs 后释放。
        /// </summary>
        internal static Task<object> GamepadButton(JObject @params)
        {
            var p = new ToolParams(@params);
            var buttonResult = p.GetRequired("button");
            if (!buttonResult.IsSuccess)
                return Task.FromResult<object>(new ErrorResponse(buttonResult.ErrorMessage));

            int durationMs = p.GetInt("duration_ms") ?? 0;
            string buttonName = buttonResult.Value;

            var gamepad = InputHelpers.EnsureGamepad();
            var buttonControl = InputHelpers.ResolveGamepadButton(gamepad, buttonName);
            if (buttonControl == null)
                return Task.FromResult<object>(new ErrorResponse(
                    $"Unknown gamepad button: '{buttonName}'. Valid: south/a, north/y, east/b, west/x, " +
                    "start, select, l1, r1, l2, r2, l3, r3, dpadup, dpaddown, dpadleft, dpadright."));

            int effectiveDuration = Math.Max(durationMs, 0);
            return InputHelpers.DelayedRelease(
                pressAction: () => InputHelpers.SetButtonState(buttonControl, 1.0f),
                releaseAction: () => InputHelpers.SetButtonState(buttonControl, 0.0f),
                durationMs: effectiveDuration,
                successMessage: $"Gamepad button '{buttonName}' pressed and released.",
                successData: new { button = buttonName, duration_ms = effectiveDuration }
            );
        }

        /// <summary>
        /// 设置手柄摇杆位置。value 范围 [-1, 1]。
        /// </summary>
        internal static object GamepadStick(JObject @params)
        {
            var p = new ToolParams(@params);
            string stickName = p.Get("stick", "left");

            if (!InputHelpers.TryParseVector2(@params, "value", out var stickValue))
                return new ErrorResponse("'value' [x, y] is required for gamepad_stick. Range: [-1, 1] per axis.");

            // 钳制范围
            stickValue.x = Mathf.Clamp(stickValue.x, -1f, 1f);
            stickValue.y = Mathf.Clamp(stickValue.y, -1f, 1f);

            var gamepad = InputHelpers.EnsureGamepad();

            switch (stickName?.ToLowerInvariant())
            {
                case "left":
                    InputState.Change(gamepad.leftStick, stickValue);
                    break;
                case "right":
                    InputState.Change(gamepad.rightStick, stickValue);
                    break;
                default:
                    return new ErrorResponse($"Unknown stick: '{stickName}'. Valid: left, right.");
            }

            return new SuccessResponse($"Gamepad {stickName} stick set to ({stickValue.x:F2}, {stickValue.y:F2}).",
                new { stick = stickName, value = new[] { stickValue.x, stickValue.y } });
        }

        /// <summary>
        /// 设置手柄扳机值。value 范围 [0, 1]。
        /// </summary>
        internal static object GamepadTrigger(JObject @params)
        {
            var p = new ToolParams(@params);
            string triggerName = p.Get("trigger", "left");
            float triggerValue = p.GetFloat("value") ?? 0f;

            // 钳制范围
            triggerValue = Mathf.Clamp01(triggerValue);

            var gamepad = InputHelpers.EnsureGamepad();

            switch (triggerName?.ToLowerInvariant())
            {
                case "left":
                    InputState.Change(gamepad.leftTrigger, triggerValue);
                    break;
                case "right":
                    InputState.Change(gamepad.rightTrigger, triggerValue);
                    break;
                default:
                    return new ErrorResponse($"Unknown trigger: '{triggerName}'. Valid: left, right.");
            }

            return new SuccessResponse($"Gamepad {triggerName} trigger set to {triggerValue:F2}.",
                new { trigger = triggerName, value = triggerValue });
        }

#endif // MCP_HAS_INPUT_SYSTEM
    }
}
