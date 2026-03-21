# Style: Result Types Over Exceptions

> Use result structs and error returns for expected failures; reserve exceptions for truly unrecoverable situations only.

## Motivation

Theatre is an MCP server where tool handlers receive arbitrary agent input. Invalid paths,
missing objects, and bad parameters are *expected* — not exceptional. Modeling these as return
values makes error paths explicit, avoids try/catch overhead, and ensures callers always handle
the failure case. Exceptions are reserved for programmer errors and system failures.

## Before / After

### From this codebase: ObjectResolver.Resolve

**Before** (anti-pattern — exceptions for expected failures):
```csharp
public static GameObject Resolve(string path, int? instanceId)
{
    if (path == null && instanceId == null)
        throw new ArgumentException("Either 'path' or 'instance_id' must be provided");

    var obj = GameObject.Find(path);
    if (obj == null)
        throw new KeyNotFoundException($"No object at path '{path}'");

    return obj;
}
```

**After** (actual codebase pattern):
```csharp
public static ResolveResult Resolve(string path = null, int? instanceId = null)
{
    if (path == null && instanceId == null)
    {
        return new ResolveResult(
            "invalid_parameter",
            "Either 'path' or 'instance_id' must be provided",
            "Provide a hierarchy path like '/Player' or an instance_id from a previous query");
    }

    // ... resolution logic ...
}

public readonly struct ResolveResult
{
    public readonly GameObject GameObject;
    public readonly string ErrorCode;
    public readonly string ErrorMessage;
    public readonly string Suggestion;
    public bool Success => GameObject != null;
}
```

### Synthetic example: tool handler validation

**Before:**
```csharp
public static string Execute(JToken args)
{
    try
    {
        var radius = args["radius"]?.Value<float>()
            ?? throw new ArgumentException("radius is required");
        // ... query logic ...
    }
    catch (ArgumentException ex)
    {
        return ResponseHelpers.ErrorResponse("invalid_parameter", ex.Message);
    }
}
```

**After:**
```csharp
public static string Execute(JToken args)
{
    var radius = args["radius"]?.Value<float>();
    if (!radius.HasValue)
        return ResponseHelpers.ErrorResponse("invalid_parameter",
            "radius is required", "Provide radius as a float in world units");

    // ... query logic, no try/catch needed ...
}
```

## Exceptions

- **ThreadAbortException / domain reload**: Must be caught to exit cleanly
- **External API failures** (file I/O, network): Try/catch is appropriate when the failure is truly unpredictable
- **Programmer errors** (null reference, index out of range): Let these throw — they indicate bugs, not expected input

## Scope

- Applies to: all tool handlers, resolvers, serializers, and internal APIs
- Does NOT apply to: Unity lifecycle callbacks (Awake, OnEnable) where Unity itself controls the flow
