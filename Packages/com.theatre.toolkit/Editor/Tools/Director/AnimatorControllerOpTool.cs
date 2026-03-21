using System;
using System.Collections.Generic;
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
    /// MCP tool: animator_controller_op
    /// Compound tool for creating and modifying AnimatorController assets in the Unity Editor.
    /// Operations: create, add_parameter, add_state, set_state_clip, add_transition,
    ///             set_transition_conditions, set_default_state, add_layer, list_states.
    /// </summary>
    public static class AnimatorControllerOpTool
    {
        private static readonly JToken s_inputSchema;

        static AnimatorControllerOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create"", ""add_parameter"", ""add_state"", ""set_state_clip"", ""add_transition"", ""set_transition_conditions"", ""set_default_state"", ""add_layer"", ""list_states""],
                        ""description"": ""The animator controller operation to perform.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for the AnimatorController (e.g. 'Assets/Animations/MyCtrl.controller').""
                    },
                    ""name"": {
                        ""type"": ""string"",
                        ""description"": ""Name for a parameter, state, or layer.""
                    },
                    ""type"": {
                        ""type"": ""string"",
                        ""enum"": [""float"", ""int"", ""bool"", ""trigger""],
                        ""description"": ""Parameter type.""
                    },
                    ""default_value"": {
                        ""type"": ""number"",
                        ""description"": ""Default value for the parameter (numeric or 0/1 for bool).""
                    },
                    ""state_name"": {
                        ""type"": ""string"",
                        ""description"": ""Name of the target state.""
                    },
                    ""clip_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path to an AnimationClip to assign to a state.""
                    },
                    ""layer"": {
                        ""type"": ""integer"",
                        ""description"": ""Layer index (default 0).""
                    },
                    ""position"": {
                        ""type"": ""array"",
                        ""description"": ""[x, y] position for the state node in the editor graph.""
                    },
                    ""source_state"": {
                        ""type"": ""string"",
                        ""description"": ""Name of the source state for a transition.""
                    },
                    ""destination_state"": {
                        ""type"": ""string"",
                        ""description"": ""Name of the destination state for a transition.""
                    },
                    ""has_exit_time"": {
                        ""type"": ""boolean"",
                        ""description"": ""Whether the transition uses exit time (default true).""
                    },
                    ""exit_time"": {
                        ""type"": ""number"",
                        ""description"": ""Normalised exit time (default 0.75).""
                    },
                    ""transition_duration"": {
                        ""type"": ""number"",
                        ""description"": ""Transition blend duration in normalised time (default 0.25).""
                    },
                    ""conditions"": {
                        ""type"": ""array"",
                        ""description"": ""Array of condition objects: [{parameter, mode, threshold?}]. Mode: if, if_not, greater, less, equals, not_equals.""
                    },
                    ""blend_mode"": {
                        ""type"": ""string"",
                        ""enum"": [""override"", ""additive""],
                        ""description"": ""Layer blend mode (default 'override').""
                    },
                    ""weight"": {
                        ""type"": ""number"",
                        ""description"": ""Layer weight (default 1.0).""
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
                name: "animator_controller_op",
                description: "Create and modify AnimatorController assets in the Unity Editor. "
                    + "Operations: create, add_parameter, add_state, set_state_clip, add_transition, "
                    + "set_transition_conditions, set_default_state, add_layer, list_states. "
                    + "Supports dry_run to validate without mutating.",
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
                    "Provide {\"operation\": \"create\", \"asset_path\": \"Assets/Animations/Ctrl.controller\"}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: create, add_parameter, add_state, set_state_clip, add_transition, set_transition_conditions, set_default_state, add_layer, list_states");
            }

            try
            {
                return operation switch
                {
                    "create"                   => Create(args),
                    "add_parameter"            => AddParameter(args),
                    "add_state"                => AddState(args),
                    "set_state_clip"           => SetStateClip(args),
                    "add_transition"           => AddTransition(args),
                    "set_transition_conditions"=> SetTransitionConditions(args),
                    "set_default_state"        => SetDefaultState(args),
                    "add_layer"                => AddLayer(args),
                    "list_states"              => ListStates(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create, add_parameter, add_state, set_state_clip, add_transition, set_transition_conditions, set_default_state, add_layer, list_states")
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] animator_controller_op:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"animator_controller_op:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }

        /// <summary>Create a new AnimatorController asset at the given path.</summary>
        internal static string Create(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".controller");
            if (pathError != null) return pathError;

            var dryRun = DirectorHelpers.CheckDryRun(args, () => (true, new List<string>()));
            if (dryRun != null) return dryRun;

            EnsureParentDirectory(assetPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(assetPath);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create";
            response["asset_path"] = assetPath;
            response["layer_count"] = controller.layers.Length;
            return response.ToString(Formatting.None);
        }

        /// <summary>Add a parameter to an existing AnimatorController.</summary>
        internal static string AddParameter(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".controller");
            if (pathError != null) return pathError;

            var name = args["name"]?.Value<string>();
            if (string.IsNullOrEmpty(name))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'name' parameter",
                    "Provide the parameter name");

            var typeStr = args["type"]?.Value<string>();
            if (!TryParseParamType(typeStr, out var paramType))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Invalid parameter type '{typeStr}'",
                    "Valid types: float, int, bool, trigger");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (controller == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"AnimatorController not found at '{assetPath}'",
                    "Check the asset path is correct and ends with .controller");

            controller.AddParameter(name, paramType);

            // Set default value if provided
            if (args["default_value"] != null)
            {
                var parameters = controller.parameters;
                // Find the parameter we just added
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].name == name && parameters[i].type == paramType)
                    {
                        switch (paramType)
                        {
                            case AnimatorControllerParameterType.Float:
                                parameters[i].defaultFloat = args["default_value"].Value<float>();
                                break;
                            case AnimatorControllerParameterType.Int:
                                parameters[i].defaultInt = args["default_value"].Value<int>();
                                break;
                            case AnimatorControllerParameterType.Bool:
                                parameters[i].defaultBool = args["default_value"].Value<bool>();
                                break;
                        }
                        break;
                    }
                }
                controller.parameters = parameters;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_parameter";
            response["asset_path"] = assetPath;
            response["name"] = name;
            response["type"] = typeStr;
            return response.ToString(Formatting.None);
        }

        /// <summary>Add a state to the AnimatorController's state machine.</summary>
        internal static string AddState(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".controller");
            if (pathError != null) return pathError;

            var name = args["name"]?.Value<string>();
            if (string.IsNullOrEmpty(name))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'name' parameter",
                    "Provide the state name");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (controller == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"AnimatorController not found at '{assetPath}'",
                    "Check the asset path is correct and ends with .controller");

            var layerIndex = args["layer"]?.Value<int>() ?? 0;
            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Layer index {layerIndex} is out of range (controller has {layers.Length} layers)",
                    "Use a valid layer index");

            Vector3 position = Vector3.zero;
            if (args["position"] is JArray posArr && posArr.Count >= 2)
                position = new Vector3(posArr[0].Value<float>(), posArr[1].Value<float>(), 0f);

            var stateMachine = layers[layerIndex].stateMachine;
            stateMachine.AddState(name, position);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_state";
            response["asset_path"] = assetPath;
            response["name"] = name;
            response["layer"] = layerIndex;
            return response.ToString(Formatting.None);
        }

        /// <summary>Assign an AnimationClip to a state's motion.</summary>
        internal static string SetStateClip(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".controller");
            if (pathError != null) return pathError;

            var stateName = args["state_name"]?.Value<string>();
            if (string.IsNullOrEmpty(stateName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'state_name' parameter",
                    "Provide the name of the state to assign the clip to");

            var clipPath = args["clip_path"]?.Value<string>();
            var clipPathError = DirectorHelpers.ValidateAssetPath(clipPath, ".anim");
            if (clipPathError != null) return clipPathError;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (controller == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"AnimatorController not found at '{assetPath}'",
                    "Check the asset path is correct");

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"AnimationClip not found at '{clipPath}'",
                    "Check the clip path is correct and ends with .anim");

            var layerIndex = args["layer"]?.Value<int>() ?? 0;
            var state = FindState(controller, stateName, layerIndex, out var stateError);
            if (stateError != null) return stateError;

            state.motion = clip;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_state_clip";
            response["asset_path"] = assetPath;
            response["state_name"] = stateName;
            response["clip_path"] = clipPath;
            return response.ToString(Formatting.None);
        }

        /// <summary>Add a transition between two states.</summary>
        internal static string AddTransition(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".controller");
            if (pathError != null) return pathError;

            var sourceStateName = args["source_state"]?.Value<string>();
            if (string.IsNullOrEmpty(sourceStateName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'source_state' parameter",
                    "Provide the name of the source state");

            var destStateName = args["destination_state"]?.Value<string>();
            if (string.IsNullOrEmpty(destStateName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'destination_state' parameter",
                    "Provide the name of the destination state");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (controller == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"AnimatorController not found at '{assetPath}'",
                    "Check the asset path is correct");

            var layerIndex = args["layer"]?.Value<int>() ?? 0;
            var sourceState = FindState(controller, sourceStateName, layerIndex, out var srcError);
            if (srcError != null) return srcError;

            var destState = FindState(controller, destStateName, layerIndex, out var dstError);
            if (dstError != null) return dstError;

            var transition = sourceState.AddTransition(destState);
            transition.hasExitTime = args["has_exit_time"]?.Value<bool>() ?? true;
            transition.exitTime = args["exit_time"]?.Value<float>() ?? 0.75f;
            transition.duration = args["transition_duration"]?.Value<float>() ?? 0.25f;

            // Add conditions
            if (args["conditions"] is JArray conditions)
            {
                foreach (var condToken in conditions)
                {
                    var cond = condToken as JObject;
                    if (cond == null) continue;

                    var paramName = cond["parameter"]?.Value<string>() ?? "";
                    var modeStr = cond["mode"]?.Value<string>() ?? "if";
                    var threshold = cond["threshold"]?.Value<float>() ?? 0f;
                    var condMode = ParseConditionMode(modeStr);
                    transition.AddCondition(condMode, threshold, paramName);
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_transition";
            response["asset_path"] = assetPath;
            response["source_state"] = sourceStateName;
            response["destination_state"] = destStateName;
            response["has_exit_time"] = transition.hasExitTime;
            response["exit_time"] = transition.exitTime;
            response["transition_duration"] = transition.duration;
            return response.ToString(Formatting.None);
        }

        /// <summary>Replace all conditions on the transition between two states.</summary>
        internal static string SetTransitionConditions(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".controller");
            if (pathError != null) return pathError;

            var sourceStateName = args["source_state"]?.Value<string>();
            if (string.IsNullOrEmpty(sourceStateName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'source_state' parameter",
                    "Provide the name of the source state");

            var destStateName = args["destination_state"]?.Value<string>();
            if (string.IsNullOrEmpty(destStateName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'destination_state' parameter",
                    "Provide the name of the destination state");

            var conditions = args["conditions"] as JArray;
            if (conditions == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'conditions' array",
                    "Provide an array of condition objects");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (controller == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"AnimatorController not found at '{assetPath}'",
                    "Check the asset path is correct");

            var layerIndex = args["layer"]?.Value<int>() ?? 0;
            var sourceState = FindState(controller, sourceStateName, layerIndex, out var srcError);
            if (srcError != null) return srcError;

            // Find the transition to the destination state
            AnimatorStateTransition targetTransition = null;
            foreach (var transition in sourceState.transitions)
            {
                if (transition.destinationState != null &&
                    transition.destinationState.name == destStateName)
                {
                    targetTransition = transition;
                    break;
                }
            }

            if (targetTransition == null)
                return ResponseHelpers.ErrorResponse(
                    "not_found",
                    $"No transition found from '{sourceStateName}' to '{destStateName}'",
                    "Add the transition first with add_transition");

            // Clear and re-add conditions using SerializedObject
            var so = new SerializedObject(targetTransition);
            var conditionsProp = so.FindProperty("m_Conditions");
            conditionsProp.ClearArray();
            so.ApplyModifiedProperties();

            foreach (var condToken in conditions)
            {
                var cond = condToken as JObject;
                if (cond == null) continue;

                var paramName = cond["parameter"]?.Value<string>() ?? "";
                var modeStr = cond["mode"]?.Value<string>() ?? "if";
                var threshold = cond["threshold"]?.Value<float>() ?? 0f;
                var condMode = ParseConditionMode(modeStr);
                targetTransition.AddCondition(condMode, threshold, paramName);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_transition_conditions";
            response["asset_path"] = assetPath;
            response["source_state"] = sourceStateName;
            response["destination_state"] = destStateName;
            response["condition_count"] = conditions.Count;
            return response.ToString(Formatting.None);
        }

        /// <summary>Set the default state in a state machine.</summary>
        internal static string SetDefaultState(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".controller");
            if (pathError != null) return pathError;

            var stateName = args["state_name"]?.Value<string>();
            if (string.IsNullOrEmpty(stateName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'state_name' parameter",
                    "Provide the name of the state to make default");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (controller == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"AnimatorController not found at '{assetPath}'",
                    "Check the asset path is correct");

            var layerIndex = args["layer"]?.Value<int>() ?? 0;
            var state = FindState(controller, stateName, layerIndex, out var stateError);
            if (stateError != null) return stateError;

            var layers = controller.layers;
            layers[layerIndex].stateMachine.defaultState = state;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_default_state";
            response["asset_path"] = assetPath;
            response["state_name"] = stateName;
            response["layer"] = layerIndex;
            return response.ToString(Formatting.None);
        }

        /// <summary>Add a new layer to the AnimatorController.</summary>
        internal static string AddLayer(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".controller");
            if (pathError != null) return pathError;

            var name = args["name"]?.Value<string>();
            if (string.IsNullOrEmpty(name))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'name' parameter",
                    "Provide the layer name");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (controller == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"AnimatorController not found at '{assetPath}'",
                    "Check the asset path is correct");

            controller.AddLayer(name);

            // Configure the new layer
            var layers = controller.layers;
            var newLayer = layers[layers.Length - 1];

            var blendModeStr = args["blend_mode"]?.Value<string>();
            if (!string.IsNullOrEmpty(blendModeStr))
            {
                newLayer.blendingMode = blendModeStr.ToLowerInvariant() == "additive"
                    ? AnimatorLayerBlendingMode.Additive
                    : AnimatorLayerBlendingMode.Override;
            }

            if (args["weight"] != null)
                newLayer.defaultWeight = args["weight"].Value<float>();
            else
                newLayer.defaultWeight = 1f;

            layers[layers.Length - 1] = newLayer;
            controller.layers = layers;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_layer";
            response["asset_path"] = assetPath;
            response["name"] = name;
            response["layer_index"] = layers.Length - 1;
            return response.ToString(Formatting.None);
        }

        /// <summary>List all states, transitions, and parameters in the controller.</summary>
        internal static string ListStates(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".controller");
            if (pathError != null) return pathError;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (controller == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"AnimatorController not found at '{assetPath}'",
                    "Check the asset path is correct");

            var layerIndex = args["layer"]?.Value<int>() ?? 0;
            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Layer index {layerIndex} is out of range (controller has {layers.Length} layers)",
                    "Use a valid layer index");

            var stateMachine = layers[layerIndex].stateMachine;
            var defaultState = stateMachine.defaultState;

            var statesArray = new JArray();
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                var stateObj = new JObject();
                stateObj["name"] = state.name;

                if (state.motion != null)
                    stateObj["clip"] = AssetDatabase.GetAssetPath(state.motion);
                else
                    stateObj["clip"] = JValue.CreateNull();

                stateObj["is_default"] = defaultState != null && state.name == defaultState.name;

                var transitionsArray = new JArray();
                foreach (var transition in state.transitions)
                {
                    var tObj = new JObject();
                    tObj["destination"] = transition.destinationState != null
                        ? transition.destinationState.name
                        : (JToken)JValue.CreateNull();
                    tObj["has_exit_time"] = transition.hasExitTime;

                    var condArray = new JArray();
                    foreach (var cond in transition.conditions)
                    {
                        condArray.Add(new JObject
                        {
                            ["parameter"] = cond.parameter,
                            ["mode"] = cond.mode.ToString().ToLowerInvariant(),
                            ["threshold"] = cond.threshold
                        });
                    }
                    tObj["conditions"] = condArray;
                    transitionsArray.Add(tObj);
                }
                stateObj["transitions"] = transitionsArray;
                statesArray.Add(stateObj);
            }

            // Parameters
            var paramsArray = new JArray();
            foreach (var param in controller.parameters)
            {
                paramsArray.Add(new JObject
                {
                    ["name"] = param.name,
                    ["type"] = param.type.ToString().ToLowerInvariant()
                });
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "list_states";
            response["asset_path"] = assetPath;
            response["layer"] = layerIndex;
            response["states"] = statesArray;
            response["parameters"] = paramsArray;
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
                "Add the state first with add_state, or check the spelling");
            return null;
        }

        private static bool TryParseParamType(string typeStr, out AnimatorControllerParameterType paramType)
        {
            paramType = AnimatorControllerParameterType.Float;
            switch (typeStr?.ToLowerInvariant())
            {
                case "float":   paramType = AnimatorControllerParameterType.Float;   return true;
                case "int":     paramType = AnimatorControllerParameterType.Int;     return true;
                case "bool":    paramType = AnimatorControllerParameterType.Bool;    return true;
                case "trigger": paramType = AnimatorControllerParameterType.Trigger; return true;
                default:        return false;
            }
        }

        private static AnimatorConditionMode ParseConditionMode(string modeStr)
        {
            return modeStr?.ToLowerInvariant() switch
            {
                "if_not"    => AnimatorConditionMode.IfNot,
                "greater"   => AnimatorConditionMode.Greater,
                "less"      => AnimatorConditionMode.Less,
                "equals"    => AnimatorConditionMode.Equals,
                "not_equals"=> AnimatorConditionMode.NotEqual,
                _           => AnimatorConditionMode.If
            };
        }

        private static void EnsureParentDirectory(string assetPath)
        {
            var lastSlash = assetPath.LastIndexOf('/');
            if (lastSlash <= 0) return;

            var parentPath = assetPath.Substring(0, lastSlash);
            if (!AssetDatabase.IsValidFolder(parentPath))
            {
                var grandparentSlash = parentPath.LastIndexOf('/');
                if (grandparentSlash >= 0)
                {
                    var grandparent = parentPath.Substring(0, grandparentSlash);
                    var folderName = parentPath.Substring(grandparentSlash + 1);
                    EnsureParentDirectory(parentPath);
                    if (!AssetDatabase.IsValidFolder(parentPath))
                        AssetDatabase.CreateFolder(grandparent, folderName);
                }
            }
        }
    }
}
