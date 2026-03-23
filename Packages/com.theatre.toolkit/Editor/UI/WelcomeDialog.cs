using Theatre;
using UnityEditor;
using UnityEngine;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// First-run welcome dialog shown when Theatre is installed.
    /// Shows the server URL and .mcp.json configuration snippet.
    /// </summary>
    public class WelcomeDialog : EditorWindow
    {
        private const string ShownKey = "Theatre_WelcomeShown";

        [InitializeOnLoadMethod]
        private static void CheckFirstRun()
        {
            if (!EditorPrefs.GetBool(ShownKey, false))
                EditorApplication.delayCall += Show;
        }

        /// <summary>Show the welcome dialog.</summary>
        public static void Show()
        {
            var window = GetWindow<WelcomeDialog>(true, "Welcome to Theatre");
            window.minSize = window.maxSize = new Vector2(450, 350);
        }

        private void OnGUI()
        {
            GUILayout.Label("Theatre is running!", EditorStyles.boldLabel);
            GUILayout.Space(10);
            GUILayout.Label($"Server URL: http://localhost:{TheatreConfig.Port}");
            GUILayout.Space(10);
            GUILayout.Label("Add this to your agent's .mcp.json:", EditorStyles.miniLabel);

            var mcpJson =
                "{\n  \"mcpServers\": {\n    \"theatre\": {\n      \"type\": \"http\",\n      \"url\": \"http://localhost:"
                + TheatreConfig.Port
                + "/mcp\"\n    }\n  }\n}";

            EditorGUILayout.TextArea(mcpJson, GUILayout.Height(100));

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy to Clipboard"))
                EditorGUIUtility.systemCopyBuffer = mcpJson;
            if (GUILayout.Button("Open Theatre Panel"))
            {
                TheatreWindow.ShowWindow();
                Close();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            if (GUILayout.Button("Don't show again"))
            {
                EditorPrefs.SetBool(ShownKey, true);
                Close();
            }
        }
    }
}
