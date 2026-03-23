using Newtonsoft.Json.Linq;
using Theatre.Stage;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor.Tools.Actions
{
    /// <summary>
    /// action:run_menu_item — execute a Unity Editor menu item by path.
    /// Works in Edit Mode only. Some dangerous menu paths are blocked.
    /// </summary>
    internal static class ActionRunMenuItem
    {
        /// <summary>
        /// Menu paths that are blocked because they are destructive or
        /// would interfere with the editor session.
        /// </summary>
        private static readonly string[] BlockedPrefixes = new[]
        {
            "File/Quit",
            "File/Exit",
        };

        private static readonly string[] BlockedExact = new[]
        {
            "File/Save Project",
            "File/New Scene",
            "Edit/Preferences",
            "Edit/Clear All PlayerPrefs",
        };

        public static string Execute(JObject args)
        {
            var menuPath = args["menu_path"]?.Value<string>();

            if (string.IsNullOrEmpty(menuPath))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'menu_path' parameter",
                    "Provide the menu item path, e.g. 'GameObject/3D Object/Cube'");

            // Check blocklist
            foreach (var prefix in BlockedPrefixes)
            {
                if (menuPath.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    return ResponseHelpers.ErrorResponse(
                        "operation_not_supported",
                        $"Menu path '{menuPath}' is blocked for safety",
                        "This menu item could disrupt the editor session");
            }
            foreach (var exact in BlockedExact)
            {
                if (string.Equals(menuPath, exact, System.StringComparison.OrdinalIgnoreCase))
                    return ResponseHelpers.ErrorResponse(
                        "operation_not_supported",
                        $"Menu path '{menuPath}' is blocked for safety",
                        "This menu item could disrupt the editor session");
            }

#if UNITY_EDITOR
            var success = EditorApplication.ExecuteMenuItem(menuPath);

            var response = new JObject();
            if (success)
            {
                response["result"] = "ok";
                response["operation"] = "run_menu_item";
                response["menu_path"] = menuPath;
            }
            else
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Menu item '{menuPath}' not found or could not be executed",
                    "Check the menu path is correct. Use Unity's menu bar to verify the exact path.");
            }

            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "operation_not_supported",
                "run_menu_item requires the Unity Editor",
                "This operation is only available in the Unity Editor");
#endif
        }
    }
}
