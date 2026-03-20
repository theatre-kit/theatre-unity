# Unity Theatre — Architecture

## One-Liner

Theatre for Unity is an all-C# MCP server running inside the Unity Editor,
giving AI agents spatial awareness of running games and programmatic control
over every Unity subsystem that agents cannot reliably hand-write.

## The In-Process Model

Unlike the Godot version (Rust sidecar + TCP bridge), Unity Theatre runs
entirely inside Unity's editor process. The MCP server is a C# class hosted
by an EditorWindow. No sidecar binary, no wire protocol between server and
engine, no serialization boundary. The server calls Unity APIs directly.

```
┌─────────────────────────────────────────────────────────────┐
│  Unity Editor Process                                       │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Theatre Package (single UPM package)                 │  │
│  │                                                       │  │
│  │  ┌─────────────┐  ┌──────────────┐  ┌─────────────┐  │  │
│  │  │  MCP Server  │  │  Stage Core   │  │  Director    │  │  │
│  │  │  (HTTP)      │  │  (Spatial)    │  │  (Mutation)  │  │  │
│  │  │              │  │              │  │              │  │  │
│  │  │  Transport   │  │  GameObject  │  │  Scene ops   │  │  │
│  │  │  Routing     │  │  ECS         │  │  Prefab ops  │  │  │
│  │  │  Tool Reg    │  │  Recording   │  │  Asset ops   │  │  │
│  │  │  Auth/Config │  │  Watches     │  │  Anim ops    │  │  │
│  │  └──────┬───────┘  │  Queries     │  │  Spatial ops │  │  │
│  │         │          └──────────────┘  │  Config ops  │  │  │
│  │         │                            └─────────────┘  │  │
│  │         │                                              │  │
│  │  ┌──────┴───────────────────────────────────────────┐  │  │
│  │  │  Editor UI Layer                                  │  │  │
│  │  │  EditorWindow (Theatre Panel)                     │  │  │
│  │  │  Scene View Gizmos (Handles/Gizmos)               │  │  │
│  │  │  Settings Provider                                │  │  │
│  │  └──────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│         ▲ Streamable HTTP (localhost:port)                   │
│         │                                                    │
└─────────┼───────────────────────────────────────────────────┘
          │
   ┌──────┴──────┐
   │  AI Agent    │
   │  (any MCP    │
   │   client)    │
   └─────────────┘
```

### Why In-Process Wins

| Concern | Godot (two-process) | Unity (in-process) |
|---|---|---|
| Latency | TCP round-trip per query | Direct method call |
| Serialization | Double: C# → JSON → Rust, Rust → JSON → C# | None internally; JSON only at MCP boundary |
| Deployment | Must build + deploy GDExtension binary | UPM package install, no native code |
| Debugging | Two debuggers, two log streams | Single Unity debugger, single Console |
| State sync | Snapshot-based, can drift | Always current — reads live engine state |
| Thread model | Main thread only in addon, async in server | EditorApplication callbacks, coroutines, or async/await on main thread |

### What We Lose

- **Isolation**: A crash in Theatre code crashes the editor. Defensive coding
  matters more. All public entry points must catch exceptions.
- **Language benefits**: No Rust memory safety or performance. C# is sufficient
  for editor tooling but we must be careful with GC pressure during frame
  capture.
- **Independent lifecycle**: Server can't outlive the editor. If Unity closes,
  the MCP server goes with it. This is fine for editor-only tooling.

---

## Package Structure

Single UPM package: `com.theatre.toolkit`

