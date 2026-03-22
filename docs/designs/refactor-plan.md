# Refactor Plan: Theatre Unity Codebase

## Overview

Analysis of the entire Theatre Unity codebase reveals three categories of
refactoring opportunity:

1. **Pattern violation (bug)**: 21 Director OpTool files (139 success
   response return points) are missing `AddFrameContext` calls — agents
   get no frame/time/play_mode context on Director tool responses.
2. **Duplicate logic**: Compound tool dispatch boilerplate (~40 lines)
   is copy-pasted across 25+ tools. `ToPascalCase` is implemented 3
   times. `ResolveComponentType` / `ResolveScriptableObjectType` are
   near-identical 60-line methods. The `FindProperty` fallback chain
   (snake_case → m_PascalCase → PascalCase → m_snake) exists in 3 files.
3. **Missing abstractions**: Spatial query handlers repeat origin
   validation, budget envelope construction, and 2D/3D vector fallback
   parsing that could be shared.

Each step below is a discrete, independently testable change.

---

## Refactor Steps

### Step 1: Add missing `AddFrameContext` to all Director OpTool responses

**Priority**: High
**Risk**: Low — additive change, no existing behavior altered
**Files**: All 21 `Editor/Tools/Director/*OpTool.cs` files missing the call

**Current State** (example from `QualityOpTool.cs:174-179`):
```csharp
var response = new JObject();
response["result"] = "ok";
response["operation"] = "set_level";
response["level"] = levelIndex;
response["name"] = QualitySettings.names[levelIndex];
return response.ToString(Formatting.None);
```

**Target State**:
```csharp
var response = new JObject();
response["result"] = "ok";
response["operation"] = "set_level";
response["level"] = levelIndex;
response["name"] = QualitySettings.names[levelIndex];
ResponseHelpers.AddFrameContext(response);
return response.ToString(Formatting.None);
```

**Implementation Notes**:
- Apply to every `return response.ToString(Formatting.None)` in success
  paths across all 21 files (~139 return points)
- Do NOT add to error early-exits (those return `ErrorResponse` strings)
- Files already correct: `SceneOpHandlers.cs`, `PrefabOpHandlers.cs`,
  `BatchTool.cs` — do not modify these
- `TerrainOpTool.cs` has 1 existing call, needs 8 more
- `LightingOpTool.cs` has 2 existing calls, needs 4 more

**Acceptance Criteria**:
- [ ] Build passes
- [ ] Tests pass
- [ ] `grep -rL "AddFrameContext" Editor/Tools/Director/*OpTool.cs`
      returns only `SceneOpTool.cs` and `PrefabOpTool.cs` (which delegate
      to handler files)
- [ ] Every Director tool response includes `frame`, `time`, `play_mode`
      fields

---

### Step 2: Extract `CompoundToolDispatcher.Execute` helper

**Priority**: High
**Risk**: Low — pure extraction, no behavior change
**Files**:
- New: `Editor/Tools/CompoundToolDispatcher.cs`
- Modified: All 25+ compound tools (Director OpTools + `ActionTool` +
  `SpatialQueryTool` + `WatchTool` + `SceneHierarchyTool` + ECS tools)

**Current State** (repeated in every compound tool, ~40 lines each):
```csharp
private static string Execute(JToken arguments)
{
    if (arguments == null || arguments.Type != JTokenType.Object)
    {
        return ResponseHelpers.ErrorResponse(
            "invalid_parameter",
            "Arguments must be a JSON object with an 'operation' field",
            "Provide {\"operation\": \"...\", ...}");
    }

    var args = (JObject)arguments;
    var operation = args["operation"]?.Value<string>();

    if (string.IsNullOrEmpty(operation))
    {
        return ResponseHelpers.ErrorResponse(
            "invalid_parameter",
            "Missing required 'operation' parameter",
            "Valid operations: create, set_properties, ...");
    }

    try
    {
        return operation switch
        {
            "create"          => Create(args),
            "set_properties"  => SetProperties(args),
            _ => ResponseHelpers.ErrorResponse(
                "invalid_parameter",
                $"Unknown operation '{operation}'",
                "Valid operations: create, set_properties, ...")
        };
    }
    catch (Exception ex)
    {
        Debug.LogError($"[Theatre] material_op:{operation} failed: {ex}");
        return ResponseHelpers.ErrorResponse(
            "internal_error",
            $"material_op:{operation} failed: {ex.Message}",
            "Check the Unity Console for details");
    }
}
```

**Target State**:

