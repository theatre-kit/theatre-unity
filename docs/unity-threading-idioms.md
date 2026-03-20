# Unity 6 Threading Idioms for Editor Extensions

## Section 1: The Fundamental Rule

Unity's engine is **single-threaded**. Every Unity API that touches the scene graph,
the asset database, the editor state, or engine services is guarded by a main-thread
check. If you call any of those APIs from a background thread (thread pool, Task,
Thread, HttpListener callback), Unity throws `UnityException` immediately:

```
UnityException: GetInt can only be called from the main thread.
```

This is not advisory. Unity's internal checks call `DebugLogHandler.Internal_Log`
through the engine's native layer, which asserts thread identity before any work
is done. There is no workaround that bypasses the check — the only correct approach
is to not call Unity APIs from background threads in the first place.

The Theatre server uses `System.Net.HttpListener`, which fires every request callback
on a .NET thread pool thread. This means **every single line in every route handler
is running on a background thread**. The check is not "some Unity calls need the
main thread" — it is "all Unity API calls need the main thread, full stop".

---

## Section 2: Main-Thread-Only APIs (FORBIDDEN from background threads)

The following APIs **must never be called from HttpListener callbacks, Task
continuations, Thread objects, or any non-main-thread context**. Each one
throws `UnityException` on background threads.

### SessionState (FORBIDDEN)

```csharp
// ALL of these throw UnityException from background threads:
SessionState.GetInt(key, defaultValue)
SessionState.SetInt(key, value)
SessionState.GetString(key, defaultValue)
SessionState.SetString(key, value)
SessionState.GetBool(key, defaultValue)
SessionState.SetBool(key, value)
SessionState.EraseInt(key)
SessionState.EraseString(key)
```

`SessionState` is backed by Unity's native editor persistence layer. Every
property access goes through native code that asserts main-thread. This was
the bug in `TheatreConfig.Port` — the property getter called
`SessionState.GetInt` and was invoked from the HttpListener thread.

### EditorApplication (FORBIDDEN)

```csharp
// FORBIDDEN from background threads:
EditorApplication.isPlaying
EditorApplication.isPaused
EditorApplication.timeSinceStartup
EditorApplication.applicationContentsPath
// Delegates are also unsafe to read/write off-thread:
EditorApplication.update
EditorApplication.hierarchyChanged
```

### SceneManager (FORBIDDEN)

```csharp
// FORBIDDEN from background threads:
SceneManager.GetActiveScene()
SceneManager.GetSceneAt(index)
SceneManager.GetSceneByName(name)
SceneManager.sceneCount
SceneManager.loadedSceneCount
SceneManager.LoadScene(...)
SceneManager.UnloadSceneAsync(...)
```

### GameObject, Transform, Component (FORBIDDEN)

```csharp
// FORBIDDEN — any access to UnityEngine.Object or its subclasses:
gameObject.transform.position
gameObject.GetComponent<T>()
gameObject.activeInHierarchy
component.enabled
Object.FindObjectsByType<T>(...)
Object.Instantiate(...)
Object.Destroy(...)
```

Even reading a field like `.position` is forbidden because the property
getter calls into the native engine layer with a main-thread assertion.

### Time (FORBIDDEN)

```csharp
// FORBIDDEN from background threads:
Time.frameCount
Time.time
Time.deltaTime
Time.realtimeSinceStartup
Time.fixedDeltaTime
Application.isPlaying
Application.isEditor
Application.unityVersion
```

### Debug (FORBIDDEN)

```csharp
// FORBIDDEN from background threads:
Debug.Log(message)
Debug.LogWarning(message)
Debug.LogError(message)
Debug.LogException(exception)
Debug.DrawLine(...)
Debug.DrawRay(...)
```

`Debug.Log` routes through Unity's native logger, which queues messages on
the main thread. Calling it from a background thread is not only wrong — it
can cause editor instability in Unity 6. Use `Console.Error.WriteLine` or
a thread-safe logger if you need background thread diagnostics.

### AssetDatabase (FORBIDDEN)

