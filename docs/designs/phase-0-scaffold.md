# Design: Phase 0 — Scaffold

## Overview

Set up the project skeleton: Unity 6 test harness project, UPM package
with assembly definitions, HttpListener server that starts on editor load
and survives domain reloads, and a `/health` endpoint. Also create CLAUDE.md
with project guidelines.

No MCP protocol, no tools, no game logic. Just proof that an HTTP server
can live inside the Unity Editor and respond to requests.

**Exit criteria**: `curl http://localhost:9078/health` returns a JSON
response from inside the Unity Editor.

---

## Implementation Units

### Unit 1: Unity Test Harness Project

**Path**: project root

Create a minimal Unity 6 project that serves as both the development
workspace and the test harness. The UPM package lives inside it as a
local package.

```
theatre-unity/
  TestProject/
    Assets/
      Scenes/
        SampleScene.unity       # Default scene
    Packages/
      manifest.json             # References local package
    ProjectSettings/
      ProjectSettings.asset     # Unity 6 defaults
  Packages/
    com.theatre.toolkit/        # The UPM package (Unit 2)
  docs/                         # Already exists
  ROADMAP.md                    # Already exists
  CLAUDE.md                     # Unit 8
```

**`TestProject/Packages/manifest.json`** references the local package:

```json
{
  "dependencies": {
    "com.theatre.toolkit": "file:../../Packages/com.theatre.toolkit"
  }
}
```

**Implementation Notes**:
- Create the Unity project via `unity -batchmode -createProject TestProject -quit`
  or manually. The project itself is minimal — just needs to load the package.
- The `Packages/com.theatre.toolkit/` path is outside TestProject so it can
  be referenced by multiple test projects later. The relative path in
  manifest.json points from `TestProject/Packages/` up to `Packages/`.

**Acceptance Criteria**:
- [ ] `TestProject/` opens in Unity 6 without errors
- [ ] The `com.theatre.toolkit` package appears in Unity's Package Manager
- [ ] No compilation errors

---

### Unit 2: UPM Package Skeleton

**Path**: `Packages/com.theatre.toolkit/`

The package structure with assembly definitions but no real code yet.

**`Packages/com.theatre.toolkit/package.json`**:

```json
{
  "name": "com.theatre.toolkit",
  "version": "0.0.1",
  "displayName": "Theatre",
  "description": "AI agent toolkit for Unity — spatial awareness and programmatic control via MCP.",
  "unity": "6000.0",
  "author": {
    "name": "Theatre",
    "url": "https://github.com/theatre-kit/theatre-unity"
  },
  "license": "MIT",
  "keywords": ["ai", "mcp", "agent", "spatial", "debug"],
  "dependencies": {}
}
```

**`Packages/com.theatre.toolkit/Runtime/com.theatre.toolkit.runtime.asmdef`**:

