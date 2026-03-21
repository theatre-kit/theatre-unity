# Pattern: Action Sub-Handler

An action sub-handler is a `static class` with a single `public static string Execute(JObject args)` method (or named variants like `ExecutePause`). Follows a strict ordering: optional play-mode guard → param validation → ResolveFromArgs → Undo → mutate → build response.

## Rationale

All action operations (`action:teleport`, `action:set_active`, etc.) are dispatched by `ActionTool` via a switch statement. Each sub-handler is self-contained with no registry entry. The consistent ordering ensures: agents get actionable error messages before any mutation, Undo is registered before any change, and the response always reflects the post-mutation state.

## Examples

### Example 1: No play-mode requirement, GameObject mutation
**File**: `Editor/Tools/Actions/ActionSetActive.cs`
```csharp
internal static class ActionSetActive
{
    public static string Execute(JObject args)
    {
        // 1. Validate operation-specific params first
        var active = args["active"];
        if (active == null)
            return ResponseHelpers.ErrorResponse(
                "invalid_parameter",
                "Missing required 'active' parameter (true/false)",
                "Example: {\"operation\": \"set_active\", \"path\": \"/Enemy\", \"active\": false}");

        // 2. Resolve the target GameObject
        var resolveError = ObjectResolver.ResolveFromArgs(args, out var go);
        if (resolveError != null) return resolveError;

        var previousActive = go.activeSelf;
        var newActive = active.ToObject<bool>();

        // 3. Undo in edit mode
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RecordObject(go, "Theatre SetActive");
#endif

        // 4. Mutate
        go.SetActive(newActive);

        // 5. Build response
        var response = new JObject();
        response["result"] = "ok";
        ResponseHelpers.AddIdentity(response, go);
        response["active"] = newActive;
        response["previous_active"] = previousActive;
        ResponseHelpers.AddFrameContext(response);

        return response.ToString(Newtonsoft.Json.Formatting.None);
    }
}
```

### Example 2: Play-mode required, no GameObject (state mutation)
**File**: `Editor/Tools/Actions/ActionPlayControl.cs`
```csharp
internal static class ActionPlayControl
{
    // Multiple named Execute variants for each sub-operation
    public static string ExecutePause(JObject args)
    {
        // 1. Play mode guard first
        var error = ResponseHelpers.RequirePlayMode("pause");
        if (error != null) return error;

        // 2. No GameObject to resolve — mutate directly
#if UNITY_EDITOR
        EditorApplication.isPaused = true;
#endif

        // 3. Build response (no AddIdentity — no GameObject)
        var response = new JObject();
        response["result"] = "ok";
        response["operation"] = "pause";
        response["paused"] = true;
        ResponseHelpers.AddFrameContext(response);
        return response.ToString(Newtonsoft.Json.Formatting.None);
    }
}
```

### Example 3: Play-mode required + ResolveFromArgs + named Execute variants
**File**: `Editor/Tools/Actions/ActionInvokeMethod.cs:24-50`
```csharp
public static string Execute(JObject args)
{
    // 1. Play mode guard
    var error = ResponseHelpers.RequirePlayMode("invoke_method");
    if (error != null) return error;

    // 2. Validate operation-specific params
    var componentName = args["component"]?.Value<string>();
    if (string.IsNullOrEmpty(componentName))
        return ResponseHelpers.ErrorResponse("invalid_parameter", "...", "...");

    // 3. Resolve GameObject
    var resolveError = ObjectResolver.ResolveFromArgs(args, out var go);
    if (resolveError != null) return resolveError;

    // 4. No Undo for reflection calls — they don't have Undo support
    // 5. Mutate (method invocation)
    // 6. Build response with AddIdentity + AddFrameContext
}
```

## Ordering Rules (MANDATORY)

1. `RequirePlayMode` — if operation only works in play mode
2. Validate operation-specific required params (position, component name, etc.)
3. `ObjectResolver.ResolveFromArgs(args, out var go)` — if a GameObject is involved
4. Validate any params that depend on the resolved object
5. `Undo.RecordObject(...)` — always inside `#if UNITY_EDITOR`, always before mutation
6. Mutate the object
7. Build response: `response["result"] = "ok"` → `AddIdentity` → data fields → `AddFrameContext` last

## When to Use
- Every `action:*` sub-operation
- Handlers that mutate GameObject state, components, or Editor play mode

## When NOT to Use
- Read-only scene queries (`scene_hierarchy`, `scene_inspect`) — no mutation, no Undo
- Spatial queries — use Spatial Index Sub-Handler pattern instead
- The parent `ActionTool.Register/Execute` — that follows the Compound Tool Dispatch pattern

## Common Violations
- Calling `Undo.RecordObject` AFTER the mutation — Undo must be registered before
- Missing `#if UNITY_EDITOR` around `Undo.*` — Undo is editor-only
- Putting `RequirePlayMode` after `ResolveFromArgs` — always guard first, resolve second
- Using `go.GetInstanceID()` directly instead of `AddIdentity` — use `AddIdentity`
