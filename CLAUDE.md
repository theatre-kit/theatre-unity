# Theatre Unity — Agent Instructions

## What This Is

Theatre for Unity is an all-C# MCP server running inside the Unity Editor.
It gives AI agents spatial awareness of running Unity games (Stage) and
programmatic control over Unity subsystems that agents can't hand-write
(Director). Single UPM package, Streamable HTTP transport, no external
dependencies beyond Unity 6.

## Repository Layout

```
Packages/
  com.theatre.toolkit/        — UPM package (the product)
    Runtime/                  — Core server, transport, Stage logic
      Core/                   — TheatreServer, TheatreConfig, ToolRegistry
      Transport/              — HttpTransport, RequestRouter, MCP types
      Stage/                  — Spatial awareness tools
        GameObject/           — GameObject-based scene queries
        ECS/                  — DOTS/Entities queries
        Recording/            — Dashcam / SQLite frame capture
        Spatial/              — Spatial index, clustering, budgeting
      Director/               — Scene/asset mutation tools
        Scenes/               — Scene and hierarchy operations
        Prefabs/              — Prefab lifecycle
        Assets/               — Materials, SOs, textures, etc.
        Animation/            — Clips, controllers, blend trees, timeline
        Spatial/              — Tilemap, terrain, navmesh, ProBuilder
        Input/                — Input System action maps
        Config/               — Project/quality/lighting settings
    Editor/                   — EditorWindow, gizmos, settings
    Tests/
      Editor/                 — EditMode tests
docs/                         — Foundation design docs
  ARCHITECTURE.md             — System architecture
  STAGE-SURFACE.md            — Stage tool definitions
  DIRECTOR-SURFACE.md         — Director tool definitions
  UX.md                       — Editor UI design
  CONTRACTS.md                — Wire format rules
  designs/                    — Per-phase implementation designs
TestProject/                  — Unity 6 test harness project
```

## Unity Installation

Unity 6 is installed via Unity Hub:
- **Editor**: `~/Unity/Hub/Editor/6000.4.0f1/Editor/Unity`
- **Version**: 6000.4.0f1 (Unity 6.4 LTS)
- **Hub manages** the installation, license, and project association

## Build & Test

```bash
# Open TestProject/ in Unity 6 via Unity Hub
# Package compiles automatically when Unity loads the project

# Run tests: Window > General > Test Runner > EditMode > Run All

# Quick health check (server starts on editor load)
curl http://localhost:9078/health

# MCP handshake test
curl -s -X POST http://localhost:9078/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"curl","version":"1.0"}}}'
```

### Unity License Note

Unity 6 requires a **Pro license** for batch mode / CLI usage
(`-batchmode`, `-nographics`). The `com.unity.editor.headless`
entitlement is not included in Unity Personal. This means:

- **No CLI test runner** — run tests from the Unity Editor GUI instead
- **No headless CI** without Pro or a Build Server license
- **Project creation** must be done via Unity Hub, not `unity -createProject`

All development and testing is done through the Unity Editor GUI.
CI automation requires Unity Pro or GameCI Docker images.

## Key Constraints

- **All C#, in-process**: The MCP server runs inside the Unity Editor.
  No sidecar binary, no TCP bridge, no wire protocol between server and
  engine. Direct Unity API access.
- **Main thread only for Unity APIs**: HttpListener runs on background
  threads. All Unity API calls must be marshaled to the main thread via
  EditorCoroutine dispatch.
- **Domain reload survival**: Server restarts via [InitializeOnLoad] on
  every domain reload. Stateful data (watches, recordings) persists via
  SessionState and SQLite.
- **Editor only**: Theatre runs in the Unity Editor, not in player builds.
- **No external MCP SDK**: MCP protocol (JSON-RPC 2.0 over Streamable
  HTTP) is implemented directly. The official C# MCP SDK requires ASP.NET
  Core which is incompatible with Unity.

## Threading Rules (MANDATORY — load unity-threading skill)

**Load the `unity-threading` skill before writing any route handler, background
task, or any code that runs outside the main thread.** This is not optional.

The bug history: `TheatreConfig.Port` was called from `HandleHealth` (a route
handler, therefore a thread-pool thread). `Port` calls `SessionState.GetInt`,
which throws `UnityException: GetInt can only be called from the main thread`.

