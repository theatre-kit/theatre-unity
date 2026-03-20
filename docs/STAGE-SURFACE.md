# Unity Theatre — Stage Tool Surface

Stage gives AI agents spatial awareness of a running Unity game. It reads
scene state, answers spatial questions, tracks changes, and captures
frame-by-frame recordings for temporal analysis.

Stage has two parallel tool sets: **GameObject** tools for traditional
Unity scenes, and **ECS** tools for DOTS/Entities worlds. Both can be
active simultaneously but projects typically enable one or the other.

---

## Object Addressing

### GameObject Mode

GameObjects are addressed by **hierarchy path** with **InstanceID fallback**.

```
/Environment/Enemies/Scout_02          ← primary, human-readable
```

Every response that references a GameObject includes both:

```json
{
  "path": "/Environment/Enemies/Scout_02",
  "instance_id": 14820
}
```

The agent can use either for subsequent queries. Path is preferred for
readability. InstanceID is stable within a play session even if the object
is reparented.

**Path format**: Forward-slash separated, rooted at scene level. Matches
`Transform` hierarchy. Scene name is omitted for single-scene projects;
multi-scene uses `SceneName:/Path/To/Object`.

**Ambiguity**: If multiple GameObjects share a path (duplicate names under
the same parent), Theatre appends the sibling index: `/Enemies/Scout (1)`,
`/Enemies/Scout (2)`. This matches Unity's own disambiguation.

### ECS Mode

Entities are addressed by **Entity index + version**, with **archetype
description** for context.

```json
{
  "entity": { "index": 42, "version": 3 },
  "archetype": ["LocalTransform", "Health", "EnemyTag"],
  "world": "Default World"
}
```

Discovery uses archetype queries: "all entities with `LocalTransform` and
`EnemyTag`". Subsequent operations use the entity index+version directly.

---

## Tool Groups

### StageGameObject — Scene Awareness

#### `scene_snapshot`

Overview of the current scene from a spatial perspective. Returns a
token-budgeted summary of GameObjects with positions, organized by
proximity to a focus point or by hierarchy.

**Parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `focus` | `Vector3?` | Camera position | Center point for spatial organization |
| `radius` | `float?` | `null` (all) | Limit to objects within radius of focus |
| `include_components` | `string[]?` | `null` | Filter: only objects with these component types |
| `exclude_inactive` | `bool` | `true` | Skip disabled GameObjects |
| `max_depth` | `int` | `3` | Hierarchy depth limit for nested objects |
| `budget` | `int` | `1500` | Target response size in tokens |
| `scene` | `string?` | Active scene | Specific scene to snapshot |

**Response shape:**

```json
{
  "scene": "SampleScene",
  "play_mode": true,
  "frame": 4580,
  "focus": [0, 1, 0],
  "summary": {
    "total_objects": 247,
    "returned": 35,
    "groups": [
      {
        "label": "Enemies (12)",
        "center": [5.2, 0, -3.1],
        "spread": 8.4
      }
    ]
  },
  "objects": [
    {
      "path": "/Player",
      "instance_id": 10240,
      "position": [0, 1.05, 0],
      "rotation_euler": [0, 90, 0],
      "components": ["CharacterController", "PlayerInput", "Health"],
      "active": true,
      "children_count": 5,
      "distance": 1.05
    }
  ]
}
```

#### `scene_hierarchy`

Navigate the Transform hierarchy. Supports find/search operations.

**Operations:**

| Operation | Description |
|---|---|
| `list` | List children of a path (or root). Pagination supported. |
| `find` | Find GameObjects by name pattern (glob or regex) |
| `search` | Find GameObjects by component type, tag, or layer |
| `path` | Get full hierarchy path + metadata for a specific instance_id |

