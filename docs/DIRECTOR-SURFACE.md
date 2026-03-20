# Unity Theatre — Director Tool Surface

Director gives AI agents programmatic control over Unity subsystems whose
file formats are too fragile for agents to hand-write. Scenes, prefabs,
materials, animations, tilemaps, terrain — anything that involves GUIDs,
binary serialization, or complex internal references.

All Director operations run through Unity's Undo system. Every mutation is
reversible by the human developer.

---

## Design Principle: Don't Duplicate What Agents Can Write

Agents can reliably hand-write:
- C# scripts (`.cs`)
- Shaders and HLSL includes (`.shader`, `.hlsl`)
- UXML and USS (UI Toolkit markup and styles)
- Assembly definitions (`.asmdef` — JSON)
- Package manifests (`package.json` — JSON)
- Shader Graph nodes (plain text, though complex)

Director does NOT provide tools for these. If the file format is stable text
that agents can produce with a Write tool, it's out of scope.

Director covers everything else.

---

## Tool Groups

### DirectorScene — Scene & Hierarchy Operations

#### `scene_op`

Create, load, modify, and manage scenes and GameObjects.

**Operations:**

##### `create_scene`

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"create_scene"` |
| `path` | `string` | Asset path: `"Assets/Scenes/Level2.unity"` |
| `template` | `string?` | `"empty"`, `"basic_3d"`, `"basic_2d"` |
| `open` | `bool` | Open in editor after creation (default true) |

##### `load_scene`

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"load_scene"` |
| `path` | `string` | Scene asset path |
| `mode` | `string` | `"single"` or `"additive"` |

##### `unload_scene`

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"unload_scene"` |
| `scene` | `string` | Scene name or path |

##### `create_gameobject`

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"create_gameobject"` |
| `name` | `string` | Object name |
| `parent` | `string?` | Parent path (null = scene root) |
| `position` | `Vector3?` | Local position |
| `rotation_euler` | `Vector3?` | Local rotation in degrees |
| `scale` | `Vector3?` | Local scale |
| `components` | `ComponentSpec[]?` | Components to add with initial values |
| `tag` | `string?` | Tag to assign |
| `layer` | `string?` | Layer name |
| `static_flags` | `string[]?` | Static flags: `["batching", "navigation", "occluder"]` |

**ComponentSpec:**

```json
{
  "type": "BoxCollider",
  "properties": {
    "center": [0, 0.5, 0],
    "size": [1, 1, 1],
    "is_trigger": true
  }
}
```

##### `delete_gameobject`

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"delete_gameobject"` |
| `path` | `string` | Hierarchy path |
| `instance_id` | `int?` | Alternative to path |

##### `reparent`

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"reparent"` |
| `path` | `string` | Object to move |
| `new_parent` | `string?` | New parent path (null = scene root) |
| `sibling_index` | `int?` | Position among siblings |
| `world_position_stays` | `bool` | Preserve world position (default true) |

##### `set_component`

Add or modify components on an existing GameObject.

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"set_component"` |
| `path` | `string` | Target object |
| `component` | `string` | Component type name |
| `properties` | `object` | Property values to set |
| `add_if_missing` | `bool` | Add component if not present (default true) |

##### `remove_component`

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"remove_component"` |
| `path` | `string` | Target object |
| `component` | `string` | Component type to remove |

##### `duplicate`

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"duplicate"` |
| `path` | `string` | Object to duplicate |
| `new_name` | `string?` | Name for the copy |
| `count` | `int` | Number of copies (default 1) |
| `offset` | `Vector3?` | Position offset per copy |

##### `move_to_scene`

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"move_to_scene"` |
| `paths` | `string[]` | Objects to move |
| `target_scene` | `string` | Destination scene name |

---

### DirectorPrefab — Prefab Operations

#### `prefab_op`

Full prefab lifecycle management.

**Operations:**

##### `create_prefab`

