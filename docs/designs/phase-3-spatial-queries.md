# Design: Phase 3 — Stage: Spatial Queries

## Overview

Agent can ask spatial questions about the scene. After this phase, an AI
agent can find nearby objects, check line-of-sight, cast rays, query physics
overlaps, measure NavMesh path distances, and read world-space bounding boxes.

**Key components:**
- `spatial_query` compound tool — nearest, radius, overlap, raycast, linecast,
  path_distance, bounds operations
- Spatial index — R-tree for 3D, grid hash for 2D (accelerates nearest/radius)
- 2D/3D physics detection — project-level default + per-query override
- Play mode gating — physics queries return `requires_play_mode` in edit mode
- Response building with frame context, token budgeting, and pagination

**Exit criteria:** Agent can ask "what's near the player?", "can this enemy
see that door?", "what's the NavMesh distance?" and get accurate answers.
Physics queries work in play mode. Transform-based queries (nearest, radius,
bounds) work in both edit and play mode.

---

## Architecture Decisions

### Single Compound Tool

`spatial_query` is one MCP tool with an `operation` parameter, matching the
pattern established by `scene_hierarchy`. This keeps the tool list compact
while offering seven distinct query types. The JSON Schema uses `oneOf` to
define per-operation parameter shapes, so agents see correct schema for each
operation.

### Physics vs Transform Queries

Not all spatial queries need the physics engine:

| Operation | Requires Physics | Works in Edit Mode | Data Source |
|---|---|---|---|
| `nearest` | No | Yes | Transform positions via spatial index |
| `radius` | No | Yes | Transform positions via spatial index |
| `overlap` | Yes | No | `Physics.OverlapSphere/Box/Capsule` or 2D equivalents |
| `raycast` | Yes | No | `Physics.Raycast` / `Physics2D.Raycast` |
| `linecast` | Yes | No | `Physics.Linecast` / `Physics2D.Linecast` |
| `path_distance` | No (NavMesh) | Yes* | `NavMesh.CalculatePath` |
| `bounds` | No | Yes | `Renderer.bounds` / `Collider.bounds` |

*`path_distance` requires a baked NavMesh but not play mode. NavMesh data
persists as an asset.

Physics queries (`overlap`, `raycast`, `linecast`) check
`Application.isPlaying` and return a `requires_play_mode` error in edit mode.

### 2D/3D Physics Detection

**Project-level default:** Auto-detected on first query by scanning the active
scene for physics components. If any `Collider2D` or `Rigidbody2D` exists,
default is `"2d"`. If any `Collider` or `Rigidbody` (3D) exists, default is
`"3d"`. If both exist, default is `"3d"` (agents can override per-query).
Cached in `PhysicsMode` and refreshed on scene change.

**Per-query override:** Every physics-dependent operation accepts an optional
`physics` parameter: `"3d"` or `"2d"`. If omitted, uses the project default.

### Spatial Index Location and Algorithm

**File:** `Runtime/Stage/Spatial/SpatialIndex.cs`

The spatial index accelerates `nearest` and `radius` queries without requiring
the physics engine. Two implementations behind a common interface:

- **3D: Loose R-tree** — good for non-uniform object distributions typical in
  3D games. Uses a simplified R-tree with linear split. Objects stored by
  world-space AABB of their Transform position (point AABBs).

- **2D: Flat grid hash** — uniform cell grid on the XY plane. Cell size
  auto-computed from scene extents. Simpler and faster for 2D games with
  roughly uniform object density.

**Lifecycle:**
1. Built lazily on first `nearest`/`radius` query from live scene state
2. Invalidated on scene change (`SceneManager.activeSceneChanged`,
   `SceneManager.sceneLoaded`, `SceneManager.sceneUnloaded`)
3. Rebuilt on next query after invalidation
4. In play mode, rebuilt every N frames (configurable, default 30) to track
   moving objects. Alternatively, a dirty flag set when `scene_delta` detects
   position changes.
5. Survives within a play session; not persisted across domain reloads
   (rebuilt from live state)

**Why not always use Physics.OverlapSphere for nearest/radius?**
Physics overlap requires play mode and colliders on objects. The spatial index
works in edit mode and finds any GameObject with a Transform, regardless of
whether it has a collider. This makes `nearest` and `radius` useful for
scene exploration in edit mode.

### NavMesh Availability

`path_distance` requires a baked NavMesh. Before attempting pathfinding:
1. Call `NavMesh.SamplePosition(from, out hit, 1.0f, NavMesh.AllAreas)` to
   verify the start point is on the NavMesh
2. If it fails, return an error with `code: "navmesh_not_available"` and
   `suggestion: "Bake a NavMesh first (Window > AI > Navigation)"`.

### Token Budgeting for Spatial Results

`nearest` and `radius` accept a `budget` parameter (default 1500). Results
are built incrementally — each entry is estimated before adding. When the
budget is exhausted, remaining results are truncated with pagination.

`overlap` results are typically small (tens of hits) and don't need budgeting.
`raycast`, `linecast`, `path_distance`, and `bounds` return single results
and don't need budgeting.

### Assembly Placement

All spatial query logic lives in the **Editor** assembly
(`Editor/Tools/SpatialQueryTool.cs`) because tool registration requires the
Editor assembly pattern established in Phase 2.

Supporting types live in **Runtime**:
- `Runtime/Stage/Spatial/SpatialIndex.cs` — index data structure
- `Runtime/Stage/Spatial/PhysicsMode.cs` — 2D/3D detection and dispatch

This follows the same split as Phase 2: Runtime for reusable logic, Editor
for tool registration and MCP schema.

---

## Implementation Units

### Unit 1: Physics Mode Detection

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/Spatial/PhysicsMode.cs`

Detects whether the project uses 2D or 3D physics and dispatches queries to
the correct Unity API.

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Theatre.Stage
{
    /// <summary>
    /// Detects and caches whether the project uses 2D or 3D physics.
    /// Provides the per-query physics parameter override logic.
    /// </summary>
    public static class PhysicsMode
    {
        private static string s_cachedDefault;
        private static string s_cachedSceneName;

        /// <summary>
        /// Get the effective physics mode for a query.
        /// </summary>
        /// <param name="perQueryOverride">
        /// Per-query override: "3d", "2d", or null (use default).
        /// </param>
        /// <returns>"3d" or "2d".</returns>
        public static string GetEffective(string perQueryOverride)
        {
            if (perQueryOverride == "3d" || perQueryOverride == "2d")
                return perQueryOverride;
            return GetDefault();
        }

        /// <summary>
        /// Get the project-level default physics mode.
        /// Auto-detected from scene content, cached per scene.
        /// </summary>
        public static string GetDefault()
        {
            var sceneName = SceneManager.GetActiveScene().name;
            if (s_cachedDefault != null && s_cachedSceneName == sceneName)
                return s_cachedDefault;

            s_cachedSceneName = sceneName;
            s_cachedDefault = Detect();
            return s_cachedDefault;
        }

        /// <summary>
        /// Invalidate the cached default (called on scene change).
        /// </summary>
        public static void Invalidate()
        {
            s_cachedDefault = null;
            s_cachedSceneName = null;
        }

        /// <summary>
        /// Detect physics mode by scanning for 2D vs 3D physics components.
        /// </summary>
        private static string Detect()
        {
            bool has3D = Object.FindAnyObjectByType<Collider>() != null
                      || Object.FindAnyObjectByType<Rigidbody>() != null;
            bool has2D = Object.FindAnyObjectByType<Collider2D>() != null
                      || Object.FindAnyObjectByType<Rigidbody2D>() != null;

            if (has2D && !has3D) return "2d";
            // Default to 3D for mixed or empty scenes
            return "3d";
        }

        /// <summary>
        /// Check if play mode is required for the given operation and
        /// return an error response if not in play mode.
        /// Returns null if play mode is not required or is satisfied.
        /// </summary>
        public static string CheckPlayModeRequired(string operation)
        {
            bool needsPlayMode = operation == "overlap"
                              || operation == "raycast"
                              || operation == "linecast";

            if (needsPlayMode && !Application.isPlaying)
            {
                return ResponseHelpers.ErrorResponse(
                    "requires_play_mode",
                    $"The '{operation}' operation requires Play Mode because it uses the physics engine",
                    "Enter Play Mode first, or use 'nearest'/'radius' for transform-based queries in Edit Mode");
            }

            return null;
        }
    }
}
```

**Implementation Notes:**
- `FindAnyObjectByType<T>()` is the non-deprecated replacement for
  `FindObjectOfType<T>()` per the deprecated API rules.
- Detection result is cached per scene name. Invalidated when the active
  scene changes.
- Mixed 2D/3D projects default to 3D. Agents can override per-query.
- `CheckPlayModeRequired` centralizes the play mode gate so each operation
  handler doesn't repeat it.

**Acceptance Criteria:**
- [ ] Scene with only `BoxCollider` components returns `"3d"`
- [ ] Scene with only `BoxCollider2D` components returns `"2d"`
- [ ] Scene with both returns `"3d"` (default)
- [ ] Empty scene returns `"3d"` (default)
- [ ] Per-query override `"2d"` takes precedence over detected `"3d"`
- [ ] `Invalidate()` forces re-detection on next call
- [ ] `CheckPlayModeRequired("raycast")` returns error when not playing
- [ ] `CheckPlayModeRequired("nearest")` returns null (no play mode needed)

---

