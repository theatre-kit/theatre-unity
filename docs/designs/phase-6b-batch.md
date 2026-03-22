# Design: Phase 6b — Batch Meta-Tool

## Overview

Atomic multi-operation tool that executes a sequence of Director
operations as a single undo step. If any operation fails, all preceding
operations are rolled back. Asset imports are batched for performance.

This completes Phase 6 by enabling agents to do complex multi-step
workflows (create object → add components → configure → save as prefab)
in a single MCP call.

---

## Architecture

```
Editor/Tools/Director/
  BatchTool.cs    — MCP tool: batch (new)
```

Single file addition. Uses existing infrastructure:
- `TheatreServer.ToolRegistry` to look up tools by name
- `DirectorHelpers.BeginUndoGroup`/`EndUndoGroup` for undo grouping
- `AssetDatabase.StartAssetEditing`/`StopAssetEditing` for import batching

---

## Implementation Units

### Unit 1: BatchTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/BatchTool.cs`

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEditor;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: batch
    /// Execute multiple Director operations as a single atomic unit.
    /// All operations share one undo group and one AssetDatabase import pass.
    /// If any operation fails, preceding operations are rolled back.
    /// </summary>
    public static class BatchTool
    {
        private static readonly JToken s_inputSchema;

        static BatchTool();

        public static void Register(ToolRegistry registry);

        private static string Execute(JToken arguments);
    }
}
```

**JSON Schema**:

```json
{
  "type": "object",
  "properties": {
    "operations": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "tool": {
            "type": "string",
            "description": "Tool name to call (e.g., 'scene_op', 'prefab_op')."
          },
          "params": {
            "type": "object",
            "description": "Parameters to pass to the tool (same as calling the tool directly)."
          }
        },
        "required": ["tool", "params"]
      },
      "description": "Array of operations to execute sequentially. Each is a {tool, params} pair.",
      "minItems": 1,
      "maxItems": 50
    },
    "dry_run": {
      "type": "boolean",
      "description": "If true, validate all operations without executing."
    }
  },
  "required": ["operations"]
}
```

**Registration**:
```csharp
registry.Register(new ToolRegistration(
    name: "batch",
    description: "Execute multiple Director operations atomically. "
        + "All operations form a single undo step. If any fails, "
        + "all preceding operations are rolled back. Asset imports "
        + "are batched for performance. "
        + "Each operation is {\"tool\": \"scene_op\", \"params\": {...}}.",
    inputSchema: s_inputSchema,
    group: ToolGroup.DirectorScene,
    handler: Execute,
    annotations: new McpToolAnnotations { ReadOnlyHint = false }
));
```

**Tool Group**: `ToolGroup.DirectorScene`

The `batch` tool belongs to `DirectorScene` because it orchestrates
scene-level operations. If `DirectorScene` is disabled, `batch` is
hidden from `tools/list`.

**Inner tool group enforcement**: Each operation within a batch is
validated against its own tool group. A batch can only contain tools
whose groups are currently enabled. If an inner operation's group is
disabled, the batch fails immediately with:

```json
{
    "result": "error",
    "operation": "batch",
    "error": {
        "code": "operation_not_supported",
        "message": "Tool 'prefab_op' is not available (DirectorPrefab group disabled)",
        "suggestion": "Enable the DirectorPrefab tool group or remove prefab operations from the batch"
    }
}
```

**Implementation Notes**:

The `Execute` method:

```csharp
private static string Execute(JToken arguments)
{
    var args = (JObject)arguments;
    var ops = args["operations"] as JArray;

    // Validation
    if (ops == null || ops.Count == 0)
        return ErrorResponse("invalid_parameter", "...", "...");

    // Dry run: validate each op without executing
    if (args["dry_run"]?.Value<bool>() == true)
        return ExecuteDryRun(ops);

    // Begin atomic batch
    int undoGroup = DirectorHelpers.BeginUndoGroup("Batch");
    AssetDatabase.StartAssetEditing();

    var results = new JArray();
    string failedResult = null;
    int failedIndex = -1;

    try
    {
        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i] as JObject;
            var toolName = op?["tool"]?.Value<string>();
            var toolParams = op?["params"] as JObject;

            if (string.IsNullOrEmpty(toolName) || toolParams == null)
            {
                failedResult = ErrorResponse("invalid_parameter",
                    $"Operation {i}: missing 'tool' or 'params'", "...");
                failedIndex = i;
                break;
            }

            // Strip dry_run from inner params (batch handles it at top level)
            toolParams.Remove("dry_run");

            // Look up the tool in the registry
            var registry = TheatreServer.ToolRegistry;
            // Use GetToolDirect to bypass group filtering — batch is
            // already group-filtered, inner tools should be callable
            var tool = registry.GetTool(toolName,
                TheatreConfig.EnabledGroups,
                TheatreConfig.DisabledTools);

            if (tool == null)
            {
                failedResult = ErrorResponse("invalid_parameter",
                    $"Operation {i}: tool '{toolName}' not found or not enabled",
                    "Check tools/list for available tools");
                failedIndex = i;
                break;
            }

            // Execute the tool handler directly
            var result = tool.Handler(toolParams);

            // Check if result is an error
            var resultObj = JObject.Parse(result);
            if (resultObj["error"] != null)
            {
                failedResult = result;
                failedIndex = i;
                break;
            }

            results.Add(resultObj);
        }
    }
    finally
    {
        AssetDatabase.StopAssetEditing();
    }

    // If any operation failed, rollback all preceding operations
    if (failedIndex >= 0)
    {
        Undo.RevertAllInCurrentGroup();
        DirectorHelpers.EndUndoGroup(undoGroup);

        var errorResponse = new JObject();
        errorResponse["result"] = "error";
        errorResponse["operation_count"] = ops.Count;
        errorResponse["failed_index"] = failedIndex;
        errorResponse["error"] = JObject.Parse(failedResult)["error"];
        errorResponse["completed_before_failure"] = results;
        ResponseHelpers.AddFrameContext(errorResponse);
        return errorResponse.ToString(Formatting.None);
    }

    // Success — collapse undo group
    DirectorHelpers.EndUndoGroup(undoGroup);

    var response = new JObject();
    response["result"] = "ok";
    response["operation_count"] = results.Count;
    response["results"] = results;
    ResponseHelpers.AddFrameContext(response);
    return response.ToString(Formatting.None);
}
```

**Dry run** validates each operation by injecting `dry_run: true` into
each op's params and calling the handler. If all succeed, returns:
```json
{
    "dry_run": true,
    "would_succeed": true,
    "operation_count": 3
}
```

If any would fail:
```json
{
    "dry_run": true,
    "would_succeed": false,
    "failed_index": 1,
    "error": { "code": "...", "message": "...", "suggestion": "..." }
}
```

**Response shapes**:

Success:
```json
{
    "result": "ok",
    "operation_count": 3,
    "results": [
        { "result": "ok", "operation": "create_gameobject", "path": "/Enemy", ... },
        { "result": "ok", "operation": "set_component", ... },
        { "result": "ok", "operation": "create_prefab", ... }
    ],
    "frame": ..., "time": ..., "play_mode": ...
}
```

Failure with rollback:
```json
{
    "result": "error",
    "operation_count": 3,
    "failed_index": 2,
    "error": { "code": "...", "message": "...", "suggestion": "..." },
    "completed_before_failure": [
        { "result": "ok", "operation": "create_gameobject", ... },
        { "result": "ok", "operation": "set_component", ... }
    ],
    "frame": ..., "time": ..., "play_mode": ...
}
```

**Key design decisions**:
- **ToolGroup**: Registered under `DirectorScene` since batch is a
  Director-level concern. Alternatively could be a new group, but since
  batch only calls Director tools, using DirectorScene keeps it simple.
  The batch tool is available whenever any Director group is enabled.
- **Max 50 operations**: Hard limit to prevent agents from constructing
  unbounded batches that could overwhelm the editor.
- **Inner tool lookup uses group filtering**: An operation in the batch
  can only call tools that are currently enabled. This prevents batch
  from bypassing tool group restrictions.
- **Error parsing**: After each handler call, parse the result JSON and
  check for an `"error"` key. This is how all Theatre tools report errors.
- **AssetDatabase batching**: `StartAssetEditing`/`StopAssetEditing`
  wraps the entire batch — one import pass at the end, not per-operation.
  This significantly improves performance for asset-heavy batches.
- **Undo rollback**: `Undo.RevertAllInCurrentGroup()` undoes everything
  in the current undo group. This is Unity's built-in mechanism for
  all-or-nothing transactions.

**Acceptance Criteria**:
- [ ] Batch with 3 valid operations executes all and returns results array
- [ ] Batch where operation 2 fails rolls back operation 1
- [ ] All operations form a single Ctrl+Z undo step
- [ ] Dry run validates without mutating
- [ ] Invalid tool name returns error at correct index
- [ ] Empty operations array returns error
- [ ] AssetDatabase batching wraps entire execution (one import pass)
- [ ] Max 50 operations enforced
- [ ] Inner ops respect tool group filtering

---

### Unit 2: Server Integration

**File**: `Packages/com.theatre.toolkit/Editor/TheatreServer.cs` (modify)

Add `BatchTool.Register(registry);` to `RegisterBuiltInTools()` after
the existing Director tool registrations.

**Acceptance Criteria**:
- [ ] `batch` appears in `tools/list`

---

### Unit 3: ToolRegistry Enhancement (optional)

The current `GetTool` requires `enabledGroups` and `disabledTools`
parameters. For `batch`, we need to access these from `TheatreConfig`.
Check if `TheatreConfig.EnabledGroups` and `.DisabledTools` are
accessible from the main thread (they are — tools run on main thread
via `MainThreadDispatcher`).

No code changes needed — `TheatreConfig` properties are already
accessible. The batch handler reads them directly.

---

## Implementation Order

1. Unit 1: BatchTool.cs (the tool itself)
2. Unit 2: Server Integration (register in TheatreServer)

---

## Testing

### Unit Tests: `Tests/Editor/DirectorTests.cs` (append)

```csharp
[TestFixture]
public class BatchToolTests
{
    [Test]
    public void Batch_MultipleOps_ExecutesAll()
    {
        var ops = new JArray
        {
            new JObject { ["tool"] = "scene_op", ["params"] = new JObject
                { ["operation"] = "create_gameobject", ["name"] = "BatchObj1" } },
            new JObject { ["tool"] = "scene_op", ["params"] = new JObject
                { ["operation"] = "create_gameobject", ["name"] = "BatchObj2" } }
        };
        var result = BatchTool.Execute(new JObject { ["operations"] = ops });
        Assert.That(result, Does.Contain("\"result\":\"ok\""));
        Assert.That(result, Does.Contain("\"operation_count\":2"));

        // Cleanup
        var go1 = GameObject.Find("BatchObj1");
        var go2 = GameObject.Find("BatchObj2");
        if (go1) Object.DestroyImmediate(go1);
        if (go2) Object.DestroyImmediate(go2);
    }

