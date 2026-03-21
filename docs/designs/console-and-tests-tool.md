# Design: Console Log & Test Results MCP Tools

## Overview

Two new MCP tools that let AI agents read Unity's Console log and run/read
test results without the human copy-pasting. Essential for development
workflow — the agent can see compile errors, runtime exceptions, and test
failures directly.

**Tools:**
- `unity_console` — read Console log with grep, dedup, and smart rollups
- `unity_tests` — run tests (EditMode, PlayMode, or both) and get results

Both assigned to `ToolGroup.StageGameObject` (visible by default).

---

## Implementation Units

### Unit 1: Console Log Buffer

**File:** `Packages/com.theatre.toolkit/Editor/Tools/ConsoleLogBuffer.cs`

Captures Console log messages in a ring buffer via `Application.logMessageReceived`.
Supports grep filtering, deduplication, and smart rollups of repeated messages.

```csharp
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// Captures Unity Console log entries in a ring buffer.
    /// Subscribes via Application.logMessageReceived on [InitializeOnLoad].
    /// Supports grep filtering, deduplication, and rollup of repeated messages.
    /// </summary>
    [UnityEditor.InitializeOnLoad]
    public static class ConsoleLogBuffer
    {
        /// <summary>A single captured log entry.</summary>
        public sealed class LogEntry
        {
            public string Message;
            public string StackTrace;
            public LogType Type;
            public DateTime Timestamp;
            /// <summary>
            /// How many consecutive identical messages were rolled up into this entry.
            /// 1 means no duplicates. >1 means this entry represents N occurrences.
            /// </summary>
            public int RepeatCount;

            public LogEntry(string message, string stackTrace, LogType type)
            {
                Message = message;
                StackTrace = stackTrace;
                Type = type;
                Timestamp = DateTime.UtcNow;
                RepeatCount = 1;
            }
        }

        private static readonly List<LogEntry> s_entries = new();
        private static readonly object s_lock = new();
        private const int MaxEntries = 1000;

        static ConsoleLogBuffer()
        {
            Application.logMessageReceived += OnLogMessage;
        }

        private static void OnLogMessage(
            string message, string stackTrace, LogType type)
        {
            lock (s_lock)
            {
                // Dedup: if the last entry has the same message and type,
                // increment its repeat count instead of adding a new entry.
                if (s_entries.Count > 0)
                {
                    var last = s_entries[s_entries.Count - 1];
                    if (last.Message == message && last.Type == type)
                    {
                        last.RepeatCount++;
                        last.Timestamp = DateTime.UtcNow;
                        return;
                    }
                }

                if (s_entries.Count >= MaxEntries)
                    s_entries.RemoveAt(0);
                s_entries.Add(new LogEntry(message, stackTrace, type));
            }
        }

        /// <summary>
        /// Query log entries with filtering options.
        /// </summary>
        /// <param name="count">Max entries to return (most recent first).</param>
        /// <param name="typeFilter">
        /// Filter by log type: "error", "warning", "log", "exception", or null for all.
        /// </param>
        /// <param name="grep">
        /// Regex or substring filter on message text. Null for no filter.
        /// </param>
        /// <param name="grepIsRegex">
        /// If true, treat grep as regex. If false, case-insensitive substring match.
        /// </param>
        public static List<LogEntry> Query(
            int count = 50,
            string typeFilter = null,
            string grep = null,
            bool grepIsRegex = false)
        {
            Regex grepRegex = null;
            if (grep != null && grepIsRegex)
            {
                try { grepRegex = new Regex(grep, RegexOptions.IgnoreCase); }
                catch { /* invalid regex — fall back to substring */ }
            }

            lock (s_lock)
            {
                var result = new List<LogEntry>();
                for (int i = s_entries.Count - 1;
                     i >= 0 && result.Count < count; i--)
                {
                    var entry = s_entries[i];

                    // Type filter
                    if (typeFilter != null && !MatchesType(entry.Type, typeFilter))
                        continue;

                    // Grep filter
                    if (grep != null)
                    {
                        if (grepRegex != null)
                        {
                            if (!grepRegex.IsMatch(entry.Message))
                                continue;
                        }
                        else
                        {
                            if (entry.Message.IndexOf(grep,
                                StringComparison.OrdinalIgnoreCase) < 0)
                                continue;
                        }
                    }

                    result.Add(entry);
                }
                return result;
            }
        }

        /// <summary>
        /// Get a rollup summary: counts by log type and top repeated messages.
        /// </summary>
        public static (int logs, int warnings, int errors, int exceptions,
            List<(string message, int count, LogType type)> topRepeated)
            GetSummary(int topN = 5)
        {
            lock (s_lock)
            {
                int logs = 0, warnings = 0, errors = 0, exceptions = 0;
                var messageCounts = new Dictionary<string, (int count, LogType type)>();

                foreach (var entry in s_entries)
                {
                    switch (entry.Type)
                    {
                        case LogType.Log: logs += entry.RepeatCount; break;
                        case LogType.Warning: warnings += entry.RepeatCount; break;
                        case LogType.Error: errors += entry.RepeatCount; break;
                        case LogType.Exception: exceptions += entry.RepeatCount; break;
                        case LogType.Assert: errors += entry.RepeatCount; break;
                    }

                    // Track unique messages for top-repeated
                    var key = entry.Message.Length > 100
                        ? entry.Message.Substring(0, 100) : entry.Message;
                    if (messageCounts.TryGetValue(key, out var existing))
                        messageCounts[key] = (existing.count + entry.RepeatCount,
                            entry.Type);
                    else
                        messageCounts[key] = (entry.RepeatCount, entry.Type);
                }

                // Sort by count descending, take topN
                var sorted = new List<(string message, int count, LogType type)>();
                foreach (var kv in messageCounts)
                    sorted.Add((kv.Key, kv.Value.count, kv.Value.type));
                sorted.Sort((a, b) => b.count.CompareTo(a.count));
                if (sorted.Count > topN) sorted.RemoveRange(topN, sorted.Count - topN);

                return (logs, warnings, errors, exceptions, sorted);
            }
        }

        /// <summary>Total unique entries in buffer (not counting repeats).</summary>
        public static int Count
        {
            get { lock (s_lock) return s_entries.Count; }
        }

        /// <summary>Clear all entries.</summary>
        public static void Clear()
        {
            lock (s_lock) s_entries.Clear();
        }

        private static bool MatchesType(LogType type, string filter)
        {
            return filter switch
            {
                "error" => type == LogType.Error || type == LogType.Assert,
                "warning" => type == LogType.Warning,
                "log" => type == LogType.Log,
                "exception" => type == LogType.Exception,
                _ => true
            };
        }
    }
}
```

