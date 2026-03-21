using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: prefab_op
    /// Compound tool for prefab lifecycle operations in the Unity Editor.
    /// Operations: create_prefab, instantiate, apply_overrides, revert_overrides,
    ///             unpack, create_variant, list_overrides.
    /// </summary>
    public static class PrefabOpTool
    {
        private static readonly JToken s_inputSchema;

        static PrefabOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create_prefab"", ""instantiate"",
                                   ""apply_overrides"", ""revert_overrides"",
                                   ""unpack"", ""create_variant"", ""list_overrides""],
                        ""description"": ""The prefab operation to perform.""
                    },
                    ""source_path"": {
                        ""type"": ""string"",
                        ""description"": ""Hierarchy path of the scene GameObject to save as a prefab. Used by create_prefab.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for the new .prefab file. Used by create_prefab, create_variant.""
                    },
                    ""instance_path"": {
                        ""type"": ""string"",
                        ""description"": ""Hierarchy path of the prefab instance in the scene. Used by apply_overrides, revert_overrides, unpack, list_overrides.""
                    },
                    ""base_prefab"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path of the base prefab for create_variant.""
                    },
                    ""prefab_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path of the prefab to instantiate. Used by instantiate.""
                    },
                    ""parent"": {
                        ""type"": ""string"",
                        ""description"": ""Parent GameObject path for the instantiated prefab. Used by instantiate.""
                    },
                    ""position"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""World position [x,y,z] for the instantiated prefab.""
                    },
                    ""rotation_euler"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Euler angles [x,y,z] in degrees for the instantiated prefab.""
                    },
                    ""name"": {
                        ""type"": ""string"",
                        ""description"": ""Name override for the instantiated prefab GameObject.""
                    },
                    ""scope"": {
                        ""type"": ""string"",
                        ""enum"": [""all""],
                        ""description"": ""Override scope for apply/revert. Currently only 'all' is supported.""
                    },
                    ""mode"": {
                        ""type"": ""string"",
                        ""enum"": [""outermost"", ""completely""],
                        ""description"": ""Unpack mode. 'outermost' unpacks one level; 'completely' unpacks all nested prefabs.""
                    },
                    ""overrides"": {
                        ""type"": ""array"",
                        ""items"": {
                            ""type"": ""object"",
                            ""properties"": {
                                ""type"": { ""type"": ""string"" },
                                ""properties"": { ""type"": ""object"" }
                            }
                        },
                        ""description"": ""Component overrides to apply when creating a variant.""
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
                name: "prefab_op",
                description: "Manage prefab assets and instances in the Unity Editor. "
                    + "Operations: create_prefab (scene GO → .prefab asset), "
                    + "instantiate (place prefab in scene), "
                    + "apply_overrides (push instance changes to prefab asset), "
                    + "revert_overrides (discard instance overrides), "
                    + "unpack (disconnect instance from prefab), "
                    + "create_variant (new prefab variant from base), "
                    + "list_overrides (read-only: show instance modifications). "
                    + "All mutations are undoable. Supports dry_run.",
                inputSchema: s_inputSchema,
                group: ToolGroup.DirectorPrefab,
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
                    "Provide {\"operation\": \"instantiate\", \"prefab_path\": \"Assets/...\", ...}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: create_prefab, instantiate, apply_overrides, "
                    + "revert_overrides, unpack, create_variant, list_overrides");
            }

            try
            {
                return operation switch
                {
                    "create_prefab"     => PrefabOpHandlers.CreatePrefab(args),
                    "instantiate"       => PrefabOpHandlers.Instantiate(args),
                    "apply_overrides"   => PrefabOpHandlers.ApplyOverrides(args),
                    "revert_overrides"  => PrefabOpHandlers.RevertOverrides(args),
                    "unpack"            => PrefabOpHandlers.Unpack(args),
                    "create_variant"    => PrefabOpHandlers.CreateVariant(args),
                    "list_overrides"    => PrefabOpHandlers.ListOverrides(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create_prefab, instantiate, apply_overrides, "
                        + "revert_overrides, unpack, create_variant, list_overrides")
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] prefab_op:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"prefab_op:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }
    }
}
