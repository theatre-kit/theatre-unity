---
name: theatre-unity
description: >
  Theatre for Unity MCP server tools. Gives AI agents spatial awareness (Stage)
  and programmatic control (Director) over Unity scenes via Streamable HTTP.
  Use when connected to a Theatre MCP server, working with Unity GameObjects,
  scene hierarchy, spatial queries, animation, materials, prefabs, tilemaps,
  terrain, or any Unity Editor automation through MCP tool calls.
---

# Theatre for Unity

MCP server running inside the Unity Editor. 42 tools, ~160 operations.
Streamable HTTP on `localhost:9078/mcp`. No sidecar process.

## When to use this skill

- You have `theatre` in your `.mcp.json` (Streamable HTTP, not stdio)
- You're calling any `scene_*`, `spatial_query`, `watch`, `action`, `recording`,
  `*_op`, `theatre_status`, `unity_console`, or `unity_tests` tool
- You need to observe, query, or mutate a Unity scene programmatically

## Connection

```json
{
  "mcpServers": {
    "theatre": {
      "type": "http",
      "url": "http://localhost:9078/mcp"
    }
  }
}
```

Server auto-starts when Unity opens. Check with `theatre_status`.

## Tool Map

### Infrastructure

| Tool | Purpose |
|------|---------|
| `theatre_status` | Server health, play mode, active scene, enabled groups |
| `unity_console` | Read/filter/grep Unity console. Ops: `query`, `summary`, `clear`, `refresh` |
| `unity_tests` | Run/list EditMode & PlayMode tests. Ops: `run`, `results`, `list` |

### Stage (Observation)

| Tool | Operations | Purpose |
|------|-----------|---------|
| `scene_snapshot` | _(none)_ | Budgeted spatial overview of all GameObjects |
| `scene_hierarchy` | `list`, `find`, `search`, `path` | Navigate/filter the transform tree |
| `scene_inspect` | _(none)_ | Deep-inspect a GameObject + all components |
| `scene_delta` | _(none)_ | Detect changes since last snapshot/delta |
| `spatial_query` | `nearest`, `radius`, `overlap`, `raycast`, `linecast`, `path_distance`, `bounds` | Spatial queries (transform + physics) |
| `watch` | `create`, `remove`, `list`, `check` | Property watches with SSE push |
| `action` | `teleport`, `set_property`, `set_active`, `set_timescale`, `pause`, `step`, `unpause`, `invoke_method`, `run_menu_item` | Mutations, play control, menu items, static methods |
| `recording` | `start`, `stop`, `marker`, `list_clips`, `delete_clip`, `query_range`, `diff_frames`, `clip_info`, `analyze` | Frame-by-frame dashcam to SQLite |

### Director (Mutation)

| Tool | Operations | Purpose |
|------|-----------|---------|
| `scene_op` | `create_scene`, `load_scene`, `unload_scene`, `create_gameobject`, `delete_gameobject`, `reparent`, `duplicate`, `set_component`, `remove_component`, `move_to_scene` | Scene & GameObject CRUD. `create_gameobject` supports `primitive_type` (cube/sphere/etc.) |
| `prefab_op` | `create_prefab`, `instantiate`, `apply_overrides`, `revert_overrides`, `unpack`, `create_variant`, `list_overrides` | Prefab lifecycle |
| `batch` | _(atomic)_ | Execute 1-50 tool calls as one undo group |
| `material_op` | `create`, `set_properties`, `set_shader`, `list_properties` | Materials & shaders |
| `scriptable_object_op` | `create`, `set_fields`, `list_fields`, `find_by_type` | ScriptableObject CRUD |
| `physics_material_op` | `create`, `set_properties` | 3D/2D physics materials |
| `texture_op` | `import`, `set_import_settings`, `create_sprite`, `sprite_sheet` | Texture import & sprites |
| `sprite_atlas_op` | `create`, `add_entries`, `remove_entries`, `pack` | Sprite atlas packing |
| `audio_mixer_op` | `create`, `add_group`, `set_volume`, `add_effect`, `create_snapshot`, `expose_parameter` | Audio mixer |
| `animation_clip_op` | `create`, `add_curve`, `remove_curve`, `set_keyframe`, `set_events`, `set_loop`, `list_curves` | Animation clips |
| `animator_controller_op` | `create`, `add_parameter`, `add_state`, `set_state_clip`, `add_transition`, `set_transition_conditions`, `set_default_state`, `add_layer`, `list_states` | Animator state machines |
| `blend_tree_op` | `create`, `add_motion`, `set_blend_type`, `set_parameter`, `set_thresholds` | Blend trees |
| `tilemap_op` | `set_tile`, `set_tiles`, `box_fill`, `flood_fill`, `clear`, `get_tile`, `get_used_tiles`, `create_rule_tile`, `set_tilemap_layer` | 2D tilemap painting |
| `terrain_op` | `create`, `set_heightmap`, `smooth_heightmap`, `paint_texture`, `add_terrain_layer`, `place_trees`, `place_details`, `set_size`, `get_height` | Terrain sculpting |
| `navmesh_op` | `bake`, `set_area`, `add_modifier`, `add_link`, `set_agent_type`, `add_surface` | NavMesh |
| `lighting_op` | `set_ambient`, `set_fog`, `set_skybox`, `add_light_probe_group`, `add_reflection_probe`, `bake` | Scene lighting |
| `quality_op` | `set_level`, `set_shadow_settings`, `set_rendering`, `list_levels` | Quality settings |
| `project_settings_op` | `set_physics`, `set_time`, `set_player`, `set_tags_and_layers` | Project-wide settings |
| `build_profile_op` | `create`, `set_scenes`, `set_platform`, `set_scripting_backend`, `list_profiles` | Build profiles |

