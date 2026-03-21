using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEngine.U2D;
using UnityEditor;
using UnityEditor.U2D;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: sprite_atlas_op
    /// Compound tool for creating and managing Sprite Atlas assets.
    /// Operations: create, add_entries, remove_entries, pack.
    /// </summary>
    public static class SpriteAtlasOpTool
    {
        private static readonly JToken s_inputSchema;

        static SpriteAtlasOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create"", ""add_entries"", ""remove_entries"", ""pack""],
                        ""description"": ""The sprite atlas operation to perform.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for the sprite atlas (must end in .spriteatlas).""
                    },
                    ""include_in_build"": {
                        ""type"": ""boolean"",
                        ""description"": ""Whether to include the atlas in the build (default true).""
                    },
                    ""packing_settings"": {
                        ""type"": ""object"",
                        ""description"": ""Packing settings: padding (int), enable_rotation (bool), enable_tight_packing (bool).""
                    },
                    ""entries"": {
                        ""type"": ""array"",
                        ""description"": ""Array of asset paths (sprites or folders) to add/remove.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "sprite_atlas_op",
                description: "Create and manage Sprite Atlas assets in the Unity Editor. "
                    + "Operations: create, add_entries, remove_entries, pack. "
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
                    "Provide {\"operation\": \"create\", \"asset_path\": \"Assets/Atlases/MyAtlas.spriteatlas\"}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: create, add_entries, remove_entries, pack");
            }

            try
            {
                return operation switch
                {
                    "create"         => Create(args),
                    "add_entries"    => AddEntries(args),
                    "remove_entries" => RemoveEntries(args),
                    "pack"           => Pack(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create, add_entries, remove_entries, pack")
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] sprite_atlas_op:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"sprite_atlas_op:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }

        /// <summary>Create a new Sprite Atlas asset at the given path.</summary>
        internal static string Create(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".spriteatlas");
            if (pathError != null) return pathError;

            var conflictError = DirectorHelpers.CheckAssetConflict(assetPath);
            if (conflictError != null) return conflictError;

            var atlas = new SpriteAtlas();

            var includeInBuild = args["include_in_build"]?.Value<bool>() ?? true;
            atlas.SetIncludeInBuild(includeInBuild);

            var packingSettingsObj = args["packing_settings"] as JObject;
            if (packingSettingsObj != null)
            {
                var packSettings = atlas.GetPackingSettings();
                if (packingSettingsObj["padding"] != null)
                    packSettings.padding = packingSettingsObj["padding"].Value<int>();
                if (packingSettingsObj["enable_rotation"] != null)
                    packSettings.enableRotation = packingSettingsObj["enable_rotation"].Value<bool>();
                if (packingSettingsObj["enable_tight_packing"] != null)
                    packSettings.enableTightPacking = packingSettingsObj["enable_tight_packing"].Value<bool>();
                atlas.SetPackingSettings(packSettings);
            }

            EnsureParentDirectory(assetPath);
            AssetDatabase.CreateAsset(atlas, assetPath);
            Undo.RegisterCreatedObjectUndo(atlas, "Theatre sprite_atlas_op:create");

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create";
            response["asset_path"] = assetPath;
            response["include_in_build"] = includeInBuild;
            return response.ToString(Formatting.None);
        }

        /// <summary>Add asset entries (sprites or folders) to an existing atlas.</summary>
        internal static string AddEntries(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath);
            if (pathError != null) return pathError;

            var entriesArr = args["entries"] as JArray;
            if (entriesArr == null || entriesArr.Count == 0)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'entries' parameter",
                    "Provide an array of asset paths to add to the atlas");

            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(assetPath);
            if (atlas == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Sprite Atlas not found at '{assetPath}'",
                    "Check the asset path is correct and ends with .spriteatlas");

            var objects = new List<UnityEngine.Object>();
            var notFound = new List<string>();

            foreach (var entryToken in entriesArr)
            {
                var entryPath = entryToken.Value<string>();
                if (string.IsNullOrEmpty(entryPath)) continue;

                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(entryPath);
                if (obj != null)
                    objects.Add(obj);
                else
                    notFound.Add(entryPath);
            }

            if (objects.Count > 0)
            {
                atlas.Add(objects.ToArray());
                EditorUtility.SetDirty(atlas);
                AssetDatabase.SaveAssets();
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_entries";
            response["asset_path"] = assetPath;
            response["added_count"] = objects.Count;
            if (notFound.Count > 0)
            {
                var notFoundArr = new JArray();
                foreach (var p in notFound) notFoundArr.Add(p);
                response["not_found"] = notFoundArr;
            }
            return response.ToString(Formatting.None);
        }

        /// <summary>Remove asset entries from an existing atlas.</summary>
        internal static string RemoveEntries(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath);
            if (pathError != null) return pathError;

            var entriesArr = args["entries"] as JArray;
            if (entriesArr == null || entriesArr.Count == 0)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'entries' parameter",
                    "Provide an array of asset paths to remove from the atlas");

            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(assetPath);
            if (atlas == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Sprite Atlas not found at '{assetPath}'",
                    "Check the asset path is correct and ends with .spriteatlas");

            var objects = new List<UnityEngine.Object>();

            foreach (var entryToken in entriesArr)
            {
                var entryPath = entryToken.Value<string>();
                if (string.IsNullOrEmpty(entryPath)) continue;

                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(entryPath);
                if (obj != null)
                    objects.Add(obj);
            }

            if (objects.Count > 0)
            {
                atlas.Remove(objects.ToArray());
                EditorUtility.SetDirty(atlas);
                AssetDatabase.SaveAssets();
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "remove_entries";
            response["asset_path"] = assetPath;
            response["removed_count"] = objects.Count;
            return response.ToString(Formatting.None);
        }

        /// <summary>Pack all sprites in a Sprite Atlas.</summary>
        internal static string Pack(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath);
            if (pathError != null) return pathError;

            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(assetPath);
            if (atlas == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Sprite Atlas not found at '{assetPath}'",
                    "Check the asset path is correct and ends with .spriteatlas");

            SpriteAtlasUtility.PackAtlases(
                new[] { atlas },
                EditorUserBuildSettings.activeBuildTarget);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "pack";
            response["asset_path"] = assetPath;
            return response.ToString(Formatting.None);
        }

        // --- Helpers ---

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
