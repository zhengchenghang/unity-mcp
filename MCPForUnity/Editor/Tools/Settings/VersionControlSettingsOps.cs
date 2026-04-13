using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Settings
{
    /// <summary>
    /// Reads and writes Version Control related ProjectSettings.
    /// Properties: mode (VCS mode), serialization_mode (asset serialization).
    /// </summary>
    internal class VersionControlSettingsOps : ICategoryHandler
    {
        public string Description => "Version control mode and asset serialization settings.";
        public int PropertyCount => 2;

        private static readonly string[] ValidModes =
        {
            "Visible Meta Files", "Hidden Meta Files", "Perforce", "PlasticSCM"
        };

        private static readonly HashSet<string> ValidKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mode", "serialization_mode", "serializationmode"
        };

        public object ListProperties()
        {
            return new SuccessResponse("Version control properties.", new
            {
                category = "version_control",
                properties = new object[]
                {
                    new
                    {
                        name = "mode",
                        type = "string",
                        description = "Version control mode",
                        valid_values = ValidModes,
                        current_value = EditorSettings.externalVersionControl
                    },
                    new
                    {
                        name = "serialization_mode",
                        type = "enum",
                        description = "Asset serialization mode",
                        valid_values = new[] { "ForceText", "ForceBinary", "Mixed" },
                        current_value = EditorSettings.serializationMode.ToString()
                    }
                }
            });
        }

        public object GetSettings(JObject @params)
        {
            return new SuccessResponse("Version control settings retrieved.", new
            {
                mode = EditorSettings.externalVersionControl,
                serialization_mode = EditorSettings.serializationMode.ToString()
            });
        }

        public object SetSettings(JObject @params)
        {
            var p = new ToolParams(@params);
            var settings = p.GetRaw("settings") as JObject;

            if (settings == null || settings.Count == 0)
                return new ErrorResponse(
                    "'settings' parameter is required and must be a non-empty object.");

            // 先验证所有 key，不做任何修改
            var unknown = new List<string>();
            foreach (var prop in settings.Properties())
            {
                if (!ValidKeys.Contains(prop.Name))
                    unknown.Add(prop.Name);
            }
            if (unknown.Count > 0)
                return new ErrorResponse(
                    $"Unknown version control setting(s): {string.Join(", ", unknown)}. " +
                    $"Valid: mode, serialization_mode");

            var changed = new List<string>();

            foreach (var prop in settings.Properties())
            {
                string key = prop.Name.ToLowerInvariant();
                switch (key)
                {
                    case "mode":
                    {
                        string newMode = prop.Value.ToString();
                        if (!ValidModes.Contains(newMode))
                            return new ErrorResponse(
                                $"Invalid VCS mode: '{newMode}'. " +
                                $"Valid: {string.Join(", ", ValidModes)}");

                        EditorSettings.externalVersionControl = newMode;
                        changed.Add("mode");
                        break;
                    }

                    case "serialization_mode":
                    case "serializationmode":
                    {
                        string modeStr = prop.Value.ToString();
                        if (Enum.TryParse<SerializationMode>(modeStr, true, out var sm))
                        {
                            EditorSettings.serializationMode = sm;
                            changed.Add("serialization_mode");
                        }
                        else
                        {
                            return new ErrorResponse(
                                $"Invalid serialization mode: '{modeStr}'. " +
                                "Valid: ForceText, ForceBinary, Mixed");
                        }
                        break;
                    }
                }
            }

            MarkEditorSettingsDirty();

            return new SuccessResponse(
                $"Updated {changed.Count} version control setting(s).",
                new
                {
                    changed,
                    current = new
                    {
                        mode = EditorSettings.externalVersionControl,
                        serialization_mode = EditorSettings.serializationMode.ToString()
                    }
                });
        }

        /// <summary>
        /// 枚举 VersionControlSettings.asset 和 EditorUserSettings 中的所有属性，
        /// 用于发现 Perforce 连接参数的实际存储位置和字段名。
        /// </summary>
        public object Discover()
        {
            var result = new Dictionary<string, object>();

            // 1. 枚举 VersionControlSettings.asset 的 SerializedProperty
            var vcAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/VersionControlSettings.asset");
            if (vcAssets != null && vcAssets.Length > 0)
            {
                var props = EnumerateSerializedProperties(new SerializedObject(vcAssets[0]));
                result["vc_settings_asset"] = new { found = true, properties = props };
            }
            else
            {
                result["vc_settings_asset"] = new { found = false };
            }

            // 2. 枚举 EditorSettings.asset 的版本控制相关属性
            var editorAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/EditorSettings.asset");
            if (editorAssets != null && editorAssets.Length > 0)
            {
                var props = EnumerateSerializedProperties(
                    new SerializedObject(editorAssets[0]),
                    pathFilter: p => {
                        string lower = p.ToLowerInvariant();
                        return lower.Contains("version") || lower.Contains("vc") ||
                               lower.Contains("perforce") || lower.Contains("p4") ||
                               lower.Contains("collab") || lower.Contains("plastic") ||
                               lower.Contains("serializ") || lower.Contains("meta");
                    });
                result["editor_settings_asset"] = new { found = true, properties = props };
            }

            // 3. 检查 EditorUserSettings 已知 config key
            var userSettingsResults = new Dictionary<string, string>();
            string[] knownKeys =
            {
                "vcPerforceServer", "vcPerforceUsername", "vcPerforceWorkspace",
                "vcPerforcePassword", "vcPerforceTimeout", "vcPerforceLogLevel",
                "vcSharedLogLevel",
                // 可能的变体
                "vcPerforceHost", "vcPerforcePort", "vcPerforceClient",
                "vcPerforce4Server", "vcPerforce4Username", "vcPerforce4Workspace"
            };
            foreach (var key in knownKeys)
            {
                try
                {
                    string val = EditorUserSettings.GetConfigValue(key);
                    // 密码字段只报告存在性，不暴露值
                    if (key.ToLowerInvariant().Contains("password") || key.ToLowerInvariant().Contains("passwd"))
                        userSettingsResults[key] = val != null ? "(exists, hidden)" : "(null)";
                    else
                        userSettingsResults[key] = val ?? "(null)";
                }
                catch (System.Exception)
                {
                    userSettingsResults[key] = "(error)";
                }
            }
            result["editor_user_settings"] = userSettingsResults;

            // 4. 当前基础状态
            result["current_mode"] = EditorSettings.externalVersionControl;
            result["serialization_mode"] = EditorSettings.serializationMode.ToString();

            return new SuccessResponse(
                "Version control settings discovery complete. Use this data to identify Perforce property paths.",
                result);
        }

        /// <summary>
        /// 枚举 SerializedObject 的所有属性，跳过元数据和已读值类型的子节点（避免 String 展开为字符数组）。
        /// pathFilter 为 null 时收集全部属性，非 null 时仅收集匹配的属性。
        /// </summary>
        private static List<object> EnumerateSerializedProperties(
            SerializedObject so, Func<string, bool> pathFilter = null)
        {
            var entries = new List<object>();
            var iter = so.GetIterator();
            bool enterChildren = true;

            while (iter.Next(enterChildren))
            {
                // 默认进入子节点，后面按需跳过
                enterChildren = true;

                // 跳过内置元数据
                if (iter.propertyPath == "m_ObjectHideFlags" || iter.propertyPath == "m_Script")
                {
                    enterChildren = false;
                    continue;
                }

                // 路径过滤（用于只收集 VC 相关属性）
                if (pathFilter != null && !pathFilter(iter.propertyPath))
                    continue;

                var entry = new Dictionary<string, object>
                {
                    { "path", iter.propertyPath },
                    { "type", iter.propertyType.ToString() },
                    { "depth", iter.depth }
                };

                // 读取值；已读到具体值的叶子类型不需要再展开子节点
                switch (iter.propertyType)
                {
                    case SerializedPropertyType.String:
                        entry["value"] = iter.stringValue;
                        enterChildren = false; // 跳过字符数组展开
                        break;
                    case SerializedPropertyType.Integer:
                        entry["value"] = iter.intValue;
                        enterChildren = false;
                        break;
                    case SerializedPropertyType.Boolean:
                        entry["value"] = iter.boolValue;
                        enterChildren = false;
                        break;
                    case SerializedPropertyType.Float:
                        entry["value"] = iter.floatValue;
                        enterChildren = false;
                        break;
                    case SerializedPropertyType.Enum:
                        entry["value"] = iter.enumValueIndex;
                        entry["enum_names"] = iter.enumDisplayNames;
                        enterChildren = false;
                        break;
                }

                entries.Add(entry);
            }

            return entries;
        }

        private static void MarkEditorSettingsDirty()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/EditorSettings.asset");
            if (assets != null && assets.Length > 0)
            {
                EditorUtility.SetDirty(assets[0]);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