### Conditional tools (require optional packages)

| Tool | Requires |
|------|----------|
| `timeline_op` | `com.unity.timeline` |
| `probuilder_op` | `com.unity.probuilder` |
| `input_action_op` | `com.unity.inputsystem` |
| `render_pipeline_op` | URP or HDRP package |
| `addressable_op` | `com.unity.addressables` |
| `ecs_world`, `ecs_snapshot`, `ecs_inspect`, `ecs_query`, `ecs_action` | `com.unity.entities` |

## Key Workflows

### Explore a scene
```
1. scene_snapshot                          -- spatial overview
2. scene_hierarchy {operation: "list"}     -- root objects per scene
3. scene_hierarchy {operation: "find", pattern: "Enemy*"}  -- glob search
4. scene_inspect {path: "/Enemy/Scout"}    -- full component details
```

### Build something
```
1. scene_op {operation: "create_gameobject", name: "Platform", primitive_type: "cube", position: [0,1,0]}
2. material_op {operation: "create", asset_path: "Assets/Materials/Platform.mat", shader: "Standard", properties: {_Color: [0.2,0.4,0.8,1]}}
3. scene_op {operation: "set_component", path: "/Platform", component: "MeshRenderer", properties: {material: "Assets/Materials/Platform.mat"}}
4. prefab_op {operation: "create_prefab", source_path: "/Platform", asset_path: "Assets/Prefabs/Platform.prefab"}
```
`primitive_type` creates a visible mesh (cube/sphere/capsule/cylinder/plane/quad) with MeshFilter, MeshRenderer, and Collider.
ObjectReference properties (materials, meshes, prefab refs) accept asset paths as strings.

### Run editor menu items & static methods
```
1. action {operation: "run_menu_item", menu_path: "GameObject/3D Object/Cube"}
2. action {operation: "invoke_method", type: "MyEditorSetup", method: "Initialize"}
```
`run_menu_item` works in Edit Mode. `invoke_method` with `type` (instead of `component`) calls static methods without Play Mode.

### Batch (atomic, one undo)
```json
{"operations": [
  {"tool": "scene_op", "params": {"operation": "create_gameobject", "name": "Enemy", "position": [5,0,0]}},
  {"tool": "scene_op", "params": {"operation": "set_component", "path": "/Enemy", "component": "Rigidbody"}}
], "dry_run": false}
```
Rolls back all on failure. Max 50 ops. Use `dry_run: true` to validate first.

### Debug at runtime (play mode)
```
1. action {operation: "pause"}
2. scene_inspect {path: "/Player"}         -- see current state
3. action {operation: "set_property", path: "/Player", component: "Health", property: "currentHp", value: 100}
4. action {operation: "step"}              -- advance one frame
5. scene_delta                             -- see what changed
6. action {operation: "unpause"}
```

