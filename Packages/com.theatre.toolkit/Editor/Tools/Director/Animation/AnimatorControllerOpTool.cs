using Newtonsoft.Json.Linq;
using Theatre.Editor.Tools;
using Theatre.Stage;
using Theatre.Transport;

namespace Theatre.Editor.Tools.Director.Animation
{
    /// <summary>
    /// MCP tool: animator_controller_op
    /// Compound tool for creating and modifying AnimatorController assets in the Unity Editor.
    /// Operations: create, add_parameter, add_state, set_state_clip, add_transition,
    ///             set_transition_conditions, set_default_state, add_layer, list_states.
    /// Handlers are in <see cref="AnimatorControllerOpHandlers"/>.
    /// </summary>
    public static class AnimatorControllerOpTool
    {
        private static readonly JToken s_inputSchema;

        static AnimatorControllerOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create"", ""add_parameter"", ""add_state"", ""set_state_clip"", ""add_transition"", ""set_transition_conditions"", ""set_default_state"", ""add_layer"", ""list_states""],
                        ""description"": ""The animator controller operation to perform.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for the AnimatorController (e.g. 'Assets/Animations/MyCtrl.controller').""
                    },
                    ""name"": {
                        ""type"": ""string"",
                        ""description"": ""Name for a parameter, state, or layer.""
                    },
                    ""type"": {
                        ""type"": ""string"",
                        ""enum"": [""float"", ""int"", ""bool"", ""trigger""],
                        ""description"": ""Parameter type.""
                    },
                    ""default_value"": {
                        ""type"": ""number"",
                        ""description"": ""Default value for the parameter (numeric or 0/1 for bool).""
                    },
                    ""state_name"": {
                        ""type"": ""string"",
                        ""description"": ""Name of the target state.""
                    },
                    ""clip_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path to an AnimationClip to assign to a state.""
                    },
                    ""layer"": {
                        ""type"": ""integer"",
                        ""description"": ""Layer index (default 0).""
                    },
                    ""position"": {
                        ""type"": ""array"",
                        ""description"": ""[x, y] position for the state node in the editor graph.""
                    },
                    ""source_state"": {
                        ""type"": ""string"",
                        ""description"": ""Name of the source state for a transition.""
                    },
                    ""destination_state"": {
                        ""type"": ""string"",
                        ""description"": ""Name of the destination state for a transition.""
                    },
                    ""has_exit_time"": {
                        ""type"": ""boolean"",
                        ""description"": ""Whether the transition uses exit time (default true).""
                    },
                    ""exit_time"": {
                        ""type"": ""number"",
                        ""description"": ""Normalised exit time (default 0.75).""
                    },
                    ""transition_duration"": {
                        ""type"": ""number"",
                        ""description"": ""Transition blend duration in normalised time (default 0.25).""
                    },
                    ""conditions"": {
                        ""type"": ""array"",
                        ""description"": ""Array of condition objects: [{parameter, mode, threshold?}]. Mode: if, if_not, greater, less, equals, not_equals.""
                    },
                    ""blend_mode"": {
                        ""type"": ""string"",
                        ""enum"": [""override"", ""additive""],
                        ""description"": ""Layer blend mode (default 'override').""
                    },
                    ""weight"": {
                        ""type"": ""number"",
                        ""description"": ""Layer weight (default 1.0).""
                    },
                    ""dry_run"": {
                        ""type"": ""boolean"",
                        ""description"": ""If true, validate only — do not mutate.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "animator_controller_op",
                description: "Create and modify AnimatorController assets in the Unity Editor. "
                    + "Operations: create, add_parameter, add_state, set_state_clip, add_transition, "
                    + "set_transition_conditions, set_default_state, add_layer, list_states. "
                    + "Supports dry_run to validate without mutating.",
                inputSchema: s_inputSchema,
                group: ToolGroup.DirectorAnim,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = false
                }
            ));
        }

        private static string Execute(JToken arguments) =>
            CompoundToolDispatcher.Execute(
                "animator_controller_op",
                arguments,
                (args, operation) => operation switch
                {
                    "create"                   => AnimatorControllerOpHandlers.Create(args),
                    "add_parameter"            => AnimatorControllerOpHandlers.AddParameter(args),
                    "add_state"                => AnimatorControllerOpHandlers.AddState(args),
                    "set_state_clip"           => AnimatorControllerOpHandlers.SetStateClip(args),
                    "add_transition"           => AnimatorControllerOpHandlers.AddTransition(args),
                    "set_transition_conditions"=> AnimatorControllerOpHandlers.SetTransitionConditions(args),
                    "set_default_state"        => AnimatorControllerOpHandlers.SetDefaultState(args),
                    "add_layer"                => AnimatorControllerOpHandlers.AddLayer(args),
                    "list_states"              => AnimatorControllerOpHandlers.ListStates(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create, add_parameter, add_state, set_state_clip, add_transition, set_transition_conditions, set_default_state, add_layer, list_states")
                },
                "create, add_parameter, add_state, set_state_clip, add_transition, set_transition_conditions, set_default_state, add_layer, list_states");
    }
}
