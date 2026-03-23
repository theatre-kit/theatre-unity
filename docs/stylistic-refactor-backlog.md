# Stylistic Refactor Backlog

Generated: 2026-03-22
Styles: Result Types, LINQ Cold Paths, Static Stateless, Guard Clauses

---

## High Value

Low risk, clear improvement in readability or consistency.

### 1. SceneDeltaTool path filter — `.Any()` instead of manual loop
**File:** `Editor/Tools/Scene/SceneDeltaTool.cs:138-151`
**Style:** LINQ Cold Paths

**Current** (10 lines, temp bool + manual break):
```csharp
bool match = false;
foreach (var p in pathFilter)
{
    if (path == p || path.StartsWith(p + "/"))
    {
        match = true;
        break;
    }
}
if (!match) skipObject = true;
```

**Proposed:**
```csharp
if (!pathFilter.Any(p => path == p || path.StartsWith(p + "/")))
    skipObject = true;
```

**Rationale:** 10 → 2 lines. `.Any()` is the idiomatic C# for "does any element match". Cold path (delta tool).

---

### 2. SceneDeltaTool path filter + skipObject — flatten with `continue`
**File:** `Editor/Tools/Scene/SceneDeltaTool.cs:138-159`
**Style:** Guard Clauses

Combined with #1, the `skipObject` flag + `if (!skipObject)` wrapping 20+ lines can become a single guard:

```csharp
if (pathFilter != null && !pathFilter.Any(p => path == p || path.StartsWith(p + "/")))
    continue;
```

**Rationale:** Eliminates one full nesting level from the snapshot block. Classic guard clause inversion.

---

### 3. DirectorHelpers sub-asset name collection — `.Where().Select()`
**File:** `Editor/Tools/Director/DirectorHelpers.cs:480-485`
**Style:** LINQ Cold Paths

**Current:**
```csharp
var names = new List<string>();
foreach (var asset in allAssets)
{
    if (asset != null && !string.IsNullOrEmpty(asset.name))
        names.Add(asset.name);
}
```

**Proposed:**
```csharp
var names = allAssets
    .Where(a => a != null && !string.IsNullOrEmpty(a.name))
    .Select(a => a.name).ToList();
```

**Rationale:** Classic filter+project accumulation. 5 → 3 lines.

---

### 4. SceneSnapshotTool root collection — `.Select()` in both branches
**File:** `Editor/Tools/Scene/SceneSnapshotTool.cs:144-151`
**Style:** LINQ Cold Paths

**Current:**
```csharp
foreach (var go in scene.GetRootGameObjects())
    roots.Add(go.transform);
// and
foreach (var go in ObjectResolver.GetAllRoots())
    roots.Add(go.transform);
```

**Proposed:**
```csharp
roots = scene.GetRootGameObjects().Select(go => go.transform).ToList();
// and
roots = ObjectResolver.GetAllRoots().Select(go => go.transform).ToList();
```

**Rationale:** Trivial transform-and-collect. `.Select().ToList()` is idiomatic.

---

### 5. SceneHierarchyTool active root count — `.Count()` + ternary
**File:** `Editor/Tools/Scene/SceneHierarchyTool.cs:175-184`
**Style:** LINQ Cold Paths

**Current:**
```csharp
int activeRootCount = 0;
if (!includeInactive)
{
    foreach (var root in roots)
        if (root.activeInHierarchy) activeRootCount++;
}
else
{
    activeRootCount = roots.Length;
}
```

**Proposed:**
```csharp
int activeRootCount = includeInactive
    ? roots.Length
    : roots.Count(r => r.activeInHierarchy);
```

**Rationale:** 8 → 3 lines. `.Count()` with predicate is idiomatic.

---

### 6. UnityConsoleTool top-repeated summary — JArray constructor + `.Select()`
**File:** `Editor/Tools/UnityConsoleTool.cs:142-151`
**Style:** LINQ Cold Paths

**Current:**
```csharp
var top = new JArray();
foreach (var (message, count, type) in topRepeated)
{
    top.Add(new JObject
    {
        ["message"] = message,
        ["count"] = count,
        ["type"] = type.ToString().ToLowerInvariant()
    });
}
```

**Proposed:**
```csharp
var top = new JArray(topRepeated.Select(t => new JObject
{
    ["message"] = t.message,
    ["count"] = t.count,
    ["type"] = t.type.ToString().ToLowerInvariant()
}));
```

**Rationale:** JArray constructor accepts `IEnumerable<JToken>`. Eliminates mutable loop pattern.

---

### 7. SceneOpHandlers tag assignment — validate instead of bare catch
**File:** `Editor/Tools/Director/SceneOpHandlers.cs:327-328`
**Style:** Result Types

