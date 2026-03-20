# Theatre Unity — Roadmap

Phased implementation plan. Each phase produces a usable, testable
increment. Phases are sequential — later phases depend on earlier ones.

---

## Phase 0 — Scaffold

Set up the project skeleton. No functionality, just structure.

- [ ] Create Unity 6 project as test harness
- [ ] Create UPM package skeleton (`com.theatre.toolkit`)
  - `package.json` targeting `"unity": "6000.0"`
  - Runtime and Editor assembly definitions
  - Folder structure per ARCHITECTURE.md
- [ ] Add `TheatreServer.cs` stub with `[InitializeOnLoad]`
- [ ] Add `HttpListener` lifecycle — bind, listen, shutdown
- [ ] Verify server starts on editor load and restarts on domain reload
- [ ] Add `/health` endpoint returning `{ "status": "ok" }`
- [ ] CI: GitHub Actions workflow — build Unity project in batch mode

**Exit criteria**: `curl http://localhost:9078/health` returns a response
from inside the Unity Editor.

---

## Phase 1 — MCP Core

Implement the MCP protocol layer. No tools yet — just the infrastructure
to register and call them.

- [ ] JSON-RPC 2.0 types (`JsonRpcRequest`, `JsonRpcResponse`, `JsonRpcError`)
- [ ] MCP handshake (`initialize` / `initialized`)
- [ ] `tools/list` — returns registered tools with JSON Schema parameters
- [ ] `tools/call` — dispatches to handler by name, returns result
- [ ] Tool registry with group-based filtering (`ToolGroup` flags enum)
- [ ] `notifications/tools/list_changed` when groups toggle
- [ ] EditorCoroutine dispatch — HTTP thread → main thread → response
- [ ] `TheatreConfig` — port, enabled groups, per-tool overrides
- [ ] SSE endpoint (GET `/mcp`) for server-initiated notifications
- [ ] Add one dummy tool (`theatre_status`) to validate the full round-trip
- [ ] MCP integration test: HttpClient → JSON-RPC request → valid response

**Exit criteria**: An MCP client (Claude Code, etc.) can connect via
Streamable HTTP, see `theatre_status` in the tool list, and call it.

---

## Phase 2 — Stage: Scene Awareness

First real tools. Agent can see the scene.

- [ ] `scene_snapshot` — budgeted overview of GameObjects with positions
- [ ] `scene_hierarchy` — list, find, search operations
- [ ] `scene_inspect` — deep single-object inspection via SerializedProperty
- [ ] Object addressing: hierarchy path + instance_id on all responses
- [ ] Multi-scene support (`SceneName:/Path` addressing)
- [ ] Prefab mode detection and scoping
- [ ] Token budgeting engine (estimate, truncate, paginate)
- [ ] Pagination with cursors
- [ ] Play mode vs edit mode reporting
- [ ] Clustering for snapshot summaries
- [ ] Test scenes with known layouts for deterministic assertions

**Exit criteria**: Agent can snapshot a scene, navigate the hierarchy,
and inspect any GameObject's full component state.

---

## Phase 3 — Stage: Spatial Queries

Agent can ask spatial questions.

- [ ] `spatial_query:nearest` — N closest objects to a point
- [ ] `spatial_query:radius` — all objects within radius
- [ ] `spatial_query:overlap` — physics overlap (sphere, box, capsule)
- [ ] `spatial_query:raycast` — single and multi-hit
- [ ] `spatial_query:linecast` — line-of-sight check
- [ ] `spatial_query:path_distance` — NavMesh path distance
- [ ] `spatial_query:bounds` — world-space bounding box
- [ ] 2D/3D physics toggle (project default + per-query `physics` param)
- [ ] Spatial index (R-tree for 3D, grid hash for 2D)
- [ ] `requires_play_mode` error for physics queries in edit mode

**Exit criteria**: Agent can ask "what's near the player?", "can this
enemy see that door?", "what's the NavMesh distance?" and get answers.

---

## Phase 4 — Stage: Watches & Actions

Agent can subscribe to changes and manipulate game state.

- [ ] `watch:create` — subscribe with conditions (threshold, proximity,
  region, property_changed, destroyed, spawned)
- [ ] `watch:remove`, `watch:list`, `watch:check`
- [ ] Watch trigger notifications via SSE
- [ ] Watch throttling (`throttle_ms`)
- [ ] Watch persistence across domain reloads (SessionState)
- [ ] `action:teleport` — move GameObject to position
- [ ] `action:set_property` — set component property via SerializedProperty
- [ ] `action:set_active` — enable/disable GameObject
- [ ] `action:set_timescale` — change Time.timeScale
- [ ] `action:pause` / `action:step` / `action:unpause`
- [ ] `action:invoke_method` — call public methods (simple signatures)
- [ ] `scene_delta` — what changed since last query / specific frame

**Exit criteria**: Agent can watch for "enemy HP below 25", get notified
when it happens, teleport the player, pause the game, and step frames.

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
- [ ] `AssetDatabase.StartAssetEditing` batching
- [ ] Asset path validation
- [ ] Dry run mode (`dry_run: true`)
- [ ] `batch` meta-tool — multi-op atomic transactions

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