```
com.theatre.toolkit/
  package.json                    # UPM manifest (unity: "6000.0")
  README.md
  CHANGELOG.md
  LICENSE.md

  Runtime/
    com.theatre.toolkit.runtime.asmdef
    Core/
      TheatreServer.cs            # HTTP server lifecycle
      TheatreConfig.cs            # Tool group toggles, port, settings
      ToolRegistry.cs             # Dynamic tool registration based on config
      ToolGroup.cs                # Group enum + metadata
    Stage/
      GameObject/
        Snapshot.cs               # Scene snapshot builder
        Inspector.cs              # Deep GameObject inspection
        Queries.cs                # Nearest, radius, overlap, raycast, etc.
        Watches.cs                # Watch engine — conditions, subscriptions
        Actions.cs                # Teleport, set property, pause, step
        HierarchyWalker.cs        # Transform tree traversal
        Delta.cs                  # Change detection between snapshots
      ECS/
        WorldSnapshot.cs          # World/archetype overview
        EntityInspector.cs        # Component data read
        EntityQueries.cs          # Spatial queries over entity positions
        EntityActions.cs          # Component add/remove/modify
        SystemInspector.cs        # System list, ordering, enabled state
      Recording/
        Recorder.cs               # Frame capture to SQLite
        RecordingDb.cs            # SQLite schema, read/write
        ClipAnalysis.cs           # Query range, diff, markers
        Timeline.cs               # Clip management, metadata
      Spatial/
        SpatialIndex.cs           # R-tree or grid for spatial queries
        BearingCalculator.cs      # Bearing/distance math
        Clustering.cs             # Node grouping for summaries
        TokenBudget.cs            # Response size management
    Director/
      Scenes/
        SceneOperations.cs        # Create, load, unload, merge, move objects
      Prefabs/
        PrefabOperations.cs       # Create, instantiate, override, unpack, variant
      Assets/
        MaterialOperations.cs     # Create/modify materials, set shader properties
        ScriptableObjectOps.cs    # Create SO instances from type
        TextureOperations.cs      # Import, configure, atlas management
        PhysicsMaterialOps.cs     # Create/modify PhysicMaterial
        AudioMixerOperations.cs   # Create/modify AudioMixer groups, snapshots
        RenderPipelineOps.cs      # URP/HDRP asset creation and configuration
        AddressableOperations.cs  # Group management, entry configuration
        SpriteAtlasOperations.cs  # Create/configure sprite atlases
      Animation/
        ClipOperations.cs         # Create clips, add curves, set keyframes
        ControllerOperations.cs   # Create AnimatorController, states, transitions
        BlendTreeOperations.cs    # Create/configure blend trees
        TimelineOperations.cs     # Create Timeline assets, tracks, clips
      Spatial/
        TilemapOperations.cs      # Paint, fill, clear, rule tile management
        TerrainOperations.cs      # Heightmap, detail, tree placement
        NavMeshOperations.cs      # Bake, modify areas, set agent types
        ProBuilderOperations.cs   # Mesh creation and modification
      Input/
        InputActionOperations.cs  # Create/modify Input System action maps
      Config/
        QualityOperations.cs      # Quality settings
        LightingOperations.cs     # Lighting, lightmap baking
        ProjectSettingsOps.cs     # Physics, time, player settings
        BuildProfileOperations.cs # Build profile management
    Transport/
      HttpTransport.cs            # Streamable HTTP server (MCP transport)
      McpRouter.cs                # JSON-RPC dispatch to tool handlers
      McpTypes.cs                 # MCP protocol types (request, response, error)
      StreamManager.cs            # SSE stream management for notifications

  Editor/
    com.theatre.toolkit.editor.asmdef
    TheatreWindow.cs              # Main EditorWindow — panel UI
    TheatreSettings.cs            # SettingsProvider for Project Settings
    SceneOverlay.cs               # Scene view gizmo rendering
    ActivityFeed.cs               # Agent activity log display
    RecordingTimeline.cs          # Timeline scrubber UI
    ToolToggleUI.cs               # Group/tool enable/disable UI
    GizmoRenderer.cs              # Draws query results, watch regions, rays
    TheatreStyles.cs              # USS styles, UI constants
    TheatreIcons.cs               # Icon assets for toolbar/gizmos

  Tests/
    Editor/
      com.theatre.toolkit.editor.tests.asmdef
    Runtime/
      com.theatre.toolkit.runtime.tests.asmdef

  Samples~/
    BasicSetup/                   # Minimal scene with Theatre enabled
    SpatialDebugging/             # Example spatial debugging session
```

### Assembly Definitions

| Assembly | Contains | References |
|---|---|---|
| `com.theatre.toolkit.runtime` | Core, Stage, Director, Transport | — |
| `com.theatre.toolkit.editor` | EditorWindow, gizmos, settings | runtime |
| `com.theatre.toolkit.editor.tests` | Editor test suite | runtime, editor |
| `com.theatre.toolkit.runtime.tests` | Runtime test suite | runtime |