Create a prefab from an existing GameObject in the scene.

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"create_prefab"` |
| `source_path` | `string` | Scene hierarchy path of source object |
| `asset_path` | `string` | Save location: `"Assets/Prefabs/Enemy.prefab"` |

##### `instantiate`

Place a prefab instance in the scene.

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"instantiate"` |
| `prefab_path` | `string` | Prefab asset path |
| `parent` | `string?` | Parent in scene hierarchy |
| `position` | `Vector3?` | World position |
| `rotation_euler` | `Vector3?` | World rotation |
| `name` | `string?` | Instance name override |

##### `apply_overrides`

Apply prefab instance overrides back to the prefab asset.

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"apply_overrides"` |
| `instance_path` | `string` | Scene path of prefab instance |
| `scope` | `string` | `"all"`, `"properties"`, `"added_components"`, `"added_objects"` |

##### `revert_overrides`

Revert a prefab instance to match its asset.

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"revert_overrides"` |
| `instance_path` | `string` | Scene path of prefab instance |
| `scope` | `string` | `"all"` or specific override type |

##### `unpack`

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"unpack"` |
| `instance_path` | `string` | Scene path of prefab instance |
| `mode` | `string` | `"outermost"` or `"completely"` |

##### `create_variant`

Create a prefab variant from an existing prefab.

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"create_variant"` |
| `base_prefab` | `string` | Source prefab asset path |
| `asset_path` | `string` | Save location for variant |
| `overrides` | `ComponentSpec[]?` | Initial overrides for the variant |

##### `list_overrides`

Inspect what overrides a prefab instance has.

| Parameter | Type | Description |
|---|---|---|
| `operation` | `string` | `"list_overrides"` |
| `instance_path` | `string` | Scene path of prefab instance |

---

### DirectorAsset — Asset Creation & Management

#### `material_op`

Create and modify materials.

| Operation | Description |
|---|---|
| `create` | Create a new material with a specified shader |
| `set_properties` | Set shader property values (colors, textures, floats) |
| `set_shader` | Change the shader on an existing material |
| `list_properties` | List all shader properties and their current values |

**Example — create material:**

```json
{
  "operation": "create",
  "asset_path": "Assets/Materials/EnemyRed.mat",
  "shader": "Universal Render Pipeline/Lit",
  "properties": {
    "_BaseColor": [1, 0, 0, 1],
    "_Metallic": 0.0,
    "_Smoothness": 0.5
  }
}
```

#### `scriptable_object_op`

Create and modify ScriptableObject instances.

| Operation | Description |
|---|---|
| `create` | Create an instance of a ScriptableObject type |
| `set_fields` | Set serialized field values |
| `list_fields` | List all serialized fields and values |
| `find_by_type` | Find all SO assets of a given type |

**Example — create:**

```json
{
  "operation": "create",
  "type": "WeaponData",
  "asset_path": "Assets/Data/Weapons/Sword.asset",
  "fields": {
    "weapon_name": "Iron Sword",
    "damage": 25,
    "attack_speed": 1.2,
    "icon": "Assets/Sprites/sword_icon.png"
  }
}
```

#### `texture_op`

Import and configure textures.

| Operation | Description |
|---|---|
| `import` | Import an image file and configure its import settings |
| `set_import_settings` | Modify texture import settings (filter mode, wrap, compression, etc.) |
| `create_sprite` | Configure a texture as a sprite with slicing |
| `sprite_sheet` | Set up sprite sheet with grid or manual slicing |

#### `sprite_atlas_op`

| Operation | Description |
|---|---|
| `create` | Create a SpriteAtlas with packing settings |
| `add_entries` | Add folders or individual sprites |
| `remove_entries` | Remove entries |
| `pack` | Trigger atlas packing |

#### `physics_material_op`

| Operation | Description |
|---|---|
| `create` | Create PhysicMaterial (3D) or PhysicsMaterial2D |
| `set_properties` | Set friction, bounciness, combine modes |

#### `audio_mixer_op`

| Operation | Description |
|---|---|
| `create` | Create an AudioMixer asset |
| `add_group` | Add a mixer group with routing |
| `set_volume` | Set group volume |
| `add_effect` | Add an effect (reverb, echo, etc.) to a group |
| `create_snapshot` | Create a mixer snapshot |
| `expose_parameter` | Expose a parameter for runtime control |

