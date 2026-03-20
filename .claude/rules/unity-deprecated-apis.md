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

## Newtonsoft Serialization Gotchas

| Problem | Fix |
|---|---|
| Public computed properties leak into JSON | Add `[JsonIgnore]` to non-data properties (e.g., `bool IsRequest => ...`) |
| Default values serialized unnecessarily | `[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]` |
| Field names not snake_case | `[JsonProperty("snake_case_name")]` on every serialized property |

## Assembly Boundary Rules

| Context | Rule |
|---|---|
| Runtime assembly (`*.runtime.asmdef`) | Cannot reference `UnityEditor.*` — use `#if UNITY_EDITOR` guard |
| Editor assembly (`*.editor.asmdef`) | Can reference both `UnityEngine.*` and `UnityEditor.*` |
| `EditorUtility.InstanceIDToObject` | Editor-only — must be behind `#if UNITY_EDITOR` if called from Runtime |
| `PrefabStageUtility.GetCurrentPrefabStage()` | Editor-only — must be behind `#if UNITY_EDITOR` if called from Runtime |
| `SerializedObject` / `SerializedProperty` | Editor-only — can only be used in Editor assembly files |

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
