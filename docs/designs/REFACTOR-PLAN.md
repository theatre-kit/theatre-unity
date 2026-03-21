# Refactor Plan: Theatre Toolkit Codebase

## Summary

The Theatre toolkit codebase has grown organically through 5 implementation phases. Several patterns have emerged independently across tool handlers, leading to duplicated logic in parameter parsing, filter construction, response building, and GameObject resolution. This plan consolidates those into shared abstractions, ordered as small, testable, non-breaking steps.

**Scope**: `Packages/com.theatre.toolkit/` — all Runtime and Editor code.

**Guiding principle**: Every step reduces line count or eliminates a copy-paste pattern. No aesthetic-only changes.

---

## Refactor Steps

### Step 1: Extract `JsonParamParser` utility from `SpatialQueryNearest`

**Priority**: High
**Risk**: Low
**Files**: `Runtime/Stage/ResponseHelpers.cs`, `Editor/Tools/SpatialQueryNearest.cs`, all `SpatialQuery*.cs`, `SceneSnapshotTool.cs`, `SceneInspectTool.cs`, `SceneHierarchyTool.cs`

**Current State**: `SpatialQueryNearest` defines `ParseVector3`, `ParseVector2`, and `ParseStringArray` as `internal static` methods. Other spatial query tools call them via `SpatialQueryNearest.ParseVector3(...)`. Non-spatial tools (`SceneSnapshotTool`, `SceneInspectTool`, `SceneHierarchyTool`) duplicate the string-array parsing logic locally instead of calling the shared version.

**Target State**: A new `JsonParamParser` static class in `Runtime/Stage/` (alongside `ResponseHelpers`) owns all JObject→typed-value parsing. Every tool calls `JsonParamParser.ParseVector3(args, "origin")` etc. Zero duplicated parsing code.

**Approach**:
1. Create `Runtime/Stage/JsonParamParser.cs` with methods: `ParseVector3`, `ParseVector2`, `ParseStringArray`, `ParseFloat`, `ParseInt`, `ParseBool`, `ParseString`.
2. Move the three methods from `SpatialQueryNearest` to `JsonParamParser`.
3. Update all callers (`SpatialQueryNearest.ParseVector3` → `JsonParamParser.ParseVector3`).
4. Replace duplicated string-array parsing in `SceneSnapshotTool`, `SceneInspectTool`, `SceneHierarchyTool` with calls to `JsonParamParser.ParseStringArray`.

**Verification**:
- Build passes (refresh + check errors)
- All tests pass
- Grep confirms no remaining `ParseVector3` or `ParseStringArray` definitions outside `JsonParamParser`

---

### Step 2: Extract `SpatialEntryFilter.Build()` — deduplicate filter predicate construction

**Priority**: High
**Risk**: Low
**Files**: `Editor/Tools/SpatialQueryNearest.cs`, `Editor/Tools/SpatialQueryRadius.cs`, `Editor/Tools/SceneSnapshotTool.cs`

**Current State**: `SpatialQueryNearest` (lines 38-74) and `SpatialQueryRadius` (lines 44-81) contain identical ~35-line filter predicate builders. `SceneSnapshotTool.FilterByComponents()` implements a similar but slightly different version.

**Target State**: A single `SpatialEntryFilter.Build(string[] includeComponents, string[] excludeTags)` method returns a `Func<SpatialEntry, bool>` predicate. Both spatial query tools and the snapshot tool call it.

**Approach**:
1. Create `Runtime/Stage/Spatial/SpatialEntryFilter.cs` with the `Build` method (moved from Nearest).
2. Replace the inline lambdas in `SpatialQueryNearest` and `SpatialQueryRadius` with `SpatialEntryFilter.Build(...)`.
3. Adapt `SceneSnapshotTool.FilterByComponents()` to delegate to the shared filter or replace it entirely.

**Verification**:
- Build passes
- `SpatialQueryTests` pass (covers nearest, radius)
- Grep confirms no remaining inline filter lambdas with `includeComponents != null` pattern

---

### Step 3: Extract `ObjectResolver.ResolveFromArgs()` — deduplicate resolve-and-error pattern

