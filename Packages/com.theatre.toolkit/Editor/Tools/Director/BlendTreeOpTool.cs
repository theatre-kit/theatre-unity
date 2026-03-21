using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: blend_tree_op
    /// Compound tool for creating and configuring blend trees within AnimatorController states.
    /// Operations: create, add_motion, set_blend_type, set_parameter, set_thresholds.
    /// </summary>
    public static class BlendTreeOpTool
    {
        private static readonly JToken s_inputSchema;

        static BlendTreeOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create"", ""add_motion"", ""set_blend_type"", ""set_parameter"", ""set_thresholds""],
                        ""description"": ""The blend tree operation to perform.""
                    },
                    ""controller_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path to the AnimatorController (.controller file).""
                    },
                    ""state_name"": {
                        ""type"": ""string"",
                        ""description"": ""Name of the state that contains (or will contain) the blend tree.""
                    },
                    ""layer"": {
                        ""type"": ""integer"",
                        ""description"": ""Layer index (default 0).""
                    },
                    ""blend_type"": {
                        ""type"": ""string"",
                        ""enum"": [""1d"", ""2d_simple_directional"", ""2d_freeform_directional"", ""2d_freeform_cartesian"", ""direct""],
                        ""description"": ""Blend tree type.""
                    },
                    ""parameter"": {
                        ""type"": ""string"",
                        ""description"": ""Name of the blend parameter.""
                    },
                    ""parameter_y"": {
                        ""type"": ""string"",
                        ""description"": ""Name of the Y blend parameter (for 2D blend types).""
                    },
                    ""clip_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path to an AnimationClip to add as a blend tree child.""
                    },
                    ""threshold"": {
                        ""type"": ""number"",
                        ""description"": ""Threshold value for 1D blend tree child (default 0).""
                    },
                    ""position"": {
                        ""type"": ""array"",
                        ""description"": ""[x, y] position for 2D blend tree child.""
                    },
                    ""time_scale"": {
                        ""type"": ""number"",
                        ""description"": ""Time scale for the added child motion (default 1).""
                    },
                    ""thresholds"": {
                        ""type"": ""array"",
                        ""description"": ""Array of float threshold values to assign to blend tree children.""
                    },
                    ""dry_run"": {
                        ""type"": ""boolean"",
                        ""description"": ""If true, validate only — do not mutate.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "blend_tree_op",
                description: "Create and configure blend trees within AnimatorController states. "
                    + "Operations: create, add_motion, set_blend_type, set_parameter, set_thresholds.",
                inputSchema: s_inputSchema,
                group: ToolGroup.DirectorAnim,
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
                    "Provide {\"operation\": \"create\", \"controller_path\": \"Assets/Ctrl.controller\", \"state_name\": \"Walk\"}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: create, add_motion, set_blend_type, set_parameter, set_thresholds");
            }

            try
            {
                return operation switch
                {
                    "create"         => Create(args),
                    "add_motion"     => AddMotion(args),
                    "set_blend_type" => SetBlendType(args),
                    "set_parameter"  => SetParameter(args),
                    "set_thresholds" => SetThresholds(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create, add_motion, set_blend_type, set_parameter, set_thresholds")
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] blend_tree_op:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"blend_tree_op:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }

        /// <summary>Create a blend tree as the motion of a state.</summary>
        internal static string Create(JObject args)
        {
            var controllerPath = args["controller_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(controllerPath, ".controller");
            if (pathError != null) return pathError;

            var stateName = args["state_name"]?.Value<string>();
            if (string.IsNullOrEmpty(stateName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'state_name' parameter",
                    "Provide the name of the state to replace its motion with a blend tree");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"AnimatorController not found at '{controllerPath}'",
                    "Check the asset path is correct and ends with .controller");

            var layerIndex = args["layer"]?.Value<int>() ?? 0;
            var state = FindState(controller, stateName, layerIndex, out var stateError);
            if (stateError != null) return stateError;

            var tree = new BlendTree();
            tree.name = stateName;

            var blendTypeStr = args["blend_type"]?.Value<string>();
            tree.blendType = ParseBlendType(blendTypeStr);

            var parameter = args["parameter"]?.Value<string>();
            if (!string.IsNullOrEmpty(parameter))
                tree.blendParameter = parameter;

            state.motion = tree;

            // CRITICAL: add as sub-asset so it persists with the controller
            AssetDatabase.AddObjectToAsset(tree, controllerPath);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create";
            response["controller_path"] = controllerPath;
            response["state_name"] = stateName;
            response["blend_type"] = blendTypeStr ?? "1d";
            return response.ToString(Formatting.None);
        }

        /// <summary>Add a motion (animation clip) as a child of a blend tree.</summary>
        internal static string AddMotion(JObject args)
        {
            var controllerPath = args["controller_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(controllerPath, ".controller");
            if (pathError != null) return pathError;

            var stateName = args["state_name"]?.Value<string>();
            if (string.IsNullOrEmpty(stateName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'state_name' parameter",
                    "Provide the name of the state containing the blend tree");

            var clipPath = args["clip_path"]?.Value<string>();
            var clipPathError = DirectorHelpers.ValidateAssetPath(clipPath, ".anim");
            if (clipPathError != null) return clipPathError;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"AnimatorController not found at '{controllerPath}'",
                    "Check the asset path is correct");

            var layerIndex = args["layer"]?.Value<int>() ?? 0;
            var state = FindState(controller, stateName, layerIndex, out var stateError);
            if (stateError != null) return stateError;

            var tree = state.motion as BlendTree;
            if (tree == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_state",
                    $"State '{stateName}' does not have a BlendTree motion",
                    "Use blend_tree_op:create to set up a blend tree on this state first");

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"AnimationClip not found at '{clipPath}'",
                    "Check the clip path is correct and ends with .anim");

            // Add child based on blend tree type
            var is2D = tree.blendType == BlendTreeType.SimpleDirectional2D
                    || tree.blendType == BlendTreeType.FreeformDirectional2D
                    || tree.blendType == BlendTreeType.FreeformCartesian2D;

            if (is2D && args["position"] is JArray posArr && posArr.Count >= 2)
            {
                var pos = new Vector2(posArr[0].Value<float>(), posArr[1].Value<float>());
                tree.AddChild(clip, pos);
            }
            else
            {
                var threshold = args["threshold"]?.Value<float>() ?? 0f;
                tree.AddChild(clip, threshold);
            }

            // Apply time_scale to the last-added child if specified
            if (args["time_scale"] != null)
            {
                var timeScale = args["time_scale"].Value<float>();
                var children = tree.children;
                if (children.Length > 0)
                {
                    children[children.Length - 1].timeScale = timeScale;
                    tree.children = children;
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_motion";
            response["controller_path"] = controllerPath;
            response["state_name"] = stateName;
            response["clip_path"] = clipPath;
            response["child_count"] = tree.children.Length;
            return response.ToString(Formatting.None);
        }

        /// <summary>Change the blend type of an existing blend tree.</summary>
        internal static string SetBlendType(JObject args)
        {
            var controllerPath = args["controller_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(controllerPath, ".controller");
            if (pathError != null) return pathError;

            var stateName = args["state_name"]?.Value<string>();
            if (string.IsNullOrEmpty(stateName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'state_name' parameter",
                    "Provide the name of the state containing the blend tree");

            var blendTypeStr = args["blend_type"]?.Value<string>();
            if (string.IsNullOrEmpty(blendTypeStr))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'blend_type' parameter",
                    "Valid values: 1d, 2d_simple_directional, 2d_freeform_directional, 2d_freeform_cartesian, direct");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"AnimatorController not found at '{controllerPath}'",
                    "Check the asset path is correct");

            var layerIndex = args["layer"]?.Value<int>() ?? 0;
            var state = FindState(controller, stateName, layerIndex, out var stateError);
            if (stateError != null) return stateError;

            var tree = state.motion as BlendTree;
            if (tree == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_state",
                    $"State '{stateName}' does not have a BlendTree motion",
                    "Use blend_tree_op:create to set up a blend tree on this state first");

            tree.blendType = ParseBlendType(blendTypeStr);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_blend_type";
            response["controller_path"] = controllerPath;
            response["state_name"] = stateName;
            response["blend_type"] = blendTypeStr;
            return response.ToString(Formatting.None);
        }

        /// <summary>Assign blend parameters to an existing blend tree.</summary>
        internal static string SetParameter(JObject args)
        {
            var controllerPath = args["controller_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(controllerPath, ".controller");
            if (pathError != null) return pathError;

            var stateName = args["state_name"]?.Value<string>();
            if (string.IsNullOrEmpty(stateName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'state_name' parameter",
                    "Provide the name of the state containing the blend tree");

            var parameter = args["parameter"]?.Value<string>();
            if (string.IsNullOrEmpty(parameter))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'parameter' parameter",
                    "Provide the animator parameter name to use for blending");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"AnimatorController not found at '{controllerPath}'",
                    "Check the asset path is correct");

            var layerIndex = args["layer"]?.Value<int>() ?? 0;
            var state = FindState(controller, stateName, layerIndex, out var stateError);
            if (stateError != null) return stateError;

            var tree = state.motion as BlendTree;
            if (tree == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_state",
                    $"State '{stateName}' does not have a BlendTree motion",
                    "Use blend_tree_op:create to set up a blend tree on this state first");

            tree.blendParameter = parameter;

            var parameterY = args["parameter_y"]?.Value<string>();
            if (!string.IsNullOrEmpty(parameterY))
                tree.blendParameterY = parameterY;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_parameter";
            response["controller_path"] = controllerPath;
            response["state_name"] = stateName;
            response["parameter"] = parameter;
            if (!string.IsNullOrEmpty(parameterY))
                response["parameter_y"] = parameterY;
            return response.ToString(Formatting.None);
        }

        /// <summary>Set per-child threshold values on an existing blend tree.</summary>
        internal static string SetThresholds(JObject args)
        {
            var controllerPath = args["controller_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(controllerPath, ".controller");
            if (pathError != null) return pathError;

            var stateName = args["state_name"]?.Value<string>();
            if (string.IsNullOrEmpty(stateName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'state_name' parameter",
                    "Provide the name of the state containing the blend tree");

            var thresholdsArray = args["thresholds"] as JArray;
            if (thresholdsArray == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'thresholds' array",
                    "Provide an array of float values, one per blend tree child");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"AnimatorController not found at '{controllerPath}'",
                    "Check the asset path is correct");

            var layerIndex = args["layer"]?.Value<int>() ?? 0;
            var state = FindState(controller, stateName, layerIndex, out var stateError);
            if (stateError != null) return stateError;

            var tree = state.motion as BlendTree;
            if (tree == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_state",
                    $"State '{stateName}' does not have a BlendTree motion",
                    "Use blend_tree_op:create to set up a blend tree on this state first");

            // BlendTree.children returns a copy — must modify then set back
            var children = tree.children;
            for (int i = 0; i < children.Length && i < thresholdsArray.Count; i++)
                children[i].threshold = thresholdsArray[i].Value<float>();
            tree.children = children;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_thresholds";
            response["controller_path"] = controllerPath;
            response["state_name"] = stateName;
            response["child_count"] = children.Length;
            return response.ToString(Formatting.None);
        }

        // --- Helpers ---

        private static AnimatorState FindState(
            AnimatorController controller, string stateName, int layerIndex,
            out string error)
        {
            error = null;
            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
            {
                error = ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Layer index {layerIndex} is out of range (controller has {layers.Length} layers)",
                    "Use a valid layer index");
                return null;
            }

            var stateMachine = layers[layerIndex].stateMachine;
            foreach (var childState in stateMachine.states)
            {
                if (childState.state.name == stateName)
                    return childState.state;
            }

            error = ResponseHelpers.ErrorResponse(
                "not_found",
                $"State '{stateName}' not found in layer {layerIndex}",
                "Add the state first with animator_controller_op:add_state, or check the spelling");
            return null;
        }

        private static BlendTreeType ParseBlendType(string blendTypeStr)
        {
            return blendTypeStr?.ToLowerInvariant() switch
            {
                "2d_simple_directional"   => BlendTreeType.SimpleDirectional2D,
                "2d_freeform_directional" => BlendTreeType.FreeformDirectional2D,
                "2d_freeform_cartesian"   => BlendTreeType.FreeformCartesian2D,
                "direct"                  => BlendTreeType.Direct,
                _                         => BlendTreeType.Simple1D
            };
        }
    }
}
