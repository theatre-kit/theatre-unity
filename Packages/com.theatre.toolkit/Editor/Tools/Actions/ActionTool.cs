using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// MCP tool: action
    /// Compound tool for game state manipulation.
    /// Operations: teleport, set_property, set_active, set_timescale,
    ///             pause, step, unpause, invoke_method.
    /// </summary>
    public static class ActionTool
    {
        private static readonly JToken s_inputSchema;

        static ActionTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""teleport"", ""set_property"", ""set_active"",
                                   ""set_timescale"", ""pause"", ""step"",
                                   ""unpause"", ""invoke_method""],
                        ""description"": ""The action to perform.""
                    },
                    ""path"": {
                        ""type"": ""string"",
                        ""description"": ""Target GameObject path. Required for teleport, set_property, set_active, invoke_method.""
                    },
                    ""instance_id"": {
                        ""type"": ""integer"",
                        ""description"": ""Target GameObject instance_id (alternative to path).""
                    },
                    ""position"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""World position [x,y,z]. Used by teleport.""
                    },
                    ""rotation_euler"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Euler angles [x,y,z] in degrees. Optional for teleport.""
                    },
                    ""component"": {
                        ""type"": ""string"",
                        ""description"": ""Component type name. Used by set_property, invoke_method.""
                    },
                    ""property"": {
                        ""type"": ""string"",
                        ""description"": ""Property name. Used by set_property.""
                    },
                    ""value"": {
                        ""description"": ""Value to set. Used by set_property, set_active, set_timescale.""
                    },
                    ""active"": {
                        ""type"": ""boolean"",
                        ""description"": ""Enable/disable state. Used by set_active.""
                    },
                    ""timescale"": {
                        ""type"": ""number"",
                        ""description"": ""Time.timeScale value (0.0-100.0). Used by set_timescale.""
                    },
                    ""method"": {
                        ""type"": ""string"",
                        ""description"": ""Method name to call. Used by invoke_method.""
                    },
                    ""arguments"": {
                        ""type"": ""array"",
                        ""description"": ""Method arguments [arg1, arg2, ...]. Used by invoke_method. Max 3 args, simple types only.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "action",
                description: "Manipulate game state for debugging. Teleport "
                    + "objects, set component properties, enable/disable "
                    + "GameObjects, control time and play mode, call methods. "
                    + "pause/step/unpause/set_timescale require Play Mode. "
                    + "teleport/set_property/set_active work in both modes.",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageAction,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = false
                }
            ));
        }

        private static string Execute(JToken arguments)
        {
            if (arguments == null || arguments.Type != JTokenType.Object)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Arguments must be a JSON object with an 'operation' field",
                    "Provide {\"operation\": \"teleport\", \"path\": \"/Player\", ...}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: teleport, set_property, set_active, "
                    + "set_timescale, pause, step, unpause, invoke_method");
            }

            try
            {
                return operation switch
                {
                    "teleport" => ActionTeleport.Execute(args),
                    "set_property" => ActionSetProperty.Execute(args),
                    "set_active" => ActionSetActive.Execute(args),
                    "set_timescale" => ActionSetTimescale.Execute(args),
                    "pause" => ActionPlayControl.ExecutePause(args),
                    "step" => ActionPlayControl.ExecuteStep(args),
                    "unpause" => ActionPlayControl.ExecuteUnpause(args),
                    "invoke_method" => ActionInvokeMethod.Execute(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: teleport, set_property, set_active, "
                        + "set_timescale, pause, step, unpause, invoke_method")
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] action:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"action:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }
    }
}
