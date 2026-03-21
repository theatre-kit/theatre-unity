# Stylistic Refactor Backlog

Generated: 2026-03-20
Styles: Result Types, LINQ Cold Paths, Static Stateless, Guard Clauses

---

## High Value

Low risk, clear improvement in readability or consistency.

### 1. SceneSnapshotTool.FilterByComponents — LINQ simplification

**File:** `Packages/com.theatre.toolkit/Editor/Tools/SceneSnapshotTool.cs:253-279`
**Style:** LINQ Cold Paths

**Current** (15 lines, 4 levels of nesting):
```csharp
private static List<HierarchyEntry> FilterByComponents(
    List<HierarchyEntry> entries, string[] requiredComponents)
{
    var filtered = new List<HierarchyEntry>();
    foreach (var entry in entries)
    {
        if (entry.Components == null) continue;
        bool hasAll = true;
        foreach (var required in requiredComponents)
        {
            bool found = false;
            foreach (var comp in entry.Components)
            {
                if (string.Equals(comp, required, StringComparison.OrdinalIgnoreCase))
                { found = true; break; }
            }
            if (!found) { hasAll = false; break; }
        }
        if (hasAll) filtered.Add(entry);
    }
    return filtered;
}
```

**Proposed:**
```csharp
private static List<HierarchyEntry> FilterByComponents(
    List<HierarchyEntry> entries, string[] requiredComponents)
{
    return entries.Where(entry =>
        entry.Components != null &&
        requiredComponents.All(required =>
            entry.Components.Any(comp =>
                string.Equals(comp, required, StringComparison.OrdinalIgnoreCase))))
        .ToList();
}
```

**Rationale:** Cold path (scene snapshot generation). 15 → 5 lines. Eliminates 4-level nesting and temp booleans. Also fixes a Guard Clauses violation.

---

### 2. ActionInvokeMethod.Execute — LINQ for method matching

**File:** `Packages/com.theatre.toolkit/Editor/Tools/Actions/ActionInvokeMethod.cs:69-89`
**Style:** LINQ Cold Paths

**Current** (17 lines):
```csharp
foreach (var method in methods)
{
    if (method.Name != methodName) continue;
    var parameters = method.GetParameters();
    if (parameters.Length != argCount) continue;
    bool allAllowed = true;
    foreach (var p in parameters)
    {
        if (!IsAllowedType(p.ParameterType))
        { allAllowed = false; break; }
    }
    if (allAllowed)
    { targetMethod = method; break; }
}
```

**Proposed:**
```csharp
targetMethod = methods.FirstOrDefault(method =>
    method.Name == methodName &&
    method.GetParameters().Length == argCount &&
    method.GetParameters().All(p => IsAllowedType(p.ParameterType)));
```

**Rationale:** Cold path (action invocation). Classic `.FirstOrDefault()` candidate. 17 → 4 lines.

---

### 3. ConsoleLogBuffer.GetSummary — LINQ for top-N sorting

**File:** `Packages/com.theatre.toolkit/Editor/Tools/ConsoleLogBuffer.cs:166-170`
**Style:** LINQ Cold Paths

**Current:**
```csharp
var sorted = new List<(string message, int count, LogType type)>();
foreach (var kv in messageCounts)
    sorted.Add((kv.Key, kv.Value.count, kv.Value.type));
sorted.Sort((a, b) => b.count.CompareTo(a.count));
if (sorted.Count > topN) sorted.RemoveRange(topN, sorted.Count - topN);
```

**Proposed:**
```csharp
var sorted = messageCounts
    .Select(kv => (message: kv.Key, count: kv.Value.count, type: kv.Value.type))
    .OrderByDescending(x => x.count)
    .Take(topN)
    .ToList();
```

**Rationale:** Cold path (console summary). `.OrderByDescending().Take()` is idiomatic and eliminates manual truncation.

---

### 4. WatchPersistence.Save/Restore — LINQ for transforms

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/GameObject/WatchPersistence.cs:23-25, 52-61`
**Style:** LINQ Cold Paths

**Current (Save):**
```csharp
var defs = new List<WatchDefinition>();
foreach (var ws in watches)
    defs.Add(ws.Definition);
