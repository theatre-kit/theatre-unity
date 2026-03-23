# Pattern: Test Fixture Infrastructure

Reusable structures for Director and integration tests: temp asset directories, tool invocation helpers, and resource cleanup.

## Rationale

Director tests create real Unity assets and GameObjects that must be cleaned up. Integration tests need a consistent way to call MCP tools through the registry. These shared patterns prevent test pollution and keep test code concise.

## Pattern 1: Temp Directory Setup/Teardown

All test fixtures that create Unity assets use a `_TheatreTest_*` folder created in `[SetUp]` and deleted in `[TearDown]` via `AssetDatabase.DeleteAsset`.

**File**: `Packages/com.theatre.toolkit/Tests/Editor/AnimationToolTests.cs:13`
```csharp
[TestFixture]
public class AnimationClipOpTests
{
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = "Assets/_TheatreTest_Anim";
        if (!AssetDatabase.IsValidFolder(_tempDir))
            AssetDatabase.CreateFolder("Assets", "_TheatreTest_Anim");
    }

    [TearDown]
    public void TearDown()
    {
        AssetDatabase.DeleteAsset(_tempDir);
    }
}
```

Used by: `AnimationToolTests`, `AssetToolTests`, `BlendTreeTimelineTests`, `InputConfigToolTests`, `TerrainProBuilderToolTests`, `DirectorTests`, `OptionalPackageToolTests`, `MediaAssetToolTests` (8 fixtures).

Naming convention: `_TheatreTest_{ToolDomain}` — prefix underscore keeps it sorted to the bottom in the Project window and makes it easy to grep.

## Pattern 2: CallTool / CallAction Helper

Integration test fixtures expose private helper methods that look up the tool by name from `TheatreServer.ToolRegistry` and invoke its handler directly.

**File**: `Packages/com.theatre.toolkit/Tests/Editor/SceneToolIntegrationTests.cs`
```csharp
private string CallTool(string toolName, JToken args)
{
    var tool = TheatreServer.ToolRegistry?.GetTool(
        toolName,
        ToolGroup.Everything);
    Assert.IsNotNull(tool, $"Tool '{toolName}' not found in registry");
    return tool.Handler(args);
}
```

**File**: `Packages/com.theatre.toolkit/Tests/Editor/WatchActionTests.cs`
```csharp
private string CallAction(JObject args)
{
    var tool = TheatreServer.ToolRegistry?.GetTool(
        "action", ToolGroup.Everything);
    Assert.IsNotNull(tool, "action tool not registered");
    return tool.Handler(args);
}

private string CallWatch(JObject args)
{
    var tool = TheatreServer.ToolRegistry?.GetTool(
        "watch", ToolGroup.Everything);
    Assert.IsNotNull(tool, "watch tool not registered");
    return tool.Handler(args);
}
```

Use `ToolGroup.Everything` to bypass group filtering in tests. Always assert the tool is non-null for a clear failure message.

## Pattern 3: Resource Tracking and Cleanup

Stateful tests (watches, created assets) track resource IDs in a list and clean up in `[TearDown]` via tool calls.

**File**: `Packages/com.theatre.toolkit/Tests/Editor/WatchActionTests.cs:530`
```csharp
private readonly List<string> _createdWatchIds = new List<string>();

private string CreateWatch(string target = "*", string conditionType = "spawned",
    string label = null)
{
    var condObj = new JObject { ["type"] = conditionType };
    var createArgs = new JObject
    {
        ["operation"] = "create",
        ["target"] = target,
        ["condition"] = condObj,
        ["throttle_ms"] = 500
    };
    if (label != null)
        createArgs["label"] = label;

    var result = CallWatch(createArgs);
    var watchId = JObject.Parse(result)["watch_id"]?.Value<string>();
    if (watchId != null)
        _createdWatchIds.Add(watchId);  // Track for cleanup
    return watchId;
}

[TearDown]
public void TearDown()
{
    foreach (var id in _createdWatchIds)
        CallWatch(new JObject { ["operation"] = "remove", ["watch_id"] = id });
    _createdWatchIds.Clear();
}
```

## Pattern 4: JObject Argument Construction

Two styles for building tool arguments:

- `JToken.Parse(...)` for static JSON literals (concise, readable inline)
- `new JObject { ... }` for dynamic/conditional argument building

**File**: `Packages/com.theatre.toolkit/Tests/Editor/SceneToolIntegrationTests.cs`
```csharp
// Static literal: parse inline JSON
var args = JToken.Parse(@"{ ""budget"": 4000 }");

// Dynamic: fluent JObject with optional fields
var args = new JObject
{
    ["operation"] = "teleport",
    ["path"] = "/Player",
    ["position"] = new JArray(5f, 2f, 3f)
};
if (rotation != null)
    args["rotation"] = rotation;
```

## When to Use

- **Temp directory**: Any test that creates Unity assets — animation clips, materials, prefabs, scriptable objects, tilemaps, terrain.
- **CallTool helper**: Integration tests that exercise the full tool dispatch path including `CompoundToolDispatcher`.
- **Resource tracking**: Tests that create watches, scenes, or other persistent objects not cleaned up by asset deletion.
- **JObject construction**: All tests. Use `JToken.Parse` for fixed schemas, `new JObject` when parameters are conditional.

## When NOT to Use

- Unit tests for pure C# helpers (e.g., `ResponseHelpers`, `TokenBudget`) — no Unity assets involved, no temp dirs needed.
- Tests that call sub-handler methods directly (e.g., `MaterialOpTool.Create(args)`) — no registry lookup needed.

## Common Violations

- Not deleting the temp folder in `[TearDown]` — leaves `_TheatreTest_*` folders in the project if a test throws.
- Using `ToolGroup.StageAll` instead of `ToolGroup.Everything` in `GetTool` calls — Director/Watch tools won't be found.
- Forgetting to track created IDs in stateful tests — resource leaks cause test interference across runs.