**Implementation Notes:**
- **Dedup:** Consecutive identical messages (same text + type) are collapsed
  into a single entry with `RepeatCount > 1`. Common in Unity — e.g.,
  per-frame warnings spam the same message hundreds of times.
- **Summary rollup:** `GetSummary()` returns type counts + top N repeated
  messages across the entire buffer, useful for "what's going on" queries.
- Ring buffer at 1000 entries (bumped from 500 to accommodate grep misses).
- `LogType.Assert` is grouped with errors since it represents assertion failures.

**Acceptance Criteria:**
- [ ] Captures all `Debug.Log*` and exception messages
- [ ] Consecutive identical messages are deduped with RepeatCount
- [ ] `Query(grep: "error")` filters by substring match
- [ ] `Query(grep: "CS\\d+", grepIsRegex: true)` matches regex
- [ ] `GetSummary()` returns type counts and top repeated messages
- [ ] Ring buffer caps at 1000 entries

---

### Unit 2: unity_console Tool

**File:** `Packages/com.theatre.toolkit/Editor/Tools/UnityConsoleTool.cs`

```csharp
using Newtonsoft.Json.Linq;
using Theatre.Transport;

namespace Theatre.Editor
{
    /// <summary>
    /// MCP tool to read Unity Console log entries with filtering,
    /// grep, and smart rollup summaries.
    /// </summary>
    public static class UnityConsoleTool
    {
        private static readonly JToken s_inputSchema;

        static UnityConsoleTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""query"", ""summary"", ""clear"", ""refresh""],
                        ""description"": ""'query' returns log entries (default). 'summary' returns type counts and top repeated messages. 'clear' empties the buffer. 'refresh' forces an AssetDatabase.Refresh() to trigger recompilation."",
                        ""default"": ""query""
                    },
                    ""count"": {
                        ""type"": ""integer"",
                        ""description"": ""Max entries to return (default 50, max 200). Only for 'query'."",
                        ""default"": 50
                    },
                    ""filter"": {
                        ""type"": ""string"",
                        ""enum"": [""error"", ""warning"", ""log"", ""exception"", ""all""],
                        ""description"": ""Filter by log type (default: all)"",
                        ""default"": ""all""
                    },
                    ""grep"": {
                        ""type"": ""string"",
                        ""description"": ""Filter messages by substring match (case-insensitive). Prefix with 'regex:' for regex matching, e.g. 'regex:CS\\d+'.""
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
                description: "Read Unity Console log entries. Supports "
                    + "type filtering (error/warning/log), grep (substring "
                    + "or regex), dedup rollup, and summary view. "
                    + "Use operation='summary' for quick overview.",
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
            var operation = arguments?["operation"]?.ToObject<string>() ?? "query";

            switch (operation)
            {
                case "summary": return ExecuteSummary();
                case "clear":   return ExecuteClear();
                case "refresh": return ExecuteRefresh();
                default:        return ExecuteQuery(arguments);
            }
        }

        private static string ExecuteQuery(JToken arguments)
        {
            int count = arguments?["count"]?.ToObject<int>() ?? 50;
            if (count > 200) count = 200;
            if (count < 1) count = 1;

            var typeFilter = arguments?["filter"]?.ToObject<string>();
            if (typeFilter == "all") typeFilter = null;

            // Parse grep — "regex:pattern" for regex, otherwise substring
            string grep = arguments?["grep"]?.ToObject<string>();
            bool isRegex = false;
            if (grep != null && grep.StartsWith("regex:"))
            {
                grep = grep.Substring(6);
                isRegex = true;
            }

            var entries = ConsoleLogBuffer.Query(count, typeFilter, grep, isRegex);

            var result = new JObject();
            result["total_in_buffer"] = ConsoleLogBuffer.Count;
            result["returned"] = entries.Count;
            if (typeFilter != null) result["filter"] = typeFilter;
            if (grep != null) result["grep"] = grep;

            var arr = new JArray();
            foreach (var entry in entries)
            {
                var obj = new JObject();
                obj["type"] = entry.Type.ToString().ToLowerInvariant();
                obj["message"] = entry.Message;
                if (entry.RepeatCount > 1)
                    obj["repeat_count"] = entry.RepeatCount;
                if (!string.IsNullOrEmpty(entry.StackTrace))
                    obj["stack_trace"] = entry.StackTrace.TrimEnd();
                obj["timestamp"] = entry.Timestamp.ToString("HH:mm:ss.fff");
                arr.Add(obj);
            }
            result["entries"] = arr;

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string ExecuteSummary()
        {
            var (logs, warnings, errors, exceptions, topRepeated) =
                ConsoleLogBuffer.GetSummary(10);

            var result = new JObject();
            result["total_entries"] = ConsoleLogBuffer.Count;
            result["counts"] = new JObject
            {
                ["log"] = logs,
                ["warning"] = warnings,
                ["error"] = errors,
                ["exception"] = exceptions
            };

            var top = new JArray();
            foreach (var (message, count, type) in topRepeated)
            {
                top.Add(new JObject
                {
                    ["message"] = message,
                    ["count"] = count,
                    ["type"] = type.ToString().ToLowerInvariant()
                });
            }
            result["top_repeated"] = top;

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string ExecuteClear()
        {
            int countBefore = ConsoleLogBuffer.Count;
            ConsoleLogBuffer.Clear();

            var result = new JObject();
            result["result"] = "ok";
            result["cleared"] = countBefore;
            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string ExecuteRefresh()
        {
            UnityEditor.AssetDatabase.Refresh();

            var result = new JObject();
            result["result"] = "ok";
            result["message"] = "AssetDatabase.Refresh() triggered. "
                + "Scripts will recompile if changed. "
                + "The server will restart after domain reload.";
            return result.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
```

