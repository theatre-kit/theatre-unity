# Pattern: Dry-Run Support

Director operation tools support a `dry_run` boolean parameter for validation without mutation.

## Rationale

Director tools mutate Unity assets (materials, prefabs, animation clips, etc.). Agents need a way to pre-flight validate operations — check asset paths exist, shaders are available, types resolve — before committing changes. `dry_run=true` runs all pre-mutation checks and returns `would_succeed + errors` without side effects.

## Structure

1. Declare `dry_run` in the JSON schema with description `"If true, validate only — do not mutate. Returns would_succeed and errors."`
2. After all parameter validation (but before any Unity mutation), call `DirectorHelpers.CheckDryRun(args, validator)` where the validator returns `(bool wouldSucceed, List<string> errors)`.
3. If dry-run returns non-null, return it immediately — do not proceed with mutation.
4. Proceed with actual mutation only when `CheckDryRun` returns null.

## Examples

### Example 1: Material create with shader validation
**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/MaterialOpTool.cs:103`
```csharp
// Dry run validation
var dryRun = DirectorHelpers.CheckDryRun(args, () =>
{
    var dryErrors = new List<string>();
    var s = Shader.Find(shaderName);
    if (s == null)
        dryErrors.Add($"Shader '{shaderName}' not found");
    return (dryErrors.Count == 0, dryErrors);
});
if (dryRun != null) return dryRun;

// Proceed with actual creation...
var shader = Shader.Find(shaderName);
var material = new Material(shader);
AssetDatabase.CreateAsset(material, assetPath);
```

### Example 2: Scene create with asset conflict check
**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/SceneOpHandlers.cs`
```csharp
var dryRun = DirectorHelpers.CheckDryRun(args, () =>
{
    var dryErrors = new List<string>();
    var conflict = DirectorHelpers.CheckAssetConflict(scenePath, allowOverwrite: false);
    if (conflict != null) dryErrors.Add(conflict);
    return (dryErrors.Count == 0, dryErrors);
});
if (dryRun != null) return dryRun;
```

### Example 3: CheckDryRun implementation
**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/DirectorHelpers.cs:612`
```csharp
public static string CheckDryRun(
    JObject args,
    Func<(bool wouldSucceed, List<string> errors)> validator)
{
    if (args["dry_run"]?.Value<bool>() != true)
        return null; // Not a dry run — caller proceeds with real op

    var (wouldSucceed, errors) = validator();
    var response = new JObject();
    response["dry_run"] = true;
    response["would_succeed"] = wouldSucceed;
    var errorArray = new JArray();
    foreach (var e in errors ?? new List<string>())
        errorArray.Add(e);
    response["errors"] = errorArray;
    return response.ToString(Formatting.None);
}
```

## When to Use

- Every Director tool operation that mutates a Unity asset
- Any create/update/delete operation where the agent may need to pre-validate inputs

## When NOT to Use

- Stage (read-only) tools — they don't mutate anything
- Action tools — play-mode actions; dry-run isn't applicable there
- Simple read operations within Director tools (e.g., `list_properties`)

## Common Violations

- Forgetting to return the dry-run result: always check `if (dryRun != null) return dryRun;` — omitting the guard proceeds with the real operation even during dry run.
- Running dry-run checks after mutation has already begun — validate ALL preconditions in the validator lambda, not inline.
- Not including `dry_run` in the JSON schema — agents won't know to use it.