The Runtime assembly contains all MCP, Stage, and Director logic. Despite the
name "Runtime", this code runs in the editor — the assembly name follows Unity
convention where "Runtime" means "not editor-only API dependencies". Director
operations that require `UnityEditor` APIs (PrefabUtility, AssetDatabase, etc.)
use `#if UNITY_EDITOR` guards or are in the Editor assembly.

**Correction**: Director operations heavily depend on `UnityEditor` APIs. The
split should be:

| Assembly | Contains |
|---|---|
| `com.theatre.toolkit.runtime` | Core server, transport, Stage spatial logic, recording |
| `com.theatre.toolkit.editor` | Director operations, EditorWindow, gizmos, settings, tool registration for editor-only tools |

Stage tools that only read scene state (snapshot, query, inspect) can live in
Runtime. Director tools that mutate assets (PrefabUtility, AssetDatabase) must
live in Editor. The tool registry dynamically registers tools from both
assemblies.

---

## Tool Namespace & Toggle System

### Tool Groups

Tools are organized into groups. Each group can be enabled/disabled
independently. The MCP server only announces enabled tools in its
`tools/list` response.

```csharp
[Flags]
public enum ToolGroup
{
    None             = 0,

    // Stage — GameObject
    StageGameObject  = 1 << 0,   // snapshot, inspect, hierarchy
    StageQuery       = 1 << 1,   // nearest, radius, overlap, raycast
    StageWatch       = 1 << 2,   // watch create/remove/check
    StageAction      = 1 << 3,   // teleport, set property, pause, step
    StageRecording   = 1 << 4,   // record, clip analysis

    // Stage — ECS
    ECSWorld         = 1 << 5,   // world snapshot, archetype list
    ECSEntity        = 1 << 6,   // entity inspect, component read
    ECSQuery         = 1 << 7,   // entity spatial queries
    ECSAction        = 1 << 8,   // entity/component mutation

    // Director
    DirectorScene    = 1 << 9,   // scene create/load/merge
    DirectorPrefab   = 1 << 10,  // prefab CRUD
    DirectorAsset    = 1 << 11,  // materials, SOs, textures, etc.
    DirectorAnim     = 1 << 12,  // animation clips, controllers, timeline
    DirectorSpatial  = 1 << 13,  // tilemap, terrain, navmesh, probuilder
    DirectorInput    = 1 << 14,  // input action maps
    DirectorConfig   = 1 << 15,  // project/quality/lighting settings

    // Presets
    StageAll         = StageGameObject | StageQuery | StageWatch
                     | StageAction | StageRecording,
    ECSAll           = ECSWorld | ECSEntity | ECSQuery | ECSAction,
    DirectorAll      = DirectorScene | DirectorPrefab | DirectorAsset
                     | DirectorAnim | DirectorSpatial | DirectorInput
                     | DirectorConfig,

    Everything       = StageAll | ECSAll | DirectorAll,

    // Common configurations
    GameObjectProject = StageAll | DirectorAll,
    ECSProject        = ECSAll | DirectorAll,
}
```

### Per-Tool Override

Beyond group toggles, individual tools can be disabled:

```json
{
  "theatre": {
    "port": 9078,
    "enabled_groups": ["StageGameObject", "StageQuery", "DirectorPrefab"],
    "disabled_tools": ["stage_action"]
  }
}
```

The server resolves: `(group enabled) AND (tool not individually disabled)`.

### Dynamic Registration

When config changes (via the Theatre editor panel or settings file), the
server sends an MCP `notifications/tools/list_changed` notification. The
agent re-fetches the tool list and sees the updated set.

---

## Transport — Streamable HTTP

### Why Not stdio

Unity's editor process owns stdout/stderr for its own logging and the Console
window. There is no clean way to hijack stdout for MCP protocol traffic
without interfering with Unity's internals. Streamable HTTP avoids this
entirely.

### Server Lifecycle

1. When Unity opens a project with Theatre installed, the `TheatreServer`
   initializes during editor load (`[InitializeOnLoad]` or
   `EditorApplication.delayCall`).
2. Server binds to `http://localhost:{port}` (default 9078, configurable).
3. Server stays alive for the editor session. Survives domain reloads
   (uses `[SerializeField]` state preservation or `SessionState`).
4. On editor quit, server shuts down gracefully.

### MCP Client Configuration

Agents connect via HTTP. Example `.mcp.json`:

```json
{
  "mcpServers": {
    "theatre": {
      "type": "streamable-http",
      "url": "http://localhost:9078/mcp"
    }
  }
}
```