**Implementation Notes:**
- **Grep syntax:** Plain string = case-insensitive substring match. Prefix
  with `regex:` for regex (e.g., `regex:CS\d{4}` for compile error codes).
- **Summary operation:** Returns counts by type + top 10 most repeated
  messages. Useful for "what's going on" without reading all entries.
- `repeat_count` only appears in output when >1 (deduped entries).

**Acceptance Criteria:**
- [ ] `unity_console` appears in `tools/list`
- [ ] `operation: "query"` returns recent entries with type, message, timestamp
- [ ] `filter: "error"` returns only errors and asserts
- [ ] `grep: "CS0"` returns only messages containing "CS0"
- [ ] `grep: "regex:error.*line \\d+"` matches regex pattern
- [ ] Deduped entries show `repeat_count`
- [ ] `operation: "summary"` returns type counts and top repeated messages
- [ ] `operation: "clear"` empties the buffer and reports count cleared

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
    /// MCP tool to run Unity tests (EditMode, PlayMode, or both)
    /// and retrieve results.
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
                        ""enum"": [""run"", ""results"", ""list""],
                        ""description"": ""'run' executes tests and returns results. 'results' returns last run results. 'list' shows available tests without running."",
                        ""default"": ""results""
                    },
                    ""mode"": {
                        ""type"": ""string"",
                        ""enum"": [""editmode"", ""playmode"", ""both""],
                        ""description"": ""Which test mode to run (default: editmode)"",
                        ""default"": ""editmode""
                    },
                    ""filter"": {
                        ""type"": ""string"",
                        ""description"": ""Test name filter (substring match). Only tests containing this string will run or be listed.""
                    },
                    ""failures_only"": {
                        ""type"": ""boolean"",
                        ""description"": ""Only return failed/errored tests (default false)"",
                        ""default"": true
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
                description: "Run Unity tests and get results. Supports "
                    + "EditMode, PlayMode, or both. Use operation='run' "
                    + "to execute, 'results' to poll, 'list' to discover tests.",
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
            var mode = arguments?["mode"]?.ToObject<string>() ?? "editmode";
            var filter = arguments?["filter"]?.ToObject<string>();
            bool failuresOnly = arguments?["failures_only"]?.ToObject<bool>() ?? true;

            switch (operation)
            {
                case "run":     return RunTests(mode, filter, failuresOnly);
                case "list":    return ListTests(mode, filter);
                default:        return GetLastResults(failuresOnly);
            }
        }

        private static string RunTests(
            string mode, string filter, bool failuresOnly)
        {
            var capture = new TestResultCapture();
            var api = UnityEngine.ScriptableObject
                .CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(capture);

            var testMode = mode switch
            {
                "playmode" => TestMode.PlayMode,
                "both" => TestMode.EditMode | TestMode.PlayMode,
                _ => TestMode.EditMode
            };

            var filterObj = new Filter { testMode = testMode };
            if (!string.IsNullOrEmpty(filter))
                filterObj.testNames = new[] { filter };

            api.Execute(new ExecutionSettings
            {
                filters = new[] { filterObj }
            });

            s_lastResults = capture;

            // Check if tests completed synchronously
            if (capture.IsComplete)
                return FormatResults(capture, failuresOnly, mode);

            var result = new JObject();
            result["status"] = "running";
            result["mode"] = mode;
            result["message"] = "Tests started. Call unity_tests with "
                + "operation='results' to get outcomes when complete.";
            if (!string.IsNullOrEmpty(filter))
                result["filter"] = filter;
            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string ListTests(string mode, string filter)
        {
            var api = UnityEngine.ScriptableObject
                .CreateInstance<TestRunnerApi>();

            var testMode = mode switch
            {
                "playmode" => TestMode.PlayMode,
                "both" => TestMode.EditMode | TestMode.PlayMode,
                _ => TestMode.EditMode
            };

            JObject resultObj = null;

            api.RetrieveTestList(testMode, testRoot =>
            {
                var tests = new JArray();
                CollectLeafTests(testRoot, tests, filter);

                resultObj = new JObject();
                resultObj["mode"] = mode;
                resultObj["total"] = tests.Count;
                if (!string.IsNullOrEmpty(filter))
                    resultObj["filter"] = filter;
                resultObj["tests"] = tests;
            });

            // RetrieveTestList calls back synchronously on main thread
            if (resultObj != null)
                return resultObj.ToString(Newtonsoft.Json.Formatting.None);

            var fallback = new JObject();
            fallback["status"] = "error";
            fallback["message"] = "Failed to retrieve test list";
            return fallback.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static void CollectLeafTests(
            ITestAdaptor test, JArray tests, string filter)
        {
            if (!test.HasChildren)
            {
                if (filter == null ||
                    test.FullName.IndexOf(filter,
                        System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tests.Add(new JObject
                    {
                        ["name"] = test.FullName,
                        ["type"] = test.RunState.ToString().ToLowerInvariant()
                    });
                }
                return;
            }

            foreach (var child in test.Children)
                CollectLeafTests(child, tests, filter);
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

            return FormatResults(s_lastResults, failuresOnly, null);
        }

        private static string FormatResults(
            TestResultCapture capture, bool failuresOnly, string mode)
        {
            var result = new JObject();
            result["status"] = "complete";
            if (mode != null) result["mode"] = mode;
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
                if (!string.IsNullOrEmpty(tr.StackTrace))
                    obj["stack_trace"] = tr.StackTrace.TrimEnd();
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
                public string Status;
                public double Duration;
                public string Message;
                public string StackTrace;
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
                    Message = result.Message,
                    StackTrace = result.StackTrace
                });
            }
        }
    }
}
```

**Implementation Notes:**
- **Test modes:** `editmode` (default), `playmode`, or `both`. PlayMode
  tests require entering Play Mode — Unity handles this automatically
  via `TestRunnerApi.Execute()`.
- **List operation:** `RetrieveTestList` returns the test tree synchronously
  on the main thread. We walk it to find leaf tests, optionally filtered
  by name substring.
- **Filter for run:** `Filter.testNames` accepts partial matches. The agent
  can filter to a specific test fixture or test name.
- **Stack traces:** Included in results for failed tests to help debugging.
- PlayMode test execution transitions Unity into Play Mode and back
  automatically — the agent should be aware of this.

**Acceptance Criteria:**
- [ ] `unity_tests` appears in `tools/list`
- [ ] `operation: "run", mode: "editmode"` runs EditMode tests
- [ ] `operation: "run", mode: "playmode"` runs PlayMode tests
- [ ] `operation: "run", mode: "both"` runs both
- [ ] `operation: "list"` returns available tests without running
- [ ] `operation: "list", filter: "JsonRpc"` returns only matching tests
- [ ] `operation: "results"` returns last run outcomes
- [ ] Failed tests include message and stack_trace
- [ ] `failures_only: true` filters to only non-passing tests
- [ ] `filter: "McpIntegration"` limits test execution to matching tests

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
# After Unity recompiles, initialize a session then:

# Console summary
curl ... '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"unity_console","arguments":{"operation":"summary"}}}'

# Console grep for compile errors
curl ... '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"unity_console","arguments":{"grep":"error CS","filter":"error"}}}'

# List available tests
curl ... '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"unity_tests","arguments":{"operation":"list","mode":"editmode"}}}'

# Run tests
curl ... '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"unity_tests","arguments":{"operation":"run","mode":"editmode"}}}'

# Get results (after completion)
curl ... '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"unity_tests","arguments":{"operation":"results","failures_only":true}}}'
```