#### `render_pipeline_op`

| Operation | Description |
|---|---|
| `create_urp_asset` | Create a URP pipeline asset with settings |
| `create_hdrp_asset` | Create an HDRP pipeline asset |
| `set_quality_settings` | Modify rendering quality (shadows, AA, etc.) |
| `create_renderer` | Create a URP renderer with feature list |
| `add_renderer_feature` | Add a renderer feature |

#### `addressable_op`

| Operation | Description |
|---|---|
| `create_group` | Create an Addressable group with packing settings |
| `add_entry` | Mark an asset as Addressable with address and labels |
| `remove_entry` | Remove Addressable marking |
| `set_labels` | Set labels on entries |
| `list_groups` | List all groups and their entries |
| `analyze` | Run Addressable analysis rules |

---

### DirectorAnim — Animation

#### `animation_clip_op`

Create and modify AnimationClips.

| Operation | Description |
|---|---|
| `create` | Create a new AnimationClip |
| `add_curve` | Add an animation curve (property path + keyframes) |
| `remove_curve` | Remove a curve |
| `set_keyframe` | Add or modify a keyframe on an existing curve |
| `set_events` | Add AnimationEvents at specified times |
| `set_loop` | Configure loop settings |
| `list_curves` | List all curves in a clip |

**Example — add position curve:**

```json
{
  "operation": "add_curve",
  "clip_path": "Assets/Animations/Walk.anim",
  "relative_path": "",
  "property_name": "m_LocalPosition.x",
  "type": "Transform",
  "keyframes": [
    { "time": 0, "value": 0, "in_tangent": 0, "out_tangent": 1 },
    { "time": 0.5, "value": 2, "in_tangent": 1, "out_tangent": 0 },
    { "time": 1.0, "value": 0, "in_tangent": 0, "out_tangent": 0 }
  ]
}
```

#### `animator_controller_op`

Create and modify AnimatorControllers (state machines).

| Operation | Description |
|---|---|
| `create` | Create a new AnimatorController |
| `add_parameter` | Add a parameter (float, int, bool, trigger) |
| `add_state` | Add a state to a layer |
| `set_state_clip` | Assign an AnimationClip to a state |
| `add_transition` | Add a transition between states with conditions |
| `set_transition_conditions` | Modify transition conditions |
| `set_default_state` | Set which state is the entry state |
| `add_layer` | Add an animation layer with blend mode and mask |
| `list_states` | List all states and transitions in a layer |

**Example — add transition:**

```json
{
  "operation": "add_transition",
  "controller_path": "Assets/Animations/PlayerController.controller",
  "source_state": "Idle",
  "destination_state": "Walk",
  "layer": 0,
  "has_exit_time": false,
  "conditions": [
    { "parameter": "Speed", "mode": "greater", "threshold": 0.1 }
  ],
  "transition_duration": 0.15
}
```

#### `blend_tree_op`

Create and configure blend trees.

| Operation | Description |
|---|---|
| `create` | Create a blend tree in a state |
| `add_motion` | Add a motion (clip or child blend tree) |
| `set_blend_type` | 1D, 2D Simple Directional, 2D Freeform, Direct |
| `set_parameter` | Set blend parameter(s) |
| `set_thresholds` | Set threshold values for each motion |

#### `timeline_op`

Create and modify Timeline assets and tracks.

| Operation | Description |
|---|---|
| `create` | Create a TimelineAsset |
| `add_track` | Add a track (Animation, Activation, Audio, Signal, Control, Playable) |
| `add_clip` | Add a clip to a track with start time and duration |
| `set_clip_properties` | Modify clip timing, blend, speed |
| `add_marker` | Add a signal or marker at a time |
| `bind_track` | Set the track binding (which object it controls) |
| `list_tracks` | List all tracks and their clips |

---

### DirectorSpatial — World Building

#### `tilemap_op`