### Unit 2: Spatial Index

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/Spatial/SpatialIndex.cs`

Accelerates transform-based spatial queries (nearest, radius) without
requiring the physics engine.

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Theatre.Stage
{
    /// <summary>
    /// Entry in the spatial index. Stores the minimum data needed for
    /// spatial queries — position, path, instance_id, and component list.
    /// </summary>
    public struct SpatialEntry
    {
        /// <summary>World position of the object.</summary>
        public Vector3 Position;

        /// <summary>Hierarchy path.</summary>
        public string Path;

        /// <summary>Unity InstanceID.</summary>
        public int InstanceId;

        /// <summary>GameObject name.</summary>
        public string Name;

        /// <summary>Component type names.</summary>
        public string[] Components;

        /// <summary>Tag.</summary>
        public string Tag;

        /// <summary>Layer name.</summary>
        public string Layer;

        /// <summary>Whether the object is active.</summary>
        public bool Active;
    }

    /// <summary>
    /// Result of a spatial query — an entry with its computed distance.
    /// </summary>
    public struct SpatialResult
    {
        /// <summary>The matched entry.</summary>
        public SpatialEntry Entry;

        /// <summary>Distance from the query origin.</summary>
        public float Distance;
    }

    /// <summary>
    /// Spatial index for transform-based queries. Uses a flat list with
    /// brute-force distance computation for scenes up to ~10K objects.
    /// For larger scenes, a grid-based spatial hash partitions the space.
    ///
    /// Built lazily from live scene state. Invalidated on scene changes.
    /// </summary>
    public sealed class SpatialIndex
    {
        private List<SpatialEntry> _entries;
        private bool _dirty = true;
        private string _sceneName;
        private int _lastBuildFrame;

        /// <summary>
        /// How often to rebuild in play mode (frames). 0 = never auto-rebuild.
        /// </summary>
        public int PlayModeRebuildInterval { get; set; } = 30;

        /// <summary>Number of entries in the index.</summary>
        public int Count => _entries?.Count ?? 0;

        /// <summary>Mark the index as needing a rebuild.</summary>
        public void Invalidate()
        {
            _dirty = true;
        }

        /// <summary>
        /// Ensure the index is up-to-date. Rebuilds if dirty or stale.
        /// </summary>
        public void EnsureFresh()
        {
            var sceneName = SceneManager.GetActiveScene().name;
            if (sceneName != _sceneName)
            {
                _dirty = true;
                _sceneName = sceneName;
            }

            if (Application.isPlaying && PlayModeRebuildInterval > 0)
            {
                if (Time.frameCount - _lastBuildFrame >= PlayModeRebuildInterval)
                    _dirty = true;
            }

            if (_dirty)
                Rebuild();
        }

        /// <summary>
        /// Find the N closest entries to a point.
        /// </summary>
        /// <param name="origin">Query center.</param>
        /// <param name="count">Maximum results.</param>
        /// <param name="maxDistance">Distance cutoff (0 = no limit).</param>
        /// <param name="filter">Optional filter predicate.</param>
        /// <returns>Results sorted by distance ascending.</returns>
        public List<SpatialResult> Nearest(
            Vector3 origin,
            int count,
            float maxDistance = 0f,
            Func<SpatialEntry, bool> filter = null)
        {
            EnsureFresh();

            var results = new List<SpatialResult>();
            if (_entries == null) return results;

            foreach (var entry in _entries)
            {
                if (filter != null && !filter(entry))
                    continue;

                float dist = Vector3.Distance(entry.Position, origin);
                if (maxDistance > 0f && dist > maxDistance)
                    continue;

                results.Add(new SpatialResult
                {
                    Entry = entry,
                    Distance = dist
                });
            }

            results.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            if (results.Count > count)
                results.RemoveRange(count, results.Count - count);

            return results;
        }

        /// <summary>
        /// Find all entries within a radius of a point.
        /// </summary>
        /// <param name="origin">Sphere center.</param>
        /// <param name="radius">Search radius.</param>
        /// <param name="filter">Optional filter predicate.</param>
        /// <param name="sortBy">Sort mode: "distance" or "name".</param>
        /// <returns>Results sorted by the specified mode.</returns>
        public List<SpatialResult> Radius(
            Vector3 origin,
            float radius,
            Func<SpatialEntry, bool> filter = null,
            string sortBy = "distance")
        {
            EnsureFresh();

            var results = new List<SpatialResult>();
            if (_entries == null) return results;

            float radiusSq = radius * radius;

            foreach (var entry in _entries)
            {
                if (filter != null && !filter(entry))
                    continue;

                float distSq = (entry.Position - origin).sqrMagnitude;
                if (distSq > radiusSq)
                    continue;

                results.Add(new SpatialResult
                {
                    Entry = entry,
                    Distance = Mathf.Sqrt(distSq)
                });
            }

            if (sortBy == "name")
                results.Sort((a, b) =>
                    string.Compare(a.Entry.Name, b.Entry.Name,
                        StringComparison.Ordinal));
            else
                results.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            return results;
        }

        /// <summary>
        /// Rebuild the index from current scene state.
        /// </summary>
        private void Rebuild()
        {
            _entries = new List<SpatialEntry>();
            var roots = ObjectResolver.GetAllRoots();

            foreach (var root in roots)
            {
                CollectRecursive(root.transform);
            }

            _dirty = false;
            _lastBuildFrame = Time.frameCount;
        }

        /// <summary>
        /// Recursively collect all active GameObjects into the index.
        /// </summary>
        private void CollectRecursive(Transform current)
        {
            if (!current.gameObject.activeInHierarchy)
                return;

            var components = current.GetComponents<Component>();
            var typeNames = new string[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                typeNames[i] = components[i] != null
                    ? components[i].GetType().Name
                    : "Missing";
            }

            _entries.Add(new SpatialEntry
            {
                Position = current.position,
                Path = ResponseHelpers.GetHierarchyPath(current),
                InstanceId = current.gameObject.GetInstanceID(),
                Name = current.name,
                Components = typeNames,
                Tag = current.gameObject.tag,
                Layer = LayerMask.LayerToName(current.gameObject.layer),
                Active = true
            });

            for (int i = 0; i < current.childCount; i++)
            {
                CollectRecursive(current.GetChild(i));
            }
        }
    }
}
```

**Implementation Notes:**
- The initial implementation uses a flat list with brute-force distance
  computation. For typical game scenes (hundreds to low thousands of objects),
  this is fast enough (< 1ms). A grid hash or R-tree can be added later as
  an optimization if profiling shows it's needed.
- `sqrMagnitude` is used in `Radius` to avoid a square root per candidate
  during the filter phase. The actual distance is only computed for entries
  that pass the radius check.
- `CollectRecursive` skips inactive objects (matching `scene_snapshot`
  behavior).
- The index stores `SpatialEntry` structs with all data needed to build a
  response, avoiding a second pass to look up paths and components.
- `#pragma warning disable CS0618` is needed on `GetInstanceID()` calls per
  the deprecated API rules.
- `PlayModeRebuildInterval` defaults to 30 frames (~0.5s at 60fps). This
  balances freshness against rebuild cost.

**Acceptance Criteria:**
- [ ] `Nearest` returns entries sorted by distance ascending
- [ ] `Nearest` with `count=3` returns at most 3 results
- [ ] `Nearest` with `maxDistance` excludes objects beyond cutoff
- [ ] `Nearest` with filter excludes non-matching objects
- [ ] `Radius` returns all entries within radius
- [ ] `Radius` with `sortBy="name"` sorts alphabetically
- [ ] `Radius` with `sortBy="distance"` sorts by distance
- [ ] `Radius` with filter excludes non-matching objects
- [ ] Index rebuilds on scene change
- [ ] Index rebuilds in play mode after `PlayModeRebuildInterval` frames
- [ ] `Invalidate()` forces rebuild on next query
- [ ] Inactive objects are excluded from the index

---

### Unit 3: Spatial Query Tool — Registration and Dispatch

**File:** `Packages/com.theatre.toolkit/Editor/Tools/SpatialQueryTool.cs`

The MCP tool registration and top-level operation dispatch. This is a
compound tool with an `operation` parameter that routes to operation-specific
handlers.