```csharp
// FORBIDDEN — all AssetDatabase methods:
AssetDatabase.CreateAsset(...)
AssetDatabase.LoadAssetAtPath<T>(...)
AssetDatabase.SaveAssets()
AssetDatabase.Refresh()
AssetDatabase.GetAssetPath(...)
AssetDatabase.FindAssets(...)
```

### Editor-Only APIs (FORBIDDEN)

```csharp
// FORBIDDEN from background threads:
Undo.RecordObject(...)
PrefabUtility.SaveAsPrefabAsset(...)
EditorPrefs.GetInt(...)
EditorPrefs.SetInt(...)
Selection.activeGameObject
EditorUtility.DisplayProgressBar(...)
SerializedObject.Update()
```

---

## Section 3: Thread-Safe APIs

The following can be called from any thread without risk of `UnityException`:

### Pure .NET (always safe)

```csharp
// Safe from any thread:
System.Threading.Thread
System.Threading.ThreadPool
System.Threading.Tasks.Task
System.Threading.ManualResetEventSlim
System.Collections.Concurrent.ConcurrentQueue<T>
System.Collections.Concurrent.ConcurrentDictionary<K,V>
System.Threading.Interlocked
System.Threading.Monitor / lock()
System.Net.HttpListener  // listener itself; callbacks run on thread pool
System.Text.Encoding
System.IO.File  // file I/O is OS-level, not Unity
DateTime.UtcNow
Guid.NewGuid()
```

### Constant and const fields

```csharp
// Compile-time constants — always safe:
TheatreConfig.DefaultPort     // const int
TheatreConfig.ProtocolVersion // const string
TheatreConfig.ServerVersion   // const string
```

### Cached primitive fields

```csharp
// Plain C# fields (not Unity-backed properties) — safe to read:
private static int s_cachedPort;           // set on main thread at startup
private static string s_cachedEnabledGroups; // set on main thread at startup
```

The key distinction: a plain `int` field set by the main thread is just a
memory location. An `int` property backed by `SessionState.GetInt()` calls
into Unity's native layer on every get. Only the former is thread-safe.

### Unity.Mathematics

```csharp
// Unity.Mathematics structs are plain value types — safe from any thread:
float3, float4x4, quaternion, math.distance(...)
```

---

## Section 4: The Caching Pattern

**When to use caching**: Values that are set at startup or changed infrequently,
where the background thread only needs to read the current value.

The pattern used in `TheatreServer`:

```csharp
// Cached on main thread at startup for background thread access
private static int s_cachedPort;
private static string s_cachedEnabledGroups;
private static ToolGroup s_cachedEnabledGroupsEnum;

public static void StartServer()
{
    // MAIN THREAD: read SessionState once, store as plain C# fields
    s_cachedPort = TheatreConfig.Port;          // calls SessionState.GetInt
    s_cachedEnabledGroupsEnum = TheatreConfig.EnabledGroups;
    s_cachedEnabledGroups = s_cachedEnabledGroupsEnum.ToString();

    // ... start HttpTransport ...
}

private static void HandleHealth(HttpListenerContext context)
{
    // BACKGROUND THREAD: read cached fields, never SessionState
    var json = $"{{\"status\":\"ok\",\"port\":{s_cachedPort}"
             + $",\"enabled_groups\":\"{s_cachedEnabledGroups}\"}}";
    // ...
}
```

**Rule**: Any value needed by a background-thread handler must be read from
`SessionState` (or any Unity API) exactly once on the main thread at startup,
stored in a plain `static` field, and read from that field on the background thread.

**When to update the cache**: If config changes at runtime (e.g., `SetEnabledGroups`),
update both the Unity backing store AND the cached field on the main thread:

```csharp
public static void SetEnabledGroups(ToolGroup groups)
{
    // Must be called from main thread
    TheatreConfig.EnabledGroups = groups;        // SessionState write
    s_cachedEnabledGroupsEnum = groups;          // cache update
    s_cachedEnabledGroups = groups.ToString();   // cache update
}
```

