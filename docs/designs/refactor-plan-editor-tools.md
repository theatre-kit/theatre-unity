# Refactor Plan: Editor/Tools Deduplication & Pattern Alignment

## Overview

The previous refactor plan (`refactor-plan.md`) extracted `CompoundToolDispatcher`,
unified `ResolveType`, consolidated `ToPascalCase` into `StringUtils`, and added
`AddFrameContext` to Director responses. Those are done.

This plan targets the **remaining** duplication and pattern violations across
`Editor/Tools/`, focusing on three themes:

1. **Director tool duplication** — 13 identical copies of `EnsureParentDirectory`,
   plus repeated asset-validate-load-nullcheck sequences across every handler.
2. **Pattern violations in Stage tools** — `SceneInspectTool` manually builds
   identity instead of using `AddIdentity`; `TheatreStatusTool` uses raw string
   concatenation instead of `JObject`; `UnityConsoleTool` bypasses
   `CompoundToolDispatcher`.
3. **Cross-cutting** — `PropertySerializer` reimplements property value reading
   that `DirectorHelpers.ReadPropertyValue` already handles.

Each step is a discrete commit that builds and passes tests independently.

---

## Refactor Steps

### Step 1: Extract `EnsureParentDirectory` to `DirectorHelpers`

**Priority**: High
**Risk**: Low — identical implementations, pure move
**Files**:
- `Editor/Tools/Director/DirectorHelpers.cs` (add method)
- 13 Director `*OpTool.cs` files (delete private copy, call shared)

**Current State** (identical in 13 files, e.g. `MaterialOpTool.cs:371-391`):
```csharp
private static void EnsureParentDirectory(string assetPath)
{
    var lastSlash = assetPath.LastIndexOf('/');
    if (lastSlash <= 0) return;

    var parentPath = assetPath.Substring(0, lastSlash);
    if (!AssetDatabase.IsValidFolder(parentPath))
    {
        var grandparentSlash = parentPath.LastIndexOf('/');
        if (grandparentSlash >= 0)
        {
            var grandparent = parentPath.Substring(0, grandparentSlash);
            var folderName = parentPath.Substring(grandparentSlash + 1);
            EnsureParentDirectory(parentPath);
            if (!AssetDatabase.IsValidFolder(parentPath))
                AssetDatabase.CreateFolder(grandparent, folderName);
        }
    }
}
```

**Target State** — single copy in `DirectorHelpers`:
```csharp
/// <summary>
/// Recursively create parent directories for an asset path.
/// E.g., for "Assets/Materials/Enemies/Red.mat" ensures
/// "Assets/Materials/Enemies" exists.
/// </summary>
public static void EnsureParentDirectory(string assetPath)
{
    var lastSlash = assetPath.LastIndexOf('/');
    if (lastSlash <= 0) return;

    var parentPath = assetPath.Substring(0, lastSlash);
    if (!AssetDatabase.IsValidFolder(parentPath))
    {
        var grandparentSlash = parentPath.LastIndexOf('/');
        if (grandparentSlash >= 0)
        {
            var grandparent = parentPath.Substring(0, grandparentSlash);
            var folderName = parentPath.Substring(grandparentSlash + 1);
            EnsureParentDirectory(parentPath);
            if (!AssetDatabase.IsValidFolder(parentPath))
                AssetDatabase.CreateFolder(grandparent, folderName);
        }
    }
}
```

Each tool's call site changes from `EnsureParentDirectory(path)` to
`DirectorHelpers.EnsureParentDirectory(path)`, and the private copy is deleted.

**Affected files** (all in `Editor/Tools/Director/`):
- `MaterialOpTool.cs:371`
- `AnimationClipOpTool.cs:522`
- `AnimatorControllerOpTool.cs:742`
- `AudioMixerOpTool.cs:503`
- `InputActionOpTool.cs:494`
- `PhysicsMaterialOpTool.cs:263`
- `ProBuilderOpTool.cs:428`
- `RenderPipelineOpTool.cs:373`
- `ScriptableObjectOpTool.cs:303`
- `SpriteAtlasOpTool.cs:265`
- `TerrainOpTool.cs:776`
- `TilemapOpTool.cs:652`
- `TimelineOpTool.cs:539`

**Acceptance Criteria**:
- [ ] Build passes
- [ ] Tests pass
- [ ] `grep -rn "private static void EnsureParentDirectory" Editor/Tools/` returns 0 results
- [ ] Only one implementation exists in `DirectorHelpers.cs`

---

### Step 2: Extract `DirectorHelpers.LoadAsset<T>` for validate-load-nullcheck