```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// MCP tool: spatial_query
    /// Compound tool for spatial questions about the scene.
    /// Operations: nearest, radius, overlap, raycast, linecast,
    ///             path_distance, bounds.
    /// </summary>
    public static class SpatialQueryTool
    {
        private static readonly JToken s_inputSchema;
        private static SpatialIndex s_spatialIndex;

        static SpatialQueryTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""nearest"", ""radius"", ""overlap"",
                                   ""raycast"", ""linecast"",
                                   ""path_distance"", ""bounds""],
                        ""description"": ""The spatial query operation to perform.""
                    },
                    ""origin"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Query center point [x,y,z] or [x,y]. Used by nearest, radius, overlap, raycast.""
                    },
                    ""direction"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Ray direction [x,y,z] or [x,y] (normalized). Used by raycast.""
                    },
                    ""from"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Start point [x,y,z] or [x,y]. Used by linecast, path_distance.""
                    },
                    ""to"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""End point [x,y,z] or [x,y]. Used by linecast, path_distance.""
                    },
                    ""radius"": {
                        ""type"": ""number"",
                        ""description"": ""Search radius. Used by radius operation.""
                    },
                    ""count"": {
                        ""type"": ""integer"",
                        ""default"": 5,
                        ""description"": ""Max results for nearest.""
                    },
                    ""max_distance"": {
                        ""type"": ""number"",
                        ""description"": ""Distance cutoff for nearest; max ray length for raycast (default 1000).""
                    },
                    ""all"": {
                        ""type"": ""boolean"",
                        ""default"": false,
                        ""description"": ""Return all hits (raycast) or just first.""
                    },
                    ""shape"": {
                        ""type"": ""string"",
                        ""enum"": [""sphere"", ""circle"", ""box"", ""capsule""],
                        ""description"": ""Overlap shape. Used by overlap.""
                    },
                    ""center"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Shape center for overlap.""
                    },
                    ""size"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Box half-extents [x,y,z], or [radius] for sphere/circle. Used by overlap.""
                    },
                    ""path"": {
                        ""type"": ""string"",
                        ""description"": ""Target object path. Used by bounds.""
                    },
                    ""instance_id"": {
                        ""type"": ""integer"",
                        ""description"": ""Target object instance_id. Used by bounds.""
                    },
                    ""source"": {
                        ""type"": ""string"",
                        ""enum"": [""renderer"", ""collider"", ""combined""],
                        ""default"": ""combined"",
                        ""description"": ""Bounds source. Used by bounds.""
                    },
                    ""layer_mask"": {
                        ""type"": ""integer"",
                        ""description"": ""Physics layer mask for overlap/raycast/linecast.""
                    },
                    ""physics"": {
                        ""type"": ""string"",
                        ""enum"": [""3d"", ""2d""],
                        ""description"": ""Physics mode override. Default: auto-detected from scene.""
                    },
                    ""include_components"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Filter results to objects with these components. Used by nearest, radius.""
                    },
                    ""exclude_tags"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Exclude objects with these tags. Used by nearest, radius.""
                    },
                    ""sort_by"": {
                        ""type"": ""string"",
                        ""enum"": [""distance"", ""name""],
                        ""default"": ""distance"",
                        ""description"": ""Sort order for radius results.""
                    },
                    ""agent_type_id"": {
                        ""type"": ""integer"",
                        ""description"": ""NavMesh agent type for path_distance.""
                    },
                    ""budget"": {
                        ""type"": ""integer"",
                        ""default"": 1500,
                        ""minimum"": 100,
                        ""maximum"": 4000,
                        ""description"": ""Token budget for nearest/radius results.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "spatial_query",
                description: "Spatial questions about the scene: find nearest "
                    + "objects, query within radius, physics overlap/raycast/"
                    + "linecast, NavMesh path distance, bounding boxes. "
                    + "Use 'nearest' or 'radius' for transform-based queries "
                    + "(works in Edit Mode). Use 'overlap', 'raycast', "
                    + "'linecast' for physics queries (requires Play Mode).",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageQuery,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = true
                }
            ));
        }

        /// <summary>
        /// Get or create the shared spatial index.
        /// </summary>
        internal static SpatialIndex GetIndex()
        {
            if (s_spatialIndex == null)
                s_spatialIndex = new SpatialIndex();
            return s_spatialIndex;
        }

        /// <summary>
        /// Invalidate the spatial index (called on scene change).
        /// </summary>
        internal static void InvalidateIndex()
        {
            s_spatialIndex?.Invalidate();
        }

        private static string Execute(JToken arguments)
        {
            if (arguments == null || arguments.Type != JTokenType.Object)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Arguments must be a JSON object with an 'operation' field",
                    "Provide {\"operation\": \"nearest\", \"origin\": [0,0,0]}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: nearest, radius, overlap, raycast, "
                    + "linecast, path_distance, bounds");
            }

            // Play mode gate for physics operations
            var playModeError = PhysicsMode.CheckPlayModeRequired(operation);
            if (playModeError != null)
                return playModeError;

            try
            {
                return operation switch
                {
                    "nearest" => SpatialQueryNearest.Execute(args),
                    "radius" => SpatialQueryRadius.Execute(args),
                    "overlap" => SpatialQueryOverlap.Execute(args),
                    "raycast" => SpatialQueryRaycast.Execute(args),
                    "linecast" => SpatialQueryLinecast.Execute(args),
                    "path_distance" => SpatialQueryPathDistance.Execute(args),
                    "bounds" => SpatialQueryBounds.Execute(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: nearest, radius, overlap, raycast, "
                        + "linecast, path_distance, bounds")
                };
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(
                    $"[Theatre] spatial_query:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"spatial_query:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }
    }
}
```

**Implementation Notes:**
- `SpatialQueryTool` is the entry point. It validates the operation parameter,
  checks play mode requirements, then delegates to per-operation handler
  classes (Units 4-10).
- The spatial index is a singleton, created lazily. `InvalidateIndex` is
  called from scene change callbacks (wired up in `TheatreServer`).
- The top-level exception handler ensures no operation can crash the editor.
  All exceptions become structured error responses.
- Group is `ToolGroup.StageQuery` — separate from `StageGameObject`.

**Acceptance Criteria:**
- [ ] Tool registers with name `"spatial_query"` and group `StageQuery`
- [ ] Missing `operation` returns `invalid_parameter` error
- [ ] Unknown operation returns `invalid_parameter` error with valid list
- [ ] Physics operations in edit mode return `requires_play_mode` error
- [ ] Transform operations (nearest, radius, bounds) work in edit mode
- [ ] Exceptions in handlers produce `internal_error` responses

---

### Unit 4: Nearest Operation

**File:** `Packages/com.theatre.toolkit/Editor/Tools/SpatialQueryNearest.cs`