    [Test]
    public void Batch_FailedOp_RollsBackPreceding()
    {
        var ops = new JArray
        {
            new JObject { ["tool"] = "scene_op", ["params"] = new JObject
                { ["operation"] = "create_gameobject", ["name"] = "RollbackTest" } },
            new JObject { ["tool"] = "scene_op", ["params"] = new JObject
                { ["operation"] = "delete_gameobject", ["path"] = "/NonExistent_Batch" } }
        };
        var result = BatchTool.Execute(new JObject { ["operations"] = ops });
        Assert.That(result, Does.Contain("\"result\":\"error\""));
        Assert.That(result, Does.Contain("\"failed_index\":1"));
        // The first op should have been rolled back
        Assert.IsNull(GameObject.Find("RollbackTest"));
    }

    [Test]
    public void Batch_DryRun_DoesNotMutate()
    {
        var ops = new JArray
        {
            new JObject { ["tool"] = "scene_op", ["params"] = new JObject
                { ["operation"] = "create_gameobject", ["name"] = "DryRunBatchGhost" } }
        };
        var result = BatchTool.Execute(new JObject
            { ["operations"] = ops, ["dry_run"] = true });
        Assert.That(result, Does.Contain("\"dry_run\":true"));
        Assert.IsNull(GameObject.Find("DryRunBatchGhost"));
    }

    [Test]
    public void Batch_EmptyOps_ReturnsError()
    {
        var result = BatchTool.Execute(new JObject
            { ["operations"] = new JArray() });
        Assert.That(result, Does.Contain("\"error\""));
    }

    [Test]
    public void Batch_InvalidToolName_ReturnsErrorAtIndex()
    {
        var ops = new JArray
        {
            new JObject { ["tool"] = "nonexistent_tool",
                ["params"] = new JObject { ["foo"] = "bar" } }
        };
        var result = BatchTool.Execute(new JObject { ["operations"] = ops });
        Assert.That(result, Does.Contain("\"failed_index\":0"));
    }
}
```

---

## Verification Checklist

1. `unity_console {"operation": "refresh"}` — recompile
2. `unity_console {"filter": "error"}` — no compile errors
3. `unity_tests {"operation": "run"}` — all tests pass
4. Manual test: call `batch` with create_gameobject + set_component,
   verify both applied, Ctrl+Z undoes both
5. Manual test: call `batch` where second op fails, verify first op
   was rolled back
