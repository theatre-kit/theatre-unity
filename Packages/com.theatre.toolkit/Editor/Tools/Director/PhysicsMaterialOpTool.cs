using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEditor;
using Physics2D = UnityEngine.Physics2D;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: physics_material_op
    /// Compound tool for creating and modifying PhysicMaterial (3D) and
    /// PhysicsMaterial2D (2D) assets in the Unity Editor.
    /// Operations: create, set_properties.
    /// </summary>
    public static class PhysicsMaterialOpTool
    {
        private static readonly JToken s_inputSchema;

        static PhysicsMaterialOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create"", ""set_properties""],
                        ""description"": ""The physics material operation to perform.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for the physics material.""
                    },
                    ""physics"": {
                        ""type"": ""string"",
                        ""enum"": [""3d"", ""2d""],
                        ""description"": ""Physics mode: '3d' (PhysicMaterial) or '2d' (PhysicsMaterial2D). Default: '3d'.""
                    },
                    ""friction"": {
                        ""type"": ""number"",
                        ""description"": ""Dynamic friction (3D) or friction (2D).""
                    },
                    ""static_friction"": {
                        ""type"": ""number"",
                        ""description"": ""Static friction (3D only).""
                    },
                    ""bounciness"": {
                        ""type"": ""number"",
                        ""description"": ""Bounciness (restitution).""
                    },
                    ""friction_combine"": {
                        ""type"": ""string"",
                        ""enum"": [""average"", ""minimum"", ""maximum"", ""multiply""],
                        ""description"": ""Friction combine mode (3D only).""
                    },
                    ""bounce_combine"": {
                        ""type"": ""string"",
                        ""enum"": [""average"", ""minimum"", ""maximum"", ""multiply""],
                        ""description"": ""Bounce combine mode (3D only).""
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
                name: "physics_material_op",
                description: "Create and modify PhysicMaterial (3D) and PhysicsMaterial2D (2D) assets. "
                    + "Operations: create, set_properties. "
                    + "All mutations are undoable.",
                inputSchema: s_inputSchema,
                group: ToolGroup.DirectorAsset,
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
                    "Provide {\"operation\": \"create\", \"asset_path\": \"Assets/Mats/Bouncy.physicMaterial\"}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: create, set_properties");
            }

            try
            {
                return operation switch
                {
                    "create"         => Create(args),
                    "set_properties" => SetProperties(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create, set_properties")
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] physics_material_op:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"physics_material_op:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }

        /// <summary>Create a new physics material asset at the given path.</summary>
        internal static string Create(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath);
            if (pathError != null) return pathError;

            var physicsMode = args["physics"]?.Value<string>() ?? "3d";

            // Dry run
            var dryRun = DirectorHelpers.CheckDryRun(args, () => (true, new List<string>()));
            if (dryRun != null) return dryRun;

            // Ensure parent directory exists
            EnsureParentDirectory(assetPath);

            if (physicsMode == "2d")
            {
                var mat2d = new PhysicsMaterial2D();
                if (args["friction"] != null)
                    mat2d.friction = args["friction"].Value<float>();
                if (args["bounciness"] != null)
                    mat2d.bounciness = args["bounciness"].Value<float>();

                AssetDatabase.CreateAsset(mat2d, assetPath);
                Undo.RegisterCreatedObjectUndo(mat2d, "Theatre physics_material_op:create");

                var response2d = new JObject();
                response2d["result"] = "ok";
                response2d["operation"] = "create";
                response2d["asset_path"] = assetPath;
                response2d["physics"] = "2d";
                response2d["friction"] = mat2d.friction;
                response2d["bounciness"] = mat2d.bounciness;
                return response2d.ToString(Formatting.None);
            }
            else
            {
                var mat3d = new PhysicsMaterial();
                if (args["friction"] != null)
                    mat3d.dynamicFriction = args["friction"].Value<float>();
                if (args["static_friction"] != null)
                    mat3d.staticFriction = args["static_friction"].Value<float>();
                if (args["bounciness"] != null)
                    mat3d.bounciness = args["bounciness"].Value<float>();
                if (args["friction_combine"] != null)
                    mat3d.frictionCombine = ParseCombineMode(args["friction_combine"].Value<string>());
                if (args["bounce_combine"] != null)
                    mat3d.bounceCombine = ParseCombineMode(args["bounce_combine"].Value<string>());

                AssetDatabase.CreateAsset(mat3d, assetPath);
                Undo.RegisterCreatedObjectUndo(mat3d, "Theatre physics_material_op:create");

                var response3d = new JObject();
                response3d["result"] = "ok";
                response3d["operation"] = "create";
                response3d["asset_path"] = assetPath;
                response3d["physics"] = "3d";
                response3d["friction"] = mat3d.dynamicFriction;
                response3d["static_friction"] = mat3d.staticFriction;
                response3d["bounciness"] = mat3d.bounciness;
                response3d["friction_combine"] = mat3d.frictionCombine.ToString().ToLowerInvariant();
                response3d["bounce_combine"] = mat3d.bounceCombine.ToString().ToLowerInvariant();
                return response3d.ToString(Formatting.None);
            }
        }

        /// <summary>Set properties on an existing physics material asset.</summary>
        internal static string SetProperties(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath);
            if (pathError != null) return pathError;

            // Try to load as 3D first, then 2D
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Physics material not found at '{assetPath}'",
                    "Check the asset path is correct");

            if (asset is PhysicsMaterial mat3d)
            {
                Undo.RecordObject(mat3d, "Theatre physics_material_op:set_properties");

                if (args["friction"] != null)
                    mat3d.dynamicFriction = args["friction"].Value<float>();
                if (args["static_friction"] != null)
                    mat3d.staticFriction = args["static_friction"].Value<float>();
                if (args["bounciness"] != null)
                    mat3d.bounciness = args["bounciness"].Value<float>();
                if (args["friction_combine"] != null)
                    mat3d.frictionCombine = ParseCombineMode(args["friction_combine"].Value<string>());
                if (args["bounce_combine"] != null)
                    mat3d.bounceCombine = ParseCombineMode(args["bounce_combine"].Value<string>());

                EditorUtility.SetDirty(mat3d);

                var response3d = new JObject();
                response3d["result"] = "ok";
                response3d["operation"] = "set_properties";
                response3d["asset_path"] = assetPath;
                response3d["physics"] = "3d";
                response3d["friction"] = mat3d.dynamicFriction;
                response3d["static_friction"] = mat3d.staticFriction;
                response3d["bounciness"] = mat3d.bounciness;
                response3d["friction_combine"] = mat3d.frictionCombine.ToString().ToLowerInvariant();
                response3d["bounce_combine"] = mat3d.bounceCombine.ToString().ToLowerInvariant();
                return response3d.ToString(Formatting.None);
            }
            else if (asset is PhysicsMaterial2D mat2d)
            {
                Undo.RecordObject(mat2d, "Theatre physics_material_op:set_properties");

                if (args["friction"] != null)
                    mat2d.friction = args["friction"].Value<float>();
                if (args["bounciness"] != null)
                    mat2d.bounciness = args["bounciness"].Value<float>();

                EditorUtility.SetDirty(mat2d);

                var response2d = new JObject();
                response2d["result"] = "ok";
                response2d["operation"] = "set_properties";
                response2d["asset_path"] = assetPath;
                response2d["physics"] = "2d";
                response2d["friction"] = mat2d.friction;
                response2d["bounciness"] = mat2d.bounciness;
                return response2d.ToString(Formatting.None);
            }
            else
            {
                return ResponseHelpers.ErrorResponse(
                    "asset_type_mismatch",
                    $"Asset at '{assetPath}' is not a PhysicMaterial or PhysicsMaterial2D",
                    "Use the correct asset path for a physics material");
            }
        }

        // --- Helpers ---

        private static PhysicsMaterialCombine ParseCombineMode(string mode)
        {
            return mode?.ToLowerInvariant() switch
            {
                "minimum"  => PhysicsMaterialCombine.Minimum,
                "maximum"  => PhysicsMaterialCombine.Maximum,
                "multiply" => PhysicsMaterialCombine.Multiply,
                _          => PhysicsMaterialCombine.Average
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
