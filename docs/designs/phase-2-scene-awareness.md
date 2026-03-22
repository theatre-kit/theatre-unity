# Design: Phase 2 — Stage: Scene Awareness

## Overview

First real tools. After this phase, an AI agent can see the Unity scene:
snapshot the full scene with token budgeting, navigate the hierarchy, and
deeply inspect any GameObject's components and serialized properties.

**Key components:**
- `scene_snapshot` tool — budgeted spatial overview of GameObjects
- `scene_hierarchy` tool — list, find, search, path operations
- `scene_inspect` tool — deep single-object inspection via SerializedProperty
- Token budgeting engine (estimate, truncate, paginate)
- Pagination with expiring cursors
- Hierarchy walker (scene traversal, multi-scene, prefab mode)
- Property serializer (SerializedProperty to JSON)
- Object resolver (path or instance_id to GameObject)
- Clustering engine (group nearby objects for snapshot summaries)
- Response builder helpers (frame context, error shapes, vector encoding)
- Test scenes with known layouts for deterministic assertions

**Exit criteria:** Agent can snapshot a scene, navigate the hierarchy,
and inspect any GameObject's full component state. Responses respect token
budgets, paginate large result sets, and work in both edit mode and play mode.

---

## Architecture Decisions

### Where Does Scene Walking Logic Live?

**Split across Runtime and Editor assemblies.**

- **Runtime** (`Runtime/Stage/GameObject/`): `HierarchyWalker`, `ObjectResolver`,
  `SceneSnapshotBuilder`, `Clustering`, `TokenBudget`, response helpers.
  These use only `UnityEngine.SceneManagement`, `Transform`, `GameObject`,
  `Component` — no `UnityEditor` dependency.

- **Editor** (`Editor/Tools/`): `SceneSnapshotTool`, `SceneHierarchyTool`,
  `SceneInspectTool`, `PropertySerializer`. These use `UnityEditor.SerializedObject`
  / `SerializedProperty` for deep property traversal and
  `UnityEditor.PrefabUtility` for prefab detection. Tool registration
  follows the `TheatreStatusTool` pattern.

Rationale: `SerializedObject` and `SerializedProperty` live in
`UnityEditor` and are unavailable in Runtime assemblies. However, hierarchy
walking and spatial logic are pure `UnityEngine` and belong in Runtime where
they can be tested without editor dependencies and reused by future tools
(spatial queries, watches, recording).

### Token Budgeting Strategy

Simple character-based estimation: **1 token ~= 4 characters** of JSON output.
The budgeting engine tracks character count as it builds responses and stops
adding content when approaching the budget. This avoids any tokenizer
dependency while being accurate enough for response shaping.

Default budget: 1500 tokens. Hard cap: 4000 tokens.

### Pagination Cursor Format

Base64-encoded JSON object: `{ "tool": "scene_hierarchy", "operation": "list",
"offset": 50, "ts": 1711234567 }`. Cursors expire after 60 seconds or when
the active scene changes. The `ts` field is `DateTimeOffset.UtcNow.ToUnixTimeSeconds()`
at cursor creation time.

### Clustering Algorithm

Grid-based spatial clustering for snapshot summaries:
1. Compute AABB of all objects in the snapshot scope
2. Divide into cells (cell size = AABB extent / 8, minimum 5 units)
3. Objects in the same cell form a cluster
4. Report cluster label (common parent name or dominant type), center, spread
5. Singleton clusters (1 object) are not grouped — reported as individual objects

### Multi-Scene Addressing

- Single loaded scene: paths are `/Name/Child` (scene name omitted)
- Multiple loaded scenes: paths are `SceneName:/Name/Child`
- Resolving a path without scene prefix: search the active scene first,
  then all loaded scenes. Return error if ambiguous.
- `scene_hierarchy` list at root level returns loaded scenes as entries.

### Prefab Mode Detection

`UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage()` returns
non-null when in prefab editing mode. When active:
- `scene_snapshot` scopes to the prefab stage scene
- `scene_hierarchy` shows the prefab root
- Responses include `"context": "prefab"` and `"prefab_path": "Assets/..."`

When not in prefab mode: `"context": "scene"`.

---

## Implementation Units

### Unit 1: Response Helpers

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/ResponseHelpers.cs`

Shared utilities for building Stage tool responses. Every Stage response
includes frame context and follows the wire format contracts.

```csharp
using System;
using System.Text;
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
        /// Append frame context fields to a JSON writer.
        /// Writes: frame, time, play_mode, context, scene.
        /// Must be called inside an open JSON object.
        /// </summary>
        public static void WriteFrameContext(Utf8JsonWriter writer)
        {
            writer.WriteNumber("frame", Time.frameCount);
            writer.WriteNumber("time", Math.Round(Time.time, 2));
            writer.WriteBoolean("play_mode", Application.isPlaying);
        }

        /// <summary>
        /// Write the editing context (scene vs prefab).
        /// Must be called inside an open JSON object.
        /// </summary>
        public static void WriteEditingContext(Utf8JsonWriter writer)
        {
#if UNITY_EDITOR
            var prefabStage = UnityEditor.SceneManagement
                .PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                writer.WriteString("context", "prefab");
                writer.WriteString("prefab_path", prefabStage.assetPath);
            }
            else
            {
                writer.WriteString("context", "scene");
            }
#else
            writer.WriteString("context", "scene");
