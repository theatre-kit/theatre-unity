using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// Handlers for all prefab_op operations.
    /// Each method is internal and called by PrefabOpTool's dispatcher.
    /// </summary>
    internal static class PrefabOpHandlers
    {
        // -----------------------------------------------------------------------
        // create_prefab
        // -----------------------------------------------------------------------

        public static string CreatePrefab(JObject args)
        {
            var sourcePath = args["source_path"]?.Value<string>();
            var assetPath = args["asset_path"]?.Value<string>();

            if (string.IsNullOrEmpty(sourcePath))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'source_path' parameter",
                    "Provide the hierarchy path of the scene GameObject to save as a prefab");

            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".prefab");
            if (pathError != null) return pathError;

#if UNITY_EDITOR
            // Dry run
            var dryRun = DirectorHelpers.CheckDryRun(args, () =>
            {
                var errors = new List<string>();
                var r = ObjectResolver.Resolve(sourcePath);
                if (!r.Success) errors.Add($"Source '{sourcePath}' not found");
                var conflict = DirectorHelpers.CheckAssetConflict(assetPath);
                if (conflict != null) errors.Add($"Asset already exists at '{assetPath}'");
                return (errors.Count == 0, errors);
            });
            if (dryRun != null) return dryRun;

            var resolved = ObjectResolver.Resolve(sourcePath);
            if (!resolved.Success)
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);

            var conflictError = DirectorHelpers.CheckAssetConflict(assetPath);
            if (conflictError != null) return conflictError;

            // Ensure directory exists
            var dir = System.IO.Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(resolved.GameObject, assetPath, out bool success);
            if (!success || prefab == null)
                return ResponseHelpers.ErrorResponse(
                    "create_prefab_failed",
                    $"Failed to save '{sourcePath}' as prefab at '{assetPath}'",
                    "Check the asset path and ensure the directory exists");

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create_prefab";
            response["source_path"] = sourcePath;
            response["asset_path"] = assetPath;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "editor_only",
                "create_prefab requires the Unity Editor",
                "This operation is only available in the Unity Editor");
#endif
        }

        // -----------------------------------------------------------------------
        // instantiate
        // -----------------------------------------------------------------------

        public static string Instantiate(JObject args)
        {
            var prefabPath = args["prefab_path"]?.Value<string>();
            if (string.IsNullOrEmpty(prefabPath))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'prefab_path' parameter",
                    "Provide the asset path to the .prefab file");

#if UNITY_EDITOR
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
                return ResponseHelpers.ErrorResponse(
                    "prefab_not_found",
                    $"No prefab found at '{prefabPath}'",
                    "Check the asset path is correct and the prefab exists");

            var instance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
            if (instance == null)
                return ResponseHelpers.ErrorResponse(
                    "instantiate_failed",
                    $"Failed to instantiate prefab '{prefabPath}'",
                    "Check that the asset is a valid prefab");

            Undo.RegisterCreatedObjectUndo(instance, "Theatre Instantiate");

            // Parent
            var parentPath = args["parent"]?.Value<string>();
            if (!string.IsNullOrEmpty(parentPath))
            {
                var parentResolve = ObjectResolver.Resolve(parentPath);
                if (!parentResolve.Success)
                {
                    Undo.DestroyObjectImmediate(instance);
                    return ResponseHelpers.ErrorResponse(
                        parentResolve.ErrorCode,
                        parentResolve.ErrorMessage,
                        parentResolve.Suggestion);
                }
                instance.transform.SetParent(parentResolve.GameObject.transform, true);
            }

            // Position/rotation
            var pos = JsonParamParser.ParseVector3(args, "position");
            if (pos.HasValue) instance.transform.position = pos.Value;

            var rot = JsonParamParser.ParseVector3(args, "rotation_euler");
            if (rot.HasValue) instance.transform.eulerAngles = rot.Value;

            // Name override
            var name = args["name"]?.Value<string>();
            if (!string.IsNullOrEmpty(name))
                instance.name = name;

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "instantiate";
            ResponseHelpers.AddIdentity(response, instance);
            response["prefab_path"] = prefabPath;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "editor_only",
                "instantiate requires the Unity Editor",
                "This operation is only available in the Unity Editor");
#endif
        }

        // -----------------------------------------------------------------------
        // apply_overrides
        // -----------------------------------------------------------------------

        public static string ApplyOverrides(JObject args)
        {
            var instancePath = args["instance_path"]?.Value<string>();
            if (string.IsNullOrEmpty(instancePath))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'instance_path' parameter",
                    "Provide the hierarchy path of the prefab instance");