New shared helper:
```csharp
namespace Theatre.Editor.Tools
{
    /// <summary>
    /// Shared dispatcher for compound MCP tools that switch on an
    /// "operation" string parameter.
    /// </summary>
    internal static class CompoundToolDispatcher
    {
        /// <summary>
        /// Validate arguments, extract operation, dispatch via the
        /// provided switch function, and wrap in a standard try/catch.
        /// </summary>
        /// <param name="arguments">Raw JToken from MCP.</param>
        /// <param name="toolName">Tool name for error messages (e.g. "material_op").</param>
        /// <param name="validOps">Comma-separated valid operations for error hints.</param>
        /// <param name="dispatch">
        ///   Function that receives (JObject args, string operation) and
        ///   returns the handler result, or null for unknown operations.
        /// </param>
        internal static string Execute(
            JToken arguments,
            string toolName,
            string validOps,
            Func<JObject, string, string> dispatch)
        {
            if (arguments == null || arguments.Type != JTokenType.Object)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Arguments must be a JSON object with an 'operation' field",
                    $"Provide {{\"operation\": \"...\", ...}}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    $"Valid operations: {validOps}");
            }

            try
            {
                var result = dispatch(args, operation);
                return result ?? ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Unknown operation '{operation}'",
                    $"Valid operations: {validOps}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] {toolName}:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"{toolName}:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }
    }
}
```

Each compound tool becomes:
```csharp
private static string Execute(JToken arguments)
{
    return CompoundToolDispatcher.Execute(
        arguments,
        "material_op",
        "create, set_properties, set_shader, list_properties",
        (args, op) => op switch
        {
            "create"          => Create(args),
            "set_properties"  => SetProperties(args),
            "set_shader"      => SetShader(args),
            "list_properties" => ListProperties(args),
            _ => null
        });
}
```

**Implementation Notes**:
- Migrate one tool at a time, verify tests pass after each
- Start with a Director tool (e.g. `MaterialOpTool`) to prove the
  pattern, then batch-migrate the rest
- The `dispatch` function returns `null` for unknown ops so the
  dispatcher can provide the standard error with valid ops list
- Preserve existing `using` statements — the new file needs
  `Newtonsoft.Json.Linq`, `Theatre.Stage`, `UnityEngine`

**Acceptance Criteria**:
- [ ] Build passes
- [ ] Tests pass (all existing compound tool tests)
- [ ] No compound tool has inline `arguments == null` / `string.IsNullOrEmpty(operation)` checks
- [ ] Each compound tool's `Execute` method is ≤10 lines

---

### Step 3: Unify `ResolveComponentType` and `ResolveScriptableObjectType`

**Priority**: High
**Risk**: Low — internal method, same logic, parameterized
**Files**: `Editor/Tools/Director/DirectorHelpers.cs`

**Current State** (`DirectorHelpers.cs:25-156`):
Two nearly identical 60-line methods that differ only in:
- Base type constraint (`Component` vs `ScriptableObject`)
- Error message noun ("Component" vs "ScriptableObject")
- Suggestion text

**Target State**:
```csharp
/// <summary>
/// Resolve a type by name, constrained to a base type.
/// Searches all loaded assemblies for exact or Name match.
/// Returns null and sets error on failure or ambiguity.
/// </summary>
public static Type ResolveType(
    string typeName, Type baseType, string label, out string error)
{
    error = null;
    if (string.IsNullOrEmpty(typeName))
    {
        error = ResponseHelpers.ErrorResponse(
            "invalid_parameter",
            $"{label} type name must not be empty",
            $"Provide a {label} type name");
        return null;
    }

    var matches = new List<Type>();

    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
    {
        var exact = assembly.GetType(typeName);
        if (exact != null && baseType.IsAssignableFrom(exact))
        {
            matches.Add(exact);
            continue;
        }

        try
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.Name == typeName && baseType.IsAssignableFrom(type))
                {
                    if (!matches.Contains(type))
                        matches.Add(type);
                }
            }
        }
        catch (System.Reflection.ReflectionTypeLoadException) { }
    }

    if (matches.Count == 0)
    {
        error = ResponseHelpers.ErrorResponse(
            "type_not_found",
            $"{label} type '{typeName}' not found in any loaded assembly",
            "Check the type name. Use fully-qualified name to disambiguate.");
        return null;
    }

    if (matches.Count > 1)
    {
        var names = string.Join(", ", matches.Select(t => t.FullName));
        error = ResponseHelpers.ErrorResponse(
            "type_ambiguous",
            $"{label} type name '{typeName}' is ambiguous. Matches: {names}",
            "Use the fully-qualified type name to disambiguate");
        return null;
    }

    return matches[0];
}

// Convenience wrappers (preserve existing call sites):
public static Type ResolveComponentType(string typeName, out string error)
    => ResolveType(typeName, typeof(Component), "Component", out error);

public static Type ResolveScriptableObjectType(string typeName, out string error)
    => ResolveType(typeName, typeof(ScriptableObject), "ScriptableObject", out error);
```

