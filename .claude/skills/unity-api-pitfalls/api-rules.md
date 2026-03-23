# Unity 6 API Rules — API Replacements, Assembly Boundaries, Serialization

## Context

This project is a UPM package (`com.theatre.toolkit`) with Runtime and Editor
assemblies running inside the Unity 6 Editor. The server uses
`System.Net.HttpListener` which fires callbacks on thread pool threads.
These are hard-won rules from bugs encountered during development.

## Table of Contents

1. [System.Text.Json Does Not Exist](#1-systemtextjson-does-not-exist)
2. [Assembly Boundary Rules](#2-assembly-boundary-rules)
3. [Internal / Missing / Deprecated APIs](#3-internal--missing--deprecated-apis)
4. [Serialization Pitfalls](#4-serialization-pitfalls)
5. [Checklist for New Code](#5-checklist-for-new-code)

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
// WRONG — in a Runtime .cs file:
using UnityEditor;  // CS0234: 'Editor' does not exist in namespace 'Unity'

// RIGHT — in a Runtime .cs file:
#if UNITY_EDITOR
var obj = UnityEditor.EditorUtility.InstanceIDToObject(id);
#endif
```

A more complete example:

```csharp
#if UNITY_EDITOR
var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
#endif
```

### Cross-Assembly Visibility

| Rule | Detail |
|---|---|
| `internal` in Runtime → invisible to Editor | Editor assembly references Runtime, but `internal` types/members are not visible. Use `public` for any Runtime type that Editor code needs to call. |
| `internal` in Editor → invisible to Tests | Test assembly references Editor, but `internal` types are not visible. We have `[assembly: InternalsVisibleTo("com.theatre.toolkit.editor.tests")]` in `Editor/AssemblyInfo.cs` to allow tests to call internal handlers directly. |
| `overrideReferences: true` hides ALL transitive DLLs | When ANY asmdef sets `overrideReferences: true`, it ONLY sees DLLs in its `precompiledReferences` list. If Runtime asmdef has this flag, `Newtonsoft.Json.dll` MUST be listed explicitly — it won't come through transitively from the UPM package dependency. |
| Adding a new precompiled DLL | When adding a new DLL to `Plugins/`, you must add it to `precompiledReferences` in EVERY asmdef that has `overrideReferences: true` and needs it (currently: runtime + test asmdefs). |

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

### `Object.GetInstanceID()` — deprecated in Unity 6.4

Keep using it — `GetEntityId()` returns an `EntityId` struct (not `int`),
breaking wire format. Suppress warning with `#pragma warning disable CS0618`.
Revisit when `EntityId` stabilizes.

---

## 4. Serialization Pitfalls

### JsonUtility limitations

Cannot serialize: `Dictionary<K,V>`, top-level arrays, properties, dynamic JSON.
Use Newtonsoft for anything beyond simple flat `[Serializable]` types.

### SerializedProperty type coverage

Handle ALL types when traversing:
`Integer`, `Boolean`, `Float`, `String`, `Color`, `Vector2/3/4`,
`Quaternion`, `Rect`, `Bounds`, `ObjectReference` (path + instance_id),
`Enum` (string name), `ArraySize` (skip), `Generic` (arrays), `ManagedReference`.

Missing type handlers cause silent data loss.

### Newtonsoft asmdef requirement

The runtime asmdef has `overrideReferences: true` (needed for SQLite DLLs).
This means `Newtonsoft.Json.dll` must be listed explicitly in
`precompiledReferences` — it won't resolve transitively. Same applies to the
test asmdef.

---

## 5. Checklist for New Code

Before writing any C# in this project:

- [ ] Am I in the Runtime or Editor assembly? Check what I can reference.
- [ ] Does this code run on the main thread or a background thread?
- [ ] Am I using any `System.*` namespace? Verify Unity ships it.
- [ ] Am I using any `UnityEngine.Object` static method? Verify it's public and not deprecated.
- [ ] Am I serializing with Newtonsoft? Check for leaked properties (`[JsonIgnore]`).
- [ ] If background thread: am I calling ANY Unity API? (Must cache or dispatch.)
- [ ] If domain reload: am I catching `ThreadAbortException`?
- [ ] Have I searched my code for every `UnityEngine.*` and `UnityEditor.*` call?
