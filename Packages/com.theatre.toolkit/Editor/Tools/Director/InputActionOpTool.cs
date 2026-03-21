#if THEATRE_HAS_INPUT_SYSTEM
using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: input_action_op
    /// Compound tool for creating and modifying Input System action assets.
    /// Operations: create_asset, add_action_map, add_action, add_binding,
    /// add_composite, set_control_scheme, list_actions.
    /// Requires com.unity.inputsystem (guarded by THEATRE_HAS_INPUT_SYSTEM).
    /// </summary>
    public static class InputActionOpTool
    {
        private static readonly JToken s_inputSchema;

        static InputActionOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create_asset"", ""add_action_map"", ""add_action"", ""add_binding"", ""add_composite"", ""set_control_scheme"", ""list_actions""],
                        ""description"": ""The input action operation to perform.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for the .inputactions file (e.g. 'Assets/Input/Controls.inputactions').""
                    },
                    ""name"": {
                        ""type"": ""string"",
                        ""description"": ""Name of the action map, action, or control scheme to create.""
                    },
                    ""action_map"": {
                        ""type"": ""string"",
                        ""description"": ""Name of the action map to target.""
                    },
                    ""action"": {
                        ""type"": ""string"",
                        ""description"": ""Name of the action to target.""
                    },
                    ""type"": {
                        ""type"": ""string"",
                        ""enum"": [""value"", ""button"", ""pass_through""],
                        ""description"": ""Action type for add_action.""
                    },
                    ""path"": {
                        ""type"": ""string"",
                        ""description"": ""Binding path (e.g. '<Keyboard>/space').""
                    },
                    ""interactions"": {
                        ""type"": ""string"",
                        ""description"": ""Interaction string for the binding (e.g. 'hold').""
                    },
                    ""processors"": {
                        ""type"": ""string"",
                        ""description"": ""Processor string for the binding (e.g. 'invert').""
                    },
                    ""composite_type"": {
                        ""type"": ""string"",
                        ""description"": ""Composite type name (e.g. '2DVector', '1DAxis').""
                    },
                    ""bindings"": {
                        ""type"": ""object"",
                        ""description"": ""Part name to path map for composite bindings (e.g. {'up':'<Keyboard>/w', 'down':'<Keyboard>/s'}).""
                    },
                    ""devices"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Device requirement paths for control scheme (e.g. ['<Keyboard>', '<Mouse>']).""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "input_action_op",
                description: "Create and modify Input System action assets (.inputactions). "
                    + "Operations: create_asset, add_action_map, add_action, add_binding, "
                    + "add_composite, set_control_scheme, list_actions. "
                    + "Requires com.unity.inputsystem package.",
                inputSchema: s_inputSchema,
                group: ToolGroup.DirectorInput,
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
                    "Provide {\"operation\": \"create_asset\", \"asset_path\": \"Assets/Input/Controls.inputactions\"}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: create_asset, add_action_map, add_action, add_binding, add_composite, set_control_scheme, list_actions");
            }

            try
            {
                return operation switch
                {
                    "create_asset"       => CreateAsset(args),
                    "add_action_map"     => AddActionMap(args),
                    "add_action"         => AddAction(args),
                    "add_binding"        => AddBinding(args),
                    "add_composite"      => AddComposite(args),
                    "set_control_scheme" => SetControlScheme(args),
                    "list_actions"       => ListActions(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create_asset, add_action_map, add_action, add_binding, add_composite, set_control_scheme, list_actions")
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] input_action_op:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"input_action_op:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }

        /// <summary>Create a new .inputactions asset at the given path.</summary>
        internal static string CreateAsset(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".inputactions");
            if (pathError != null) return pathError;

            // Ensure parent directory exists
            EnsureParentDirectory(assetPath);

            // Write minimal valid InputActionAsset JSON directly.
            // ScriptableObject.CreateInstance<InputActionAsset>().ToJson() throws
            // on a fresh asset with null internal maps.
            var emptyJson = "{ \"name\": \"\", \"maps\": [], \"controlSchemes\": [] }";
            File.WriteAllText(assetPath, emptyJson);
            AssetDatabase.ImportAsset(assetPath);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create_asset";
            response["asset_path"] = assetPath;
            return response.ToString(Formatting.None);
        }

        /// <summary>Add an action map to an existing .inputactions asset.</summary>
        internal static string AddActionMap(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".inputactions");
            if (pathError != null) return pathError;

            var name = args["name"]?.Value<string>();
            if (string.IsNullOrEmpty(name))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'name' parameter",
                    "Provide a name for the action map (e.g. 'Gameplay')");

            var asset = LoadAsset(assetPath, out var loadError);
            if (asset == null) return loadError;

            asset.AddActionMap(name);
            SaveAsset(asset, assetPath);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_action_map";
            response["asset_path"] = assetPath;
            response["action_map"] = name;
            return response.ToString(Formatting.None);
        }

        /// <summary>Add an action to an existing action map.</summary>
        internal static string AddAction(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".inputactions");
            if (pathError != null) return pathError;

            var mapName = args["action_map"]?.Value<string>();
            if (string.IsNullOrEmpty(mapName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'action_map' parameter",
                    "Provide the name of the action map to add the action to");

            var actionName = args["name"]?.Value<string>();
            if (string.IsNullOrEmpty(actionName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'name' parameter",
                    "Provide a name for the action (e.g. 'Jump')");

            var typeStr = args["type"]?.Value<string>() ?? "button";
            InputActionType actionType = typeStr switch
            {
                "value"        => InputActionType.Value,
                "button"       => InputActionType.Button,
                "pass_through" => InputActionType.PassThrough,
                _ => InputActionType.Button
            };

            var asset = LoadAsset(assetPath, out var loadError);
            if (asset == null) return loadError;

            var map = asset.FindActionMap(mapName);
            if (map == null)
                return ResponseHelpers.ErrorResponse(
                    "not_found",
                    $"Action map '{mapName}' not found in '{assetPath}'",
                    "Use add_action_map to create the map first");

            map.AddAction(actionName, type: actionType);
            SaveAsset(asset, assetPath);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_action";
            response["asset_path"] = assetPath;
            response["action_map"] = mapName;
            response["action"] = actionName;
            response["type"] = typeStr;
            return response.ToString(Formatting.None);
        }

        /// <summary>Add a binding to an existing action.</summary>
        internal static string AddBinding(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".inputactions");
            if (pathError != null) return pathError;

            var mapName = args["action_map"]?.Value<string>();
            if (string.IsNullOrEmpty(mapName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'action_map' parameter",
                    "Provide the name of the action map");

            var actionName = args["action"]?.Value<string>();
            if (string.IsNullOrEmpty(actionName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'action' parameter",
                    "Provide the name of the action");

            var bindingPath = args["path"]?.Value<string>();
            if (string.IsNullOrEmpty(bindingPath))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'path' parameter",
                    "Provide a binding path (e.g. '<Keyboard>/space')");

            var interactions = args["interactions"]?.Value<string>();
            var processors = args["processors"]?.Value<string>();

            var asset = LoadAsset(assetPath, out var loadError);
            if (asset == null) return loadError;

            var map = asset.FindActionMap(mapName);
            if (map == null)
                return ResponseHelpers.ErrorResponse(
                    "not_found",
                    $"Action map '{mapName}' not found in '{assetPath}'",
                    "Use add_action_map to create the map first");

            var action = map.FindAction(actionName);
            if (action == null)
                return ResponseHelpers.ErrorResponse(
                    "not_found",
                    $"Action '{actionName}' not found in map '{mapName}'",
                    "Use add_action to create the action first");

            var builder = action.AddBinding(bindingPath);
            if (!string.IsNullOrEmpty(interactions))
                builder = builder.WithInteraction(interactions);
            if (!string.IsNullOrEmpty(processors))
                builder = builder.WithProcessor(processors);

            SaveAsset(asset, assetPath);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_binding";
            response["asset_path"] = assetPath;
            response["action_map"] = mapName;
            response["action"] = actionName;
            response["path"] = bindingPath;
            return response.ToString(Formatting.None);
        }

        /// <summary>Add a composite binding to an existing action.</summary>
        internal static string AddComposite(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".inputactions");
            if (pathError != null) return pathError;

            var mapName = args["action_map"]?.Value<string>();
            if (string.IsNullOrEmpty(mapName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'action_map' parameter",
                    "Provide the name of the action map");

            var actionName = args["action"]?.Value<string>();
            if (string.IsNullOrEmpty(actionName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'action' parameter",
                    "Provide the name of the action");

            var compositeType = args["composite_type"]?.Value<string>();
            if (string.IsNullOrEmpty(compositeType))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'composite_type' parameter",
                    "Provide a composite type (e.g. '2DVector', '1DAxis', 'ButtonWithOneModifier')");

            var bindingsToken = args["bindings"] as JObject;
            if (bindingsToken == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'bindings' parameter",
                    "Provide a bindings object mapping part names to paths (e.g. {\"up\":\"<Keyboard>/w\", \"down\":\"<Keyboard>/s\"})");

            var asset = LoadAsset(assetPath, out var loadError);
            if (asset == null) return loadError;

            var map = asset.FindActionMap(mapName);
            if (map == null)
                return ResponseHelpers.ErrorResponse(
                    "not_found",
                    $"Action map '{mapName}' not found in '{assetPath}'",
                    "Use add_action_map to create the map first");

            var action = map.FindAction(actionName);
            if (action == null)
                return ResponseHelpers.ErrorResponse(
                    "not_found",
                    $"Action '{actionName}' not found in map '{mapName}'",
                    "Use add_action to create the action first");

            var composite = action.AddCompositeBinding(compositeType);
            foreach (var prop in bindingsToken.Properties())
            {
                composite.With(prop.Name, prop.Value.Value<string>());
            }

            SaveAsset(asset, assetPath);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_composite";
            response["asset_path"] = assetPath;
            response["action_map"] = mapName;
            response["action"] = actionName;
            response["composite_type"] = compositeType;
            return response.ToString(Formatting.None);
        }

        /// <summary>Add a control scheme to an existing asset.</summary>
        internal static string SetControlScheme(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".inputactions");
            if (pathError != null) return pathError;

            var name = args["name"]?.Value<string>();
            if (string.IsNullOrEmpty(name))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'name' parameter",
                    "Provide a name for the control scheme (e.g. 'Keyboard&Mouse')");

            var asset = LoadAsset(assetPath, out var loadError);
            if (asset == null) return loadError;

            var builder = asset.AddControlScheme(name);

            var devicesToken = args["devices"] as JArray;
            if (devicesToken != null)
            {
                foreach (var device in devicesToken)
                {
                    var devicePath = device.Value<string>();
                    if (!string.IsNullOrEmpty(devicePath))
                        builder = builder.WithRequiredDevice(devicePath);
                }
            }

            // builder.Done() completes the scheme
            builder.Done();

            SaveAsset(asset, assetPath);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_control_scheme";
            response["asset_path"] = assetPath;
            response["name"] = name;
            return response.ToString(Formatting.None);
        }

        /// <summary>List all action maps, actions, and bindings in an asset.</summary>
        internal static string ListActions(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".inputactions");
            if (pathError != null) return pathError;

            var asset = LoadAsset(assetPath, out var loadError);
            if (asset == null) return loadError;

            var mapsArray = new JArray();
            foreach (var map in asset.actionMaps)
            {
                var mapObj = new JObject();
                mapObj["name"] = map.name;

                var actionsArray = new JArray();
                foreach (var action in map.actions)
                {
                    var actionObj = new JObject();
                    actionObj["name"] = action.name;
                    actionObj["type"] = action.type.ToString().ToLowerInvariant();

                    var bindingsArray = new JArray();
                    foreach (var binding in action.bindings)
                    {
                        var bindingObj = new JObject();
                        bindingObj["path"] = binding.path;
                        if (binding.isComposite)
                            bindingObj["is_composite"] = true;
                        if (binding.isPartOfComposite)
                        {
                            bindingObj["is_part_of_composite"] = true;
                            bindingObj["name"] = binding.name;
                        }
                        if (!string.IsNullOrEmpty(binding.interactions))
                            bindingObj["interactions"] = binding.interactions;
                        if (!string.IsNullOrEmpty(binding.processors))
                            bindingObj["processors"] = binding.processors;
                        bindingsArray.Add(bindingObj);
                    }
                    actionObj["bindings"] = bindingsArray;
                    actionsArray.Add(actionObj);
                }
                mapObj["actions"] = actionsArray;
                mapsArray.Add(mapObj);
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "list_actions";
            response["asset_path"] = assetPath;
            response["maps"] = mapsArray;
            return response.ToString(Formatting.None);
        }

        // --- Helpers ---

        private static InputActionAsset LoadAsset(string assetPath, out string error)
        {
            error = null;
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);
            if (asset == null)
            {
                error = ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"InputActionAsset not found at '{assetPath}'",
                    "Check the path is correct and ends with .inputactions. Use create_asset first.");
            }
            return asset;
        }

        private static void SaveAsset(InputActionAsset asset, string assetPath)
        {
            File.WriteAllText(assetPath, asset.ToJson());
            AssetDatabase.ImportAsset(assetPath);
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
#endif
