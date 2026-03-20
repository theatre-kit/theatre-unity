# Unity Theatre — Contracts

Wire format and MCP response conventions for Unity Theatre. These rules
govern every tool response, parameter struct, and notification payload.

---

## Field Naming: Unity-Aligned

All field names follow Unity's property naming conventions, translated to
snake_case for JSON.

### Transform Properties

| Unity Property | JSON Field | Notes |
|---|---|---|
| `transform.position` | `position` | World space Vector3 |
| `transform.localPosition` | `local_position` | Parent-relative |
| `transform.eulerAngles` | `euler_angles` | World rotation in degrees |
| `transform.localEulerAngles` | `local_euler_angles` | Local rotation in degrees |
| `transform.rotation` | `rotation` | Quaternion `[x, y, z, w]` |
| `transform.localRotation` | `local_rotation` | Local quaternion |
| `transform.localScale` | `local_scale` | Scale vector |
| `transform.lossyScale` | `lossy_scale` | World-space approximate scale |

### GameObject Properties

| Unity Property | JSON Field |
|---|---|
| `gameObject.name` | `name` |
| `gameObject.tag` | `tag` |
| `gameObject.layer` | `layer` (layer name, not int) |
| `gameObject.activeSelf` | `active_self` |
| `gameObject.activeInHierarchy` | `active_hierarchy` |
| `gameObject.isStatic` | `static_flags` (array of flag names) |
| `gameObject.scene.name` | `scene` |

### Common Component Properties

| Unity Property | JSON Field |
|---|---|
| `Rigidbody.velocity` | `velocity` |
| `Rigidbody.angularVelocity` | `angular_velocity` |
| `Rigidbody.mass` | `mass` |
| `Rigidbody.isKinematic` | `is_kinematic` |
| `Collider.isTrigger` | `is_trigger` |
| `Renderer.enabled` | `enabled` |
| `Renderer.bounds` | `bounds` |
| `CharacterController.isGrounded` | `is_grounded` |
| `Animator.speed` | `speed` |

### Naming Rules

1. **Unity property → snake_case**: `localPosition` → `local_position`,
   `isGrounded` → `is_grounded`, `activeSelf` → `active_self`

2. **Boolean `is` prefix**: Preserve the `is_` prefix when Unity uses it.
   `isKinematic` → `is_kinematic`, `isTrigger` → `is_trigger`.
   When Unity uses bare booleans, keep them bare: `enabled` → `enabled`.

3. **No abbreviations**: `position` not `pos`, `rotation` not `rot`,
   `distance` not `dist`, `velocity` not `vel`. Full words always.

4. **Layer as name not int**: Unity internally uses layer indices, but
   responses use the human-readable layer name. Inputs accept either.

5. **Enum values as snake_case strings**: `ForceMode.Impulse` →
   `"impulse"`, `CollisionDetectionMode.Continuous` → `"continuous"`.

---

## ID Fields

### `<resource>_id` Convention

Every resource identifier uses `<resource>_id`, never bare `"id"`.

```
watch_id       ✓    id      ✗
clip_id        ✓    id      ✗
session_id     ✓    id      ✗
profile_id     ✓    id      ✗
```

### `instance_id` for GameObjects

Unity's `Object.GetInstanceID()` is always `instance_id`:

```json
{
  "path": "/Player",
  "instance_id": 10240
}
```

Both fields appear together on every object reference. This is mandatory
on all responses that reference a GameObject — no exceptions.

### `entity` for ECS

ECS entities use a structured identifier:

```json
{
  "entity": { "index": 42, "version": 3 },
  "world": "Default World"
}
```

The `entity` object is always present on ECS responses.

---

## Response Envelopes

### Singular vs Plural

| Query semantics | Field | Type |
|---|---|---|
| Returns a ranked/filtered list | `results` | Array |
| Returns one answer | `result` | Object or string |

**`results`** (plural): `nearest`, `radius`, `overlap`, `find`, `search`,
`list`, `list_clips`, `list_states`, `list_tracks`

**`result`** (singular): `raycast`, `linecast`, `path_distance`, `bounds`,
`create_*`, `delete_*`, `set_*`

### Frame Reference

All Stage responses include frame context:

```json
{
  "frame": 4580,
  "time": 76.33,
  "play_mode": true,
  ...
}
```

Director responses in Edit Mode omit `frame` and `time`.

### Director Result Envelope

All Director mutations return:

```json
{
  "result": "ok",
  "operation": "create_gameobject",
  "<resource>_id_or_path": "...",
  "details": { ... }
}
```

The specific echo fields depend on the operation (path for hierarchy ops,
asset_path for asset ops, watch_id for watch ops, etc.).

### Echo Convention

Response fields echo input parameter names exactly:

```
Input:  { "path": "/Player", "component": "Health", "property": "current_hp" }
Output: { "result": "ok", "path": "/Player", "component": "Health", "property": "current_hp", "value": 100 }
```

An agent can read a response using the same field names it used in the request.

---

## Error Shape

All errors follow this structure:

```json
{
  "error": {
    "code": "gameobject_not_found",
    "message": "GameObject at path '/Enemies/Scout_99' does not exist",
    "suggestion": "Use scene_hierarchy with operation 'find' to search for matching objects"
  }
}
```