**Current:**
```csharp
try { go.tag = tag; }
catch { /* Invalid tag — ignore */ }
```

**Proposed:**
```csharp
if (UnityEditorInternal.InternalEditorUtility.tags.Contains(tag))
    go.tag = tag;
else
    compErrors.Add($"Invalid tag '{tag}'");
```

**Rationale:** Silent bare catch hides errors from the agent. Validating first provides feedback and avoids exception overhead.

---

### 8. SceneSnapshotTool.FilterByComponents — nested foreach to LINQ
**File:** `Editor/Tools/Scene/SceneSnapshotTool.cs` (FilterByComponents method)
**Style:** LINQ Cold Paths + Guard Clauses

**Current** (15 lines, 4 levels of nesting, temp booleans):
```csharp
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
```

**Proposed:**
```csharp
return entries.Where(entry =>
    entry.Components != null &&
    requiredComponents.All(required =>
        entry.Components.Any(comp =>
            string.Equals(comp, required, StringComparison.OrdinalIgnoreCase))))
    .ToList();
```

**Rationale:** 15 → 5 lines. Eliminates 4-level nesting and two temp booleans.

---

### 9. SpatialEntryFilter component matching — `.Any()` for inner loop
**File:** `Runtime/Stage/Spatial/SpatialEntryFilter.cs:44-60`
**Style:** LINQ Cold Paths + Guard Clauses

**Current:**
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
        !entry.Components.Any(c => string.Equals(c, required, StringComparison.OrdinalIgnoreCase)))
        return false;
}
```

**Rationale:** Eliminates inner foreach + temp bool. Filter predicate is built once per query (cold path).

---

### 10. PropertySerializer component filter — `.Any()`
**File:** `Editor/Tools/Scene/PropertySerializer.cs:58-72`
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

**Rationale:** 7 → 2 lines. Same `.Any()` pattern as #1 and #9.

---

### 11. Property name candidate lookup — deduplicate with shared helper
**Files:** `Editor/Tools/Actions/ActionSetProperty.cs:58-62`, `Editor/Tools/Director/DirectorHelpers.cs:177-181`, `Runtime/Stage/GameObject/WatchConditions.cs:230-244`
**Style:** LINQ Cold Paths + Static Stateless (deduplication)

**Current** (appears 3 times):
```csharp
SerializedProperty sp = null;
foreach (var candidate in StringUtils.GetPropertyNameCandidates(propName))
{
    sp = so.FindProperty(candidate);
    if (sp != null) break;
}
```

**Proposed:** Add to `StringUtils` or a new extension:
```csharp
public static SerializedProperty FindPropertyFuzzy(
    this SerializedObject so, string name)
{
    return StringUtils.GetPropertyNameCandidates(name)
        .Select(c => so.FindProperty(c))
        .FirstOrDefault(p => p != null);
}
```

Then all three call sites become: `var sp = so.FindPropertyFuzzy(propName);`

**Rationale:** Eliminates duplication across 3 files. `.Select().FirstOrDefault()` expresses intent.

---

## Worth Considering

Valid refactors with moderate impact or moderate effort.

### 12. PrefabOpHandlers — three accumulation loops to LINQ
**File:** `Editor/Tools/Director/PrefabOpHandlers.cs:448-490`

Three consecutive foreach loops (property modifications, added components, added objects) filter and transform into JArrays. Each is a `.Where().Select()` candidate. Moderate win — loops are already readable.

### 13. InputActionOpTool.ListActions — extract `BuildBindingJObject()`
**File:** `Editor/Tools/Director/InputActionOpTool.cs:416-470`
**Style:** Guard Clauses

Triple nested foreach (maps → actions → bindings) with conditionals at level 4-5. Extracting `BuildBindingJObject(binding)` would flatten the inner loop body.

### 14. AnimatorControllerOpTool.ListStates — extract `BuildTransitionJObject()`
**File:** `Editor/Tools/Director/AnimatorControllerOpTool.cs:543-600+`
**Style:** Guard Clauses

States → transitions → conditions creates 4 nesting levels. Helper method would flatten.

### 15. RecordingEngine.Tick delta merging — extract `MergeSnapshotDelta()`
**File:** `Runtime/Stage/Recording/RecordingEngine.cs:248-325`
**Style:** Guard Clauses

The if-else with nested property merging hits 6 levels. Extracting a merge helper would flatten the inner block significantly.

### 16. HierarchyWalker.SearchRecursive — extract `MatchesComponentFilter()`
**File:** `Runtime/Stage/GameObject/HierarchyWalker.cs:287-325`
**Style:** Guard Clauses

RequiredComponents check is a nested foreach-with-break. Extracting `MatchesComponentFilter(go, filter)` returning bool would flatten.

### 17. ConsoleLogBuffer.GetSummary — `.OrderByDescending().Take()`
**File:** `Editor/Tools/ConsoleLogBuffer.cs:166-170`
**Style:** LINQ Cold Paths

Replace manual sort + `RemoveRange` truncation with `.OrderByDescending(x => x.count).Take(topN)`. Idiomatic but small gain.

### 18. WatchPersistence.Save/Restore — `.Select()` transforms
**File:** `Runtime/Stage/GameObject/WatchPersistence.cs:23-25, 52-61`
**Style:** LINQ Cold Paths

Trivial foreach-to-`.Select()` in both Save and Restore. Small win.

### 19. ObjectResolver.FindComponent — `.FirstOrDefault()`
**File:** `Runtime/Stage/GameObject/ObjectResolver.cs:279-286`
**Style:** LINQ Cold Paths

Classic `.FirstOrDefault()` candidate for component lookup. Small but consistent.

### 20. SceneOpHandlers.CreateGameObject component loop — extract helper
**File:** `Editor/Tools/Director/SceneOpHandlers.cs:344-381`
**Style:** Guard Clauses

Loop body with `#if UNITY_EDITOR` blocks adds visual nesting. Extracting `AddComponentWithProperties()` would flatten.

