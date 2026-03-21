# Theatre Unity — Roadmap

Phased implementation plan. Each phase produces a usable, testable
increment. Phases are sequential — later phases depend on earlier ones.

---

## Phase 0 — Scaffold ✓

Set up the project skeleton. No functionality, just structure.

- [x] Create Unity 6 project as test harness
- [x] Create UPM package skeleton (`com.theatre.toolkit`)
- [x] Add `TheatreServer.cs` stub with `[InitializeOnLoad]`
- [x] Add `HttpListener` lifecycle — bind, listen, shutdown
- [x] Verify server starts on editor load and restarts on domain reload
- [x] Add `/health` endpoint returning `{ "status": "ok" }`
- [ ] CI: GitHub Actions workflow (blocked — requires Unity Pro for batch mode)

---

## Phase 1 — MCP Core ✓

Implement the MCP protocol layer.

- [x] JSON-RPC 2.0 types (`JsonRpcMessage`, `JsonRpcError`, `JsonRpcResponse`)
- [x] MCP handshake (`initialize` / `initialized`)
- [x] `tools/list` — returns registered tools with JSON Schema parameters
- [x] `tools/call` — dispatches to handler by name, returns result
- [x] Tool registry with group-based filtering (`ToolGroup` flags enum)
- [x] `notifications/tools/list_changed` when groups toggle
- [x] `MainThreadDispatcher` — ConcurrentQueue + EditorApplication.update
- [x] `TheatreConfig` — port, enabled groups, per-tool overrides
- [x] SSE endpoint (GET `/mcp`) for server-initiated notifications
- [x] `theatre_status` dummy tool validates full round-trip
- [x] MCP integration tests (HttpClient → JSON-RPC → valid response)

---

## Phase 2 — Stage: Scene Awareness ✓

First real tools. Agent can see the scene.

- [x] `scene_snapshot` — budgeted overview of GameObjects with positions
- [x] `scene_hierarchy` — list, find, search operations
- [x] `scene_inspect` — deep single-object inspection via SerializedProperty
- [x] Object addressing: hierarchy path + instance_id on all responses
- [x] Multi-scene support (`SceneName:/Path` addressing)
- [x] Prefab mode detection and scoping
- [x] Token budgeting engine (estimate, truncate, paginate)
- [x] Pagination with cursors
- [x] Play mode vs edit mode reporting
- [x] Clustering for snapshot summaries
- [x] Test scene with known layout (auto-generated via TestSceneCreator)

---

## Phase 3 — Stage: Spatial Queries ✓

Agent can ask spatial questions.

- [x] `spatial_query:nearest` — N closest objects to a point
- [x] `spatial_query:radius` — all objects within radius
- [x] `spatial_query:overlap` — physics overlap (sphere, box, capsule)
- [x] `spatial_query:raycast` — single and multi-hit
- [x] `spatial_query:linecast` — line-of-sight check
- [x] `spatial_query:path_distance` — NavMesh path distance
- [x] `spatial_query:bounds` — world-space bounding box
- [x] 2D/3D physics toggle (auto-detect + per-query `physics` param)
- [x] Spatial index (flat-list brute force, adequate for typical scenes)
- [x] `requires_play_mode` error for physics queries in edit mode

---

## Phase 4 — Stage: Watches & Actions ✓

Agent can subscribe to changes and manipulate game state.

- [x] `watch:create` — subscribe with conditions (threshold, proximity,
  region, property_changed, destroyed, spawned)
- [x] `watch:remove`, `watch:list`, `watch:check`
- [x] Watch trigger notifications via SSE
- [x] Watch throttling (`throttle_ms`)
- [x] Watch persistence across domain reloads (SessionState)
- [x] `action:teleport` — move GameObject to position
- [x] `action:set_property` — set component property via SerializedProperty
- [x] `action:set_active` — enable/disable GameObject
- [x] `action:set_timescale` — change Time.timeScale
- [x] `action:pause` / `action:step` / `action:unpause`
- [x] `action:invoke_method` — call public methods (simple signatures)
- [x] `scene_delta` — what changed since last query / specific frame

