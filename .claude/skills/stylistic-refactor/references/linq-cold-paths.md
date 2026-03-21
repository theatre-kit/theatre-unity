# Style: LINQ in Cold Paths Only

> Use LINQ for readability in tool handlers and editor code; foreach in hot paths like spatial indexing and recording.

## Motivation

LINQ allocates enumerator objects and delegate closures, causing GC pressure. In Theatre's
MCP tool handlers — which run once per agent request — this is negligible. A clear
`.Where().Select().ToList()` can replace 6+ lines of foreach-with-temp-list. But in spatial
indexing, recording, and any code that runs per-frame or per-rebuild, foreach with pre-allocated
lists is the right choice.

## Before / After

### From this codebase: SpatialIndex.Nearest (hot path — keep foreach)

This is a **correct** use of foreach. Do NOT refactor to LINQ:
```csharp
// Hot path — spatial query runs on every agent request with O(n) entries
foreach (var entry in _entries)
{
    if (filter != null && !filter(entry))
        continue;

    float dist = Vector3.Distance(entry.Position, origin);
    if (maxDistance > 0f && dist > maxDistance)
        continue;

    results.Add(new SpatialResult { Entry = entry, Distance = dist });
}
```

### Synthetic example: tool handler collecting component names (cold path)

**Before** (verbose foreach in a cold path):
```csharp
var componentNames = new List<string>();
foreach (var component in go.GetComponents<Component>())
{
    if (component == null) continue;
    componentNames.Add(component.GetType().Name);
}
return componentNames.ToArray();
```

**After** (LINQ is clearer here):
```csharp
return go.GetComponents<Component>()
    .Where(c => c != null)
    .Select(c => c.GetType().Name)
    .ToArray();
```

### Synthetic example: filtering tool registrations (cold path)

**Before:**
```csharp
var enabled = new List<ToolRegistration>();
foreach (var tool in _tools.Values)
{
    if (tool.Group == null || enabledGroups.Contains(tool.Group))
        enabled.Add(tool);
}
```

**After:**
```csharp
var enabled = _tools.Values
    .Where(t => t.Group == null || enabledGroups.Contains(t.Group))
    .ToList();
```

## Exceptions

- **Spatial index rebuild / query**: Always foreach — runs on potentially 10K+ entries
- **Recording / dashcam**: Always foreach — runs per-frame
- **Token budgeting loops**: Always foreach — tight inner loops with early exit
- **When foreach is equally readable**: If the loop body is 2-3 lines, LINQ may not add clarity — use judgment

## Scope

- Applies to: tool handlers (`Execute` methods), editor UI code, test setup, one-shot operations
- Does NOT apply to: `SpatialIndex`, `TokenBudget`, `Recording/`, any code in a per-frame or per-rebuild path