```

**Proposed (Save):**
```csharp
var defs = watches.Select(ws => ws.Definition).ToList();
```

**Current (Restore):**
```csharp
foreach (var def in defs)
{
    var state = new WatchState { Definition = def, LastTriggeredAt = 0, TriggerCount = 0 };
    watches.Add(state);
}
```

**Proposed (Restore):**
```csharp
watches.AddRange(defs.Select(def => new WatchState
{
    Definition = def, LastTriggeredAt = 0, TriggerCount = 0
}));
```

**Rationale:** Cold path (domain reload persistence). Classic `.Select()` patterns.

---

### 5. SpatialEntryFilter — LINQ for component matching

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/Spatial/SpatialEntryFilter.cs:44-60`
**Style:** LINQ Cold Paths + Guard Clauses

**Current** (nested foreach with temp bool):
```csharp
foreach (var required in includeComponents)
{
    bool found = false;
    if (entry.Components != null)
    {
        foreach (var comp in entry.Components)
        {
            if (string.Equals(comp, required, StringComparison.OrdinalIgnoreCase))
            { found = true; break; }
        }
    }
    if (!found) return false;
}
```

**Proposed:**
```csharp
foreach (var required in includeComponents)
{
    if (entry.Components == null ||
        !entry.Components.Any(comp => string.Equals(comp, required, StringComparison.OrdinalIgnoreCase)))
        return false;
}
```

**Rationale:** Cold path (filter predicate built once per query). Eliminates inner foreach + temp bool.

---

### 6. SpatialQueryBounds.Execute — extract helper to flatten nesting

**File:** `Packages/com.theatre.toolkit/Editor/Tools/SpatialQueryBounds.cs:42-71`
**Style:** Guard Clauses

**Issue:** Collider bounds calculation has 4 levels of nesting (foreach → if → if → assignment).

**Proposed:** Extract `GetColliderBounds(GameObject go)` as a private static method returning `Bounds?`. The caller becomes:
```csharp
var bounds = GetColliderBounds(go);
if (!bounds.HasValue)
    return ResponseHelpers.ErrorResponse("no_bounds", "...");
```

**Rationale:** Flattens the deepest nesting in Execute to 2 levels. Helper is reusable.

---

### 7. TheatreServer tool execution — result type over exception

**File:** `Packages/com.theatre.toolkit/Editor/TheatreServer.cs:156-157`
**Style:** Result Types

**Current:**
```csharp
if (tool == null)
    throw new InvalidOperationException($"Tool '{toolName}' not found or not enabled");
```

**Proposed:** Return an error string (JSON error response) instead of throwing, since "tool not found" is an expected client error, not an exceptional situation. The caller in McpRouter already wraps tool execution in try/catch — but checking the return is cleaner than throwing across the call boundary.

**Rationale:** A client requesting a disabled tool is expected behavior, not an exceptional condition.

---

### 8. PropertySerializer.SerializeComponents — LINQ for component filtering

**File:** `Packages/com.theatre.toolkit/Editor/Tools/PropertySerializer.cs:58-72`
**Style:** LINQ Cold Paths

**Current:**
```csharp
if (componentFilter != null)
{
    bool match = false;
    foreach (var filter in componentFilter)
    {
        if (string.Equals(filter, typeName, StringComparison.OrdinalIgnoreCase))
        { match = true; break; }
    }
    if (!match) continue;
}
```

**Proposed:**
```csharp
if (componentFilter != null &&
    !componentFilter.Any(f => string.Equals(f, typeName, StringComparison.OrdinalIgnoreCase)))
    continue;
```

**Rationale:** Cold path (property serialization). `.Any()` replaces manual search loop + temp bool.

---

### 9. ObjectResolver.FindComponent — LINQ FirstOrDefault

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/GameObject/ObjectResolver.cs:279-286`
**Style:** LINQ Cold Paths

**Current:**
```csharp
foreach (var comp in go.GetComponents<Component>())
{
    if (comp == null) continue;
    if (string.Equals(comp.GetType().Name, componentName, StringComparison.OrdinalIgnoreCase))
        return comp;
}
return null;
```

**Proposed:**
```csharp
return go.GetComponents<Component>()
    .FirstOrDefault(comp => comp != null &&
        string.Equals(comp.GetType().Name, componentName, StringComparison.OrdinalIgnoreCase));
```

**Rationale:** Cold path (component lookup). Classic `.FirstOrDefault()`.

---

### 10. JsonParamParser.ParseStringArray — LINQ Select

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/JsonParamParser.cs:57-60`
**Style:** LINQ Cold Paths