#endif
        }

        /// <summary>
        /// Write a Vector3 as a JSON array [x, y, z] with 2 decimal places.
        /// </summary>
        public static void WriteVector3(
            Utf8JsonWriter writer, string propertyName, Vector3 v)
        {
            writer.WriteStartArray(propertyName);
            writer.WriteNumberValue(Math.Round(v.x, 2));
            writer.WriteNumberValue(Math.Round(v.y, 2));
            writer.WriteNumberValue(Math.Round(v.z, 2));
            writer.WriteEndArray();
        }

        /// <summary>
        /// Write a Vector2 as a JSON array [x, y] with 2 decimal places.
        /// </summary>
        public static void WriteVector2(
            Utf8JsonWriter writer, string propertyName, Vector2 v)
        {
            writer.WriteStartArray(propertyName);
            writer.WriteNumberValue(Math.Round(v.x, 2));
            writer.WriteNumberValue(Math.Round(v.y, 2));
            writer.WriteEndArray();
        }

        /// <summary>
        /// Write a Quaternion as a JSON array [x, y, z, w] with 4 decimal places.
        /// </summary>
        public static void WriteQuaternion(
            Utf8JsonWriter writer, string propertyName, Quaternion q)
        {
            writer.WriteStartArray(propertyName);
            writer.WriteNumberValue(Math.Round(q.x, 4));
            writer.WriteNumberValue(Math.Round(q.y, 4));
            writer.WriteNumberValue(Math.Round(q.z, 4));
            writer.WriteNumberValue(Math.Round(q.w, 4));
            writer.WriteEndArray();
        }

        /// <summary>
        /// Write a Color as a JSON array [r, g, b, a] with 3 decimal places.
        /// </summary>
        public static void WriteColor(
            Utf8JsonWriter writer, string propertyName, Color c)
        {
            writer.WriteStartArray(propertyName);
            writer.WriteNumberValue(Math.Round(c.r, 3));
            writer.WriteNumberValue(Math.Round(c.g, 3));
            writer.WriteNumberValue(Math.Round(c.b, 3));
            writer.WriteNumberValue(Math.Round(c.a, 3));
            writer.WriteEndArray();
        }

        /// <summary>
        /// Build a standard Theatre error response JSON string.
        /// </summary>
        public static string ErrorResponse(
            string code, string message, string suggestion)
        {
            using var stream = new System.IO.MemoryStream();
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteStartObject("error");
            writer.WriteString("code", code);
            writer.WriteString("message", message);
            writer.WriteString("suggestion", suggestion);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
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
            var parts = new System.Collections.Generic.List<string>();
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
```

**Implementation Notes:**
- `WriteVector3` etc. use `Utf8JsonWriter` for zero-allocation JSON building.
  All Stage tools should use `Utf8JsonWriter` directly rather than serializing
  POCOs, per the ARCHITECTURE.md performance guidance.
- `GetHierarchyPath` auto-detects multi-scene by checking `SceneManager.sceneCount`.
  When only one scene is loaded, paths are `/Name/Child`. When multiple are
  loaded, paths are `SceneName:/Name/Child`.
- Sibling disambiguation matches Unity's own naming: `/Enemies/Scout (0)`,
  `/Enemies/Scout (1)`.
- Float rounding: 2 decimal places for positions/rotations, 4 for quaternions,
  3 for colors. This matches the CONTRACTS.md default.

**Acceptance Criteria:**
- [ ] `WriteFrameContext` writes `frame`, `time`, `play_mode` fields
- [ ] `WriteEditingContext` writes `"context": "prefab"` with `prefab_path` when in prefab stage
- [ ] `WriteEditingContext` writes `"context": "scene"` when not in prefab stage
- [ ] `WriteVector3` produces `[x, y, z]` array with 2 decimal places
- [ ] `GetHierarchyPath` returns `/Name` for single-scene root objects
- [ ] `GetHierarchyPath` returns `SceneName:/Name` when multiple scenes are loaded
- [ ] `GetHierarchyPath` appends `(index)` for duplicate sibling names
- [ ] `ErrorResponse` produces `{ "error": { "code": ..., "message": ..., "suggestion": ... } }`

---

### Unit 2: Object Resolver

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/GameObject/ObjectResolver.cs`

Resolves a path string or instance_id to a Unity `GameObject`. Used by all
three tools.

```csharp
using System;
using System.Collections.Generic;
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
            var obj = UnityEngine.Object.FindObjectFromInstanceID(instanceId)
                as GameObject;
            if (obj == null)
            {
                // Could be a component instance id — try to find it
                var component = UnityEngine.Object.FindObjectFromInstanceID(instanceId)
                    as Component;
                if (component != null)
                    obj = component.gameObject;
            }

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
```

**Implementation Notes:**
- `FindObjectFromInstanceID` is an internal Unity method exposed since
  Unity 2021. If unavailable, fall back to `Resources.FindObjectsOfTypeAll<GameObject>()`
  with a linear search — slower but functional.
- Multi-scene path parsing: split on `:/` to get scene name prefix.
  If no prefix, search active scene first to avoid ambiguity.
- `GetAllRoots` checks for prefab stage first. When editing a prefab,
  only the prefab contents are visible — the main scene is inaccessible.

**Acceptance Criteria:**
- [ ] `Resolve(path: "/Player")` finds a root GameObject named "Player"
- [ ] `Resolve(path: "/Player/Camera")` finds nested child
- [ ] `Resolve(instanceId: id)` finds GameObject by InstanceID
- [ ] `Resolve(path: "MainLevel:/Player")` scopes to named scene
- [ ] `Resolve(path: null, instanceId: null)` returns `invalid_parameter` error
- [ ] `Resolve(path: "/Nonexistent")` returns `gameobject_not_found` error
- [ ] `Resolve(path: "UnloadedScene:/X")` returns `scene_not_loaded` error
- [ ] `GetAllRoots()` returns prefab root when in prefab stage
- [ ] `GetAllRoots()` returns all scene roots when not in prefab stage

---

### Unit 3: Hierarchy Walker

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/GameObject/HierarchyWalker.cs`

Traverses the Transform hierarchy with depth limits, filters, and
pagination support.

```csharp
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Theatre.Stage
{
    /// <summary>
    /// Entry for a single GameObject in hierarchy traversal results.
    /// </summary>
    public struct HierarchyEntry
    {
        /// <summary>Full hierarchy path.</summary>
        public string Path;

        /// <summary>Unity InstanceID.</summary>
        public int InstanceId;

        /// <summary>GameObject name.</summary>
        public string Name;

        /// <summary>Number of direct children.</summary>
        public int ChildrenCount;

        /// <summary>Whether the object is active in hierarchy.</summary>
        public bool Active;

        /// <summary>Component type names on this object.</summary>
        public string[] Components;

        /// <summary>World position (if Transform present).</summary>
        public Vector3 Position;

        /// <summary>Distance from a reference point (optional).</summary>
        public float Distance;
    }

    /// <summary>
    /// Filter criteria for hierarchy traversal.
    /// </summary>
    public sealed class HierarchyFilter
    {
        /// <summary>Name pattern for find operations (glob: "*Door*").</summary>
        public string NamePattern;

        /// <summary>Required component type names.</summary>
        public string[] RequiredComponents;

        /// <summary>Required tag.</summary>
        public string Tag;

        /// <summary>Required layer name.</summary>
        public string Layer;

        /// <summary>Include inactive GameObjects.</summary>
        public bool IncludeInactive;

        /// <summary>Maximum hierarchy depth to traverse.</summary>
        public int MaxDepth = int.MaxValue;
    }

    /// <summary>
    /// Walks the Unity Transform hierarchy with filtering, depth limits,
    /// and pagination support.
    /// </summary>
    public static class HierarchyWalker
    {
        /// <summary>
        /// List direct children of a path (or scene roots if path is null).
        /// </summary>
        /// <param name="parentPath">
        /// Parent path to list children of. Null for root level.
        /// </param>
        /// <param name="includeInactive">Include disabled GameObjects.</param>
        /// <param name="offset">Pagination offset (skip this many).</param>
        /// <param name="limit">Maximum results to return.</param>
        /// <returns>List of child entries and total count.</returns>
        public static (List<HierarchyEntry> Entries, int Total) ListChildren(
            string parentPath,
            bool includeInactive = false,
            int offset = 0,
            int limit = 50)
        {
            var entries = new List<HierarchyEntry>();

            if (parentPath == null)
            {
                // Root level — list loaded scenes (or prefab root)
                return ListSceneRoots(includeInactive, offset, limit);
            }

            var resolved = ObjectResolver.Resolve(path: parentPath);
            if (!resolved.Success)
                return (entries, 0);

            var parent = resolved.GameObject.transform;
            int total = parent.childCount;
            int end = Math.Min(offset + limit, total);

            for (int i = offset; i < end; i++)
            {
                var child = parent.GetChild(i);
                if (!includeInactive && !child.gameObject.activeInHierarchy)
                    continue;
                entries.Add(BuildEntry(child));
            }

            return (entries, total);
        }

        /// <summary>
        /// Find GameObjects by name pattern (glob matching).
        /// Searches the entire hierarchy or a subtree.
        /// </summary>
        /// <param name="pattern">
        /// Glob pattern: "*" matches any characters, "?" matches one character.
        /// Examples: "Scout*", "*Door*", "Enemy_??"
        /// </param>
        /// <param name="root">Optional subtree root to limit search.</param>
        /// <param name="includeInactive">Include disabled objects.</param>
        /// <param name="maxResults">Maximum results to return.</param>
        /// <returns>Matching entries.</returns>
        public static List<HierarchyEntry> Find(
            string pattern,
            string root = null,
            bool includeInactive = false,
            int maxResults = 50)
        {
            var regex = GlobToRegex(pattern);
            var results = new List<HierarchyEntry>();
            var searchRoots = GetSearchRoots(root);

            foreach (var searchRoot in searchRoots)
            {
                FindRecursive(searchRoot, regex, includeInactive,
                    maxResults, results);
                if (results.Count >= maxResults) break;
            }

            return results;
        }

        /// <summary>
        /// Search GameObjects by component type, tag, or layer.
        /// </summary>
        /// <param name="filter">Search criteria.</param>
        /// <param name="root">Optional subtree root.</param>
        /// <param name="maxResults">Maximum results to return.</param>
        /// <returns>Matching entries.</returns>
        public static List<HierarchyEntry> Search(
            HierarchyFilter filter,
            string root = null,
            int maxResults = 50)
        {
            var results = new List<HierarchyEntry>();
            var searchRoots = GetSearchRoots(root);

            foreach (var searchRoot in searchRoots)
            {
                SearchRecursive(searchRoot, filter, maxResults, results);
                if (results.Count >= maxResults) break;
            }

            return results;
        }

        /// <summary>
        /// Walk the hierarchy from a root, collecting entries up to a depth limit.
        /// Used by scene_snapshot to gather objects for budgeted output.
        /// </summary>
        /// <param name="roots">Root transforms to walk from.</param>
        /// <param name="maxDepth">Maximum depth to traverse.</param>
        /// <param name="excludeInactive">Skip inactive objects.</param>
        /// <param name="focus">Optional focus point for distance calculation.</param>
        /// <param name="radius">Optional radius limit from focus.</param>
        /// <returns>All entries within the constraints.</returns>
        public static List<HierarchyEntry> Walk(
            List<Transform> roots,
            int maxDepth = 3,
            bool excludeInactive = true,
            Vector3? focus = null,
            float? radius = null)
        {
            var entries = new List<HierarchyEntry>();

            foreach (var root in roots)
            {
                WalkRecursive(root, 0, maxDepth, excludeInactive,
                    focus, radius, entries);
            }

            return entries;
        }

        // --- Private helpers ---

        private static (List<HierarchyEntry>, int) ListSceneRoots(
            bool includeInactive, int offset, int limit)
        {
            var allRoots = ObjectResolver.GetAllRoots();
            var entries = new List<HierarchyEntry>();
            int total = allRoots.Count;
            int end = Math.Min(offset + limit, total);

            for (int i = offset; i < end; i++)
            {
                if (!includeInactive && !allRoots[i].activeInHierarchy)
                    continue;
                entries.Add(BuildEntry(allRoots[i].transform));
            }

            return (entries, total);
        }

        private static HierarchyEntry BuildEntry(Transform t)
        {
            var components = t.GetComponents<Component>();
            var typeNames = new string[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                typeNames[i] = components[i] != null
                    ? components[i].GetType().Name
                    : "Missing";
            }

            return new HierarchyEntry
            {
                Path = ResponseHelpers.GetHierarchyPath(t),
                InstanceId = t.gameObject.GetInstanceID(),
                Name = t.name,
                ChildrenCount = t.childCount,
                Active = t.gameObject.activeInHierarchy,
                Components = typeNames,
                Position = t.position
            };
        }

        private static void WalkRecursive(
            Transform current, int depth, int maxDepth,
            bool excludeInactive, Vector3? focus, float? radius,
            List<HierarchyEntry> entries)
        {
            if (excludeInactive && !current.gameObject.activeInHierarchy)
                return;

            float distance = 0f;
            if (focus.HasValue)
            {
                distance = Vector3.Distance(current.position, focus.Value);
                if (radius.HasValue && distance > radius.Value)
                    return;
            }

            var entry = BuildEntry(current);
            entry.Distance = distance;
            entries.Add(entry);

            if (depth < maxDepth)
            {
                for (int i = 0; i < current.childCount; i++)
                {
                    WalkRecursive(current.GetChild(i), depth + 1, maxDepth,
                        excludeInactive, focus, radius, entries);
                }
            }
        }

        private static void FindRecursive(
            Transform current, Regex pattern, bool includeInactive,
            int maxResults, List<HierarchyEntry> results)
        {
            if (results.Count >= maxResults) return;
            if (!includeInactive && !current.gameObject.activeInHierarchy)
                return;

            if (pattern.IsMatch(current.name))
                results.Add(BuildEntry(current));

            for (int i = 0; i < current.childCount; i++)
            {
                FindRecursive(current.GetChild(i), pattern,
                    includeInactive, maxResults, results);
            }
        }

        private static void SearchRecursive(
            Transform current, HierarchyFilter filter,
            int maxResults, List<HierarchyEntry> results)
        {
            if (results.Count >= maxResults) return;
            if (!filter.IncludeInactive && !current.gameObject.activeInHierarchy)
                return;

            bool matches = true;
            var go = current.gameObject;

            if (filter.Tag != null && !go.CompareTag(filter.Tag))
                matches = false;

            if (matches && filter.Layer != null
                && LayerMask.LayerToName(go.layer) != filter.Layer)
                matches = false;

            if (matches && filter.RequiredComponents != null)
            {
                foreach (var typeName in filter.RequiredComponents)
                {
                    if (go.GetComponent(typeName) == null)
                    {
                        matches = false;
                        break;
                    }
                }
            }

            if (matches)
                results.Add(BuildEntry(current));

            for (int i = 0; i < current.childCount; i++)
            {
                SearchRecursive(current.GetChild(i), filter,
                    maxResults, results);
            }
        }

        private static List<Transform> GetSearchRoots(string root)
        {
            var roots = new List<Transform>();
            if (root != null)
            {
                var resolved = ObjectResolver.Resolve(path: root);
                if (resolved.Success)
                    roots.Add(resolved.GameObject.transform);
            }
            else
            {
                foreach (var go in ObjectResolver.GetAllRoots())
                    roots.Add(go.transform);
            }
            return roots;
        }

        /// <summary>
        /// Convert a glob pattern to a Regex.
        /// "*" becomes ".*", "?" becomes ".".
        /// </summary>
        private static Regex GlobToRegex(string pattern)
        {
            var escaped = Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".");
            return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase);
        }
    }
}
```

**Implementation Notes:**
- `BuildEntry` collects component type names for each object. This is used
  in snapshot summaries and hierarchy listings. The `Transform` component is
  always present but included for completeness.
- `WalkRecursive` supports both depth-limited traversal (for `scene_snapshot`)
  and distance-limited traversal (when `focus` and `radius` are provided).
- Glob matching is case-insensitive to match Unity's behavior with
  `GameObject.Find`.
- `GetSearchRoots` handles prefab mode via `ObjectResolver.GetAllRoots()`.

**Acceptance Criteria:**
- [ ] `ListChildren(null)` returns root GameObjects across loaded scenes
- [ ] `ListChildren("/Parent")` returns direct children of that object
- [ ] `ListChildren` with offset/limit paginates correctly
- [ ] `Find("Scout*")` matches "Scout_01", "Scout_02" but not "EnemyScout"
- [ ] `Find("*Door*")` matches "WoodenDoor", "DoorFrame", "backdoor"
- [ ] `Search` with tag filter returns only matching tagged objects
- [ ] `Search` with component filter returns only objects with that component
- [ ] `Walk` respects `maxDepth` — objects deeper than limit are not returned
- [ ] `Walk` with `focus` and `radius` excludes objects outside radius
- [ ] All entries have `Path`, `InstanceId`, `Name`, `ChildrenCount`, `Active`

---

### Unit 4: Token Budgeting Engine

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/Spatial/TokenBudget.cs`

Estimates token cost and manages response truncation.

```csharp
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Theatre.Stage
{
    /// <summary>
    /// Token budget metadata included in budgeted responses.
    /// </summary>
    public struct BudgetInfo
    {
        /// <summary>The budget the caller requested.</summary>
        public int Requested;

        /// <summary>Estimated tokens actually used.</summary>
        public int Used;

        /// <summary>Whether the response was truncated to fit.</summary>
        public bool Truncated;

        /// <summary>Reason for truncation, if truncated.</summary>
        public string TruncationReason;

        /// <summary>Suggestion for the agent when truncated.</summary>
        public string Suggestion;
    }

    /// <summary>
    /// Token budgeting engine. Estimates response size in tokens and manages
    /// truncation. Uses a simple heuristic: 1 token ~= 4 characters of JSON.
    /// </summary>
    public sealed class TokenBudget
    {
        /// <summary>Hard cap — responses never exceed this regardless of requested budget.</summary>
        public const int HardCap = 4000;

        /// <summary>Default budget when not specified by caller.</summary>
        public const int DefaultBudget = 1500;

        private readonly int _budget;
        private int _charCount;

        /// <summary>
        /// Create a new budget tracker.
        /// </summary>
        /// <param name="requestedBudget">Requested token budget. Clamped to hard cap.</param>
        public TokenBudget(int requestedBudget)
        {
            _budget = Math.Min(Math.Max(requestedBudget, 100), HardCap);
            _charCount = 0;
        }

        /// <summary>
        /// The effective budget (clamped to hard cap).
        /// </summary>
        public int Budget => _budget;

        /// <summary>
        /// Estimated tokens consumed so far.
        /// </summary>
        public int EstimatedTokens => _charCount / 4;

        /// <summary>
        /// Remaining token capacity.
        /// </summary>
        public int Remaining => Math.Max(0, _budget - EstimatedTokens);

        /// <summary>
        /// Whether the budget has been exhausted.
        /// </summary>
        public bool IsExhausted => EstimatedTokens >= _budget;

        /// <summary>
        /// Check if adding content of the given character count would
        /// exceed the budget.
        /// </summary>
        /// <param name="additionalChars">Number of JSON characters to add.</param>
        /// <returns>True if adding this content would exceed the budget.</returns>
        public bool WouldExceed(int additionalChars)
        {
            return (_charCount + additionalChars) / 4 >= _budget;
        }

        /// <summary>
        /// Record that characters were added to the response.
        /// </summary>
        /// <param name="chars">Character count added.</param>
        public void Add(int chars)
        {
            _charCount += chars;
        }

        /// <summary>
        /// Estimate the token cost of a JSON string.
        /// </summary>
        public static int EstimateTokens(string json)
        {
            return (json?.Length ?? 0) / 4;
        }

        /// <summary>
        /// Estimate the token cost of a single HierarchyEntry when serialized.
        /// This is an approximation — actual size depends on component count,
        /// path length, etc. Used for pre-flight budget checks.
        /// </summary>
        /// <param name="entry">The entry to estimate.</param>
        /// <returns>Estimated token count.</returns>
        public static int EstimateEntryTokens(HierarchyEntry entry)
        {
            // Base: {"path":"...","instance_id":...,"position":[...],...} ~= 120 chars
            int chars = 120;
            chars += entry.Path?.Length ?? 0;
            chars += entry.Name?.Length ?? 0;
            if (entry.Components != null)
            {
                // Component array: ["Type1","Type2"] ~= 12 chars per component
                chars += entry.Components.Length * 12;
            }
            return chars / 4;
        }

        /// <summary>
        /// Build the budget metadata for inclusion in the response.
        /// </summary>
        /// <param name="wasTruncated">Whether content was omitted.</param>
        /// <param name="reason">Truncation reason if truncated.</param>
        /// <param name="suggestion">Recovery suggestion if truncated.</param>
        /// <returns>BudgetInfo to serialize into the response.</returns>
        public BudgetInfo ToBudgetInfo(
            bool wasTruncated = false,
            string reason = null,
            string suggestion = null)
        {
            return new BudgetInfo
            {
                Requested = _budget,
                Used = EstimatedTokens,
                Truncated = wasTruncated,
                TruncationReason = reason,
                Suggestion = suggestion
            };
        }

        /// <summary>
        /// Write budget metadata to a JSON writer.
        /// </summary>
        public void WriteBudgetInfo(
            Utf8JsonWriter writer,
            bool truncated = false,
            string reason = null,
            string suggestion = null)
        {
            writer.WriteStartObject("budget");
            writer.WriteNumber("requested", _budget);
            writer.WriteNumber("used", EstimatedTokens);
            writer.WriteBoolean("truncated", truncated);
            if (truncated)
            {
                if (reason != null)
                    writer.WriteString("truncation_reason", reason);
                if (suggestion != null)
                    writer.WriteString("suggestion", suggestion);
            }
            writer.WriteEndObject();
        }
    }
}
```

