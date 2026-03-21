# Design: Console Log & Test Results MCP Tool

## Overview

Two new MCP tools that let AI agents read Unity's Console log and run/read
test results without the human copy-pasting. Essential for development
workflow — the agent can see compile errors, runtime exceptions, and test
failures directly.

**Tools:**
- `unity_console` — read recent Console log entries (errors, warnings, logs)
- `unity_tests` — run EditMode tests and get results

Both assigned to `ToolGroup.StageGameObject` (visible by default).

---

## Implementation Units

### Unit 1: Console Log Buffer

**File:** `Packages/com.theatre.toolkit/Editor/Tools/ConsoleLogBuffer.cs`

Captures Console log messages in a ring buffer via `Application.logMessageReceived`.
Persists across tool calls (not across domain reloads — that's fine, the
interesting messages are recent ones).

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// Captures Unity Console log entries in a ring buffer.
    /// Subscribes via Application.logMessageReceived on [InitializeOnLoad].
    /// </summary>
    [UnityEditor.InitializeOnLoad]
    public static class ConsoleLogBuffer
    {
        /// <summary>A single captured log entry.</summary>
        public readonly struct LogEntry
        {
            public readonly string Message;
            public readonly string StackTrace;
            public readonly LogType Type;
            public readonly DateTime Timestamp;

            public LogEntry(string message, string stackTrace, LogType type)
            {
                Message = message;
                StackTrace = stackTrace;
                Type = type;
                Timestamp = DateTime.UtcNow;
            }
        }

        private static readonly List<LogEntry> s_entries = new();
        private static readonly object s_lock = new();
        private const int MaxEntries = 500;

        static ConsoleLogBuffer()
        {
            Application.logMessageReceived += OnLogMessage;
        }

        private static void OnLogMessage(
            string message, string stackTrace, LogType type)
        {
            lock (s_lock)
            {
                if (s_entries.Count >= MaxEntries)
                    s_entries.RemoveAt(0);
                s_entries.Add(new LogEntry(message, stackTrace, type));
            }
        }

        /// <summary>
        /// Get recent log entries. Thread-safe.
        /// </summary>
        /// <param name="count">Max entries to return (most recent first).</param>
        /// <param name="filter">
        /// Optional filter: "error", "warning", "log", "exception", or null for all.
        /// </param>
        public static List<LogEntry> GetRecent(
            int count = 50, string filter = null)
        {
            lock (s_lock)
            {
                var result = new List<LogEntry>();
                for (int i = s_entries.Count - 1;
                     i >= 0 && result.Count < count; i--)
                {
                    var entry = s_entries[i];
                    if (filter != null && !MatchesFilter(entry.Type, filter))
                        continue;
                    result.Add(entry);
                }
                return result;
            }
        }

        /// <summary>Total entries in buffer.</summary>
        public static int Count
        {
            get { lock (s_lock) return s_entries.Count; }
        }

        /// <summary>Clear all entries.</summary>
        public static void Clear()
        {
            lock (s_lock) s_entries.Clear();
        }

        private static bool MatchesFilter(LogType type, string filter)
        {
            return filter switch
            {
                "error" => type == LogType.Error,
                "warning" => type == LogType.Warning,
                "log" => type == LogType.Log,
                "exception" => type == LogType.Exception,
                "assert" => type == LogType.Assert,
                _ => true
            };
        }
    }
}
```

**Implementation Notes:**
- `Application.logMessageReceived` fires on the main thread only — no
  thread-safety issues with the callback itself. The lock is for
  `GetRecent` which may be called from a tool handler (main thread via
  dispatcher, but lock is cheap insurance).
- Ring buffer at 500 entries. Oldest entries are discarded.
- `GetRecent` returns most-recent-first so the agent sees the latest
  messages at the top.

**Acceptance Criteria:**
- [ ] Captures `Debug.Log`, `Debug.LogWarning`, `Debug.LogError`, `Debug.LogException` messages
- [ ] Ring buffer caps at 500 entries
- [ ] `GetRecent(count, filter)` returns filtered, most-recent-first
- [ ] `Clear()` empties the buffer

---

### Unit 2: unity_console Tool

**File:** `Packages/com.theatre.toolkit/Editor/Tools/UnityConsoleTool.cs`

```csharp
using Newtonsoft.Json.Linq;
using Theatre.Transport;

namespace Theatre.Editor
{
    /// <summary>
    /// MCP tool to read Unity Console log entries.
    /// </summary>
    public static class UnityConsoleTool
    {
        private static readonly JToken s_inputSchema;