### Watch a property (SSE push)
```
watch {operation: "create", target: "/Player", track: ["position", "currentHp"],
       condition: {type: "threshold", property: "currentHp", below: 25},
       label: "Player low health"}
```
Max 20 concurrent watches. Throttle with `throttle_ms` (default 500).

### Record gameplay
```
1. recording {operation: "start", label: "combat_test", track_paths: ["/Enemy/*"], track_components: ["Health", "Transform"]}
2. [play the game]
3. recording {operation: "stop"}
4. recording {operation: "query_range", clip_id: "...", from_frame: 100, to_frame: 200}
5. recording {operation: "analyze", clip_id: "...", query: "threshold", path: "/Enemy/Scout", property: "currentHp", below: 0}
```

## Gotchas

### Play mode requirements
Only these operations require play mode:
- `action`: `pause`, `step`, `unpause`, `set_timescale`, `invoke_method` (instance methods only)
- `recording`: `start`

These work in Edit Mode:
- `action`: `invoke_method` with `type` param (static methods), `run_menu_item`, `teleport`, `set_property`, `set_active`
- All Director operations, all Stage read operations, spatial queries

### Token budget
Most read tools accept a `budget` parameter (default 1500, max 4000). This controls response size in estimated tokens. If a response is truncated, the `budget` object in the response tells you:
```json
{"budget": {"used": 1480, "total": 1500, "truncated": true, "reason": "budget", "suggestion": "Increase budget or narrow with filters"}}
```
Use `include_components`, `radius`, or `paths` filters to get more targeted results.

### GameObject addressing
Always use **hierarchy paths** (`/Parent/Child`) or **instance_id** (int). Both appear in every response. Multi-scene paths: `SceneName:/Path`. Duplicate names get `(1)`, `(2)` suffixes.

### Property names (set_property / set_component)
Theatre uses Unity's **serialized field names** in snake_case — NOT the C# API property names.

| You might try | Actual property name | Why |
|---|---|---|
| `shared_material` | `materials` | `sharedMaterial` is a C# accessor, not a serialized field. The field is `m_Materials` → `materials` |
| `is_trigger` | `is_trigger` | This one works — `m_IsTrigger` → `is_trigger` |
| `velocity` | _(none)_ | `Rigidbody.velocity` is a runtime-only C# property, not serialized |
| `mesh` | `mesh` | MeshFilter's `m_Mesh` → `mesh` |

**When in doubt**: use `scene_inspect` with a component filter to see the actual property names. Or just try — if a property isn't found, the error lists all available properties sorted by relevance to what you typed.

**ObjectReference values** (materials, meshes, prefab refs): pass the asset path as a string.
```
set_component: {component: "MeshRenderer", properties: {materials: "Assets/Materials/Foo.mat"}}
set_property:  {component: "MeshFilter", property: "mesh", value: "Assets/Models/Cube.fbx::Cube"}
```
Single value sets element [0] of array properties. Pass a JSON array to set multiple: `{materials: ["Assets/Mat1.mat", "Assets/Mat2.mat"]}`. Pass `null` to clear.

### Wire format
- All field names: `snake_case` (Unity's `localPosition` becomes `local_position`)
- Vectors: arrays `[x, y, z]`
- Colors: arrays `[r, g, b, a]`
- IDs: always `<resource>_id` (e.g., `watch_id`, `clip_id`), never bare `id`
- Errors: `{"error": {"code": "...", "message": "...", "suggestion": "..."}}`

### 2D vs 3D physics
`spatial_query` auto-detects 2D vs 3D from scene contents. Override per query with `"physics": "2d"` or `"physics": "3d"`.

### Undo support
All Director mutations register with Unity's Undo system. Ctrl+Z in the editor reverses them. Batch operations share one undo group.

### Domain reloads
After code changes, Unity reloads the domain. The server restarts in ~2-3 seconds. Watches persist via SessionState. SSE clients should reconnect.

## References

- [references/stage-tools.md](references/stage-tools.md) — Full Stage tool parameter reference (snapshot, hierarchy, inspect, delta, spatial, watch, action, recording)
- [references/director-tools.md](references/director-tools.md) — Full Director tool parameter reference (scene_op, prefab_op, batch, materials, animation, spatial building, config)
