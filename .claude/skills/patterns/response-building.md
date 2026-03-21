# Pattern: Response Building

All tool responses are `JObject` instances serialized to JSON strings. Helper methods in `ResponseHelpers` centralize frame context, identity, vector serialization, and error formatting.

## Rationale

Every response must include frame context so agents can correlate tool calls with game state. Vector3/Quaternion/Color types need consistent precision and array format. Error responses need a consistent `{ error: { code, message, suggestion } }` envelope. Centralizing this eliminates drift across 15+ tool files.

## Examples

### Example 1: Mutation action response (canonical shape)
**File**: `Editor/Tools/Actions/ActionTeleport.cs:73-89`
```csharp
var response = new JObject();
response["result"] = "ok";
ResponseHelpers.AddIdentity(response, go);           // path + instance_id
response["position"] = ResponseHelpers.ToJArray(newPos);          // [x, y, z]
response["previous_position"] = ResponseHelpers.ToJArray(oldPos);
ResponseHelpers.AddFrameContext(response);           // frame, time, play_mode
return response.ToString(Newtonsoft.Json.Formatting.None);
```

### Example 2: Error early-exit pattern
**File**: `Editor/Tools/Actions/ActionSetProperty.cs:24-34`
```csharp
if (string.IsNullOrEmpty(componentName))
    return ResponseHelpers.ErrorResponse(
        "invalid_parameter",
        "Missing required 'component' parameter",
        "Provide the component type name, e.g., 'Health', 'Transform'");
```
Error shape: `{ "error": { "code": "...", "message": "...", "suggestion": "..." } }`

### Example 3: Query response with editing context
**File**: `Editor/Tools/SceneSnapshotTool.cs:186-195`
```csharp
var response = new JObject();
response["scene"] = sceneName ?? SceneManager.GetActiveScene().name;
ResponseHelpers.AddFrameContext(response);       // frame, time, play_mode
ResponseHelpers.AddEditingContext(response);     // "context": "scene"|"prefab"
response["focus"] = ResponseHelpers.ToJArray(focus.Value);
```

### Example 4: Play-mode guard
**File**: `Editor/Tools/Actions/ActionInvokeMethod.cs:25-26`
```csharp
var error = ResponseHelpers.RequirePlayMode("invoke_method");
if (error != null) return error;
```

## ResponseHelpers reference

| Method | Purpose | Location |
|--------|---------|---------|
| `AddFrameContext(JObject)` | Adds `frame`, `time`, `play_mode` | ResponseHelpers.cs:20 |
| `AddEditingContext(JObject)` | Adds `context` (scene/prefab), `prefab_path` | ResponseHelpers.cs:30 |
| `AddIdentity(JObject, GameObject)` | Adds `path` + `instance_id` | ResponseHelpers.cs:113 |
| `ErrorResponse(code, msg, suggestion)` | Returns `{ error: { ... } }` string | ResponseHelpers.cs:97 |
| `RequirePlayMode(opName)` | Returns error string if not playing, null if OK | ResponseHelpers.cs:125 |
| `ToJArray(Vector3/2/Color)` | Serializes to `[x, y, z]` arrays | ResponseHelpers.cs:52-92 |
| `QuaternionToJArray(Quaternion)` | Serializes to `[x, y, z, w]` | ResponseHelpers.cs:73 |
| `GetHierarchyPath(Transform)` | Full path with multi-scene + disambiguation | ResponseHelpers.cs:138 |

## When to Use
- Every single tool response goes through these helpers — no exceptions
- `AddFrameContext` on every response (even errors don't need it, but success responses always do)
- `AddEditingContext` on scene-scoped responses (snapshot, hierarchy, inspect)
- `AddIdentity` whenever a `GameObject` is the primary subject of a response

## When NOT to Use
- Don't add `AddFrameContext` to error responses (early-exit before any Unity state is read)
- Don't use `ToJArray` for non-spatial arrays (e.g., a list of strings)

## Common Violations
- Manually building `response["path"] = ...; response["instance_id"] = go.GetInstanceID()` — use `AddIdentity` instead
- Inline `if (!Application.isPlaying) return ResponseHelpers.ErrorResponse(...)` — use `RequirePlayMode` instead
- Raw `#pragma warning disable CS0618` around `GetInstanceID()` outside of `ResponseHelpers` — the pragma lives in `AddIdentity`, nowhere else
