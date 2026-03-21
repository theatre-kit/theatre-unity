# Style: Guard Clauses and Early Returns

> Validate preconditions at the top of methods; return early on failure; max 2-3 levels of nesting.

## Motivation

Guard clauses make the "happy path" the main flow of the method. Deeply nested conditionals
force readers to track multiple branches simultaneously. In Theatre's tool handlers, where
parameter validation is the first thing that happens, early returns keep methods flat and
readable. The 2-3 nesting level limit is a practical guideline, not a hard rule — but if
you hit 4+ levels, consider extracting a helper.

## Before / After

### From this codebase: ObjectResolver.Resolve

```csharp
public static ResolveResult Resolve(string path = null, int? instanceId = null)
{
    // Guard: validate inputs first
    if (path == null && instanceId == null)
    {
        return new ResolveResult(
            "invalid_parameter",
            "Either 'path' or 'instance_id' must be provided",
            "Provide a hierarchy path like '/Player' or an instance_id");
    }

    // Guard: prefer instance ID path
    if (instanceId.HasValue)
    {
        return ResolveByInstanceId(instanceId.Value);
    }

    // Happy path: resolve by path
    return ResolveByPath(path);
}
```

### From this codebase: HierarchyWalker.List

```csharp
public static (List<HierarchyEntry> entries, int total) List(
    string parentPath, bool includeInactive, int offset, int limit)
{
    var entries = new List<HierarchyEntry>();

    // Guard: root-level listing is a different path
    if (string.IsNullOrEmpty(parentPath))
    {
        return ListSceneRoots(includeInactive, offset, limit);
    }

    // Guard: resolve parent or fail
    var resolved = ObjectResolver.Resolve(path: parentPath);
    if (!resolved.Success)
        return (entries, 0);

    // Happy path: iterate children
    var parent = resolved.GameObject.transform;
    // ...
}
```

### Synthetic example: deeply nested validation

**Before** (nested conditionals):
```csharp
public static string Execute(JToken args)
{
    if (args != null)
    {
        var path = args["path"]?.Value<string>();
        if (path != null)
        {
            var resolved = ObjectResolver.Resolve(path: path);
            if (resolved.Success)
            {
                var component = resolved.GameObject.GetComponent(typeName);
                if (component != null)
                {
                    return SerializeComponent(component);
                }
                else
                {
                    return ErrorResponse("not_found", "Component not found");
                }
            }
            else
            {
                return ErrorResponse(resolved.ErrorCode, resolved.ErrorMessage);
            }
        }
        else
        {
            return ErrorResponse("invalid_parameter", "path is required");
        }
    }
    return ErrorResponse("invalid_parameter", "args is null");
}
```

**After** (guard clauses):
```csharp
public static string Execute(JToken args)
{
    if (args == null)
        return ErrorResponse("invalid_parameter", "args is null");

    var path = args["path"]?.Value<string>();
    if (path == null)
        return ErrorResponse("invalid_parameter", "path is required");

    var resolved = ObjectResolver.Resolve(path: path);
    if (!resolved.Success)
        return ErrorResponse(resolved.ErrorCode, resolved.ErrorMessage);

    var component = resolved.GameObject.GetComponent(typeName);
    if (component == null)
        return ErrorResponse("not_found", "Component not found");

    return SerializeComponent(component);
}
```

## Exceptions

- **Using statements / try-finally**: These add a nesting level but are structural, not conditional — don't count them
- **Switch/case blocks**: A switch with early returns in each case is fine; nesting inside cases should still be minimal
- **LINQ lambdas**: A `.Where(x => ...)` adds visual nesting but not cognitive nesting — don't count it

## Scope

- Applies to: all methods, especially tool handlers, resolvers, and serializers
- Does NOT apply to: deeply nested data structure traversal where recursion is the natural pattern (tree walks)
