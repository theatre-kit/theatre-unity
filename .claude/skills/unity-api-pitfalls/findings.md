# Unity 6 API Rules for Theatre Editor Extensions

## Context

This project is a UPM package (`com.theatre.toolkit`) with Runtime and Editor
assemblies running inside the Unity 6 Editor. The server uses
`System.Net.HttpListener` which fires callbacks on thread pool threads.
These are hard-won rules from bugs encountered during development.

---

## 1. System.Text.Json Does Not Exist

Unity 6 uses .NET Standard 2.1 but does NOT ship `System.Text.Json`.
Any code using `JsonSerializer`, `JsonElement`, `Utf8JsonWriter`,
`JsonPropertyName`, `JsonIgnoreCondition` will fail to compile.

**Use instead:** `Newtonsoft.Json` via the `com.unity.nuget.newtonsoft-json`
UPM package (ships Newtonsoft.Json 13.0.2).

| Don't use | Use instead |
|---|---|
| `System.Text.Json.JsonSerializer` | `Newtonsoft.Json.JsonConvert` |
| `System.Text.Json.JsonElement` | `Newtonsoft.Json.Linq.JToken` |
| `System.Text.Json.Utf8JsonWriter` | `Newtonsoft.Json.Linq.JObject` / `JArray` |
| `[JsonPropertyName("x")]` | `[JsonProperty("x")]` |
| `[JsonIgnore(Condition = ...)]` | `[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]` |
| `JsonDocument.Parse(s).RootElement` | `JToken.Parse(s)` |

**Newtonsoft gotcha:** Newtonsoft serializes ALL public properties by default.
Computed properties like `bool IsRequest => ...` will appear in JSON output
unless marked `[JsonIgnore]`.

---

## 2. Assembly Boundary Rules

The UPM package has two assemblies:

| Assembly | Can Reference | Cannot Reference |
|---|---|---|
| `com.theatre.toolkit.runtime` | `UnityEngine.*` | `UnityEditor.*` |
| `com.theatre.toolkit.editor` | `UnityEngine.*`, `UnityEditor.*`, runtime assembly | — |

**Runtime code that needs editor APIs:** Wrap in `#if UNITY_EDITOR`:

```csharp
#if UNITY_EDITOR
var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
#endif
```

---

## 3. Internal / Missing / Deprecated APIs

### `Object.FindObjectFromInstanceID(int)` — DOES NOT EXIST

This is an internal Unity API, not in the public C# API.

**Use instead:**
```csharp
#if UNITY_EDITOR
var obj = UnityEditor.EditorUtility.InstanceIDToObject(instanceId);
#endif
```

### `Object.FindObjectsOfType<T>()` — DEPRECATED in Unity 6

```csharp
// WRONG: Object.FindObjectsOfType<T>()
// RIGHT:
Object.FindObjectsByType<T>(FindObjectsSortMode.None);
```

### `Object.FindObjectOfType<T>()` — DEPRECATED in Unity 6

```csharp
// WRONG: Object.FindObjectOfType<T>()
// RIGHT:
Object.FindFirstObjectByType<T>();
Object.FindAnyObjectByType<T>(); // faster, no guaranteed order
```

### `EditorSceneManager.GetSceneManagerSetup()` — Editor only

Cannot be used in Runtime assembly. Use `SceneManager.GetSceneAt(i)` and
`SceneManager.sceneCount` instead.

---

## 4. Threading — The Fundamental Rule

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

## 5. Domain Reload

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

## 6. Serialization Pitfalls

### JsonUtility limitations

Cannot serialize: `Dictionary<K,V>`, top-level arrays, properties, dynamic JSON.
Use Newtonsoft for anything beyond simple flat `[Serializable]` types.

### SerializedProperty type coverage

Handle ALL types when traversing:
`Integer`, `Boolean`, `Float`, `String`, `Color`, `Vector2/3/4`,
`Quaternion`, `Rect`, `Bounds`, `ObjectReference` (path + instance_id),
`Enum` (string name), `ArraySize` (skip), `Generic` (arrays), `ManagedReference`.

Missing type handlers cause silent data loss.

---

## 7. Test Setup for UPM Packages

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

---

## 8. Checklist for New Code

Before writing any C# in this project:

- [ ] Am I in the Runtime or Editor assembly? Check what I can reference.
- [ ] Does this code run on the main thread or a background thread?
- [ ] Am I using any `System.*` namespace? Verify Unity ships it.
- [ ] Am I using any `UnityEngine.Object` static method? Verify it's public and not deprecated.
- [ ] Am I serializing with Newtonsoft? Check for leaked properties (`[JsonIgnore]`).
- [ ] If background thread: am I calling ANY Unity API? (Must cache or dispatch.)
- [ ] If domain reload: am I catching `ThreadAbortException`?
- [ ] Have I searched my code for every `UnityEngine.*` and `UnityEditor.*` call?