**Current:**
```csharp
var list = new List<string>();
foreach (var item in (JArray)token)
    list.Add(item.Value<string>());
return list.Count > 0 ? list.ToArray() : null;
```

**Proposed:**
```csharp
var arr = ((JArray)token).Select(item => item.Value<string>()).ToArray();
return arr.Length > 0 ? arr : null;
```

**Rationale:** Cold path (parameter parsing). 3 → 2 lines.

---

## Worth Considering

Valid refactors with moderate impact or moderate effort.

### 11. SpatialEntryFilter tag exclusion — LINQ Any

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/Spatial/SpatialEntryFilter.cs:35-40`

Replace tag-exclusion foreach with `.Any()`. Small improvement (4 → 2 lines), but the existing code is already readable. Marginal gain.

### 12. SceneSnapshotTool component filtering in response — LINQ Where

**File:** `Packages/com.theatre.toolkit/Editor/Tools/SceneSnapshotTool.cs:225-229`

Filter `entry.Components` to exclude "Transform" with `.Where()`. Only saves 2 lines. Current code is clear.

### 13. ConsoleLogBuffer.Query grep filtering — extract helper

**File:** `Packages/com.theatre.toolkit/Editor/Tools/ConsoleLogBuffer.cs:111-124`

The grep matching logic has 3 levels of nesting inside a lock + loop. Extracting a `MatchesGrep()` helper would flatten it, but the code is within a lock block so restructuring needs care.

### 14. ResponseHelpers.GetLocalPath duplicate counting

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/ResponseHelpers.cs:187-194`

Could use `.Count()` and `Array.FindIndex()` instead of manual loop. But the current loop does both operations in a single pass, which is technically more efficient.

---

## Not Worth It

Code that technically violates a style but should NOT be refactored.

### ToolRegistry constructor — `?? throw new ArgumentNullException`

**File:** `Packages/com.theatre.toolkit/Runtime/Core/ToolRegistry.cs:45,49`

```csharp
Name = name ?? throw new ArgumentNullException(nameof(name));
Handler = handler ?? throw new ArgumentNullException(nameof(handler));
```

**Why not:** These are programmer errors (registering a tool with a null name), not recoverable failures from external input. `ArgumentNullException` is the correct C# idiom for catching API misuse at construction time. The Result Types rule says "reserve exceptions for truly unrecoverable situations" — null tool name is exactly that.

### SpatialIndex / TokenBudget foreach loops

**Files:** `Runtime/Stage/Spatial/SpatialIndex.cs`, `Runtime/Stage/Spatial/Clustering.cs`

**Why not:** Hot paths. LINQ Cold Paths rule explicitly excludes spatial index operations. The foreach loops here are performance-correct.

### WatchEngine.ListAll / WatchEngine.Check

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/GameObject/WatchEngine.cs:137-155, 164-183`

**Why not:** Builds JObjects with complex conditional logic and side effects. LINQ would obscure intent and not reduce line count meaningfully.

### SseStreamManager.PushNotification

**File:** `Packages/com.theatre.toolkit/Runtime/Transport/SseStreamManager.cs:77-87`

**Why not:** Exception handling inside the loop body. Each iteration catches independently. LINQ doesn't model "try per element" cleanly.

### UnityConsoleTool.ExecuteQuery

**File:** `Packages/com.theatre.toolkit/Editor/Tools/UnityConsoleTool.cs:104-115`

**Why not:** Builds JObjects with conditional field additions. LINQ `.Select()` would need the same conditional logic inside the lambda, gaining nothing.

### Clustering.DeriveLabel — moderate nesting

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/Spatial/Clustering.cs:184-251`

**Why not:** The method has duplicated cell-processing patterns at 3 levels of nesting, but never reaches 4+. Extracting a helper would add indirection for marginal nesting improvement. The duplication is spatial-algorithm-specific and reads fine in context.

### Static Stateless — no violations found

All utility classes (`ObjectResolver`, `HierarchyWalker`, `PropertySerializer`, `ResponseHelpers`, `JsonParamParser`, `SpatialEntryFilter`, `SpatialResultBuilder`) are already static. All instance classes (`SpatialIndex`, `WatchEngine`, `TokenBudget`, `PaginationCursor`) have genuine state. No action needed.