Tilemap painting and management.

| Operation | Description |
|---|---|
| `set_tile` | Place a tile at a cell position |
| `set_tiles` | Batch place tiles (array of position + tile pairs) |
| `box_fill` | Fill a rectangular region with a tile |
| `flood_fill` | Flood fill from a position |
| `clear` | Clear all tiles or a region |
| `get_tile` | Read what tile is at a position |
| `get_used_tiles` | List all occupied cell positions |
| `create_rule_tile` | Create a RuleTile with neighbor rules |
| `set_tilemap_layer` | Configure tilemap sorting order, material |

**Example — batch paint:**

```json
{
  "operation": "set_tiles",
  "tilemap_path": "/Grid/Ground",
  "tile_asset": "Assets/Tiles/Grass.asset",
  "positions": [
    [0, 0, 0], [1, 0, 0], [2, 0, 0],
    [0, 1, 0], [1, 1, 0], [2, 1, 0]
  ]
}
```

#### `terrain_op`

Terrain manipulation.

| Operation | Description |
|---|---|
| `create` | Create a Terrain + TerrainData asset |
| `set_heightmap` | Set heightmap values (region or full) |
| `smooth_heightmap` | Smooth a region of the heightmap |
| `paint_texture` | Paint terrain layers (splatmap) at positions |
| `add_terrain_layer` | Add a terrain texture layer |
| `place_trees` | Place tree instances |
| `place_details` | Paint detail meshes/textures (grass, flowers) |
| `set_size` | Set terrain dimensions |
| `get_height` | Sample height at a world position |

**Example — set heightmap region:**

```json
{
  "operation": "set_heightmap",
  "terrain_path": "/Terrain",
  "region": { "x": 0, "y": 0, "width": 64, "height": 64 },
  "heights": [[0.1, 0.15, 0.2, ...], ...]
}
```

#### `navmesh_op`

NavMesh configuration and baking.

| Operation | Description |
|---|---|
| `bake` | Bake the NavMesh with current settings |
| `set_area` | Configure NavMesh area types (cost, name) |
| `add_modifier` | Add/configure NavMeshModifier on an object |
| `add_link` | Create an OffMeshLink between two points |
| `set_agent_type` | Configure agent type settings (radius, height, step) |
| `add_surface` | Add NavMeshSurface component to an object |

#### `probuilder_op`

ProBuilder mesh creation (requires com.unity.probuilder).

| Operation | Description |
|---|---|
| `create_shape` | Create a ProBuilder shape (cube, cylinder, stair, arch, etc.) |
| `extrude_faces` | Extrude selected faces |
| `set_material` | Assign material to specific faces |
| `merge` | Merge multiple ProBuilder objects |
| `boolean_op` | Union, subtract, intersect two meshes |
| `export_mesh` | Export to standard Mesh asset |

---

### DirectorInput — Input System

#### `input_action_op`

Create and modify Input System action maps (requires com.unity.inputsystem).

| Operation | Description |
|---|---|
| `create_asset` | Create a new InputActionAsset |
| `add_action_map` | Add an action map |
| `add_action` | Add an action with type (value, button, pass-through) |
| `add_binding` | Add a binding to an action (path, interactions, processors) |
| `add_composite` | Add a composite binding (e.g., 2D vector from WASD) |
| `set_control_scheme` | Define control schemes |
| `list_actions` | List all maps, actions, and bindings |

**Example — add WASD movement:**

```json
{
  "operation": "add_composite",
  "asset_path": "Assets/Input/PlayerInput.inputactions",
  "action_map": "Gameplay",
  "action": "Move",
  "composite_type": "2DVector",
  "bindings": {
    "up": "<Keyboard>/w",
    "down": "<Keyboard>/s",
    "left": "<Keyboard>/a",
    "right": "<Keyboard>/d"
  }
}
```

---

### DirectorConfig — Project Configuration

#### `lighting_op`

