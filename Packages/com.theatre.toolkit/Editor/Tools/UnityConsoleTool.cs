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
