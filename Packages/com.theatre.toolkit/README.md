# Theatre for Unity

AI agent toolkit for Unity — spatial awareness and programmatic control via MCP.

Theatre is an all-C# MCP server running inside the Unity Editor. It gives AI agents spatial awareness of running Unity games and programmatic control over Unity subsystems. Single UPM package, Streamable HTTP transport, no external dependencies beyond Unity 6.

---

## Features

### Stage — Observation Tools

| Tool | Operations | Description |
|------|-----------|-------------|
| `scene_snapshot` | 1 | Budgeted overview of all scene GameObjects |
| `scene_hierarchy` | 1 | Navigate and filter the scene hierarchy |
| `scene_inspect` | 1 | Deep-inspect a specific GameObject and its components |
| `scene_delta` | 1 | Track what changed in the scene since last snapshot |
| `spatial_query` | 7 | Spatial queries: nearest, radius, overlap, raycast, linecast, path_distance, bounds |
| `watch` | 4 | Property watches with SSE notifications: create, remove, list, check |
| `action` | 8 | Runtime mutations: teleport, set_property, set_active, set_timescale, pause, step, unpause, invoke_method |
| `recording` | 9 | Dashcam recording: start, stop, marker, list_clips, delete_clip, query_range, diff_frames, clip_info, analyze |

### Director — Mutation Tools

| Tool | Operations | Description |
|------|-----------|-------------|
| `scene_op` | 10 | Scene lifecycle: load, unload, create, save, move objects, etc. |
| `prefab_op` | 7 | Prefab lifecycle: instantiate, apply, revert, unpack, etc. |
| `batch` | 1 | Execute multiple tool calls in a single round-trip |
| `material_op` | 4 | Create/modify materials and shader properties |
| `scriptable_object_op` | 4 | Create/read/modify/list ScriptableObjects |
| `physics_material_op` | 2 | Create/modify physics materials |
| `texture_op` | 4 | Import settings and texture operations |
| `sprite_atlas_op` | 4 | Sprite atlas creation and management |
| `audio_mixer_op` | 6 | Audio mixer groups, parameters, snapshots |
| `render_pipeline_op` | 5 | URP/HDRP render pipeline asset settings |
| `addressable_op` | 6 | Addressables groups, labels, build |
| `animation_clip_op` | 7 | Animation clip curves, events, and settings |
| `animator_controller_op` | 9 | Animator controller states, transitions, parameters |
| `blend_tree_op` | 5 | Blend tree creation and child management |
| `timeline_op` | 7 | Timeline tracks, clips, and playable directors |
| `tilemap_op` | 9 | Tilemap tiles, rules, and painting |
| `navmesh_op` | 6 | NavMesh surfaces, modifiers, baking |
| `terrain_op` | 9 | Terrain heightmap, textures, trees, details |
| `probuilder_op` | 6 | ProBuilder mesh editing |
| `input_action_op` | 7 | Input System action maps and bindings |
| `lighting_op` | 6 | Lighting settings, probes, baked GI |
| `quality_op` | 4 | Quality levels and render settings |
| `project_settings_op` | 4 | Project settings and player settings |
| `build_profile_op` | 5 | Build profiles and target configuration |

### ECS — DOTS Tools

| Tool | Operations | Description |
|------|-----------|-------------|
| `ecs_world` | 4 | World info, system list, enable/disable systems |
| `ecs_snapshot` | 1 | Budgeted overview of all entities |
| `ecs_inspect` | 1 | Deep-inspect a specific entity and its components |
| `ecs_query` | 3 | Query entities by component types |
| `ecs_action` | 5 | Mutate entities: add/remove components, set values |

---

## Quick Start

**1. Install via Package Manager** (git URL — see Installation below)

**2. Theatre starts automatically.** The server runs on `http://localhost:9078/mcp` as soon as the package is imported. Check the Theatre panel via **Window > Theatre** to confirm it is running.

**3. Add Theatre to your MCP client config:**

For Claude Code, Cursor, or any MCP-compatible client, add to `.mcp.json` in your project root:

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

Then reload your MCP client. Theatre's tools will appear automatically.

---

## Installation

### Via Package Manager (Recommended)

1. Open **Window > Package Manager**
2. Click **+** → **Add package from git URL**
3. Enter:
   ```
   https://github.com/theatre-kit/theatre-unity.git?path=Packages/com.theatre.toolkit
   ```

### Manual

Clone the repository and copy `Packages/com.theatre.toolkit` into your project's `Packages/` folder:

```sh
git clone https://github.com/theatre-kit/theatre-unity.git
cp -r theatre-unity/Packages/com.theatre.toolkit /path/to/your/project/Packages/
```

---

## Requirements

- **Unity 6** (6000.0+)
- **.NET Standard 2.1**

No external dependencies beyond Unity 6 and its built-in Newtonsoft.Json package (`com.unity.nuget.newtonsoft-json`), which is declared as an automatic dependency.

---

## Optional Packages

Some Director and ECS tools only activate when the corresponding Unity package is installed. The core Stage tools work without any optional packages.

| Package | Tools Unlocked |
|---------|---------------|
| `com.unity.timeline` | `timeline_op` |
| `com.unity.probuilder` | `probuilder_op` |
| `com.unity.entities` | `ecs_world`, `ecs_snapshot`, `ecs_inspect`, `ecs_query`, `ecs_action` |
| `com.unity.addressables` | `addressable_op` |
| `com.unity.inputsystem` | `input_action_op` |
| `com.unity.render-pipelines.universal` | `render_pipeline_op` (URP mode) |
| `com.unity.render-pipelines.high-definition` | `render_pipeline_op` (HDRP mode) |
| `com.unity.ai.navigation` | `navmesh_op` (`add_modifier`, `add_surface` operations) |

---

## How It Works

Theatre runs as an in-process HTTP server inside the Unity Editor. There is no sidecar binary, no TCP bridge, and no external process — all Unity API access is direct.

- **Transport**: Streamable HTTP (MCP protocol, JSON-RPC 2.0)
- **Threading**: HTTP listener runs on background threads; all Unity API calls are marshaled to the main thread via `EditorCoroutine` dispatch
- **Domain reload survival**: Server restarts automatically via `[InitializeOnLoad]` on every recompile. Watches and recording state persist via `SessionState` and SQLite
- **Editor only**: Theatre runs in the Unity Editor, not in player builds

---

## Sample

A **Basic Setup** sample is included. Import it via **Package Manager > Theatre > Samples > Basic Setup**. It provides a `Health` MonoBehaviour and instructions for building a minimal test scene.

---

## License

MIT — see [LICENSE.md](LICENSE.md).