**Implementation Notes:**
- The 1:4 ratio (chars to tokens) is a deliberate simplification. For JSON
  with many short keys and numbers, actual tokenization varies, but the
  4-char heuristic provides a reasonable upper bound for budget enforcement.
- `EstimateEntryTokens` provides a fast pre-flight check so tools can
  decide whether to include an entry before serializing it.
- Budget is clamped: minimum 100 tokens (to avoid useless responses),
  maximum 4000 tokens (hard cap per CONTRACTS.md).

**Acceptance Criteria:**
- [ ] `TokenBudget(1500)` sets budget to 1500
- [ ] `TokenBudget(5000)` clamps to 4000 (hard cap)
- [ ] `TokenBudget(50)` clamps to 100 (minimum)
- [ ] `EstimateTokens("abcd")` returns 1
- [ ] `WouldExceed` returns true when adding content would cross budget
- [ ] `Add` increments the character counter
- [ ] `IsExhausted` returns true when estimated tokens >= budget
- [ ] `WriteBudgetInfo` includes `requested`, `used`, `truncated` fields
- [ ] `WriteBudgetInfo` includes `truncation_reason` and `suggestion` only when truncated

---

### Unit 5: Pagination Cursor

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/Spatial/PaginationCursor.cs`

Opaque cursor encoding for paginated results.

```csharp
using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Theatre.Stage
{
    /// <summary>
    /// Opaque pagination cursor. Encoded as base64 JSON.
    /// Cursors expire after 60 seconds or when the active scene changes.
    /// </summary>
    public sealed class PaginationCursor
    {
        /// <summary>Time-to-live in seconds.</summary>
        public const int TtlSeconds = 60;

        /// <summary>Tool name this cursor belongs to.</summary>
        public string Tool { get; set; }

        /// <summary>Operation within the tool (e.g., "list", "find").</summary>
        public string Operation { get; set; }

        /// <summary>Offset into the result set.</summary>
        public int Offset { get; set; }

        /// <summary>Creation timestamp (Unix seconds).</summary>
        public long Timestamp { get; set; }

        /// <summary>Scene name at creation time (for expiry detection).</summary>
        public string SceneName { get; set; }

        /// <summary>
        /// Encode this cursor as an opaque base64 string.
        /// </summary>
        public string Encode()
        {
            var obj = new JObject
            {
                ["tool"] = Tool,
                ["op"] = Operation,
                ["offset"] = Offset,
                ["ts"] = Timestamp,
                ["scene"] = SceneName
            };
            var json = obj.ToString(Formatting.None);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        /// Decode a cursor string. Returns null if the cursor is invalid
        /// or expired.
        /// </summary>
        /// <param name="encoded">Base64-encoded cursor string.</param>
        /// <param name="currentScene">Current active scene name for expiry check.</param>
        /// <returns>Decoded cursor, or null if invalid/expired.</returns>
        public static PaginationCursor Decode(string encoded, string currentScene)
        {
            if (string.IsNullOrEmpty(encoded)) return null;

            try
            {
                var bytes = Convert.FromBase64String(encoded);
                var json = Encoding.UTF8.GetString(bytes);
                var root = JObject.Parse(json);

                var cursor = new PaginationCursor
                {
                    Tool = root["tool"]?.Value<string>(),
                    Operation = root["op"]?.Value<string>(),
                    Offset = root["offset"]?.Value<int>() ?? 0,
                    Timestamp = root["ts"]?.Value<long>() ?? 0,
                    SceneName = root["scene"]?.Value<string>()
                };

                // Check expiry
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (now - cursor.Timestamp > TtlSeconds)
                    return null; // Expired

                // Check scene change
                if (cursor.SceneName != currentScene)
                    return null; // Scene changed

                return cursor;
            }
            catch
            {
                return null; // Invalid format
            }
        }

        /// <summary>
        /// Create a cursor for the next page.
        /// </summary>
        /// <param name="tool">Tool name.</param>
        /// <param name="operation">Operation name.</param>
        /// <param name="nextOffset">Offset for the next page.</param>
        /// <returns>Encoded cursor string.</returns>
        public static string Create(string tool, string operation, int nextOffset)
        {
            var cursor = new PaginationCursor
            {
                Tool = tool,
                Operation = operation,
                Offset = nextOffset,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                SceneName = UnityEngine.SceneManagement.SceneManager
                    .GetActiveScene().name
            };
            return cursor.Encode();
        }

        /// <summary>
        /// Write pagination metadata to a JSON writer.
        /// </summary>
        public static void WritePagination(
            Utf8JsonWriter writer,
            string cursorString,
            bool hasMore,
            int returned,
            int? total = null)
        {
            writer.WriteStartObject("pagination");
            if (cursorString != null)
                writer.WriteString("cursor", cursorString);
            writer.WriteBoolean("has_more", hasMore);
            writer.WriteNumber("returned", returned);
            if (total.HasValue)
                writer.WriteNumber("total", total.Value);
            writer.WriteEndObject();
        }
    }
}
```

**Implementation Notes:**
- Cursors are self-contained — no server-side state to manage. The server
  validates expiry and scene match on decode.
- The `scene` field ensures cursors become invalid when the scene changes
  (objects may have been added/removed, making the offset meaningless).
- Invalid cursors return null rather than throwing — callers return an
  `invalid_cursor` error to the agent.

**Acceptance Criteria:**
- [ ] `Create` + `Decode` round-trips correctly
- [ ] Cursor expires after 60 seconds (Decode returns null)
- [ ] Cursor expires on scene change (Decode returns null)
- [ ] `Decode` returns null for malformed base64
- [ ] `Decode` returns null for invalid JSON within base64
- [ ] `WritePagination` writes cursor, has_more, returned, total fields
- [ ] `WritePagination` omits cursor when null
- [ ] `WritePagination` omits total when null

---

### Unit 6: Clustering Engine

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/Spatial/Clustering.cs`

Groups nearby objects by position for snapshot summaries.

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Theatre.Stage
{
    /// <summary>
    /// A cluster of nearby GameObjects for snapshot summaries.
    /// </summary>
    public struct ObjectCluster
    {
        /// <summary>
        /// Label for this cluster (common parent name or dominant component type).
        /// </summary>
        public string Label;

        /// <summary>Center position (average of member positions).</summary>
        public Vector3 Center;

        /// <summary>Spread — maximum distance from center to any member.</summary>
        public float Spread;

        /// <summary>Number of objects in this cluster.</summary>
        public int Count;
    }

    /// <summary>
    /// Grid-based spatial clustering for snapshot summaries.
    /// Divides space into cells and groups objects that share a cell.
    /// </summary>
    public static class Clustering
    {
        /// <summary>Minimum cell size in world units.</summary>
        public const float MinCellSize = 5f;

        /// <summary>Number of grid divisions per axis.</summary>
        public const int GridDivisions = 8;

        /// <summary>
        /// Minimum cluster size. Clusters with fewer objects are not grouped.
        /// </summary>
        public const int MinClusterSize = 2;

        /// <summary>
        /// Cluster a set of hierarchy entries by spatial proximity.
        /// </summary>
        /// <param name="entries">Objects to cluster.</param>
        /// <returns>
        /// List of clusters (only groups of MinClusterSize or more).
        /// Singleton objects are not included in clusters.
        /// </returns>
        public static List<ObjectCluster> Compute(List<HierarchyEntry> entries)
        {
            if (entries == null || entries.Count < MinClusterSize)
                return new List<ObjectCluster>();

            // Compute AABB
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (var entry in entries)
            {
                min = Vector3.Min(min, entry.Position);
                max = Vector3.Max(max, entry.Position);
            }

            var extent = max - min;
            var cellSize = new Vector3(
                Mathf.Max(extent.x / GridDivisions, MinCellSize),
                Mathf.Max(extent.y / GridDivisions, MinCellSize),
                Mathf.Max(extent.z / GridDivisions, MinCellSize)
            );

            // Assign objects to grid cells
            var cells = new Dictionary<(int, int, int), List<HierarchyEntry>>();

            foreach (var entry in entries)
            {
                var offset = entry.Position - min;
                var key = (
                    (int)(offset.x / cellSize.x),
                    (int)(offset.y / cellSize.y),
                    (int)(offset.z / cellSize.z)
                );

                if (!cells.TryGetValue(key, out var cell))
                {
                    cell = new List<HierarchyEntry>();
                    cells[key] = cell;
                }
                cell.Add(entry);
            }

            // Build clusters from cells with enough objects
            var clusters = new List<ObjectCluster>();

            foreach (var cell in cells.Values)
            {
                if (cell.Count < MinClusterSize) continue;

                var center = Vector3.zero;
                foreach (var entry in cell)
                    center += entry.Position;
                center /= cell.Count;

                float spread = 0f;
                foreach (var entry in cell)
                {
                    float d = Vector3.Distance(entry.Position, center);
                    if (d > spread) spread = d;
                }

                clusters.Add(new ObjectCluster
                {
                    Label = DeriveLabel(cell),
                    Center = center,
                    Spread = spread,
                    Count = cell.Count
                });
            }

            return clusters;
        }

        /// <summary>
        /// Determine which entries are not part of any cluster.
        /// Returns entries that are in cells with fewer than MinClusterSize objects.
        /// </summary>
        public static List<HierarchyEntry> GetUnclustered(
            List<HierarchyEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return new List<HierarchyEntry>();

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (var entry in entries)
            {
                min = Vector3.Min(min, entry.Position);
                max = Vector3.Max(max, entry.Position);
            }

            var extent = max - min;
            var cellSize = new Vector3(
                Mathf.Max(extent.x / GridDivisions, MinCellSize),
                Mathf.Max(extent.y / GridDivisions, MinCellSize),
                Mathf.Max(extent.z / GridDivisions, MinCellSize)
            );

            var cells = new Dictionary<(int, int, int), List<HierarchyEntry>>();

            foreach (var entry in entries)
            {
                var offset = entry.Position - min;
                var key = (
                    (int)(offset.x / cellSize.x),
                    (int)(offset.y / cellSize.y),
                    (int)(offset.z / cellSize.z)
                );

                if (!cells.TryGetValue(key, out var cell))
                {
                    cell = new List<HierarchyEntry>();
                    cells[key] = cell;
                }
                cell.Add(entry);
            }

            var unclustered = new List<HierarchyEntry>();
            foreach (var cell in cells.Values)
            {
                if (cell.Count < MinClusterSize)
                    unclustered.AddRange(cell);
            }

            return unclustered;
        }

        /// <summary>
        /// Derive a human-readable label for a cluster.
        /// Strategy: find the most common parent name or most common component type.
        /// </summary>
        private static string DeriveLabel(List<HierarchyEntry> entries)
        {
            // Try to find a common parent name
            var parentCounts = new Dictionary<string, int>();
            foreach (var entry in entries)
            {
                if (entry.Path == null) continue;
                var lastSlash = entry.Path.LastIndexOf('/');
                if (lastSlash > 0)
                {
                    var parentSegment = entry.Path.Substring(0, lastSlash);
                    var parentSlash = parentSegment.LastIndexOf('/');
                    var parentName = parentSlash >= 0
                        ? parentSegment.Substring(parentSlash + 1)
                        : parentSegment;

                    if (!parentCounts.ContainsKey(parentName))
                        parentCounts[parentName] = 0;
                    parentCounts[parentName]++;
                }
            }

            // Find the most common parent
            string bestParent = null;
            int bestCount = 0;
            foreach (var kvp in parentCounts)
            {
                if (kvp.Value > bestCount)
                {
                    bestCount = kvp.Value;
                    bestParent = kvp.Key;
                }
            }

            if (bestParent != null && bestCount >= entries.Count / 2)
                return $"{bestParent} ({entries.Count})";

            // Fall back to most common non-Transform component
            var componentCounts = new Dictionary<string, int>();
            foreach (var entry in entries)
            {
                if (entry.Components == null) continue;
                foreach (var comp in entry.Components)
                {
                    if (comp == "Transform") continue;
                    if (!componentCounts.ContainsKey(comp))
                        componentCounts[comp] = 0;
                    componentCounts[comp]++;
                }
            }

            string bestComponent = null;
            bestCount = 0;
            foreach (var kvp in componentCounts)
            {
                if (kvp.Value > bestCount)
                {
                    bestCount = kvp.Value;
                    bestComponent = kvp.Key;
                }
            }

            if (bestComponent != null)
                return $"{bestComponent} ({entries.Count})";

            return $"Group ({entries.Count})";
        }
    }
}
```

**Implementation Notes:**
- Grid-based clustering is O(n) — one pass to assign cells, one pass to
  compute centers/spreads. No iterative algorithms like k-means.
- `MinCellSize` of 5 units prevents degenerate clusters when all objects
  are very close together.
- `DeriveLabel` uses heuristics: prefer common parent name (if >50% of
  entries share it), fall back to common component type, then generic label.
- `GetUnclustered` returns the entries that are singletons (not part of
  any cluster). The snapshot tool can include these as individual objects.

**Acceptance Criteria:**
- [ ] 10 objects at the same position form 1 cluster
- [ ] Objects spread across the scene form multiple clusters
- [ ] Single objects are not included in clusters (returned by `GetUnclustered`)
- [ ] Cluster `Center` is the average position of members
- [ ] Cluster `Spread` is the max distance from center to any member
- [ ] `DeriveLabel` returns parent name when objects share a common parent
- [ ] `DeriveLabel` falls back to component type when no common parent
- [ ] Empty input returns empty cluster list

---

### Unit 7: Property Serializer

**File:** `Packages/com.theatre.toolkit/Editor/Tools/PropertySerializer.cs`

Traverses `SerializedProperty` trees and writes them as JSON. This is the
core of `scene_inspect` — it turns any Unity component's serialized state
into a JSON object following the wire format contracts.

```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// Serializes Unity SerializedProperty trees to JSON.
    /// Handles all SerializedPropertyType values and follows Theatre's
    /// wire format conventions (snake_case, vectors as arrays, etc.).
    /// </summary>
    public static class PropertySerializer
    {
        /// <summary>
        /// Detail level for property serialization.
        /// </summary>
        public enum DetailLevel
        {
            /// <summary>Type name and key values only.</summary>
            Summary,

            /// <summary>All visible serialized properties.</summary>
            Full,

            /// <summary>All properties including debug/hidden.</summary>
            Properties
        }

        /// <summary>
        /// Serialize all components on a GameObject to JSON.
        /// </summary>
        /// <param name="writer">JSON writer (must be inside an array).</param>
        /// <param name="gameObject">Target GameObject.</param>
        /// <param name="detail">Detail level.</param>
        /// <param name="componentFilter">
        /// Optional filter: only serialize components matching these type names.
        /// </param>
        /// <param name="budget">Token budget tracker.</param>
        public static void WriteComponents(
            Utf8JsonWriter writer,
            GameObject gameObject,
            DetailLevel detail,
            string[] componentFilter,
            Stage.TokenBudget budget)
        {
            var components = gameObject.GetComponents<Component>();

            foreach (var component in components)
            {
                if (component == null) continue; // Missing script

                var typeName = component.GetType().Name;

                // Apply component filter
                if (componentFilter != null)
                {
                    bool match = false;
                    foreach (var filter in componentFilter)
                    {
                        if (string.Equals(filter, typeName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            match = true;
                            break;
                        }
                    }
                    if (!match) continue;
                }

                // Budget check — estimate before serializing
                if (budget != null && budget.IsExhausted)
                    break;

                writer.WriteStartObject();
                writer.WriteString("type", typeName);

                // Script path for MonoBehaviours
                if (component is MonoBehaviour mb)
                {
                    var script = MonoScript.FromMonoBehaviour(mb);
                    if (script != null)
                    {
                        var path = AssetDatabase.GetAssetPath(script);
                        if (!string.IsNullOrEmpty(path))
                            writer.WriteString("script", path);
                    }
                }

                if (detail == DetailLevel.Summary)
                {
                    WriteSummaryProperties(writer, component);
                }
                else
                {
                    writer.WriteStartObject("properties");
                    WriteAllProperties(writer, component,
                        detail == DetailLevel.Properties, budget);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();

                // Track budget
                if (budget != null)
                    budget.Add(writer.BytesPending);
            }
        }

        /// <summary>
        /// Write summary-level properties for known component types.
        /// For Transform: position, euler_angles. For Renderer: enabled, bounds.
        /// For unknown types: first 3 serialized properties.
        /// </summary>
        private static void WriteSummaryProperties(
            Utf8JsonWriter writer, Component component)
        {
            writer.WriteStartObject("properties");

            if (component is Transform t)
            {
                Stage.ResponseHelpers.WriteVector3(writer, "position", t.position);
                Stage.ResponseHelpers.WriteVector3(writer, "euler_angles",
                    t.eulerAngles);
                Stage.ResponseHelpers.WriteVector3(writer, "local_scale",
                    t.localScale);
            }
            else
            {
                // Generic: first 3 visible properties
                var so = new SerializedObject(component);
                var prop = so.GetIterator();
                int count = 0;
                while (prop.NextVisible(enterChildren: false) && count < 3)
                {
                    if (prop.name == "m_Script") continue;
                    WriteProperty(writer, prop);
                    count++;
                }
            }

            writer.WriteEndObject();
        }

        /// <summary>
        /// Write all serialized properties for a component.
        /// </summary>
        private static void WriteAllProperties(
            Utf8JsonWriter writer,
            Component component,
            bool includeHidden,
            Stage.TokenBudget budget)
        {
            var so = new SerializedObject(component);
            var prop = so.GetIterator();
            bool enterChildren = true;

            while (includeHidden
                ? prop.Next(enterChildren)
                : prop.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (prop.name == "m_Script") continue;

                if (budget != null && budget.IsExhausted)
                {
                    writer.WriteString("_truncated",
                        "Budget exhausted — use 'components' filter");
                    break;
                }

                WriteProperty(writer, prop);
            }
        }

        /// <summary>
        /// Write a single SerializedProperty to JSON.
        /// Property name is converted to snake_case.
        /// </summary>
        private static void WriteProperty(Utf8JsonWriter writer, SerializedProperty prop)
        {
            var name = ToSnakeCase(prop.name);
            // Strip Unity's internal "m_" prefix
            if (name.StartsWith("m_"))
                name = name.Substring(2);

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    writer.WriteNumber(name, prop.intValue);
                    break;

                case SerializedPropertyType.Float:
                    writer.WriteNumber(name, Math.Round(prop.floatValue, 4));
                    break;

                case SerializedPropertyType.Boolean:
                    writer.WriteBoolean(name, prop.boolValue);
                    break;

                case SerializedPropertyType.String:
                    writer.WriteString(name, prop.stringValue);
                    break;

                case SerializedPropertyType.Enum:
                    writer.WriteString(name, ToSnakeCase(
                        prop.enumDisplayNames.Length > prop.enumValueIndex
                            ? prop.enumDisplayNames[prop.enumValueIndex]
                            : prop.enumValueIndex.ToString()));
                    break;

                case SerializedPropertyType.Vector2:
                    Stage.ResponseHelpers.WriteVector2(writer, name,
                        prop.vector2Value);
                    break;

                case SerializedPropertyType.Vector3:
                    Stage.ResponseHelpers.WriteVector3(writer, name,
                        prop.vector3Value);
                    break;

                case SerializedPropertyType.Vector4:
                    writer.WriteStartArray(name);
                    var v4 = prop.vector4Value;
                    writer.WriteNumberValue(Math.Round(v4.x, 2));
                    writer.WriteNumberValue(Math.Round(v4.y, 2));
                    writer.WriteNumberValue(Math.Round(v4.z, 2));
                    writer.WriteNumberValue(Math.Round(v4.w, 2));
                    writer.WriteEndArray();
                    break;

                case SerializedPropertyType.Quaternion:
                    Stage.ResponseHelpers.WriteQuaternion(writer, name,
                        prop.quaternionValue);
                    break;

                case SerializedPropertyType.Color:
                    Stage.ResponseHelpers.WriteColor(writer, name,
                        prop.colorValue);
                    break;

                case SerializedPropertyType.Rect:
                    var rect = prop.rectValue;
                    writer.WriteStartArray(name);
                    writer.WriteNumberValue(Math.Round(rect.x, 2));
                    writer.WriteNumberValue(Math.Round(rect.y, 2));
                    writer.WriteNumberValue(Math.Round(rect.width, 2));
                    writer.WriteNumberValue(Math.Round(rect.height, 2));
                    writer.WriteEndArray();
                    break;

                case SerializedPropertyType.Bounds:
                    var bounds = prop.boundsValue;
                    writer.WriteStartObject(name);
                    Stage.ResponseHelpers.WriteVector3(writer, "center",
                        bounds.center);
                    Stage.ResponseHelpers.WriteVector3(writer, "size",
                        bounds.size);
                    writer.WriteEndObject();
                    break;

                case SerializedPropertyType.ObjectReference:
                    WriteObjectReference(writer, name, prop);
                    break;

                case SerializedPropertyType.ArraySize:
                    // Skip — array contents handled separately
                    break;

                default:
                    if (prop.isArray)
                    {
                        WriteArray(writer, name, prop);
                    }
                    else if (prop.hasChildren)
                    {
                        WriteNestedObject(writer, name, prop);
                    }
                    else
                    {
                        writer.WriteString(name, $"<{prop.propertyType}>");
                    }
                    break;
            }
        }

        /// <summary>
        /// Write an ObjectReference property.
        /// </summary>
        private static void WriteObjectReference(
            Utf8JsonWriter writer, string name, SerializedProperty prop)
        {
            var obj = prop.objectReferenceValue;
            if (obj == null)
            {
                writer.WriteNull(name);
                return;
            }

            writer.WriteStartObject(name);
            writer.WriteNumber("instance_id", obj.GetInstanceID());

            var assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath))
                writer.WriteString("asset_path", assetPath);
            else
                writer.WriteString("name", obj.name);

            writer.WriteString("type", obj.GetType().Name);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Write a serialized array property.
        /// </summary>
        private static void WriteArray(
            Utf8JsonWriter writer, string name, SerializedProperty prop)
        {
            writer.WriteStartArray(name);
            for (int i = 0; i < prop.arraySize; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                WritePropertyValue(writer, element);
            }
            writer.WriteEndArray();
        }

        /// <summary>
        /// Write a nested struct/object property.
        /// </summary>
        private static void WriteNestedObject(
            Utf8JsonWriter writer, string name, SerializedProperty prop)
        {
            writer.WriteStartObject(name);
            var child = prop.Copy();
            var endProp = prop.Copy();
            endProp.Next(false); // Move past this property

            child.Next(true); // Enter children
            do
            {
                if (SerializedProperty.EqualContents(child, endProp))
                    break;
                WriteProperty(writer, child);
            } while (child.Next(false));

            writer.WriteEndObject();
        }

        /// <summary>
        /// Write a property value without a property name (for array elements).
        /// </summary>
        private static void WritePropertyValue(
            Utf8JsonWriter writer, SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    writer.WriteNumberValue(prop.intValue);
                    break;
                case SerializedPropertyType.Float:
                    writer.WriteNumberValue(Math.Round(prop.floatValue, 4));
                    break;
                case SerializedPropertyType.Boolean:
                    writer.WriteBooleanValue(prop.boolValue);
                    break;
                case SerializedPropertyType.String:
                    writer.WriteStringValue(prop.stringValue);
                    break;
                default:
                    writer.WriteStringValue($"<{prop.propertyType}>");
                    break;
            }
        }

        /// <summary>
        /// Convert a camelCase or PascalCase name to snake_case.
        /// Also handles Unity's "m_FieldName" convention.
        /// </summary>
        public static string ToSnakeCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0 && !char.IsUpper(name[i - 1]))
                        sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