Also implemented (not in original roadmap):
- [x] `unity_console` — read Console log with grep, dedup, rollup, refresh
- [x] `unity_tests` — run EditMode/PlayMode tests and get results via MCP

---

## Phase 5 — Stage: Recording

Dashcam — frame-by-frame capture and temporal analysis.

- [ ] SQLite integration (native binaries in Plugins/)
- [ ] Database schema (frames, markers, metadata)
- [ ] `recording:start` — begin capture with filters
- [ ] `recording:stop` — end capture, return clip metadata
- [ ] `recording:marker` — insert named marker
- [ ] `recording:list_clips` / `recording:delete_clip`
- [ ] `recording:query_range` — query frames in time/frame range
- [ ] `recording:diff_frames` — compare two specific frames
- [ ] `recording:clip_info` / `recording:analyze`
- [ ] Delta compression (only write changed properties per frame)
- [ ] GC-friendly capture (object pooling, reused buffers)
- [ ] Configurable capture rate, max duration
- [ ] Recording survives domain reload (SessionState + SQLite persistence)

**Exit criteria**: Human reproduces a bug, agent scrubs through the
recording to find when and where things went wrong.

---

## Phase 6 — Director: Scenes & Prefabs

Agent can create and modify scenes and prefabs.

### Phase 6a — Core Operations

- [ ] `scene_op:create_scene` / `load_scene` / `unload_scene`
- [ ] `scene_op:create_gameobject` — with components, position, tag, layer
- [ ] `scene_op:delete_gameobject` / `reparent` / `duplicate`
- [ ] `scene_op:set_component` / `remove_component`
- [ ] `scene_op:move_to_scene`
- [ ] `prefab_op:create_prefab` / `instantiate`
- [ ] `prefab_op:apply_overrides` / `revert_overrides`
- [ ] `prefab_op:unpack` / `create_variant` / `list_overrides`
- [ ] Component type resolution (exact, qualified, script name search)
- [ ] Undo integration — every op undoable, grouped logically
- [ ] Asset path validation
- [ ] Dry run mode (`dry_run: true`)

### Phase 6b — Batch Operations

- [ ] `batch` meta-tool — multi-op atomic transactions
- [ ] `AssetDatabase.StartAssetEditing` batching
- [ ] All-or-nothing rollback via `Undo.RevertAllInCurrentGroup()`

**Exit criteria**: Agent can create a prefab with components, instantiate
it in a scene, apply overrides, and the human can undo it all with Ctrl+Z.

---

## Phase 7 — Director: Assets

Agent can create and configure non-code assets.

- [ ] `material_op` — create, set properties, set shader, list properties
- [ ] `scriptable_object_op` — create instances, set fields, find by type
- [ ] `texture_op` — import, configure import settings, sprite setup
- [ ] `sprite_atlas_op` — create, add/remove entries, pack
- [ ] `physics_material_op` — create, set friction/bounciness
- [ ] `audio_mixer_op` — create mixer, groups, effects, snapshots, expose params
- [ ] `render_pipeline_op` — create URP/HDRP assets, renderers, features
- [ ] `addressable_op` — create groups, add entries, set labels, analyze

**Exit criteria**: Agent can create a material with a URP/Lit shader,
create a ScriptableObject weapon data asset, configure an Addressable
group, and build an audio mixer hierarchy.

---

## Phase 8 — Director: Animation

Agent can create animation assets and state machines.

- [ ] `animation_clip_op` — create clips, add/remove curves, set keyframes,
  animation events, loop settings
- [ ] `animator_controller_op` — create controllers, add parameters, states,
  transitions with conditions, layers, default state
- [ ] `blend_tree_op` — create blend trees, add motions, set blend type
  and parameters
- [ ] `timeline_op` — create Timeline assets, add tracks (animation,
  activation, audio, signal, control), add clips, set bindings

**Exit criteria**: Agent can create a walk animation clip with position
curves, build an AnimatorController with Idle→Walk→Run state machine,
and create a Timeline cutscene with animation and audio tracks.

---

## Phase 9 — Director: Spatial

Agent can build worlds — tilemaps, terrain, navigation.

- [ ] `tilemap_op` — set_tile, set_tiles, box_fill, flood_fill, clear,
  get_tile, get_used_tiles, create_rule_tile
