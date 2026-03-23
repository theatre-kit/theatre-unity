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

        private static string Execute(JToken arguments) =>
            CompoundToolDispatcher.Execute(
                "sprite_atlas_op",
                arguments,
                (args, operation) => operation switch
                {
                    "create"         => Create(args),
                    "add_entries"    => AddEntries(args),
                    "remove_entries" => RemoveEntries(args),
                    "pack"           => Pack(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create, add_entries, remove_entries, pack")
                },
                "create, add_entries, remove_entries, pack");

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

            DirectorHelpers.EnsureParentDirectory(assetPath);
            AssetDatabase.CreateAsset(atlas, assetPath);
            Undo.RegisterCreatedObjectUndo(atlas, "Theatre sprite_atlas_op:create");

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create";
            response["asset_path"] = assetPath;
            response["include_in_build"] = includeInBuild;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Add asset entries (sprites or folders) to an existing atlas.</summary>
        internal static string AddEntries(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<SpriteAtlas>(
                args, out var atlas, out var assetPath, ".spriteatlas");
            if (loadError != null) return loadError;

            var entriesArr = args["entries"] as JArray;
            if (entriesArr == null || entriesArr.Count == 0)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'entries' parameter",
                    "Provide an array of asset paths to add to the atlas");

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
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Remove asset entries from an existing atlas.</summary>
        internal static string RemoveEntries(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<SpriteAtlas>(
                args, out var atlas, out var assetPath, ".spriteatlas");
            if (loadError != null) return loadError;

            var entriesArr = args["entries"] as JArray;
            if (entriesArr == null || entriesArr.Count == 0)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'entries' parameter",
                    "Provide an array of asset paths to remove from the atlas");

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
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Pack all sprites in a Sprite Atlas.</summary>
        internal static string Pack(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<SpriteAtlas>(
                args, out var atlas, out var assetPath, ".spriteatlas");
            if (loadError != null) return loadError;

            SpriteAtlasUtility.PackAtlases(
                new[] { atlas },
                EditorUserBuildSettings.activeBuildTarget);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "pack";
            response["asset_path"] = assetPath;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        // --- Helpers ---

    }
}