**When NOT to use caching**: Values that must reflect live editor state at the
moment of the call (e.g., current play mode, current active scene name). These
require the dispatcher pattern.

---

## Section 5: The Dispatcher Pattern

When a background-thread handler needs a value that can only be obtained from
a live Unity API call (not a startup snapshot), use `MainThreadDispatcher.Invoke<T>`.

### How MainThreadDispatcher works

`MainThreadDispatcher` in `Theatre.Editor` uses `EditorApplication.update` as
its pump. It maintains a `ConcurrentQueue<WorkItem>`. The `[InitializeOnLoad]`
static constructor hooks `ProcessQueue` into `EditorApplication.update`, which
fires on every editor frame on the main thread.

```csharp
// Background thread: enqueue work and block until complete
public static T Invoke<T>(Func<T> func)
{
    T result = default;
    Exception caught = null;
    var done = new ManualResetEventSlim(false);

    s_queue.Enqueue(new WorkItem
    {
        Work = () =>
        {
            try { result = func(); }
            catch (Exception ex) { caught = ex; }
        },
        Done = done
    });

    done.Wait();   // blocks background thread until main thread completes
    done.Dispose();

    if (caught != null) throw caught;
    return result;
}

// Main thread: drain the queue every editor update
private static void ProcessQueue()
{
    while (s_queue.TryDequeue(out var item))
    {
        item.Work();    // runs on main thread — Unity APIs safe here
        item.Done.Set(); // unblocks the background thread
    }
}
```

### Latency

The background thread blocks until the next `EditorApplication.update` tick,
which fires approximately once per editor frame (~16ms at 60fps). This is
acceptable for MCP tool calls. Do not use `MainThreadDispatcher` in tight loops.

### Usage in tool handlers

All MCP tool handlers in Theatre are already dispatched via `MainThreadDispatcher`
through `ExecuteToolOnMainThread` in `TheatreServer`. Tool handler code (`Execute`
methods) always runs on the main thread:

```csharp
private static string ExecuteToolOnMainThread(string toolName, JToken arguments)
{
    return MainThreadDispatcher.Invoke(() =>
    {
        // This lambda runs on main thread — all Unity APIs safe here
        var tool = s_toolRegistry.GetTool(toolName,
            TheatreConfig.EnabledGroups,   // SessionState — OK, main thread
            TheatreConfig.DisabledTools);

        return tool.Handler(arguments);    // tool Execute() — OK, main thread
    });
}
```

### When to use dispatch vs cache

| Scenario | Use |
|---|---|
| Port number (set at startup, rarely changes) | Cache |
| EnabledGroups (set at startup, changes via UI) | Cache + update cache on change |
| `isPlaying` (changes at any time) | Dispatch |
| Active scene name (changes when scenes load) | Dispatch |
| Live transform position | Dispatch |
| SessionState values in route handlers | Cache (read once on main thread) |

---

## Section 6: Domain Reload Rules

Unity reloads the managed C# domain on every script recompilation (and on play
mode entry unless "Reload Domain" is disabled in project settings). Domain reload
destroys all managed state: static fields, threads, `HttpListener` instances,
in-memory collections, and SqliteConnection handles.

### What happens during domain reload