**Priority**: High
**Risk**: Low — wraps existing helpers, eliminates 3-line boilerplate
**Files**:
- `Editor/Tools/Director/DirectorHelpers.cs` (add method)
- All Director `*OpTool.cs` files using the pattern (~70 call sites)

**Current State** (repeated 3-5 times per Director tool):
```csharp
var assetPath = args["asset_path"]?.Value<string>();
var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".mat");
if (pathError != null) return pathError;

var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
if (material == null)
    return ResponseHelpers.ErrorResponse(
        "asset_not_found",
        $"Material not found at '{assetPath}'",
        "Check the asset path is correct and ends with .mat");
```

**Target State** — new method in `DirectorHelpers`:
```csharp
/// <summary>
/// Parse asset_path from args, validate it, and load the asset.
/// Returns null on success (asset written to out param),
/// or an error response string on failure.
/// </summary>
public static string LoadAsset<T>(
    JObject args, out T asset,
    string requiredExtension = null,
    string pathParam = "asset_path") where T : UnityEngine.Object
{
    asset = null;
    var assetPath = args[pathParam]?.Value<string>();
    var pathError = ValidateAssetPath(assetPath, requiredExtension);
    if (pathError != null) return pathError;

    asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
    if (asset == null)
        return ResponseHelpers.ErrorResponse(
            "asset_not_found",
            $"{typeof(T).Name} not found at '{assetPath}'",
            $"Check the asset path is correct"
            + (requiredExtension != null ? $" and ends with '{requiredExtension}'" : ""));

    return null;
}
```

Each handler becomes:
```csharp
var error = DirectorHelpers.LoadAsset<Material>(args, out var material, ".mat");
if (error != null) return error;
```

**Implementation Notes**:
- Migrate one tool at a time (start with `MaterialOpTool` to prove the pattern)
- Some handlers need the raw `assetPath` string after loading — add an overload
  with `out string assetPath` parameter
- Handlers that validate the path but don't load (e.g., `Create` ops that check
  for conflicts) continue using `ValidateAssetPath` directly

**Acceptance Criteria**:
- [ ] Build passes
- [ ] Tests pass
- [ ] No inline `LoadAssetAtPath` + null-check + ErrorResponse sequences remain
  in Director handlers that load existing assets

---

### Step 3: Migrate `UnityConsoleTool` to `CompoundToolDispatcher`

**Priority**: Medium
**Risk**: Low — behavioral parity, standardized error handling
**Files**: `Editor/Tools/UnityConsoleTool.cs`

**Current State** (`UnityConsoleTool.cs:64-74`):
```csharp
private static string Execute(JToken arguments)
{
    var operation = arguments?["operation"]?.ToObject<string>() ?? "query";

    switch (operation)
    {
        case "summary": return ExecuteSummary();
        case "clear":   return ExecuteClear();
        case "refresh": return ExecuteRefresh();
        default:        return ExecuteQuery(arguments);
    }
}
```

Problems:
- No top-level `try/catch` — exceptions crash silently
- No `null` argument check
- `default` silently falls through to `ExecuteQuery` for unknown operations
  instead of returning an error

**Target State**:
```csharp
private static string Execute(JToken arguments) =>
    CompoundToolDispatcher.Execute(
        "unity_console",
        arguments,
        (args, operation) => operation switch
        {
            "query"   => ExecuteQuery(args),
            "summary" => ExecuteSummary(),
            "clear"   => ExecuteClear(),
            "refresh" => ExecuteRefresh(),
            _ => ResponseHelpers.ErrorResponse(
                "invalid_parameter",
                $"Unknown operation '{operation}'",
                "Valid operations: query, summary, clear, refresh")
        },
        "query, summary, clear, refresh");
```

**Implementation Notes**:
- `operation` defaults to `"query"` in the schema, so passing no operation
  will still work via the MCP client's default handling
- `ExecuteQuery` needs to accept `JObject` instead of `JToken` (minor signature change)
- `ExecuteSummary`/`ExecuteClear`/`ExecuteRefresh` don't use args — they can
  ignore the parameter

**Acceptance Criteria**:
- [ ] Build passes
- [ ] Tests pass
- [ ] Unknown operations return proper error response
- [ ] Exceptions in handlers are caught and logged

---

### Step 4: Fix `SceneInspectTool` to use `AddIdentity`

**Priority**: Medium
**Risk**: Low — response content unchanged (same fields, same values)
**Files**: `Editor/Tools/Scene/SceneInspectTool.cs`

**Current State** (`SceneInspectTool.cs:116-117`):
```csharp
response["path"] = ResponseHelpers.GetHierarchyPath(transform);
response["instance_id"] = go.GetInstanceID();
```

