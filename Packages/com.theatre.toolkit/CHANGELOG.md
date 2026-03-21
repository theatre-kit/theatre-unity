# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [0.1.0] - 2026-03-21

### Added

#### Stage — Observation

- `scene_snapshot`: budgeted overview of all scene GameObjects with component listing and token budget enforcement
- `scene_hierarchy`: navigate and filter the scene hierarchy by path, tag, layer, or component type
- `scene_inspect`: deep-inspect a specific GameObject, its transform, and all attached components with serialized property values
- `scene_delta`: track what changed in the scene between two snapshots (added, removed, modified GameObjects)
- `spatial_query` with 7 operations: `nearest`, `radius`, `overlap`, `raycast`, `linecast`, `path_distance`, `bounds` — supports 2D and 3D physics modes, component and tag filters, token budget
- `watch` with 4 operations: `create`, `remove`, `list`, `check` — property watches with SSE push notifications and SQLite persistence across domain reloads
- `action` with 8 operations: `teleport`, `set_property`, `set_active`, `set_timescale`, `pause`, `step`, `unpause`, `invoke_method` — runtime mutations with Undo support
- `recording` (Dashcam) with 9 operations: `start`, `stop`, `marker`, `list_clips`, `delete_clip`, `query_range`, `diff_frames`, `clip_info`, `analyze` — SQLite-backed frame capture with delta compression and semantic analysis

#### Director — Mutation

- `scene_op` with 10 operations: scene lifecycle management (load, unload, create, save, duplicate, rename, set active, move object to scene, merge scenes, instantiate at path)
- `prefab_op` with 7 operations: instantiate, apply overrides, revert overrides, unpack, open/close prefab stage, create from GameObject
- `batch`: execute multiple tool calls in a single round-trip with per-call error isolation
- `material_op` with 4 operations: create, get properties, set property, assign to renderer
- `scriptable_object_op` with 4 operations: create, read, write, list assets of type
- `physics_material_op` with 2 operations: create, set properties (friction, bounciness, combine modes)
- `texture_op` with 4 operations: get import settings, set import settings, create render texture, read pixels
- `sprite_atlas_op` with 4 operations: create, add sprites, set settings, pack
- `audio_mixer_op` with 6 operations: create group, set parameter, create snapshot, transition to snapshot, expose parameter, set exposed parameter
- `render_pipeline_op` with 5 operations: URP and HDRP render pipeline asset property get/set, create URP/HDRP asset, set project render pipeline
- `addressable_op` with 6 operations: create group, add asset, remove asset, set label, build, get settings
- `animation_clip_op` with 7 operations: create clip, add curve, remove curve, add event, set loop, get info, list clips
- `animator_controller_op` with 9 operations: create controller, add layer, add parameter, add state, add transition, set transition condition, set state motion, set default state, get info
- `blend_tree_op` with 5 operations: create blend tree, add child, set threshold, set blend type, get info
- `timeline_op` with 7 operations: create timeline, add track, add clip, set clip time, remove clip, remove track, get info
- `tilemap_op` with 9 operations: set tile, clear tile, fill rect, flood fill, create tilemap, create tile, get tile, get used tiles, clear all
- `navmesh_op` with 6 operations: bake, clear, add surface, add modifier, get settings, set settings
- `terrain_op` with 9 operations: create, set heightmap, get heightmap, set splat (texture layer), add tree prototype, paint trees, add detail prototype, paint detail, get info
- `probuilder_op` with 6 operations: create shape, extrude face, inset face, delete face, merge objects, triangulate
- `input_action_op` with 7 operations: create asset, add action map, add action, add binding, enable/disable action map, get info
- `lighting_op` with 6 operations: set ambient, set skybox, add reflection probe, bake GI, set fog, get settings
- `quality_op` with 4 operations: list levels, set active level, get settings, set setting
- `project_settings_op` with 4 operations: get player settings, set player setting, get time settings, set time setting
- `build_profile_op` with 5 operations: list profiles, create profile, set active, get settings, set setting

#### ECS — DOTS

- `ecs_world` with 4 operations: list worlds, get world info, enable system, disable system
- `ecs_snapshot`: budgeted overview of all entities across all worlds
- `ecs_inspect`: deep-inspect a specific entity and all its component data
- `ecs_query` with 3 operations: query by component types (all, any, none filters)
- `ecs_action` with 5 operations: add component, remove component, set component value, create entity, destroy entity

#### Editor UI

- Theatre panel (**Window > Theatre**) with server status, play mode indicator, active scene display
- Project Settings integration (**Edit > Project Settings > Theatre**): port, enabled tool groups, token budget
- Keyboard shortcuts: F8 (start/stop server), F9 (toggle play mode), Ctrl+Shift+T (open Theatre panel)
- Scene View gizmos: spatial query visualization with auto-fade and per-gizmo type toggles
- Scene View overlay for quick server status
- Welcome dialog shown on first install with setup instructions

#### Infrastructure

- Streamable HTTP transport implementing MCP protocol (JSON-RPC 2.0 over HTTP)
- All-C# in-process MCP server — no sidecar binary, no external dependencies beyond Unity 6
- Main thread dispatch via `EditorCoroutine` for safe Unity API access from background HTTP handler threads
- Domain reload survival: server restarts via `[InitializeOnLoad]`, MCP session ID persisted via `SessionState`
- SQLite-backed recording with delta compression (only changed component values stored per frame)
- Watch persistence across domain reloads via `SessionState` serialization
- Spatial index with configurable clustering and token budget enforcement
- `PropertySerializer` for round-trip serialization of Unity component properties via `SerializedObject`
- `ObjectResolver` for resolving GameObjects by path or instance ID with caching
- Token budget system for controlling response size in list-returning tools
- 350+ unit tests (EditMode) covering all tool handlers, spatial queries, watch engine, and recording

[0.1.0]: https://github.com/theatre-kit/theatre-unity/releases/tag/v0.1.0
