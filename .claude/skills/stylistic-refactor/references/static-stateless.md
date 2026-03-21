# Style: Static Stateless Classes

> Prefer static classes for stateless operations; only use instances when state is needed.

## Motivation

Theatre's utility classes — resolvers, walkers, serializers — are pure functions operating on
inputs and returning outputs. Making them `static` communicates that they hold no state,
eliminates unnecessary instantiation, and prevents accidental statefulness. Instance classes
are reserved for things that genuinely manage state (`SpatialIndex`, `WatchEngine`,
`TokenBudget`).

## Before / After

### From this codebase: ObjectResolver (static, correct)

```csharp
public static class ObjectResolver
{
    public static ResolveResult Resolve(string path = null, int? instanceId = null)
    {
        // Pure function: takes input, returns result, no instance state
    }
}
```

### From this codebase: SpatialIndex (instance, correct)

```csharp
public sealed class SpatialIndex
{
    private List<SpatialEntry> _entries;  // State: cached spatial data

    public List<SpatialResult> Nearest(Vector3 origin, int count, ...)
    {
        EnsureFresh();  // Manages its own cache lifecycle
        // ...
    }
}
```

### Synthetic example: unnecessary instance class

**Before** (instance class with no state):
```csharp
public class PathNormalizer
{
    public string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        return path.StartsWith("/") ? path : "/" + path;
    }

    public string[] Split(string path)
    {
        return Normalize(path).Split('/');
    }
}

// Caller must instantiate for no reason
var normalizer = new PathNormalizer();
var normalized = normalizer.Normalize(path);
```

**After** (static — no state, no instantiation):
```csharp
public static class PathNormalizer
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        return path.StartsWith("/") ? path : "/" + path;
    }

    public static string[] Split(string path)
    {
        return Normalize(path).Split('/');
    }
}

// Direct call
var normalized = PathNormalizer.Normalize(path);
```

## Exceptions

- **Classes that cache or accumulate**: `SpatialIndex`, `WatchEngine`, `TokenBudget` — these hold state and need instances
- **Classes designed for dependency injection**: If a class needs to be swapped in tests, an interface + instance may be appropriate
- **MonoBehaviours and ScriptableObjects**: Unity requires these to be instances; don't fight the framework

## Scope

- Applies to: resolvers, walkers, serializers, validators, converters, response builders
- Does NOT apply to: Unity components, stateful engines, classes with lifecycle management
