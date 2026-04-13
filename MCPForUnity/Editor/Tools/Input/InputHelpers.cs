using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

#if MCP_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace MCPForUnity.Editor.Tools.Input
{
    /// <summary>
    /// 输入模拟共享工具：设备管理、键名解析、延迟释放、坐标解析。
    /// </summary>
    internal static class InputHelpers
    {
        internal const int MaxDurationMs = 10000;

#if MCP_HAS_INPUT_SYSTEM

        // ── 设备管理 ──────────────────────────────────────────

        internal static Keyboard EnsureKeyboard() =>
            InputSystem.GetDevice<Keyboard>() ?? InputSystem.AddDevice<Keyboard>();

        internal static Mouse EnsureMouse() =>
            InputSystem.GetDevice<Mouse>() ?? InputSystem.AddDevice<Mouse>();

        internal static Gamepad EnsureGamepad() =>
            InputSystem.GetDevice<Gamepad>() ?? InputSystem.AddDevice<Gamepad>();

        internal static Touchscreen EnsureTouchscreen() =>
            InputSystem.GetDevice<Touchscreen>() ?? InputSystem.AddDevice<Touchscreen>();

        // ── 键名解析 ──────────────────────────────────────────

        /// <summary>
        /// 将字符串键名解析为 KeyControl。支持 "w"、"space"、"leftShift" 等。
        /// </summary>
        internal static KeyControl ResolveKey(Keyboard keyboard, string keyName)
        {
            if (string.IsNullOrEmpty(keyName)) return null;

            // InputSystem 的 Keyboard 可以通过 [] 索引访问，路径格式为小写
            try
            {
                var control = keyboard.TryGetChildControl(keyName);
                if (control is KeyControl kc) return kc;
            }
            catch { /* ignore */ }

            // 尝试常用别名映射
            var mapped = MapKeyAlias(keyName);
            if (mapped != null)
            {
                try
                {
                    var control = keyboard.TryGetChildControl(mapped);
                    if (control is KeyControl kc) return kc;
                }
                catch { /* ignore */ }
            }

            return null;
        }

        /// <summary>
        /// 常用键名别名映射（用户可能输入的名称 → InputSystem 路径名）。
        /// </summary>
        private static string MapKeyAlias(string keyName)
        {
            switch (keyName.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    return "leftCtrl";
                case "shift":
                    return "leftShift";
                case "alt":
                    return "leftAlt";
                case "esc":
                    return "escape";
                case "return":
                    return "enter";
                case "up":
                    return "upArrow";
                case "down":
                    return "downArrow";
                case "left":
                    return "leftArrow";
                case "right":
                    return "rightArrow";
                case "del":
                    return "delete";
                case "ins":
                    return "insert";
                default:
                    return null;
            }
        }

        /// <summary>
        /// 将字符串按钮名解析为鼠标 ButtonControl。
        /// </summary>
        internal static ButtonControl ResolveMouseButton(Mouse mouse, string buttonName)
        {
            switch (buttonName?.ToLowerInvariant())
            {
                case "left":
                case "leftbutton":
                case "0":
                    return mouse.leftButton;
                case "right":
                case "rightbutton":
                case "1":
                    return mouse.rightButton;
                case "middle":
                case "middlebutton":
                case "2":
                    return mouse.middleButton;
                case "forward":
                case "forwardbutton":
                    return mouse.forwardButton;
                case "back":
                case "backbutton":
                    return mouse.backButton;
                default:
                    return null;
            }
        }

        /// <summary>
        /// 将字符串按钮名解析为手柄 ButtonControl。
        /// </summary>
        internal static ButtonControl ResolveGamepadButton(Gamepad gamepad, string buttonName)
        {
            switch (buttonName?.ToLowerInvariant())
            {
                case "south":
                case "a":
                case "cross":
                    return gamepad.buttonSouth;
                case "north":
                case "y":
                case "triangle":
                    return gamepad.buttonNorth;
                case "east":
                case "b":
                case "circle":
                    return gamepad.buttonEast;
                case "west":
                case "x":
                case "square":
                    return gamepad.buttonWest;
                case "start":
                    return gamepad.startButton;
                case "select":
                    return gamepad.selectButton;
                case "leftshoulder":
                case "l1":
                    return gamepad.leftShoulder;
                case "rightshoulder":
                case "r1":
                    return gamepad.rightShoulder;
                case "leftstickbutton":
                case "l3":
                    return gamepad.leftStickButton;
                case "rightstickbutton":
                case "r3":
                    return gamepad.rightStickButton;
                case "lefttrigger":
                case "l2":
                    return gamepad.leftTrigger;
                case "righttrigger":
                case "r2":
                    return gamepad.rightTrigger;
                case "dpadup":
                    return gamepad.dpad.up;
                case "dpaddown":
                    return gamepad.dpad.down;
                case "dpadleft":
                    return gamepad.dpad.left;
                case "dpadright":
                    return gamepad.dpad.right;
                default:
                    return null;
            }
        }

        // ── 延迟释放 ──────────────────────────────────────────

        /// <summary>
        /// 按下 → 等待 durationMs → 释放，返回异步结果。
        /// durationMs &lt;= 0 时在下一帧释放。
        /// </summary>
        internal static Task<object> DelayedRelease(
            Action pressAction, Action releaseAction, int durationMs,
            string successMessage, object successData = null)
        {
            // 限制最大持续时间
            if (durationMs > MaxDurationMs)
                durationMs = MaxDurationMs;

            pressAction();

            if (durationMs <= 0)
            {
                // 下一帧释放
                var tcs = new TaskCompletionSource<object>();
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        releaseAction();
                        tcs.TrySetResult(new SuccessResponse(successMessage, successData));
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetResult(new ErrorResponse($"Release failed: {ex.Message}"));
                    }
                };
                return tcs.Task;
            }

            // 定时释放
            var tcsDelayed = new TaskCompletionSource<object>();
            var startTime = DateTime.UtcNow;
            var timeout = TimeSpan.FromMilliseconds(durationMs);

            void Tick()
            {
                if (DateTime.UtcNow - startTime < timeout) return;

                EditorApplication.update -= Tick;
                try
                {
                    releaseAction();
                    tcsDelayed.TrySetResult(new SuccessResponse(successMessage, successData));
                }
                catch (Exception ex)
                {
                    tcsDelayed.TrySetResult(new ErrorResponse($"Release failed: {ex.Message}"));
                }
            }

            EditorApplication.update += Tick;
            return tcsDelayed.Task;
        }

        // ── 设备列表 ──────────────────────────────────────────

        internal static object GetDevices()
        {
            var devices = InputSystem.devices;
            var list = new List<object>();
            foreach (var device in devices)
            {
                list.Add(new
                {
                    name = device.name,
                    type = device.GetType().Name,
                    deviceId = device.deviceId,
                    enabled = device.enabled,
                    path = device.path,
                });
            }
            return new SuccessResponse("Input devices listed.", new { devices = list, count = list.Count });
        }

        // ── 坐标解析 ──────────────────────────────────────────

        /// <summary>
        /// 从参数中解析 [x, y] 坐标。支持数组和对象格式。
        /// </summary>
        internal static bool TryParseVector2(JObject @params, string key, out Vector2 value)
        {
            value = Vector2.zero;

            var p = new ToolParams(@params);
            var token = p.GetRaw(key);
            if (token == null) return false;

            // [x, y] 数组格式
            if (token is JArray arr && arr.Count >= 2)
            {
                value = new Vector2(arr[0].Value<float>(), arr[1].Value<float>());
                return true;
            }

            // {x: .., y: ..} 对象格式
            if (token is JObject obj)
            {
                value = new Vector2(
                    obj["x"]?.Value<float>() ?? 0f,
                    obj["y"]?.Value<float>() ?? 0f);
                return true;
            }

            return false;
        }

#endif // MCP_HAS_INPUT_SYSTEM
    }
}
