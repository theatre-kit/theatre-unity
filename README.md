# Theatre for Unity

AI agent toolkit for Unity — spatial awareness and programmatic control via [MCP](https://modelcontextprotocol.io/).

Theatre is an all-C# MCP server that runs inside the Unity Editor. It gives AI coding agents (Claude Code, Cursor, Windsurf, etc.) the ability to see and manipulate your Unity scenes in real time. No sidecar process, no external runtime — just a single UPM package.

## What agents can do with Theatre

**Observe** (Stage tools) — snapshot the scene hierarchy, inspect GameObjects and components, run spatial queries (nearest, radius, raycast, overlap), watch properties for changes via SSE, and record frame-by-frame dashcam clips to SQLite.

**Build** (Director tools) — create and load scenes, instantiate prefabs, create and edit materials, animation clips, animator controllers, blend trees, timelines, tilemaps, terrain, navmesh, lighting, quality settings, input action maps, and more. All with full Undo support.

**42 tools** across 3 categories (Stage, Director, ECS) with ~160 total operations.

## Requirements

- **Unity 6** (6000.0+)
- No external dependencies (Newtonsoft.Json is pulled automatically)

## Installation

### Option 1: Git URL (recommended)

1. Open your Unity project
2. Go to **Window > Package Manager**
3. Click **+** > **Add package from git URL**
4. Enter:

```
https://github.com/theatre-kit/theatre-unity.git?path=Packages/com.theatre.toolkit
```

To pin a specific version, append `#v0.1.0` (or any tag/commit):

```
https://github.com/theatre-kit/theatre-unity.git?path=Packages/com.theatre.toolkit#v0.1.0
```

### Option 2: Local path (for development)

Clone the repo somewhere on your machine:

```sh
git clone https://github.com/theatre-kit/theatre-unity.git
```

Then in your Unity project, add it as a local package. Edit your project's `Packages/manifest.json` and add:

```json
{
  "dependencies": {
    "com.theatre.toolkit": "file:/path/to/theatre-unity/Packages/com.theatre.toolkit",
    ...
  }
}
```

Use an absolute path or a relative path from your project's `Packages/` folder.

### Option 3: Copy into project

Copy `Packages/com.theatre.toolkit/` directly into your project's `Packages/` folder:

```sh
cp -r theatre-unity/Packages/com.theatre.toolkit /path/to/your-project/Packages/
```

## Setup

**Theatre starts automatically.** The MCP server begins listening on `http://localhost:9078/mcp` as soon as the package is imported. Open **Window > Theatre** to see server status, toggle tool groups, and manage watches.

### Connect your AI agent

Add Theatre to your MCP client config. Create or edit `.mcp.json` in your project root:

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

This works with Claude Code, Cursor, Windsurf, and any MCP-compatible client. Reload the client and Theatre's tools will appear.

### Verify it's working

From your agent, call `theatre_status` — you should get back server info, the active scene, play mode state, and enabled tool groups.

Or hit the health endpoint directly:

```sh
curl http://localhost:9078/health
```

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| Port | `9078` | HTTP server port (persists via SessionState) |
| Tool groups | `GameObjectProject` | Which tool categories are active |
| Disabled tools | _(none)_ | Individual tools to hide |

All settings are accessible from the **Window > Theatre** editor panel.

## Tool Overview

### Stage (observation)

| Tool | Description |
|------|-------------|
| `scene_snapshot` | Budgeted overview of scene GameObjects with spatial clustering |
| `scene_hierarchy` | Navigate and filter the transform tree |
| `scene_inspect` | Deep-inspect a GameObject and all its components |
| `scene_delta` | Detect what changed since the last snapshot |
| `spatial_query` | nearest, radius, overlap, raycast, linecast, path_distance, bounds |
| `watch` | Create property watches with SSE push notifications |
| `action` | teleport, set_property, set_active, set_timescale, pause/step/unpause, invoke_method |
| `recording` | Frame-by-frame dashcam: start, stop, query, diff, analyze |

### Director (mutation)

| Tool | Description |
|------|-------------|
| `scene_op` | Load, unload, create, save, merge scenes; move objects |
| `prefab_op` | Instantiate, apply, revert, unpack, create prefabs |
| `batch` | Execute multiple tool calls atomically |
| `material_op` | Create and modify materials and shader properties |
| `scriptable_object_op` | CRUD for ScriptableObjects |
| `physics_material_op` | Create/modify physics materials |
| `texture_op` | Import settings, render textures, read pixels |
| `sprite_atlas_op` | Atlas creation and packing |
| `audio_mixer_op` | Mixer groups, parameters, snapshots |
| `animation_clip_op` | Curves, keyframes, events |
| `animator_controller_op` | States, transitions, parameters |
| `blend_tree_op` | Create and configure blend trees |
| `timeline_op` | Tracks, clips, playable directors |
| `tilemap_op` | Tile painting, rules, fills |
| `navmesh_op` | Surfaces, modifiers, baking |
| `terrain_op` | Heightmap, splatmaps, trees, details |
| `probuilder_op` | Mesh shapes, extrusion, booleans |
| `input_action_op` | Input System action maps and bindings |
| `lighting_op` | Ambient, skybox, probes, baked GI |
| `quality_op` | Quality levels, render settings |
| `project_settings_op` | Physics, time, player settings |
| `build_profile_op` | Build profiles and platform targets |

### ECS / DOTS (requires `com.unity.entities`)

| Tool | Description |
|------|-------------|
| `ecs_world` | World info, system listing |
| `ecs_snapshot` | Budgeted entity overview |
| `ecs_inspect` | Deep entity + component inspection |
| `ecs_query` | Query entities by component type |
| `ecs_action` | Add/remove/modify components |

### Infrastructure

| Tool | Description |
|------|-------------|
| `theatre_status` | Server health, play mode, active scene |
| `unity_console` | Read/filter/grep the Unity console |
| `unity_tests` | Run and retrieve EditMode/PlayMode test results |

## Optional package integrations

Some tools only activate when the corresponding package is installed:

| Package | Tools unlocked |
|---------|---------------|
| `com.unity.timeline` | `timeline_op` |
| `com.unity.probuilder` | `probuilder_op` |
| `com.unity.entities` | All ECS tools |
| `com.unity.addressables` | `addressable_op` |
| `com.unity.inputsystem` | `input_action_op` |
| `com.unity.render-pipelines.universal` | `render_pipeline_op` (URP) |
| `com.unity.render-pipelines.high-definition` | `render_pipeline_op` (HDRP) |
| `com.unity.ai.navigation` | `navmesh_op` (surface/modifier ops) |

## How it works

Theatre runs entirely inside the Unity Editor process:

1. **HTTP server** (`System.Net.HttpListener`) listens on `localhost:9078`
2. **MCP router** handles JSON-RPC 2.0 requests at `POST /mcp`
3. **Main thread dispatch** marshals all Unity API calls from HTTP threads to the editor's main thread
4. **Domain reload survival** — server auto-restarts via `[InitializeOnLoad]` on every recompile; watches and recordings persist via `SessionState` and SQLite
5. **SSE endpoint** (`GET /mcp`) pushes watch notifications to connected clients

No Unity Pro license is required for normal usage. Pro is only needed for headless CI (`-batchmode`).

## Repository layout

```
Packages/com.theatre.toolkit/   UPM package (the product)
  Runtime/                      Core server, transport, spatial index
  Editor/                       Tools, MCP router, editor UI
  Tests/                        EditMode tests
  Samples~/                     Basic Setup sample
docs/                           Architecture and design documents
TestProject/                    Unity 6 test harness
```

## Development

If you're contributing to Theatre itself:

1. Open `TestProject/` in Unity 6 (6000.0+)
2. The test project references the package via local path automatically
3. Run tests from **Window > General > Test Runner** (EditMode tab)
4. Check **Window > Theatre** for server status

See the [package README](Packages/com.theatre.toolkit/README.md) for the full tool reference.

## License

MIT
