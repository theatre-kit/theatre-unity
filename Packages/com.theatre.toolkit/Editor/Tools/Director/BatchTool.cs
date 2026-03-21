using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: batch
    /// Executes multiple tool operations sequentially as a single atomic unit.
    /// All operations share one undo group and one AssetDatabase editing batch.
    /// If any operation fails, all preceding mutations are rolled back via Undo.
    /// </summary>
    public static class BatchTool
    {
        private static readonly JToken s_inputSchema;

        static BatchTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operations"": {
                        ""type"": ""array"",
                        ""items"": {
                            ""type"": ""object"",
                            ""properties"": {
                                ""tool"": { ""type"": ""string"", ""description"": ""Tool name (e.g., 'scene_op', 'prefab_op')."" },
                                ""params"": { ""type"": ""object"", ""description"": ""Parameters for the tool call."" }
                            },
                            ""required"": [""tool"", ""params""]
                        },
                        ""minItems"": 1, ""maxItems"": 50,
                        ""description"": ""Operations to execute sequentially as an atomic unit.""
                    },
                    ""dry_run"": { ""type"": ""boolean"", ""description"": ""If true, validate without executing."" }
                },
                ""required"": [""operations""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "batch",
                description: "Execute multiple tool operations as a single atomic unit. "
                    + "All operations share one undo group — if any operation fails, "
                    + "all preceding mutations are rolled back. "
                    + "Supports dry_run to validate all operations without mutating. "
                    + "Maximum 50 operations per batch.",
                inputSchema: s_inputSchema,
                group: ToolGroup.DirectorScene,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = false
                }
            ));
        }

        /// <summary>
        /// Execute a batch of operations atomically.
        /// Internal visibility allows direct testing without going through the registry.
        /// </summary>
        internal static string Execute(JToken arguments)
        {
            if (arguments == null || arguments.Type != JTokenType.Object)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Arguments must be a JSON object with an 'operations' array",
                    "Provide {\"operations\": [{\"tool\": \"scene_op\", \"params\": {...}}, ...]}");
            }

            var args = (JObject)arguments;
            var operationsToken = args["operations"];

            if (operationsToken == null || operationsToken.Type != JTokenType.Array)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operations' array",
                    "Provide {\"operations\": [{\"tool\": \"scene_op\", \"params\": {...}}, ...]}");
            }

            var operations = (JArray)operationsToken;

            if (operations.Count == 0)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Operations array must not be empty",
                    "Provide at least one operation in the array");
            }

            if (operations.Count > 50)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Operations array exceeds maximum of 50 (got {operations.Count})",
                    "Split the batch into smaller groups of 50 operations or fewer");
            }

            // Validate that every operation has required fields before doing any work
            for (int i = 0; i < operations.Count; i++)
            {
                var op = operations[i] as JObject;
                if (op == null)
                {
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Operation at index {i} must be a JSON object",
                        "Each operation must have {\"tool\": \"...\", \"params\": {...}}");
                }

                var toolName = op["tool"]?.Value<string>();
                if (string.IsNullOrEmpty(toolName))
                {
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Operation at index {i} is missing required 'tool' field",
                        "Each operation must have {\"tool\": \"...\", \"params\": {...}}");
                }

                if (op["params"] == null || op["params"].Type != JTokenType.Object)
                {
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Operation at index {i} is missing required 'params' object",
                        "Each operation must have {\"tool\": \"...\", \"params\": {...}}");
                }
            }

            bool isDryRun = args["dry_run"]?.Value<bool>() == true;

            try
            {
                if (isDryRun)
                    return ExecuteDryRun(operations, args);
                else
                    return ExecuteLive(operations, args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] batch failed with exception: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"batch failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }

        private static string ExecuteDryRun(JArray operations, JObject args)
        {
            var results = new JArray();
            var registry = TheatreServer.ToolRegistry;

            for (int i = 0; i < operations.Count; i++)
            {
                var op = (JObject)operations[i];
                var toolName = op["tool"].Value<string>();
                var opParams = (JObject)op["params"].DeepClone();

                // Inject dry_run into inner params so each tool validates only
                opParams["dry_run"] = true;

                var tool = registry?.GetTool(
                    toolName,
                    TheatreConfig.EnabledGroups,
                    TheatreConfig.DisabledTools);

                if (tool == null)
                {
                    // Dry run validation: report tool-not-found but continue checking rest
                    var notFoundResult = new JObject();
                    notFoundResult["index"] = i;
                    notFoundResult["tool"] = toolName;
                    notFoundResult["error"] = new JObject
                    {
                        ["code"] = "tool_not_found",
                        ["message"] = $"Tool '{toolName}' not found or not enabled"
                    };
                    results.Add(notFoundResult);
                    continue;
                }

                var resultJson = tool.Handler(opParams);
                JObject resultObj;
                try { resultObj = JObject.Parse(resultJson); }
                catch { resultObj = new JObject { ["raw"] = resultJson }; }

                var entry = new JObject();
                entry["index"] = i;
                entry["tool"] = toolName;
                entry["result"] = resultObj;
                results.Add(entry);
            }

            var response = new JObject();
            response["dry_run"] = true;
            response["operation_count"] = operations.Count;
            response["results"] = results;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        private static string ExecuteLive(JArray operations, JObject args)
        {
#if UNITY_EDITOR
            var registry = TheatreServer.ToolRegistry;
            var results = new JArray();
            int failedIndex = -1;
            JObject failureError = null;
            int undoGroup = DirectorHelpers.BeginUndoGroup("batch");

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < operations.Count; i++)
                {
                    var op = (JObject)operations[i];
                    var toolName = op["tool"].Value<string>();
                    var opParams = (JObject)op["params"].DeepClone();

                    // Ensure dry_run is not forwarded to inner tool
                    opParams.Remove("dry_run");

                    var tool = registry?.GetTool(
                        toolName,
                        TheatreConfig.EnabledGroups,
                        TheatreConfig.DisabledTools);

                    if (tool == null)
                    {
                        failedIndex = i;
                        failureError = new JObject
                        {
                            ["code"] = "tool_not_found",
                            ["message"] = $"Tool '{toolName}' not found or not enabled",
                            ["suggestion"] = "Check that the tool name is correct and the tool group is enabled"
                        };
                        break;
                    }

                    var resultJson = tool.Handler(opParams);
                    JObject resultObj;
                    try { resultObj = JObject.Parse(resultJson); }
                    catch { resultObj = new JObject { ["raw"] = resultJson }; }

                    if (resultObj["error"] != null)
                    {
                        failedIndex = i;
                        failureError = resultObj["error"] as JObject ?? new JObject
                        {
                            ["code"] = "operation_failed",
                            ["message"] = $"Operation '{toolName}' at index {i} returned an error"
                        };
                        break;
                    }

                    var entry = new JObject();
                    entry["index"] = i;
                    entry["tool"] = toolName;
                    entry["result"] = resultObj;
                    results.Add(entry);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            if (failedIndex >= 0)
            {
                // Roll back all mutations in this undo group
                Undo.RevertAllInCurrentGroup();

                var errorResponse = new JObject();
                errorResponse["result"] = "error";
                errorResponse["operation_count"] = operations.Count;
                errorResponse["failed_index"] = failedIndex;
                errorResponse["error"] = failureError;
                errorResponse["completed_before_failure"] = results;
                ResponseHelpers.AddFrameContext(errorResponse);
                return errorResponse.ToString(Formatting.None);
            }

            DirectorHelpers.EndUndoGroup(undoGroup);

            var successResponse = new JObject();
            successResponse["result"] = "ok";
            successResponse["operation_count"] = operations.Count;
            successResponse["results"] = results;
            ResponseHelpers.AddFrameContext(successResponse);
            return successResponse.ToString(Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "editor_only",
                "batch is only available in the Unity Editor",
                "Run Theatre in the Unity Editor");
#endif
        }
    }
}
