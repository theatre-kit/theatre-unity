# Pattern: Compound Tool Dispatch

A single registered tool handles multiple related operations by switching on an `"operation"` string parameter and delegating to dedicated static sub-handler classes or methods.

## Rationale

Grouping related operations into one MCP tool reduces tool-list noise and keeps related schemas together. The switch expression clearly enumerates all valid operations and produces a uniform unknown-operation error for anything else. Each operation is isolated in its own class for testability.

## Examples

### Example 1: ActionTool — 8 operations dispatched to separate classes
**File**: `Editor/Tools/Actions/ActionTool.cs:102-151`
```csharp
private static string Execute(JToken arguments)
{
    var args = (JObject)arguments;
    var operation = args["operation"]?.Value<string>();

    if (string.IsNullOrEmpty(operation))
        return ResponseHelpers.ErrorResponse("invalid_parameter", "Missing 'operation'", "...");

    try
    {
        return operation switch
        {
            "teleport"       => ActionTeleport.Execute(args),
            "set_property"   => ActionSetProperty.Execute(args),
            "set_active"     => ActionSetActive.Execute(args),
            "set_timescale"  => ActionSetTimescale.Execute(args),
            "pause"          => ActionPlayControl.ExecutePause(args),
            "step"           => ActionPlayControl.ExecuteStep(args),
            "unpause"        => ActionPlayControl.ExecuteUnpause(args),
            "invoke_method"  => ActionInvokeMethod.Execute(args),
            _ => ResponseHelpers.ErrorResponse("invalid_parameter",
                $"Unknown operation '{operation}'", "Valid operations: ...")
        };
    }
    catch (Exception ex)
    {
        Debug.LogError($"[Theatre] action:{operation} failed: {ex}");
        return ResponseHelpers.ErrorResponse("internal_error", $"action:{operation} failed: {ex.Message}", "...");
    }
}
```

### Example 2: SpatialQueryTool — 7 spatial operations dispatched to dedicated classes
**File**: `Editor/Tools/Spatial/SpatialQueryTool.cs:180-234`
```csharp
return operation switch
{
    "nearest"       => SpatialQueryNearest.Execute(args),
    "radius"        => SpatialQueryRadius.Execute(args),
    "overlap"       => SpatialQueryOverlap.Execute(args),
    "raycast"       => SpatialQueryRaycast.Execute(args),
    "linecast"      => SpatialQueryLinecast.Execute(args),
    "path_distance" => SpatialQueryPathDistance.Execute(args),
    "bounds"        => SpatialQueryBounds.Execute(args),
    _ => ResponseHelpers.ErrorResponse("invalid_parameter", $"Unknown operation '{operation}'", "...")
};
```

### Example 3: WatchTool — 4 operations dispatched to private methods
**File**: `Editor/Tools/Watch/WatchTool.cs:156-166`
```csharp
return operation switch
{
    "create" => ExecuteCreate(args),
    "remove" => ExecuteRemove(args),
    "list"   => ExecuteList(args),
    "check"  => ExecuteCheck(args),
    _ => ResponseHelpers.ErrorResponse("invalid_parameter", $"Unknown operation '{operation}'", "...")
};
```

### Example 4: SceneHierarchyTool — 4 hierarchy operations
**File**: `Editor/Tools/Scene/SceneHierarchyTool.cs:109-119`
```csharp
return operation switch
{
    "list"   => ExecuteList(args),
    "find"   => ExecuteFind(args),
    "search" => ExecuteSearch(args),
    "path"   => ExecutePath(args),
    _ => ResponseHelpers.ErrorResponse("invalid_parameter", $"Unknown operation '{operation}'", "...")
};
```

## When to Use
- When a tool has 3+ closely related operations that share parameters (path, instance_id, budget, etc.)
- When each operation is complex enough to warrant its own file/class
- The `operation` parameter is always listed as `"required"` in the JSON schema

## When NOT to Use
- Unrelated tools that happen to be similar — give them separate registrations
- Simple tools with a single action — no dispatch needed

## Common Violations
- Missing the catch-all `_` case — every switch must return an error for unknown operations
- Forgetting the top-level `try/catch` in the parent dispatcher — each sub-handler has its own error handling, but the parent catches unexpected panics
- Sub-handlers that register themselves instead of being called from the parent