**Parameters (find):**

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"find"` |
| `pattern` | `string` | Name pattern: `"Scout*"`, `"*Door*"` |
| `root` | `string?` | Limit search to subtree |
| `include_inactive` | `bool` | Include disabled objects (default false) |

#### `scene_inspect`

Deep inspection of a single GameObject — all components, all serialized
properties, references, hierarchy context.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `path` | `string?` | Hierarchy path (use this or instance_id) |
| `instance_id` | `int?` | InstanceID (use this or path) |
| `components` | `string[]?` | Filter to specific component types |
| `depth` | `string` | `"summary"` (default), `"full"`, or `"properties"` |
| `budget` | `int` | Target token budget (default 1500) |

**Response (full depth):**

```json
{
  "path": "/Player",
  "instance_id": 10240,
  "tag": "Player",
  "layer": "Default",
  "static_flags": [],
  "active_self": true,
  "active_hierarchy": true,
  "scene": "SampleScene",
  "is_prefab_instance": true,
  "prefab_asset": "Assets/Prefabs/Player.prefab",
  "components": [
    {
      "type": "Transform",
      "properties": {
        "local_position": [0, 0, 0],
        "local_rotation": [0, 0, 0, 1],
        "local_scale": [1, 1, 1],
        "position": [0, 1.05, 0],
        "euler_angles": [0, 90, 0]
      }
    },
    {
      "type": "CharacterController",
      "properties": {
        "center": [0, 1, 0],
        "radius": 0.5,
        "height": 2.0,
        "slope_limit": 45,
        "step_offset": 0.3,
        "is_grounded": true,
        "velocity": [0, 0, 2.5]
      }
    },
    {
      "type": "Health",
      "script": "Assets/Scripts/Health.cs",
      "properties": {
        "max_hp": 100,
        "current_hp": 73,
        "is_invulnerable": false
      }
    }
  ],
  "children": [
    { "path": "/Player/Model", "instance_id": 10241, "children_count": 3 },
    { "path": "/Player/Camera", "instance_id": 10245, "children_count": 0 }
  ]
}
```

#### `scene_delta`

What changed since the last snapshot or since a specific frame.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `since_frame` | `int?` | Compare against this frame (or last query if null) |
| `paths` | `string[]?` | Limit to specific objects |
| `track` | `string[]?` | Limit to specific properties (e.g., `["position", "current_hp"]`) |
| `budget` | `int` | Target token budget |

**Response:**

```json
{
  "from_frame": 4500,
  "to_frame": 4580,
  "elapsed_seconds": 1.33,
  "changes": [
    {
      "path": "/Player",
      "instance_id": 10240,
      "changed": {
        "position": { "from": [0, 1, -2], "to": [0, 1.05, 0] },
        "velocity": { "from": [0, 0, 3], "to": [0, 0, 2.5] }
      }
    },
    {
      "path": "/Enemies/Scout_02",
      "instance_id": 14820,
      "changed": {
        "position": { "from": [8, 0, -1], "to": [6.5, 0, -2.3] }
      }
    }
  ],
  "spawned": [],
  "destroyed": [
    { "path": "/Projectiles/Bullet_47", "instance_id": 18920 }
  ]
}
```

---

### StageQuery — Spatial Questions

#### `spatial_query`

Targeted spatial questions about the scene. Compound tool with operation types.

**Operations:**

##### `nearest`

Find the N closest GameObjects to a point.

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"nearest"` |
| `origin` | `Vector3` | Query center |
| `count` | `int` | Max results (default 5) |
| `include_components` | `string[]?` | Filter by component type |
| `exclude_tags` | `string[]?` | Exclude by tag |
| `max_distance` | `float?` | Distance cutoff |

##### `radius`

Find all GameObjects within a radius.

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"radius"` |
| `origin` | `Vector3` | Sphere center |
| `radius` | `float` | Search radius |
| `include_components` | `string[]?` | Filter |
| `sort_by` | `string` | `"distance"` (default) or `"name"` |

##### `overlap`

Find all GameObjects in a box, sphere, or capsule region using physics
overlap queries. Only finds objects with colliders.

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"overlap"` |
| `shape` | `string` | `"sphere"` / `"circle"` (2D), `"box"`, `"capsule"` |
| `center` | `Vector3` or `Vector2` | Shape center |
| `size` | `Vector3` or `Vector2` | Box half-extents, or `[radius]` for sphere/circle |
| `layer_mask` | `int?` | Physics layer mask |
| `physics` | `string?` | `"3d"` or `"2d"` (default: project setting) |

##### `raycast`