| Operation | Description |
|---|---|
| `set_ambient` | Set ambient lighting mode and color |
| `set_fog` | Configure fog (mode, color, density) |
| `set_skybox` | Set skybox material |
| `add_light_probe_group` | Create a light probe group with positions |
| `add_reflection_probe` | Create a reflection probe |
| `bake` | Trigger lightmap baking |

#### `quality_op`

| Operation | Description |
|---|---|
| `set_level` | Switch quality level |
| `set_shadow_settings` | Shadow distance, resolution, cascades |
| `set_rendering` | LOD bias, pixel light count, texture quality |
| `list_levels` | List all quality levels and their settings |

#### `project_settings_op`

| Operation | Description |
|---|---|
| `set_physics` | Gravity, default material, layer collision matrix |
| `set_time` | Fixed timestep, max timestep |
| `set_player` | Company name, product name, default icon, splash |
| `set_tags_and_layers` | Add tags, add sorting layers, add layers |

#### `build_profile_op`

| Operation | Description |
|---|---|
| `create` | Create a new Build Profile |
| `set_scenes` | Set scene list for a profile |
| `set_platform` | Configure platform-specific settings |
| `set_scripting_backend` | Set Mono or IL2CPP |
| `list_profiles` | List all build profiles |

---

### Batch — Multi-Operation Transactions

#### `batch`

Execute multiple Director operations as a single atomic unit.

| Parameter | Type | Description |
|---|---|---|
| `operations` | `BatchOp[]` | Array of operations to execute sequentially |

**BatchOp:**

```json
{
  "tool": "scene_op",
  "params": { "operation": "create_gameobject", "name": "Enemy", ... }
}
```

Operations execute in order. Later operations can reference objects created
by earlier ones. If any operation fails, all preceding operations are rolled
back. The entire batch is a single Undo step.

See ARCHITECTURE.md "Batch Operations" for full semantics.

---

## Cross-Cutting Concerns

### Undo Grouping

Related mutations are grouped into a single undo step:

```csharp
Undo.SetCurrentGroupName("Theatre: Create enemy prefab");
// ... multiple operations ...
Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
```

An agent creating a prefab with 5 components results in one Ctrl+Z action
for the human, not 6.

### Asset Refresh

Director batches asset operations to avoid expensive reimports:

```csharp
AssetDatabase.StartAssetEditing();
try
{
    // Multiple asset operations
}
finally
{
    AssetDatabase.StopAssetEditing();
}
```

The response indicates whether a refresh occurred.

### Asset Path Validation

All asset paths must:
- Start with `"Assets/"` or `"Packages/"`
- Use forward slashes
- Include appropriate file extension
- Not conflict with existing assets (unless explicitly overwriting)

Invalid paths return an `invalid_asset_path` error with a suggestion.

### Type Resolution

Component and ScriptableObject types are resolved by:
1. Exact match: `"BoxCollider"` → `UnityEngine.BoxCollider`
2. Qualified name: `"UnityEngine.UI.Image"` → exact
3. Script name: `"Health"` → searches project scripts

Ambiguous matches return an error listing the candidates.

### Dry Run Mode

All Director operations support an optional `dry_run: true` parameter.
When set, the operation validates inputs and returns what would happen
without actually making changes. Useful for agents to verify before
committing.

```json
{
  "operation": "create_gameobject",
  "name": "Enemy",
  "components": [{ "type": "NonExistentComponent" }],
  "dry_run": true
}
```

Response:

```json
{
  "dry_run": true,
  "would_succeed": false,
  "errors": [
    { "field": "components[0].type", "error": "type_not_found", "value": "NonExistentComponent" }
  ]
}
```

### Response Envelope

All Director operations return a consistent envelope:

```json
{
  "result": "ok",
  "operation": "create_gameobject",
  "path": "/Enemy",
  "instance_id": 19500,
  "details": { ... }
}
```

Failed operations:

```json
{
  "result": "error",
  "operation": "create_gameobject",
  "error": {
    "code": "parent_not_found",
    "message": "Parent path '/NonExistent' does not exist",
    "suggestion": "Use scene_hierarchy:find to locate the correct parent"
  }
}
```
