using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;

namespace Theatre.Editor
{
    /// <summary>
    /// Built-in dummy tool for Phase 1 validation.
    /// Returns server status information.
    /// </summary>
    public static class TheatreStatusTool
    {
        private static readonly JToken s_inputSchema;

        static TheatreStatusTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {},
                ""required"": []
            }");
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

        private static string Execute(JToken arguments)
        {
            var playMode = UnityEditor.EditorApplication.isPlaying;
            var sceneName = UnityEngine.SceneManagement.SceneManager
                .GetActiveScene().name;

            var response = new JObject();
            response["status"] = "ok";
            response["project"] = UnityEngine.Application.productName;
            response["company"] = UnityEngine.Application.companyName;
            response["version"] = TheatreConfig.ServerVersion;
            response["port"] = TheatreConfig.Port;
            response["play_mode"] = playMode;
            response["active_scene"] = sceneName;
            response["enabled_groups"] = TheatreConfig.EnabledGroups.ToString();
            response["tool_count"] = TheatreServer.ToolRegistry?.Count ?? 0;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }
    }
}
