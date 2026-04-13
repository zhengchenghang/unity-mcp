using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Settings
{
    /// <summary>
    /// Unified tool for reading/writing Unity ProjectSettings by category.
    /// New categories are added by implementing ICategoryHandler and registering in the dictionary.
    /// </summary>
    [McpForUnityTool("manage_project_settings", AutoRegister = false, Group = "core")]
    public static class ManageProjectSettings
    {
        private static readonly Dictionary<string, ICategoryHandler> Categories
            = new Dictionary<string, ICategoryHandler>(StringComparer.OrdinalIgnoreCase)
        {
            { "version_control", new VersionControlSettingsOps() }
        };

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            string action = p.Get("action")?.ToLowerInvariant();

            if (string.IsNullOrEmpty(action))
                return new ErrorResponse("'action' parameter is required.");

            try
            {
                switch (action)
                {
                    case "list_categories":
                        return ListCategories();

                    case "list_properties":
                    case "get_settings":
                    case "set_settings":
                    case "discover":
                    {
                        string category = p.Get("category");
                        if (string.IsNullOrEmpty(category))
                            return new ErrorResponse(
                                $"'category' parameter is required for '{action}'. " +
                                $"Valid categories: {string.Join(", ", Categories.Keys)}");

                        if (!Categories.TryGetValue(category, out var handler))
                            return new ErrorResponse(
                                $"Unknown category '{category}'. " +
                                $"Valid categories: {string.Join(", ", Categories.Keys)}");

                        if (action == "list_properties")
                            return handler.ListProperties();
                        if (action == "get_settings")
                            return handler.GetSettings(@params);
                        if (action == "discover")
                            return handler.Discover();
                        // set_settings
                        return handler.SetSettings(@params);
                    }

                    default:
                        return new ErrorResponse(
                            $"Unknown action '{action}'. " +
                            "Valid actions: list_categories, list_properties, get_settings, set_settings, discover");
                }
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error in manage_project_settings/{action}: {e.Message}");
            }
        }

        private static object ListCategories()
        {
            var list = Categories.Select(kvp => new
            {
                name = kvp.Key,
                description = kvp.Value.Description,
                property_count = kvp.Value.PropertyCount
            }).ToArray();

            return new SuccessResponse("Available settings categories.", new { categories = list });
        }
    }

    /// <summary>
    /// Interface for a settings category handler.
    /// Implement this to add a new ProjectSettings category.
    /// </summary>
    internal interface ICategoryHandler
    {
        string Description { get; }
        int PropertyCount { get; }
        object ListProperties();
        object GetSettings(JObject @params);
        object SetSettings(JObject @params);
        /// <summary>
        /// 枚举该分类下所有可发现的底层属性，用于调试和属性路径发现。
        /// </summary>
        object Discover();
    }
}
