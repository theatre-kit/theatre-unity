using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor.TestTools.TestRunner.Api;
using Theatre.Stage;
using Theatre.Transport;

namespace Theatre.Editor.Tools
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
                        ""description"": ""Only return failed/errored tests (default true)"",
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
            ResponseHelpers.AddProjectContext(result);
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
                ResponseHelpers.AddProjectContext(resultObj);
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
                ResponseHelpers.AddProjectContext(result);
                result["status"] = "no_results";
                result["message"] = "No test results available. "
                    + "Call unity_tests with operation='run' first.";
                return result.ToString(Newtonsoft.Json.Formatting.None);
            }

            if (!s_lastResults.IsComplete)
            {
                var result = new JObject();
                ResponseHelpers.AddProjectContext(result);
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
            ResponseHelpers.AddProjectContext(result);
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