**Implementation Notes**:
- Keep the two convenience wrappers so no call sites need to change
- Delete the original two 60-line method bodies
- Error messages will be slightly different ("Component type" vs
  "Component") — acceptable

**Acceptance Criteria**:
- [ ] Build passes
- [ ] `DirectorTests.ResolveComponentType_*` tests pass
- [ ] `DirectorTests.ResolveScriptableObjectType_*` tests pass
- [ ] Only one assembly-scanning loop in `DirectorHelpers.cs`

---

### Step 4: Consolidate `ToPascalCase` into a shared location

**Priority**: High
**Risk**: Low — identical implementations, pure function
**Files**:
- `Editor/Tools/Actions/ActionSetProperty.cs` (lines 229-240, private)
- `Runtime/Stage/GameObject/WatchConditions.cs` (lines 284-295, private)
- `Editor/Tools/Director/DirectorHelpers.cs` (lines 422-433, public)

**Current State**: Three identical implementations of `ToPascalCase`:
```csharp
private static string ToPascalCase(string snakeCase)
{
    if (string.IsNullOrEmpty(snakeCase)) return snakeCase;
    // ... identical logic ...
}
```

**Target State**:
- Keep the canonical `public` copy in `DirectorHelpers.ToPascalCase`
  (it's already public and tested via `DirectorTests`)
- `ActionSetProperty` calls `DirectorHelpers.ToPascalCase` instead
  (Editor assembly can reference Director namespace)
- `WatchConditions` (Runtime assembly) cannot reference Editor code.
  Move the method to a new shared utility in Runtime:
  `Runtime/Core/StringUtils.cs` with a `public static string
  ToPascalCase(string snakeCase)` method. Then all three callers
  reference this single copy.

**Implementation Notes**:
- `WatchConditions` is in the Runtime assembly — it cannot call
  `DirectorHelpers` (Editor-only). The shared method must live in
  Runtime.
- After creating `StringUtils.ToPascalCase` in Runtime:
  - `DirectorHelpers.ToPascalCase` delegates to `StringUtils.ToPascalCase`
    (or is deleted, with callers updated)
  - `ActionSetProperty` private copy is deleted, calls `StringUtils`
  - `WatchConditions` private copy is deleted, calls `StringUtils`
- Namespace: `Theatre` (matches Runtime/Core convention)

**Acceptance Criteria**:
- [ ] Build passes
- [ ] `DirectorTests.ToPascalCase_ConvertsCorrectly` passes
- [ ] `grep -rn "private static string ToPascalCase"` returns 0 results
- [ ] Only one implementation exists in `Runtime/Core/StringUtils.cs`

---

### Step 5: Extract `SerializedPropertyResolver` for FindProperty fallback chain

**Priority**: Medium
**Risk**: Low — pure extraction of duplicated logic
**Files**:
- `Editor/Tools/Actions/ActionSetProperty.cs` (lines 54-62)
- `Editor/Tools/Director/DirectorHelpers.cs` (lines 226-232)
- `Runtime/Stage/GameObject/WatchConditions.cs` (lines 233-243)

**Current State** (repeated 3 times with minor variations):
```csharp
var prop = so.FindProperty(propertyName);
if (prop == null)
    prop = so.FindProperty("m_" + ToPascalCase(propertyName));
if (prop == null)
    prop = so.FindProperty(ToPascalCase(propertyName));
if (prop == null)
    prop = so.FindProperty("m_" + propertyName);
```

**Target State**:
New method in `Runtime/Core/StringUtils.cs` (or a new
`Runtime/Core/PropertyNameResolver.cs`):
```csharp
/// <summary>
/// Try multiple Unity naming conventions to find a SerializedProperty.
/// Unity uses m_PascalCase internally, but users send snake_case.
/// </summary>
public static string[] GetPropertyNameCandidates(string snakeCaseName)
{
    var pascal = ToPascalCase(snakeCaseName);
    return new[]
    {
        snakeCaseName,              // exact match
        "m_" + pascal,              // Unity internal (m_IsKinematic)
        pascal,                     // PascalCase (IsKinematic)
        "m_" + snakeCaseName        // m_ + original (m_mass)
    };
}
```

Each call site becomes:
```csharp
SerializedProperty prop = null;
foreach (var candidate in PropertyNameResolver.GetPropertyNameCandidates(propertyName))
{
    prop = so.FindProperty(candidate);
    if (prop != null) break;
}
```

**Implementation Notes**:
- Since `WatchConditions` is in Runtime, the helper must be in Runtime
- `SerializedObject.FindProperty` is Editor-only, but the name
  generation logic is pure string work — safe for Runtime
- The actual `FindProperty` loop stays at each call site (Editor-only)

**Acceptance Criteria**:
- [ ] Build passes
- [ ] Tests pass (ActionSetProperty tests, Watch tests, Director tests)
- [ ] No inline `FindProperty` fallback chains remain
- [ ] Property name generation is in one place

---

### Step 6: Add `JsonParamParser.RequireVector3` for common validation

**Priority**: Medium
**Risk**: Low — additive API, existing code migrates incrementally
**Files**:
- `Runtime/Stage/Spatial/JsonParamParser.cs` (add method)
- `Editor/Tools/Spatial/SpatialQueryNearest.cs`
- `Editor/Tools/Spatial/SpatialQueryRadius.cs`
- `Editor/Tools/Spatial/SpatialQueryOverlap.cs`
- `Editor/Tools/Spatial/SpatialQueryRaycast.cs`
- `Editor/Tools/Spatial/SpatialQueryLinecast.cs`
- `Editor/Tools/Spatial/SpatialQueryPathDistance.cs`

**Current State** (repeated 8+ times across spatial handlers):
```csharp
var origin = JsonParamParser.ParseVector3(args, "origin");
if (!origin.HasValue)
{
    return ResponseHelpers.ErrorResponse(
        "invalid_parameter",
        "Missing or invalid 'origin' parameter",
        "Provide origin as [x, y, z] array");
}
```

**Target State**:
New method in `JsonParamParser`:
```csharp
/// <summary>
/// Parse a required Vector3 parameter. Returns null on success
/// (value written to out param), or an error response string on failure.
/// </summary>
public static string RequireVector3(
    JObject args, string field, out Vector3 value,
    string suggestion = null)
{
    var parsed = ParseVector3(args, field);
    if (!parsed.HasValue)
    {
        value = default;
        return ResponseHelpers.ErrorResponse(
            "invalid_parameter",
            $"Missing or invalid '{field}' parameter",
            suggestion ?? $"Provide {field} as [x, y, z] array");
    }
    value = parsed.Value;
    return null;
}
```

Each spatial handler becomes:
```csharp
var error = JsonParamParser.RequireVector3(args, "origin", out var origin);
if (error != null) return error;
```

**Implementation Notes**:
- Also add `RequireVector2` and `RequirePositiveFloat` for completeness
- `RequireVector2WithFallback` for the 2D handlers that accept either
  `[x,y]` or `[x,y,z]` and drop the z component
- Migrate handlers one at a time

**Acceptance Criteria**:
- [ ] Build passes
- [ ] All spatial query tests pass
- [ ] No inline `if (!origin.HasValue) return ErrorResponse(...)` blocks
      remain in spatial handlers

---

### Step 7: Extract budgeted spatial response envelope helper

**Priority**: Medium
**Risk**: Low — pure extraction
**Files**:
- `Editor/Tools/Spatial/SpatialResultBuilder.cs` (add method)
- `Editor/Tools/Spatial/SpatialQueryNearest.cs`
- `Editor/Tools/Spatial/SpatialQueryRadius.cs`

**Current State** (nearly identical in Nearest and Radius, ~20 lines each):
```csharp
var budget = new TokenBudget(budgetTokens);
var response = new JObject();
ResponseHelpers.AddFrameContext(response);
response["operation"] = "nearest";
response["origin"] = ResponseHelpers.ToJArray(origin.Value);

var (resultsArray, returned, truncated) =
    SpatialResultBuilder.BuildResultsArray(results, budget);

response["results"] = resultsArray;
response["returned"] = returned;
response["budget"] = budget.ToBudgetJObject(
    truncated: truncated,
    reason: truncated ? "budget" : null,
    suggestion: truncated ? "..." : null);

return response.ToString(Formatting.None);
```

**Target State**:
New method in `SpatialResultBuilder`:
```csharp
/// <summary>
/// Build a complete budgeted spatial query response.
/// Adds frame context, operation echo, results array, and budget envelope.
/// </summary>
public static string BuildBudgetedResponse(
    string operation,
    IReadOnlyList<SpatialResult> results,
    int budgetTokens,
    Action<JObject> addParams,
    string truncationSuggestion)
{
    var budget = new TokenBudget(budgetTokens);
    var response = new JObject();
    ResponseHelpers.AddFrameContext(response);
    response["operation"] = operation;
    addParams?.Invoke(response);

    var (resultsArray, returned, truncated) =
        BuildResultsArray(results, budget);

    response["results"] = resultsArray;
    response["total"] = results.Count;
    response["returned"] = returned;
    response["budget"] = budget.ToBudgetJObject(
        truncated: truncated,
        reason: truncated ? "budget" : null,
        suggestion: truncated ? truncationSuggestion : null);

    return response.ToString(Formatting.None);
}
```

Each handler becomes:
```csharp
return SpatialResultBuilder.BuildBudgetedResponse(
    "nearest", results, budgetTokens,
    r => { r["origin"] = ResponseHelpers.ToJArray(origin); },
    "Reduce count or increase budget to see more results");
```

**Implementation Notes**:
- Only applies to spatial index queries (Nearest, Radius) — NOT to
  physics queries which have different response shapes
- The `addParams` callback lets each handler inject operation-specific
  fields (origin, radius, sort_by) without the helper needing to know

**Acceptance Criteria**:
- [ ] Build passes
- [ ] Spatial query tests pass
- [ ] Nearest and Radius handlers are ≤20 lines each

---

### Step 8: Add `JsonParamParser.RequireVector2WithFallback` for physics handlers

**Priority**: Low
**Risk**: Low — additive, no behavior change
**Files**:
- `Runtime/Stage/Spatial/JsonParamParser.cs` (add method)
- `Editor/Tools/Spatial/SpatialQueryRaycast.cs`
- `Editor/Tools/Spatial/SpatialQueryLinecast.cs`
- `Editor/Tools/Spatial/SpatialQueryOverlap.cs`

**Current State** (repeated 3+ times, ~10 lines each):
```csharp
var origin = JsonParamParser.ParseVector2(args, "origin");
if (!origin.HasValue)
{
    var origin3 = JsonParamParser.ParseVector3(args, "origin");
    if (origin3.HasValue)
        origin = new Vector2(origin3.Value.x, origin3.Value.y);
    else
        return ResponseHelpers.ErrorResponse(
            "invalid_parameter",
            "Missing or invalid 'origin'",
            "Provide origin as [x, y] or [x, y, z]");
}
```

**Target State**:
```csharp
/// <summary>
/// Parse a Vector2 parameter, falling back to Vector3 XY if given a 3-element array.
/// </summary>
public static string RequireVector2WithFallback(
    JObject args, string field, out Vector2 value,
    string suggestion = null)
{
    var v2 = ParseVector2(args, field);
    if (v2.HasValue) { value = v2.Value; return null; }

    var v3 = ParseVector3(args, field);
    if (v3.HasValue) { value = new Vector2(v3.Value.x, v3.Value.y); return null; }

    value = default;
    return ResponseHelpers.ErrorResponse(
        "invalid_parameter",
        $"Missing or invalid '{field}' parameter",
        suggestion ?? $"Provide {field} as [x, y] or [x, y, z] array");
}
```

**Acceptance Criteria**:
- [ ] Build passes
- [ ] Physics spatial tests pass (raycast, linecast, overlap)
- [ ] No inline Vector2/Vector3 fallback logic remains

---

## Implementation Order

1. **Step 1**: Add missing `AddFrameContext` to Director tools (bug fix,
   zero dependency on other steps)
2. **Step 4**: Consolidate `ToPascalCase` (prerequisite for Step 5)
3. **Step 5**: Extract `SerializedPropertyResolver` (depends on Step 4)
4. **Step 3**: Unify `ResolveType` in DirectorHelpers (independent)
5. **Step 6**: Add `JsonParamParser.RequireVector3` (independent)
6. **Step 8**: Add `RequireVector2WithFallback` (depends on Step 6)
7. **Step 7**: Extract budgeted spatial response helper (independent)
8. **Step 2**: Extract `CompoundToolDispatcher` (largest change, do last
   to avoid merge conflicts with Step 1)

Steps 1, 3, 4, 6, and 7 are fully independent and can be parallelized
if desired. Steps 2 and 8 should be done after their prerequisites.
