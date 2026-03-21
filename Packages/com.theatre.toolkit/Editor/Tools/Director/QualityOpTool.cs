using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEditor;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: quality_op
    /// Compound tool for quality settings management.
    /// Operations: set_level, set_shadow_settings, set_rendering, list_levels.
    /// </summary>
    public static class QualityOpTool
    {
        private static readonly JToken s_inputSchema;

        static QualityOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""set_level"", ""set_shadow_settings"", ""set_rendering"", ""list_levels""],
                        ""description"": ""The quality operation to perform.""
                    },
                    ""level"": {
                        ""description"": ""Quality level index (int) or name (string)."",
                        ""oneOf"": [{""type"": ""integer""}, {""type"": ""string""}]
                    },
                    ""distance"": {
                        ""type"": ""number"",
                        ""description"": ""Shadow distance.""
                    },
                    ""resolution"": {
                        ""type"": ""string"",
                        ""enum"": [""low"", ""medium"", ""high"", ""very_high""],
                        ""description"": ""Shadow resolution.""
                    },
                    ""cascades"": {
                        ""type"": ""integer"",
                        ""enum"": [0, 1, 2, 4],
                        ""description"": ""Number of shadow cascades.""
                    },
                    ""lod_bias"": {
                        ""type"": ""number"",
                        ""description"": ""LOD bias.""
                    },
                    ""pixel_light_count"": {
                        ""type"": ""integer"",
                        ""description"": ""Pixel light count.""
                    },
                    ""texture_quality"": {
                        ""type"": ""integer"",
                        ""enum"": [0, 1, 2, 3],
                        ""description"": ""Texture quality: 0=full, 1=half, 2=quarter, 3=eighth.""
                    },
                    ""anisotropic_filtering"": {
                        ""type"": ""string"",
                        ""enum"": [""disable"", ""enable"", ""force_enable""],
                        ""description"": ""Anisotropic filtering mode.""
                    },
                    ""vsync"": {
                        ""type"": ""integer"",
                        ""enum"": [0, 1, 2],
                        ""description"": ""VSync count: 0=off, 1=every vblank, 2=every second vblank.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "quality_op",
                description: "Manage quality settings. Operations: set_level, set_shadow_settings, set_rendering, list_levels.",
                inputSchema: s_inputSchema,
                group: ToolGroup.DirectorConfig,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = false
                }
            ));
        }

        private static string Execute(JToken arguments)
        {
            if (arguments == null || arguments.Type != JTokenType.Object)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Arguments must be a JSON object with an 'operation' field",
                    "Provide {\"operation\": \"list_levels\"}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: set_level, set_shadow_settings, set_rendering, list_levels");
            }

            try
            {
                return operation switch
                {
                    "set_level"           => SetLevel(args),
                    "set_shadow_settings" => SetShadowSettings(args),
                    "set_rendering"       => SetRendering(args),
                    "list_levels"         => ListLevels(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: set_level, set_shadow_settings, set_rendering, list_levels")
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] quality_op:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"quality_op:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }

        /// <summary>Set the active quality level.</summary>
        internal static string SetLevel(JObject args)
        {
            var levelToken = args["level"];
            if (levelToken == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'level' parameter",
                    "Provide a quality level as an integer index or string name");

            int levelIndex;
            if (levelToken.Type == JTokenType.Integer)
            {
                levelIndex = levelToken.Value<int>();
            }
            else
            {
                var levelName = levelToken.Value<string>();
                var names = QualitySettings.names;
                levelIndex = -1;
                for (int i = 0; i < names.Length; i++)
                {
                    if (string.Equals(names[i], levelName, StringComparison.OrdinalIgnoreCase))
                    {
                        levelIndex = i;
                        break;
                    }
                }
                if (levelIndex < 0)
                    return ResponseHelpers.ErrorResponse(
                        "not_found",
                        $"Quality level '{levelName}' not found",
                        $"Valid levels: {string.Join(", ", names)}");
            }

            QualitySettings.SetQualityLevel(levelIndex, true);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_level";
            response["level"] = levelIndex;
            response["name"] = QualitySettings.names[levelIndex];
            return response.ToString(Formatting.None);
        }

        /// <summary>Set shadow quality settings.</summary>
        internal static string SetShadowSettings(JObject args)
        {
            var distanceToken = args["distance"];
            if (distanceToken != null)
                QualitySettings.shadowDistance = distanceToken.Value<float>();

            var resToken = args["resolution"]?.Value<string>();
            if (resToken != null)
            {
                QualitySettings.shadowResolution = resToken switch
                {
                    "low"       => ShadowResolution.Low,
                    "medium"    => ShadowResolution.Medium,
                    "high"      => ShadowResolution.High,
                    "very_high" => ShadowResolution.VeryHigh,
                    _ => QualitySettings.shadowResolution
                };
            }

            var cascadesToken = args["cascades"];
            if (cascadesToken != null)
                QualitySettings.shadowCascades = cascadesToken.Value<int>();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_shadow_settings";
            response["shadow_distance"] = QualitySettings.shadowDistance;
            response["shadow_cascades"] = QualitySettings.shadowCascades;
            return response.ToString(Formatting.None);
        }

        /// <summary>Set rendering quality settings.</summary>
        internal static string SetRendering(JObject args)
        {
            var lodBiasToken = args["lod_bias"];
            if (lodBiasToken != null)
                QualitySettings.lodBias = lodBiasToken.Value<float>();

            var pixelLightToken = args["pixel_light_count"];
            if (pixelLightToken != null)
                QualitySettings.pixelLightCount = pixelLightToken.Value<int>();

            var texQualityToken = args["texture_quality"];
            if (texQualityToken != null)
            {
                // globalTextureMipmapLimit: 0=full, 1=half, 2=quarter, 3=eighth
                QualitySettings.globalTextureMipmapLimit = texQualityToken.Value<int>();
            }

            var anisoToken = args["anisotropic_filtering"]?.Value<string>();
            if (anisoToken != null)
            {
                QualitySettings.anisotropicFiltering = anisoToken switch
                {
                    "disable"      => AnisotropicFiltering.Disable,
                    "enable"       => AnisotropicFiltering.Enable,
                    "force_enable" => AnisotropicFiltering.ForceEnable,
                    _ => QualitySettings.anisotropicFiltering
                };
            }

            var vsyncToken = args["vsync"];
            if (vsyncToken != null)
                QualitySettings.vSyncCount = vsyncToken.Value<int>();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_rendering";
            response["lod_bias"] = QualitySettings.lodBias;
            response["pixel_light_count"] = QualitySettings.pixelLightCount;
            response["vsync"] = QualitySettings.vSyncCount;
            return response.ToString(Formatting.None);
        }

        /// <summary>List all quality levels and their settings.</summary>
        internal static string ListLevels(JObject args)
        {
            var names = QualitySettings.names;
            var currentLevel = QualitySettings.GetQualityLevel();
            var levelsArray = new JArray();

            for (int i = 0; i < names.Length; i++)
            {
                var levelObj = new JObject();
                levelObj["index"] = i;
                levelObj["name"] = names[i];
                levelObj["is_active"] = (i == currentLevel);
                levelsArray.Add(levelObj);
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "list_levels";
            response["current_level"] = currentLevel;
            response["current_name"] = names.Length > 0 ? names[currentLevel] : null;
            response["levels"] = levelsArray;
            return response.ToString(Formatting.None);
        }
    }
}