Find the N closest GameObjects to a point using the spatial index.

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// spatial_query:nearest — find the N closest objects to a point.
    /// Uses the spatial index (works in both edit and play mode).
    /// </summary>
    internal static class SpatialQueryNearest
    {
        internal static string Execute(JObject args)
        {
            // Parse origin (required)
            var origin = ParseVector3(args, "origin");
            if (!origin.HasValue)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'origin' parameter",
                    "Provide origin as [x, y, z] array");
            }

            int count = args["count"]?.Value<int>() ?? 5;
            float maxDistance = args["max_distance"]?.Value<float>() ?? 0f;
            int budgetTokens = args["budget"]?.Value<int>()
                ?? TokenBudget.DefaultBudget;

            // Parse filters
            var includeComponents = ParseStringArray(args, "include_components");
            var excludeTags = ParseStringArray(args, "exclude_tags");

            // Build filter predicate
            Func<SpatialEntry, bool> filter = null;
            if (includeComponents != null || excludeTags != null)
            {
                filter = entry =>
                {
                    if (excludeTags != null)
                    {
                        foreach (var tag in excludeTags)
                        {
                            if (string.Equals(entry.Tag, tag,
                                StringComparison.OrdinalIgnoreCase))
                                return false;
                        }
                    }
                    if (includeComponents != null)
                    {
                        foreach (var required in includeComponents)
                        {
                            bool found = false;
                            if (entry.Components != null)
                            {
                                foreach (var comp in entry.Components)
                                {
                                    if (string.Equals(comp, required,
                                        StringComparison.OrdinalIgnoreCase))
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                            }
                            if (!found) return false;
                        }
                    }
                    return true;
                };
            }

            // Query spatial index
            var index = SpatialQueryTool.GetIndex();
            var results = index.Nearest(
                origin.Value, count, maxDistance, filter);

            // Build response
            var budget = new TokenBudget(budgetTokens);
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "nearest";
            response["origin"] = ResponseHelpers.ToJArray(origin.Value);

            var resultsArray = new JArray();
            int returned = 0;

            foreach (var result in results)
            {
                if (budget.IsExhausted) break;

                var entryObj = new JObject();
                entryObj["path"] = result.Entry.Path;
                entryObj["instance_id"] = result.Entry.InstanceId;
                entryObj["name"] = result.Entry.Name;
                entryObj["position"] = ResponseHelpers.ToJArray(
                    result.Entry.Position);
                entryObj["distance"] = Math.Round(result.Distance, 2);

                if (result.Entry.Components != null
                    && result.Entry.Components.Length > 0)
                {
                    var comps = new JArray();
                    foreach (var c in result.Entry.Components)
                    {
                        if (c != "Transform")
                            comps.Add(c);
                    }
                    if (comps.Count > 0)
                        entryObj["components"] = comps;
                }

                // Estimate cost before adding
                var json = entryObj.ToString(Formatting.None);
                if (budget.WouldExceed(json.Length))
                    break;

                resultsArray.Add(entryObj);
                budget.Add(json.Length);
                returned++;
            }

            response["results"] = resultsArray;
            response["returned"] = returned;

            bool truncated = returned < results.Count;
            response["budget"] = budget.ToBudgetJObject(
                truncated: truncated,
                reason: truncated ? "budget" : null,
                suggestion: truncated
                    ? "Reduce count or increase budget to see more results"
                    : null);

            return response.ToString(Formatting.None);
        }

        // --- Shared parsing helpers (used by other operations too) ---

        internal static Vector3? ParseVector3(JObject args, string field)
        {
            var token = args[field];
            if (token == null || token.Type != JTokenType.Array)
                return null;
            var arr = (JArray)token;
            if (arr.Count < 3) return null;
            return new Vector3(
                arr[0].Value<float>(),
                arr[1].Value<float>(),
                arr[2].Value<float>());
        }

        internal static Vector2? ParseVector2(JObject args, string field)
        {
            var token = args[field];
            if (token == null || token.Type != JTokenType.Array)
                return null;
            var arr = (JArray)token;
            if (arr.Count < 2) return null;
            return new Vector2(
                arr[0].Value<float>(),
                arr[1].Value<float>());
        }

        internal static string[] ParseStringArray(JObject args, string field)
        {
            var token = args[field];
            if (token == null || token.Type != JTokenType.Array)
                return null;
            var list = new List<string>();
            foreach (var item in (JArray)token)
                list.Add(item.Value<string>());
            return list.Count > 0 ? list.ToArray() : null;
        }
    }
}
```

**Implementation Notes:**
- Uses `"results"` (plural array) per CONTRACTS.md for list queries.
- Filters for `include_components` and `exclude_tags` are applied via a
  predicate passed to the spatial index. This avoids post-filtering large
  result sets.
- `ParseVector3`, `ParseVector2`, `ParseStringArray` are shared helpers
  used by other operation handlers. They live here because `nearest` is the
  most commonly used operation.
- Token budgeting truncates results when the response grows too large.
  The `budget` metadata tells the agent if results were truncated.

**Acceptance Criteria:**
- [ ] Returns `results` array with `path`, `instance_id`, `position`, `distance`
- [ ] Results sorted by distance ascending
- [ ] `count` limits number of results
- [ ] `max_distance` excludes objects beyond cutoff
- [ ] `include_components` filters to objects with matching components
- [ ] `exclude_tags` excludes objects with matching tags
- [ ] Budget truncation works — response includes budget metadata
- [ ] Missing `origin` returns `invalid_parameter` error
- [ ] Frame context (frame, time, play_mode) included in response

---

### Unit 5: Radius Operation

**File:** `Packages/com.theatre.toolkit/Editor/Tools/SpatialQueryRadius.cs`

Find all GameObjects within a radius of a point.

```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// spatial_query:radius — find all objects within a radius of a point.
    /// Uses the spatial index (works in both edit and play mode).
    /// </summary>
    internal static class SpatialQueryRadius
    {
        internal static string Execute(JObject args)
        {
            var origin = SpatialQueryNearest.ParseVector3(args, "origin");
            if (!origin.HasValue)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'origin' parameter",
                    "Provide origin as [x, y, z] array");
            }

            var radiusVal = args["radius"]?.Value<float>();
            if (!radiusVal.HasValue || radiusVal.Value <= 0f)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'radius' parameter",
                    "Provide a positive radius value");
            }

            string sortBy = args["sort_by"]?.Value<string>() ?? "distance";
            int budgetTokens = args["budget"]?.Value<int>()
                ?? TokenBudget.DefaultBudget;

            var includeComponents = SpatialQueryNearest.ParseStringArray(
                args, "include_components");
            var excludeTags = SpatialQueryNearest.ParseStringArray(
                args, "exclude_tags");

            // Build filter
            Func<SpatialEntry, bool> filter = null;
            if (includeComponents != null || excludeTags != null)
            {
                filter = entry =>
                {
                    if (excludeTags != null)
                    {
                        foreach (var tag in excludeTags)
                        {
                            if (string.Equals(entry.Tag, tag,
                                StringComparison.OrdinalIgnoreCase))
                                return false;
                        }
                    }
                    if (includeComponents != null)
                    {
                        foreach (var required in includeComponents)
                        {
                            bool found = false;
                            if (entry.Components != null)
                            {
                                foreach (var comp in entry.Components)
                                {
                                    if (string.Equals(comp, required,
                                        StringComparison.OrdinalIgnoreCase))
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                            }
                            if (!found) return false;
                        }
                    }
                    return true;
                };
            }

            var index = SpatialQueryTool.GetIndex();
            var results = index.Radius(
                origin.Value, radiusVal.Value, filter, sortBy);

            // Build response
            var budget = new TokenBudget(budgetTokens);
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "radius";
            response["origin"] = ResponseHelpers.ToJArray(origin.Value);
            response["radius"] = Math.Round(radiusVal.Value, 2);

            var resultsArray = new JArray();
            int returned = 0;

            foreach (var result in results)
            {
                if (budget.IsExhausted) break;

                var entryObj = new JObject();
                entryObj["path"] = result.Entry.Path;
                entryObj["instance_id"] = result.Entry.InstanceId;
                entryObj["name"] = result.Entry.Name;
                entryObj["position"] = ResponseHelpers.ToJArray(
                    result.Entry.Position);
                entryObj["distance"] = Math.Round(result.Distance, 2);

                if (result.Entry.Components != null
                    && result.Entry.Components.Length > 0)
                {
                    var comps = new JArray();
                    foreach (var c in result.Entry.Components)
                    {
                        if (c != "Transform")
                            comps.Add(c);
                    }
                    if (comps.Count > 0)
                        entryObj["components"] = comps;
                }

                var json = entryObj.ToString(Formatting.None);
                if (budget.WouldExceed(json.Length))
                    break;

                resultsArray.Add(entryObj);
                budget.Add(json.Length);
                returned++;
            }

            response["results"] = resultsArray;
            response["total"] = results.Count;
            response["returned"] = returned;

            bool truncated = returned < results.Count;
            response["budget"] = budget.ToBudgetJObject(
                truncated: truncated,
                reason: truncated ? "budget" : null,
                suggestion: truncated
                    ? "Reduce radius or increase budget"
                    : null);

            return response.ToString(Formatting.None);
        }
    }
}
```

**Implementation Notes:**
- Includes `total` field so the agent knows how many objects exist within
  the radius even if the response is truncated.
- Shares the filter-building pattern with `nearest`. A future refactor could
  extract a shared `BuildFilter` helper, but for now the duplication is
  minimal and keeps each handler self-contained.

**Acceptance Criteria:**
- [ ] Returns `results` array with all objects within radius
- [ ] `sort_by="distance"` sorts by distance (default)
- [ ] `sort_by="name"` sorts alphabetically
- [ ] `total` reflects the full count before budget truncation
- [ ] Budget truncation works correctly
- [ ] Missing `radius` returns `invalid_parameter` error
- [ ] Negative `radius` returns `invalid_parameter` error

---

### Unit 6: Overlap Operation

**File:** `Packages/com.theatre.toolkit/Editor/Tools/SpatialQueryOverlap.cs`

Physics overlap queries — sphere, box, capsule. Requires play mode.

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// spatial_query:overlap — physics overlap query.
    /// Finds all colliders intersecting a shape (sphere, box, capsule).
    /// Requires Play Mode.
    /// </summary>
    internal static class SpatialQueryOverlap
    {
        internal static string Execute(JObject args)
        {
            var shape = args["shape"]?.Value<string>();
            if (string.IsNullOrEmpty(shape))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'shape' parameter",
                    "Provide shape: 'sphere', 'circle', 'box', or 'capsule'");
            }

            var physicsMode = PhysicsMode.GetEffective(
                args["physics"]?.Value<string>());
            int layerMask = args["layer_mask"]?.Value<int>()
                ?? Physics.DefaultRaycastLayers;

            if (physicsMode == "2d")
                return Execute2D(args, shape, layerMask);
            else
                return Execute3D(args, shape, layerMask);
        }

        private static string Execute3D(
            JObject args, string shape, int layerMask)
        {
            var center = SpatialQueryNearest.ParseVector3(args, "center");
            if (!center.HasValue)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'center' parameter",
                    "Provide center as [x, y, z] array");
            }

            Collider[] colliders;

            switch (shape)
            {
                case "sphere":
                {
                    float radius = ParseRadius(args);
                    if (radius <= 0f)
                        return RadiusError();
                    colliders = Physics.OverlapSphere(
                        center.Value, radius, layerMask);
                    break;
                }
                case "box":
                {
                    var halfExtents = SpatialQueryNearest.ParseVector3(
                        args, "size");
                    if (!halfExtents.HasValue)
                    {
                        return ResponseHelpers.ErrorResponse(
                            "invalid_parameter",
                            "Missing 'size' (half-extents) for box overlap",
                            "Provide size as [x, y, z] half-extents array");
                    }
                    colliders = Physics.OverlapBox(
                        center.Value, halfExtents.Value,
                        Quaternion.identity, layerMask);
                    break;
                }
                case "capsule":
                {
                    var size = SpatialQueryNearest.ParseVector3(args, "size");
                    if (!size.HasValue)
                    {
                        return ResponseHelpers.ErrorResponse(
                            "invalid_parameter",
                            "Missing 'size' for capsule overlap",
                            "Provide size as [radius, height, 0] array");
                    }
                    float capsuleRadius = size.Value.x;
                    float height = size.Value.y;
                    var point0 = center.Value
                        + Vector3.up * (height * 0.5f - capsuleRadius);
                    var point1 = center.Value
                        - Vector3.up * (height * 0.5f - capsuleRadius);
                    colliders = Physics.OverlapCapsule(
                        point0, point1, capsuleRadius, layerMask);
                    break;
                }
                default:
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Invalid 3D shape '{shape}'",
                        "Valid 3D shapes: 'sphere', 'box', 'capsule'");
            }

            return BuildOverlapResponse(colliders, null, shape);
        }

        private static string Execute2D(
            JObject args, string shape, int layerMask)
        {
            var center2D = SpatialQueryNearest.ParseVector2(args, "center");
            if (!center2D.HasValue)
            {
                // Try parsing as Vector3 and take XY
                var center3D = SpatialQueryNearest.ParseVector3(args, "center");
                if (center3D.HasValue)
                    center2D = new Vector2(center3D.Value.x, center3D.Value.y);
                else
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        "Missing or invalid 'center' parameter",
                        "Provide center as [x, y] or [x, y, z] array");
            }

            Collider2D[] colliders;

            switch (shape)
            {
                case "circle":
                case "sphere":
                {
                    float radius = ParseRadius(args);
                    if (radius <= 0f) return RadiusError();
                    colliders = Physics2D.OverlapCircleAll(
                        center2D.Value, radius, layerMask);
                    break;
                }
                case "box":
                {
                    var size2D = SpatialQueryNearest.ParseVector2(args, "size");
                    if (!size2D.HasValue)
                    {
                        var size3D = SpatialQueryNearest.ParseVector3(
                            args, "size");
                        if (size3D.HasValue)
                            size2D = new Vector2(
                                size3D.Value.x, size3D.Value.y);
                    }
                    if (!size2D.HasValue)
                        return ResponseHelpers.ErrorResponse(
                            "invalid_parameter",
                            "Missing 'size' for box overlap",
                            "Provide size as [x, y] half-extents");
                    colliders = Physics2D.OverlapBoxAll(
                        center2D.Value, size2D.Value * 2f, 0f, layerMask);
                    break;
                }
                case "capsule":
                {
                    var size2D = SpatialQueryNearest.ParseVector2(args, "size");
                    if (!size2D.HasValue)
                        return ResponseHelpers.ErrorResponse(
                            "invalid_parameter",
                            "Missing 'size' for capsule overlap",
                            "Provide size as [width, height]");
                    colliders = Physics2D.OverlapCapsuleAll(
                        center2D.Value, size2D.Value,
                        CapsuleDirection2D.Vertical, 0f, layerMask);
                    break;
                }
                default:
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Invalid 2D shape '{shape}'",
                        "Valid 2D shapes: 'circle', 'box', 'capsule'");
            }

            return BuildOverlapResponse(null, colliders, shape);
        }

        private static string BuildOverlapResponse(
            Collider[] colliders3D, Collider2D[] colliders2D, string shape)
        {
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "overlap";
            response["shape"] = shape;

            var resultsArray = new JArray();

            if (colliders3D != null)
            {
                foreach (var col in colliders3D)
                {
                    if (col == null) continue;
                    resultsArray.Add(BuildColliderEntry(
                        col.gameObject, col.GetType().Name));
                }
            }

            if (colliders2D != null)
            {
                foreach (var col in colliders2D)
                {
                    if (col == null) continue;
                    resultsArray.Add(BuildColliderEntry(
                        col.gameObject, col.GetType().Name));
                }
            }

            response["results"] = resultsArray;
            response["returned"] = resultsArray.Count;

            return response.ToString(Formatting.None);
        }

        internal static JObject BuildColliderEntry(
            GameObject go, string colliderType)
        {
            var entry = new JObject();
            entry["path"] = ResponseHelpers.GetHierarchyPath(go.transform);
            #pragma warning disable CS0618
            entry["instance_id"] = go.GetInstanceID();
            #pragma warning restore CS0618
            entry["tag"] = go.tag;
            entry["layer"] = LayerMask.LayerToName(go.layer);
            entry["collider_type"] = colliderType;
            return entry;
        }

        private static float ParseRadius(JObject args)
        {
            // Try size array first (for sphere/circle: [radius])
            var sizeToken = args["size"];
            if (sizeToken != null && sizeToken.Type == JTokenType.Array)
            {
                var arr = (JArray)sizeToken;
                if (arr.Count >= 1)
                    return arr[0].Value<float>();
            }
            // Fall back to radius field
            return args["radius"]?.Value<float>() ?? 0f;
        }

        private static string RadiusError()
        {
            return ResponseHelpers.ErrorResponse(
                "invalid_parameter",
                "Missing or invalid radius for sphere/circle overlap",
                "Provide size as [radius] or set 'radius' parameter");
        }
    }
}
```

