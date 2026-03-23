# Stage Tools — Parameter Reference

Complete parameter reference for Theatre's observation tools. All tools return `snake_case` fields.
Every response from Stage tools includes frame context: `frame` (int), `time` (float), `play_mode` (bool).

## Table of Contents

- [scene_snapshot](#scene_snapshot)
- [scene_hierarchy](#scene_hierarchy)
- [scene_inspect](#scene_inspect)
- [scene_delta](#scene_delta)
- [spatial_query](#spatial_query)
- [watch](#watch)
- [action](#action)
- [recording](#recording)
- [theatre_status](#theatre_status)
- [unity_console](#unity_console)
- [unity_tests](#unity_tests)

---

## scene_snapshot

Budgeted spatial overview with clustering. No operation parameter.

**Parameters:**

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `focus` | `[x,y,z]` | camera pos | Center point for spatial organization |
| `radius` | float | _(all)_ | Limit to objects within this distance of focus |
| `include_components` | string[] | _(all)_ | Filter by component types |
| `exclude_inactive` | bool | `true` | Skip disabled GameObjects |
| `max_depth` | int | `3` | Hierarchy depth limit (0-20) |
| `budget` | int | `1500` | Target response size in tokens (100-4000) |
| `scene` | string | _(all)_ | Limit to specific scene name |

**Response shape:**
```
scene, frame, time, focus, summary.total_objects, summary.groups[{label, center, spread, count}],
objects[{path, instance_id, position, active, children_count, distance, components}],
returned, budget{used, total, truncated, reason, suggestion}
```

---

## scene_hierarchy

Navigate the transform tree. 4 operations.

### list
List children of a path. Omit `path` for scene roots.

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `path` | string | _(roots)_ | Parent path |
| `include_inactive` | bool | `false` | Include disabled objects |
| `cursor` | string | | Pagination cursor |

### find
Glob search by name pattern.

| Param | Type | Description |
|-------|------|-------------|
| `pattern` | string | Name glob (`Scout*`, `*Door*`) |
| `path` | string | Subtree root to limit search |
| `include_inactive` | bool | Include disabled objects |
| `cursor` | string | Pagination cursor |

### search
Filter by component type, tag, or layer.

| Param | Type | Description |
|-------|------|-------------|
| `include_components` | string[] | Component type filter |
| `tag` | string | Tag filter |
| `layer` | string | Layer name filter |
| `path` | string | Subtree root |
| `include_inactive` | bool | Include disabled objects |
| `cursor` | string | Pagination cursor |

### path
Look up a single object by instance_id.

| Param | Type | Description |
|-------|------|-------------|
| `instance_id` | int | The instance ID to resolve |

**Response (list/find/search):**
```
results[{path, instance_id, name, position, active, children_count, components}],
returned, pagination{cursor, has_more, returned, total}
```

**Response (path):**
```
result{path, instance_id, name, scene, position, active, children_count, parent{path, instance_id}}
```

---

## scene_inspect

Deep-inspect a single GameObject. No operation parameter.

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `path` | string | | Hierarchy path (`/Player`) |
| `instance_id` | int | | Alternative to path |
| `components` | string[] | _(all)_ | Filter to specific component types |
| `depth` | string | `"summary"` | `"summary"`, `"full"`, or `"properties"` (includes hidden) |
| `budget` | int | `1500` | Token budget (100-4000) |

**Response:**
```
path, instance_id, tag, layer, static_flags, active_self, active_hierarchy, scene,
is_prefab_instance, prefab_asset,
components[{type, enabled, properties{...}}],
children[{path, instance_id, children_count}],
budget{used, total, truncated}
```

---

## scene_delta

Detect changes since last snapshot or delta call. No operation parameter.

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `since_frame` | int | _(auto)_ | Frame to compare against |
| `paths` | string[] | _(all)_ | Limit to specific paths |
| `track` | string[] | | Extra properties to track (e.g., `["current_hp"]`) |
| `budget` | int | `1500` | Token budget (100-4000) |

**Response:**
```
from_frame, to_frame, elapsed_seconds,
changes[{path, instance_id, changed{position{from,to}, euler_angles{from,to}, ...}}],
spawned[{path, instance_id, position}],
destroyed[{path, instance_id}]
```

---

## spatial_query

7 spatial query operations. All work in both edit and play mode.

### nearest
Transform-distance nearest neighbors (no physics engine needed).

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `origin` | `[x,y,z]` | | Query center |
| `count` | int | `5` | Max results |
| `max_distance` | float | | Distance cutoff |
| `include_components` | string[] | | Filter by component |
| `exclude_tags` | string[] | | Exclude by tag |
| `budget` | int | `1500` | Token budget |

### radius
All objects within a radius.

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `origin` | `[x,y,z]` | | Query center |
| `radius` | float | | Search radius |
| `include_components` | string[] | | Filter by component |
| `exclude_tags` | string[] | | Exclude by tag |
| `sort_by` | string | `"distance"` | `"distance"` or `"name"` |
| `budget` | int | `1500` | Token budget |

### overlap
Physics overlap query (sphere, box, capsule, circle).

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `shape` | string | | `"sphere"`, `"circle"`, `"box"`, `"capsule"` |
| `center` | `[x,y,z]` | | Shape center |
| `size` | number[] | | `[radius]` for sphere/circle, `[x,y,z]` half-extents for box |
| `layer_mask` | int | | Physics layer mask |
| `physics` | string | _(auto)_ | `"3d"` or `"2d"` override |

### raycast
Physics raycast. Returns first hit or all hits.

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `origin` | `[x,y,z]` | | Ray start |
| `direction` | `[x,y,z]` | | Ray direction (normalized) |
| `max_distance` | float | `1000` | Max ray length |
| `all` | bool | `false` | Return all hits |
| `layer_mask` | int | | Physics layer mask |
| `physics` | string | _(auto)_ | `"3d"` or `"2d"` override |

**Single hit response:** `result{hit, point, normal, distance, collider{path, instance_id, ...}}`
**All hits response:** `results[{point, normal, distance, collider{...}}]`

### linecast
Line segment intersection test.

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `from` | `[x,y,z]` | | Start point |
| `to` | `[x,y,z]` | | End point |
| `layer_mask` | int | | Physics layer mask |
| `physics` | string | _(auto)_ | `"3d"` or `"2d"` override |

**Response:** `result{blocked, from, to, distance, hit_point, hit_normal, hit_distance, collider{...}}`

### path_distance
NavMesh path distance between two points.

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `from` | `[x,y,z]` | | Start point |
| `to` | `[x,y,z]` | | End point |
| `agent_type_id` | int | | NavMesh agent type |

**Response:** `result{from, to, straight_distance, path_found, path_distance, path_status, waypoints[[x,y,z],...], waypoint_count}`

### bounds
Get bounding box of an object.

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `path` | string | | Hierarchy path |
| `instance_id` | int | | Alternative to path |
| `source` | string | `"combined"` | `"renderer"`, `"collider"`, or `"combined"` |

**Response:** `result{path, instance_id, source, center, size, min, max, extents}`

---

## watch

Property watches with SSE notifications. Max 20 concurrent.

### create

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `target` | string | | Hierarchy path or `"*"` for global |
| `track` | string[] | | Properties to track |
| `condition` | object | | Trigger condition (see below) |
| `throttle_ms` | int | `500` | Min interval between notifications |
| `label` | string | | Human-readable label |

**Condition types:**
- `threshold`: `{type, property, below?, above?}`
- `proximity`: `{type, target, within?, beyond?}`
- `entered_region`: `{type, min: [x,y,z], max: [x,y,z]}`
- `property_changed`: `{type, property}`
- `destroyed`: `{type}`
- `spawned`: `{type, name_pattern}`

### remove
`{watch_id: "w_01"}`

### list
No params. Returns all active watches.

### check
`{watch_id: "w_01"}` — poll current values and trigger state.

**SSE notification format:**
```json
{"jsonrpc":"2.0","method":"notifications/theatre/watch_triggered",
 "params":{"watch_id":"w_01","frame":5200,"trigger":{...}}}
```

---

## action

Runtime mutations and play control. 8 operations.

### teleport
| Param | Type | Description |
|-------|------|-------------|
| `path` / `instance_id` | string / int | Target object |
| `position` | `[x,y,z]` | World position |
| `rotation_euler` | `[x,y,z]` | Euler angles (degrees, optional) |

### set_property
| Param | Type | Description |
|-------|------|-------------|
| `path` / `instance_id` | string / int | Target object |
| `component` | string | Component type |
| `property` | string | Property name |
| `value` | any | New value |

### set_active
| Param | Type | Description |
|-------|------|-------------|
| `path` / `instance_id` | string / int | Target object |
| `active` | bool | Enable/disable |

### set_timescale (play mode only)
| Param | Type | Description |
|-------|------|-------------|
| `timescale` | float | `Time.timeScale` (0.0-100.0) |

### pause / step / unpause (play mode only)
No additional params. `step` advances one frame.

### invoke_method (play mode only)
| Param | Type | Description |
|-------|------|-------------|
| `path` / `instance_id` | string / int | Target object |
| `component` | string | Component type |
| `method` | string | Method name |
| `arguments` | any[] | Max 3 args, simple types only |

---

## recording

Frame-by-frame dashcam. `start` requires play mode.

### start (play mode only)
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `label` | string | | Clip name |
| `track_paths` | string[] | | Glob patterns for objects |
| `track_components` | string[] | | Component types to capture |
| `capture_rate` | int | `60` | FPS (1-120) |

### stop
No params. Returns `clip_id`, duration, frame count.

### marker
| Param | Type | Description |
|-------|------|-------------|
| `label` | string | Marker name |
| `metadata` | object | Arbitrary metadata |

### list_clips
No params. Returns all recorded clips.

### delete_clip
`{clip_id: "..."}`

### query_range
| Param | Type | Description |
|-------|------|-------------|
| `clip_id` | string | Clip to query |
| `from_frame` | int | Start frame |
| `to_frame` | int | End frame |
| `paths` | string[] | Filter by object paths |
| `properties` | string[] | Filter by property names |
| `budget` | int | Token budget (default 1500) |

### diff_frames
| Param | Type | Description |
|-------|------|-------------|
| `clip_id` | string | Clip to query |
| `frame_a` | int | First frame |
| `frame_b` | int | Second frame |

### clip_info
`{clip_id: "..."}` — metadata, markers, duration, track info.

### analyze
| Param | Type | Description |
|-------|------|-------------|
| `clip_id` | string | Clip to analyze |
| `query` | string | `"threshold"`, `"min"`, `"max"`, `"count_changes"` |
| `path` | string | Target object path |
| `property` | string | Property name |
| `below` / `above` | float | For threshold queries |

---

## theatre_status

No parameters. Returns: `status`, `version`, `port`, `play_mode`, `active_scene`, `enabled_groups`, `tool_count`.

---

## unity_console

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `operation` | string | `"query"` | `"query"`, `"summary"`, `"clear"`, `"refresh"` |
| `count` | int | `50` | Max entries (max 200). Query only. |
| `filter` | string | `"all"` | `"error"`, `"warning"`, `"log"`, `"exception"`, `"all"` |
| `grep` | string | | Substring match. Prefix `regex:` for regex. |

`refresh` triggers `AssetDatabase.Refresh()` (recompile). Server restarts after domain reload.

---

## unity_tests

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `operation` | string | `"results"` | `"run"`, `"results"`, `"list"` |
| `mode` | string | `"editmode"` | `"editmode"`, `"playmode"`, `"both"` |
| `filter` | string | | Substring match on test name |
| `failures_only` | bool | `true` | Only show failed tests |

`run` is async — call `results` after to poll for completion.