**Target State**:
```csharp
ResponseHelpers.AddIdentity(response, go);
```

**Implementation Notes**:
- `AddIdentity` internally calls `GetHierarchyPath` and `GetInstanceID` —
  the output is identical
- Verify that `AddIdentity` sets `response["path"]` and `response["instance_id"]`
  with the same keys (it does, per `ResponseHelpers.cs:113`)

**Acceptance Criteria**:
- [ ] Build passes
- [ ] Tests pass
- [ ] `SceneInspectTool.cs` has no direct `GetInstanceID()` calls

---

### Step 5: Migrate `TheatreStatusTool` from string concatenation to `JObject`

**Priority**: Medium
**Risk**: Low — output format unchanged
**Files**: `Editor/Tools/TheatreStatusTool.cs`

**Current State** (`TheatreStatusTool.cs:47-54`):
```csharp
return $"{{\"status\":\"ok\""
    + $",\"version\":\"{TheatreConfig.ServerVersion}\""
    + $",\"port\":{TheatreConfig.Port}"
    + $",\"play_mode\":{(playMode ? "true" : "false")}"
    + $",\"active_scene\":\"{sceneName}\""
    + $",\"enabled_groups\":\"{TheatreConfig.EnabledGroups}\""
    + $",\"tool_count\":{TheatreServer.ToolRegistry?.Count ?? 0}"
    + "}";
```

Problems:
- Scene names or group strings containing quotes or backslashes would
  produce invalid JSON
- No `AddFrameContext` — inconsistent with all other tools

**Target State**:
```csharp
var response = new JObject();
response["status"] = "ok";
response["version"] = TheatreConfig.ServerVersion;
response["port"] = TheatreConfig.Port;
response["play_mode"] = playMode;
response["active_scene"] = sceneName;
response["enabled_groups"] = TheatreConfig.EnabledGroups;
response["tool_count"] = TheatreServer.ToolRegistry?.Count ?? 0;
ResponseHelpers.AddFrameContext(response);
return response.ToString(Newtonsoft.Json.Formatting.None);
```

**Implementation Notes**:
- Add `using Newtonsoft.Json.Linq;` and `using Theatre.Stage;` to imports
- The response gains `frame`, `time`, `play_mode` fields from
  `AddFrameContext` — this is a net improvement

**Acceptance Criteria**:
- [ ] Build passes
- [ ] Tests pass
- [ ] Response is valid JSON for all scene names
- [ ] Response includes `frame` and `time` fields

---

### Step 6: Deduplicate `PropertySerializer` value reading

**Priority**: Low
**Risk**: Medium — need to verify output parity between the two implementations
**Files**:
- `Editor/Tools/Scene/PropertySerializer.cs`
- `Editor/Tools/Director/DirectorHelpers.cs` (already has `ReadPropertyValue`)

**Current State**: `PropertySerializer` has its own property-type switch for
reading `SerializedProperty` values into JSON, separate from
`DirectorHelpers.ReadPropertyValue`. Both handle the same property types
(Integer, Float, Boolean, String, Vector2/3, Color, Enum, ObjectReference)
but may differ in edge-case formatting (rounding, null handling).

**Target State**: `PropertySerializer` delegates to
`DirectorHelpers.ReadPropertyValue` for individual property value conversion,
keeping its own logic only for traversal and budget management.

**Implementation Notes**:
- First, audit both implementations side-by-side to identify formatting
  differences (e.g., rounding precision, enum display names vs raw values)
- If there are intentional differences (PropertySerializer returns more
  detail at `Full` level), keep a wrapper that delegates for common cases
- This step may be deferred if the implementations serve genuinely
  different purposes

**Acceptance Criteria**:
- [ ] Build passes
- [ ] `scene_inspect` tests pass with identical output
- [ ] Only one property-type switch exists for reading values to JSON

---

## Implementation Order

1. **Step 1**: `EnsureParentDirectory` → `DirectorHelpers` (highest duplication count, zero risk)
2. **Step 2**: `LoadAsset<T>` helper (depends on Step 1 being done to avoid merge conflicts in same files)
3. **Step 4**: `SceneInspectTool` AddIdentity fix (independent, small)
4. **Step 5**: `TheatreStatusTool` JObject migration (independent, small)
5. **Step 3**: `UnityConsoleTool` CompoundToolDispatcher migration (independent, small)
6. **Step 6**: `PropertySerializer` dedup (lowest priority, needs careful audit)

Steps 3, 4, and 5 are fully independent and can be done in parallel.
Steps 1 and 2 should be sequential (both touch Director tool files).
