using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Theatre.Stage
{
    /// <summary>
    /// Shared helpers for building Stage tool JSON responses.
    /// All Stage responses include frame context (frame, time, play_mode).
    /// </summary>
    public static class ResponseHelpers
    {
        /// <summary>
        /// Add frame context fields to a JObject.
        /// Adds: frame, time, play_mode.
        /// </summary>
        public static void AddFrameContext(JObject obj)
        {
            obj["project"] = Application.productName;
            obj["frame"] = Time.frameCount;
            obj["time"] = Math.Round(Time.time, 2);
            obj["play_mode"] = Application.isPlaying;
        }

        /// <summary>
        /// Add the editing context (scene vs prefab) to a JObject.
        /// </summary>
        public static void AddEditingContext(JObject obj)
        {
#if UNITY_EDITOR
            var prefabStage = UnityEditor.SceneManagement
                .PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                obj["context"] = "prefab";
                obj["prefab_path"] = prefabStage.assetPath;
            }
            else
            {
                obj["context"] = "scene";
            }
#else
            obj["context"] = "scene";
#endif
        }

        /// <summary>
        /// Convert a Vector3 to a JArray [x, y, z] with 2 decimal places.
        /// </summary>
        public static JArray ToJArray(Vector3 v)
        {
            return new JArray(
                Math.Round(v.x, 2),
                Math.Round(v.y, 2),
                Math.Round(v.z, 2));
        }

        /// <summary>
        /// Convert a Vector2 to a JArray [x, y] with 2 decimal places.
        /// </summary>
        public static JArray ToJArray(Vector2 v)
        {
            return new JArray(
                Math.Round(v.x, 2),
                Math.Round(v.y, 2));
        }

        /// <summary>
        /// Convert a Quaternion to a JArray [x, y, z, w] with 4 decimal places.
        /// </summary>
        public static JArray QuaternionToJArray(Quaternion q)
        {
            return new JArray(
                Math.Round(q.x, 4),
                Math.Round(q.y, 4),
                Math.Round(q.z, 4),
                Math.Round(q.w, 4));
        }

        /// <summary>
        /// Convert a Color to a JArray [r, g, b, a] with 3 decimal places.
        /// </summary>
        public static JArray ToJArray(Color c)
        {
            return new JArray(
                Math.Round(c.r, 3),
                Math.Round(c.g, 3),
                Math.Round(c.b, 3),
                Math.Round(c.a, 3));
        }

        /// <summary>
        /// Build a standard Theatre error response JSON string.
        /// </summary>
        public static string ErrorResponse(
            string code, string message, string suggestion)
        {
            var obj = new JObject();
            var error = new JObject();
            error["code"] = code;
            error["message"] = message;
            error["suggestion"] = suggestion;
            obj["error"] = error;
            return obj.ToString(Formatting.None);
        }

        /// <summary>
        /// Add identity fields (path and instance_id) for a GameObject to a response.
        /// Centralizes the CS0618 suppression for GetInstanceID().
        /// </summary>
        public static void AddIdentity(JObject obj, GameObject go)
        {
            obj["path"] = GetHierarchyPath(go.transform);
#pragma warning disable CS0618
            obj["instance_id"] = go.GetInstanceID();
#pragma warning restore CS0618
        }

        /// <summary>
        /// Guard that requires play mode. Returns null if in play mode,
        /// or an error response string if not.
        /// </summary>
        public static string RequirePlayMode(string operationName)
        {
            if (Application.isPlaying) return null;
            return ErrorResponse(
                "requires_play_mode",
                $"{operationName} requires Play Mode",
                "Enter Play Mode first");
        }

        /// <summary>
        /// Get the hierarchy path for a Transform, handling multi-scene
        /// addressing and sibling disambiguation.
        /// </summary>
        public static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return null;

            var path = GetLocalPath(transform);
            var loadedSceneCount = SceneManager.sceneCount;
            if (loadedSceneCount > 1)
            {
                var sceneName = transform.gameObject.scene.name;
                return sceneName + ":/" + path;
            }
            return "/" + path;
        }

        /// <summary>
        /// Build the local path (within a scene) for a Transform.
        /// Handles sibling name disambiguation by appending (index).
        /// </summary>
        private static string GetLocalPath(Transform transform)
        {
            var parts = new List<string>();
            var current = transform;
            while (current != null)
            {
                var name = current.name;
                // Check for duplicate sibling names
                if (current.parent != null)
                {
                    int duplicateCount = 0;
                    int myIndex = 0;
                    for (int i = 0; i < current.parent.childCount; i++)
                    {
                        var sibling = current.parent.GetChild(i);
                        if (sibling.name == name)
                        {
                            if (sibling == current) myIndex = duplicateCount;
                            duplicateCount++;
                        }
                    }
                    if (duplicateCount > 1)
                        name += $" ({myIndex})";
                }
                else
                {
                    // Root-level: check other root objects in the scene
                    var scene = current.gameObject.scene;
                    var roots = scene.GetRootGameObjects();
                    int duplicateCount = 0;
                    int myIndex = 0;
                    foreach (var root in roots)
                    {
                        if (root.name == name)
                        {
                            if (root.transform == current) myIndex = duplicateCount;
                            duplicateCount++;
                        }
                    }
                    if (duplicateCount > 1)
                        name += $" ({myIndex})";
                }
                parts.Add(name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
