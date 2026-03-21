using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Transport;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: lighting_op
    /// Compound tool for scene lighting configuration.
    /// Operations: set_ambient, set_fog, set_skybox, add_light_probe_group,
    /// add_reflection_probe, bake.
    /// </summary>
    public static class LightingOpTool
    {
        private static readonly JToken s_inputSchema;

        static LightingOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""set_ambient"", ""set_fog"", ""set_skybox"", ""add_light_probe_group"", ""add_reflection_probe"", ""bake""],
                        ""description"": ""The lighting operation to perform.""
                    },
                    ""mode"": {
                        ""type"": ""string"",
                        ""description"": ""Ambient mode ('color'/'gradient'/'skybox') or fog mode ('linear'/'exponential'/'exponential_squared').""
                    },
                    ""color"": {
                        ""type"": ""array"",
                        ""description"": ""RGBA color as [r, g, b, a].""
                    },
                    ""sky_color"": {
                        ""type"": ""array"",
                        ""description"": ""Gradient sky color [r, g, b, a].""
                    },
                    ""equator_color"": {
                        ""type"": ""array"",
                        ""description"": ""Gradient equator color [r, g, b, a].""
                    },
                    ""ground_color"": {
                        ""type"": ""array"",
                        ""description"": ""Gradient ground color [r, g, b, a].""
                    },
                    ""intensity"": {
                        ""type"": ""number"",
                        ""description"": ""Ambient intensity.""
                    },
                    ""enabled"": {
                        ""type"": ""boolean"",
                        ""description"": ""Enable or disable fog.""
                    },
                    ""density"": {
                        ""type"": ""number"",
                        ""description"": ""Fog density (exponential modes).""
                    },
                    ""start_distance"": {
                        ""type"": ""number"",
                        ""description"": ""Fog start distance (linear mode).""
                    },
                    ""end_distance"": {
                        ""type"": ""number"",
                        ""description"": ""Fog end distance (linear mode).""
                    },
                    ""material"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for skybox material.""
                    },
                    ""path"": {
                        ""type"": ""string"",
                        ""description"": ""Hierarchy path for attaching a light probe group.""
                    },
                    ""positions"": {
                        ""type"": ""array"",
                        ""description"": ""Array of [x, y, z] probe positions for light probe group.""
                    },
                    ""name"": {
                        ""type"": ""string"",
                        ""description"": ""Name for the created GameObject.""
                    },
                    ""position"": {
                        ""type"": ""array"",
                        ""description"": ""Position [x, y, z] for reflection probe.""
                    },
                    ""size"": {
                        ""type"": ""array"",
                        ""description"": ""Size [x, y, z] for reflection probe box.""
                    },
                    ""resolution"": {
                        ""type"": ""integer"",
                        ""description"": ""Resolution for reflection probe cubemap.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "lighting_op",
                description: "Configure scene lighting. Operations: set_ambient, set_fog, set_skybox, "
                    + "add_light_probe_group, add_reflection_probe, bake.",
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
                    "Provide {\"operation\": \"set_ambient\", \"mode\": \"color\", \"color\": [1,1,1,1]}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: set_ambient, set_fog, set_skybox, add_light_probe_group, add_reflection_probe, bake");
            }

            try
            {
                return operation switch
                {
                    "set_ambient"           => SetAmbient(args),
                    "set_fog"               => SetFog(args),
                    "set_skybox"            => SetSkybox(args),
                    "add_light_probe_group" => AddLightProbeGroup(args),
                    "add_reflection_probe"  => AddReflectionProbe(args),
                    "bake"                  => Bake(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: set_ambient, set_fog, set_skybox, add_light_probe_group, add_reflection_probe, bake")
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] lighting_op:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"lighting_op:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }

        /// <summary>Configure ambient lighting.</summary>
        internal static string SetAmbient(JObject args)
        {
            var modeStr = args["mode"]?.Value<string>();
            if (modeStr != null)
            {
                RenderSettings.ambientMode = modeStr switch
                {
                    "color"    => AmbientMode.Flat,
                    "gradient" => AmbientMode.Trilight,
                    "skybox"   => AmbientMode.Skybox,
                    _ => RenderSettings.ambientMode
                };
            }

            var colorToken = args["color"] as JArray;
            if (colorToken != null && colorToken.Count >= 4)
                RenderSettings.ambientLight = new Color(
                    colorToken[0].Value<float>(),
                    colorToken[1].Value<float>(),
                    colorToken[2].Value<float>(),
                    colorToken[3].Value<float>());

            var skyToken = args["sky_color"] as JArray;
            if (skyToken != null && skyToken.Count >= 4)
                RenderSettings.ambientSkyColor = new Color(
                    skyToken[0].Value<float>(),
                    skyToken[1].Value<float>(),
                    skyToken[2].Value<float>(),
                    skyToken[3].Value<float>());

            var equatorToken = args["equator_color"] as JArray;
            if (equatorToken != null && equatorToken.Count >= 4)
                RenderSettings.ambientEquatorColor = new Color(
                    equatorToken[0].Value<float>(),
                    equatorToken[1].Value<float>(),
                    equatorToken[2].Value<float>(),
                    equatorToken[3].Value<float>());

            var groundToken = args["ground_color"] as JArray;
            if (groundToken != null && groundToken.Count >= 4)
                RenderSettings.ambientGroundColor = new Color(
                    groundToken[0].Value<float>(),
                    groundToken[1].Value<float>(),
                    groundToken[2].Value<float>(),
                    groundToken[3].Value<float>());

            var intensityToken = args["intensity"];
            if (intensityToken != null)
                RenderSettings.ambientIntensity = intensityToken.Value<float>();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_ambient";
            response["mode"] = RenderSettings.ambientMode.ToString().ToLowerInvariant();
            return response.ToString(Formatting.None);
        }

        /// <summary>Configure fog settings.</summary>
        internal static string SetFog(JObject args)
        {
            var enabledToken = args["enabled"];
            if (enabledToken != null)
                RenderSettings.fog = enabledToken.Value<bool>();

            var modeStr = args["mode"]?.Value<string>();
            if (modeStr != null)
            {
                RenderSettings.fogMode = modeStr switch
                {
                    "linear"               => FogMode.Linear,
                    "exponential"          => FogMode.Exponential,
                    "exponential_squared"  => FogMode.ExponentialSquared,
                    _ => RenderSettings.fogMode
                };
            }

            var colorToken = args["color"] as JArray;
            if (colorToken != null && colorToken.Count >= 4)
                RenderSettings.fogColor = new Color(
                    colorToken[0].Value<float>(),
                    colorToken[1].Value<float>(),
                    colorToken[2].Value<float>(),
                    colorToken[3].Value<float>());

            var densityToken = args["density"];
            if (densityToken != null)
                RenderSettings.fogDensity = densityToken.Value<float>();

            var startToken = args["start_distance"];
            if (startToken != null)
                RenderSettings.fogStartDistance = startToken.Value<float>();

            var endToken = args["end_distance"];
            if (endToken != null)
                RenderSettings.fogEndDistance = endToken.Value<float>();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_fog";
            response["fog_enabled"] = RenderSettings.fog;
            return response.ToString(Formatting.None);
        }

        /// <summary>Set the skybox material.</summary>
        internal static string SetSkybox(JObject args)
        {
            var materialPath = args["material"]?.Value<string>();
            if (string.IsNullOrEmpty(materialPath))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'material' parameter",
                    "Provide the asset path of a skybox material (e.g. 'Assets/Materials/Sky.mat')");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Material not found at '{materialPath}'",
                    "Check the asset path is correct and the material exists");

            RenderSettings.skybox = mat;

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_skybox";
            response["material"] = materialPath;
            return response.ToString(Formatting.None);
        }

        /// <summary>Add a LightProbeGroup to the scene.</summary>
        internal static string AddLightProbeGroup(JObject args)
        {
            var name = args["name"]?.Value<string>() ?? "LightProbeGroup";

            GameObject go;
            var hierarchyPath = args["path"]?.Value<string>();
            if (!string.IsNullOrEmpty(hierarchyPath))
            {
                var existing = GameObject.Find(hierarchyPath);
                if (existing != null)
                    go = existing;
                else
                {
                    go = new GameObject(name);
                    Undo.RegisterCreatedObjectUndo(go, "Theatre lighting_op:add_light_probe_group");
                }
            }
            else
            {
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Theatre lighting_op:add_light_probe_group");
            }

            var group = Undo.AddComponent<LightProbeGroup>(go);

            var positionsToken = args["positions"] as JArray;
            if (positionsToken != null)
            {
                var positions = new Vector3[positionsToken.Count];
                for (int i = 0; i < positionsToken.Count; i++)
                {
                    var p = positionsToken[i] as JArray;
                    if (p != null && p.Count >= 3)
                        positions[i] = new Vector3(
                            p[0].Value<float>(),
                            p[1].Value<float>(),
                            p[2].Value<float>());
                }
                group.probePositions = positions;
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_light_probe_group";
            response["name"] = go.name;
            #pragma warning disable CS0618
            response["instance_id"] = go.GetInstanceID();
            #pragma warning restore CS0618
            return response.ToString(Formatting.None);
        }

        /// <summary>Add a ReflectionProbe to the scene.</summary>
        internal static string AddReflectionProbe(JObject args)
        {
            var name = args["name"]?.Value<string>() ?? "ReflectionProbe";
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Theatre lighting_op:add_reflection_probe");

            var posToken = args["position"] as JArray;
            if (posToken != null && posToken.Count >= 3)
                go.transform.position = new Vector3(
                    posToken[0].Value<float>(),
                    posToken[1].Value<float>(),
                    posToken[2].Value<float>());

            var probe = Undo.AddComponent<ReflectionProbe>(go);

            var sizeToken = args["size"] as JArray;
            if (sizeToken != null && sizeToken.Count >= 3)
                probe.size = new Vector3(
                    sizeToken[0].Value<float>(),
                    sizeToken[1].Value<float>(),
                    sizeToken[2].Value<float>());

            var resToken = args["resolution"];
            if (resToken != null)
                probe.resolution = resToken.Value<int>();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_reflection_probe";
            response["name"] = go.name;
            #pragma warning disable CS0618
            response["instance_id"] = go.GetInstanceID();
            #pragma warning restore CS0618
            return response.ToString(Formatting.None);
        }

        /// <summary>Trigger an async lightmap bake.</summary>
        internal static string Bake(JObject args)
        {
            Lightmapping.BakeAsync();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "bake";
            response["status"] = "started";
            return response.ToString(Formatting.None);
        }
    }
}