Cast a ray and report hits.

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"raycast"` |
| `origin` | `Vector3` or `Vector2` | Ray start |
| `direction` | `Vector3` or `Vector2` | Ray direction (normalized) |
| `max_distance` | `float` | Max ray length (default 1000) |
| `all` | `bool` | Return all hits or just first (default false) |
| `layer_mask` | `int?` | Physics layer mask |
| `physics` | `string?` | `"3d"` or `"2d"` (default: project setting) |

**Raycast response:**

```json
{
  "result": {
    "hit": true,
    "point": [5.2, 0, -3.1],
    "normal": [0, 1, 0],
    "distance": 12.4,
    "collider": {
      "path": "/Environment/Floor",
      "instance_id": 5000,
      "tag": "Ground",
      "layer": "Default"
    }
  }
}
```

##### `linecast`

Test if anything blocks line-of-sight between two points.

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"linecast"` |
| `from` | `Vector3` or `Vector2` | Start point |
| `to` | `Vector3` or `Vector2` | End point |
| `layer_mask` | `int?` | Physics layer mask |
| `physics` | `string?` | `"3d"` or `"2d"` (default: project setting) |

##### `path_distance`

Calculate NavMesh path distance between two points (if NavMesh is baked).

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"path_distance"` |
| `from` | `Vector3` | Start |
| `to` | `Vector3` | End |
| `agent_type_id` | `int?` | NavMesh agent type |

##### `bounds`

Get the world-space bounding box of an object (Renderer bounds, Collider
bounds, or combined).

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"bounds"` |
| `path` | `string` | Target object |
| `source` | `string` | `"renderer"`, `"collider"`, or `"combined"` |

**Results envelope:**

- `nearest`, `radius`, `overlap`: `"results": [...]` (plural array)
- `raycast`, `linecast`, `path_distance`, `bounds`: `"result": {...}` (singular)

---

### StageWatch — Change Subscriptions

#### `watch`

Subscribe to changes or conditions. Triggers are delivered as MCP
notifications via the SSE stream.

**Operations:**

##### `create`

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"create"` |
| `target` | `string` | Hierarchy path or `"*"` for global |
| `track` | `string[]` | Properties to watch: `["position", "current_hp"]` |
| `condition` | `object?` | Optional trigger condition |
| `throttle_ms` | `int` | Min interval between notifications (default 500) |
| `label` | `string?` | Human-readable name for this watch |

**Condition types:**

```json
{ "type": "threshold", "property": "current_hp", "below": 25 }
{ "type": "proximity", "target": "/Player", "within": 5.0 }
{ "type": "entered_region", "min": [0, 0, 0], "max": [10, 5, 10] }
{ "type": "property_changed", "property": "is_grounded" }
{ "type": "destroyed" }
{ "type": "spawned", "name_pattern": "Bullet*" }
```

##### `remove`

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"remove"` |
| `watch_id` | `string` | ID from create response |

##### `list`

List all active watches with their status.

##### `check`

Manually poll a watch for current state (without waiting for notification).

**Watch notification (via SSE):**

```json
{
  "jsonrpc": "2.0",
  "method": "notifications/theatre/watch_triggered",
  "params": {
    "watch_id": "w_01",
    "label": "enemy_low_health",
    "frame": 5200,
    "trigger": {
      "path": "/Enemies/Scout_02",
      "instance_id": 14820,
      "condition": "threshold",
      "property": "current_hp",
      "value": 22,
      "threshold": 25
    }
  }
}
```

---

### StageAction — Game State Manipulation

#### `action`

Manipulate game state for debugging purposes. Only available in Play Mode.

**Operations:**

| Operation | Description |
|---|---|
| `set_property` | Set a serialized property value on a component |
| `teleport` | Move a GameObject to a specific position |
| `set_active` | Enable/disable a GameObject |
| `set_timescale` | Change `Time.timeScale` |
| `pause` | Pause the game (`EditorApplication.isPaused`) |
| `step` | Advance a single frame while paused |
| `unpause` | Resume |
| `invoke_method` | Call a public method on a component (limited to simple signatures) |

**Example — teleport:**

```json
{
  "operation": "teleport",
  "path": "/Player",
  "position": [10, 0, 5],
  "rotation_euler": [0, 180, 0]
}
```

**Example — set property:**

```json
{
  "operation": "set_property",
  "path": "/Enemies/Scout_02",
  "component": "Health",
  "property": "current_hp",
  "value": 100
}
```

---

### StageRecording — Dashcam

#### `recording`

Capture and analyze frame-by-frame game state. Records to SQLite.

**Operations:**

| Operation | Description |
|---|---|
| `start` | Begin recording with optional filters |
| `stop` | Stop recording, returns clip metadata |
| `marker` | Insert a named marker at current frame |
| `list_clips` | List saved recording clips |
| `delete_clip` | Delete a recording |
| `query_range` | Query recorded frames in a time/frame range |
| `diff_frames` | Compare two specific frames |
| `clip_info` | Get metadata, duration, frame count, markers |
| `analyze` | Run analysis queries on a clip (e.g., "when did HP drop below 50?") |

