# Director Tools — Parameter Reference

Complete parameter reference for Theatre's mutation tools. All Director tools support Undo
and use `snake_case` field names. Responses include `result: "ok"` on success.

## Table of Contents

- [scene_op](#scene_op)
- [prefab_op](#prefab_op)
- [batch](#batch)
- [material_op](#material_op)
- [scriptable_object_op](#scriptable_object_op)
- [physics_material_op](#physics_material_op)
- [texture_op](#texture_op)
- [sprite_atlas_op](#sprite_atlas_op)
- [audio_mixer_op](#audio_mixer_op)
- [animation_clip_op](#animation_clip_op)
- [animator_controller_op](#animator_controller_op)
- [blend_tree_op](#blend_tree_op)
- [timeline_op](#timeline_op)
- [tilemap_op](#tilemap_op)
- [navmesh_op](#navmesh_op)
- [terrain_op](#terrain_op)
- [probuilder_op](#probuilder_op)
- [input_action_op](#input_action_op)
- [lighting_op](#lighting_op)
- [quality_op](#quality_op)
- [project_settings_op](#project_settings_op)
- [build_profile_op](#build_profile_op)

---

## scene_op

Scene lifecycle and GameObject CRUD. 10 operations.

### create_scene
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `path` | string | | Scene asset path (e.g., `"Assets/Scenes/Level.unity"`) |
| `template` | string | `"empty"` | `"empty"`, `"basic_2d"`, `"basic_3d"` |

### load_scene
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `path` | string | | Scene asset path |
| `mode` | string | `"single"` | `"single"` or `"additive"` |

### unload_scene
| Param | Type | Description |
|-------|------|-------------|
| `path` | string | Scene name or asset path |

### create_gameobject
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `name` | string | | GameObject name |
| `parent` | string | | Parent hierarchy path |
| `position` | `[x,y,z]` | `[0,0,0]` | Local position |
| `rotation_euler` | `[x,y,z]` | `[0,0,0]` | Local rotation (degrees) |
| `scale` | `[x,y,z]` | `[1,1,1]` | Local scale |
| `tag` | string | | Tag name |
| `layer` | string | | Layer name |
| `components` | array | | `[{type: "BoxCollider", properties: {size: [1,1,1]}}]` |

### delete_gameobject
| Param | Type | Description |
|-------|------|-------------|
| `path` / `instance_id` | string / int | Target object |

### reparent
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `path` / `instance_id` | string / int | | Object to move |
| `new_parent` | string | | New parent path (empty string = root) |
| `world_position_stays` | bool | `true` | Preserve world position |
| `sibling_index` | int | | Child order index |

### duplicate
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `path` / `instance_id` | string / int | | Object to duplicate |
| `count` | int | `1` | Number of copies |
| `offset` | `[x,y,z]` | `[0,0,0]` | Position offset per copy |

### set_component
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `path` / `instance_id` | string / int | | Target object |
| `component` | string | | Component type name |
| `properties` | object | | Property name-value pairs |
| `add_if_missing` | bool | `true` | Add component if not present |

### remove_component
| Param | Type | Description |
|-------|------|-------------|
| `path` / `instance_id` | string / int | Target object |
| `component` | string | Component type (cannot remove Transform) |

### move_to_scene
| Param | Type | Description |
|-------|------|-------------|
| `path` / `instance_id` | string / int | Root object to move |
| `target_scene` | string | Target scene name |

---

## prefab_op

Prefab lifecycle. 7 operations.

### create_prefab
| Param | Type | Description |
|-------|------|-------------|
| `source_path` | string | Scene hierarchy path of source GameObject |
| `asset_path` | string | Output `.prefab` path |

### instantiate
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `prefab_path` | string | | `.prefab` asset path |
| `parent` | string | | Parent hierarchy path |
| `position` | `[x,y,z]` | `[0,0,0]` | Position |
| `rotation_euler` | `[x,y,z]` | `[0,0,0]` | Rotation |
| `name` | string | | Override name |

### apply_overrides
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `instance_path` | string | | Hierarchy path to prefab instance |
| `scope` | string | `"all"` | Override scope |

### revert_overrides
| Param | Type | Description |
|-------|------|-------------|
| `instance_path` | string | Hierarchy path to prefab instance |

### unpack
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `instance_path` | string | | Hierarchy path |
| `mode` | string | `"outermost"` | `"outermost"` or `"completely"` |

### create_variant
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `base_prefab` | string | | Base `.prefab` path |
| `asset_path` | string | | Output `.prefab` path |
| `overrides` | array | | `[{type: "Transform", properties: {position: [1,0,0]}}]` |

### list_overrides
| Param | Type | Description |
|-------|------|-------------|
| `instance_path` | string | Hierarchy path to prefab instance |

---

## batch

Atomic execution of multiple tool operations.

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `operations` | array | | 1-50 operations: `[{tool: "scene_op", params: {...}}]` |
| `dry_run` | bool | `false` | Validate without executing |

- All ops share one undo group (one Ctrl+Z to undo all)
- First failure rolls back all preceding mutations
- Single `AssetDatabase` import pass at end (efficient)
- Inner tools must be in enabled tool groups

**Success:** `{result: "ok", operation_count, results: [{index, tool, result}]}`
**Failure:** `{result: "error", failed_index, error: {code, message, suggestion}, completed_before_failure: [...]}`

---

## material_op

### create
| Param | Type | Description |
|-------|------|-------------|
| `asset_path` | string | `.mat` path |
| `shader` | string | Shader name (e.g., `"Standard"`, `"Universal Render Pipeline/Lit"`) |
| `properties` | object | Initial property values |

### set_properties
| Param | Type | Description |
|-------|------|-------------|
| `asset_path` | string | `.mat` path |
| `properties` | object | Property values (see value types below) |

**Property value types:**
- Number → `SetFloat`
- `[r,g,b,a]` array → `SetColor`
- `[x,y,...]` array → `SetVector`
- String (`"Assets/..."`) → `SetTexture`

### set_shader
| Param | Type | Description |
|-------|------|-------------|
| `asset_path` | string | `.mat` path |
| `shader` | string | New shader name |

### list_properties
| Param | Type | Description |
|-------|------|-------------|
| `asset_path` | string | `.mat` path |

Returns: `properties[{name, type, value}]`

---

## scriptable_object_op

### create
| Param | Type | Description |
|-------|------|-------------|
| `type` | string | ScriptableObject type name |
| `asset_path` | string | `.asset` path |

### set_fields
| Param | Type | Description |
|-------|------|-------------|
| `asset_path` | string | `.asset` path |
| `fields` | object | Field name-value pairs |

### list_fields
`{asset_path: "..."}` — returns `fields[{name, type, value}]`

### find_by_type
`{type: "..."}` — returns `assets[{asset_path, name}]`

---

## physics_material_op

### create / set_properties
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `asset_path` | string | | `.physicMaterial` or `.physicsMaterial2D` path |
| `physics` | string | `"3d"` | `"3d"` or `"2d"` |
| `friction` | float | | Dynamic friction |
| `static_friction` | float | | Static friction (3D only) |
| `bounciness` | float | | Bounce factor |
| `friction_combine` | string | | `"average"`, `"minimum"`, `"maximum"`, `"multiply"` (3D) |
| `bounce_combine` | string | | Same options (3D) |

---

## texture_op

### import
`{asset_path: "Assets/Textures/img.png"}` — verify/import texture.

### set_import_settings
| Param | Type | Description |
|-------|------|-------------|
| `asset_path` | string | Texture asset path |
| `settings` | object | Keys: `texture_type`, `filter_mode`, `wrap_mode`, `max_size`, `compression`, `srgb`, `read_write`, `generate_mipmaps`, `pixels_per_unit` |

Texture types: `"default"`, `"sprite"`, `"normal_map"`, `"editor_gui"`, `"lightmap"`
Filter modes: `"point"`, `"bilinear"`, `"trilinear"`
Compression: `"none"`, `"low"`, `"normal"`, `"high"`

### create_sprite
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `asset_path` | string | | Texture path |
| `pixels_per_unit` | float | `100` | PPU |
| `pivot` | `[x,y]` | `[0.5, 0.5]` | Sprite pivot |

### sprite_sheet
| Param | Type | Description |
|-------|------|-------------|
| `asset_path` | string | Texture path |
| `mode` | string | `"grid"` or `"manual"` |
| `cell_size` | `[w,h]` | Grid cell size (grid mode) |
| `offset` | `[x,y]` | Grid offset |
| `padding` | `[x,y]` | Grid padding |
| `sprites` | array | Manual: `[{name, rect: [x,y,w,h], pivot: [x,y]}]` |

---

## sprite_atlas_op

| Op | Key Params |
|----|-----------|
| `create` | `asset_path` (.spriteatlas), `include_in_build`, `packing_settings{padding, enable_rotation, enable_tight_packing}` |
| `add_entries` | `asset_path`, `entries` (asset paths array) |
| `remove_entries` | `asset_path`, `entries` |
| `pack` | `asset_path` |

---

## audio_mixer_op

| Op | Key Params |
|----|-----------|
| `create` | `asset_path` (.mixer) |
| `add_group` | `asset_path`, `name`, `parent_group` (default "Master") |
| `set_volume` | `asset_path`, `group`, `volume` (dB) |
| `add_effect` | `asset_path`, `group`, `effect` (e.g., "SFX Reverb", "Echo") |
| `create_snapshot` | `asset_path`, `name` |
| `expose_parameter` | `asset_path`, `parameter` |

---

## animation_clip_op

| Op | Key Params |
|----|-----------|
| `create` | `asset_path` (.anim), `frame_rate` (60), `wrap_mode`, `legacy` (false) |
| `add_curve` | `clip_path`, `property_name` (e.g., "m_LocalPosition.x"), `type` (component), `relative_path`, `keyframes[{time, value, in_tangent?, out_tangent?}]` |
| `remove_curve` | `clip_path`, `property_name`, `type`, `relative_path` |
| `set_keyframe` | `clip_path`, `property_name`, `type`, `relative_path`, `time`, `value`, `in_tangent?`, `out_tangent?` |
| `set_events` | `clip_path`, `events[{time, function, int_param?, float_param?, string_param?}]` |
| `set_loop` | `clip_path`, `loop_time`, `loop_pose?`, `cycle_offset?` |
| `list_curves` | `clip_path` — returns `curves[{path, property, type, keyframe_count}]` |

---

## animator_controller_op

| Op | Key Params |
|----|-----------|
| `create` | `asset_path` (.controller) |
| `add_parameter` | `asset_path`, `name`, `type` ("float"/"int"/"bool"/"trigger"), `default_value` |
| `add_state` | `asset_path`, `state_name`, `layer` (0), `position` ([x,y]) |
| `set_state_clip` | `asset_path`, `state_name`, `clip_path` (.anim), `layer` |
| `add_transition` | `asset_path`, `source_state`, `destination_state`, `layer`, `has_exit_time` (true), `exit_time` (0.75), `transition_duration` (0.25), `conditions[{parameter, mode, threshold}]` |
| `set_transition_conditions` | `asset_path`, `source_state`, `destination_state`, `layer`, `conditions` |
| `set_default_state` | `asset_path`, `state_name`, `layer` |
| `add_layer` | `asset_path`, `name`, `blend_mode` ("override"/"additive"), `weight` (1.0) |
| `list_states` | `asset_path`, `layer` — returns `states`, `parameters` |

**Condition modes:** `"if"`, `"if_not"`, `"greater"`, `"less"`, `"equals"`, `"not_equals"`

---

## blend_tree_op

| Op | Key Params |
|----|-----------|
| `create` | `controller_path`, `state_name`, `layer`, `blend_type`, `parameter`, `parameter_y` (2D) |
| `add_motion` | `controller_path`, `state_name`, `layer`, `clip_path`, `threshold` (1D), `position` ([x,y] 2D), `time_scale` (1) |
| `set_blend_type` | `controller_path`, `state_name`, `layer`, `blend_type` |
| `set_parameter` | `controller_path`, `state_name`, `layer`, `parameter`, `parameter_y` |
| `set_thresholds` | `controller_path`, `state_name`, `layer`, `thresholds` (float array) |

**Blend types:** `"1d"`, `"2d_simple_directional"`, `"2d_freeform_directional"`, `"2d_freeform_cartesian"`, `"direct"`

---

## timeline_op

Requires `com.unity.timeline`.

| Op | Key Params |
|----|-----------|
| `create` | `asset_path` (.playable), `frame_rate` (60) |
| `add_track` | `asset_path`, `track_type` ("animation"/"activation"/"audio"/"control"/"signal"), `name` |
| `add_clip` | `asset_path`, `track_name`, `clip_asset_path` (.anim), `start`, `duration` |
| `set_clip_properties` | `asset_path`, `track_name`, `clip_index`, `speed`, `blend_in`, `blend_out` |
| `add_marker` | `asset_path`, `time`, `label` |
| `bind_track` | `asset_path`, `track_name`, `object_path` (hierarchy path) |
| `list_tracks` | `asset_path` — returns tracks and clips |

---

## tilemap_op

| Op | Key Params |
|----|-----------|
| `set_tile` | `tilemap_path`, `position` ([x,y]), `tile_asset` |
| `set_tiles` | `tilemap_path`, `positions`, `tile_asset` |
| `box_fill` | `tilemap_path`, `start`, `end`, `tile_asset` |
| `flood_fill` | `tilemap_path`, `position`, `tile_asset` |
| `clear` | `tilemap_path`, `region{start, end}` (optional — omit for full clear) |
| `get_tile` | `tilemap_path`, `position` |
| `get_used_tiles` | `tilemap_path` |
| `create_rule_tile` | `asset_path` (.asset), `default_sprite` |
| `set_tilemap_layer` | `tilemap_path`, `sorting_layer`, `sorting_order` |

---

## navmesh_op

Some operations require `com.unity.ai.navigation`.

| Op | Key Params |
|----|-----------|
| `bake` | `agent_type_id` (0) |
| `set_area` | `index` (0-31), `name`, `cost` |
| `add_modifier` | `path`, `area`, `ignore_from_build`, `affect_children` |
| `add_link` | `path`, `start` ([x,y,z]), `end` ([x,y,z]), `bidirectional` (true) |
| `set_agent_type` | `agent_type_id`, `name` |
| `add_surface` | `path` |

---

## terrain_op

| Op | Key Params |
|----|-----------|
| `create` | `terrain_path`, `position`, `width`, `height`, `length`, `heightmap_resolution` (powers of 2 + 1: 33, 65, 129, 257, 513) |
| `set_heightmap` | `terrain_path`, `heights` (2D array 0.0-1.0), `region{x, y, width, height}` |
| `smooth_heightmap` | `terrain_path`, `region`, `iterations` (1) |
| `paint_texture` | `terrain_path`, `layer_index`, `positions`, `opacity` |
| `add_terrain_layer` | `terrain_path`, `asset_path` (TerrainLayer .asset) |
| `place_trees` | `terrain_path`, `positions`, asset params |
| `place_details` | `terrain_path`, `positions`, `layer_index` |
| `set_size` | `terrain_path`, `width`, `height`, `length` |
| `get_height` | `terrain_path`, `position` |

---

## probuilder_op

Requires `com.unity.probuilder`.

| Op | Key Params |
|----|-----------|
| `create_shape` | `shape` (cube/cylinder/sphere/plane/stair/arch/door/pipe/cone/torus/prism), `position`, `size` |
| `extrude_faces` | `path`, `faces` (index array), `distance` (1) |
| `set_material` | `path`, `faces`, `material` (asset path) |
| `merge` | `paths` (hierarchy paths array) |
| `boolean_op` | `path_a`, `path_b`, operation |
| `export_mesh` | `path`, `asset_path` (.asset) |

---

## input_action_op

Requires `com.unity.inputsystem`.

| Op | Key Params |
|----|-----------|
| `create_asset` | `asset_path` (.inputactions) |
| `add_action_map` | `asset_path`, `name` |
| `add_action` | `asset_path`, `action_map`, `name`, `type` ("value"/"button"/"pass_through") |
| `add_binding` | `asset_path`, `action_map`, `action`, `path` (e.g., `"<Keyboard>/space"`), `interactions`, `processors` |
| `add_composite` | `asset_path`, `action_map`, `action`, `composite_type` ("2DVector"/"1DAxis"), `bindings{part: path}` |
| `set_control_scheme` | `asset_path`, `name`, `devices` (e.g., `["<Keyboard>", "<Mouse>"]`) |
| `list_actions` | `asset_path` |

---

## lighting_op

| Op | Key Params |
|----|-----------|
| `set_ambient` | `mode` ("color"/"gradient"/"skybox"), `color`, `sky_color`, `equator_color`, `ground_color`, `intensity` |
| `set_fog` | `enabled`, `mode` ("linear"/"exponential"/"exponential_squared"), `color`, `density`, `start_distance`, `end_distance` |
| `set_skybox` | `material` (asset path) |
| `add_light_probe_group` | `path`, `positions` |
| `add_reflection_probe` | `path`, `size` |
| `bake` | _(no params)_ |

---

## quality_op

| Op | Key Params |
|----|-----------|
| `set_level` | `level` (int index or string name) |
| `set_shadow_settings` | `distance`, `resolution` ("low"/"medium"/"high"/"very_high"), `cascades` (0/1/2/4) |
| `set_rendering` | `lod_bias`, `pixel_light_count`, `texture_quality` (0-3), `anisotropic_filtering` ("disable"/"enable"/"force_enable"), `vsync` (0/1/2) |
| `list_levels` | — returns all quality level names and settings |

---

## project_settings_op

| Op | Key Params |
|----|-----------|
| `set_physics` | `gravity` ([x,y,z]), `bounce_threshold`, `default_solver_iterations` |
| `set_time` | `fixed_timestep`, `maximum_timestep`, `time_scale` |
| `set_player` | `company_name`, `product_name`, `version` |
| `set_tags_and_layers` | `add_tags`, `add_sorting_layers`, `add_layers[{index, name}]` |

---

## build_profile_op

| Op | Key Params |
|----|-----------|
| `create` | `name`, `platform` ("windows"/"macos"/"linux"/"android"/"ios"/"webgl") |
| `set_scenes` | `profile_path`, `scenes` (asset paths array in build order) |
| `set_platform` | `profile_path`, `platform` |
| `set_scripting_backend` | `profile_path`, `backend` ("mono"/"il2cpp") |
| `list_profiles` | — returns all build profiles |

---

## Error response shape (all tools)

```json
{
  "error": {
    "code": "gameobject_not_found",
    "message": "GameObject at path '/Missing' does not exist",
    "suggestion": "Use scene_hierarchy with operation 'find' to search"
  }
}
```

Common error codes: `gameobject_not_found`, `invalid_parameter`, `requires_play_mode`,
`component_not_found`, `asset_not_found`, `tool_not_found`, `budget_exceeded`, `watch_limit_reached`.
