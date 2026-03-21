# Pattern: Main-Thread Dispatch

HTTP request handlers run on thread pool threads. All Unity API calls must be marshaled to the editor's main thread via `MainThreadDispatcher.Invoke()`, which blocks the background thread until the work completes.

## Rationale

Unity's entire API surface (GameObjects, transforms, SceneManager, SessionState, Debug.Log, etc.) is main-thread-only. `HttpListener` callbacks arrive on background threads. The `MainThreadDispatcher` queues work via a `ConcurrentQueue<WorkItem>` and pumps the queue from `EditorApplication.update`. All tool handlers are thus already on the main thread when they execute.

## Examples

### Example 1: Tool execution wraps all handler logic in MainThreadDispatcher
**File**: `Editor/TheatreServer.cs:141-161`
```csharp
private static string ExecuteToolOnMainThread(string toolName, JToken arguments)
{
    return MainThreadDispatcher.Invoke(() =>
    {
        var enabledGroups = s_cachedEnabledGroupsEnum;
        var disabledTools = TheatreConfig.DisabledTools;
        var tool = s_toolRegistry.GetTool(toolName, enabledGroups, disabledTools);
        if (tool == null)
            throw new InvalidOperationException($"Tool '{toolName}' is not available");
        return tool.Handler(arguments);   // tool handler runs on main thread
    });
}
```

### Example 2: MainThreadDispatcher implementation — queue + ManualResetEvent
**File**: `Editor/MainThreadDispatcher.cs:34-67`
```csharp
public static T Invoke<T>(Func<T> func)
{
    T result = default;
    Exception caught = null;
    var done = new ManualResetEventSlim(false);

    s_queue.Enqueue(new WorkItem
    {
        Work = () => {
            try { result = func(); }
            catch (Exception ex) { caught = ex; }
        },
        Done = done
    });

    done.Wait();          // background thread blocks here
    done.Dispose();
    if (caught != null) ExceptionDispatchInfo.Capture(caught).Throw();
    return result;
}
```

### Example 3: McpRouter calls the executor delegate (set up by TheatreServer)
**File**: `Runtime/Transport/McpRouter.cs:234-260`
```csharp
// _executeToolOnMainThread is the delegate passed from TheatreServer
var resultJson = _executeToolOnMainThread(callParams.Name, callParams.Arguments);
// Back on background thread, resultJson is the completed tool response
var callResult = new McpToolCallResult {
    Content = new List<McpContentItem> {
        new McpContentItem { Type = "text", Text = resultJson }
    },
    IsError = false
};
```

## Threading rules

| Code location | Thread | Notes |
|---------------|--------|-------|
| `HttpTransport` listener callbacks | Thread pool | Never call Unity APIs here |
| `McpRouter.HandlePost` | Thread pool | Parses JSON, calls executor delegate |
| `_executeToolOnMainThread(...)` | Blocks thread pool, executes on main | The marshal boundary |
| All tool `Execute()` handlers | Main thread | Safe to call any Unity API |
| `SessionState.*` reads | **Must be main thread** | Cached at startup for background callers |
| `Debug.Log*` | **Must be main thread** | Same rule applies |

## Config caching pattern (corollary)

Values read at startup from SessionState are cached in private static fields on the main thread. Background threads use the cached values:

**File**: `Editor/TheatreServer.cs:60-63`
```csharp
// In StartServer() — runs on main thread
s_cachedPort = TheatreConfig.Port;                  // SessionState read — main thread OK
s_cachedEnabledGroupsEnum = TheatreConfig.EnabledGroups;   // same
// Background threads use s_cachedPort, not TheatreConfig.Port
```

## When to Use
- Every Unity API call from a background context uses `MainThreadDispatcher.Invoke()`
- Config values read at startup are cached — never re-read from SessionState on background threads

## Common Violations
- Calling `SessionState.GetInt/SetString` from a route handler — use cached field instead
- Calling `Debug.Log` from `HttpTransport`'s listener thread — wrap in `MainThreadDispatcher.Invoke`
- Calling `GameObject.Find` or any scene query from a background thread — always wrap