**Example — start recording:**

```json
{
  "operation": "start",
  "label": "wall_clip_repro",
  "track_paths": ["/Player", "/Environment/Walls/*"],
  "track_components": ["Transform", "Health", "CharacterController"],
  "capture_rate": 60
}
```

**Example — query range:**

```json
{
  "operation": "query_range",
  "clip_id": "rec_001",
  "from_frame": 100,
  "to_frame": 200,
  "paths": ["/Player"],
  "properties": ["position", "is_grounded", "velocity"]
}
```

---

## ECS Tool Groups

### ECSWorld — World Awareness

#### `ecs_world`

Overview of active ECS Worlds, their entity counts, archetypes, and systems.

**Operations:**

| Operation | Description |
|---|---|
| `list_worlds` | List all active Worlds |
| `world_summary` | Archetype breakdown, entity counts, system groups |
| `list_archetypes` | All archetypes with component sets and entity counts |
| `list_systems` | System execution order, enabled state, world assignment |

#### `ecs_snapshot`

Spatial snapshot of entities in a World, analogous to `scene_snapshot`.

| Parameter | Type | Description |
|---|---|---|
| `world` | `string?` | World name (default: "Default World") |
| `required_components` | `string[]` | Entities must have all of these |
| `focus` | `Vector3?` | Spatial center (requires `LocalTransform`) |
| `radius` | `float?` | Limit by distance from focus |
| `budget` | `int` | Token budget |

**Response:**

```json
{
  "world": "Default World",
  "entity_count": 15000,
  "returned": 50,
  "entities": [
    {
      "entity": { "index": 42, "version": 3 },
      "archetype": ["LocalTransform", "Health", "EnemyTag"],
      "position": [5.2, 0, -3.1],
      "distance": 8.4
    }
  ]
}
```

### ECSEntity — Entity Inspection

#### `ecs_inspect`

Read all component data for an entity.

| Parameter | Type | Description |
|---|---|---|
| `entity_index` | `int` | Entity index |
| `entity_version` | `int` | Entity version |
| `world` | `string?` | World name |
| `components` | `string[]?` | Filter to specific components |

### ECSQuery — Spatial Queries on Entities

#### `ecs_query`

Same spatial query operations as `spatial_query` but over entities with
`LocalTransform` (or `LocalToWorld`) components.

Supports: `nearest`, `radius`, `overlap` (AABB), `raycast` (if Unity Physics
or Havok Physics is installed).

### ECSAction — Entity Mutation

#### `ecs_action`

Modify entity component data for debugging.

| Operation | Description |
|---|---|
| `set_component` | Set component field values |
| `add_component` | Add a component to an entity |
| `remove_component` | Remove a component from an entity |
| `destroy_entity` | Destroy an entity |
| `create_entity` | Create a new entity with specified components |

---

## Cross-Cutting Concerns

### Token Budgeting

Every tool that returns variable-length data accepts a `budget` parameter.
The server shapes responses to fit within the budget:

1. Estimate response size
2. If over budget, reduce detail level (full → summary → count-only)
3. If still over, paginate and return a cursor
4. Response includes `budget_used` and `total_available` so the agent
   can judge whether to drill deeper

### Pagination

Large result sets return a cursor:

```json
{
  "results": [...],
  "cursor": "eyJwYWdlIjoyLCJvZmZzZXQiOjUwfQ==",
  "has_more": true,
  "total": 247,
  "returned": 50
}
```

Pass `cursor` back to the same tool to get the next page.

### Frame Reference

All spatial data includes the frame number at which it was captured. This
lets agents detect stale data and correlate across tools:

```json
{
  "frame": 4580,
  "time": 76.33,
  ...
}
```

### Play Mode Requirement

Spatial queries that depend on physics (raycast, overlap, linecast) require
Play Mode. If called in Edit Mode, they return an error with:

```json
{
  "error": {
    "code": "requires_play_mode",
    "message": "Physics queries require Play Mode",
    "suggestion": "Enter Play Mode or use scene_hierarchy for edit-mode queries"
  }
}
```

Static spatial queries (nearest by transform, hierarchy search) work in
both modes.