No command to spawn — the server is already running inside Unity.

### Endpoints

| Endpoint | Method | Purpose |
|---|---|---|
| `/mcp` | POST | MCP JSON-RPC requests (tools/call, tools/list, etc.) |
| `/mcp` | GET | SSE stream for server-initiated notifications (watch triggers, recording events) |
| `/health` | GET | Server status, enabled groups, connection check |

### Domain Reload Survival

Unity reloads the C# domain on script recompilation. All managed state is
destroyed — threads, HttpListener, in-memory data, SQLite connections.

**Strategy: Fast restart with state persistence.**

The HTTP server is inherently stateless per-request (each MCP `tools/call`
is an independent HTTP POST). Only SSE notification streams and in-memory
caches require recovery.

1. **HttpListener**: `[InitializeOnLoad]` static constructor re-creates
   and re-binds on every domain reload. Typical restart gap: ~50-100ms.
   MCP clients see a brief connection refused and retry.

2. **Watches**: Serialized to `SessionState` (key-value store that survives
   domain reloads but not editor restarts). On reload, watch definitions
   are restored and re-activated. Brief monitoring gap during reload — no
   watch triggers fire for the ~100ms window. Acceptable.

3. **Spatial index**: Rebuilt from live scene state on the first query
   after reload. This is derived data — no persistence needed.

4. **Active recording**: SQLite file on disk is unaffected. Recording
   metadata (current clip ID, frame counter, tracking config) persisted
   to `SessionState`. SQLite connection reopens, recording resumes.
   May lose 1-2 frames during the reload gap.

5. **SSE notification streams**: Drop on reload. Clients reconnect via
   GET and receive any queued notifications. Watches that triggered during
   the gap are re-evaluated on reconnect.

6. **Agent activity log**: Persisted to `SessionState` as a ring buffer.
   Survives reload.

**Enter Play Mode Settings**: Some projects disable domain reload on play
mode entry for faster iteration. Theatre handles this gracefully — if no
domain reload occurs, no restart needed. The `PlayModeStateChanged` event
still fires and Theatre uses it to switch between edit/play mode behavior.

---

## Threading Model

### The Problem

`HttpListener` receives requests on thread pool threads. All Unity APIs
(`Transform.position`, `Physics.Raycast`, `AssetDatabase.CreateAsset`, etc.)
must be called from the main thread. Every tool handler must marshal its
work to the main thread and return the result to the HTTP response thread.

### Solution: EditorCoroutine Dispatch

HTTP request handlers use `EditorCoroutineUtility` to schedule work on
Unity's main thread:

```csharp
// On thread pool thread (HttpListener callback)
async Task HandleRequest(HttpListenerContext context)
{
    var request = ParseMcpRequest(context.Request);

    // Marshal to main thread
    object result = null;
    Exception error = null;
    var done = new ManualResetEventSlim(false);

    EditorCoroutineUtility.StartCoroutineOwnerless(
        ExecuteOnMainThread(request, r => result = r, e => error = e, done));

    // Block HTTP thread until main thread completes
    done.Wait();

    // Back on thread pool thread — send response
    if (error != null)
        SendErrorResponse(context.Response, error);
    else
        SendSuccessResponse(context.Response, result);
}
```

**Latency**: One editor frame (~16ms at 60fps editor refresh) for the
main thread to pick up the work. Acceptable for MCP tool calls.

**Concurrency**: Multiple HTTP requests can queue work simultaneously.
The main thread processes them sequentially in `EditorApplication.update`
order. No parallel Unity API access — this is intentional and safe.

**Long operations**: Some Director operations (NavMesh bake, lightmap bake)
take multiple seconds. These use Unity's async/progress APIs and report
progress. The HTTP response waits until completion or returns a job ID
for polling (TBD based on which operations need this).

### Thread Safety Rules

- **Never** access Unity APIs from the HTTP listener thread.
- **Never** hold locks across the main thread dispatch boundary.
- Tool handler result objects are built on the main thread, then serialized
  to JSON on the main thread before being passed back to the HTTP thread.
  No shared mutable state between threads.
- The only cross-thread data is the immutable JSON response string.

---

## MCP Protocol Implementation

### No External SDK

The official C# MCP SDK (`ModelContextProtocol`) requires ASP.NET Core
for Streamable HTTP transport, which is incompatible with Unity. Instead
of fighting this dependency, Theatre implements the MCP protocol directly.

