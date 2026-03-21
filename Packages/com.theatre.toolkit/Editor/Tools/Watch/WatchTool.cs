using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;

namespace Theatre.Editor.Tools.Watch
{
    /// <summary>
    /// MCP tool: watch
    /// Compound tool for change subscriptions.
    /// Operations: create, remove, list, check.
    /// </summary>
    public static class WatchTool
    {
        private static readonly JToken s_inputSchema;
        private static WatchEngine s_engine;

        private const int MaxWatches = 32;

        static WatchTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create"", ""remove"", ""list"", ""check""],
                        ""description"": ""The watch operation to perform.""
                    },
                    ""target"": {
                        ""type"": ""string"",
                        ""description"": ""Hierarchy path of the object to watch, or '*' for global watches. Required for create.""
                    },
                    ""track"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Properties to track: ['position', 'current_hp']. Required for create.""
                    },
                    ""condition"": {
                        ""type"": ""object"",
                        ""description"": ""Trigger condition. Types: threshold, proximity, entered_region, property_changed, destroyed, spawned."",
                        ""properties"": {
                            ""type"": {
                                ""type"": ""string"",
                                ""enum"": [""threshold"", ""proximity"", ""entered_region"", ""property_changed"", ""destroyed"", ""spawned""]
                            },
                            ""property"": { ""type"": ""string"" },
                            ""below"": { ""type"": ""number"" },
                            ""above"": { ""type"": ""number"" },
                            ""target"": { ""type"": ""string"" },
                            ""within"": { ""type"": ""number"" },
                            ""beyond"": { ""type"": ""number"" },
                            ""min"": { ""type"": ""array"", ""items"": { ""type"": ""number"" } },
                            ""max"": { ""type"": ""array"", ""items"": { ""type"": ""number"" } },
                            ""name_pattern"": { ""type"": ""string"" }
                        }
                    },
                    ""throttle_ms"": {
                        ""type"": ""integer"",
                        ""default"": 500,
                        ""description"": ""Min interval between notifications in milliseconds.""
                    },
                    ""label"": {
                        ""type"": ""string"",
                        ""description"": ""Human-readable label for this watch.""
                    },
                    ""watch_id"": {
                        ""type"": ""string"",
                        ""description"": ""Watch ID. Required for remove and check.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "watch",
                description: "Subscribe to changes or conditions on GameObjects. "
                    + "Use 'create' to set up a watch with a condition "
                    + "(threshold, proximity, entered_region, property_changed, "
                    + "destroyed, spawned). Watches fire notifications via SSE "
                    + "when conditions are met. Use 'check' to poll manually.",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageWatch,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = false
                }
            ));
        }

        /// <summary>
        /// Get or initialize the watch engine.
        /// </summary>
        internal static WatchEngine GetEngine()
        {
            if (s_engine == null)
            {
                s_engine = new WatchEngine();
                s_engine.Initialize(OnWatchTriggered);

                // Hook into editor update for polling
                UnityEditor.EditorApplication.update += s_engine.Tick;
            }
            return s_engine;
        }

        /// <summary>
        /// Teardown — remove update hook. Called on server shutdown.
        /// </summary>
        internal static void Shutdown()
        {
            if (s_engine != null)
            {
                UnityEditor.EditorApplication.update -= s_engine.Tick;
                s_engine = null;
            }
        }

        private static void OnWatchTriggered(JObject notifParams)
        {
            var notification = JsonRpcResponse.Notification(
                "notifications/theatre/watch_triggered", notifParams);
            TheatreServer.SseManager?.PushNotification(notification);
        }

        private static string Execute(JToken arguments)
        {
            if (arguments == null || arguments.Type != JTokenType.Object)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Arguments must be a JSON object with an 'operation' field",
                    "Provide {\"operation\": \"create\", \"target\": \"/Player\", ...}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: create, remove, list, check");
            }

            try
            {
                return operation switch
                {
                    "create" => ExecuteCreate(args),
                    "remove" => ExecuteRemove(args),
                    "list" => ExecuteList(args),
                    "check" => ExecuteCheck(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create, remove, list, check")
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] watch:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"watch:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }

        private static string ExecuteCreate(JObject args)
        {
            var target = args["target"]?.Value<string>();
            if (string.IsNullOrEmpty(target))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'target' parameter",
                    "Provide a hierarchy path like '/Player' or '*' for global watches");
            }

            var condition = args["condition"]?.ToObject<WatchCondition>();
            if (condition == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'condition' parameter",
                    "Provide a condition object with 'type' field: threshold, "
                    + "proximity, entered_region, property_changed, destroyed, spawned");
            }

            var track = args["track"]?.ToObject<string[]>();
            var throttleMs = args["throttle_ms"]?.Value<int>() ?? 500;
            var label = args["label"]?.Value<string>();

            var def = new WatchDefinition
            {
                Target = target,
                Track = track,
                Condition = condition,
                ThrottleMs = throttleMs,
                Label = label
            };

            var engine = GetEngine();
            var watchId = engine.Create(def);

            if (watchId == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "watch_limit_reached",
                    $"Maximum {MaxWatches} concurrent watches reached",
                    "Remove unused watches with watch:remove before creating new ones");
            }

            var response = new JObject();
            response["result"] = "ok";
            response["watch_id"] = watchId;
            response["target"] = target;
            if (track != null)
                response["track"] = new JArray(track);
            response["condition"] = JObject.FromObject(condition);
            response["throttle_ms"] = throttleMs;
            if (label != null)
                response["label"] = label;
            response["active_watches"] = engine.Count;
            ResponseHelpers.AddFrameContext(response);

            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string ExecuteRemove(JObject args)
        {
            var watchId = args["watch_id"]?.Value<string>();
            if (string.IsNullOrEmpty(watchId))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'watch_id' parameter",
                    "Provide the watch_id returned from watch:create");
            }

            var engine = GetEngine();
            if (!engine.Remove(watchId))
            {
                return ResponseHelpers.ErrorResponse(
                    "gameobject_not_found",
                    $"No watch found with watch_id '{watchId}'",
                    "Use watch:list to see active watches");
            }

            var response = new JObject();
            response["result"] = "ok";
            response["watch_id"] = watchId;
            response["active_watches"] = engine.Count;
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string ExecuteList(JObject args)
        {
            var engine = GetEngine();
            var response = new JObject();
            response["results"] = engine.ListAll();
            response["active_watches"] = engine.Count;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string ExecuteCheck(JObject args)
        {
            var watchId = args["watch_id"]?.Value<string>();
            if (string.IsNullOrEmpty(watchId))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'watch_id' parameter",
                    "Provide the watch_id returned from watch:create");
            }

            var engine = GetEngine();
            var result = engine.Check(watchId);
            if (result == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "gameobject_not_found",
                    $"No watch found with watch_id '{watchId}'",
                    "Use watch:list to see active watches");
            }

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
