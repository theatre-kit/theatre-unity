using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEditor;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: project_settings_op
    /// Compound tool for project-level settings.
    /// Operations: set_physics, set_time, set_player, set_tags_and_layers.
    /// </summary>
    public static class ProjectSettingsOpTool
    {
        private static readonly JToken s_inputSchema;

        static ProjectSettingsOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""set_physics"", ""set_time"", ""set_player"", ""set_tags_and_layers""],
                        ""description"": ""The project settings operation to perform.""
                    },
                    ""gravity"": {
                        ""type"": ""array"",
                        ""description"": ""Gravity vector [x, y, z].""
                    },
                    ""bounce_threshold"": {
                        ""type"": ""number"",
                        ""description"": ""Physics bounce threshold.""
                    },
                    ""default_solver_iterations"": {
                        ""type"": ""integer"",
                        ""description"": ""Default solver iterations.""
                    },
                    ""fixed_timestep"": {
                        ""type"": ""number"",
                        ""description"": ""Fixed timestep (Time.fixedDeltaTime).""
                    },
                    ""maximum_timestep"": {
                        ""type"": ""number"",
                        ""description"": ""Maximum timestep (Time.maximumDeltaTime).""
                    },
                    ""time_scale"": {
                        ""type"": ""number"",
                        ""description"": ""Time scale.""
                    },
                    ""company_name"": {
                        ""type"": ""string"",
                        ""description"": ""Player settings company name.""
                    },
                    ""product_name"": {
                        ""type"": ""string"",
                        ""description"": ""Player settings product name.""
                    },
                    ""version"": {
                        ""type"": ""string"",
                        ""description"": ""Player settings version string.""
                    },
                    ""add_tags"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Tags to add.""
                    },
                    ""add_sorting_layers"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Sorting layers to add.""
                    },
                    ""add_layers"": {
                        ""type"": ""array"",
                        ""description"": ""Layers to set: [{index: int, name: string}].""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "project_settings_op",
                description: "Configure project-level settings. Operations: set_physics, set_time, set_player, set_tags_and_layers.",
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
                    "Provide {\"operation\": \"set_physics\", \"gravity\": [0, -9.81, 0]}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: set_physics, set_time, set_player, set_tags_and_layers");
            }

            try
            {
                return operation switch
                {
                    "set_physics"         => SetPhysics(args),
                    "set_time"            => SetTime(args),
                    "set_player"          => SetPlayer(args),
                    "set_tags_and_layers" => SetTagsAndLayers(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: set_physics, set_time, set_player, set_tags_and_layers")
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] project_settings_op:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"project_settings_op:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }

        /// <summary>Set physics simulation settings.</summary>
        internal static string SetPhysics(JObject args)
        {
            var gravityToken = args["gravity"] as JArray;
            if (gravityToken != null && gravityToken.Count >= 3)
                Physics.gravity = new Vector3(
                    gravityToken[0].Value<float>(),
                    gravityToken[1].Value<float>(),
                    gravityToken[2].Value<float>());

            var bounceToken = args["bounce_threshold"];
            if (bounceToken != null)
                Physics.bounceThreshold = bounceToken.Value<float>();

            var solverToken = args["default_solver_iterations"];
            if (solverToken != null)
                Physics.defaultSolverIterations = solverToken.Value<int>();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_physics";
            response["gravity"] = new JArray(Physics.gravity.x, Physics.gravity.y, Physics.gravity.z);
            response["bounce_threshold"] = Physics.bounceThreshold;
            response["default_solver_iterations"] = Physics.defaultSolverIterations;
            return response.ToString(Formatting.None);
        }

        /// <summary>Set time settings.</summary>
        internal static string SetTime(JObject args)
        {
            var fixedToken = args["fixed_timestep"];
            if (fixedToken != null)
                Time.fixedDeltaTime = fixedToken.Value<float>();

            var maxToken = args["maximum_timestep"];
            if (maxToken != null)
                Time.maximumDeltaTime = maxToken.Value<float>();

            var scaleToken = args["time_scale"];
            if (scaleToken != null)
                Time.timeScale = scaleToken.Value<float>();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_time";
            response["fixed_timestep"] = Time.fixedDeltaTime;
            response["maximum_timestep"] = Time.maximumDeltaTime;
            response["time_scale"] = Time.timeScale;
            return response.ToString(Formatting.None);
        }

        /// <summary>Set player settings.</summary>
        internal static string SetPlayer(JObject args)
        {
            var companyToken = args["company_name"]?.Value<string>();
            if (companyToken != null)
                PlayerSettings.companyName = companyToken;

            var productToken = args["product_name"]?.Value<string>();
            if (productToken != null)
                PlayerSettings.productName = productToken;

            var versionToken = args["version"]?.Value<string>();
            if (versionToken != null)
                PlayerSettings.bundleVersion = versionToken;

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_player";
            response["company_name"] = PlayerSettings.companyName;
            response["product_name"] = PlayerSettings.productName;
            response["version"] = PlayerSettings.bundleVersion;
            return response.ToString(Formatting.None);
        }

        /// <summary>Add tags, sorting layers, and named layers to the project.</summary>
        internal static string SetTagsAndLayers(JObject args)
        {
            var tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

            var addedTags = new JArray();
            var addedLayers = new JArray();
            var addedSortingLayers = new JArray();

            // --- Tags ---
            var addTagsToken = args["add_tags"] as JArray;
            if (addTagsToken != null)
            {
                var tagsProp = tagManager.FindProperty("tags");
                foreach (var tagToken in addTagsToken)
                {
                    var tagName = tagToken.Value<string>();
                    if (string.IsNullOrEmpty(tagName)) continue;

                    // Check if tag already exists
                    bool exists = false;
                    for (int i = 0; i < tagsProp.arraySize; i++)
                    {
                        if (tagsProp.GetArrayElementAtIndex(i).stringValue == tagName)
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                    {
                        tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                        tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tagName;
                        addedTags.Add(tagName);
                    }
                }
            }

            // --- Layers ---
            var addLayersToken = args["add_layers"] as JArray;
            if (addLayersToken != null)
            {
                var layersProp = tagManager.FindProperty("layers");
                foreach (var layerToken in addLayersToken)
                {
                    var layerObj = layerToken as JObject;
                    if (layerObj == null) continue;

                    var index = layerObj["index"]?.Value<int>() ?? -1;
                    var layerName = layerObj["name"]?.Value<string>();
                    if (index < 0 || index >= layersProp.arraySize || string.IsNullOrEmpty(layerName))
                        continue;

                    layersProp.GetArrayElementAtIndex(index).stringValue = layerName;
                    addedLayers.Add(new JObject { ["index"] = index, ["name"] = layerName });
                }
            }

            // --- Sorting Layers ---
            var addSortingLayersToken = args["add_sorting_layers"] as JArray;
            if (addSortingLayersToken != null)
            {
                var sortingLayersProp = tagManager.FindProperty("m_SortingLayers");
                if (sortingLayersProp != null)
                {
                    // Collect existing names to avoid duplicates
                    var existingNames = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < sortingLayersProp.arraySize; i++)
                    {
                        var elem = sortingLayersProp.GetArrayElementAtIndex(i);
                        var nameProp = elem.FindPropertyRelative("name");
                        if (nameProp != null)
                            existingNames.Add(nameProp.stringValue);
                    }

                    foreach (var slToken in addSortingLayersToken)
                    {
                        var slName = slToken.Value<string>();
                        if (string.IsNullOrEmpty(slName) || existingNames.Contains(slName)) continue;

                        sortingLayersProp.InsertArrayElementAtIndex(sortingLayersProp.arraySize);
                        var newElem = sortingLayersProp.GetArrayElementAtIndex(sortingLayersProp.arraySize - 1);
                        var nameProp = newElem.FindPropertyRelative("name");
                        if (nameProp != null)
                        {
                            nameProp.stringValue = slName;
                            existingNames.Add(slName);
                            addedSortingLayers.Add(slName);
                        }
                    }
                }
            }

            tagManager.ApplyModifiedProperties();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_tags_and_layers";
            response["added_tags"] = addedTags;
            response["added_layers"] = addedLayers;
            response["added_sorting_layers"] = addedSortingLayers;
            return response.ToString(Formatting.None);
        }
    }
}