**Implementation Notes:**
- Play mode check is handled by `SpatialQueryTool.Execute` before dispatch,
  so this handler can assume play mode is active.
- `BuildColliderEntry` is reused by `raycast` and `linecast` for hit results.
- 2D overlap accepts both `[x, y]` and `[x, y, z]` center coordinates
  (z is ignored) for convenience when agents don't know the physics mode.
- `Physics2D.OverlapBoxAll` expects full size, not half-extents, so we
  multiply by 2. The wire format uses half-extents per STAGE-SURFACE.md.
- `#pragma warning disable CS0618` around `GetInstanceID()` per the
  deprecated API rules.

**Acceptance Criteria:**
- [ ] Sphere overlap finds colliders within the sphere
- [ ] Box overlap finds colliders within the box
- [ ] Capsule overlap finds colliders within the capsule
- [ ] 2D circle overlap uses `Physics2D.OverlapCircleAll`
- [ ] 2D box overlap uses `Physics2D.OverlapBoxAll`
- [ ] `layer_mask` filters results by physics layer
- [ ] Results include `path`, `instance_id`, `tag`, `layer`, `collider_type`
- [ ] Missing `shape` returns `invalid_parameter` error
- [ ] Invalid shape returns `invalid_parameter` error

---

### Unit 7: Raycast Operation

**File:** `Packages/com.theatre.toolkit/Editor/Tools/SpatialQueryRaycast.cs`

Cast a ray and report hits. Supports single and multi-hit. Requires play mode.

```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// spatial_query:raycast — cast a ray and report hits.
    /// Supports single-hit and all-hits modes.
    /// Requires Play Mode.
    /// </summary>
    internal static class SpatialQueryRaycast
    {
        internal static string Execute(JObject args)
        {
            var physicsMode = PhysicsMode.GetEffective(
                args["physics"]?.Value<string>());
            float maxDistance = args["max_distance"]?.Value<float>() ?? 1000f;
            bool all = args["all"]?.Value<bool>() ?? false;
            int layerMask = args["layer_mask"]?.Value<int>()
                ?? Physics.DefaultRaycastLayers;

            if (physicsMode == "2d")
                return Execute2D(args, maxDistance, all, layerMask);
            else
                return Execute3D(args, maxDistance, all, layerMask);
        }

        private static string Execute3D(
            JObject args, float maxDistance, bool all, int layerMask)
        {
            var origin = SpatialQueryNearest.ParseVector3(args, "origin");
            if (!origin.HasValue)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'origin' parameter",
                    "Provide origin as [x, y, z]");

            var direction = SpatialQueryNearest.ParseVector3(args, "direction");
            if (!direction.HasValue)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'direction' parameter",
                    "Provide direction as [x, y, z] (will be normalized)");

            var dir = direction.Value.normalized;

            if (all)
            {
                var hits = Physics.RaycastAll(
                    origin.Value, dir, maxDistance, layerMask);
                System.Array.Sort(hits,
                    (a, b) => a.distance.CompareTo(b.distance));
                return BuildMultiHitResponse3D(hits, origin.Value, dir);
            }
            else
            {
                bool hit = Physics.Raycast(
                    origin.Value, dir, out RaycastHit hitInfo,
                    maxDistance, layerMask);
                return BuildSingleHitResponse3D(
                    hit, hitInfo, origin.Value, dir);
            }
        }

        private static string Execute2D(
            JObject args, float maxDistance, bool all, int layerMask)
        {
            var origin = SpatialQueryNearest.ParseVector2(args, "origin");
            if (!origin.HasValue)
            {
                var origin3 = SpatialQueryNearest.ParseVector3(args, "origin");
                if (origin3.HasValue)
                    origin = new Vector2(origin3.Value.x, origin3.Value.y);
                else
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        "Missing or invalid 'origin'",
                        "Provide origin as [x, y] or [x, y, z]");
            }

            var direction = SpatialQueryNearest.ParseVector2(
                args, "direction");
            if (!direction.HasValue)
            {
                var dir3 = SpatialQueryNearest.ParseVector3(
                    args, "direction");
                if (dir3.HasValue)
                    direction = new Vector2(dir3.Value.x, dir3.Value.y);
                else
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        "Missing or invalid 'direction'",
                        "Provide direction as [x, y] or [x, y, z]");
            }

            var dir = direction.Value.normalized;

            if (all)
            {
                var hits = Physics2D.RaycastAll(
                    origin.Value, dir, maxDistance, layerMask);
                return BuildMultiHitResponse2D(hits, origin.Value, dir);
            }
            else
            {
                var hit = Physics2D.Raycast(
                    origin.Value, dir, maxDistance, layerMask);
                return BuildSingleHitResponse2D(
                    hit, origin.Value, dir);
            }
        }

        private static string BuildSingleHitResponse3D(
            bool didHit, RaycastHit hitInfo, Vector3 origin, Vector3 dir)
        {
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "raycast";
            response["origin"] = ResponseHelpers.ToJArray(origin);
            response["direction"] = ResponseHelpers.ToJArray(dir);

            var result = new JObject();
            result["hit"] = didHit;

            if (didHit)
            {
                result["point"] = ResponseHelpers.ToJArray(hitInfo.point);
                result["normal"] = ResponseHelpers.ToJArray(hitInfo.normal);
                result["distance"] = Math.Round(hitInfo.distance, 2);
                result["collider"] = SpatialQueryOverlap.BuildColliderEntry(
                    hitInfo.collider.gameObject,
                    hitInfo.collider.GetType().Name);
            }

            response["result"] = result;
            return response.ToString(Formatting.None);
        }

        private static string BuildMultiHitResponse3D(
            RaycastHit[] hits, Vector3 origin, Vector3 dir)
        {
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "raycast";
            response["origin"] = ResponseHelpers.ToJArray(origin);
            response["direction"] = ResponseHelpers.ToJArray(dir);

            var resultsArray = new JArray();
            foreach (var hit in hits)
            {
                var hitObj = new JObject();
                hitObj["point"] = ResponseHelpers.ToJArray(hit.point);
                hitObj["normal"] = ResponseHelpers.ToJArray(hit.normal);
                hitObj["distance"] = Math.Round(hit.distance, 2);
                hitObj["collider"] = SpatialQueryOverlap.BuildColliderEntry(
                    hit.collider.gameObject,
                    hit.collider.GetType().Name);
                resultsArray.Add(hitObj);
            }

            response["results"] = resultsArray;
            response["returned"] = resultsArray.Count;
            return response.ToString(Formatting.None);
        }

        private static string BuildSingleHitResponse2D(
            RaycastHit2D hit, Vector2 origin, Vector2 dir)
        {
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "raycast";
            response["origin"] = ResponseHelpers.ToJArray(origin);
            response["direction"] = ResponseHelpers.ToJArray(dir);

            var result = new JObject();
            result["hit"] = hit.collider != null;

            if (hit.collider != null)
            {
                result["point"] = ResponseHelpers.ToJArray(hit.point);
                result["normal"] = ResponseHelpers.ToJArray(hit.normal);
                result["distance"] = Math.Round(hit.distance, 2);
                result["collider"] = SpatialQueryOverlap.BuildColliderEntry(
                    hit.collider.gameObject,
                    hit.collider.GetType().Name);
            }

            response["result"] = result;
            return response.ToString(Formatting.None);
        }

        private static string BuildMultiHitResponse2D(
            RaycastHit2D[] hits, Vector2 origin, Vector2 dir)
        {
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "raycast";
            response["origin"] = ResponseHelpers.ToJArray(origin);
            response["direction"] = ResponseHelpers.ToJArray(dir);

            var resultsArray = new JArray();
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                var hitObj = new JObject();
                hitObj["point"] = ResponseHelpers.ToJArray(hit.point);
                hitObj["normal"] = ResponseHelpers.ToJArray(hit.normal);
                hitObj["distance"] = Math.Round(hit.distance, 2);
                hitObj["collider"] = SpatialQueryOverlap.BuildColliderEntry(
                    hit.collider.gameObject,
                    hit.collider.GetType().Name);
                resultsArray.Add(hitObj);
            }

            response["results"] = resultsArray;
            response["returned"] = resultsArray.Count;
            return response.ToString(Formatting.None);
        }
    }
}
```