```

**Implementation Notes:**
- Unity's `SerializedProperty.name` uses `m_FieldName` convention for
  built-in components. The `m_` prefix is stripped, and the remainder is
  converted to snake_case. `m_LocalPosition` becomes `local_position`.
- `WriteObjectReference` includes `instance_id` for scene objects and
  `asset_path` for assets, matching the CONTRACTS.md rules.
- For `Summary` detail level, known component types (Transform) get
  hand-picked key properties. Unknown types get their first 3 visible
  serialized properties — enough for the agent to decide if it wants
  a full inspection.
- Array and nested object serialization is recursive but bounded by
  the token budget — `WriteAllProperties` checks budget before each property.
- `enumDisplayNames` gives human-readable enum values which are then
  converted to snake_case, matching the CONTRACTS.md rule.

**Acceptance Criteria:**
- [ ] `ToSnakeCase("localPosition")` returns `"local_position"`
- [ ] `ToSnakeCase("isGrounded")` returns `"is_grounded"`
- [ ] `ToSnakeCase("m_LocalPosition")` prefix stripping produces `"local_position"`
- [ ] Transform component serializes `position`, `euler_angles`, `local_scale` at summary level
- [ ] Integer, float, boolean, string properties serialize to JSON primitives
- [ ] Vector3 properties serialize as `[x, y, z]` arrays
- [ ] Quaternion properties serialize as `[x, y, z, w]` arrays
- [ ] Color properties serialize as `[r, g, b, a]` arrays
- [ ] Bounds serialize as `{ "center": [...], "size": [...] }`
- [ ] ObjectReference properties include `instance_id` and either `asset_path` or `name`
- [ ] Null ObjectReference serializes as JSON null
- [ ] Enum values serialize as snake_case strings
- [ ] Array properties serialize as JSON arrays
- [ ] Component filter limits output to matching component types
- [ ] Budget exhaustion inserts `_truncated` marker and stops serializing
- [ ] MonoBehaviour components include `script` field with asset path

---

### Unit 8: scene_snapshot Tool

**File:** `Packages/com.theatre.toolkit/Editor/Tools/SceneSnapshotTool.cs`

MCP tool that returns a budgeted spatial overview of the scene.

```csharp
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Theatre.Editor
{
    /// <summary>
    /// MCP tool: scene_snapshot
    /// Returns a token-budgeted overview of GameObjects with positions,
    /// organized by proximity to a focus point, with clustering summaries.
    /// </summary>
    public static class SceneSnapshotTool
    {
        private static readonly JToken s_inputSchema;

        static SceneSnapshotTool()
        {
            s_inputSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""focus"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""minItems"": 3,
                        ""maxItems"": 3,
                        ""description"": ""Center point [x, y, z] for spatial organization. Defaults to main camera position.""
                    },
                    ""radius"": {
                        ""type"": ""number"",
                        ""description"": ""Limit to objects within this radius of focus.""
                    },
                    ""include_components"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Filter: only include objects with these component types.""
                    },
                    ""exclude_inactive"": {
                        ""type"": ""boolean"",
                        ""default"": true,
                        ""description"": ""Skip disabled GameObjects.""
                    },
                    ""max_depth"": {
                        ""type"": ""integer"",
                        ""default"": 3,
                        ""minimum"": 0,
                        ""maximum"": 20,
                        ""description"": ""Hierarchy depth limit for nested objects.""
                    },
                    ""budget"": {
                        ""type"": ""integer"",
                        ""default"": 1500,
                        ""minimum"": 100,
                        ""maximum"": 4000,
                        ""description"": ""Target response size in tokens.""
                    },
                    ""scene"": {
                        ""type"": ""string"",
                        ""description"": ""Specific scene name to snapshot. Omit for all loaded scenes.""
                    }
                },
                ""required"": []
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "scene_snapshot",
                description: "Budgeted spatial overview of GameObjects in the "
                    + "scene with positions, component lists, and clustering "
                    + "summaries. Use this for initial scene understanding.",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageGameObject,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = true
                }
            ));
        }

        private static string Execute(JToken arguments)
        {
            // Parse parameters
            Vector3? focus = null;
            float? radius = null;
            string[] includeComponents = null;
            bool excludeInactive = true;
            int maxDepth = 3;
            int budgetTokens = TokenBudget.DefaultBudget;
            string sceneName = null;

            if (arguments.HasValue)
            {
                var args = arguments.Value;

                if (args.TryGetProperty("focus", out var focusEl)
                    && focusEl.ValueKind == JsonValueKind.Array)
                {
                    var arr = focusEl;
                    focus = new Vector3(
                        arr[0].GetSingle(),
                        arr[1].GetSingle(),
                        arr[2].GetSingle());
                }

                if (args.TryGetProperty("radius", out var radiusEl))
                    radius = radiusEl.GetSingle();

                if (args.TryGetProperty("include_components", out var compEl)
                    && compEl.ValueKind == JsonValueKind.Array)
                {
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var item in compEl.EnumerateArray())
                        list.Add(item.GetString());
                    includeComponents = list.ToArray();
                }

                if (args.TryGetProperty("exclude_inactive", out var inactiveEl))
                    excludeInactive = inactiveEl.GetBoolean();

                if (args.TryGetProperty("max_depth", out var depthEl))
                    maxDepth = depthEl.GetInt32();

                if (args.TryGetProperty("budget", out var budgetEl))
                    budgetTokens = budgetEl.GetInt32();

                if (args.TryGetProperty("scene", out var sceneEl))
                    sceneName = sceneEl.GetString();
            }

            // Default focus: main camera position
            if (!focus.HasValue)
            {
                var cam = Camera.main;
                focus = cam != null ? cam.transform.position : Vector3.zero;
            }

            // Determine scene scope
            var roots = new System.Collections.Generic.List<Transform>();
            if (sceneName != null)
            {
                var scene = SceneManager.GetSceneByName(sceneName);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    return ResponseHelpers.ErrorResponse(
                        "scene_not_loaded",
                        $"Scene '{sceneName}' is not loaded",
                        "Use scene_hierarchy to see loaded scenes");
                }
                foreach (var go in scene.GetRootGameObjects())
                    roots.Add(go.transform);
            }
            else
            {
                foreach (var go in ObjectResolver.GetAllRoots())
                    roots.Add(go.transform);
            }

            // Walk hierarchy
            var entries = HierarchyWalker.Walk(
                roots, maxDepth, excludeInactive, focus, radius);

            // Apply component filter
            if (includeComponents != null)
            {
                entries = FilterByComponents(entries, includeComponents);
            }

            // Sort by distance from focus
            entries.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Compute clusters
            var clusters = Clustering.Compute(entries);

            // Build budgeted response
            var budget = new TokenBudget(budgetTokens);

            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);

            writer.WriteStartObject();

            // Scene info
            writer.WriteString("scene",
                sceneName ?? SceneManager.GetActiveScene().name);
            ResponseHelpers.WriteFrameContext(writer);
            ResponseHelpers.WriteEditingContext(writer);
            ResponseHelpers.WriteVector3(writer, "focus", focus.Value);

            // Summary
            writer.WriteStartObject("summary");
            writer.WriteNumber("total_objects", entries.Count);

            // Write clusters
            if (clusters.Count > 0)
            {
                writer.WriteStartArray("groups");
                foreach (var cluster in clusters)
                {
                    writer.WriteStartObject();
                    writer.WriteString("label", cluster.Label);
                    ResponseHelpers.WriteVector3(writer, "center", cluster.Center);
                    writer.WriteNumber("spread",
                        Math.Round(cluster.Spread, 2));
                    writer.WriteNumber("count", cluster.Count);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject(); // summary

            // Objects (budgeted)
            writer.WriteStartArray("objects");
            int returned = 0;

            foreach (var entry in entries)
            {
                if (budget.IsExhausted) break;

                var estimatedCost = TokenBudget.EstimateEntryTokens(entry);
                if (budget.WouldExceed(estimatedCost * 4))
                    break;

                writer.WriteStartObject();
                writer.WriteString("path", entry.Path);
                writer.WriteNumber("instance_id", entry.InstanceId);
                ResponseHelpers.WriteVector3(writer, "position", entry.Position);
                writer.WriteBoolean("active", entry.Active);
                writer.WriteNumber("children_count", entry.ChildrenCount);
                writer.WriteNumber("distance",
                    Math.Round(entry.Distance, 2));

                // Components list
                if (entry.Components != null && entry.Components.Length > 0)
                {
                    writer.WriteStartArray("components");
                    foreach (var comp in entry.Components)
                    {
                        if (comp != "Transform") // Always present, skip noise
                            writer.WriteStringValue(comp);
                    }
                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
                returned++;
                budget.Add(estimatedCost * 4);
            }

            writer.WriteEndArray(); // objects

            // Update summary with returned count
            // (We write it inline above, but also add returned as a field)

            // Budget info
            budget.WriteBudgetInfo(writer,
                truncated: returned < entries.Count,
                reason: returned < entries.Count ? "budget" : null,
                suggestion: returned < entries.Count
                    ? "Increase budget or use radius/include_components to narrow scope"
                    : null);

            writer.WriteNumber("returned", returned);

            writer.WriteEndObject();
            writer.Flush();

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static System.Collections.Generic.List<HierarchyEntry>
            FilterByComponents(
                System.Collections.Generic.List<HierarchyEntry> entries,
                string[] requiredComponents)
        {
            var filtered = new System.Collections.Generic.List<HierarchyEntry>();
            foreach (var entry in entries)
            {
                if (entry.Components == null) continue;
                bool hasAll = true;
                foreach (var required in requiredComponents)
                {
                    bool found = false;
                    foreach (var comp in entry.Components)
                    {
                        if (string.Equals(comp, required,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found) { hasAll = false; break; }
                }
                if (hasAll) filtered.Add(entry);
            }
            return filtered;
        }
    }
}
```

**Implementation Notes:**
- Parameters are parsed from `JToken` following the `TheatreStatusTool`
  pattern. No deserialization into a typed params object — direct property
  access keeps the code simple and avoids defining params types.
- Focus defaults to main camera position. In edit mode `Camera.main` may
  be null, so fall back to `Vector3.zero`.
- Objects are sorted by distance from focus, so the closest objects are
  included first when the budget truncates.
- The `Transform` component is omitted from the `components` array to
  reduce noise — it's always present and its data is already in `position`.
- Component filter is case-insensitive for usability — the agent might
  not know the exact casing.

**Acceptance Criteria:**
- [ ] Returns `scene`, `frame`, `time`, `play_mode`, `context` fields
- [ ] Returns `focus` field (defaults to camera position)
- [ ] `summary.total_objects` counts all objects in scope
- [ ] `summary.groups` contains cluster information when objects are grouped
- [ ] `objects` array contains entries with `path`, `instance_id`, `position`, `active`, `children_count`, `distance`
- [ ] Objects are sorted by distance from focus (nearest first)
- [ ] Response stays within requested token budget
- [ ] `budget.truncated` is true when not all objects fit
- [ ] `include_components` filter limits results to objects with specified components
- [ ] `exclude_inactive` (default true) skips inactive objects
- [ ] `max_depth` limits hierarchy traversal depth
- [ ] `radius` limits results to objects within distance of focus
- [ ] `scene` parameter scopes to a specific loaded scene
- [ ] Missing scene returns `scene_not_loaded` error
- [ ] `returned` field shows how many objects are in the response

---

### Unit 9: scene_hierarchy Tool

**File:** `Packages/com.theatre.toolkit/Editor/Tools/SceneHierarchyTool.cs`

MCP tool for hierarchy navigation: list, find, search, path operations.

```csharp
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Theatre.Editor
{
    /// <summary>
    /// MCP tool: scene_hierarchy
    /// Navigate the Transform hierarchy with list, find, search, and path operations.
    /// </summary>
    public static class SceneHierarchyTool
    {
        private static readonly JToken s_inputSchema;

        static SceneHierarchyTool()
        {
            s_inputSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""list"", ""find"", ""search"", ""path""],
                        ""description"": ""Operation to perform.""
                    },
                    ""path"": {
                        ""type"": ""string"",
                        ""description"": ""For 'list': parent path (omit for roots). For 'path': not used. For 'find'/'search': root subtree to limit search.""
                    },
                    ""pattern"": {
                        ""type"": ""string"",
                        ""description"": ""For 'find': name glob pattern (e.g., 'Scout*', '*Door*').""
                    },
                    ""instance_id"": {
                        ""type"": ""integer"",
                        ""description"": ""For 'path': get hierarchy path and metadata for this instance ID.""
                    },
                    ""include_components"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""For 'search': filter by component type.""
                    },
                    ""tag"": {
                        ""type"": ""string"",
                        ""description"": ""For 'search': filter by tag.""
                    },
                    ""layer"": {
                        ""type"": ""string"",
                        ""description"": ""For 'search': filter by layer name.""
                    },
                    ""include_inactive"": {
                        ""type"": ""boolean"",
                        ""default"": false,
                        ""description"": ""Include disabled GameObjects in results.""
                    },
                    ""cursor"": {
                        ""type"": ""string"",
                        ""description"": ""Pagination cursor from a previous response.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "scene_hierarchy",
                description: "Navigate the Transform hierarchy. Operations: "
                    + "'list' children of a path, 'find' by name pattern, "
                    + "'search' by component/tag/layer, 'path' to get info for an instance_id.",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageGameObject,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = true
                }
            ));
        }

        private static string Execute(JToken arguments)
        {
            if (!arguments.HasValue
                || !arguments.Value.TryGetProperty("operation", out var opEl))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required parameter 'operation'",
                    "Specify operation: 'list', 'find', 'search', or 'path'");
            }

            var operation = opEl.GetString();

            return operation switch
            {
                "list" => ExecuteList(arguments.Value),
                "find" => ExecuteFind(arguments.Value),
                "search" => ExecuteSearch(arguments.Value),
                "path" => ExecutePath(arguments.Value),
                _ => ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Unknown operation '{operation}'",
                    "Valid operations: 'list', 'find', 'search', 'path'")
            };
        }

        private static string ExecuteList(JToken args)
        {
            string parentPath = null;
            bool includeInactive = false;
            int offset = 0;
            const int pageSize = 50;

            if (args.TryGetProperty("path", out var pathEl))
                parentPath = pathEl.GetString();

            if (args.TryGetProperty("include_inactive", out var inactiveEl))
                includeInactive = inactiveEl.GetBoolean();

            // Handle pagination cursor
            if (args.TryGetProperty("cursor", out var cursorEl))
            {
                var cursor = PaginationCursor.Decode(
                    cursorEl.GetString(),
                    SceneManager.GetActiveScene().name);
                if (cursor == null)
                {
                    return ResponseHelpers.ErrorResponse(
                        "invalid_cursor",
                        "Pagination cursor is expired or invalid",
                        "Re-issue the original query without a cursor");
                }
                offset = cursor.Offset;
            }

            // Special case: root level with no parent path
            if (parentPath == null)
            {
                return ExecuteListRoots(includeInactive, offset, pageSize);
            }

            var (entries, total) = HierarchyWalker.ListChildren(
                parentPath, includeInactive, offset, pageSize);

            if (entries.Count == 0 && offset == 0)
            {
                // Verify parent exists
                var resolved = ObjectResolver.Resolve(path: parentPath);
                if (!resolved.Success)
                {
                    return ResponseHelpers.ErrorResponse(
                        resolved.ErrorCode,
                        resolved.ErrorMessage,
                        resolved.Suggestion);
                }
            }

            return BuildListResponse(entries, total, offset, pageSize,
                "scene_hierarchy", "list");
        }

        private static string ExecuteListRoots(
            bool includeInactive, int offset, int pageSize)
        {
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);

            writer.WriteStartObject();
            ResponseHelpers.WriteFrameContext(writer);
            ResponseHelpers.WriteEditingContext(writer);

            // List loaded scenes as top-level entries
            writer.WriteStartArray("results");

            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                var roots = scene.GetRootGameObjects();
                int activeRootCount = 0;
                if (!includeInactive)
                {
                    foreach (var root in roots)
                        if (root.activeInHierarchy) activeRootCount++;
                }
                else
                {
                    activeRootCount = roots.Length;
                }

                writer.WriteStartObject();
                writer.WriteString("scene", scene.name);
                writer.WriteNumber("root_count", activeRootCount);
                writer.WriteBoolean("active",
                    scene == SceneManager.GetActiveScene());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string ExecuteFind(JToken args)
        {
            if (!args.TryGetProperty("pattern", out var patternEl))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required parameter 'pattern' for find operation",
                    "Provide a name pattern like 'Scout*' or '*Door*'");
            }

            string root = null;
            bool includeInactive = false;

            if (args.TryGetProperty("path", out var rootEl))
                root = rootEl.GetString();
            if (args.TryGetProperty("include_inactive", out var inactiveEl))
                includeInactive = inactiveEl.GetBoolean();

            var entries = HierarchyWalker.Find(
                patternEl.GetString(), root, includeInactive);

            return BuildResultsResponse(entries);
        }

        private static string ExecuteSearch(JToken args)
        {
            var filter = new HierarchyFilter();

            if (args.TryGetProperty("include_components", out var compEl)
                && compEl.ValueKind == JsonValueKind.Array)
            {
                var list = new System.Collections.Generic.List<string>();
                foreach (var item in compEl.EnumerateArray())
                    list.Add(item.GetString());
                filter.RequiredComponents = list.ToArray();
            }

            if (args.TryGetProperty("tag", out var tagEl))
                filter.Tag = tagEl.GetString();

            if (args.TryGetProperty("layer", out var layerEl))
                filter.Layer = layerEl.GetString();

            if (args.TryGetProperty("include_inactive", out var inactiveEl))
                filter.IncludeInactive = inactiveEl.GetBoolean();

            string root = null;
            if (args.TryGetProperty("path", out var rootEl))
                root = rootEl.GetString();

            if (filter.RequiredComponents == null
                && filter.Tag == null
                && filter.Layer == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Search requires at least one filter: include_components, tag, or layer",
                    "Specify include_components, tag, or layer to search by");
            }

            var entries = HierarchyWalker.Search(filter, root);
            return BuildResultsResponse(entries);
        }

        private static string ExecutePath(JToken args)
        {
            if (!args.TryGetProperty("instance_id", out var idEl))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required parameter 'instance_id' for path operation",
                    "Provide the instance_id of the object to look up");
            }

            var resolved = ObjectResolver.Resolve(instanceId: idEl.GetInt32());
            if (!resolved.Success)
            {
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode,
                    resolved.ErrorMessage,
                    resolved.Suggestion);
            }

            var go = resolved.GameObject;
            var t = go.transform;

            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);

            writer.WriteStartObject();
            ResponseHelpers.WriteFrameContext(writer);
            writer.WriteStartObject("result");
            writer.WriteString("path",
                ResponseHelpers.GetHierarchyPath(t));
            writer.WriteNumber("instance_id", go.GetInstanceID());
            writer.WriteString("name", go.name);
            writer.WriteString("scene", go.scene.name);
            ResponseHelpers.WriteVector3(writer, "position", t.position);
            writer.WriteBoolean("active", go.activeInHierarchy);
            writer.WriteNumber("children_count", t.childCount);

            // Parent info
            if (t.parent != null)
            {
                writer.WriteStartObject("parent");
                writer.WriteString("path",
                    ResponseHelpers.GetHierarchyPath(t.parent));
                writer.WriteNumber("instance_id",
                    t.parent.gameObject.GetInstanceID());
                writer.WriteEndObject();
            }

            writer.WriteEndObject(); // result
            writer.WriteEndObject();
            writer.Flush();

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        // --- Response builders ---

        private static string BuildListResponse(
            System.Collections.Generic.List<HierarchyEntry> entries,
            int total, int offset, int pageSize,
            string tool, string operation)
        {
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);

            writer.WriteStartObject();
            ResponseHelpers.WriteFrameContext(writer);

            writer.WriteStartArray("results");
            foreach (var entry in entries)
            {
                WriteEntryJson(writer, entry);
            }
            writer.WriteEndArray();

            // Pagination
            bool hasMore = offset + entries.Count < total;
            string cursor = hasMore
                ? PaginationCursor.Create(tool, operation,
                    offset + entries.Count)
                : null;
            PaginationCursor.WritePagination(writer, cursor, hasMore,
                entries.Count, total);

            writer.WriteEndObject();
            writer.Flush();

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string BuildResultsResponse(
            System.Collections.Generic.List<HierarchyEntry> entries)
        {
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);

            writer.WriteStartObject();
            ResponseHelpers.WriteFrameContext(writer);

            writer.WriteStartArray("results");
            foreach (var entry in entries)
            {
                WriteEntryJson(writer, entry);
            }
            writer.WriteEndArray();

            writer.WriteNumber("returned", entries.Count);
            writer.WriteEndObject();
            writer.Flush();

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void WriteEntryJson(
            Utf8JsonWriter writer, HierarchyEntry entry)
        {
            writer.WriteStartObject();
            writer.WriteString("path", entry.Path);
            writer.WriteNumber("instance_id", entry.InstanceId);
            writer.WriteString("name", entry.Name);
            ResponseHelpers.WriteVector3(writer, "position", entry.Position);
            writer.WriteBoolean("active", entry.Active);
            writer.WriteNumber("children_count", entry.ChildrenCount);

            if (entry.Components != null && entry.Components.Length > 0)
            {
                writer.WriteStartArray("components");
                foreach (var comp in entry.Components)
                    writer.WriteStringValue(comp);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
    }
}
```

**Implementation Notes:**
- The `list` operation at root level is special: it returns loaded scenes
  as entries (scene name + root count + active flag) rather than individual
  GameObjects. This matches the STAGE-SURFACE.md design.
- `find` uses glob patterns (`*`, `?`). The HierarchyWalker converts
  these to regex internally.
- `search` requires at least one filter to prevent accidental full-scene
  enumeration.
- `path` is the reverse lookup: given an instance_id, return the full
  hierarchy path with context. Useful when the agent has an instance_id
  from a previous query and wants the human-readable path.
- Pagination is implemented for `list` only. `find` and `search` use
  `maxResults` limits instead. This is intentional — find/search results
  are not stable across calls (scene may change), so offset-based
  pagination is unreliable. The agent should narrow its search instead.

**Acceptance Criteria:**
- [ ] `list` with no path returns loaded scenes as entries
- [ ] `list` with path returns direct children
- [ ] `list` pagination works: cursor carries offset, next page returns correct items
- [ ] `list` with invalid cursor returns `invalid_cursor` error
- [ ] `find` with pattern `"Scout*"` returns matching objects
- [ ] `find` with root limits search to subtree
- [ ] `search` with `include_components: ["Rigidbody"]` returns only objects with Rigidbody
- [ ] `search` with `tag: "Player"` returns tagged objects
- [ ] `search` with no filters returns `invalid_parameter` error
- [ ] `path` with valid instance_id returns full path, name, scene, parent
- [ ] `path` with invalid instance_id returns `gameobject_not_found` error
- [ ] All responses include `frame`, `time`, `play_mode` fields
- [ ] All entries have both `path` and `instance_id`
- [ ] Missing `operation` parameter returns `invalid_parameter` error
- [ ] Unknown operation returns `invalid_parameter` error

---

### Unit 10: scene_inspect Tool

**File:** `Packages/com.theatre.toolkit/Editor/Tools/SceneInspectTool.cs`

MCP tool for deep single-object inspection via SerializedProperty.

```csharp
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Theatre.Editor
{
    /// <summary>
    /// MCP tool: scene_inspect
    /// Deep inspection of a single GameObject — all components, serialized
    /// properties, references, hierarchy context.
    /// </summary>
    public static class SceneInspectTool
    {
        private static readonly JToken s_inputSchema;

        static SceneInspectTool()
        {
            s_inputSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""path"": {
                        ""type"": ""string"",
                        ""description"": ""Hierarchy path (e.g., '/Player'). Use this or instance_id.""
                    },
                    ""instance_id"": {
                        ""type"": ""integer"",
                        ""description"": ""InstanceID. Use this or path.""
                    },
                    ""components"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Filter to specific component types.""
                    },
                    ""depth"": {
                        ""type"": ""string"",
                        ""enum"": [""summary"", ""full"", ""properties""],
                        ""default"": ""summary"",
                        ""description"": ""Detail level: 'summary' (default), 'full', or 'properties' (includes hidden fields).""
                    },
                    ""budget"": {
                        ""type"": ""integer"",
                        ""default"": 1500,
                        ""minimum"": 100,
                        ""maximum"": 4000,
                        ""description"": ""Target token budget.""
                    }
                },
                ""required"": []
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "scene_inspect",
                description: "Deep inspection of a single GameObject. Returns "
                    + "all components with serialized properties. Use 'depth' to "
                    + "control detail level and 'components' to filter.",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageGameObject,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = true
                }
            ));
        }

        private static string Execute(JToken arguments)
        {
            // Parse parameters
            string path = null;
            int? instanceId = null;
            string[] componentFilter = null;
            var detail = PropertySerializer.DetailLevel.Summary;
            int budgetTokens = TokenBudget.DefaultBudget;

            if (arguments.HasValue)
            {
                var args = arguments.Value;

                if (args.TryGetProperty("path", out var pathEl))
                    path = pathEl.GetString();

                if (args.TryGetProperty("instance_id", out var idEl))
                    instanceId = idEl.GetInt32();

                if (args.TryGetProperty("components", out var compEl)
                    && compEl.ValueKind == JsonValueKind.Array)
                {
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var item in compEl.EnumerateArray())
                        list.Add(item.GetString());
                    componentFilter = list.ToArray();
                }

                if (args.TryGetProperty("depth", out var depthEl))
                {
                    detail = depthEl.GetString() switch
                    {
                        "full" => PropertySerializer.DetailLevel.Full,
                        "properties" => PropertySerializer.DetailLevel.Properties,
                        _ => PropertySerializer.DetailLevel.Summary
                    };
                }

                if (args.TryGetProperty("budget", out var budgetEl))
                    budgetTokens = budgetEl.GetInt32();
            }

            // Resolve the target object
            var resolved = ObjectResolver.Resolve(path, instanceId);
            if (!resolved.Success)
            {
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode,
                    resolved.ErrorMessage,
                    resolved.Suggestion);
            }

            var go = resolved.GameObject;
            var transform = go.transform;
            var budget = new TokenBudget(budgetTokens);

            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);

            writer.WriteStartObject();
            ResponseHelpers.WriteFrameContext(writer);

            // Object identity
            writer.WriteString("path",
                ResponseHelpers.GetHierarchyPath(transform));
            writer.WriteNumber("instance_id", go.GetInstanceID());

            // GameObject metadata
            writer.WriteString("tag", go.tag);
            writer.WriteString("layer", LayerMask.LayerToName(go.layer));
            WriteStaticFlags(writer, go);
            writer.WriteBoolean("active_self", go.activeSelf);
            writer.WriteBoolean("active_hierarchy", go.activeInHierarchy);
            writer.WriteString("scene", go.scene.name);

            // Prefab info
            WritePrefabInfo(writer, go);

            // Components
            writer.WriteStartArray("components");
            PropertySerializer.WriteComponents(writer, go, detail,
                componentFilter, budget);
            writer.WriteEndArray();

            // Children summary
            writer.WriteStartArray("children");
            int childCount = Math.Min(transform.childCount, 20);
            for (int i = 0; i < childCount; i++)
            {
                var child = transform.GetChild(i);
                writer.WriteStartObject();
                writer.WriteString("path",
                    ResponseHelpers.GetHierarchyPath(child));
                writer.WriteNumber("instance_id",
                    child.gameObject.GetInstanceID());
                writer.WriteNumber("children_count", child.childCount);
                writer.WriteEndObject();
            }
            if (transform.childCount > 20)
            {
                writer.WriteStartObject();
                writer.WriteString("_truncated",
                    $"{transform.childCount - 20} more children");
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            // Budget info
            budget.WriteBudgetInfo(writer,
                truncated: budget.IsExhausted,
                reason: budget.IsExhausted ? "budget" : null,
                suggestion: budget.IsExhausted
                    ? "Use 'components' filter to inspect specific components"
                    : null);

            writer.WriteEndObject();
            writer.Flush();

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// Write static flags as an array of flag names.
        /// </summary>
        private static void WriteStaticFlags(Utf8JsonWriter writer, GameObject go)
        {
#if UNITY_EDITOR
            var flags = UnityEditor.GameObjectUtility.GetStaticEditorFlags(go);
            writer.WriteStartArray("static_flags");

            if (flags.HasFlag(UnityEditor.StaticEditorFlags.ContributeGI))
                writer.WriteStringValue("contribute_gi");
            if (flags.HasFlag(UnityEditor.StaticEditorFlags.OccluderStatic))
                writer.WriteStringValue("occluder");
            if (flags.HasFlag(UnityEditor.StaticEditorFlags.OccludeeStatic))
                writer.WriteStringValue("occludee");
            if (flags.HasFlag(UnityEditor.StaticEditorFlags.BatchingStatic))
                writer.WriteStringValue("batching");
            if (flags.HasFlag(UnityEditor.StaticEditorFlags.NavigationStatic))
                writer.WriteStringValue("navigation");
            if (flags.HasFlag(UnityEditor.StaticEditorFlags.OffMeshLinkGeneration))
                writer.WriteStringValue("off_mesh_link");
            if (flags.HasFlag(UnityEditor.StaticEditorFlags.ReflectionProbeStatic))
                writer.WriteStringValue("reflection_probe");

            writer.WriteEndArray();
#else
            writer.WriteStartArray("static_flags");
            writer.WriteEndArray();
#endif
        }

        /// <summary>
        /// Write prefab instance information.
        /// </summary>
        private static void WritePrefabInfo(Utf8JsonWriter writer, GameObject go)
        {
#if UNITY_EDITOR
            var isPrefab = UnityEditor.PrefabUtility.IsPartOfAnyPrefab(go);
            writer.WriteBoolean("is_prefab_instance", isPrefab);
            if (isPrefab)
            {
                var prefabAsset = UnityEditor.PrefabUtility
                    .GetCorrespondingObjectFromOriginalSource(go);
                if (prefabAsset != null)
                {
                    var assetPath = UnityEditor.AssetDatabase
                        .GetAssetPath(prefabAsset);
                    if (!string.IsNullOrEmpty(assetPath))
                        writer.WriteString("prefab_asset", assetPath);
                }
            }
#else
            writer.WriteBoolean("is_prefab_instance", false);
#endif
        }
    }
}
```

**Implementation Notes:**
- `scene_inspect` accepts either `path` or `instance_id` (or both). The
  `ObjectResolver` handles resolution.
- Children are capped at 20 entries in the response to prevent oversized
  output. If there are more, a `_truncated` marker tells the agent to use
  `scene_hierarchy` to list them.
- Static flags are reported as an array of human-readable names rather than
  a bitmask integer. This is more useful for agents.
- Prefab info includes `is_prefab_instance` (always) and `prefab_asset`
  (path to the prefab asset, when available).
- The `depth` parameter controls how much detail is included for components:
  - `summary`: type name + 3 key properties (fast, low token cost)
  - `full`: all visible serialized properties
  - `properties`: all properties including hidden/debug ones

**Acceptance Criteria:**
- [ ] Returns `path`, `instance_id`, `tag`, `layer`, `static_flags`, `active_self`, `active_hierarchy`, `scene`
- [ ] `is_prefab_instance` is true for prefab instances with `prefab_asset` path
- [ ] `components` array contains serialized component data
- [ ] `depth: "summary"` returns type + key properties for each component
- [ ] `depth: "full"` returns all visible serialized properties
- [ ] `depth: "properties"` includes hidden fields
- [ ] `components` filter limits output to specified component types
- [ ] `children` array lists up to 20 children with path, instance_id, children_count
- [ ] Children truncated at 20 with `_truncated` marker
- [ ] Budget metadata is included
- [ ] Missing object returns `gameobject_not_found` error
- [ ] Both `path` and `instance_id` are accepted as input
- [ ] Response includes frame context (frame, time, play_mode)
- [ ] Layer is reported as name, not integer

---

### Unit 11: Tool Registration in TheatreServer

**File:** `Packages/com.theatre.toolkit/Editor/TheatreServer.cs`

Update `RegisterBuiltInTools` to include the three new tools.

```csharp
// In TheatreServer.RegisterBuiltInTools:
private static void RegisterBuiltInTools(ToolRegistry registry)
{
    TheatreStatusTool.Register(registry);
    SceneSnapshotTool.Register(registry);
    SceneHierarchyTool.Register(registry);
    SceneInspectTool.Register(registry);
}
```

**Implementation Notes:**
- All three tools register under `ToolGroup.StageGameObject`, so they are
  visible whenever that group is enabled (which it is by default via
  `GameObjectProject`).
- Registration order does not matter — `tools/list` returns tools in
  insertion order, but MCP clients do not depend on order.

**Acceptance Criteria:**
- [ ] `tools/list` response includes `scene_snapshot`, `scene_hierarchy`, `scene_inspect`
- [ ] All three tools have `readOnlyHint: true` in annotations
- [ ] Disabling `StageGameObject` group hides all three tools
- [ ] All three tools are callable via `tools/call`

---

### Unit 12: Test Scene Fixture

**File:** `TestProject/Assets/Scenes/TestScene_Hierarchy.unity`

This is a Unity scene file created in the editor (not hand-written). The
design specifies the required layout; the implementer creates it in Unity.

**Required layout:**

```
TestScene_Hierarchy (scene)
├── Player                    (tag: Player, layer: Default)
│   ├── Camera                (Camera component)
│   ├── Model
│   │   ├── Body
│   │   ├── Head
│   │   └── Weapon
│   └── UI
│       └── HealthBar
├── Environment
│   ├── Floor                 (BoxCollider, scale 100x1x100)
│   ├── Wall_01               (position: [5, 1, 0])
│   ├── Wall_02               (position: [-5, 1, 0])
│   └── Pillar                (position: [0, 1, 3])
├── Enemies
│   ├── Scout_01              (position: [10, 0, 5])
│   ├── Scout_02              (position: [12, 0, 6])
│   ├── Scout_03              (position: [11, 0, 4])
│   ├── Heavy_01              (position: [-8, 0, -3])
│   └── Heavy_02              (position: [-9, 0, -4])
├── Collectibles
│   ├── Coin_01               (position: [3, 0.5, 2])
│   ├── Coin_02               (position: [3.5, 0.5, 2.2])
│   ├── Coin_03               (position: [4, 0.5, 1.8])
│   └── HealthPack            (position: [0, 0.5, -5])
└── Lights
    ├── DirectionalLight
    └── PointLight            (position: [0, 5, 0])
