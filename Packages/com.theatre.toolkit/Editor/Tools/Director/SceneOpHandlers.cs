using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
using UnityEngine.SceneManagement;
using UScene = UnityEngine.SceneManagement.Scene;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// Handlers for all scene_op operations.
    /// Each method is internal and called by SceneOpTool's dispatcher.
    /// </summary>
    internal static class SceneOpHandlers
    {
        private static readonly Dictionary<string, PrimitiveType> PrimitiveTypeMap =
            new Dictionary<string, PrimitiveType>(StringComparer.OrdinalIgnoreCase)
            {
                ["cube"]     = PrimitiveType.Cube,
                ["sphere"]   = PrimitiveType.Sphere,
                ["capsule"]  = PrimitiveType.Capsule,
                ["cylinder"] = PrimitiveType.Cylinder,
                ["plane"]    = PrimitiveType.Plane,
                ["quad"]     = PrimitiveType.Quad,
            };

        private static PrimitiveType? ResolvePrimitiveType(string name)
        {
            return PrimitiveTypeMap.TryGetValue(name, out var pt) ? pt : (PrimitiveType?)null;
        }

        // -----------------------------------------------------------------------
        // create_scene
        // -----------------------------------------------------------------------

        public static string CreateScene(JObject args)
        {
            var path = args["path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(path, ".unity");
            if (pathError != null) return pathError;

            var template = args["template"]?.Value<string>() ?? "basic_3d";
            var open = args["open"]?.Value<bool>() ?? true;

#if UNITY_EDITOR
            // Dry run
            var dryRun = DirectorHelpers.CheckDryRun(args, () =>
            {
                var errors = new List<string>();
                var conflict = DirectorHelpers.CheckAssetConflict(path);
                if (conflict != null)
                    errors.Add($"Asset already exists at '{path}'");
                return (errors.Count == 0, errors);
            });
            if (dryRun != null) return dryRun;

            var conflictError = DirectorHelpers.CheckAssetConflict(path);
            if (conflictError != null) return conflictError;

            // Determine setup and mode
            NewSceneSetup setup;
            switch (template)
            {
                case "empty":
                    setup = NewSceneSetup.EmptyScene;
                    break;
                case "basic_2d":
                case "basic_3d":
                default:
                    setup = NewSceneSetup.DefaultGameObjects;
                    break;
            }

            // Remember active scene if we need to restore it
            string previousScenePath = null;
            if (!open)
            {
                previousScenePath = SceneManager.GetActiveScene().path;
            }

            // Create the scene
            var scene = EditorSceneManager.NewScene(setup, NewSceneMode.Single);

            // Ensure directory exists
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            // Save the scene
            EditorSceneManager.SaveScene(scene, path);

            var sceneName = scene.name;

            // If not open, reload the previous scene
            if (!open && !string.IsNullOrEmpty(previousScenePath))
            {
                EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create_scene";
            response["path"] = path;
            response["scene"] = sceneName;
            response["opened"] = open;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "editor_only",
                "create_scene requires the Unity Editor",
                "This operation is only available in the Unity Editor");
#endif
        }

        // -----------------------------------------------------------------------
        // load_scene
        // -----------------------------------------------------------------------

        public static string LoadScene(JObject args)
        {
            var path = args["path"]?.Value<string>();
            if (string.IsNullOrEmpty(path))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'path' parameter",
                    "Provide the .unity asset path, e.g. 'Assets/Scenes/Level1.unity'");

            var modeStr = args["mode"]?.Value<string>() ?? "single";

#if UNITY_EDITOR
            OpenSceneMode openMode = modeStr == "additive"
                ? OpenSceneMode.Additive
                : OpenSceneMode.Single;

            var scene = EditorSceneManager.OpenScene(path, openMode);
            if (!scene.IsValid())
                return ResponseHelpers.ErrorResponse(
                    "scene_not_found",
                    $"Could not open scene at '{path}'",
                    "Verify the path exists and is a valid .unity file");

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "load_scene";
            response["path"] = path;
            response["scene"] = scene.name;
            response["mode"] = modeStr;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "editor_only",
                "load_scene requires the Unity Editor",
                "This operation is only available in the Unity Editor");
#endif
        }

        // -----------------------------------------------------------------------
        // unload_scene
        // -----------------------------------------------------------------------

        public static string UnloadScene(JObject args)
        {
            var sceneName = args["scene"]?.Value<string>();
            if (string.IsNullOrEmpty(sceneName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'scene' parameter",
                    "Provide the scene name or path");

#if UNITY_EDITOR
            // Find the scene
            UScene found = default;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.name == sceneName || s.path == sceneName)
                {
                    found = s;
                    break;
                }
            }

            if (!found.IsValid())
                return ResponseHelpers.ErrorResponse(
                    "scene_not_found",
                    $"Scene '{sceneName}' is not loaded",
                    "Use scene_hierarchy to see loaded scenes");

            if (SceneManager.sceneCount <= 1)
                return ResponseHelpers.ErrorResponse(
                    "cannot_unload_last_scene",
                    "Cannot unload the only loaded scene",
                    "Load another scene first before unloading this one");

            EditorSceneManager.CloseScene(found, true);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "unload_scene";
            response["scene"] = sceneName;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "editor_only",
                "unload_scene requires the Unity Editor",
                "This operation is only available in the Unity Editor");
