# Pattern: Spatial Index Sub-Handler

A spatial index sub-handler validates required spatial params, builds a filter predicate, queries the `SpatialIndex` via `SpatialQueryTool.GetIndex()`, and formats results via `SpatialResultBuilder`. Works in both edit and play mode.

## Rationale

Spatial queries (`nearest`, `radius`) share a fixed pipeline: validate → filter → query → budget-limited results. Centralizing the filter and result-building steps in `SpatialEntryFilter` and `SpatialResultBuilder` ensures consistent behavior (case-insensitive matching, truncation, budget reporting) without duplication in each handler.

## Examples

### Example 1: Nearest — required Vector3 origin
**File**: `Editor/Tools/Spatial/SpatialQueryNearest.cs`
```csharp
internal static string Execute(JObject args)
{
    // 1. Parse required Vector3 param via JsonParamParser
    var origin = JsonParamParser.ParseVector3(args, "origin");
    if (!origin.HasValue)
        return ResponseHelpers.ErrorResponse(
            "invalid_parameter",
            "Missing or invalid 'origin' parameter",
            "Provide origin as [x, y, z] array");

    // 2. Parse scalar params with defaults
    int count = args["count"]?.Value<int>() ?? 5;
    float maxDistance = args["max_distance"]?.Value<float>() ?? 0f;
    int budgetTokens = args["budget"]?.Value<int>() ?? TokenBudget.DefaultBudget;

    // 3. Parse optional filter arrays
    var includeComponents = JsonParamParser.ParseStringArray(args, "include_components");
    var excludeTags = JsonParamParser.ParseStringArray(args, "exclude_tags");

    // 4. Build filter predicate (null = no filtering)
    var filter = SpatialEntryFilter.Build(includeComponents, excludeTags);

    // 5. Query spatial index
    var index = SpatialQueryTool.GetIndex();
    var results = index.Nearest(origin.Value, count, maxDistance, filter);

    // 6. Build budgeted response
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
        suggestion: truncated ? "Reduce count or increase budget to see more results" : null);

    return response.ToString(Formatting.None);
}
```

### Example 2: Radius — additional required scalar param
**File**: `Editor/Tools/Spatial/SpatialQueryRadius.cs`
```csharp
internal static string Execute(JObject args)
{
    var origin = JsonParamParser.ParseVector3(args, "origin");
    if (!origin.HasValue)
        return ResponseHelpers.ErrorResponse(...);

    // Additional required scalar validated with HasValue + range check
    var radiusVal = args["radius"]?.Value<float>();
    if (!radiusVal.HasValue || radiusVal.Value <= 0f)
        return ResponseHelpers.ErrorResponse("invalid_parameter",
            "Missing or invalid 'radius' parameter",
            "Provide a positive radius value");

    string sortBy = args["sort_by"]?.Value<string>() ?? "distance";
    int budgetTokens = args["budget"]?.Value<int>() ?? TokenBudget.DefaultBudget;

    var filter = SpatialEntryFilter.Build(
        JsonParamParser.ParseStringArray(args, "include_components"),
        JsonParamParser.ParseStringArray(args, "exclude_tags"));

    var index = SpatialQueryTool.GetIndex();
    var results = index.Radius(origin.Value, radiusVal.Value, filter, sortBy);

    // Budget + results (same as nearest)
    var budget = new TokenBudget(budgetTokens);
    // ... BuildResultsArray, response, budget envelope
}
```

### Example 3: SpatialEntryFilter + SpatialResultBuilder API
**Files**: `Runtime/Stage/Spatial/SpatialEntryFilter.cs`, `Editor/Tools/Spatial/SpatialResultBuilder.cs`
```csharp
// SpatialEntryFilter.Build returns null when no filtering required
Func<SpatialEntry, bool> filter = SpatialEntryFilter.Build(includeComponents, excludeTags);
// SpatialIndex methods accept null filter (means "include everything")
var results = index.Nearest(origin, count, maxDistance, filter);

// SpatialResultBuilder returns a tuple
var (resultsArray, returned, truncated) =
    SpatialResultBuilder.BuildResultsArray(results, budget);
// resultsArray: JArray with per-entry objects (path, instance_id, distance, components)
// returned: count of entries in the array
// truncated: true when budget exhausted before all results were added
```

## Pipeline (MANDATORY order)

1. `JsonParamParser.ParseVector3(args, "origin")` — validate required vector
2. Validate required scalars with `HasValue` + range checks as needed
3. Parse scalar optionals with `?.Value<T>() ?? default`
4. `JsonParamParser.ParseStringArray(args, "include_components")` and `"exclude_tags"`
5. `SpatialEntryFilter.Build(includeComponents, excludeTags)` — returns `null` if both null
6. `SpatialQueryTool.GetIndex()` — lazy-initialized, auto-refreshes on scene changes
7. `index.Nearest/Radius/...` — pass the filter (nullable)
8. `new TokenBudget(budgetTokens)` — create AFTER getting results
9. `ResponseHelpers.AddFrameContext(response)` — FIRST field in response
10. `response["operation"] = "..."` — echo operation name
11. `SpatialResultBuilder.BuildResultsArray(results, budget)` — fills array, tracks budget
12. `budget.ToBudgetJObject(truncated, reason, suggestion)` — always included

## When to Use
- Any `spatial_query:*` sub-handler that uses the transform-based spatial index
- Both edit mode and play mode — no `RequirePlayMode` needed

## When NOT to Use
- Physics-based queries (`overlap`, `raycast`, `linecast`) — those use `PhysicsMode.GetEffective()` and Unity Physics APIs, not the SpatialIndex. Use Physics Mode Dispatch pattern instead.
- Queries that return a single result without a budget (e.g., `bounds`) — no SpatialIndex involved

## Common Violations
- Calling `index.Nearest()` with a non-null filter when `includeComponents` and `excludeTags` are both null — always route through `SpatialEntryFilter.Build` which returns null when appropriate
- Creating `TokenBudget` before running the query — budget is only needed for response building
- Forgetting `response["operation"]` — agents use this to correlate request and response