#if UNITY_EDITOR
            var resolved = ObjectResolver.Resolve(instancePath);
            if (!resolved.Success)
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);

            var go = resolved.GameObject;
            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return ResponseHelpers.ErrorResponse(
                    "not_prefab_instance",
                    $"'{instancePath}' is not part of a prefab instance",
                    "Only GameObjects that are prefab instances can have overrides applied");

            var scope = args["scope"]?.Value<string>() ?? "all";
            if (scope != "all")
                return ResponseHelpers.ErrorResponse(
                    "unsupported_scope",
                    $"Scope '{scope}' is not yet supported. Only 'all' is available.",
                    "Use scope 'all' to apply all overrides to the prefab asset");

            PrefabUtility.ApplyPrefabInstance(go, InteractionMode.UserAction);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "apply_overrides";
            ResponseHelpers.AddIdentity(response, go);
            response["scope"] = scope;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "editor_only",
                "apply_overrides requires the Unity Editor",
                "This operation is only available in the Unity Editor");
#endif
        }

        // -----------------------------------------------------------------------
        // revert_overrides
        // -----------------------------------------------------------------------

        public static string RevertOverrides(JObject args)
        {
            var instancePath = args["instance_path"]?.Value<string>();
            if (string.IsNullOrEmpty(instancePath))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'instance_path' parameter",
                    "Provide the hierarchy path of the prefab instance");

#if UNITY_EDITOR
            var resolved = ObjectResolver.Resolve(instancePath);
            if (!resolved.Success)
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);

            var go = resolved.GameObject;
            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return ResponseHelpers.ErrorResponse(
                    "not_prefab_instance",
                    $"'{instancePath}' is not part of a prefab instance",
                    "Only GameObjects that are prefab instances can have overrides reverted");

            var scope = args["scope"]?.Value<string>() ?? "all";
            if (scope != "all")
                return ResponseHelpers.ErrorResponse(
                    "unsupported_scope",
                    $"Scope '{scope}' is not yet supported. Only 'all' is available.",
                    "Use scope 'all' to revert all overrides");

            PrefabUtility.RevertPrefabInstance(go, InteractionMode.UserAction);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "revert_overrides";
            ResponseHelpers.AddIdentity(response, go);
            response["scope"] = scope;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "editor_only",
                "revert_overrides requires the Unity Editor",
                "This operation is only available in the Unity Editor");
#endif
        }

        // -----------------------------------------------------------------------
        // unpack
        // -----------------------------------------------------------------------

        public static string Unpack(JObject args)
        {
            var instancePath = args["instance_path"]?.Value<string>();
            if (string.IsNullOrEmpty(instancePath))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'instance_path' parameter",
                    "Provide the hierarchy path of the prefab instance to unpack");

#if UNITY_EDITOR
            var resolved = ObjectResolver.Resolve(instancePath);
            if (!resolved.Success)
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);

            var go = resolved.GameObject;
            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return ResponseHelpers.ErrorResponse(
                    "not_prefab_instance",
                    $"'{instancePath}' is not part of a prefab instance",
                    "Only prefab instances can be unpacked");

            var modeStr = args["mode"]?.Value<string>() ?? "outermost";
            PrefabUnpackMode unpackMode = modeStr == "completely"
                ? PrefabUnpackMode.Completely
                : PrefabUnpackMode.OutermostRoot;

            PrefabUtility.UnpackPrefabInstance(go, unpackMode, InteractionMode.UserAction);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "unpack";
            ResponseHelpers.AddIdentity(response, go);
            response["mode"] = modeStr;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "editor_only",
                "unpack requires the Unity Editor",
                "This operation is only available in the Unity Editor");
#endif
        }

        // -----------------------------------------------------------------------
        // create_variant
        // -----------------------------------------------------------------------

        public static string CreateVariant(JObject args)
        {
            var basePrefabPath = args["base_prefab"]?.Value<string>();
            var assetPath = args["asset_path"]?.Value<string>();

            if (string.IsNullOrEmpty(basePrefabPath))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'base_prefab' parameter",
                    "Provide the asset path of the base prefab");

            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".prefab");
            if (pathError != null) return pathError;

