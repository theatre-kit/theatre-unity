# Pattern: Physics Mode 2D/3D Dispatch

Physics-based spatial queries call `PhysicsMode.GetEffective(args["physics"])` to determine whether to run Unity 3D or 2D physics APIs, then dispatch to separate `Execute3D`/`Execute2D` private methods.

## Rationale

Unity has completely separate 3D (`Physics.*`, `Collider`) and 2D (`Physics2D.*`, `Collider2D`) physics APIs. A single spatial query tool must handle both. `PhysicsMode.GetEffective()` checks scene content and the caller's override to pick the right physics stack. Dispatching to separate methods keeps each physics path clean without interleaving 2D/3D branching throughout.

## Examples

### Example 1: SpatialQueryOverlap dispatch
**File**: `Editor/Tools/Spatial/SpatialQueryOverlap.cs:17-37`
```csharp
internal static string Execute(JObject args)
{
    var shape = args["shape"]?.Value<string>();
    if (string.IsNullOrEmpty(shape))
        return ResponseHelpers.ErrorResponse("invalid_parameter", "...", "...");

    // Determine 2D or 3D physics mode
    var physicsMode = PhysicsMode.GetEffective(args["physics"]?.Value<string>());
    int layerMask = args["layer_mask"]?.Value<int>() ?? Physics.DefaultRaycastLayers;

    // Dispatch to dimension-specific implementation
    if (physicsMode == "2d")
        return Execute2D(args, shape, layerMask);
    else
        return Execute3D(args, shape, layerMask);
}

private static string Execute3D(JObject args, string shape, int layerMask)
{
    // Uses Physics.OverlapSphere, Physics.OverlapBox, etc.
}

private static string Execute2D(JObject args, string shape, int layerMask)
{
    // Uses Physics2D.OverlapCircle, Physics2D.OverlapBox, etc.
}
```

### Example 2: SpatialQueryRaycast dispatch
**File**: `Editor/Tools/Spatial/SpatialQueryRaycast.cs`
```csharp
internal static string Execute(JObject args)
{
    // ... validate origin, direction ...
    var physicsMode = PhysicsMode.GetEffective(args["physics"]?.Value<string>());
    bool all = args["all"]?.Value<bool>() ?? false;
    float maxDistance = args["max_distance"]?.Value<float>() ?? 1000f;
    int layerMask = args["layer_mask"]?.Value<int>() ?? Physics.DefaultRaycastLayers;

    if (physicsMode == "2d")
        return Execute2D(args, origin2D, direction2D, maxDistance, layerMask, all);
    else
        return Execute3D(args, origin3D, direction3D, maxDistance, layerMask, all);
}
```

### Example 3: SpatialQueryLinecast dispatch
**File**: `Editor/Tools/Spatial/SpatialQueryLinecast.cs`
```csharp
internal static string Execute(JObject args)
{
    // ... validate from, to ...
    var physicsMode = PhysicsMode.GetEffective(args["physics"]?.Value<string>());
    int layerMask = args["layer_mask"]?.Value<int>() ?? Physics.DefaultRaycastLayers;

    if (physicsMode == "2d")
        return Execute2D(args, from2D, to2D, layerMask);
    else
        return Execute3D(args, from3D, to3D, layerMask);
}
```

### Example 4: PhysicsMode.GetEffective API
**File**: `Runtime/Stage/Spatial/PhysicsMode.cs`
```csharp
// PhysicsMode.GetEffective(override):
// - If override is "3d" or "2d" → use it directly
// - If override is null → auto-detect from scene (checks for Rigidbody2D components)
// Returns: "3d" or "2d"
var physicsMode = PhysicsMode.GetEffective(args["physics"]?.Value<string>());
```

## Structure Rules

- Validate any shared params (origin, direction, shape) BEFORE the physics dispatch
- Shared params that differ between 2D and 3D (e.g., Vector3 vs Vector2 origin) are parsed inside each Execute3D/Execute2D
- `layerMask` is always parsed before dispatch and passed to both branches (same API surface)
- Each dimension method builds its own response independently — no shared response object
- Response `operation` field is echoed in both branches

## When to Use
- Any spatial query that calls Unity Physics or Physics2D APIs
- Operations where the 2D/3D execution path diverges significantly (different collider types, different physics calls, different response fields)

## When NOT to Use
- Transform-based spatial queries (`nearest`, `radius`) — those use the SpatialIndex, not Physics APIs. Use Spatial Index Sub-Handler pattern instead.
- `spatial_query:bounds` — reads Renderer/Collider bounds directly, no physics query involved

## Common Violations
- Calling `Physics.Raycast` without checking physics mode — will silently fail on 2D-only scenes
- Duplicating the param validation in both Execute3D and Execute2D — validate shared params once before the branch
- Hard-coding `Physics.DefaultRaycastLayers` as the default without reading `args["layer_mask"]` — always expose layer mask