**Priority**: High
**Risk**: Low
**Files**: `Runtime/Stage/GameObject/ObjectResolver.cs`, all `Actions/*.cs`, `SceneInspectTool.cs`, `SceneHierarchyTool.cs`, `SpatialQueryBounds.cs`

**Current State**: Eight tool handlers repeat this 6-line pattern:
```csharp
var path = args["path"]?.Value<string>();
var instanceId = args["instance_id"]?.Value<int>();
var resolved = ObjectResolver.Resolve(path, instanceId);
if (!resolved.Success)
    return ResponseHelpers.ErrorResponse(
        resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);
var go = resolved.GameObject;
```

**Target State**: `ObjectResolver.ResolveFromArgs(JObject args, out GameObject go)` returns either `null` (success, `go` set) or an error response string. Callers become:
```csharp
var error = ObjectResolver.ResolveFromArgs(args, out var go);
if (error != null) return error;
```

**Approach**:
1. Add `ResolveFromArgs` method to `ObjectResolver.cs`.
2. Replace all 8 call sites with the one-liner.
3. Keep the existing `Resolve(path, instanceId)` method for callers that need the full `ResolveResult`.

**Verification**:
- Build passes
- `WatchActionTests` and `SceneToolIntegrationTests` pass
- Grep confirms at most 1 remaining `ObjectResolver.Resolve(path, instanceId)` call (in `ResolveFromArgs` itself)

---

### Step 4: Extract `ComponentResolver.FindByName()` — deduplicate component lookup

**Priority**: Medium
**Risk**: Low
**Files**: `Editor/Tools/Actions/ActionSetProperty.cs`, `Editor/Tools/Actions/ActionInvokeMethod.cs`

**Current State**: Both files contain identical 10-line loops to find a component by case-insensitive type name:
```csharp
Component component = null;
foreach (var comp in go.GetComponents<Component>())
{
    if (comp == null) continue;
    if (string.Equals(comp.GetType().Name, componentName, StringComparison.OrdinalIgnoreCase))
    { component = comp; break; }
}
```

**Target State**: `ObjectResolver.FindComponent(GameObject go, string componentName)` returns the component or null. Both action handlers call it.

**Approach**:
1. Add `FindComponent` to `ObjectResolver.cs` (it already handles object resolution, component resolution is a natural extension).
2. Replace the inline loops in `ActionSetProperty` and `ActionInvokeMethod`.

**Verification**:
- Build passes
- `WatchActionTests` pass (covers set_property and invoke_method indirectly)

---

### Step 5: Extract `SpatialResultBuilder` — deduplicate entry+budget response building

**Priority**: Medium
**Risk**: Low
**Files**: `Editor/Tools/SpatialQueryNearest.cs`, `Editor/Tools/SpatialQueryRadius.cs`

**Current State**: Both files contain nearly identical ~30-line blocks that iterate results, build entry JObjects (path, instance_id, name, position, distance, components), check budget, and append to a results array. The entry-building block (lines 95-114 in Nearest, 102-121 in Radius) is character-for-character identical.

**Target State**: A `SpatialResultBuilder` class or static method in `Editor/Tools/` that:
- Takes an `IReadOnlyList<SpatialResult>` and a `TokenBudget`
- Builds the `JArray` of entry objects
- Returns `(JArray results, int returned, bool truncated)`

**Approach**:
1. Create `Editor/Tools/SpatialResultBuilder.cs` with a `BuildResultsArray` method.
2. Replace the duplicated loops in both Nearest and Radius.

**Verification**:
- Build passes
- `SpatialQueryTests` pass
- Both tools produce identical JSON output for the same inputs (manual spot-check or add a test)

---

### Step 6: Centralize `GetInstanceID()` suppression into `ResponseHelpers`

**Priority**: Medium
**Risk**: Low
**Files**: `Runtime/Stage/ResponseHelpers.cs`, 16+ files that use `#pragma warning disable CS0618` around `GetInstanceID()`

**Current State**: Every file that adds `instance_id` to a response wraps `GetInstanceID()` in a 3-line pragma suppression block:
```csharp
#pragma warning disable CS0618
response["instance_id"] = go.GetInstanceID();
#pragma warning restore CS0618
```
This appears in ~16 files.

