---
name: unity-api-pitfalls
description: "Unity 6 API rules, threading idioms, and gotchas for Theatre editor extensions. Auto-loads when working with
  Unity C# scripts, UnityEngine, UnityEditor, GameObject, Transform, Component, SerializedProperty,
  EditorUtility, AssetDatabase, PrefabUtility, SceneManager, Object.FindObjectFromInstanceID,
  Object.FindObjectsOfType, System.Text.Json, JsonUtility, Newtonsoft, SessionState, HttpListener,
  background threads, domain reload, ThreadAbortException, #if UNITY_EDITOR, Runtime assembly,
  Editor assembly, asmdef, MainThreadDispatcher, TheatreServer, TheatreConfig, InitializeOnLoad,
  UnityException, main thread dispatch, caching pattern.
  Contains API availability rules, assembly boundaries, threading patterns, deprecated method replacements,
  serialization gotchas, and a mandatory checklist for new code."
user-invocable: false
---

# Unity 6 API Rules for Theatre

See [api-rules.md](api-rules.md) for API replacements, assembly boundaries, and serialization.
See [threading.md](threading.md) for threading patterns, domain reload, and test setup.

## Key Rules

- **System.Text.Json does NOT exist in Unity 6.** Use `Newtonsoft.Json` via `com.unity.nuget.newtonsoft-json`.
- **ALL Unity APIs are main-thread-only.** HttpListener callbacks run on thread pool threads. Cache or dispatch.
- **Runtime assemblies cannot reference UnityEditor.** Use `#if UNITY_EDITOR` guards.
- **`Object.FindObjectFromInstanceID` is internal.** Use `EditorUtility.InstanceIDToObject`.
- **`Object.FindObjectsOfType<T>()` is deprecated.** Use `FindObjectsByType<T>(FindObjectsSortMode)`.
- **Newtonsoft serializes ALL public properties.** Use `[JsonIgnore]` on computed ones.
- **Domain reload kills all threads.** Catch `ThreadAbortException`. Restart via `[InitializeOnLoad]`.
- **SessionState is main-thread-only.** `TheatreConfig.Port` and `.EnabledGroups` use it — cache at startup.

## Quick Checklist

1. Which assembly am I in? Runtime can't use `UnityEditor.*`.
2. Which thread am I on? Route handlers = background. Tool handlers = main.
3. Does `System.X` exist in Unity? (System.Text.Json: NO)
4. Is this Unity API public and non-deprecated? (Check Unity 6 docs)
5. Am I leaking properties via Newtonsoft? (`[JsonIgnore]`)
6. Background thread + Unity API = BUG. Cache or dispatch.
7. UPM package tests need `"testables": ["com.theatre.toolkit"]` in project manifest.
