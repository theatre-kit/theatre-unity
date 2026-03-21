# Pattern: Domain Reload Survival

Unity reloads the C# domain on every script recompile, destroying all managed state. Theatre survives by: restarting the HTTP server via `[InitializeOnLoad]`, caching main-thread values at startup, and persisting stateful data to `SessionState` (watches) and SQLite (recordings).

## Rationale

Domain reload is Unity's equivalent of a process restart. There is no `[Serializable]` escape hatch for static fields — everything is zeroed. The only survival strategies are: re-initialize on load, persist to durable stores (SessionState, EditorPrefs, disk), and treat any in-flight request as lost.

## Examples

### Example 1: [InitializeOnLoad] triggers server restart on every domain reload
**File**: `Editor/TheatreServer.cs:17-53`
```csharp
[InitializeOnLoad]
public static class TheatreServer
{
    private static HttpTransport s_transport;
    private static ToolRegistry s_toolRegistry;

    // Cached on main thread at startup for background thread access
    private static int s_cachedPort;
    private static ToolGroup s_cachedEnabledGroupsEnum;

    static TheatreServer()
    {
        EditorApplication.delayCall += StartServer;   // delayCall = after domain reload settles
        EditorApplication.quitting += StopServer;
    }
```

### Example 2: SessionState-backed config — survives reload, lost on editor restart
**File**: `Runtime/Core/TheatreConfig.cs:14-46`
```csharp
public static int Port
{
#if UNITY_EDITOR
    get => UnityEditor.SessionState.GetInt("Theatre.Port", DefaultPort);
    set => UnityEditor.SessionState.SetInt("Theatre.Port", value);
#else
    get => DefaultPort;
    set { }
#endif
}
```
`SessionState` survives domain reloads but is cleared when the editor closes. Not persisted to disk — use `EditorPrefs` for values that must survive restarts.

### Example 3: WatchEngine restores persisted watches on Initialize()
**File**: `Runtime/Stage/GameObject/WatchEngine.cs:42-46`
```csharp
public void Initialize(Action<JObject> notifyCallback)
{
    _notifyCallback = notifyCallback;
    var (watches, nextId) = WatchPersistence.Restore();  // reads SessionState
    _watches.AddRange(watches);
    _nextId = nextId;
}
```
**File**: `Runtime/Stage/GameObject/WatchPersistence.cs:27-42`
```csharp
public static void Save(List<WatchState> watches, int nextId)
{
#if UNITY_EDITOR
    var json = JsonConvert.SerializeObject(defs);
    SessionState.SetString("Theatre_Watches", json);
    SessionState.SetInt("Theatre_WatchCounter", nextId);
#endif
}
```

### Example 4: WatchTool — lazy engine init with EditorApplication.update registration
**File**: `Editor/Tools/Watch/WatchTool.cs:98-112`
```csharp
internal static WatchEngine GetEngine()
{
    if (s_engine == null)
    {
        s_engine = new WatchEngine();
        s_engine.Initialize(OnWatchTriggered);
        UnityEditor.EditorApplication.update += s_engine.Tick;  // re-register each reload
    }
    return s_engine;
}
```

## Checklist for new stateful systems

| Concern | Strategy |
|---------|---------|
| Tool registry | Rebuilt from scratch — tools are stateless, `RegisterBuiltInTools()` re-runs |
| HTTP server | Re-bound via `[InitializeOnLoad]` + `delayCall` |
| Watch definitions | Serialized to `SessionState` as JSON, restored in `Initialize()` |
| Spatial index | Rebuilt lazily from live scene on first query — derived data, no persistence |
| SSE notification streams | Drop on reload; clients reconnect and re-subscribe |
| Config (port, groups) | `SessionState` + cached in private static at startup |
| Recording clips | SQLite file on disk — unaffected by reload; connection reopened at startup |

## When to Use
- Any new stateful subsystem must document how it survives domain reload
- Test with: save a state value, trigger compile (modify any .cs file), verify state restored

## Common Violations
- Storing state in static fields without a `[InitializeOnLoad]` restore path — loses state on every recompile
- Reading `SessionState` from background threads — always cache on main thread at startup
- Using `EditorApplication.update +=` outside `[InitializeOnLoad]` without re-registering on reload
