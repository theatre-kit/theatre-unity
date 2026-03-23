#if THEATRE_HAS_ADDRESSABLES
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using Theatre.Editor.Tools.Director.Shared;

namespace Theatre.Editor.Tools.Director.Assets
{
    /// <summary>
    /// MCP tool: addressable_op
    /// Compound tool for managing Unity Addressables groups and entries.
    /// Operations: create_group, add_entry, remove_entry, set_labels, list_groups, analyze.
    /// Only registered when the Addressables package is installed.
    /// </summary>
    public static class AddressableOpTool
    {
        private static readonly JToken s_inputSchema;

        static AddressableOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create_group"", ""add_entry"", ""remove_entry"", ""set_labels"", ""list_groups"", ""analyze""],
                        ""description"": ""The Addressables operation to perform.""
                    },
                    ""name"": {
                        ""type"": ""string"",
                        ""description"": ""Group name for create_group.""
                    },
                    ""group"": {
                        ""type"": ""string"",
                        ""description"": ""Group name for add_entry.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for add_entry, remove_entry, or set_labels.""
                    },
                    ""address"": {
                        ""type"": ""string"",
                        ""description"": ""Addressable address override. Defaults to asset_path.""
                    },
                    ""labels"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Labels to apply to the entry.""
                    },
                    ""replace"": {
                        ""type"": ""boolean"",
                        ""description"": ""If true (default), replace existing labels. If false, add to existing.""
                    },
                    ""packing_mode"": {
                        ""type"": ""string"",
                        ""enum"": [""pack_together"", ""pack_separately"", ""pack_together_by_label""],
                        ""description"": ""Packing mode for create_group.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "addressable_op",
                description: "Manage Unity Addressable Asset System groups and entries. "
                    + "Operations: create_group, add_entry, remove_entry, set_labels, list_groups, analyze. "
                    + "Requires Addressables package.",
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
                "addressable_op",
                arguments,
                (args, operation) => operation switch
                {
                    "create_group"  => CreateGroup(args),
                    "add_entry"     => AddEntry(args),
                    "remove_entry"  => RemoveEntry(args),
                    "set_labels"    => SetLabels(args),
                    "list_groups"   => ListGroups(args),
                    "analyze"       => Analyze(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create_group, add_entry, remove_entry, set_labels, list_groups, analyze")
                },
                "create_group, add_entry, remove_entry, set_labels, list_groups, analyze");

        /// <summary>Create a new Addressables group.</summary>
        internal static string CreateGroup(JObject args)
        {
            var groupName = args["name"]?.Value<string>();
            if (string.IsNullOrEmpty(groupName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'name' parameter",
                    "Provide a group name such as 'RemoteAssets'");

            var settings = GetOrCreateSettings();
            if (settings == null)
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    "Failed to get or create AddressableAssetSettings",
                    "Ensure the Addressables package is properly initialized");

            // Check if group already exists
            var existing = settings.FindGroup(groupName);
            if (existing != null)
                return ResponseHelpers.ErrorResponse(
                    "asset_exists",
                    $"Addressables group '{groupName}' already exists",
                    "Choose a different name or use add_entry to add entries to the existing group");

            // Create with default schemas
            var schemas = new List<AddressableAssetGroupSchema>
            {
                ScriptableObject.CreateInstance<BundledAssetGroupSchema>(),
                ScriptableObject.CreateInstance<ContentUpdateGroupSchema>()
            };

            // Apply packing mode if specified
            var packingMode = args["packing_mode"]?.Value<string>();
            if (!string.IsNullOrEmpty(packingMode))
            {
                var bundleSchema = schemas[0] as BundledAssetGroupSchema;
                if (bundleSchema != null)
                {
                    switch (packingMode)
                    {
                        case "pack_separately":
                            bundleSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
                            break;
                        case "pack_together_by_label":
                            bundleSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel;
                            break;
                        default: // "pack_together"
                            bundleSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
                            break;
                    }
                }
            }

            var group = settings.CreateGroup(groupName, false, false, true, schemas);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create_group";
            response["name"] = group.Name;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Add or move an asset entry into an Addressables group.</summary>
        internal static string AddEntry(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath);
            if (pathError != null) return pathError;

            var groupName = args["group"]?.Value<string>();
            if (string.IsNullOrEmpty(groupName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'group' parameter",
                    "Provide the name of an existing Addressables group");

            var settings = GetOrCreateSettings();
            if (settings == null)
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    "Failed to get AddressableAssetSettings",
                    "Ensure the Addressables package is properly initialized");

            var group = settings.FindGroup(groupName);
            if (group == null)
                return ResponseHelpers.ErrorResponse(
                    "not_found",
                    $"Addressables group '{groupName}' not found",
                    "Create the group first with create_group, or check the name is correct");

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Asset not found at '{assetPath}'",
                    "Check the asset path is correct");

            var entry = settings.CreateOrMoveEntry(guid, group, false, false);
            if (entry == null)
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"Failed to create addressable entry for '{assetPath}'",
                    "Check the Unity Console for details");

            var address = args["address"]?.Value<string>();
            if (!string.IsNullOrEmpty(address))
                entry.SetAddress(address);

            var labels = args["labels"] as JArray;
            if (labels != null)
            {
                foreach (var label in labels)
                    entry.SetLabel(label.Value<string>(), true);
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryAdded, entry, true);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_entry";
            response["asset_path"] = assetPath;
            response["address"] = entry.address;
            response["group"] = groupName;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Remove an asset entry from Addressables.</summary>
        internal static string RemoveEntry(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath);
            if (pathError != null) return pathError;

            var settings = GetOrCreateSettings();
            if (settings == null)
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    "Failed to get AddressableAssetSettings",
                    "Ensure the Addressables package is properly initialized");

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Asset not found at '{assetPath}'",
                    "Check the asset path is correct");

            var entry = settings.FindAssetEntry(guid);
            if (entry == null)
                return ResponseHelpers.ErrorResponse(
                    "not_found",
                    $"Asset '{assetPath}' is not registered as an Addressable entry",
                    "The asset must be added with add_entry before it can be removed");

            settings.RemoveAssetEntry(guid);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "remove_entry";
            response["asset_path"] = assetPath;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Set or update labels on an existing Addressables entry.</summary>
        internal static string SetLabels(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath);
            if (pathError != null) return pathError;

            var labels = args["labels"] as JArray;
            if (labels == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'labels' parameter",
                    "Provide an array of label strings");

            var settings = GetOrCreateSettings();
            if (settings == null)
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    "Failed to get AddressableAssetSettings",
                    "Ensure the Addressables package is properly initialized");

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var entry = settings.FindAssetEntry(guid);
            if (entry == null)
                return ResponseHelpers.ErrorResponse(
                    "not_found",
                    $"Asset '{assetPath}' is not registered as an Addressable entry",
                    "Add the asset with add_entry first");

            var replace = args["replace"]?.Value<bool>() ?? true;

            // If replacing, clear existing labels first
            if (replace)
            {
                var existingLabels = new List<string>(entry.labels);
                foreach (var label in existingLabels)
                    entry.SetLabel(label, false);
            }

            // Apply new labels
            foreach (var label in labels)
                entry.SetLabel(label.Value<string>(), true);

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_labels";
            response["asset_path"] = assetPath;
            var labelsArray = new JArray();
            foreach (var label in entry.labels)
                labelsArray.Add(label);
            response["labels"] = labelsArray;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>List all Addressables groups and their entries.</summary>
        internal static string ListGroups(JObject args)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                var response = new JObject();
                response["result"] = "ok";
                response["operation"] = "list_groups";
                response["groups"] = new JArray();
                response["message"] = "Addressables not initialized. Use create_group to initialize.";
                ResponseHelpers.AddFrameContext(response);
                return response.ToString(Formatting.None);
            }

            var groupsArray = new JArray();
            foreach (var group in settings.groups)
            {
                if (group == null) continue;

                var groupObj = new JObject();
                groupObj["name"] = group.Name;
                groupObj["entry_count"] = group.entries.Count;

                var schemasArray = new JArray();
                foreach (var schema in group.Schemas)
                {
                    if (schema != null)
                        schemasArray.Add(schema.GetType().Name);
                }
                groupObj["schemas"] = schemasArray;

                groupsArray.Add(groupObj);
            }

            var result = new JObject();
            result["result"] = "ok";
            result["operation"] = "list_groups";
            result["groups"] = groupsArray;
            ResponseHelpers.AddFrameContext(result);
            return result.ToString(Formatting.None);
        }

        /// <summary>Analyze Addressables configuration (stub — full analysis requires async API).</summary>
        internal static string Analyze(JObject args)
        {
            return ResponseHelpers.ErrorResponse(
                "internal_api_unavailable",
                "Addressables analysis requires asynchronous execution not supported in this context",
                "Run analysis manually via Window > Asset Management > Addressables > Analyze");
        }

        // --- Helpers ---

        private static AddressableAssetSettings GetOrCreateSettings()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            return settings;
        }
    }
}
#endif
