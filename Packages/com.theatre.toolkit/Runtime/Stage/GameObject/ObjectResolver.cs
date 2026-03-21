using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Theatre.Stage
{
    /// <summary>
    /// Resolves GameObject references from hierarchy paths or instance IDs.
    /// Handles multi-scene addressing (SceneName:/Path) and prefab mode scoping.
    /// </summary>
    public static class ObjectResolver
    {
        /// <summary>
        /// Result of resolving a GameObject reference.
        /// </summary>
        public readonly struct ResolveResult
        {
            /// <summary>The resolved GameObject, or null if not found.</summary>
            public readonly GameObject GameObject;

            /// <summary>Error code if resolution failed.</summary>
            public readonly string ErrorCode;

            /// <summary>Error message if resolution failed.</summary>
            public readonly string ErrorMessage;

            /// <summary>Suggestion for recovery if resolution failed.</summary>
            public readonly string Suggestion;

            /// <summary>Whether resolution succeeded.</summary>
            public bool Success => GameObject != null;

            public ResolveResult(GameObject go)
            {
                GameObject = go;
                ErrorCode = null;
                ErrorMessage = null;
                Suggestion = null;
            }

            public ResolveResult(string errorCode, string message, string suggestion)
            {
                GameObject = null;
                ErrorCode = errorCode;
                ErrorMessage = message;
                Suggestion = suggestion;
            }
        }

        /// <summary>
        /// Resolve a GameObject by path, instance_id, or both.
        /// At least one of path or instanceId must be provided.
        /// </summary>
        /// <param name="path">Hierarchy path (e.g., "/Player" or "MainLevel:/Player").</param>
        /// <param name="instanceId">Unity InstanceID (e.g., 10240).</param>
        /// <returns>ResolveResult with the GameObject or error details.</returns>
        public static ResolveResult Resolve(string path = null, int? instanceId = null)
        {
            if (path == null && instanceId == null)
            {
                return new ResolveResult(
                    "invalid_parameter",
                    "Either 'path' or 'instance_id' must be provided",
                    "Provide a hierarchy path like '/Player' or an instance_id from a previous query");
            }

            // Instance ID takes priority if both provided
            if (instanceId.HasValue)
            {
                return ResolveByInstanceId(instanceId.Value);
            }

            return ResolveByPath(path);
        }

        /// <summary>
        /// Resolve a GameObject by its InstanceID.
        /// </summary>
        private static ResolveResult ResolveByInstanceId(int instanceId)
        {
            GameObject obj = null;
#if UNITY_EDITOR
            var found = UnityEditor.EditorUtility.InstanceIDToObject(instanceId);
            obj = found as GameObject;
            if (obj == null && found is Component comp)
                obj = comp.gameObject;
#endif

            if (obj == null)
            {
                return new ResolveResult(
                    "gameobject_not_found",
                    $"No GameObject found with instance_id {instanceId}",
                    "The object may have been destroyed. Use scene_hierarchy to find current objects.");
            }

            return new ResolveResult(obj);
        }

        /// <summary>
        /// Resolve a GameObject by hierarchy path.
        /// Supports multi-scene format: "SceneName:/Path/To/Object"
        /// </summary>
        private static ResolveResult ResolveByPath(string path)
        {
            // Parse scene prefix
            string sceneName = null;
            string localPath = path;

            int colonSlash = path.IndexOf(":/", StringComparison.Ordinal);
            if (colonSlash >= 0)
            {
                sceneName = path.Substring(0, colonSlash);
                localPath = path.Substring(colonSlash + 1); // keeps leading /
            }

            // Strip leading slash
            if (localPath.StartsWith("/"))
                localPath = localPath.Substring(1);

            if (string.IsNullOrEmpty(localPath))
            {
                return new ResolveResult(
                    "invalid_parameter",
                    $"Path '{path}' does not identify a GameObject",
                    "Provide a path like '/Player' or '/Environment/Enemies/Scout_02'");
            }

            // Split into segments
            var segments = localPath.Split('/');

            // Determine which scenes to search
            var scenesToSearch = new List<Scene>();
            if (sceneName != null)
            {
                var scene = SceneManager.GetSceneByName(sceneName);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    return new ResolveResult(
                        "scene_not_loaded",
                        $"Scene '{sceneName}' is not loaded",
                        "Use scene_hierarchy to see loaded scenes");
                }
                scenesToSearch.Add(scene);
            }
            else
            {
                // Search active scene first, then others
                var active = SceneManager.GetActiveScene();
                scenesToSearch.Add(active);
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    if (s.isLoaded && s != active)
                        scenesToSearch.Add(s);
                }
            }

            // Search each scene
            GameObject found = null;
            int matchCount = 0;

            foreach (var scene in scenesToSearch)
            {
                var result = FindInScene(scene, segments);
                if (result != null)
                {
                    found = result;
                    matchCount++;
                    if (sceneName != null) break; // Explicit scene — first match wins
                }
            }

            if (found == null)
            {
                return new ResolveResult(
                    "gameobject_not_found",
                    $"GameObject at path '{path}' does not exist",
                    "Use scene_hierarchy with operation 'find' to search for matching objects");
            }

            if (matchCount > 1 && sceneName == null)
            {
                // Ambiguous — return the active scene match but warn
                // (The first match IS the active scene match due to search order)
            }

            return new ResolveResult(found);
        }

        /// <summary>
        /// Find a GameObject in a specific scene by path segments.
        /// </summary>
        private static GameObject FindInScene(Scene scene, string[] segments)
        {
            var roots = scene.GetRootGameObjects();
            Transform current = null;

            // Find root
            foreach (var root in roots)
            {
                if (NameMatches(root.name, segments[0]))
                {
                    current = root.transform;
                    break;
                }
            }

            if (current == null) return null;

            // Walk remaining segments
            for (int i = 1; i < segments.Length; i++)
            {
                Transform child = null;
                for (int c = 0; c < current.childCount; c++)
                {
                    if (NameMatches(current.GetChild(c).name, segments[i]))
                    {
                        child = current.GetChild(c);
                        break;
                    }
                }
                if (child == null) return null;
                current = child;
            }

            return current.gameObject;
        }

        /// <summary>
        /// Check if a GameObject name matches a path segment.
        /// Handles disambiguation suffixes like "Scout (1)".
        /// </summary>
        private static bool NameMatches(string objectName, string segment)
        {
            if (objectName == segment) return true;

            // Handle disambiguation: segment "Scout (1)" should match
            // the second object named "Scout"
            // For now, exact match only — disambiguation is resolved by
            // GetHierarchyPath when building paths
            return false;
        }

        /// <summary>
        /// Resolve a GameObject from JObject args containing "path" and/or "instance_id".
        /// Returns null on success (go is set), or an error response JSON string on failure.
        /// </summary>
        /// <param name="args">The tool arguments object.</param>
        /// <param name="go">Set to the resolved GameObject on success; null on failure.</param>
        /// <returns>Null on success, or a JSON error response string on failure.</returns>
        public static string ResolveFromArgs(JObject args, out GameObject go)
        {
            go = null;
            var path = args["path"]?.Value<string>();
            int? instanceId = null;
            if (args["instance_id"] != null)
                instanceId = args["instance_id"].Value<int>();

            var resolved = Resolve(path, instanceId);
            if (!resolved.Success)
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);

            go = resolved.GameObject;
            return null;
        }

        /// <summary>
        /// Find a component on a GameObject by type name (case-insensitive).
        /// Returns null if not found.
        /// </summary>
        /// <param name="go">The target GameObject.</param>
        /// <param name="componentName">Component type name to search for.</param>
        public static Component FindComponent(GameObject go, string componentName)
        {
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (string.Equals(comp.GetType().Name, componentName,
                    StringComparison.OrdinalIgnoreCase))
                    return comp;
            }
            return null;
        }

        /// <summary>
        /// Get all root GameObjects across all loaded scenes,
        /// or scoped to the prefab stage if active.
        /// </summary>
        public static List<GameObject> GetAllRoots()
        {
            var roots = new List<GameObject>();

#if UNITY_EDITOR
            var prefabStage = UnityEditor.SceneManagement
                .PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                var prefabRoot = prefabStage.prefabContentsRoot;
                if (prefabRoot != null)
                    roots.Add(prefabRoot);
                return roots;
            }
#endif

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                    roots.AddRange(scene.GetRootGameObjects());
            }
            return roots;
        }
    }
}