#endif
        }

        // -----------------------------------------------------------------------
        // create_gameobject
        // -----------------------------------------------------------------------

        public static string CreateGameObject(JObject args)
        {
            var name = args["name"]?.Value<string>();
            if (string.IsNullOrEmpty(name))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'name' parameter",
                    "Provide a name for the new GameObject");

            var primitiveTypeStr = args["primitive_type"]?.Value<string>();
            PrimitiveType? primitiveType = null;
            if (!string.IsNullOrEmpty(primitiveTypeStr))
            {
                primitiveType = ResolvePrimitiveType(primitiveTypeStr);
                if (primitiveType == null)
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown primitive_type '{primitiveTypeStr}'",
                        "Valid values: cube, sphere, capsule, cylinder, plane, quad");
            }

            // Dry run
            var dryRun = DirectorHelpers.CheckDryRun(args, () =>
            {
                var errors = new List<string>();
                var parentPath = args["parent"]?.Value<string>();
                if (!string.IsNullOrEmpty(parentPath))
                {
                    var pr = ObjectResolver.Resolve(parentPath);
                    if (!pr.Success)
                        errors.Add($"Parent '{parentPath}' not found");
                }
                if (!string.IsNullOrEmpty(primitiveTypeStr)
                    && ResolvePrimitiveType(primitiveTypeStr) == null)
                    errors.Add($"Unknown primitive_type '{primitiveTypeStr}'");
                var components = args["components"] as JArray;
                if (components != null)
                {
                    foreach (var comp in components)
                    {
                        var typeName = comp["type"]?.Value<string>();
                        if (!string.IsNullOrEmpty(typeName))
                        {
                            DirectorHelpers.ResolveComponentType(typeName, out var typeError);
                            if (typeError != null)
                                errors.Add($"Component type '{typeName}' not found");
                        }
                    }
                }
                return (errors.Count == 0, errors);
            });
            if (dryRun != null) return dryRun;

            // Create the object
            GameObject go;
            if (primitiveType.HasValue)
            {
                go = GameObject.CreatePrimitive(primitiveType.Value);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }
#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(go, "Theatre CreateGameObject");
#endif

            // Parent
            var parentPathStr = args["parent"]?.Value<string>();
            if (!string.IsNullOrEmpty(parentPathStr))
            {
                var parentResolve = ObjectResolver.Resolve(parentPathStr);
                if (!parentResolve.Success)
                {
#if UNITY_EDITOR
                    Undo.DestroyObjectImmediate(go);
#else
                    UnityEngine.Object.DestroyImmediate(go);
#endif
                    return ResponseHelpers.ErrorResponse(
                        parentResolve.ErrorCode,
                        parentResolve.ErrorMessage,
                        parentResolve.Suggestion);
                }
                go.transform.SetParent(parentResolve.GameObject.transform, false);
            }

            // Transform
            var pos = JsonParamParser.ParseVector3(args, "position");
            if (pos.HasValue) go.transform.localPosition = pos.Value;

            var rot = JsonParamParser.ParseVector3(args, "rotation_euler");
            if (rot.HasValue) go.transform.localEulerAngles = rot.Value;

            var scale = JsonParamParser.ParseVector3(args, "scale");
            if (scale.HasValue) go.transform.localScale = scale.Value;

            // Tag
            var tag = args["tag"]?.Value<string>();
            if (!string.IsNullOrEmpty(tag))
            {
                try { go.tag = tag; }
                catch { /* Invalid tag — ignore */ }
            }

            // Layer
            var layerName = args["layer"]?.Value<string>();
            if (!string.IsNullOrEmpty(layerName))
            {
                var layerIdx = LayerMask.NameToLayer(layerName);
                if (layerIdx >= 0)
                    go.layer = layerIdx;
            }

            // Components
            var compErrors = new List<string>();
            var components = args["components"] as JArray;
            if (components != null)
            {
                foreach (var compToken in components)
                {
                    var typeName = compToken["type"]?.Value<string>();
                    if (string.IsNullOrEmpty(typeName))
                    {
                        compErrors.Add("Component entry missing 'type' field");
                        continue;
                    }

                    var compType = DirectorHelpers.ResolveComponentType(typeName, out var typeError);
                    if (compType == null)
                    {
                        compErrors.Add($"Component type '{typeName}' not found");
                        continue;
                    }

                    Component comp;
#if UNITY_EDITOR
                    comp = Undo.AddComponent(go, compType);
#else
                    comp = go.AddComponent(compType);
#endif
                    if (comp == null)
                    {
                        compErrors.Add($"Failed to add component '{typeName}'");
                        continue;
                    }

                    var compProperties = compToken["properties"] as JObject;
#if UNITY_EDITOR
                    if (compProperties != null && compProperties.HasValues)
                    {
                        var (_, propErrors) = DirectorHelpers.SetProperties(comp, compProperties);
                        compErrors.AddRange(propErrors);
                    }
#endif
                }
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create_gameobject";
            if (primitiveType.HasValue)
                response["primitive_type"] = primitiveTypeStr;
            ResponseHelpers.AddIdentity(response, go);
            if (compErrors.Count > 0)
            {
                var errArr = new JArray();
                foreach (var e in compErrors) errArr.Add(e);
                response["component_errors"] = errArr;
            }
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        // -----------------------------------------------------------------------
        // delete_gameobject
        // -----------------------------------------------------------------------

        public static string DeleteGameObject(JObject args)
        {
            var resolveError = ObjectResolver.ResolveFromArgs(args, out var go);
            if (resolveError != null) return resolveError;

            // Capture identity before destroy
            var path = ResponseHelpers.GetHierarchyPath(go.transform);
#pragma warning disable CS0618
            var instanceId = go.GetInstanceID();
#pragma warning restore CS0618

#if UNITY_EDITOR
            Undo.DestroyObjectImmediate(go);
#else
            UnityEngine.Object.DestroyImmediate(go);
#endif

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "delete_gameobject";
            response["path"] = path;
            response["instance_id"] = instanceId;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        // -----------------------------------------------------------------------
        // reparent
        // -----------------------------------------------------------------------

        public static string Reparent(JObject args)
        {
            var resolveError = ObjectResolver.ResolveFromArgs(args, out var go);
            if (resolveError != null) return resolveError;

            var oldParentPath = go.transform.parent != null
                ? ResponseHelpers.GetHierarchyPath(go.transform.parent)
                : null;

            var newParentPath = args["new_parent"]?.Value<string>();
            GameObject newParentGo = null;

            if (!string.IsNullOrEmpty(newParentPath))
            {
                var parentResolve = ObjectResolver.Resolve(newParentPath);
                if (!parentResolve.Success)
                    return ResponseHelpers.ErrorResponse(
                        parentResolve.ErrorCode,
                        parentResolve.ErrorMessage,
                        parentResolve.Suggestion);
                newParentGo = parentResolve.GameObject;
            }

            var worldPositionStays = args["world_position_stays"]?.Value<bool>() ?? true;

#if UNITY_EDITOR
            Undo.RecordObject(go.transform, "Theatre Reparent");
#endif
            go.transform.SetParent(newParentGo?.transform, worldPositionStays);

            var siblingIndex = args["sibling_index"]?.Value<int>();
            if (siblingIndex.HasValue)
                go.transform.SetSiblingIndex(siblingIndex.Value);

            var newPath = ResponseHelpers.GetHierarchyPath(go.transform);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "reparent";
            ResponseHelpers.AddIdentity(response, go);
            response["old_parent"] = oldParentPath;
            response["new_parent"] = newParentGo != null
                ? ResponseHelpers.GetHierarchyPath(newParentGo.transform)
                : null;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        // -----------------------------------------------------------------------
        // duplicate
        // -----------------------------------------------------------------------

        public static string Duplicate(JObject args)
        {
            var resolveError = ObjectResolver.ResolveFromArgs(args, out var go);
            if (resolveError != null) return resolveError;

            var count = args["count"]?.Value<int>() ?? 1;
            var newName = args["new_name"]?.Value<string>();
            var offset = JsonParamParser.ParseVector3(args, "offset");

            var created = new JArray();

            for (int i = 0; i < count; i++)
            {
                var copy = UnityEngine.Object.Instantiate(go, go.transform.parent);
                if (!string.IsNullOrEmpty(newName))
                    copy.name = count > 1 ? $"{newName} ({i + 1})" : newName;
                else
                    copy.name = go.name; // Instantiate appends "(Clone)" by default — reset

                if (offset.HasValue)
                    copy.transform.localPosition =
                        go.transform.localPosition + offset.Value * (i + 1);

#if UNITY_EDITOR
                Undo.RegisterCreatedObjectUndo(copy, "Theatre Duplicate");
#endif

                var entry = new JObject();
                ResponseHelpers.AddIdentity(entry, copy);
                created.Add(entry);
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "duplicate";
            response["source_path"] = ResponseHelpers.GetHierarchyPath(go.transform);
            response["results"] = created;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        // -----------------------------------------------------------------------
        // set_component
        // -----------------------------------------------------------------------

        public static string SetComponent(JObject args)
        {
            var resolveError = ObjectResolver.ResolveFromArgs(args, out var go);
            if (resolveError != null) return resolveError;

            var componentName = args["component"]?.Value<string>();
            if (string.IsNullOrEmpty(componentName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'component' parameter",
                    "Provide the component type name, e.g. 'BoxCollider'");

            var properties = args["properties"] as JObject;
            var addIfMissing = args["add_if_missing"]?.Value<bool>() ?? true;

            // Resolve the type
            var compType = DirectorHelpers.ResolveComponentType(componentName, out var typeError);
            if (compType == null) return typeError;

            // Find or add component
            var comp = go.GetComponent(compType);
            if (comp == null)
            {
                if (!addIfMissing)
                    return ResponseHelpers.ErrorResponse(
                        "component_not_found",
                        $"Component '{componentName}' not found on '{go.name}'",
                        "Set add_if_missing to true to add it automatically");

#if UNITY_EDITOR
                comp = Undo.AddComponent(go, compType);
#else
                comp = go.AddComponent(compType);
#endif
                if (comp == null)
                    return ResponseHelpers.ErrorResponse(
                        "add_component_failed",
                        $"Failed to add component '{componentName}' to '{go.name}'",
                        "Some components cannot be added at runtime or may conflict");
            }

            int propsSet = 0;
            var propErrors = new List<string>();

#if UNITY_EDITOR
            if (properties != null && properties.HasValues)
            {
                Undo.RecordObject(comp, "Theatre SetComponent");
                (propsSet, propErrors) = DirectorHelpers.SetProperties(comp, properties);
            }
#endif

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_component";
            ResponseHelpers.AddIdentity(response, go);
            response["component"] = componentName;
            response["properties_set"] = propsSet;

            if (propErrors.Count > 0)
            {
                var errArr = new JArray();
                foreach (var e in propErrors) errArr.Add(e);
                response["property_errors"] = errArr;
            }

            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        // -----------------------------------------------------------------------
        // remove_component
        // -----------------------------------------------------------------------

        public static string RemoveComponent(JObject args)
        {
            var resolveError = ObjectResolver.ResolveFromArgs(args, out var go);
            if (resolveError != null) return resolveError;

            var componentName = args["component"]?.Value<string>();
            if (string.IsNullOrEmpty(componentName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'component' parameter",
                    "Provide the component type name to remove");

            // Cannot remove Transform
            if (string.Equals(componentName, "Transform", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(componentName, "RectTransform", StringComparison.OrdinalIgnoreCase))
                return ResponseHelpers.ErrorResponse(
                    "cannot_remove_transform",
                    "Cannot remove the Transform component — it is required by all GameObjects",
                    "Remove the GameObject instead if no longer needed");

            var comp = ObjectResolver.FindComponent(go, componentName);
            if (comp == null)
                return ResponseHelpers.ErrorResponse(
                    "component_not_found",
                    $"Component '{componentName}' not found on '{go.name}'",
                    "Use scene_inspect to list all components on this GameObject");

#if UNITY_EDITOR
            Undo.DestroyObjectImmediate(comp);
#else
            UnityEngine.Object.DestroyImmediate(comp);
#endif

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "remove_component";
            ResponseHelpers.AddIdentity(response, go);
            response["component"] = componentName;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        // -----------------------------------------------------------------------
        // move_to_scene
        // -----------------------------------------------------------------------

        public static string MoveToScene(JObject args)
        {
            var paths = args["paths"] as JArray;
            if (paths == null || paths.Count == 0)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'paths' parameter",
                    "Provide an array of root GameObject paths to move");

            var targetSceneName = args["target_scene"]?.Value<string>();
            if (string.IsNullOrEmpty(targetSceneName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'target_scene' parameter",
                    "Provide the target scene name");

            var targetScene = SceneManager.GetSceneByName(targetSceneName);
            if (!targetScene.IsValid() || !targetScene.isLoaded)
                return ResponseHelpers.ErrorResponse(
                    "scene_not_found",
                    $"Scene '{targetSceneName}' is not loaded",
                    "Load the target scene first using load_scene with mode 'additive'");

            int movedCount = 0;
            var errors = new JArray();

            foreach (var pathToken in paths)
            {
                var p = pathToken.Value<string>();
                var resolved = ObjectResolver.Resolve(p);
                if (!resolved.Success)
                {
                    errors.Add($"'{p}': not found — {resolved.ErrorMessage}");
                    continue;
                }

                var go = resolved.GameObject;
                if (go.transform.parent != null)
                {
                    errors.Add($"'{p}': not a root object — detach from parent first");
                    continue;
                }

                SceneManager.MoveGameObjectToScene(go, targetScene);
                movedCount++;
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "move_to_scene";
            response["target_scene"] = targetSceneName;
            response["moved"] = movedCount;
            if (errors.Count > 0)
                response["errors"] = errors;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