1. The CLR is torn down. All background threads are aborted.
2. All static fields are reset to their default values.
3. The new domain loads; `[InitializeOnLoad]` static constructors fire.
4. `EditorApplication.delayCall` callbacks fire at the start of the next editor frame.
5. `SessionState` and `EditorPrefs` survive (they're native, not managed).
6. Files on disk survive (SQLite database, asset files, project settings).

### Threading hazard during domain reload

If a background thread is executing Unity API calls at the moment domain reload
fires, the thread will be using managed objects that are being destroyed. This
can cause:
- `ObjectDisposedException` or `NullReferenceException` on the background thread
- `UnityException` because thread identity checks fail during teardown
- Editor instability if the thread holds a lock

**Prevention**: Background threads must check for disposal before accessing any
shared state:

```csharp
// Pattern: check disposed flag before any work
private static volatile bool s_disposed;

private static void HandleRequest(HttpListenerContext context)
{
    if (s_disposed)
    {
        context.Response.StatusCode = 503;
        context.Response.Close();
        return;
    }
    // ... proceed
}

public static void StopServer()
{
    s_disposed = true;
    s_transport?.Stop();
    // ... cleanup
}
```

`HttpTransport.Stop()` calls `HttpListener.Close()`, which causes any pending
`GetContext()` / `BeginGetContext()` calls to throw `HttpListenerException`. This
is expected and must be caught in the listener loop.

### InitializeOnLoad re-fires

`[InitializeOnLoad]` static constructors re-run on every domain reload. This
is Theatre's mechanism for restarting the server after domain reload:

```csharp
[InitializeOnLoad]
public static class TheatreServer
{
    static TheatreServer()
    {
        EditorApplication.delayCall += StartServer;  // re-fires every reload
        EditorApplication.quitting += StopServer;
    }
}
```

`delayCall` fires after the editor finishes loading the new domain, ensuring
all types are available before `StartServer` runs. Restarting directly in the
static constructor risks race conditions with other `[InitializeOnLoad]` types.

---

## Section 7: The HttpListener Pattern

`System.Net.HttpListener` receives connections and dispatches them to callbacks
via the .NET thread pool. This is fundamental to how Theatre works, and it means:

**Every route handler body runs on a thread pool thread.**

This applies without exception to:
- `HandleHealth` (the bug that prompted this document)
- `HandleSessionDelete`
- `s_mcpRouter.HandlePost`
- `s_sseManager.HandleSseConnect`
- Any future route added to `RequestRouter`

### The two safe patterns for route handlers

**Pattern A — Cached values only** (for status/health endpoints):

The handler reads only: const fields, cached primitive fields, thread-safe
collections. No Unity API calls of any kind.

```csharp
private static void HandleHealth(HttpListenerContext context)
{
    // BACKGROUND THREAD — only read from:
    //   const fields: TheatreConfig.ServerVersion
    //   cached fields: s_cachedPort, s_cachedEnabledGroups
    //   thread-safe: s_sseManager.ConnectionCount (ConcurrentDictionary)
    //   C# only: IsClientConnected (reads a C# bool field)
    var json = $"{{\"status\":\"ok\""
             + $",\"version\":\"{TheatreConfig.ServerVersion}\""
             + $",\"port\":{s_cachedPort}"
             + $",\"enabled_groups\":\"{s_cachedEnabledGroups}\""
             + $",\"sse_connections\":{s_sseManager?.ConnectionCount ?? 0}"
             + "}}";
    // write response...
}
```

**Pattern B — Dispatch to main thread** (for tool execution endpoints):

The handler enqueues work via `MainThreadDispatcher.Invoke`, blocks on the
result, then writes the response.

```csharp
private static void HandlePost(HttpListenerContext context)
{
    // BACKGROUND THREAD — safe: parse JSON (pure C#)
    var body = ReadRequestBody(context.Request);
    var request = ParseJsonRpc(body);

    // DISPATCH to main thread — all Unity API access happens in the lambda
    var responseJson = MainThreadDispatcher.Invoke(() =>
    {
        // MAIN THREAD — Unity APIs safe here
        return ExecuteTool(request);
    });

    // BACKGROUND THREAD — safe: write HTTP response (pure .NET)
    WriteResponse(context.Response, responseJson);
}
```

### What is NEVER acceptable

```csharp
// WRONG — called from background thread route handler:
private static void HandleHealth(HttpListenerContext context)
{
    var port = TheatreConfig.Port;            // SessionState.GetInt — THROWS
    var groups = TheatreConfig.EnabledGroups; // SessionState.GetInt — THROWS
    var playing = EditorApplication.isPlaying; // THROWS
    var scene = SceneManager.GetActiveScene().name; // THROWS
    Debug.Log("[Theatre] health check");       // THROWS
}
```

### Adding new route handlers

Every new route handler added to `RequestRouter` must be reviewed against
this checklist before merging. No Unity API call is acceptable in a route
handler body unless it is wrapped in `MainThreadDispatcher.Invoke`.

---

## Section 8: Checklist for New Code

Apply this checklist whenever writing code that may run on a background thread —
including route handlers, Task continuations, Thread callbacks, and any async
method that may have been `await`-ed off the main thread.

### Before writing the code

- [ ] Identify the thread this code runs on. Route handlers = thread pool.
      `Task.Run(...)` = thread pool. `async void` on main thread that `await`s
      a non-main-thread continuation = may resume on thread pool.
- [ ] Identify every value the code needs. For each value, determine whether
      it is Unity-backed (requires main thread) or pure-C# (thread-safe).

### For each Unity-backed value

- [ ] Can it be cached at startup? If yes: cache it in `StartServer()` as a
      plain `static` field and read the cached field in the handler.
- [ ] Does it need to be live? If yes: wrap the entire section that needs it
      in `MainThreadDispatcher.Invoke(() => { ... })`.
- [ ] Never mix: do not read `SessionState` directly in a route handler body,
      even once.

### API red flags — search for these in handler code

- [ ] `SessionState.*` — forbidden in handlers; use cached field
- [ ] `EditorApplication.*` — forbidden in handlers; use cached field or dispatch
- [ ] `SceneManager.*` — forbidden in handlers; dispatch
- [ ] `Debug.Log*` — forbidden in handlers; use cached response or dispatch
- [ ] `TheatreConfig.Port` — forbidden in handlers; use `s_cachedPort`
- [ ] `TheatreConfig.EnabledGroups` — forbidden in handlers; use `s_cachedEnabledGroupsEnum`
- [ ] `TheatreConfig.HttpPrefix` — forbidden in handlers (calls `Port` which calls `SessionState`)
- [ ] Any `UnityEngine.*` or `UnityEditor.*` type not on the approved list above

### Reviewing an existing handler

1. Read every line of the handler method.
2. Follow every method call to check what it calls.
3. Any call chain that reaches `SessionState`, `Debug`, `SceneManager`,
   `EditorApplication`, `GameObject`, `Transform`, `Component`, or any
   native Unity property must be eliminated or wrapped in `MainThreadDispatcher.Invoke`.
4. Run the handler under load and check the Unity Console for `UnityException`.

### Domain reload safety

- [ ] Does the handler read from a static field that may have been reset by
      domain reload? Add a null/disposed check.
- [ ] Does the handler write to a static field? Ensure the write is either
      idempotent or protected by a lock.
- [ ] Does `StopServer` set a `s_disposed` flag before any other cleanup,
      so in-flight handlers can bail early?

---

## Quick Reference

| API | Thread | Pattern |
|---|---|---|
| `TheatreConfig.Port` | Main only | Cache as `s_cachedPort` at startup |
| `TheatreConfig.EnabledGroups` | Main only | Cache as `s_cachedEnabledGroupsEnum` at startup |
| `TheatreConfig.HttpPrefix` | Main only | Cache or compute from `s_cachedPort` |
| `TheatreConfig.ServerVersion` | Any (const) | Read directly |
| `TheatreConfig.DisabledTools` | Any (HashSet, no Unity) | Read directly |
| `SessionState.*` | Main only | Cache or dispatch |
| `EditorApplication.isPlaying` | Main only | Dispatch |
| `SceneManager.GetActiveScene()` | Main only | Dispatch |
| `Debug.Log*` | Main only | Dispatch (or omit in handlers) |
| `AssetDatabase.*` | Main only | Dispatch |
| Tool handler `Execute()` body | Main (via dispatcher) | No action needed |
| `IsClientConnected` | Any (C# bool) | Read directly |
| `s_sseManager.ConnectionCount` | Any (ConcurrentDictionary) | Read directly |
| `DateTime.UtcNow` | Any | Read directly |
| `Guid.NewGuid()` | Any | Call directly |
