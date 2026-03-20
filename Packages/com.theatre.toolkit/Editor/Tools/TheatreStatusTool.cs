using System.Text.Json;
using Theatre.Transport;

namespace Theatre.Editor
{
    /// <summary>
    /// Built-in dummy tool for Phase 1 validation.
    /// Returns server status information.
    /// </summary>
    public static class TheatreStatusTool
    {
        private static readonly JsonElement s_inputSchema;

        static TheatreStatusTool()
        {
            s_inputSchema = JsonDocument.Parse(@"{
                ""type"": ""object"",
                ""properties"": {},
                ""required"": []
            }").RootElement.Clone();
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "theatre_status",
                description: "Returns Theatre server status including "
                    + "version, enabled tool groups, and connection info.",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageGameObject,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = true
                }
            ));
        }

        private static string Execute(JsonElement? arguments)
        {
            // This runs on the main thread — safe to access Unity APIs
            var playMode = UnityEditor.EditorApplication.isPlaying;
            var sceneName = UnityEngine.SceneManagement.SceneManager
                .GetActiveScene().name;

            return $"{{\"status\":\"ok\""
                + $",\"version\":\"{TheatreConfig.ServerVersion}\""
                + $",\"port\":{TheatreConfig.Port}"
                + $",\"play_mode\":{(playMode ? "true" : "false")}"
                + $",\"active_scene\":\"{sceneName}\""
                + $",\"enabled_groups\":\"{TheatreConfig.EnabledGroups}\""
                + $",\"tool_count\":{TheatreServer.ToolRegistry?.Count ?? 0}"
                + "}";
        }
    }
}