```

**Design constraints:**
- All objects are empty GameObjects with Transform only, except where
  noted (Camera, BoxCollider).
- Positions are fixed integer/half-integer values for deterministic test assertions.
- Scout enemies are clustered near [11, 0, 5] — tests verify clustering.
- Heavy enemies are clustered near [-8.5, 0, -3.5] — second cluster.
- Coins are clustered near [3.5, 0.5, 2] — third cluster.
- Player is at origin [0, 0, 0] for easy distance calculations.
- Hierarchy is 4 levels deep (Player/Model/Body = 3 levels under root).

**Acceptance Criteria:**
- [ ] Scene loads without errors
- [ ] All objects are at specified positions
- [ ] Player has "Player" tag
- [ ] Hierarchy depth matches design (4 levels max)
- [ ] No scripts or components beyond those specified (keeps tests deterministic)

---

### Unit 13: Unit Tests

**File:** `Packages/com.theatre.toolkit/Tests/Editor/SceneAwarenessTests.cs`

Tests for the non-tool infrastructure: ResponseHelpers, ObjectResolver,
HierarchyWalker, TokenBudget, PaginationCursor, Clustering.

```csharp
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Theatre.Stage;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class ResponseHelpersTests
    {
        [Test]
        public void WriteVector3_ProducesCorrectArray()
        {
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            ResponseHelpers.WriteVector3(writer, "pos", new Vector3(1.234f, 5.678f, 9.012f));
            writer.WriteEndObject();
            writer.Flush();
            var json = Encoding.UTF8.GetString(stream.ToArray());
            Assert.That(json, Does.Contain("\"pos\":[1.23,5.68,9.01]"));
        }

        [Test]
        public void ErrorResponse_ContainsAllFields()
        {
            var json = ResponseHelpers.ErrorResponse(
                "test_error", "Something failed", "Try again");
            Assert.That(json, Does.Contain("\"code\":\"test_error\""));
            Assert.That(json, Does.Contain("\"message\":\"Something failed\""));
            Assert.That(json, Does.Contain("\"suggestion\":\"Try again\""));
        }

        [Test]
        public void ToSnakeCase_ConvertsCorrectly()
        {
            Assert.AreEqual("local_position",
                PropertySerializer.ToSnakeCase("localPosition"));
            Assert.AreEqual("is_grounded",
                PropertySerializer.ToSnakeCase("isGrounded"));
            Assert.AreEqual("active_self",
                PropertySerializer.ToSnakeCase("activeSelf"));
        }
    }

    [TestFixture]
    public class TokenBudgetTests
    {
        [Test]
        public void DefaultBudget_Is1500()
        {
            Assert.AreEqual(1500, TokenBudget.DefaultBudget);
        }

        [Test]
        public void HardCap_ClampsLargeBudget()
        {
            var budget = new TokenBudget(10000);
            Assert.AreEqual(TokenBudget.HardCap, budget.Budget);
        }

        [Test]
        public void MinimumBudget_Clamps()
        {
            var budget = new TokenBudget(10);
            Assert.AreEqual(100, budget.Budget);
        }

        [Test]
        public void WouldExceed_ReturnsTrueAtLimit()
        {
            var budget = new TokenBudget(100); // 100 tokens = 400 chars
            budget.Add(380);
            Assert.IsFalse(budget.IsExhausted);
            Assert.IsTrue(budget.WouldExceed(100)); // 480/4 = 120 > 100
        }

        [Test]
        public void EstimateTokens_FourCharsPerToken()
        {
            Assert.AreEqual(25, TokenBudget.EstimateTokens("a]" + new string('x', 98)));
        }
    }

    [TestFixture]
    public class PaginationCursorTests
    {
        [Test]
        public void Encode_Decode_RoundTrips()
        {
            var sceneName = SceneManager.GetActiveScene().name;
            var encoded = PaginationCursor.Create(
                "scene_hierarchy", "list", 50);
            var decoded = PaginationCursor.Decode(encoded, sceneName);

            Assert.IsNotNull(decoded);
            Assert.AreEqual("scene_hierarchy", decoded.Tool);
            Assert.AreEqual("list", decoded.Operation);
            Assert.AreEqual(50, decoded.Offset);
        }

        [Test]
        public void Decode_ReturnsNull_ForSceneChange()
        {
            var encoded = PaginationCursor.Create(
                "scene_hierarchy", "list", 50);
            var decoded = PaginationCursor.Decode(encoded, "DifferentScene");
            Assert.IsNull(decoded);
        }

        [Test]
        public void Decode_ReturnsNull_ForGarbage()
        {
            var decoded = PaginationCursor.Decode("not-valid-base64!!!", "scene");
            Assert.IsNull(decoded);
        }
    }

    [TestFixture]
    public class ClusteringTests
    {
        [Test]
        public void Compute_GroupsNearbyObjects()
        {
            var entries = new System.Collections.Generic.List<HierarchyEntry>();
            // Cluster 1: three objects near (10, 0, 5)
            entries.Add(new HierarchyEntry { Position = new Vector3(10, 0, 5), Name = "A", Path = "/Enemies/A", Components = new[] { "Transform" } });
            entries.Add(new HierarchyEntry { Position = new Vector3(11, 0, 6), Name = "B", Path = "/Enemies/B", Components = new[] { "Transform" } });
            entries.Add(new HierarchyEntry { Position = new Vector3(10.5f, 0, 5.5f), Name = "C", Path = "/Enemies/C", Components = new[] { "Transform" } });

            var clusters = Clustering.Compute(entries);
            Assert.AreEqual(1, clusters.Count);
            Assert.AreEqual(3, clusters[0].Count);
        }

        [Test]
        public void Compute_ReturnsEmpty_ForSingleObject()
        {
            var entries = new System.Collections.Generic.List<HierarchyEntry>
            {
                new HierarchyEntry { Position = Vector3.zero, Name = "Solo" }
            };
            var clusters = Clustering.Compute(entries);
            Assert.AreEqual(0, clusters.Count);
        }

        [Test]
        public void GetUnclustered_ReturnsSingletons()
        {
            var entries = new System.Collections.Generic.List<HierarchyEntry>();
            // Cluster: two nearby
            entries.Add(new HierarchyEntry { Position = new Vector3(0, 0, 0), Name = "A", Path = "/A" });
            entries.Add(new HierarchyEntry { Position = new Vector3(0.1f, 0, 0), Name = "B", Path = "/B" });
            // Singleton: far away
            entries.Add(new HierarchyEntry { Position = new Vector3(100, 0, 100), Name = "Solo", Path = "/Solo" });

            var unclustered = Clustering.GetUnclustered(entries);
            Assert.AreEqual(1, unclustered.Count);
            Assert.AreEqual("Solo", unclustered[0].Name);
        }
    }

    [TestFixture]
    public class ObjectResolverTests
    {
        private GameObject _testObject;

        [SetUp]
        public void SetUp()
        {
            _testObject = new GameObject("TestResolverTarget");
        }

        [TearDown]
        public void TearDown()
        {
            if (_testObject != null)
                Object.DestroyImmediate(_testObject);
        }

        [Test]
        public void Resolve_ByPath_FindsObject()
        {
            var result = ObjectResolver.Resolve(path: "/TestResolverTarget");
            Assert.IsTrue(result.Success);
            Assert.AreEqual(_testObject, result.GameObject);
        }

        [Test]
        public void Resolve_ByInstanceId_FindsObject()
        {
            var result = ObjectResolver.Resolve(
                instanceId: _testObject.GetInstanceID());
            Assert.IsTrue(result.Success);
            Assert.AreEqual(_testObject, result.GameObject);
        }

        [Test]
        public void Resolve_NoParams_ReturnsError()
        {
            var result = ObjectResolver.Resolve();
            Assert.IsFalse(result.Success);
            Assert.AreEqual("invalid_parameter", result.ErrorCode);
        }

        [Test]
        public void Resolve_NonexistentPath_ReturnsError()
        {
            var result = ObjectResolver.Resolve(path: "/DoesNotExist");
            Assert.IsFalse(result.Success);
            Assert.AreEqual("gameobject_not_found", result.ErrorCode);
        }
    }
}
```

**Implementation Notes:**
- Tests are EditMode tests in the `Theatre.Tests.Editor` namespace.
- `ObjectResolverTests` creates temporary GameObjects in SetUp and destroys
  them in TearDown. These run in the editor's active scene.
- `ClusteringTests` use synthetic `HierarchyEntry` data — no scene needed.
- `TokenBudgetTests` are pure math — no Unity dependencies.
- Integration tests that require the test scene fixture (Unit 12) are
  in a separate test class (see Unit 14).

**Acceptance Criteria:**
- [ ] All tests pass in Unity Test Runner (EditMode)
- [ ] `ResponseHelpersTests` validates vector serialization and error format
- [ ] `TokenBudgetTests` validates budget clamping and exhaustion detection
- [ ] `PaginationCursorTests` validates round-trip and expiry
- [ ] `ClusteringTests` validates grouping logic
- [ ] `ObjectResolverTests` validates path and instance_id resolution

---

### Unit 14: Integration Tests with Test Scene

**File:** `Packages/com.theatre.toolkit/Tests/Editor/SceneToolIntegrationTests.cs`

End-to-end tests that call the MCP tools against the test scene fixture.

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Theatre.Editor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Theatre.Tests.Editor
{
    /// <summary>
    /// Integration tests for scene awareness tools.
    /// Requires TestScene_Hierarchy.unity to be set up per the design.
    /// </summary>
    [TestFixture]
    public class SceneToolIntegrationTests
    {
        private const string TestScenePath =
            "Assets/Scenes/TestScene_Hierarchy.unity";

        [OneTimeSetUp]
        public void LoadTestScene()
        {
            EditorSceneManager.OpenScene(TestScenePath,
                OpenSceneMode.Single);
        }

        [Test]
        public void SceneSnapshot_ReturnsObjects()
        {
            var args = JObject.Parse(
                @"{ ""budget"": 2000 }");
            var result = CallTool("scene_snapshot", args);

            Assert.That(result, Does.Contain("\"scene\""));
            Assert.That(result, Does.Contain("\"objects\""));
            Assert.That(result, Does.Contain("\"Player\""));
            Assert.That(result, Does.Contain("\"frame\""));
            Assert.That(result, Does.Contain("\"budget\""));
        }

        [Test]
        public void SceneSnapshot_RespectsRadius()
        {
            // Player is at origin. Scouts are at ~[11, 0, 5]. Radius 3 should exclude scouts.
            var args = JObject.Parse(
                @"{ ""focus"": [0, 0, 0], ""radius"": 3, ""budget"": 2000 }");
            var result = CallTool("scene_snapshot", args);

            Assert.That(result, Does.Not.Contain("\"Scout_01\""));
        }

        [Test]
        public void SceneSnapshot_ClustersEnemies()
        {
            var args = JObject.Parse(
                @"{ ""budget"": 2000 }");
            var result = CallTool("scene_snapshot", args);

            // Should have cluster summaries
            Assert.That(result, Does.Contain("\"groups\""));
        }

        [Test]
        public void SceneHierarchy_ListRoots()
        {
            var args = JObject.Parse(
                @"{ ""operation"": ""list"" }");
            var result = CallTool("scene_hierarchy", args);

            Assert.That(result, Does.Contain("\"results\""));
            Assert.That(result, Does.Contain("\"TestScene_Hierarchy\""));
        }

        [Test]
        public void SceneHierarchy_ListChildren()
        {
            var args = JObject.Parse(
                @"{ ""operation"": ""list"", ""path"": ""/Enemies"" }");
            var result = CallTool("scene_hierarchy", args);

            Assert.That(result, Does.Contain("\"Scout_01\""));
            Assert.That(result, Does.Contain("\"Heavy_01\""));
        }

        [Test]
        public void SceneHierarchy_FindByPattern()
        {
            var args = JObject.Parse(
                @"{ ""operation"": ""find"", ""pattern"": ""Scout*"" }");
            var result = CallTool("scene_hierarchy", args);

            Assert.That(result, Does.Contain("\"Scout_01\""));
            Assert.That(result, Does.Contain("\"Scout_02\""));
            Assert.That(result, Does.Contain("\"Scout_03\""));
            Assert.That(result, Does.Not.Contain("\"Heavy_01\""));
        }

        [Test]
        public void SceneHierarchy_SearchByTag()
        {
            var args = JObject.Parse(
                @"{ ""operation"": ""search"", ""tag"": ""Player"" }");
            var result = CallTool("scene_hierarchy", args);

            Assert.That(result, Does.Contain("\"Player\""));
            Assert.That(result, Does.Not.Contain("\"Scout\""));
        }

        [Test]
        public void SceneInspect_ByPath()
        {
            var args = JObject.Parse(
                @"{ ""path"": ""/Player"", ""depth"": ""full"" }");
            var result = CallTool("scene_inspect", args);

            Assert.That(result, Does.Contain("\"path\":\"/Player\""));
            Assert.That(result, Does.Contain("\"instance_id\""));
            Assert.That(result, Does.Contain("\"tag\":\"Player\""));
            Assert.That(result, Does.Contain("\"components\""));
            Assert.That(result, Does.Contain("\"children\""));
        }

        [Test]
        public void SceneInspect_NotFound()
        {
            var args = JObject.Parse(
                @"{ ""path"": ""/DoesNotExist"" }");
            var result = CallTool("scene_inspect", args);

            Assert.That(result, Does.Contain("\"error\""));
            Assert.That(result, Does.Contain("\"gameobject_not_found\""));
            Assert.That(result, Does.Contain("\"suggestion\""));
        }

        [Test]
        public void SceneInspect_ComponentFilter()
        {
            var args = JObject.Parse(
                @"{ ""path"": ""/Player/Camera"", ""components"": [""Camera""], ""depth"": ""full"" }");
            var result = CallTool("scene_inspect", args);

            Assert.That(result, Does.Contain("\"Camera\""));
            // Transform should be filtered out
            Assert.That(result, Does.Not.Contain("\"type\":\"Transform\""));
        }

        // --- Helper ---

        private string CallTool(string toolName, JToken args)
        {
            var tool = TheatreServer.ToolRegistry?.GetTool(
                toolName,
                Theatre.ToolGroup.Everything);
            Assert.IsNotNull(tool,
                $"Tool '{toolName}' not found in registry");
            return tool.Handler(args);
        }
    }
}
```

