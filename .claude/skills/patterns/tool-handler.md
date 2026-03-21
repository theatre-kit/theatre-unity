# Pattern: Tool Handler

Every MCP tool is a static class with a JSON Schema, a `Register()` method, and an `Execute()` handler returning a JSON string.

## Rationale

Tools must be stateless (Unity domain reloads destroy all managed state), self-describing (schema is the contract the MCP client sees), and easy to toggle on/off by group. The static class + Register/Execute split satisfies all three.

## Examples

### Example 1: Read-only tool with budgeted results
**File**: `Editor/Tools/SceneSnapshotTool.cs:17-86`
```csharp
public static class SceneSnapshotTool
{
    private static readonly JToken s_inputSchema;

    static SceneSnapshotTool()
    {
        s_inputSchema = JToken.Parse(@"{
            ""type"": ""object"",
            ""properties"": { ... },
            ""required"": []
        }");
    }

    public static void Register(ToolRegistry registry)
    {
        registry.Register(new ToolRegistration(
            name: "scene_snapshot",
            description: "...",
            inputSchema: s_inputSchema,
            group: ToolGroup.StageGameObject,
            handler: Execute,
            annotations: new McpToolAnnotations { ReadOnlyHint = true }
        ));
    }

    private static string Execute(JToken arguments)
    {
        var args = (JObject)arguments;
        // ... parse args, call Unity APIs on main thread, return JSON string
    }
}
```

### Example 2: Mutating tool with ReadOnlyHint false
**File**: `Editor/Tools/ActionTool.cs:82-100`
```csharp
public static void Register(ToolRegistry registry)
{
    registry.Register(new ToolRegistration(
        name: "action",
        description: "Manipulate game state ...",
        inputSchema: s_inputSchema,
        group: ToolGroup.StageAction,
        handler: Execute,
        annotations: new McpToolAnnotations { ReadOnlyHint = false }
    ));
}
```

### Example 3: Central registration in server startup
**File**: `Editor/TheatreServer.cs:165-177`
```csharp
private static void RegisterBuiltInTools(ToolRegistry registry)
{
    TheatreStatusTool.Register(registry);
    SceneSnapshotTool.Register(registry);
    SceneHierarchyTool.Register(registry);
    SceneInspectTool.Register(registry);
    SpatialQueryTool.Register(registry);
    WatchTool.Register(registry);
    ActionTool.Register(registry);
    SceneDeltaTool.Register(registry);
    UnityConsoleTool.Register(registry);
    UnityTestsTool.Register(registry);
}
```

## When to Use
- Every new MCP tool follows this pattern exactly
- Schema is parsed once in the static constructor and reused across all requests
- `ReadOnlyHint = true` for query tools; `false` for anything that mutates scene or assets

## When NOT to Use
- Sub-handlers within a compound tool (e.g., `ActionTeleport`, `SpatialQueryNearest`) do **not** follow this pattern — they are not registered directly; they are called from a parent compound tool

## Common Violations
- Parsing schema in `Execute()` on every call — use static constructor instead
- Forgetting `ReadOnlyHint` on mutations — MCP clients use this hint for safety UIs
- Putting logic that calls Unity APIs in the constructor — only schema parsing belongs there
