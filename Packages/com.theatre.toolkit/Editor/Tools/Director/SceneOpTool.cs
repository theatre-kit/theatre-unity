using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: scene_op
    /// Compound tool for scene and GameObject mutation in the Unity Editor.
    /// Operations: create_scene, load_scene, unload_scene,
    ///             create_gameobject, delete_gameobject, reparent,
    ///             duplicate, set_component, remove_component, move_to_scene.
    /// </summary>
    public static class SceneOpTool
    {
        private static readonly JToken s_inputSchema;

        static SceneOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create_scene"", ""load_scene"", ""unload_scene"",
                                   ""create_gameobject"", ""delete_gameobject"",
                                   ""reparent"", ""duplicate"",
                                   ""set_component"", ""remove_component"",
                                   ""move_to_scene""],
                        ""description"": ""The scene operation to perform.""
                    },
                    ""path"": {
                        ""type"": ""string"",
                        ""description"": ""Target GameObject hierarchy path (e.g. '/Player'). Used by most operations. For create_scene/load_scene: the .unity asset path.""
                    },
                    ""instance_id"": {
                        ""type"": ""integer"",
                        ""description"": ""Target GameObject instance_id (alternative to path).""
                    },
                    ""name"": {
                        ""type"": ""string"",
                        ""description"": ""GameObject name for create_gameobject; new name for duplicate.""
                    },
                    ""parent"": {
                        ""type"": ""string"",
                        ""description"": ""Parent GameObject path for create_gameobject.""
                    },
                    ""position"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Local position [x,y,z] for create_gameobject.""
                    },
                    ""rotation_euler"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Local Euler angles [x,y,z] in degrees for create_gameobject.""
                    },
                    ""scale"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Local scale [x,y,z] for create_gameobject.""
                    },
                    ""tag"": {
                        ""type"": ""string"",
                        ""description"": ""Tag to assign to the new GameObject.""
                    },
                    ""layer"": {
                        ""type"": ""string"",
                        ""description"": ""Layer name to assign to the new GameObject.""
                    },
                    ""components"": {
                        ""type"": ""array"",
                        ""items"": {
                            ""type"": ""object"",
                            ""properties"": {
                                ""type"": { ""type"": ""string"" },
                                ""properties"": { ""type"": ""object"" }
                            }
                        },
                        ""description"": ""Components to add to the new GameObject. Each item: {type, properties}.""
                    },
                    ""component"": {
                        ""type"": ""string"",
                        ""description"": ""Component type name for set_component or remove_component.""
                    },
                    ""properties"": {
                        ""type"": ""object"",
                        ""description"": ""Properties to set on the component (snake_case keys).""
                    },
                    ""add_if_missing"": {
                        ""type"": ""boolean"",
                        ""description"": ""If true, add the component if not present. Used by set_component. Default: true.""
                    },
                    ""new_parent"": {
                        ""type"": ""string"",
                        ""description"": ""New parent path for reparent. Omit to move to scene root.""
                    },
                    ""sibling_index"": {
                        ""type"": ""integer"",
                        ""description"": ""Target sibling index after reparent or duplicate.""
                    },
                    ""world_position_stays"": {
                        ""type"": ""boolean"",
                        ""description"": ""Whether world position is preserved during reparent. Default: true.""
                    },
                    ""new_name"": {
                        ""type"": ""string"",
                        ""description"": ""New name for duplicated GameObjects.""
                    },
                    ""count"": {
                        ""type"": ""integer"",
                        ""description"": ""Number of duplicates to create. Default: 1.""
                    },
                    ""offset"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Positional offset [x,y,z] applied to each duplicate (multiplied by copy index).""
                    },
                    ""paths"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Array of GameObject paths for move_to_scene.""
                    },
                    ""target_scene"": {
                        ""type"": ""string"",
                        ""description"": ""Target scene name for move_to_scene.""
                    },
                    ""template"": {
                        ""type"": ""string"",
                        ""enum"": [""empty"", ""basic_3d"", ""basic_2d""],
                        ""description"": ""Scene template for create_scene. Default: 'basic_3d'.""
                    },
                    ""open"": {
                        ""type"": ""boolean"",
                        ""description"": ""Whether to open the scene after creation. Default: true.""
                    },
                    ""mode"": {
                        ""type"": ""string"",
                        ""enum"": [""single"", ""additive""],
                        ""description"": ""Load mode for load_scene. Default: 'single'.""
                    },
                    ""scene"": {
                        ""type"": ""string"",
                        ""description"": ""Scene name or path for unload_scene.""
                    },
                    ""dry_run"": {
                        ""type"": ""boolean"",
                        ""description"": ""If true, validate only — do not mutate. Returns would_succeed and errors.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "scene_op",
                description: "Create and modify scenes and GameObjects in the Unity Editor. "
                    + "Operations: create_scene, load_scene, unload_scene, "
                    + "create_gameobject, delete_gameobject, reparent, duplicate, "
                    + "set_component, remove_component, move_to_scene. "
                    + "All mutations are undoable. Supports dry_run to validate without mutating.",
                inputSchema: s_inputSchema,
                group: ToolGroup.DirectorScene,
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
                    "Provide {\"operation\": \"create_gameobject\", \"name\": \"MyObject\", ...}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: create_scene, load_scene, unload_scene, "
                    + "create_gameobject, delete_gameobject, reparent, duplicate, "
                    + "set_component, remove_component, move_to_scene");
            }

            try
            {
                return operation switch
                {
                    "create_scene"       => SceneOpHandlers.CreateScene(args),
                    "load_scene"         => SceneOpHandlers.LoadScene(args),
                    "unload_scene"       => SceneOpHandlers.UnloadScene(args),
                    "create_gameobject"  => SceneOpHandlers.CreateGameObject(args),
                    "delete_gameobject"  => SceneOpHandlers.DeleteGameObject(args),
                    "reparent"           => SceneOpHandlers.Reparent(args),
                    "duplicate"          => SceneOpHandlers.Duplicate(args),
                    "set_component"      => SceneOpHandlers.SetComponent(args),
                    "remove_component"   => SceneOpHandlers.RemoveComponent(args),
                    "move_to_scene"      => SceneOpHandlers.MoveToScene(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create_scene, load_scene, unload_scene, "
                        + "create_gameobject, delete_gameobject, reparent, duplicate, "
                        + "set_component, remove_component, move_to_scene")
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] scene_op:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"scene_op:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }
    }
}