**Implementation Notes:**
- These tests load the test scene in `OneTimeSetUp` so all tests in the
  fixture run against the same scene state.
- Tests call tool handlers directly (not via HTTP) to avoid HTTP transport
  dependencies. The handlers run on the main thread in EditMode tests,
  which is the same thread context as normal tool execution.
- Tests validate JSON string content with `Does.Contain` — not parsed
  structure. This is intentional: we're testing tool output format, not
  JSON parsing. More specific structural assertions can be added as needed.

**Acceptance Criteria:**
- [ ] All tests pass when TestScene_Hierarchy is set up per Unit 12
- [ ] Snapshot returns objects with positions and budget metadata
- [ ] Snapshot respects radius filter
- [ ] Snapshot produces cluster summaries
- [ ] Hierarchy list shows scene roots
- [ ] Hierarchy list shows children of a path
- [ ] Hierarchy find matches glob patterns
- [ ] Hierarchy search filters by tag
- [ ] Inspect returns full component data for a known object
- [ ] Inspect returns error for nonexistent object
- [ ] Inspect component filter limits output

---

## File Summary

| Unit | File Path | Assembly | Description |
|------|-----------|----------|-------------|
| 1 | `Runtime/Stage/ResponseHelpers.cs` | Runtime | Vector encoding, frame context, error builder, path builder |
| 2 | `Runtime/Stage/GameObject/ObjectResolver.cs` | Runtime | Path/instance_id to GameObject resolution |
| 3 | `Runtime/Stage/GameObject/HierarchyWalker.cs` | Runtime | Transform tree traversal, find, search |
| 4 | `Runtime/Stage/Spatial/TokenBudget.cs` | Runtime | Token estimation and budget tracking |
| 5 | `Runtime/Stage/Spatial/PaginationCursor.cs` | Runtime | Base64 cursor encode/decode with expiry |
| 6 | `Runtime/Stage/Spatial/Clustering.cs` | Runtime | Grid-based spatial clustering |
| 7 | `Editor/Tools/PropertySerializer.cs` | Editor | SerializedProperty to JSON traversal |
| 8 | `Editor/Tools/SceneSnapshotTool.cs` | Editor | `scene_snapshot` MCP tool |
| 9 | `Editor/Tools/SceneHierarchyTool.cs` | Editor | `scene_hierarchy` MCP tool |
| 10 | `Editor/Tools/SceneInspectTool.cs` | Editor | `scene_inspect` MCP tool |
| 11 | `Editor/TheatreServer.cs` | Editor | RegisterBuiltInTools update |
| 12 | `TestProject/Assets/Scenes/TestScene_Hierarchy.unity` | — | Test scene fixture |
| 13 | `Tests/Editor/SceneAwarenessTests.cs` | Tests | Unit tests for infrastructure |
| 14 | `Tests/Editor/SceneToolIntegrationTests.cs` | Tests | Integration tests against test scene |

