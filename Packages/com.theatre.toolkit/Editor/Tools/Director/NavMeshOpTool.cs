using Newtonsoft.Json.Linq;
using Theatre.Editor.Tools;
using Theatre.Stage;
using Theatre.Transport;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: navmesh_op
    /// Compound tool for NavMesh configuration and baking in the Unity Editor.
    /// Operations: bake, set_area, add_modifier, add_link, set_agent_type, add_surface.
    /// Handlers are in <see cref="NavMeshOpHandlers"/>.
    /// </summary>
    public static class NavMeshOpTool
    {
        private static readonly JToken s_inputSchema;

        static NavMeshOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""bake"", ""set_area"", ""add_modifier"", ""add_link"", ""set_agent_type"", ""add_surface""],
                        ""description"": ""The NavMesh operation to perform.""
                    },
                    ""agent_type_id"": {
                        ""type"": ""integer"",
                        ""description"": ""Agent type ID (default 0 — Humanoid).""
                    },
                    ""index"": {
                        ""type"": ""integer"",
                        ""description"": ""NavMesh area index (0-31) for set_area.""
                    },
                    ""name"": {
                        ""type"": ""string"",
                        ""description"": ""Area name for set_area.""
                    },
                    ""cost"": {
                        ""type"": ""number"",
                        ""description"": ""Area traversal cost for set_area.""
                    },
                    ""path"": {
                        ""type"": ""string"",
                        ""description"": ""Hierarchy path to a GameObject for add_modifier or add_surface.""
                    },
                    ""area"": {
                        ""type"": ""integer"",
                        ""description"": ""NavMesh area index for add_modifier or add_link.""
                    },
                    ""ignore_from_build"": {
                        ""type"": ""boolean"",
                        ""description"": ""If true, the modifier excludes the object from NavMesh builds.""
                    },
                    ""affect_children"": {
                        ""type"": ""boolean"",
                        ""description"": ""If true, the modifier affects child objects too.""
                    },
                    ""start"": {
                        ""type"": ""array"",
                        ""description"": ""Start world position [x, y, z] for add_link.""
                    },
                    ""end"": {
                        ""type"": ""array"",
                        ""description"": ""End world position [x, y, z] for add_link.""
                    },
                    ""bidirectional"": {
                        ""type"": ""boolean"",
                        ""description"": ""Whether the link is bidirectional (default true).""
                    },
                    ""width"": {
                        ""type"": ""number"",
                        ""description"": ""Link width for add_link.""
                    },
                    ""parent_path"": {
                        ""type"": ""string"",
                        ""description"": ""Optional hierarchy path to parent the link GameObject under.""
                    },
                    ""radius"": {
                        ""type"": ""number"",
                        ""description"": ""Agent radius for set_agent_type.""
                    },
                    ""height"": {
                        ""type"": ""number"",
                        ""description"": ""Agent height for set_agent_type.""
                    },
                    ""step_height"": {
                        ""type"": ""number"",
                        ""description"": ""Agent step height for set_agent_type.""
                    },
                    ""max_slope"": {
                        ""type"": ""number"",
                        ""description"": ""Maximum walkable slope angle for set_agent_type.""
                    },
                    ""collect_objects"": {
                        ""type"": ""string"",
                        ""description"": ""How to collect objects for add_surface: 'all', 'volume', 'children'.""
                    },
                    ""use_geometry"": {
                        ""type"": ""string"",
                        ""description"": ""Geometry source for add_surface: 'render_meshes', 'physics_colliders'.""
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
                name: "navmesh_op",
                description: "NavMesh configuration and baking. "
                    + "Operations: bake, set_area, add_modifier, add_link, set_agent_type, add_surface. "
                    + "All mutations are undoable. NavMeshSurface and NavMeshModifier require 'com.unity.ai.navigation'.",
                inputSchema: s_inputSchema,
                group: ToolGroup.DirectorSpatial,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = false
                }
            ));
        }

        private static string Execute(JToken arguments) =>
            CompoundToolDispatcher.Execute(
                "navmesh_op",
                arguments,
                (args, operation) => operation switch
                {
                    "bake"           => NavMeshOpHandlers.Bake(args),
                    "set_area"       => NavMeshOpHandlers.SetArea(args),
                    "add_modifier"   => NavMeshOpHandlers.AddModifier(args),
                    "add_link"       => NavMeshOpHandlers.AddLink(args),
                    "set_agent_type" => NavMeshOpHandlers.SetAgentType(args),
                    "add_surface"    => NavMeshOpHandlers.AddSurface(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: bake, set_area, add_modifier, add_link, set_agent_type, add_surface")
                },
                "bake, set_area, add_modifier, add_link, set_agent_type, add_surface");
    }
}
