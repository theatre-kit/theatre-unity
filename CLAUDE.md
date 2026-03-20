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

## Build & Test

```bash
# Open in Unity 6 (6000.0+)
# Package compiles automatically when Unity opens TestProject/

# Run tests via Unity batch mode
unity -batchmode -projectPath TestProject -runTests \
  -testResults results.xml -quit

# Quick health check (server starts on editor load)
curl http://localhost:9078/health
```

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

## Code Style

- Namespace: `Theatre` (runtime), `Theatre.Editor` (editor),
  `Theatre.Tests.Editor` (tests)
- Unity 6 / .NET Standard 2.1
- `System.Text.Json` for JSON serialization (no Newtonsoft)
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