The MCP protocol surface we need is small:

| Protocol Feature | Implementation |
|---|---|
| JSON-RPC 2.0 dispatch | Custom router (~200 lines) |
| `initialize` handshake | Capability negotiation |
| `tools/list` | Dynamic tool registry |
| `tools/call` | Route to tool handlers |
| `notifications/*` | SSE push via GET endpoint |
| Streamable HTTP framing | HttpListener + SSE formatting |

### Protocol Types

~10 C# types cover the full MCP surface:

- `JsonRpcRequest`, `JsonRpcResponse`, `JsonRpcError`
- `McpInitializeParams`, `McpInitializeResult`, `McpCapabilities`
- `McpToolDefinition`, `McpToolParameter` (JSON Schema subset)
- `McpToolCallParams`, `McpToolCallResult`
- `McpNotification`

These are simple POCOs with `System.Text.Json` serialization. No code
generation, no reflection. Hand-written for clarity and Unity compatibility.

### Streamable HTTP Implementation

Based on `System.Net.HttpListener` (available in Unity's .NET BCL):

**POST `/mcp`**: Receives JSON-RPC requests. Each request is dispatched
to the tool registry, executed via EditorCoroutine on the main thread,
and the JSON-RPC response is written to the HTTP response body.

**GET `/mcp`**: Opens an SSE (Server-Sent Events) stream. Theatre pushes
notifications (watch triggers, recording events, tool list changes) as
SSE `data:` frames. The response uses `Transfer-Encoding: chunked` with
`Content-Type: text/event-stream`.

**GET `/health`**: Returns server status, enabled tool groups, Unity
version, play mode state. Useful for diagnostics and MCP client discovery.

---

## Serialization Strategy

### Component Property Reading

Theatre needs to read arbitrary component properties for `scene_inspect`
and recording. Two approaches depending on context:

**Edit Mode — SerializedObject/SerializedProperty**:

```csharp
var so = new SerializedObject(component);
var prop = so.GetIterator();
while (prop.NextVisible(enterChildren: true))
{
    EmitProperty(prop.name, prop.propertyType, GetValue(prop));
}
```

This traverses all serialized fields generically — works for any
component, including custom MonoBehaviours. Handles arrays, nested
structs, object references (emitted as asset path or instance_id).

**Play Mode — Reflection + known types**:

For performance during recording at 60fps, we use a hybrid:
- Known Unity types (Transform, Rigidbody, etc.) use direct property
  access via cached delegates — no reflection overhead.
- Custom MonoBehaviour fields use cached `FieldInfo`/`PropertyInfo`
  from the first access — subsequent reads are fast.
- IL2CPP note: not relevant since we're editor-only, where Mono is
  always the scripting backend.

### Component Property Writing

`scene_op:set_component` and `action:set_property` use
`SerializedObject/SerializedProperty` for type-safe writes with Undo support:

```csharp
var so = new SerializedObject(component);
var prop = so.FindProperty(propertyName);
SetValueByType(prop, value);
so.ApplyModifiedProperties(); // Registers Undo automatically
```

### Type Mapping

| SerializedPropertyType | JSON Type | Example |
|---|---|---|
| Integer | number | `42` |
| Float | number | `3.14` |
| Boolean | boolean | `true` |
| String | string | `"hello"` |
| Vector2/3/4 | array | `[1, 2, 3]` |
| Quaternion | array | `[0, 0, 0, 1]` |
| Color | array | `[1, 0, 0, 1]` |
| Enum | string | `"continuous"` |
| ObjectReference | object | `{ "instance_id": 123, "path": "Assets/..." }` |
| ArrayOf* | array | `[...]` |
| Nested struct | object | `{ "x": 1, "y": 2 }` |

### ECS Component Reading

ECS components are unmanaged structs. Reading them requires the Entities
API:

```csharp
var em = World.DefaultGameObjectObjectWorld.EntityManager;
if (em.HasComponent<LocalTransform>(entity))
{
    var transform = em.GetComponentData<LocalTransform>(entity);
    // transform.Position, transform.Rotation, transform.Scale
}
```

For generic/dynamic component access (when the type isn't known at compile
time), use `EntityManager.GetComponentDataRaw` with `TypeManager` lookups.
This requires knowing the component type's `TypeIndex` — resolved from
the type name string via `TypeManager.GetTypeIndexFromStableTypeHash` or
assembly scanning.

---

## 2D vs 3D Physics

Unity has separate physics systems: `UnityEngine.Physics` (3D/PhysX) and
`UnityEngine.Physics2D` (Box2D). Spatial queries need to target the right
one.

### Detection and Configuration

**Project-level default**: Set in Theatre project settings. Auto-detected
on first run by checking the project's default scene template (2D vs 3D)
or the presence of 2D/3D components in the active scene.

**Per-query override**: Every spatial query tool accepts an optional
`physics` parameter:

```json
{ "operation": "raycast", "physics": "3d", ... }
{ "operation": "overlap", "physics": "2d", ... }
```

If omitted, uses the project default.

### API Mapping

| Operation | 3D API | 2D API |
|---|---|---|
| Raycast | `Physics.Raycast` | `Physics2D.Raycast` |
| Overlap sphere/circle | `Physics.OverlapSphere` | `Physics2D.OverlapCircle` |
| Overlap box | `Physics.OverlapBox` | `Physics2D.OverlapBox` |
| Overlap capsule | `Physics.OverlapCapsule` | `Physics2D.OverlapCapsule` |
| Linecast | `Physics.Linecast` | `Physics2D.Linecast` |

**Mixed projects**: Projects using both 2D and 3D physics (e.g., 3D world
with 2D UI collision) can issue queries against either system. The agent
specifies which per-call.

### Coordinate Handling

2D physics uses `Vector2` (XY plane). Theatre adapts:
- 2D positions reported as `[x, y]` (2-element array)
- 3D positions reported as `[x, y, z]` (3-element array)
- Agents can tell from array length which coordinate space they're in

---

## Prefab Mode

When Unity enters Prefab Stage (double-click a prefab to edit in
isolation), Theatre adapts:

- **Stage tools** scope to the prefab contents. `scene_snapshot` returns
  the prefab's isolated scene. `scene_hierarchy` shows the prefab root
  and its children. Spatial queries operate within the prefab's space.

- **Director tools** continue to work on project assets. Prefab-specific
  operations (`apply_overrides`, `revert_overrides`) are available.

- **Exiting prefab mode** re-scopes Stage to the main scene(s).

- **Detection**: Theatre listens to `PrefabStage.prefabStageOpened` and
  `PrefabStage.prefabStageClosing` events.

Responses include a `context` field indicating the current editing context:

```json
{
  "context": "prefab",
  "prefab_path": "Assets/Prefabs/Enemy.prefab",
  ...
}
```

Or `"context": "scene"` for normal scene editing.

---

## Multi-Scene Addressing

Unity supports multiple scenes loaded simultaneously (additive loading).

### Path Format

- **Single scene**: `/Path/To/Object` (scene name omitted)
- **Multi-scene**: `SceneName:/Path/To/Object`
- **Active scene objects**: `/Path/To/Object` resolves in the active scene

### Hierarchy Root

`scene_hierarchy:list` at root level returns loaded scenes as top-level
entries:

```json
{
  "results": [
    { "scene": "MainLevel", "root_count": 45, "active": true },
    { "scene": "UI", "root_count": 3, "active": false },
    { "scene": "AudioManager", "root_count": 1, "active": false }
  ]
}
```

### Snapshot Scope

`scene_snapshot` accepts a `scene` parameter:
- Omitted: all loaded scenes
- `"scene": "MainLevel"`: only that scene
- `"scene": ["MainLevel", "UI"]`: specific scenes

---

## State Management

### Play Mode vs Edit Mode

Theatre operates in both modes but the available data differs:

| State | Stage | Director |
|---|---|---|
| **Edit Mode** | Scene hierarchy visible, transforms readable, no physics, no velocities, no frame stepping | Full access — all asset/scene operations |
| **Play Mode** | Full spatial awareness — physics queries, velocities, frame stepping, recording | Limited — runtime objects mutable, but asset creation may require leaving play mode |
| **Transitioning** | Brief unavailability during mode switch | Queued operations execute after transition |

The server reports current mode in status responses so agents can adapt.

### Undo Integration

All Director operations that modify assets or scene state go through Unity's
Undo system (`Undo.RecordObject`, `Undo.RegisterCreatedObjectUndo`, etc.).
This means:

- Every agent mutation is undoable by the human (Ctrl+Z)
- Operations are grouped into logical undo steps
- The human maintains full control — they can review and revert any agent action

### Error Handling

All public entry points (tool handlers) catch exceptions and return structured
MCP errors. Theatre code never crashes the editor. Defensive pattern:

```csharp
try
{
    var result = ExecuteToolLogic(parameters);
    return McpResponse.Success(result);
}
catch (GameObjectNotFoundException e)
{
    return McpResponse.Error("gameobject_not_found", e.Message,
        suggestion: "Use hierarchy_find to search for matching GameObjects");
}
catch (Exception e)
{
    Debug.LogException(e);
    return McpResponse.InternalError("Unexpected error in tool execution");
}
```

---

## Dependencies

| Dependency | Purpose | Source |
|---|---|---|
| Microsoft.Data.Sqlite | Recording storage | NuGet (via UPM or vendored) |
| System.Net.HttpListener or custom | HTTP server | .NET BCL |
| System.Text.Json | JSON serialization | .NET BCL (Unity 6 / .NET Standard 2.1) |
| Unity.Mathematics | Spatial math (optional) | UPM (com.unity.mathematics) |
| Unity.Collections | NativeArray for batch queries (optional) | UPM (com.unity.collections) |

No external MCP SDK — the MCP protocol is simple enough (JSON-RPC 2.0 over
HTTP) to implement directly. This avoids dependency on a C# MCP library that
may not be Unity-compatible or well-maintained.

### Optional Package Dependencies

Director tools for specific subsystems gracefully degrade when the target
package is not installed:

| Director Tool Group | Required Package |
|---|---|
| Tilemap operations | `com.unity.2d.tilemap` (built-in) |
| ProBuilder operations | `com.unity.probuilder` |
| Timeline operations | `com.unity.timeline` |
| Addressable operations | `com.unity.addressables` |
| Input System operations | `com.unity.inputsystem` |
| ECS tools | `com.unity.entities` |

Tools for uninstalled packages are automatically hidden from the MCP tool
list. The tool registry detects installed packages via `PackageManager.Client`
or assembly presence checks.

---

## Coordinate System

Unity uses a **left-handed Y-up** coordinate system. All spatial data in
Theatre responses uses Unity's native coordinates — no conversion, no
abstraction. Agents receive positions, rotations, and directions exactly as
Unity represents them.

| Axis | Direction |
|---|---|
| X | Right |
| Y | Up |
| Z | Forward |

Rotations are reported as Euler angles in degrees (matching
`Transform.eulerAngles`) and optionally as quaternions. Distances are in
Unity world units.

---

## SQLite Packaging

Theatre uses native SQLite for recording storage via `Microsoft.Data.Sqlite`.

### Native Binary Bundling

The UPM package includes pre-built `sqlite3` native libraries under
`Plugins/`:

```
com.theatre.toolkit/
  Plugins/
    x86_64/
      sqlite3.dll           # Windows
      sqlite3.bundle        # macOS (universal binary)
      libsqlite3.so         # Linux
```

Each native plugin has a `.meta` file with platform import settings
(CPU architecture, OS). Unity handles loading the correct binary for
the host platform.

### Recording File Location

Recordings are stored in `Library/Theatre/` by default — inside Unity's
Library folder which is gitignored, local to the machine, and survives
editor restarts. Configurable in project settings.

### Database Schema

One SQLite database per recording clip. Schema covers:
- Frame table: frame number, timestamp, delta-compressed snapshot
- Marker table: frame number, label, metadata
- Metadata table: clip info, tracked paths, components, start/end time

---

## Batch Operations

Director supports batching multiple operations into a single MCP tool call
via the `batch` meta-tool:

```json
{
  "tool": "batch",
  "operations": [
    { "tool": "scene_op", "params": { "operation": "create_gameobject", "name": "Enemy", ... } },
    { "tool": "scene_op", "params": { "operation": "set_component", "path": "/Enemy", "component": "Health", ... } },
    { "tool": "prefab_op", "params": { "operation": "create_prefab", "source_path": "/Enemy", ... } }
  ]
}
```

### Semantics

- **Undo grouping**: All operations in a batch form a single undo step.
  One Ctrl+Z reverts the entire batch.
- **Sequential execution**: Operations execute in order. Later operations
  can reference objects created by earlier ones (e.g., create then modify).
- **All-or-nothing**: If any operation fails, all preceding operations in
  the batch are rolled back via `Undo.RevertAllInCurrentGroup()`. The
  response indicates which operation failed and why.
- **Asset refresh**: `AssetDatabase.StartAssetEditing()` wraps the entire
  batch — one import pass at the end, not per-operation.

### Response

```json
{
  "result": "ok",
  "operation_count": 3,
  "results": [
    { "result": "ok", "operation": "create_gameobject", "path": "/Enemy", ... },
    { "result": "ok", "operation": "set_component", ... },
    { "result": "ok", "operation": "create_prefab", ... }
  ]
}
```

---

## Performance Considerations

### Frame Capture (Recording at 60fps)

Recording captures scene state every physics frame. Key concerns:

- **GC pressure**: Avoid per-frame allocations. Use object pooling for
  snapshot buffers. Reuse `StringBuilder` instances for JSON. Pre-allocate
  component property arrays based on tracked component count.
- **Delta compression**: Only write changed properties to SQLite. Compare
  against the previous frame and skip unchanged values. Typical frame
  write is 10-50 properties, not the full scene.
- **Batch SQLite writes**: Buffer frames in memory and flush to SQLite
  every N frames (e.g., every 10 frames). Use WAL mode for concurrent
  read/write without blocking.
- **Tracked subset**: Recording doesn't capture the entire scene — only
  objects matching the tracking filter (paths, component types). A typical
  recording tracks 10-50 objects, not hundreds.

### Spatial Index

- **R-tree** (via a C# implementation or custom) for 3D nearest/radius
  queries. Rebuilt on demand from current scene state.
- **Grid hash** for 2D scenes — simpler, lower overhead for uniform
  distributions.
- Index is rebuilt when the scene changes significantly (objects
  created/destroyed) and updated incrementally for position changes.

### JSON Serialization

- Use `System.Text.Json` source generators where possible to avoid
  reflection overhead (Unity 6 supports .NET Standard 2.1 which is
  sufficient for source generators with the right package versions).
- For hot paths (recording, spatial queries), use `Utf8JsonWriter`
  directly instead of serializing through POCOs.

---

## Testing Strategy

### Test Layers

| Layer | Framework | Runs In | What It Tests |
|---|---|---|---|
| Unit tests | Unity Test Framework (EditMode) | Editor, no scene | JSON serialization, spatial math, protocol parsing, token budgeting, delta computation |
| Component tests | Unity Test Framework (EditMode) | Editor, test scenes | SerializedProperty traversal, hierarchy walking, component inspection |
| Integration tests | Unity Test Framework (PlayMode) | Editor, play mode | Physics queries, recording, watches, frame stepping |
| MCP integration | Unity Test Framework (EditMode) + HttpClient | Editor | Full HTTP round-trip: send JSON-RPC request, verify response |
| Director tests | Unity Test Framework (EditMode) | Editor | Asset creation, prefab operations, scene manipulation — verify via AssetDatabase |

### Mock MCP Client

A test helper class that sends HTTP requests to Theatre's localhost server
and validates responses:

```csharp
var client = new TheatreTestClient("http://localhost:9078");
var response = await client.CallTool("scene_snapshot", new { budget = 500 });
Assert.IsNotNull(response["objects"]);
Assert.AreEqual("ok", response["result"]?.ToString() ?? "ok");
```

### Test Scene Fixtures

Pre-built test scenes in `Tests/` with known object layouts for
deterministic spatial query results:

- `TestScene_3D.unity`: Grid of cubes with known positions
- `TestScene_2D.unity`: Tilemap with known tile layout
- `TestScene_Hierarchy.unity`: Deep hierarchy for traversal tests
- `TestScene_Prefabs.unity`: Prefab instances with overrides

### Running Tests

All tests run from the Unity Editor GUI:
**Window > General > Test Runner > EditMode > Run All**

### CI

Unity 6 requires a **Pro license** for batch mode (`-batchmode`). The
`com.unity.editor.headless` entitlement is not included in Personal.

**With Unity Pro:**
```bash
xvfb-run unity -batchmode -projectPath . -runTests -testResults results.xml -quit
```

**Without Pro:** CI requires GameCI Docker images or similar workarounds.
See [GameCI docs](https://game.ci/docs/) for containerized Unity builds.

For local development, use the Editor GUI test runner.