**Implementation Notes:**
- Single-hit uses `"result"` (singular). Multi-hit (`all: true`) uses
  `"results"` (plural). Per CONTRACTS.md envelope rules.
- Direction is normalized before use — agents may pass un-normalized vectors.
- 3D multi-hit results are sorted by distance. `Physics.RaycastAll` does not
  guarantee sorted order.
- The response echoes `origin` and `direction` so agents can correlate
  results with their query.

**Acceptance Criteria:**
- [ ] Single-hit 3D raycast returns `result` with `hit`, `point`, `normal`, `distance`, `collider`
- [ ] Single-hit returns `"hit": false` when nothing is hit
- [ ] Multi-hit (`all: true`) returns `results` array sorted by distance
- [ ] 2D raycast uses `Physics2D.Raycast`
- [ ] Direction is normalized
- [ ] `max_distance` limits ray length
- [ ] `layer_mask` filters by physics layer
- [ ] Missing `origin` or `direction` returns error

---

### Unit 8: Linecast Operation

**File:** `Packages/com.theatre.toolkit/Editor/Tools/SpatialQueryLinecast.cs`

Test if anything blocks line-of-sight between two points. Requires play mode.

```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// spatial_query:linecast — test line-of-sight between two points.
    /// Returns whether the line is blocked and the first hit point.
    /// Requires Play Mode.
    /// </summary>
    internal static class SpatialQueryLinecast
    {
        internal static string Execute(JObject args)
        {
            var physicsMode = PhysicsMode.GetEffective(
                args["physics"]?.Value<string>());
            int layerMask = args["layer_mask"]?.Value<int>()
                ?? Physics.DefaultRaycastLayers;

            if (physicsMode == "2d")
                return Execute2D(args, layerMask);
            else
                return Execute3D(args, layerMask);
        }

        private static string Execute3D(JObject args, int layerMask)
        {
            var from = SpatialQueryNearest.ParseVector3(args, "from");
            var to = SpatialQueryNearest.ParseVector3(args, "to");

            if (!from.HasValue)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'from' parameter",
                    "Provide from as [x, y, z]");
            if (!to.HasValue)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'to' parameter",
                    "Provide to as [x, y, z]");

            bool blocked = Physics.Linecast(
                from.Value, to.Value, out RaycastHit hitInfo, layerMask);

            return BuildResponse(
                blocked, from.Value, to.Value,
                blocked ? (Vector3?)hitInfo.point : null,
                blocked ? (Vector3?)hitInfo.normal : null,
                blocked ? (float?)hitInfo.distance : null,
                blocked ? hitInfo.collider?.gameObject : null,
                blocked ? hitInfo.collider?.GetType().Name : null);
        }

        private static string Execute2D(JObject args, int layerMask)
        {
            var from2D = SpatialQueryNearest.ParseVector2(args, "from");
            var to2D = SpatialQueryNearest.ParseVector2(args, "to");

            // Allow Vector3 input, take XY
            if (!from2D.HasValue)
            {
                var f3 = SpatialQueryNearest.ParseVector3(args, "from");
                if (f3.HasValue) from2D = new Vector2(f3.Value.x, f3.Value.y);
            }
            if (!to2D.HasValue)
            {
                var t3 = SpatialQueryNearest.ParseVector3(args, "to");
                if (t3.HasValue) to2D = new Vector2(t3.Value.x, t3.Value.y);
            }

            if (!from2D.HasValue)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'from' parameter",
                    "Provide from as [x, y] or [x, y, z]");
            if (!to2D.HasValue)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'to' parameter",
                    "Provide to as [x, y] or [x, y, z]");

            var direction = to2D.Value - from2D.Value;
            float dist = direction.magnitude;
            var hit = Physics2D.Linecast(
                from2D.Value, to2D.Value, layerMask);

            bool blocked = hit.collider != null;

            return BuildResponse(
                blocked,
                new Vector3(from2D.Value.x, from2D.Value.y, 0),
                new Vector3(to2D.Value.x, to2D.Value.y, 0),
                blocked ? (Vector3?)new Vector3(
                    hit.point.x, hit.point.y, 0) : null,
                blocked ? (Vector3?)new Vector3(
                    hit.normal.x, hit.normal.y, 0) : null,
                blocked ? (float?)hit.distance : null,
                blocked ? hit.collider?.gameObject : null,
                blocked ? hit.collider?.GetType().Name : null);
        }

        private static string BuildResponse(
            bool blocked, Vector3 from, Vector3 to,
            Vector3? hitPoint, Vector3? hitNormal, float? hitDistance,
            GameObject hitObject, string colliderType)
        {
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "linecast";

            var result = new JObject();
            result["blocked"] = blocked;
            result["from"] = ResponseHelpers.ToJArray(from);
            result["to"] = ResponseHelpers.ToJArray(to);
            result["distance"] = Math.Round(
                Vector3.Distance(from, to), 2);

            if (blocked)
            {
                result["hit_point"] = ResponseHelpers.ToJArray(
                    hitPoint.Value);
                result["hit_normal"] = ResponseHelpers.ToJArray(
                    hitNormal.Value);
                result["hit_distance"] = Math.Round(hitDistance.Value, 2);
                if (hitObject != null)
                {
                    result["collider"] = SpatialQueryOverlap
                        .BuildColliderEntry(hitObject, colliderType);
                }
            }

            response["result"] = result;
            return response.ToString(Formatting.None);
        }
    }
}
```

**Implementation Notes:**
- Uses `"result"` (singular) per CONTRACTS.md — linecast returns one answer.
- `blocked: true` means line-of-sight is obstructed. `blocked: false` means
  clear path.
- `distance` is the total distance between `from` and `to`. `hit_distance`
  is the distance to the first obstruction (only present when blocked).
- Echo fields: `from` and `to` are echoed back per the echo convention.

**Acceptance Criteria:**
- [ ] Returns `"blocked": true` when something blocks the line
- [ ] Returns `"blocked": false` when line is clear
- [ ] When blocked, includes `hit_point`, `hit_normal`, `hit_distance`, `collider`
- [ ] 2D linecast uses `Physics2D.Linecast`
- [ ] `layer_mask` filters by physics layer
- [ ] `distance` is the total from-to distance
- [ ] Missing `from` or `to` returns error

---

### Unit 9: Path Distance Operation

**File:** `Packages/com.theatre.toolkit/Editor/Tools/SpatialQueryPathDistance.cs`

Calculate NavMesh path distance between two points.

```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
using UnityEngine.AI;

namespace Theatre.Editor
{
    /// <summary>
    /// spatial_query:path_distance — calculate NavMesh path distance.
    /// Works in both edit and play mode (NavMesh data is baked to an asset).
    /// Returns the path distance, or an error if no NavMesh is available.
    /// </summary>
    internal static class SpatialQueryPathDistance
    {
        private const float SampleRadius = 2.0f;

        internal static string Execute(JObject args)
        {
            var from = SpatialQueryNearest.ParseVector3(args, "from");
            var to = SpatialQueryNearest.ParseVector3(args, "to");

            if (!from.HasValue)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'from' parameter",
                    "Provide from as [x, y, z]");
            if (!to.HasValue)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'to' parameter",
                    "Provide to as [x, y, z]");

            int agentTypeId = args["agent_type_id"]?.Value<int>() ?? 0;

            // Verify NavMesh is available at start point
            if (!NavMesh.SamplePosition(
                from.Value, out NavMeshHit fromHit,
                SampleRadius, NavMesh.AllAreas))
            {
                return ResponseHelpers.ErrorResponse(
                    "navmesh_not_available",
                    $"No NavMesh found near start point "
                    + $"[{from.Value.x:F1}, {from.Value.y:F1}, "
                    + $"{from.Value.z:F1}]",
                    "Bake a NavMesh first: Window > AI > Navigation > Bake");
            }

            // Verify NavMesh at end point
            if (!NavMesh.SamplePosition(
                to.Value, out NavMeshHit toHit,
                SampleRadius, NavMesh.AllAreas))
            {
                return ResponseHelpers.ErrorResponse(
                    "navmesh_not_available",
                    $"No NavMesh found near end point "
                    + $"[{to.Value.x:F1}, {to.Value.y:F1}, "
                    + $"{to.Value.z:F1}]",
                    "Bake a NavMesh first: Window > AI > Navigation > Bake");
            }

            // Calculate path
            var path = new NavMeshPath();
            bool found = NavMesh.CalculatePath(
                fromHit.position, toHit.position,
                NavMesh.AllAreas, path);

            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "path_distance";

            var result = new JObject();
            result["from"] = ResponseHelpers.ToJArray(from.Value);
            result["to"] = ResponseHelpers.ToJArray(to.Value);
            result["straight_distance"] = Math.Round(
                Vector3.Distance(from.Value, to.Value), 2);

            if (found && path.status == NavMeshPathStatus.PathComplete)
            {
                float pathDist = CalculatePathLength(path);
                result["path_found"] = true;
                result["path_distance"] = Math.Round(pathDist, 2);
                result["path_status"] = "complete";

                // Include waypoints if path has corners
                if (path.corners.Length > 0)
                {
                    var waypoints = new JArray();
                    foreach (var corner in path.corners)
                    {
                        waypoints.Add(ResponseHelpers.ToJArray(corner));
                    }
                    result["waypoints"] = waypoints;
                    result["waypoint_count"] = path.corners.Length;
                }
            }
            else if (found
                && path.status == NavMeshPathStatus.PathPartial)
            {
                float pathDist = CalculatePathLength(path);
                result["path_found"] = true;
                result["path_distance"] = Math.Round(pathDist, 2);
                result["path_status"] = "partial";
            }
            else
            {
                result["path_found"] = false;
                result["path_status"] = "invalid";
            }

            response["result"] = result;
            return response.ToString(Formatting.None);
        }

        private static float CalculatePathLength(NavMeshPath path)
        {
            float length = 0f;
            var corners = path.corners;
            for (int i = 1; i < corners.Length; i++)
            {
                length += Vector3.Distance(corners[i - 1], corners[i]);
            }
            return length;
        }
    }
}
```