---

## Not Worth It

Code that technically violates a style but should NOT be refactored.

### ToolRegistry constructor — `?? throw new ArgumentNullException`
**File:** `Runtime/Core/ToolRegistry.cs:45,49`

```csharp
Name = name ?? throw new ArgumentNullException(nameof(name));
Handler = handler ?? throw new ArgumentNullException(nameof(handler));
```

**Why not:** Programmer errors (null tool name at registration), not agent input failures. `ArgumentNullException` is the correct C# idiom for API misuse at construction time. The Result Types rule says "reserve exceptions for truly unrecoverable situations" — this qualifies.

### DirectorHelpers.ResolveType — catch (ReflectionTypeLoadException) in assembly loop
**File:** `Editor/Tools/Director/DirectorHelpers.cs:39-68`

Nested foreach with try/catch and duplicate checking. LINQ `.SelectMany()` would require wrapping the catch in a helper, obscuring *why* the catch exists. The current code is explicit about assembly scanning failures.

### TerrainOpTool grid loops — SmoothHeightmap, PaintTexture
**File:** `Editor/Tools/Director/TerrainOpTool.cs:266-441`

Triple/quadruple nested loops for 2D/3D grid operations (smoothing kernels, texture painting). Nesting is *inherent to the algorithm*. Extracting helpers would move nesting without reducing cognitive load. The loops are tightly coupled to array indices and bounds.

### TilemapOpTool grid loops — BoxFill, GetUsedTiles, Clear
**File:** `Editor/Tools/Director/TilemapOpTool.cs:234-469`

Same as TerrainOpTool. Grid iteration over `(x, y, z)` bounds is inherently nested. Loop bodies are minimal (1-3 lines). Extracting adds indirection for no readability gain.

### WatchEngine.ListAll / WatchEngine.Check
**File:** `Runtime/Stage/GameObject/WatchEngine.cs:136-184`

Builds JObjects with complex conditional properties (`Label`, `Condition`, `target_alive`). LINQ `.Select()` would produce a dense lambda harder to read than the explicit loop. The lookup loop uses early return — `.FirstOrDefault()` would need a null check + separate build step, adding lines.

### SseStreamManager.PushNotification — try/catch per element
**File:** `Runtime/Transport/SseStreamManager.cs:77-87`

Each iteration catches independently. LINQ doesn't model "try per element" cleanly without making the lambda worse than the loop.

### UnityConsoleTool.ExecuteQuery log entry builder
**File:** `Editor/Tools/UnityConsoleTool.cs:109-121`

Builds JObjects with conditional fields (`RepeatCount`, `StackTrace`). LINQ `.Select()` would need the same conditional logic inside the lambda, gaining nothing.

### Clustering.DeriveLabel — moderate nesting
**File:** `Runtime/Stage/Spatial/Clustering.cs:184-251`

Has 3 nesting levels but never reaches 4+. Spatial-algorithm-specific and reads fine in context. Extracting a helper adds indirection for marginal gain.

### Static Stateless — no violations found

All utility classes (`ObjectResolver`, `HierarchyWalker`, `PropertySerializer`, `ResponseHelpers`, `JsonParamParser`, `SpatialEntryFilter`, `SpatialResultBuilder`, `DirectorHelpers`) are already static. Instance classes (`SpatialIndex`, `WatchEngine`, `TokenBudget`, `RequestRouter`, `SseConnection`) hold genuine state. UI classes (`TheatreOverlay`, `WelcomeDialog`, `TheatreSettingsProvider`) must be instances (Unity framework base classes). No action needed.