        static UnityConsoleTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""count"": {
                        ""type"": ""integer"",
                        ""description"": ""Max entries to return (default 50, max 200)"",
                        ""default"": 50
                    },
                    ""filter"": {
                        ""type"": ""string"",
                        ""enum"": [""error"", ""warning"", ""log"", ""exception"", ""all""],
                        ""description"": ""Filter by log type (default: all)"",
                        ""default"": ""all""
                    },
                    ""clear"": {
                        ""type"": ""boolean"",
                        ""description"": ""Clear the buffer after reading (default false)"",
                        ""default"": false
                    }
                },
                ""required"": []
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "unity_console",
                description: "Read recent Unity Console log entries. "
                    + "Returns errors, warnings, and log messages. "
                    + "Use filter='error' to see only errors.",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageGameObject,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = true
                }
            ));
        }

        private static string Execute(JToken arguments)
        {
            int count = arguments?["count"]?.ToObject<int>() ?? 50;
            if (count > 200) count = 200;
            if (count < 1) count = 1;

            var filterStr = arguments?["filter"]?.ToObject<string>();
            if (filterStr == "all") filterStr = null;

            bool clear = arguments?["clear"]?.ToObject<bool>() ?? false;

            var entries = ConsoleLogBuffer.GetRecent(count, filterStr);

            var result = new JObject();
            result["total_in_buffer"] = ConsoleLogBuffer.Count;
            result["returned"] = entries.Count;
            if (filterStr != null) result["filter"] = filterStr;

            var arr = new JArray();
            foreach (var entry in entries)
            {
                var obj = new JObject();
                obj["type"] = entry.Type.ToString().ToLowerInvariant();
                obj["message"] = entry.Message;
                if (!string.IsNullOrEmpty(entry.StackTrace))
                    obj["stack_trace"] = entry.StackTrace.TrimEnd();
                obj["timestamp"] = entry.Timestamp.ToString("HH:mm:ss.fff");
                arr.Add(obj);
            }
            result["entries"] = arr;

            if (clear) ConsoleLogBuffer.Clear();

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
```

**Implementation Notes:**
- Tool handler runs on main thread (dispatched by TheatreServer) — safe
  to call `ConsoleLogBuffer.GetRecent`.
- `ReadOnlyHint = true` since it reads state but doesn't mutate the game.
  The `clear` option mutates the buffer but not the game.
- Max 200 entries per call to keep response size reasonable.
- Stack traces are trimmed to remove trailing newlines.

**Acceptance Criteria:**
- [ ] `unity_console` appears in `tools/list`
- [ ] Returns recent log entries with type, message, stack_trace, timestamp
- [ ] `filter: "error"` returns only errors
- [ ] `count: 10` limits results to 10
- [ ] `clear: true` empties the buffer after reading

---

### Unit 3: unity_tests Tool

**File:** `Packages/com.theatre.toolkit/Editor/Tools/UnityTestsTool.cs`

```csharp
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor.TestTools.TestRunner.Api;
using Theatre.Transport;

namespace Theatre.Editor
{
    /// <summary>
    /// MCP tool to run Unity EditMode tests and retrieve results.
    /// </summary>
    public static class UnityTestsTool
    {
        private static readonly JToken s_inputSchema;
        private static TestResultCapture s_lastResults;

        static UnityTestsTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""run"", ""results""],
                        ""description"": ""'run' executes tests and returns results. 'results' returns last run results without re-running."",
                        ""default"": ""results""
                    },
                    ""filter"": {
                        ""type"": ""string"",
                        ""description"": ""Test name filter (partial match). Only tests matching this string will run."",
                        ""default"": """"
                    },
                    ""failures_only"": {
                        ""type"": ""boolean"",
                        ""description"": ""Only return failed/errored tests (default false)"",
                        ""default"": false
                    }
                },
                ""required"": []
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "unity_tests",
                description: "Run Unity EditMode tests and get results. "
                    + "Use operation='run' to execute tests, "
                    + "operation='results' to see last run results.",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageGameObject,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = true
                }
            ));
        }

        private static string Execute(JToken arguments)
        {
            var operation = arguments?["operation"]?.ToObject<string>() ?? "results";
            var filter = arguments?["filter"]?.ToObject<string>() ?? "";
            bool failuresOnly = arguments?["failures_only"]?.ToObject<bool>() ?? false;

            if (operation == "run")
            {
                return RunTests(filter, failuresOnly);
            }
            else
            {
                return GetLastResults(failuresOnly);
            }
        }

        private static string RunTests(string filter, bool failuresOnly)
        {
            var capture = new TestResultCapture();
            var api = UnityEngine.ScriptableObject
                .CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(capture);

            var settings = new ExecutionSettings
            {
                filters = new[]
                {
                    new Filter { testMode = TestMode.EditMode }
                }
            };

            // Note: Execute() runs tests asynchronously in Unity.
            // The results won't be available immediately.
            // For MCP, we return a "started" status and the agent
            // must call with operation="results" to get outcomes.
            api.Execute(settings);

            s_lastResults = capture;

            // If results are already available (synchronous completion),
            // return them. Otherwise indicate tests are running.
            if (capture.IsComplete)
            {
                return FormatResults(capture, failuresOnly);
            }

            var result = new JObject();
            result["status"] = "running";
            result["message"] = "Tests started. Call unity_tests with "
                + "operation='results' to get outcomes when complete.";
            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string GetLastResults(bool failuresOnly)
        {
            if (s_lastResults == null)
            {
                var result = new JObject();
                result["status"] = "no_results";
                result["message"] = "No test results available. "
                    + "Call unity_tests with operation='run' first.";
                return result.ToString(Newtonsoft.Json.Formatting.None);
            }

            if (!s_lastResults.IsComplete)
            {
                var result = new JObject();
                result["status"] = "running";
                result["completed"] = s_lastResults.CompletedCount;
                result["message"] = "Tests still running.";
                return result.ToString(Newtonsoft.Json.Formatting.None);
            }

            return FormatResults(s_lastResults, failuresOnly);
        }

        private static string FormatResults(
            TestResultCapture capture, bool failuresOnly)
        {
            var result = new JObject();
            result["status"] = "complete";
            result["passed"] = capture.Passed;
            result["failed"] = capture.Failed;
            result["skipped"] = capture.Skipped;
            result["total"] = capture.Total;
            result["duration"] = System.Math.Round(capture.TotalDuration, 3);

            var tests = new JArray();
            foreach (var tr in capture.Results)
            {
                if (failuresOnly && tr.Status == "passed")
                    continue;

                var obj = new JObject();
                obj["name"] = tr.Name;
                obj["status"] = tr.Status;
                obj["duration"] = System.Math.Round(tr.Duration, 3);
                if (!string.IsNullOrEmpty(tr.Message))
                    obj["message"] = tr.Message;
                tests.Add(obj);
            }
            result["tests"] = tests;

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// Captures test results via ICallbacks.
        /// </summary>
        private class TestResultCapture : ICallbacks
        {
            public struct TestResult
            {
                public string Name;
                public string Status; // "passed", "failed", "skipped"
                public double Duration;
                public string Message;
            }

            public List<TestResult> Results { get; } = new();
            public bool IsComplete { get; private set; }
            public int CompletedCount => Results.Count;
            public int Passed { get; private set; }
            public int Failed { get; private set; }
            public int Skipped { get; private set; }
            public int Total => Passed + Failed + Skipped;
            public double TotalDuration { get; private set; }

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                TotalDuration = result.Duration;
                IsComplete = true;
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                // Only capture leaf tests (not suites)
                if (result.HasChildren) return;

                var status = result.TestStatus switch
                {
                    TestStatus.Passed => "passed",
                    TestStatus.Failed => "failed",
                    TestStatus.Skipped => "skipped",
                    _ => "inconclusive"
                };

                if (status == "passed") Passed++;
                else if (status == "failed") Failed++;
                else Skipped++;

                Results.Add(new TestResult
                {
                    Name = result.FullName,
                    Status = status,
                    Duration = result.Duration,
                    Message = result.Message
                });
            }
        }
    }
}
```

**Implementation Notes:**
- `TestRunnerApi.Execute()` runs tests asynchronously. The tool returns
  `"status": "running"` immediately, and the agent calls back with
  `operation="results"` to poll for completion.
- `TestResultCapture` implements `ICallbacks` to collect results as
  tests complete. `IsComplete` is set when `RunFinished` fires.
- Only leaf tests are captured (not suite/fixture containers).
- `s_lastResults` persists until the next `run` call or domain reload.
- The `filter` parameter in the schema is for future use — Unity's
  `ExecutionSettings.filters` can filter by test name but the API is
  more complex. For v1, all EditMode tests run.

**Acceptance Criteria:**
- [ ] `unity_tests` appears in `tools/list`
- [ ] `operation: "run"` starts test execution
- [ ] `operation: "results"` returns last run outcomes
- [ ] Results include passed/failed/skipped counts and per-test details
- [ ] Failed tests include error messages
- [ ] `failures_only: true` filters to only failed tests

---

### Unit 4: Tool Registration

**File:** `Packages/com.theatre.toolkit/Editor/TheatreServer.cs` (modify)

Add to `RegisterBuiltInTools`:

```csharp
private static void RegisterBuiltInTools(ToolRegistry registry)
{
    TheatreStatusTool.Register(registry);
    SceneSnapshotTool.Register(registry);
    SceneHierarchyTool.Register(registry);
    SceneInspectTool.Register(registry);
    UnityConsoleTool.Register(registry);
    UnityTestsTool.Register(registry);
}
```

---

## Implementation Order

1. **Unit 1: ConsoleLogBuffer** — no dependencies
2. **Unit 2: UnityConsoleTool** — depends on Unit 1
3. **Unit 3: UnityTestsTool** — no dependencies on Units 1-2
4. **Unit 4: TheatreServer registration** — depends on all above

---

## Verification Checklist

```bash
# After Unity recompiles:
curl -s http://localhost:9078/health
# Should show tool_count increased

# Initialize + list tools
# Should show unity_console and unity_tests

# Read console
curl -s -X POST http://localhost:9078/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "Mcp-Session-Id: <sid>" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"unity_console","arguments":{"count":10,"filter":"error"}}}'

# Run tests
curl -s -X POST http://localhost:9078/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "Mcp-Session-Id: <sid>" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"unity_tests","arguments":{"operation":"run"}}}'

# Get test results (after tests complete)
curl -s -X POST http://localhost:9078/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "Mcp-Session-Id: <sid>" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"unity_tests","arguments":{"operation":"results"}}}'
```