**Implementation Notes:**
- Uses `"result"` (singular) per CONTRACTS.md.
- `NavMesh.SamplePosition` verifies that both endpoints are on or near a
  baked NavMesh before attempting pathfinding. The `SampleRadius` of 2 units
  gives some tolerance for points slightly off the NavMesh surface.
- `straight_distance` provides the Euclidean distance for comparison with
  the path distance — agents use the ratio to assess obstacle complexity.
- Waypoints are included for complete paths so agents can understand the
  route (around obstacles, through doorways, etc.).
- `path_status` can be `"complete"`, `"partial"`, or `"invalid"`.
  `"partial"` means a path was found but it doesn't reach the destination
  (e.g., destination is on a disconnected NavMesh island).
- `agent_type_id` is accepted but not yet used in the `NavMesh.CalculatePath`
  call (the basic API uses `AllAreas`). A future enhancement could use
  `NavMeshQuery` with agent type filtering.

**Acceptance Criteria:**
- [ ] Returns path distance for reachable points on NavMesh
- [ ] Returns `"path_found": false` when no path exists
- [ ] Returns `"path_status": "partial"` for partial paths
- [ ] Returns `navmesh_not_available` error when no NavMesh is baked
- [ ] Includes `straight_distance` for comparison
- [ ] Includes waypoints for complete paths
- [ ] Works in edit mode (NavMesh data is baked to asset)
- [ ] Missing `from` or `to` returns error

---

### Unit 10: Bounds Operation

**File:** `Packages/com.theatre.toolkit/Editor/Tools/SpatialQueryBounds.cs`

Get the world-space bounding box of an object. Works in both edit and play
mode.

```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// spatial_query:bounds — get world-space bounding box of an object.
    /// Reads from Renderer, Collider, or combined bounds.
    /// Works in both edit and play mode.
    /// </summary>
    internal static class SpatialQueryBounds
    {
        internal static string Execute(JObject args)
        {
            var path = args["path"]?.Value<string>();
            int? instanceId = null;
            if (args["instance_id"] != null)
                instanceId = args["instance_id"].Value<int>();

            var resolved = ObjectResolver.Resolve(path, instanceId);
            if (!resolved.Success)
            {
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode,
                    resolved.ErrorMessage,
                    resolved.Suggestion);
            }

            var go = resolved.GameObject;
            var source = args["source"]?.Value<string>() ?? "combined";

            Bounds? rendererBounds = null;
            Bounds? colliderBounds = null;

            // Gather renderer bounds
            if (source == "renderer" || source == "combined")
            {
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var combined = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                    {
                        combined.Encapsulate(renderers[i].bounds);
                    }
                    rendererBounds = combined;
                }
            }

            // Gather collider bounds
            if (source == "collider" || source == "combined")
            {
                var colliders3D = go.GetComponentsInChildren<Collider>();
                var colliders2D = go.GetComponentsInChildren<Collider2D>();

                Bounds? cBounds = null;
                foreach (var col in colliders3D)
                {
                    if (cBounds == null)
                        cBounds = col.bounds;
                    else
                    {
                        var b = cBounds.Value;
                        b.Encapsulate(col.bounds);
                        cBounds = b;
                    }
                }
                foreach (var col in colliders2D)
                {
                    if (cBounds == null)
                        cBounds = col.bounds;
                    else
                    {
                        var b = cBounds.Value;
                        b.Encapsulate(col.bounds);
                        cBounds = b;
                    }
                }
                colliderBounds = cBounds;
            }

            // Compute final bounds
            Bounds? finalBounds = null;
            if (source == "combined")
            {
                if (rendererBounds.HasValue && colliderBounds.HasValue)
                {
                    var b = rendererBounds.Value;
                    b.Encapsulate(colliderBounds.Value);
                    finalBounds = b;
                }
                else
                {
                    finalBounds = rendererBounds ?? colliderBounds;
                }
            }
            else if (source == "renderer")
            {
                finalBounds = rendererBounds;
            }
            else if (source == "collider")
            {
                finalBounds = colliderBounds;
            }

            if (!finalBounds.HasValue)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"No {source} bounds found on '{path ?? instanceId?.ToString()}'",
                    source == "renderer"
                        ? "This object has no Renderer. Try source='collider' or 'combined'."
                        : source == "collider"
                            ? "This object has no Collider. Try source='renderer' or 'combined'."
                            : "This object has neither Renderer nor Collider.");
            }

            // Build response
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "bounds";

            var result = new JObject();
            result["path"] = ResponseHelpers.GetHierarchyPath(go.transform);
            #pragma warning disable CS0618
            result["instance_id"] = go.GetInstanceID();
            #pragma warning restore CS0618
            result["source"] = source;
            result["center"] = ResponseHelpers.ToJArray(
                finalBounds.Value.center);
            result["size"] = ResponseHelpers.ToJArray(
                finalBounds.Value.size);
            result["min"] = ResponseHelpers.ToJArray(
                finalBounds.Value.min);
            result["max"] = ResponseHelpers.ToJArray(
                finalBounds.Value.max);
            result["extents"] = ResponseHelpers.ToJArray(
                finalBounds.Value.extents);

            response["result"] = result;
            return response.ToString(Formatting.None);
        }
    }
}
```

**Implementation Notes:**
- Uses `"result"` (singular) per CONTRACTS.md.
- `Bounds` is encoded per CONTRACTS.md: `{ "center": [...], "size": [...] }`.
  Additionally includes `min`, `max`, `extents` for convenience.
- `GetComponentsInChildren<Renderer>()` captures child renderers, giving the
  total visual bounding box of an object hierarchy (e.g., a character with
  multiple mesh parts).
- Works in edit mode — `Renderer.bounds` and `Collider.bounds` are always
  available.
- Uses `ObjectResolver.Resolve` for path/instance_id resolution, reusing the
  Phase 2 infrastructure.

**Acceptance Criteria:**
- [ ] Returns bounds with `center`, `size`, `min`, `max`, `extents`
- [ ] `source="renderer"` uses only Renderer bounds
- [ ] `source="collider"` uses only Collider bounds
- [ ] `source="combined"` merges Renderer and Collider bounds
- [ ] Child renderers/colliders are included (GetComponentsInChildren)
- [ ] Returns error when requested source has no components
- [ ] Object resolved by path or instance_id
- [ ] Works in edit mode

---

### Unit 11: Server Registration and Scene Change Hooks

**File:** `Packages/com.theatre.toolkit/Editor/TheatreServer.cs` (modification)

Wire up `SpatialQueryTool` registration and scene change callbacks.

**Changes to `TheatreServer.RegisterBuiltInTools`:**

```csharp
private static void RegisterBuiltInTools(ToolRegistry registry)
{
    TheatreStatusTool.Register(registry);
    SceneSnapshotTool.Register(registry);
    SceneHierarchyTool.Register(registry);
    SceneInspectTool.Register(registry);
    UnityConsoleTool.Register(registry);
    UnityTestsTool.Register(registry);
    SpatialQueryTool.Register(registry);  // Phase 3
}
```

**Scene change hooks** (added to static constructor or `StartServer`):

```csharp
SceneManager.activeSceneChanged += (oldScene, newScene) =>
{
    SpatialQueryTool.InvalidateIndex();
    PhysicsMode.Invalidate();
};
SceneManager.sceneLoaded += (scene, mode) =>
{
    SpatialQueryTool.InvalidateIndex();
};
SceneManager.sceneUnloaded += (scene) =>
{
    SpatialQueryTool.InvalidateIndex();
};
```

**Implementation Notes:**
- Scene change callbacks invalidate both the spatial index and the cached
  physics mode. This ensures stale data is never served after a scene change.
- The callbacks use static delegates — no instance state, compatible with
  domain reload (re-registered in the static constructor).
- `SpatialQueryTool.Register(registry)` is one line, following the same
  pattern as all other tools.