**Target State**: `ResponseHelpers.AddIdentity(JObject obj, GameObject go)` adds both `path` and `instance_id`, with the pragma suppression in one place. Callers become:
```csharp
ResponseHelpers.AddIdentity(response, go);
```

**Approach**:
1. Add `AddIdentity(JObject obj, GameObject go)` to `ResponseHelpers.cs` that sets `obj["path"]` and `obj["instance_id"]`.
2. Replace all 16+ call sites.

**Verification**:
- Build passes
- All tests pass
- Grep confirms no remaining `#pragma warning disable CS0618` outside `ResponseHelpers`

---

### Step 7: Extract play-mode guard to shared helper

**Priority**: Low
**Risk**: Low
**Files**: `Editor/Tools/Actions/ActionPlayControl.cs`, `Editor/Tools/Actions/ActionSetTimescale.cs`, `Editor/Tools/Actions/ActionInvokeMethod.cs`

**Current State**: Three action handlers repeat the same play-mode check:
```csharp
if (!Application.isPlaying)
    return ResponseHelpers.ErrorResponse(
        "requires_play_mode", "... requires Play Mode", "Enter Play Mode first ...");
```
`ActionPlayControl` has this check 3 times internally (pause, step, resume).

**Target State**: `ResponseHelpers.RequirePlayMode(string operationName)` returns `null` if in play mode, or the error string if not. Callers:
```csharp
var error = ResponseHelpers.RequirePlayMode("invoke_method");
if (error != null) return error;
```

**Approach**:
1. Add `RequirePlayMode` to `ResponseHelpers.cs`.
2. Replace all 5 call sites.

**Verification**:
- Build passes
- `WatchActionTests` pass

---

### Step 8: Split `WatchEngine.cs` (562 lines) into focused files

**Priority**: Low
**Risk**: Medium — touches core watch infrastructure
**Files**: `Runtime/Stage/GameObject/WatchEngine.cs`

**Current State**: `WatchEngine.cs` is 562 lines and handles: watch CRUD, SessionState persistence, condition evaluation (value_changed, threshold, enter_region, exit_region, component_enabled, became_active), target resolution, and notification dispatch.

**Target State**: Split into:
- `WatchEngine.cs` (~150 lines) — CRUD, tick loop, notification dispatch
- `WatchPersistence.cs` (~80 lines) — SessionState save/restore
- `WatchConditions.cs` (~200 lines) — condition evaluation logic

**Approach**:
1. Extract `WatchPersistence` as an internal class with `Save(List<WatchState>)` and `Restore()` methods.
2. Extract `WatchConditions` as an internal static class with `Evaluate(WatchState, ...)` method.
3. Keep `WatchEngine` as the orchestrator that calls both.

**Verification**:
- Build passes
- `WatchActionTests` pass (covers watch create, evaluate, remove)
- Watch persistence survives simulated domain reload in tests

---

## Dependency Order

```
Step 1 (JsonParamParser)
  └─ Step 2 (SpatialEntryFilter) — uses JsonParamParser for parsing filter arrays
Step 3 (ResolveFromArgs) — independent
  └─ Step 4 (ComponentResolver) — extends ObjectResolver from Step 3
Step 5 (SpatialResultBuilder) — can run after Step 1
Step 6 (AddIdentity) — independent
Step 7 (RequirePlayMode) — independent
Step 8 (WatchEngine split) — independent
```

Steps 1, 3, 6, 7, 8 can run in parallel. Steps 2 and 4 depend on 1 and 3 respectively. Step 5 benefits from Step 1 being done first.

## Expected Impact

| Metric | Before | After (estimated) |
|--------|--------|-------------------|
| Duplicated filter predicate | 3 copies (~105 lines) | 1 copy (~35 lines) |
| Resolve-and-error pattern | 8 copies (~48 lines) | 8 one-liners + 1 helper (~14 lines) |
| Component-find loop | 2 copies (~20 lines) | 2 one-liners + 1 helper (~12 lines) |
| GetInstanceID pragma blocks | ~16 copies (~48 lines) | 1 helper (~6 lines) |
| Entry-building duplication | 2 copies (~40 lines) | 1 shared builder (~25 lines) |
| Play-mode checks | 5 copies (~15 lines) | 5 one-liners + 1 helper (~8 lines) |
| **Total lines saved** | | **~200+ lines** |
