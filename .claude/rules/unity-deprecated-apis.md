# Unity 6 API Quick Reference

The `unity-api-pitfalls` skill auto-loads with complete rules, threading patterns,
and code examples. This file provides the essential quick-reference subset.

## Critical API Rules

| FORBIDDEN | Use Instead |
|---|---|
| `System.Text.Json.*` | `Newtonsoft.Json.*` via `com.unity.nuget.newtonsoft-json` |
| `Object.FindObjectFromInstanceID(id)` | `EditorUtility.InstanceIDToObject(id)` (editor-only) |
| `Object.FindObjectsOfType<T>()` | `Object.FindObjectsByType<T>(FindObjectsSortMode.None)` |
| `Object.FindObjectOfType<T>()` | `Object.FindAnyObjectByType<T>()` |

## Threading Rule

ALL Unity APIs are main-thread-only. HttpListener route handlers run on background
threads. Use `MainThreadDispatcher.Invoke()` or cache values at startup.

## Assembly Rule

Runtime assembly cannot reference `UnityEditor.*` — use `#if UNITY_EDITOR` guards.