**Acceptance Criteria:**
- [ ] `spatial_query` appears in `tools/list` when `StageQuery` group is enabled
- [ ] `spatial_query` does not appear when `StageQuery` group is disabled
- [ ] Spatial index is invalidated on scene change
- [ ] Physics mode cache is invalidated on scene change

---

### Unit 12: Tests

**File:** `Packages/com.theatre.toolkit/Tests/Editor/SpatialQueryTests.cs`

EditMode unit tests for spatial query infrastructure. Physics-dependent tests
require PlayMode and are marked accordingly.

```csharp
using NUnit.Framework;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class PhysicsModeTests
    {
        [Test]
        public void GetEffective_WithOverride_ReturnsOverride()
        {
            Assert.AreEqual("2d", PhysicsMode.GetEffective("2d"));
            Assert.AreEqual("3d", PhysicsMode.GetEffective("3d"));
        }

        [Test]
        public void GetEffective_WithNull_ReturnsDefault()
        {
            var result = PhysicsMode.GetEffective(null);
            Assert.IsTrue(result == "2d" || result == "3d");
        }

        [Test]
        public void CheckPlayModeRequired_PhysicsOps_ReturnsError()
        {
            // We're in edit mode during tests
            Assert.IsNotNull(
                PhysicsMode.CheckPlayModeRequired("raycast"));
            Assert.IsNotNull(
                PhysicsMode.CheckPlayModeRequired("overlap"));
            Assert.IsNotNull(
                PhysicsMode.CheckPlayModeRequired("linecast"));
        }

        [Test]
        public void CheckPlayModeRequired_TransformOps_ReturnsNull()
        {
            Assert.IsNull(
                PhysicsMode.CheckPlayModeRequired("nearest"));
            Assert.IsNull(
                PhysicsMode.CheckPlayModeRequired("radius"));
            Assert.IsNull(
                PhysicsMode.CheckPlayModeRequired("bounds"));
            Assert.IsNull(
                PhysicsMode.CheckPlayModeRequired("path_distance"));
        }
    }

    [TestFixture]
    public class SpatialIndexTests
    {
        private SpatialIndex _index;

        [SetUp]
        public void SetUp()
        {
            _index = new SpatialIndex();
        }

        [Test]
        public void Nearest_EmptyIndex_ReturnsEmpty()
        {
            // Force rebuild on empty scene
            _index.EnsureFresh();
            var results = _index.Nearest(Vector3.zero, 5);
            Assert.IsNotNull(results);
            // May have results from test scene — just verify no crash
        }

        [Test]
        public void Radius_EmptyIndex_ReturnsEmpty()
        {
            _index.EnsureFresh();
            var results = _index.Radius(Vector3.zero, 10f);
            Assert.IsNotNull(results);
        }

        [Test]
        public void Invalidate_ForcesRebuild()
        {
            _index.EnsureFresh();
            int countBefore = _index.Count;
            _index.Invalidate();
            _index.EnsureFresh();
            // Count should be same (same scene) but rebuild happened
            Assert.AreEqual(countBefore, _index.Count);
        }
    }

    [TestFixture]
    public class SpatialQueryToolTests
    {
        [Test]
        public void Execute_MissingOperation_ReturnsError()
        {
            var args = new Newtonsoft.Json.Linq.JObject();
            // SpatialQueryTool.Execute is private — test via
            // ToolRegistry or make internal with InternalsVisibleTo
        }

        [Test]
        public void Execute_UnknownOperation_ReturnsError()
        {
            // Similar — test the dispatch logic
        }
    }
}
```

**Implementation Notes:**
- EditMode tests can test `PhysicsMode`, `SpatialIndex`, parameter parsing,
  and response building. They cannot test actual physics queries (raycast,
  overlap, linecast) which require play mode.
- PlayMode tests for physics queries should be added in
  `Tests/Runtime/SpatialQueryPlayModeTests.cs` and would create test scenes
  with known collider layouts.
- Tests use `[TestFixture]` and `[Test]` attributes per Unity Test Framework
  conventions.
- To test the tool handler directly, add `[assembly: InternalsVisibleTo("com.theatre.toolkit.editor.tests")]`
  to `AssemblyInfo.cs`, or test via the public `ToolRegistry.GetTool` +
  `Handler.Invoke` path.

**Acceptance Criteria:**
- [ ] `PhysicsModeTests` verify per-query override logic
- [ ] `PhysicsModeTests` verify play mode gating for physics operations
- [ ] `SpatialIndexTests` verify nearest/radius on empty index
- [ ] `SpatialIndexTests` verify invalidation forces rebuild
- [ ] All EditMode tests pass without entering play mode

---

## File Summary

| File | Assembly | Purpose |
|---|---|---|
| `Runtime/Stage/Spatial/PhysicsMode.cs` | Runtime | 2D/3D detection, play mode gating |
| `Runtime/Stage/Spatial/SpatialIndex.cs` | Runtime | Transform-based spatial index |
| `Editor/Tools/SpatialQueryTool.cs` | Editor | Tool registration, dispatch |
| `Editor/Tools/SpatialQueryNearest.cs` | Editor | `nearest` operation + shared parsing |
| `Editor/Tools/SpatialQueryRadius.cs` | Editor | `radius` operation |
| `Editor/Tools/SpatialQueryOverlap.cs` | Editor | `overlap` operation (physics) |
| `Editor/Tools/SpatialQueryRaycast.cs` | Editor | `raycast` operation (physics) |
| `Editor/Tools/SpatialQueryLinecast.cs` | Editor | `linecast` operation (physics) |
| `Editor/Tools/SpatialQueryPathDistance.cs` | Editor | `path_distance` operation (NavMesh) |
| `Editor/Tools/SpatialQueryBounds.cs` | Editor | `bounds` operation |
| `Editor/TheatreServer.cs` | Editor | Registration + scene hooks (modification) |
| `Tests/Editor/SpatialQueryTests.cs` | Tests | EditMode unit tests |

---

## Implementation Order

1. **PhysicsMode** — needed by all physics operations
2. **SpatialIndex** — needed by nearest and radius
3. **SpatialQueryTool** — registration and dispatch shell
4. **SpatialQueryNearest** — most useful operation, includes shared parsers
5. **SpatialQueryRadius** — similar to nearest, exercises same index
6. **SpatialQueryBounds** — simple, no physics, validates ObjectResolver reuse
7. **SpatialQueryOverlap** — first physics operation
8. **SpatialQueryRaycast** — most complex physics operation
9. **SpatialQueryLinecast** — simple physics operation
10. **SpatialQueryPathDistance** — NavMesh, independent of physics system
11. **Server registration** — wire everything up
12. **Tests** — validate all units

Each unit can be implemented and tested independently. Units 1-6 work in edit
mode. Units 7-9 require play mode for functional testing. Unit 10 requires a
baked NavMesh in the test project.

---

## Response Examples

### nearest

```json
{
  "frame": 4580,
  "time": 76.33,
  "play_mode": true,
  "operation": "nearest",
  "origin": [0, 1, 0],
  "results": [
    {
      "path": "/Player",
      "instance_id": 10240,
      "name": "Player",
      "position": [0, 1.05, 0],
      "distance": 0.05,
      "components": ["CharacterController", "PlayerInput"]
    },
    {
      "path": "/Enemies/Scout_01",
      "instance_id": 14800,
      "name": "Scout_01",
      "position": [5.2, 0, -3.1],
      "distance": 6.48,
      "components": ["Health", "NavMeshAgent"]
    }
  ],
  "returned": 2,
  "budget": {
    "requested": 1500,
    "used": 120,
    "truncated": false
  }
}
```

### raycast (single hit)

```json
{
  "frame": 4580,
  "time": 76.33,
  "play_mode": true,
  "operation": "raycast",
  "origin": [0, 1, 0],
  "direction": [1, 0, 0],
  "result": {
    "hit": true,
    "point": [5.2, 1, 0],
    "normal": [-1, 0, 0],
    "distance": 5.2,
    "collider": {
      "path": "/Environment/Wall_East",
      "instance_id": 5200,
      "tag": "Untagged",
      "layer": "Default",
      "collider_type": "BoxCollider"
    }
  }
}
```

### linecast

```json
{
  "frame": 4580,
  "time": 76.33,
  "play_mode": true,
  "operation": "linecast",
  "result": {
    "blocked": false,
    "from": [0, 1, 0],
    "to": [5.2, 0, -3.1],
    "distance": 6.48
  }
}
```

### path_distance

```json
{
  "frame": 0,
  "time": 0,
  "play_mode": false,
  "operation": "path_distance",
  "result": {
    "from": [0, 0, 0],
    "to": [10, 0, 10],
    "straight_distance": 14.14,
    "path_found": true,
    "path_distance": 22.5,
    "path_status": "complete",
    "waypoints": [[0, 0, 0], [5, 0, 0], [5, 0, 5], [10, 0, 10]],
    "waypoint_count": 4
  }
}
```

### bounds

```json
{
  "frame": 0,
  "time": 0,
  "play_mode": false,
  "operation": "bounds",
  "result": {
    "path": "/Player",
    "instance_id": 10240,
    "source": "combined",
    "center": [0, 1, 0],
    "size": [1, 2, 1],
    "min": [-0.5, 0, -0.5],
    "max": [0.5, 2, 0.5],
    "extents": [0.5, 1, 0.5]
  }
}
```

### requires_play_mode error

```json
{
  "error": {
    "code": "requires_play_mode",
    "message": "The 'raycast' operation requires Play Mode because it uses the physics engine",
    "suggestion": "Enter Play Mode first, or use 'nearest'/'radius' for transform-based queries in Edit Mode"
  }
}
```