#if UNITY_EDITOR
            var dryRun = DirectorHelpers.CheckDryRun(args, () =>
            {
                var errors = new List<string>();
                var basePrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
                if (basePrefabAsset == null)
                    errors.Add($"Base prefab '{basePrefabPath}' not found");
                var conflict = DirectorHelpers.CheckAssetConflict(assetPath);
                if (conflict != null) errors.Add($"Asset already exists at '{assetPath}'");
                return (errors.Count == 0, errors);
            });
            if (dryRun != null) return dryRun;

            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
            if (basePrefab == null)
                return ResponseHelpers.ErrorResponse(
                    "prefab_not_found",
                    $"No prefab found at '{basePrefabPath}'",
                    "Check the base_prefab path is correct");

            var conflictError = DirectorHelpers.CheckAssetConflict(assetPath);
            if (conflictError != null) return conflictError;

            // Ensure directory exists
            var dir = System.IO.Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            // Instantiate base temporarily
            var tempInstance = PrefabUtility.InstantiatePrefab(basePrefab) as GameObject;
            if (tempInstance == null)
                return ResponseHelpers.ErrorResponse(
                    "instantiate_failed",
                    $"Failed to instantiate base prefab '{basePrefabPath}'",
                    "Check the base prefab is valid");

            // Apply component overrides if specified
            var overrides = args["overrides"] as JArray;
            if (overrides != null)
            {
                var overrideErrors = new List<string>();
                foreach (var overrideToken in overrides)
                {
                    var typeName = overrideToken["type"]?.Value<string>();
                    if (string.IsNullOrEmpty(typeName)) continue;

                    var compType = DirectorHelpers.ResolveComponentType(typeName, out var typeError);
                    if (compType == null)
                    {
                        overrideErrors.Add($"Component type '{typeName}' not found");
                        continue;
                    }

                    var comp = tempInstance.GetComponent(compType);
                    if (comp == null) comp = tempInstance.AddComponent(compType);

                    var compProperties = overrideToken["properties"] as JObject;
                    if (compProperties != null && compProperties.HasValues)
                        DirectorHelpers.SetProperties(comp, compProperties);
                }
            }

            // Save as new prefab (variant)
            var variant = PrefabUtility.SaveAsPrefabAsset(tempInstance, assetPath, out bool success);
            UnityEngine.Object.DestroyImmediate(tempInstance);

            if (!success || variant == null)
                return ResponseHelpers.ErrorResponse(
                    "create_variant_failed",
                    $"Failed to save prefab variant at '{assetPath}'",
                    "Check the asset path and ensure the directory exists");

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create_variant";
            response["base_prefab"] = basePrefabPath;
            response["asset_path"] = assetPath;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "editor_only",
                "create_variant requires the Unity Editor",
                "This operation is only available in the Unity Editor");
#endif
        }

        // -----------------------------------------------------------------------
        // list_overrides
        // -----------------------------------------------------------------------

        public static string ListOverrides(JObject args)
        {
            var instancePath = args["instance_path"]?.Value<string>();
            if (string.IsNullOrEmpty(instancePath))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'instance_path' parameter",
                    "Provide the hierarchy path of the prefab instance");

#if UNITY_EDITOR
            var resolved = ObjectResolver.Resolve(instancePath);
            if (!resolved.Success)
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);

            var go = resolved.GameObject;
            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return ResponseHelpers.ErrorResponse(
                    "not_prefab_instance",
                    $"'{instancePath}' is not part of a prefab instance",
                    "Only prefab instances have overrides to list");

            // Property modifications
            var propertyMods = PrefabUtility.GetPropertyModifications(go);
            var propModArray = new JArray();
            if (propertyMods != null)
            {
                foreach (var mod in propertyMods)
                {
                    // Skip internal Unity-managed properties
                    if (mod.propertyPath.StartsWith("m_ObjectHideFlags") ||
                        mod.propertyPath.StartsWith("m_Father") ||
                        mod.propertyPath.StartsWith("m_RootOrder"))
                        continue;

                    var entry = new JObject();
                    entry["property_path"] = mod.propertyPath;
                    entry["value"] = mod.value;
                    entry["target_type"] = mod.target?.GetType()?.Name ?? "unknown";
                    propModArray.Add(entry);
                }
            }

            // Added components
            var addedComponents = PrefabUtility.GetAddedComponents(go);
            var addedCompArray = new JArray();
            if (addedComponents != null)
            {
                foreach (var ac in addedComponents)
                {
                    var entry = new JObject();
                    entry["component_type"] = ac.instanceComponent?.GetType()?.Name ?? "unknown";
                    addedCompArray.Add(entry);
                }
            }

            // Added GameObjects (children added to the instance)
            var addedObjects = PrefabUtility.GetAddedGameObjects(go);
            var addedObjArray = new JArray();
            if (addedObjects != null)
            {
                foreach (var ao in addedObjects)
                {
                    var entry = new JObject();
                    entry["name"] = ao.instanceGameObject?.name ?? "unknown";
                    entry["path"] = ao.instanceGameObject != null
                        ? ResponseHelpers.GetHierarchyPath(ao.instanceGameObject.transform)
                        : null;
                    addedObjArray.Add(entry);
                }
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "list_overrides";
            ResponseHelpers.AddIdentity(response, go);
            response["property_modifications"] = propModArray;
            response["added_components"] = addedCompArray;
            response["added_gameobjects"] = addedObjArray;
            response["total_overrides"] = propModArray.Count + addedCompArray.Count + addedObjArray.Count;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "editor_only",
                "list_overrides requires the Unity Editor",
                "This operation is only available in the Unity Editor");
#endif
        }
    }
}
