using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools.Input
{
    /// <summary>
    /// 输入模拟工具入口。在 Play Mode 下通过 Input System 模拟键盘、鼠标、手柄、触屏输入。
    /// 配合 manage_camera 截屏和 manage_editor 控制 Play Mode，可构建游戏交互 Agent 循环。
    /// </summary>
    [McpForUnityTool("manage_input", AutoRegister = false, Group = "core")]
    public static class ManageInput
    {
        public static async Task<object> HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            string action = p.Get("action")?.ToLowerInvariant();

            if (string.IsNullOrEmpty(action))
                return new ErrorResponse("'action' parameter is required.");

#if !MCP_HAS_INPUT_SYSTEM
            return new ErrorResponse(
                "Input simulation requires the Unity Input System package. " +
                "Install via: Window > Package Manager > Unity Registry > Input System, " +
                "or add \"com.unity.inputsystem\" to Packages/manifest.json.");
#else
            // get_devices 不要求 Play Mode
            if (action == "get_devices")
                return InputHelpers.GetDevices();

            // 其他所有操作都需要 Play Mode
            if (!EditorApplication.isPlaying)
                return new ErrorResponse(
                    "Input simulation requires Play Mode. Use manage_editor(action='play') first.");

            try
            {
                switch (action)
                {
                    // ── 键盘 ──
                    case "key_press":
                        return await InputKeyboardOps.KeyPress(@params);
                    case "key_down":
                        return InputKeyboardOps.KeyDown(@params);
                    case "key_up":
                        return InputKeyboardOps.KeyUp(@params);

                    // ── 鼠标 ──
                    case "mouse_move":
                        return InputMouseOps.MouseMove(@params);
                    case "mouse_click":
                        return await InputMouseOps.MouseClick(@params);
                    case "mouse_down":
                        return InputMouseOps.MouseDown(@params);
                    case "mouse_up":
                        return InputMouseOps.MouseUp(@params);
                    case "mouse_scroll":
                        return InputMouseOps.MouseScroll(@params);

                    // ── 手柄 ──
                    case "gamepad_button":
                        return await InputGamepadOps.GamepadButton(@params);
                    case "gamepad_stick":
                        return InputGamepadOps.GamepadStick(@params);
                    case "gamepad_trigger":
                        return InputGamepadOps.GamepadTrigger(@params);

                    // ── 触屏 ──
                    case "touch_tap":
                        return await InputTouchOps.TouchTap(@params);
                    case "touch_down":
                        return InputTouchOps.TouchDown(@params);
                    case "touch_move":
                        return InputTouchOps.TouchMove(@params);
                    case "touch_up":
                        return InputTouchOps.TouchUp(@params);

                    default:
                        return new ErrorResponse(
                            $"Unknown action: '{action}'. Valid actions: " +
                            "key_press, key_down, key_up, " +
                            "mouse_move, mouse_click, mouse_down, mouse_up, mouse_scroll, " +
                            "gamepad_button, gamepad_stick, gamepad_trigger, " +
                            "touch_tap, touch_down, touch_move, touch_up, " +
                            "get_devices.");
                }
            }
            catch (System.Exception ex)
            {
                return new ErrorResponse($"Input simulation failed: {ex.Message}");
            }
#endif
        }
    }
}
