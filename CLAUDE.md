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
      Core/                   — TheatreConfig, ToolRegistry, ToolGroup
      Transport/              — HttpTransport, McpRouter, RequestRouter, MCP types
      Stage/                  — Spatial awareness helpers
        GameObject/           — HierarchyWalker, ObjectResolver, Watch*
        ECS/                  — DOTS/Entities queries (placeholder)
        Recording/            — Dashcam / SQLite frame capture (placeholder)
        Spatial/              — SpatialIndex, Clustering, TokenBudget
      Director/               — Scene/asset mutation tools (placeholder)
        Scenes/               — Scene and hierarchy operations
        Prefabs/              — Prefab lifecycle
        Assets/               — Materials, SOs, textures, etc.
        Animation/            — Clips, controllers, blend trees, timeline
        Spatial/              — Tilemap, terrain, navmesh, ProBuilder
        Input/                — Input System action maps
        Config/               — Project/quality/lighting settings
    Editor/                   — TheatreServer, MainThreadDispatcher
      Tools/                  — MCP tool implementations
        Actions/              — ActionTool dispatcher + 6 action handlers
        Scene/                — SceneHierarchy/Snapshot/Inspect/Delta, PropertySerializer
        Spatial/              — SpatialQueryTool dispatcher + 7 query handlers, ResultBuilder
        Watch/                — WatchTool
        (root)                — TheatreStatusTool, UnityConsole/Tests, ConsoleLogBuffer
    Tests/
      Editor/                 — EditMode tests (feature-grouped)
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

Theatre runs as an MCP server inside Unity. You have direct access to
these tools — **use them after every code change instead of asking the
user to check Unity.**

### Development loop (MANDATORY after writing C# code)

1. `unity_console` `{"operation": "refresh"}` — trigger recompile
2. Wait ~5 seconds for domain reload
3. `unity_console` `{"filter": "error"}` — check for compile errors
4. If clean: `unity_tests` `{"operation": "run"}` — run tests
5. Wait ~12 seconds for tests to complete
6. `unity_tests` `{"operation": "results"}` — see failures (failures_only is default)
7. Fix any failures and repeat from step 1

### Session gotchas

- **MCP is stateless — no session enforcement.** The server returns a
  `Mcp-Session-Id` header per MCP spec but does not validate it on
  subsequent requests. Multiple agents can connect simultaneously.
  No re-initialization needed after domain reloads.
- **Test scene auto-generates.** `TestSceneCreator.cs` has an
  `[InitializeOnLoadMethod]` that creates `TestScene_Hierarchy.unity`
  if it doesn't exist. This fires on every domain reload.
- **Newtonsoft, not System.Text.Json.** All JSON code uses
  `Newtonsoft.Json` (`JObject`, `JArray`, `JsonConvert`). System.Text.Json
  does not exist in Unity 6. See `.claude/rules/unity-deprecated-apis.md`.
- **Test count drops = hidden compile error.** If test count unexpectedly
  drops (e.g., 142 → 87), a compile error in one of the test files is
  making all tests in that file invisible — no error is reported by the
  test runner. Run `unity_console {"filter": "error"}` to find it.
- **`internal` visibility across assemblies.** Runtime `internal` types
  are invisible to Editor code — use `public`. Editor `internal` types
  are visible to tests via `InternalsVisibleTo` in `Editor/AssemblyInfo.cs`.
- **`overrideReferences` hides Newtonsoft.** The runtime asmdef has
  `overrideReferences: true` (needed for SQLite DLLs). This means
  `Newtonsoft.Json.dll` must be listed explicitly in `precompiledReferences`
  — it won't resolve transitively. Same applies to the test asmdef.

### Available MCP tools

| Tool | Use for |
|---|---|
| `unity_console` `{"operation": "summary"}` | Quick overview: error/warning/log counts |
| `unity_console` `{"filter": "error"}` | See compile errors and runtime exceptions |
| `unity_console` `{"grep": "CS0246"}` | Search for specific error codes or patterns |
| `unity_console` `{"grep": "regex:error.*line \\d+"}` | Regex search (prefix with `regex:`) |
| `unity_console` `{"operation": "refresh"}` | Force `AssetDatabase.Refresh()` — triggers recompile |
| `unity_console` `{"operation": "clear"}` | Clear the log buffer |
| `unity_tests` `{"operation": "run"}` | Run all EditMode tests |
| `unity_tests` `{"operation": "run", "mode": "playmode"}` | Run PlayMode tests |
| `unity_tests` `{"operation": "run", "filter": "McpIntegration"}` | Run specific tests by name |
| `unity_tests` `{"operation": "results"}` | Get last run results (failures only by default) |
| `unity_tests` `{"operation": "results", "failures_only": false}` | See all results including passes |
| `theatre_status` | Server status, play mode, active scene |
| `scene_snapshot` | Budgeted overview of scene GameObjects |
| `scene_hierarchy` `{"operation": "list"}` | Navigate scene hierarchy |
| `scene_inspect` `{"path": "/Player"}` | Deep inspect a specific GameObject |

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
  every domain reload. Stateful data (watches, recordings, MCP session ID)
  persists via SessionState and SQLite.
- **Editor only**: Theatre runs in the Unity Editor, not in player builds.
- **No external MCP SDK**: MCP protocol (JSON-RPC 2.0 over Streamable
  HTTP) is implemented directly. The official C# MCP SDK requires ASP.NET
  Core which is incompatible with Unity.

## Unity API Rules (MANDATORY — load unity-api-pitfalls skill)

**Before writing ANY C# code in this project, load the `unity-api-pitfalls`
skill.** This is not optional. Every Unity 6 bug we've hit came from
violating rules in this skill.

The skill covers threading, API availability, assembly boundaries,
serialization, and deprecated APIs — all in one place. Key traps:

- `System.Text.Json` does not exist — use Newtonsoft
- `Object.FindObjectFromInstanceID` is internal — use `EditorUtility.InstanceIDToObject`
- `Object.FindObjectsOfType<T>()` is deprecated — use `FindObjectsByType<T>(FindObjectsSortMode)`
- ALL Unity APIs are main-thread-only — route handlers run on background threads
- `SessionState.*` / `TheatreConfig.Port` / `.EnabledGroups` — cache at startup
- Runtime assemblies cannot reference `UnityEditor.*` — use `#if UNITY_EDITOR`
- Newtonsoft serializes ALL public properties — use `[JsonIgnore]` on computed ones
- `ThreadAbortException` on domain reload — catch and exit cleanly

See `docs/unity-threading-idioms.md` for the extended reference with code examples.

## Code Style

- Namespace mirrors folder path: `Theatre` (core), `Theatre.Transport`,
  `Theatre.Stage` (runtime), `Theatre.Editor` (editor root),
  `Theatre.Editor.Tools.Scene`, `.Spatial`, `.Actions`, `.Watch` (tools),
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

## Agent Tracker
- Project ID: de744a41-ab2c-46aa-8d6d-a3fa89482c2e
- Project Name: theatre-unity
- Tracker URL: http://localhost:57328/mcp

When you complete a meaningful unit of work, post an update using the
`post_update` MCP tool with the project ID above. Use status "in-progress"
for normal progress, "blocked" if you hit an obstacle, or "error" for
failures. Include relevant tags for categorization.