### Hard rules — no exceptions

1. **Every `HttpListener` route handler body runs on a thread pool thread.**
   Treat every method registered with `s_router.Map(...)` as background-thread code.

2. **FORBIDDEN in route handlers** — these all throw `UnityException`:
   - `SessionState.*` (any method)
   - `TheatreConfig.Port`, `TheatreConfig.EnabledGroups`, `TheatreConfig.HttpPrefix`
   - `EditorApplication.isPlaying`, `EditorApplication.isPaused`, any `EditorApplication` property
   - `SceneManager.*` (any method or property)
   - `Debug.Log`, `Debug.LogWarning`, `Debug.LogError`, `Debug.LogException`
   - `AssetDatabase.*`, `Undo.*`, `PrefabUtility.*`, `EditorPrefs.*`
   - Any `UnityEngine.Object` property or method (GameObject, Transform, Component, etc.)
   - `Time.frameCount`, `Time.time`, `Application.isPlaying`

3. **Two safe patterns for route handlers:**
   - **Cache**: Read Unity APIs once on the main thread at `StartServer()`, store in plain
     `static` fields (`s_cachedPort`, `s_cachedEnabledGroupsEnum`), read those fields in handlers.
   - **Dispatch**: Wrap Unity API access in `MainThreadDispatcher.Invoke(() => { ... })`.

4. **Tool handler `Execute()` methods are safe** — they are already dispatched to the main
   thread via `ExecuteToolOnMainThread` in `TheatreServer`. Unity APIs are allowed there.

5. **When adding new route handlers**: before committing, search the handler body for every
   `UnityEngine.*` and `UnityEditor.*` call. Each one must be either (a) eliminated in favor
   of a cached field, or (b) wrapped in `MainThreadDispatcher.Invoke`.

6. **Domain reload**: `[InitializeOnLoad]` static constructors re-run on every domain reload.
   Use `EditorApplication.delayCall += StartServer` (not direct construction) to restart
   the server. `SessionState` survives reload; all managed state (threads, `HttpListener`,
   static fields) is destroyed and recreated.

See `/home/nathan/dev/theatre-unity/docs/unity-threading-idioms.md` for the full reference.

## Code Style

- Namespace: `Theatre` (runtime), `Theatre.Editor` (editor),
  `Theatre.Tests.Editor` (tests)
- Unity 6 / .NET Standard 2.1
- `Newtonsoft.Json` for JSON serialization (via com.unity.nuget.newtonsoft-json)
- `Debug.Log`/`Debug.LogWarning`/`Debug.LogError` with `[Theatre]` prefix
  for all console output
- No `#pragma warning disable` without justification
- All public types have XML doc comments
- Tests alongside source in `Tests/Editor/` using NUnit + Unity Test Framework
- No `UnityEngine.Object.Instantiate` or scene manipulation from
  background threads — always marshal to main thread first

## Wire Format Rules

- All JSON field names: `snake_case`
- Unity properties to snake_case: `localPosition` to `local_position`
- No abbreviations: `position` not `pos`, `distance` not `dist`
- Resource IDs: `<resource>_id` never bare `id`
- GameObjects always have both `path` and `instance_id`
- Singular result: `"result"`, plural list: `"results"`
- Errors include `code`, `message`, `suggestion`
- Vectors as arrays: `[x, y, z]`

See `docs/CONTRACTS.md` for full rules.

## Design Documents

Foundation docs in `docs/`:
- `ARCHITECTURE.md` — in-process model, package structure, threading,
  transport, domain reload, serialization, testing strategy
- `STAGE-SURFACE.md` — all Stage tools (GameObject + ECS)
- `DIRECTOR-SURFACE.md` — all Director tools (full Unity subsystem coverage)
- `UX.md` — editor window, Scene view gizmos, settings
- `CONTRACTS.md` — wire format naming, envelopes, errors, pagination

Phase designs in `docs/designs/`. Read the relevant phase design before
implementing.

## Git Conventions

- Commit messages: short imperative subject line (72 chars max)
- Do NOT add Co-Authored-By or AI attribution to commits