### Error Codes

| Code | Meaning |
|---|---|
| `gameobject_not_found` | Path or instance_id doesn't resolve |
| `component_not_found` | Named component doesn't exist on target |
| `type_not_found` | Component or SO type doesn't exist in project |
| `type_ambiguous` | Multiple types match the name (lists candidates) |
| `property_not_found` | Serialized property doesn't exist on component |
| `invalid_asset_path` | Path doesn't start with Assets/ or has wrong extension |
| `asset_exists` | Asset already exists at path (and overwrite not specified) |
| `asset_not_found` | No asset at specified path |
| `prefab_not_found` | Path doesn't point to a valid prefab |
| `not_prefab_instance` | Target is not a prefab instance |
| `scene_not_loaded` | Referenced scene is not loaded |
| `requires_play_mode` | Operation needs Play Mode (physics queries, frame step) |
| `requires_edit_mode` | Operation needs Edit Mode (asset creation) |
| `entity_not_found` | Entity index+version doesn't resolve |
| `world_not_found` | Named World doesn't exist |
| `package_not_installed` | Required package missing (e.g., ProBuilder, Entities) |
| `invalid_parameter` | Parameter value out of range or wrong type |
| `budget_exceeded` | Response would exceed hard cap |
| `watch_limit_reached` | Maximum concurrent watches |
| `recording_in_progress` | Cannot start a recording while one is active |
| `no_active_recording` | Stop/marker called with no recording running |
| `invalid_cursor` | Pagination cursor expired or invalid |
| `dry_run` | Not an error — dry run result (returned as result, not error) |

### Suggestions

Every error includes a `suggestion` field with actionable guidance. These
are written for AI agents, not humans — they reference specific Theatre
tool names and operations.

```json
{
  "code": "component_not_found",
  "message": "Component 'HealthSystem' not found on '/Player'",
  "suggestion": "Use scene_inspect to list all components on this GameObject. The component may be named 'Health' or 'PlayerHealth'."
}
```

---

## Pagination

Large result sets return paginated:

```json
{
  "results": [...],
  "pagination": {
    "cursor": "eyJvZmZzZXQiOjUwfQ==",
    "has_more": true,
    "returned": 50,
    "total": 247
  }
}
```

- `cursor` is opaque — agents pass it back unchanged
- Cursors expire after 60 seconds or on scene change
- `total` is exact when known, omitted when expensive to compute
- First page is returned without a cursor parameter

To fetch next page, pass `cursor` as a top-level parameter to the same tool.

---

## Token Budgeting

Stage tools that return variable-length data accept a `budget` parameter
(target response size in estimated tokens).

### Budget Behavior

1. Server estimates response size as it builds the result
2. If approaching budget, it reduces detail:
   - Full properties → summary (type + key values only)
   - All children → child count only
   - Full component list → component type names only
3. If still over, it truncates with pagination
4. Response includes budget metadata:

```json
{
  "budget": {
    "requested": 1500,
    "used": 1340,
    "truncated": false
  }
}
```

### Hard Cap

Responses never exceed 4000 tokens regardless of requested budget. This
prevents runaway responses from consuming agent context. If a single object
inspection would exceed the hard cap (e.g., an object with hundreds of
components), the response is truncated with:

```json
{
  "budget": {
    "requested": 4000,
    "used": 4000,
    "truncated": true,
    "truncation_reason": "hard_cap",
    "suggestion": "Use 'components' filter to inspect specific components"
  }
}
```

---

## Vector Encoding

All vectors are encoded as JSON arrays:

| Unity Type | JSON | Example |
|---|---|---|
| `Vector2` | `[x, y]` | `[1.5, 3.0]` |
| `Vector3` | `[x, y, z]` | `[0, 1.05, -3.2]` |
| `Vector4` | `[x, y, z, w]` | `[0, 0, 0, 1]` |
| `Quaternion` | `[x, y, z, w]` | `[0, 0.707, 0, 0.707]` |
| `Color` | `[r, g, b, a]` | `[1, 0, 0, 1]` |
| `Rect` | `[x, y, width, height]` | `[0, 0, 1920, 1080]` |
| `Bounds` | `{ "center": [...], "size": [...] }` | Object, not array |
| `BoundsInt` | `{ "position": [...], "size": [...] }` | Object |

Floats are rounded to 2 decimal places by default. Configurable via
`spatial_config` or project settings.

---

## Notification Format

Server-initiated notifications (watch triggers, recording events) use
MCP's notification mechanism over the SSE stream:

```json
{
  "jsonrpc": "2.0",
  "method": "notifications/theatre/<event_type>",
  "params": { ... }
}
```

### Event Types

| Method | When |
|---|---|
| `notifications/theatre/watch_triggered` | A watch condition is met |
| `notifications/theatre/recording_started` | Recording begins |
| `notifications/theatre/recording_stopped` | Recording ends |
| `notifications/theatre/recording_marker` | Marker placed |
| `notifications/theatre/play_mode_changed` | Play/Edit mode transition |
| `notifications/theatre/scene_changed` | Active scene changed |
| `notifications/theatre/tool_groups_changed` | Tool visibility changed (echoes tools/list_changed) |