- [ ] `terrain_op` — create, set_heightmap, smooth, paint_texture,
  add_terrain_layer, place_trees, place_details
- [ ] `navmesh_op` — bake, set_area, add_modifier, add_link,
  set_agent_type, add_surface
- [ ] `probuilder_op` — create_shape, extrude_faces, set_material,
  merge, boolean_op, export_mesh (requires com.unity.probuilder)

**Exit criteria**: Agent can paint a tilemap level, sculpt terrain with
textures and trees, bake a NavMesh, and create ProBuilder geometry.

---

## Phase 10 — Director: Input & Config

Agent can configure project settings and input.

- [ ] `input_action_op` — create asset, add maps, actions, bindings,
  composites, control schemes (requires com.unity.inputsystem)
- [ ] `lighting_op` — ambient, fog, skybox, light probes, reflection
  probes, bake
- [ ] `quality_op` — set level, shadows, rendering settings
- [ ] `project_settings_op` — physics, time, player, tags and layers
- [ ] `build_profile_op` — create, set scenes, platform, scripting backend

**Exit criteria**: Agent can set up WASD+mouse input, configure lighting
and quality settings, and create a build profile.

---

## Phase 11 — ECS

Full DOTS/Entities support. Parallel tool set to the GameObject tools.

- [ ] Package detection (com.unity.entities installed check)
- [ ] `ecs_world` — list_worlds, world_summary, list_archetypes, list_systems
- [ ] `ecs_snapshot` — spatial overview of entities by archetype query
- [ ] `ecs_inspect` — read all component data for an entity
- [ ] `ecs_query` — nearest, radius, overlap, raycast over entities
  with LocalTransform
- [ ] `ecs_action` — set_component, add_component, remove_component,
  destroy_entity, create_entity
- [ ] Entity addressing (index+version) with archetype decoration
- [ ] Dynamic component type resolution via TypeManager
- [ ] ECS tools auto-hidden when Entities package not installed

**Exit criteria**: Agent can snapshot an ECS World, query entities by
archetype, inspect component data, and modify entity state.

---

## Phase 12 — Editor UI

The human-facing side. See what the agent sees and does.

- [ ] Theatre EditorWindow (UI Toolkit)
  - Server status bar (running, port, agent connected)
  - Tool group toggles with presets dropdown
  - Active watches list with status indicators
  - Agent activity feed (tool calls, params, token cost)
  - Session stats
- [ ] Recording section
  - Record/Stop/Mark buttons
  - Timeline scrubber with markers and agent query indicators
  - Recording stats (duration, frames, size)
  - Recordings library (list, load, delete)
- [ ] Scene View gizmos
  - Query visualizations (wire spheres, rays, overlap regions)
  - Watch visualizations (proximity circles, region boxes, highlights)
  - Action visualizations (teleport trails, property pulses)
  - Gizmo controls (toggle, duration, opacity)
- [ ] Scene View overlay (compact status)
- [ ] Project Settings provider (`Project > Theatre`)
- [ ] Keyboard shortcuts (F8 record, F9 marker, Ctrl+Shift+T panel)
- [ ] First-run welcome dialog with .mcp.json snippet
- [ ] Console logging with `[Theatre]` prefix for Director mutations

**Exit criteria**: Human can see agent activity in real-time, visualize
spatial queries in the Scene view, control recordings, and toggle tools —
all without leaving the Unity Editor.

---

## Phase 13 — Polish & Release

Harden everything for public release.

- [ ] Error message audit — every error has actionable `suggestion`
- [ ] Performance profiling — recording at 60fps, spatial query latency
- [ ] Domain reload stress testing
- [ ] Documentation site (VitePress, mirroring Godot site structure)
- [ ] README with installation instructions, .mcp.json setup, screenshots
- [ ] Sample project with example scenes
- [ ] Package validation (Unity's package validation suite)
- [ ] CHANGELOG
- [ ] Release workflow (GitHub Actions → UPM package tag)
- [ ] OpenUPM or git-based installation guide

**Exit criteria**: Public repo with installable UPM package, documentation
site, and working integration with at least one MCP client.