```json
{
  "name": "com.theatre.toolkit.runtime",
  "rootNamespace": "Theatre",
  "references": [],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

**`Packages/com.theatre.toolkit/Editor/com.theatre.toolkit.editor.asmdef`**:

```json
{
  "name": "com.theatre.toolkit.editor",
  "rootNamespace": "Theatre.Editor",
  "references": ["com.theatre.toolkit.runtime"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

**`Packages/com.theatre.toolkit/Tests/Editor/com.theatre.toolkit.editor.tests.asmdef`**:

```json
{
  "name": "com.theatre.toolkit.editor.tests",
  "rootNamespace": "Theatre.Tests.Editor",
  "references": [
    "com.theatre.toolkit.runtime",
    "com.theatre.toolkit.editor",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner"
  ],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

**Directory layout** (empty folders with placeholder `.cs` files where needed):

```
com.theatre.toolkit/
  package.json
  README.md
  CHANGELOG.md
  LICENSE.md
  Runtime/
    com.theatre.toolkit.runtime.asmdef
    Core/                       # TheatreServer, TheatreConfig, ToolRegistry
    Transport/                  # HttpTransport, McpRouter, McpTypes
    Stage/
      GameObject/
      ECS/
      Recording/
      Spatial/
    Director/
      Scenes/
      Prefabs/
      Assets/
      Animation/
      Spatial/
      Input/
      Config/
  Editor/
    com.theatre.toolkit.editor.asmdef
  Tests/
    Editor/
      com.theatre.toolkit.editor.tests.asmdef
```

**Implementation Notes**:
- Empty directories can have a `.gitkeep` or the first `.cs` file from
  subsequent units will create them.
- `README.md` is a one-liner ("Theatre for Unity — see docs/").
- `CHANGELOG.md` starts with `## [0.0.1] - Unreleased`.
- `LICENSE.md` is MIT.

**Acceptance Criteria**:
- [ ] Package compiles with zero errors in Unity 6
- [ ] Both assemblies (runtime + editor) appear in Assembly Definition list
- [ ] Test assembly compiles and appears in Test Runner

---

### Unit 3: TheatreConfig

**File**: `Packages/com.theatre.toolkit/Runtime/Core/TheatreConfig.cs`

Static configuration for the server. Minimal for Phase 0 — just port.
Later phases add tool groups, per-tool overrides, etc.

```csharp
namespace Theatre
{
    /// <summary>
    /// Server configuration. Phase 0: port only.
    /// Later phases add ToolGroup flags, disabled tools, recording settings.
    /// </summary>
    public static class TheatreConfig
    {
        /// <summary>
        /// HTTP server port. Default 9078.
        /// Read from SessionState on domain reload, falls back to default.
        /// </summary>
        public static int Port
        {
            get => UnityEditor.SessionState.GetInt("Theatre.Port", DefaultPort);
            set => UnityEditor.SessionState.SetInt("Theatre.Port", value);
        }

        public const int DefaultPort = 9078;

        /// <summary>
        /// Prefix for HttpListener. Derived from Port.
        /// </summary>
        public static string HttpPrefix => $"http://localhost:{Port}/";
    }
}
```

**Implementation Notes**:
- Uses `SessionState` so port survives domain reloads but not editor restarts.
- `TheatreConfig` is static for now. Later phases may move to a
  ScriptableObject-backed `SettingsProvider` for project-level persistence.
- This file references `UnityEditor.SessionState`. Since it's in the
  Runtime assembly, wrap with `#if UNITY_EDITOR`. All Theatre code runs
  in the editor anyway — this guard prevents build errors if someone
  accidentally includes the runtime asmdef in a player build.

**Acceptance Criteria**:
- [ ] `TheatreConfig.Port` returns 9078 by default
- [ ] Setting `TheatreConfig.Port = 9999` persists across domain reload
- [ ] `TheatreConfig.HttpPrefix` returns `"http://localhost:9078/"`

---

### Unit 4: HttpTransport

**File**: `Packages/com.theatre.toolkit/Runtime/Transport/HttpTransport.cs`

The HTTP server lifecycle. Owns the `HttpListener`, runs the accept loop
on a thread pool thread, routes requests by path and method.

```csharp
using System;
using System.Net;
using System.Text;
using System.Threading;

namespace Theatre.Transport
{
    /// <summary>
    /// Lightweight HTTP server wrapping System.Net.HttpListener.
    /// Runs the accept loop on a background thread.
    /// Request handlers execute on the calling thread (background) —
    /// callers must marshal to the main thread if Unity APIs are needed.
    /// </summary>
    public sealed class HttpTransport : IDisposable
    {
        private HttpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private Action<HttpListenerContext> _requestHandler;

        /// <summary>
        /// Whether the server is currently listening.
        /// </summary>
        public bool IsListening => _running && (_listener?.IsListening ?? false);

        /// <summary>
        /// The prefix the server is bound to (e.g., "http://localhost:9078/").
        /// </summary>
        public string Prefix { get; private set; }

        /// <summary>
        /// Start listening on the given prefix.
        /// </summary>
        /// <param name="prefix">HTTP prefix, e.g., "http://localhost:9078/"</param>
        /// <param name="requestHandler">
        /// Called on a thread pool thread for each incoming request.
        /// The handler is responsible for writing the response and closing it.
        /// </param>
        public void Start(string prefix, Action<HttpListenerContext> requestHandler)
        {
            if (_running) Stop();

            Prefix = prefix;
            _requestHandler = requestHandler;

            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            _running = true;

            _acceptThread = new Thread(AcceptLoop)
            {
                IsBackground = true,
                Name = "Theatre.HttpAccept"
            };
            _acceptThread.Start();
        }

        /// <summary>
        /// Stop the server and release the port.
        /// </summary>
        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* swallow on shutdown */ }
            try { _listener?.Close(); } catch { /* swallow on shutdown */ }
            _listener = null;
        }

        public void Dispose() => Stop();

        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    // GetContext blocks until a request arrives or listener stops
                    var context = _listener.GetContext();
                    // Handle each request on a thread pool thread
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
                }
                catch (HttpListenerException) when (!_running)
                {
                    // Expected when Stop() is called — listener.GetContext throws
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogException(ex);
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            try
            {
                _requestHandler?.Invoke(context);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogException(ex);
                try
                {
                    context.Response.StatusCode = 500;
                    var body = Encoding.UTF8.GetBytes(
                        "{\"error\":\"internal_server_error\"}");
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = body.Length;
                    context.Response.OutputStream.Write(body, 0, body.Length);
                    context.Response.Close();
                }
                catch { /* best effort */ }
            }
        }
    }
}
```

**Implementation Notes**:
- `HttpListener` on Windows requires URL ACL reservation for non-admin use.
  `http://localhost:PORT/` is typically allowed without elevation. If we hit
  permission issues, we may need to fall back to `TcpListener` + manual
  HTTP parsing — but try `HttpListener` first.
- The accept loop uses blocking `GetContext` on a dedicated thread rather
  than async `GetContextAsync` — simpler, no `SynchronizationContext`
  conflicts with Unity's main thread context.
- `ThreadPool.QueueUserWorkItem` for request handling keeps the accept
  thread free to receive the next connection immediately.
- The handler is called on a background thread. TheatreServer (Unit 6)
  is responsible for marshaling to the main thread when needed.

**Acceptance Criteria**:
- [ ] `Start(prefix, handler)` binds the port and begins accepting
- [ ] Requests arrive at the handler on a background thread
- [ ] `Stop()` releases the port without throwing
- [ ] Calling `Start()` after `Stop()` rebinds successfully (domain reload)
- [ ] Unhandled exceptions in the handler return HTTP 500, don't crash
- [ ] `IsListening` reflects actual state

---

### Unit 5: RequestRouter

**File**: `Packages/com.theatre.toolkit/Runtime/Transport/RequestRouter.cs`

Simple path + method routing. Maps incoming requests to handler functions.

```csharp
using System;
using System.Collections.Generic;
using System.Net;

namespace Theatre.Transport
{
    /// <summary>
    /// Routes HTTP requests by method + path to handler functions.
    /// </summary>
    public sealed class RequestRouter
    {
        private readonly Dictionary<(string method, string path), Action<HttpListenerContext>> _routes = new();
        private Action<HttpListenerContext> _notFoundHandler;

        /// <summary>
        /// Register a route handler.
        /// </summary>
        /// <param name="method">HTTP method (uppercase): "GET", "POST", "DELETE"</param>
        /// <param name="path">Absolute path: "/health", "/mcp"</param>
        /// <param name="handler">Handler called on background thread</param>
        public void Map(string method, string path, Action<HttpListenerContext> handler)
        {
            _routes[(method.ToUpperInvariant(), path)] = handler;
        }

        /// <summary>
        /// Set handler for unmatched routes. Default returns 404.
        /// </summary>
        public void SetNotFoundHandler(Action<HttpListenerContext> handler)
        {
            _notFoundHandler = handler;
        }

        /// <summary>
        /// Dispatch an incoming request to the matching handler.
        /// Called from a background thread.
        /// </summary>
        public void Dispatch(HttpListenerContext context)
        {
            var method = context.Request.HttpMethod.ToUpperInvariant();
            var path = context.Request.Url.AbsolutePath;

            if (_routes.TryGetValue((method, path), out var handler))
            {
                handler(context);
            }
            else if (_notFoundHandler != null)
            {
                _notFoundHandler(context);
            }
            else
            {
                SendNotFound(context);
            }
        }

        private static void SendNotFound(HttpListenerContext context)
        {
            var body = System.Text.Encoding.UTF8.GetBytes(
                "{\"error\":\"not_found\",\"message\":\"No handler for this endpoint\"}");
            context.Response.StatusCode = 404;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
            context.Response.Close();
        }
    }
}
```

**Implementation Notes**:
- Path matching is exact (no wildcards, no path params). Sufficient for
  the small number of Theatre endpoints (`/mcp`, `/health`).
- Future phases may add path prefix matching for versioned endpoints.

**Acceptance Criteria**:
- [ ] `Map("GET", "/health", handler)` routes GET /health to handler
- [ ] Unmatched routes return 404 with JSON body
- [ ] Method matching is case-insensitive
- [ ] Path matching is exact

---

### Unit 6: TheatreServer

**File**: `Packages/com.theatre.toolkit/Editor/TheatreServer.cs`

The entry point. Initializes on editor load, owns the `HttpTransport`
and `RequestRouter`, registers the `/health` endpoint, handles domain
reload restart.

```csharp
using System;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;
using Theatre.Transport;

namespace Theatre.Editor
{
    /// <summary>
    /// Main Theatre server. Starts on editor load via [InitializeOnLoad].
    /// Owns the HTTP transport and request routing.
    /// </summary>
    [InitializeOnLoad]
    public static class TheatreServer
    {
        private static HttpTransport s_transport;
        private static RequestRouter s_router;

        /// <summary>Whether the server is currently running.</summary>
        public static bool IsRunning => s_transport?.IsListening ?? false;

        /// <summary>The URL the server is listening on.</summary>
        public static string Url => IsRunning
            ? $"http://localhost:{TheatreConfig.Port}"
            : null;

        static TheatreServer()
        {
            // Delay start to avoid issues during early editor initialization
            EditorApplication.delayCall += StartServer;
            EditorApplication.quitting += StopServer;
        }

        /// <summary>
        /// Start or restart the HTTP server.
        /// </summary>
        public static void StartServer()
        {
            StopServer();

            s_router = new RequestRouter();
            RegisterRoutes(s_router);

            s_transport = new HttpTransport();
            try
            {
                s_transport.Start(TheatreConfig.HttpPrefix, s_router.Dispatch);
                Debug.Log($"[Theatre] Server started on {Url}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] Failed to start server: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop the HTTP server and release the port.
        /// </summary>
        public static void StopServer()
        {
            if (s_transport != null)
            {
                s_transport.Stop();
                s_transport = null;
                s_router = null;
            }
        }

        private static void RegisterRoutes(RequestRouter router)
        {
            router.Map("GET", "/health", HandleHealth);
        }

        // --- Route Handlers ---

        private static void HandleHealth(HttpListenerContext context)
        {
            var response = new StringBuilder();
            response.Append('{');
            response.Append("\"status\":\"ok\"");
            response.Append(",\"version\":\"");
            response.Append("0.0.1");
            response.Append('"');
            response.Append(",\"port\":");
            response.Append(TheatreConfig.Port);
            response.Append(",\"play_mode\":");
            // EditorApplication.isPlaying must be read on main thread,
            // but for /health we just report false on background thread.
            // Phase 1 will marshal to main thread properly.
            response.Append("false");
            response.Append('}');

            var body = Encoding.UTF8.GetBytes(response.ToString());
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
            context.Response.Close();
        }
    }
}
```

**Implementation Notes**:
- `[InitializeOnLoad]` fires the static constructor on every domain reload.
  This is the restart mechanism — each reload creates a fresh server.
- `EditorApplication.delayCall` defers startup to after editor initialization
  completes, avoiding race conditions with other `[InitializeOnLoad]` code.
- `EditorApplication.quitting` ensures clean shutdown when the editor closes.
- The `/health` handler is deliberately simple — no main thread marshaling
  needed. It returns static info. Phase 1 will add `play_mode` correctly
  via EditorCoroutine dispatch.
- `Debug.Log` with `[Theatre]` prefix per UX doc convention.

**Acceptance Criteria**:
- [ ] Server starts automatically when Unity opens the project
- [ ] `curl http://localhost:9078/health` returns `{"status":"ok",...}`
- [ ] Modifying a .cs file triggers domain reload; server restarts and
  responds to requests again within ~200ms
- [ ] Closing Unity stops the server cleanly (port released)
- [ ] `TheatreServer.IsRunning` is true when server is active
- [ ] If port is in use, logs an error without crashing the editor

---

### Unit 7: Health Endpoint Tests

**File**: `Packages/com.theatre.toolkit/Tests/Editor/HealthEndpointTests.cs`

```csharp
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Theatre.Editor;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class HealthEndpointTests
    {
        [Test]
        public void ServerIsRunning()
        {
            Assert.IsTrue(TheatreServer.IsRunning,
                "TheatreServer should be running after editor initialization");
        }

        [Test]
        public async Task HealthEndpointReturnsOk()
        {
            using var client = new HttpClient();
            var response = await client.GetAsync(TheatreServer.Url + "/health");

            Assert.AreEqual(200, (int)response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            StringAssert.Contains("\"status\":\"ok\"", body);
            StringAssert.Contains("\"port\":", body);
        }

        [Test]
        public async Task UnknownRouteReturns404()
        {
            using var client = new HttpClient();
            var response = await client.GetAsync(TheatreServer.Url + "/nonexistent");

            Assert.AreEqual(404, (int)response.StatusCode);
        }
    }
}
```

**File**: `Packages/com.theatre.toolkit/Tests/Editor/HttpTransportTests.cs`

```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Theatre.Transport;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class HttpTransportTests
    {
        private HttpTransport _transport;
        private const int TestPort = 19078; // Avoid conflicting with real server

        [TearDown]
        public void TearDown()
        {
            _transport?.Dispose();
            _transport = null;
        }

        [Test]
        public void StartAndStopWithoutError()
        {
            _transport = new HttpTransport();
            _transport.Start($"http://localhost:{TestPort}/", _ => { });
            Assert.IsTrue(_transport.IsListening);

            _transport.Stop();
            Assert.IsFalse(_transport.IsListening);
        }

        [Test]
        public async Task HandlerReceivesRequests()
        {
            bool handlerCalled = false;
            _transport = new HttpTransport();
            _transport.Start($"http://localhost:{TestPort}/", ctx =>
            {
                handlerCalled = true;
                ctx.Response.StatusCode = 200;
                ctx.Response.Close();
            });

            using var client = new HttpClient();
            await client.GetAsync($"http://localhost:{TestPort}/");

            Assert.IsTrue(handlerCalled);
        }

        [Test]
        public void CanRestartAfterStop()
        {
            _transport = new HttpTransport();

            _transport.Start($"http://localhost:{TestPort}/", _ => { });
            _transport.Stop();

            // Should not throw
            _transport.Start($"http://localhost:{TestPort}/", _ => { });
            Assert.IsTrue(_transport.IsListening);
        }

        [Test]
        public async Task HandlerExceptionReturns500()
        {
            _transport = new HttpTransport();
            _transport.Start($"http://localhost:{TestPort}/", _ =>
            {
                throw new InvalidOperationException("test error");
            });

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://localhost:{TestPort}/");

            Assert.AreEqual(500, (int)response.StatusCode);
        }
    }
}
```

**File**: `Packages/com.theatre.toolkit/Tests/Editor/RequestRouterTests.cs`

```csharp
using NUnit.Framework;
using Theatre.Transport;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class RequestRouterTests
    {
        // RequestRouter.Dispatch needs an HttpListenerContext which is hard
        // to construct in tests. Test the route registration logic:

        [Test]
        public void MapDoesNotThrow()
        {
            var router = new RequestRouter();
            Assert.DoesNotThrow(() =>
                router.Map("GET", "/health", _ => { }));
        }

        [Test]
        public void MapMultipleRoutesDoesNotThrow()
        {
            var router = new RequestRouter();
            router.Map("GET", "/health", _ => { });
            router.Map("POST", "/mcp", _ => { });
            router.Map("GET", "/mcp", _ => { });
        }
    }
}
```

**Implementation Notes**:
- `HttpClient` is available in Unity's .NET Standard 2.1.
- Tests that hit the real TheatreServer (`HealthEndpointTests`) depend on
  the server running — they're integration tests. Tests against
  `HttpTransport` directly use a separate port to avoid conflicts.
- `async Task` test methods are supported by Unity's Test Framework with
  NUnit 3.
- `RequestRouter` is hard to unit test in isolation because
  `HttpListenerContext` has no public constructor. The unit tests verify
  registration doesn't throw; the integration tests verify dispatching.

**Acceptance Criteria**:
- [ ] All tests pass in Unity Test Runner (EditMode)
- [ ] `HealthEndpointReturnsOk` verifies the full round-trip
- [ ] `HandlerExceptionReturns500` verifies error containment
- [ ] `CanRestartAfterStop` verifies domain reload recovery

---

### Unit 8: CLAUDE.md

**File**: `CLAUDE.md` (project root)

```markdown
# Theatre Unity — Agent Instructions

## What This Is

Theatre for Unity is an all-C# MCP server running inside the Unity Editor.
It gives AI agents spatial awareness of running Unity games (Stage) and
programmatic control over Unity subsystems that agents can't hand-write
(Director). Single UPM package, Streamable HTTP transport, no external
dependencies beyond Unity 6.

## Repository Layout

\```
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
\```

## Build & Test

\```bash
# Open in Unity 6 (6000.0+)
# Package compiles automatically when Unity opens TestProject/

# Run tests via Unity batch mode
unity -batchmode -projectPath TestProject -runTests \
  -testResults results.xml -quit

# Quick health check (server starts on editor load)
curl http://localhost:9078/health
\```

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
- Unity properties → snake_case: `localPosition` → `local_position`
- No abbreviations: `position` not `pos`, `distance` not `dist`
- Resource IDs: `<resource>_id` never bare `id`
- GameObjects always have both `path` and `instance_id`
- Singular result: `"result"`, plural list: `"results"`
- Errors include `code`, `message`, `suggestion`
- Vectors as arrays: `[x, y, z]`

See `docs/CONTRACTS.md` for full rules.

## Git Conventions

- Commit messages: short imperative subject line (≤72 chars)
- Do NOT add Co-Authored-By or AI attribution to commits
```

**Acceptance Criteria**:
- [ ] CLAUDE.md exists at project root
- [ ] Build commands are accurate and work
- [ ] Repository layout matches actual file structure

---

## Implementation Order

1. **Unit 2: UPM Package Skeleton** — creates the folder structure and
   assembly definitions. Everything else goes into these folders.
2. **Unit 1: Unity Test Harness Project** — creates `TestProject/` and
   links to the package. Needed to compile and test.
3. **Unit 3: TheatreConfig** — simple static config, no dependencies.
4. **Unit 4: HttpTransport** — HTTP server lifecycle. Depends on nothing.
5. **Unit 5: RequestRouter** — routing layer. Depends on nothing.
6. **Unit 6: TheatreServer** — wires everything together. Depends on
   Units 3, 4, 5.
7. **Unit 7: Tests** — validates the full stack. Depends on all above.
8. **Unit 8: CLAUDE.md** — can be written at any point.

Units 3, 4, 5 have no dependencies on each other and can be implemented
in parallel.

---

## Testing

### EditMode Tests: `Packages/com.theatre.toolkit/Tests/Editor/`

| Test File | Key Test Cases |
|---|---|
| `HttpTransportTests.cs` | Start/stop, restart after stop, handler receives requests, exception → 500 |
| `RequestRouterTests.cs` | Route registration, multiple routes |
| `HealthEndpointTests.cs` | Server is running, /health returns 200+JSON, unknown route returns 404 |

### Manual Verification

```bash
# After opening TestProject in Unity 6:
curl -s http://localhost:9078/health | python3 -m json.tool
# Expected: {"status": "ok", "version": "0.0.1", "port": 9078, "play_mode": false}

curl -s -o /dev/null -w "%{http_code}" http://localhost:9078/nonexistent
# Expected: 404
```

---

## Verification Checklist

```bash
# 1. Package compiles
# Open TestProject/ in Unity 6 — no errors in Console

# 2. Tests pass
# Window > General > Test Runner > EditMode > Run All

# 3. Health endpoint works
curl http://localhost:9078/health

# 4. Domain reload recovery
# Modify any .cs file in the package, save, wait for recompile
curl http://localhost:9078/health
# Should still respond

# 5. Port release on quit
# Close Unity, then:
curl http://localhost:9078/health
# Should fail (connection refused)
```
