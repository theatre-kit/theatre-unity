# Unity 6 API Rules — Threading, Domain Reload, and Test Setup

## Table of Contents

1. [Threading — The Fundamental Rule](#1-threading--the-fundamental-rule)
2. [Domain Reload](#2-domain-reload)
3. [Test Setup for UPM Packages](#3-test-setup-for-upm-packages)

---

## 1. Threading — The Fundamental Rule

Unity is **single-threaded**. Every Unity API that touches scene graph,
asset database, editor state, or engine services throws `UnityException`
from background threads. No exceptions, no workarounds.

Theatre's `HttpListener` fires every callback on a thread pool thread.
**Every route handler body runs on a background thread.**

### FORBIDDEN from background threads (throws UnityException)

```csharp
// SessionState
SessionState.GetInt / SetInt / GetString / SetString

// TheatreConfig properties that call SessionState
TheatreConfig.Port          // calls SessionState.GetInt
TheatreConfig.EnabledGroups // calls SessionState.GetInt
TheatreConfig.HttpPrefix    // calls TheatreConfig.Port

// EditorApplication
EditorApplication.isPlaying / isPaused / timeSinceStartup

// SceneManager
SceneManager.GetActiveScene() / GetSceneAt() / sceneCount

// All UnityEngine.Object access
gameObject.transform.position / GetComponent<T>() / activeInHierarchy
Object.FindObjectsByType<T>() / Instantiate() / Destroy()

// Time
Time.frameCount / Time.time / Time.deltaTime
Application.isPlaying

// Debug
Debug.Log / LogWarning / LogError / LogException

// Asset operations
AssetDatabase.* / Undo.* / PrefabUtility.* / EditorPrefs.*
SerializedObject.Update() / Selection.activeGameObject
```

### Safe from any thread

```csharp
// Pure .NET
System.Threading.* / System.Collections.Concurrent.*
System.Net.HttpListener / System.IO.File
DateTime.UtcNow / Guid.NewGuid()
System.Text.Encoding

// Const fields
TheatreConfig.DefaultPort / ServerVersion / ProtocolVersion

// Cached primitive fields (set on main thread)
s_cachedPort / s_cachedEnabledGroups

// In-memory C# collections (no Unity backing)
TheatreConfig.DisabledTools (HashSet)
s_sseManager.ConnectionCount
IsClientConnected (plain bool)
```

### Pattern A: Caching (for values that rarely change)

```csharp
// StartServer() — MAIN THREAD
s_cachedPort = TheatreConfig.Port;          // reads SessionState once
s_cachedEnabledGroupsEnum = TheatreConfig.EnabledGroups;
s_cachedEnabledGroups = s_cachedEnabledGroupsEnum.ToString();

// HandleHealth() — BACKGROUND THREAD
var json = $"\"port\":{s_cachedPort}";      // reads cached field
```

Update cache when config changes (on main thread):
```csharp
public static void SetEnabledGroups(ToolGroup groups)
{
    TheatreConfig.EnabledGroups = groups;      // SessionState write
    s_cachedEnabledGroupsEnum = groups;        // cache update
    s_cachedEnabledGroups = groups.ToString();  // cache update
}
```

### Pattern B: Dispatch (for live values)

```csharp
var result = MainThreadDispatcher.Invoke(() =>
{
    // MAIN THREAD — all Unity APIs safe here
    return EditorApplication.isPlaying;
});
```

`MainThreadDispatcher` uses `EditorApplication.update` as its pump. ~16ms
latency per dispatch (one editor frame). Tool handlers are always dispatched
via `ExecuteToolOnMainThread` — their `Execute()` bodies are safe.

### When to use which

| Value | Pattern |
|---|---|
| Port, EnabledGroups (set at startup) | Cache |
| isPlaying (changes at any time) | Dispatch |
| Active scene name | Dispatch |
| Live transform position | Dispatch |
| Tool handler body | Already dispatched |

---

## 2. Domain Reload

Unity reloads C# domain on script recompilation. All managed state dies:
static fields, threads, HttpListener, ManualResetEventSlim.

**What survives:** SessionState, EditorPrefs, files on disk.

**Restart pattern:**
```csharp
[InitializeOnLoad]
public static class TheatreServer
{
    static TheatreServer()
    {
        EditorApplication.delayCall += StartServer;
        EditorApplication.quitting += StopServer;
    }
}
```

**Catch ThreadAbortException** in background loops:
```csharp
catch (System.Threading.ThreadAbortException)
{
    break; // Domain reload — exit cleanly
}
```

---

## 3. Test Setup for UPM Packages

Tests in UPM packages require specific configuration to appear in Unity's
Test Runner window.

### Project manifest must list testables

Local packages (`file:` references) require `"testables"` in the project
manifest or their tests are invisible to the Test Runner:

```json
{
  "dependencies": {
    "com.theatre.toolkit": "file:../../Packages/com.theatre.toolkit"
  },
  "testables": ["com.theatre.toolkit"]
}
```

Without `"testables"`, the asmdef can be perfect and tests still won't show.

### Test asmdef pattern (Unity 6)

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
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "noEngineReferences": false
}
```

Key fields:
- `overrideReferences: true` + `precompiledReferences: ["nunit.framework.dll"]` — required for NUnit
- `defineConstraints: ["UNITY_INCLUDE_TESTS"]` — defined by `com.unity.test-framework`
- `autoReferenced: false` — prevents production code from referencing tests
- References can use names or GUIDs — both work

### Running tests

No batch mode available (requires Pro license). Run from GUI:
**Window > General > Test Runner > EditMode > Run All**