All `Runtime/` paths are relative to `Packages/com.theatre.toolkit/Runtime/`.
All `Editor/` paths are relative to `Packages/com.theatre.toolkit/Editor/`.
All `Tests/` paths are relative to `Packages/com.theatre.toolkit/Tests/`.

---

## Implementation Order

1. **Unit 1: ResponseHelpers** — no dependencies, used by everything else
2. **Unit 4: TokenBudget** — no dependencies, used by tools
3. **Unit 5: PaginationCursor** — no dependencies, used by hierarchy tool
4. **Unit 2: ObjectResolver** — depends on ResponseHelpers
5. **Unit 3: HierarchyWalker** — depends on ObjectResolver, ResponseHelpers
6. **Unit 6: Clustering** — depends on HierarchyEntry struct from HierarchyWalker
7. **Unit 7: PropertySerializer** — depends on ResponseHelpers, TokenBudget
8. **Unit 8: SceneSnapshotTool** — depends on all of the above
9. **Unit 9: SceneHierarchyTool** — depends on HierarchyWalker, ObjectResolver, PaginationCursor
10. **Unit 10: SceneInspectTool** — depends on ObjectResolver, PropertySerializer, TokenBudget
11. **Unit 11: TheatreServer registration** — depends on all tools
12. **Unit 12: Test scene** — create in Unity Editor
13. **Unit 13: Unit tests** — depends on Units 1-6
14. **Unit 14: Integration tests** — depends on everything

---

## Not In Scope (Deferred to Later Phases)

- **scene_delta** — deferred to Phase 4 (Watches & Actions). Requires frame
  state tracking infrastructure that does not yet exist.
- **Spatial index** — deferred to Phase 3 (Spatial Queries). scene_snapshot
  uses linear scans which are fast enough for scenes with <10,000 objects.
- **Watch notifications** — Phase 4.
- **Recording integration** — Phase 5.
- **Play mode-specific behavior** (velocities, physics state) — snapshot
  and inspect work in both modes, but runtime-only properties like velocity
  are naturally available only in play mode via the SerializedProperty
  traversal.
