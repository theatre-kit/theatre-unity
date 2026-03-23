using Newtonsoft.Json.Linq;
using Theatre.Editor.Tools;
using Theatre.Stage;
using Theatre.Transport;

namespace Theatre.Editor.Tools.Actions
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
                                   ""unpause"", ""invoke_method"", ""run_menu_item""],
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
                        ""description"": ""Value to set. Used by set_property, set_active, set_timescale. For ObjectReference properties, pass an asset path string (e.g. 'Assets/Materials/Foo.mat').""
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
                    },
                    ""type"": {
                        ""type"": ""string"",
                        ""description"": ""Type name for static method invocation. Used by invoke_method in Edit Mode. Alternative to component+path.""
                    },
                    ""menu_path"": {
                        ""type"": ""string"",
                        ""description"": ""Menu item path to execute. Used by run_menu_item. E.g. 'GameObject/3D Object/Cube'.""
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
                    + "GameObjects, control time and play mode, call methods, "
                    + "run Editor menu items. "
                    + "pause/step/unpause/set_timescale require Play Mode. "
                    + "invoke_method with 'type' for static methods works in Edit Mode. "
                    + "teleport/set_property/set_active/run_menu_item work in both modes.",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageAction,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = false
                }
            ));
        }

        private static string Execute(JToken arguments) =>
            CompoundToolDispatcher.Execute(
                "action",
                arguments,
                (args, operation) => operation switch
                {
                    "teleport"       => ActionTeleport.Execute(args),
                    "set_property"   => ActionSetProperty.Execute(args),
                    "set_active"     => ActionSetActive.Execute(args),
                    "set_timescale"  => ActionSetTimescale.Execute(args),
                    "pause"          => ActionPlayControl.ExecutePause(args),
                    "step"           => ActionPlayControl.ExecuteStep(args),
                    "unpause"        => ActionPlayControl.ExecuteUnpause(args),
                    "invoke_method"  => ActionInvokeMethod.Execute(args),
                    "run_menu_item"  => ActionRunMenuItem.Execute(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: teleport, set_property, set_active, "
                        + "set_timescale, pause, step, unpause, invoke_method, run_menu_item")
                },
                "teleport, set_property, set_active, set_timescale, pause, step, unpause, invoke_method, run_menu_item");
    }
}
