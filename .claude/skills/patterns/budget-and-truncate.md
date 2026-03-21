# Pattern: Budget-and-Truncate

Tools that return variable-sized lists (spatial queries, scene snapshots) use a `TokenBudget` to limit response size. Items are added incrementally with pre-flight `WouldExceed` checks; the response always includes a `"budget"` envelope with truncation metadata.

## Rationale

MCP responses have no hard size limit, but very large responses hurt agent context windows. The budget pattern lets callers control response size while ensuring the response always arrives complete (not cut off mid-JSON). The `TokenBudget` uses a 4-chars-per-token heuristic, sufficient for practical bounding.

## Examples

### Example 1: SpatialResultBuilder — canonical budgeted loop
**File**: `Editor/Tools/SpatialResultBuilder.cs:25-67`
```csharp
public static (JArray results, int returned, bool truncated) BuildResultsArray(
    IReadOnlyList<SpatialResult> results, TokenBudget budget)
{
    var arr = new JArray();
    int returned = 0;

    foreach (var result in results)
    {
        if (budget.IsExhausted) break;                          // hard exit

        var entryObj = new JObject();
        // ... build entry ...
        var json = entryObj.ToString(Formatting.None);
        if (budget.WouldExceed(json.Length)) break;             // pre-flight check

        arr.Add(entryObj);
        budget.Add(json.Length);                                // record usage
        returned++;
    }

    bool truncated = returned < results.Count;
    return (arr, returned, truncated);
}
```

### Example 2: Tool handler — query budget parameters and assemble budget envelope
**File**: `Editor/Tools/SpatialQueryNearest.cs:28-63`
```csharp
int budgetTokens = args["budget"]?.Value<int>() ?? TokenBudget.DefaultBudget;
// ...
var budget = new TokenBudget(budgetTokens);

var (resultsArray, returned, truncated) = SpatialResultBuilder.BuildResultsArray(results, budget);

response["results"] = resultsArray;
response["returned"] = returned;
response["budget"] = budget.ToBudgetJObject(
    truncated: truncated,
    reason: truncated ? "budget" : null,
    suggestion: truncated ? "Reduce count or increase budget to see more results" : null);
```

### Example 3: SceneSnapshotTool — per-entry estimation for hierarchy entries
**File**: `Editor/Tools/SceneSnapshotTool.cs:222-253`
```csharp
var budget = new TokenBudget(budgetTokens);
foreach (var entry in entries)
{
    if (budget.IsExhausted) break;
    var estimatedCost = TokenBudget.EstimateEntryTokens(entry);
    if (budget.WouldExceed(estimatedCost * 4)) break;
    // ... build entry object ...
    objectsArray.Add(entryObj);
    budget.Add(estimatedCost * 4);
    returned++;
}
response["budget"] = budget.ToBudgetJObject(
    truncated: returned < entries.Count,
    reason: returned < entries.Count ? "budget" : null,
    suggestion: returned < entries.Count ? "Increase budget or use radius/include_components to narrow scope" : null);
```

## TokenBudget API

| Member | Purpose |
|--------|---------|
| `DefaultBudget = 1500` | Default token target |
| `HardCap = 4000` | Maximum allowed (clamped in constructor) |
| `IsExhausted` | True when estimated tokens ≥ budget |
| `WouldExceed(int chars)` | True if adding `chars` would breach budget |
| `Add(int chars)` | Record that `chars` characters were written |
| `ToBudgetJObject(truncated, reason, suggestion)` | Build the `"budget"` field |
| `EstimateEntryTokens(HierarchyEntry)` | Per-entry estimate for hierarchy results |

## When to Use
- Any tool that returns a list of results whose size depends on scene content
- Always expose `"budget"` parameter in the tool's JSON schema with `default: 1500`

## When NOT to Use
- Fixed-size responses (single object inspect, status, watch create) — no budget needed
- Error responses — early exit before budget is relevant

## Common Violations
- Calling `budget.Add()` without first calling `budget.WouldExceed()` — can overshoot the budget
- Forgetting to include `"returned"` and `"budget"` in the response — agents can't know if results were truncated
- Using raw character counts instead of `budget.Add()` — always route through the budget tracker
