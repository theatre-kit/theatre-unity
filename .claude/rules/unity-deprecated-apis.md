# Unity 6 Deprecated & Missing API Rules

Never use the APIs in the left column. Always use the replacement in the right column.

## Missing APIs (do not exist in Unity 6 public API)

| FORBIDDEN | Replacement |
|---|---|
| `Object.FindObjectFromInstanceID(id)` | `EditorUtility.InstanceIDToObject(id)` (editor-only, wrap in `#if UNITY_EDITOR`) |
| `System.Text.Json.*` (entire namespace) | `Newtonsoft.Json.*` via `com.unity.nuget.newtonsoft-json` |
| `System.Text.Json.JsonSerializer` | `Newtonsoft.Json.JsonConvert` |
| `System.Text.Json.JsonElement` | `Newtonsoft.Json.Linq.JToken` |
| `System.Text.Json.Utf8JsonWriter` | `Newtonsoft.Json.Linq.JObject` / `JArray` |
| `[JsonPropertyName("x")]` | `[JsonProperty("x")]` |
| `[JsonIgnore(Condition = JsonIgnoreCondition.*)]` | `[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]` |

## Deprecated APIs (compile with warnings, will be removed)

| DEPRECATED | Replacement |
|---|---|
| `Object.FindObjectsOfType<T>()` | `Object.FindObjectsByType<T>(FindObjectsSortMode.None)` |
| `Object.FindObjectOfType<T>()` | `Object.FindFirstObjectByType<T>()` or `Object.FindAnyObjectByType<T>()` |
| `Object.FindObjectsOfType(Type)` | `Object.FindObjectsByType(Type, FindObjectsSortMode.None)` |
| `Object.FindObjectOfType(Type)` | `Object.FindAnyObjectByType(Type)` |
| `Object.GetInstanceID()` | **KEEP USING** — deprecated in 6.4 but `GetEntityId()` returns `EntityId` struct (not int), breaking wire format. Suppress warning with `#pragma warning disable CS0618`. Revisit when `EntityId` stabilizes. |

## Newtonsoft Serialization Gotchas

| Problem | Fix |
|---|---|
| Public computed properties leak into JSON | Add `[JsonIgnore]` to non-data properties (e.g., `bool IsRequest => ...`) |
| Default values serialized unnecessarily | `[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]` |
| `jtoken.Value<T>()` fails without key arg | Use `jtoken.ToObject<T>()` or cast `(int)jtoken` instead |
| `overrideReferences` hides transitive DLLs | Test asmdefs must list `"Newtonsoft.Json.dll"` in `precompiledReferences` |
| Field names not snake_case | `[JsonProperty("snake_case_name")]` on every serialized property |

## Assembly Boundary Rules

| Context | Rule |
|---|---|
| Runtime assembly (`*.runtime.asmdef`) | Cannot reference `UnityEditor.*` — use `#if UNITY_EDITOR` guard |
| Editor assembly (`*.editor.asmdef`) | Can reference both `UnityEngine.*` and `UnityEditor.*` |
| `EditorUtility.InstanceIDToObject` | Editor-only — must be behind `#if UNITY_EDITOR` if called from Runtime |
| `PrefabStageUtility.GetCurrentPrefabStage()` | Editor-only — must be behind `#if UNITY_EDITOR` if called from Runtime |
| `SerializedObject` / `SerializedProperty` | Editor-only — can only be used in Editor assembly files |

### Cross-Assembly Visibility (CRITICAL — we've hit this multiple times)

| Rule | Detail |
|---|---|
| `internal` in Runtime → invisible to Editor | Editor assembly references Runtime, but `internal` types/members are not visible. Use `public` for any Runtime type that Editor code needs to call. |
| `internal` in Editor → invisible to Tests | Test assembly references Editor, but `internal` types are not visible. We have `[assembly: InternalsVisibleTo("com.theatre.toolkit.editor.tests")]` in `Editor/AssemblyInfo.cs` to allow tests to call internal handlers directly. |
| `overrideReferences: true` hides ALL transitive DLLs | When ANY asmdef sets `overrideReferences: true`, it ONLY sees DLLs in its `precompiledReferences` list. If Runtime asmdef has this flag, `Newtonsoft.Json.dll` MUST be listed explicitly — it won't come through transitively from the UPM package dependency. |
| Adding a new precompiled DLL | When adding a new DLL to `Plugins/`, you must add it to `precompiledReferences` in EVERY asmdef that has `overrideReferences: true` and needs it (currently: runtime + test asmdefs). |

## Test Assembly Rules

| Rule | Detail |
|---|---|
| `testables` in project manifest | Required for local (`file:`) packages: `"testables": ["com.theatre.toolkit"]` |
| `overrideReferences: true` | Test asmdefs only see DLLs listed in `precompiledReferences` — transitive deps don't come through |
| `precompiledReferences` must include | `"nunit.framework.dll"` AND `"Newtonsoft.Json.dll"` AND any SQLite DLLs if tests use them |
| `InternalsVisibleTo` | `Editor/AssemblyInfo.cs` grants test assembly access to `internal` types in the editor assembly. If you add a new editor assembly, add a corresponding `InternalsVisibleTo`. |
| Test discovery requires clean compile | If ANY `.cs` file in the test assembly has a compile error, ALL tests in that assembly become invisible to the test runner — no error is shown, tests simply disappear from the count. Always check `unity_console {"filter": "error"}` if test count drops unexpectedly. |

## Main-Thread-Only APIs (throw UnityException from background threads)

These APIs are valid but MUST only be called from the main thread.
HttpListener route handlers run on background threads — use caching or
`MainThreadDispatcher.Invoke()`.

- `SessionState.*` (GetInt, SetInt, etc.)
- `TheatreConfig.Port`, `.EnabledGroups`, `.HttpPrefix` (backed by SessionState)
- `EditorApplication.isPlaying`, `.isPaused`
- `SceneManager.*` (GetActiveScene, GetSceneAt, sceneCount)
- `Debug.Log`, `Debug.LogWarning`, `Debug.LogError`, `Debug.LogException`
- `Time.frameCount`, `Time.time`, `Application.isPlaying`
- All `GameObject`, `Transform`, `Component` property access
- `AssetDatabase.*`, `Undo.*`, `PrefabUtility.*`, `EditorPrefs.*`
