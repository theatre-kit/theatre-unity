using Newtonsoft.Json.Linq;
using Theatre.Editor.Tools;
using Theatre.Stage;
using Theatre.Transport;

namespace Theatre.Editor.Tools.Director.Spatial
{
    /// <summary>
    /// MCP tool: terrain_op
    /// Compound tool for terrain creation and sculpting in the Unity Editor.
    /// Operations: create, set_heightmap, smooth_heightmap, paint_texture,
    /// add_terrain_layer, place_trees, place_details, set_size, get_height.
    /// Handlers are in <see cref="TerrainOpHandlers"/>.
    /// </summary>
    public static class TerrainOpTool
    {
        private static readonly JToken s_inputSchema;

        static TerrainOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create"", ""set_heightmap"", ""smooth_heightmap"", ""paint_texture"",
                                   ""add_terrain_layer"", ""place_trees"", ""place_details"", ""set_size"", ""get_height""],
                        ""description"": ""The terrain operation to perform.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for the TerrainData .asset file (create).""
                    },
                    ""terrain_path"": {
                        ""type"": ""string"",
                        ""description"": ""Hierarchy path to the Terrain GameObject.""
                    },
                    ""position"": {
                        ""type"": ""array"",
                        ""description"": ""World position [x, y, z] or [x, z] depending on operation.""
                    },
                    ""positions"": {
                        ""type"": ""array"",
                        ""description"": ""Array of [x, z] world positions for paint_texture, place_trees, place_details.""
                    },
                    ""width"": {
                        ""type"": ""number"",
                        ""description"": ""Terrain width (X axis).""
                    },
                    ""height"": {
                        ""type"": ""number"",
                        ""description"": ""Terrain height (Y axis, vertical scale).""
                    },
                    ""length"": {
                        ""type"": ""number"",
                        ""description"": ""Terrain length (Z axis).""
                    },
                    ""heightmap_resolution"": {
                        ""type"": ""integer"",
                        ""description"": ""Heightmap resolution (must be 2^n+1, e.g. 33, 65, 129, 257, 513). Default 513.""
                    },
                    ""heights"": {
                        ""type"": ""array"",
                        ""description"": ""2D array of floats (0.0-1.0) for set_heightmap. Rows × columns.""
                    },
                    ""region"": {
                        ""type"": ""object"",
                        ""description"": ""Optional region offset for set_heightmap ({x, y}) or smooth_heightmap ({x, y, width, height}).""
                    },
                    ""iterations"": {
                        ""type"": ""integer"",
                        ""description"": ""Number of smoothing iterations for smooth_heightmap. Default 1.""
                    },
                    ""layer_index"": {
                        ""type"": ""integer"",
                        ""description"": ""Terrain layer index for paint_texture or place_details.""
                    },
                    ""opacity"": {
                        ""type"": ""number"",
                        ""description"": ""Paint opacity (0-1) for paint_texture. Default 1.""
                    },
                    ""brush_size"": {
                        ""type"": ""integer"",
                        ""description"": ""Brush size in alphamap cells for paint_texture. Default 5.""
                    },
                    ""diffuse_texture"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path to Texture2D for add_terrain_layer.""
                    },
                    ""normal_texture"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path to normal map Texture2D for add_terrain_layer.""
                    },
                    ""tile_size"": {
                        ""type"": ""array"",
                        ""description"": ""Tile size [x, y] for add_terrain_layer. Default [15, 15].""
                    },
                    ""tile_offset"": {
                        ""type"": ""array"",
                        ""description"": ""Tile offset [x, y] for add_terrain_layer. Default [0, 0].""
                    },
                    ""prefab"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path to tree prefab for place_trees.""
                    },
                    ""height_scale"": {
                        ""type"": ""number"",
                        ""description"": ""Tree height scale for place_trees. Default 1.""
                    },
                    ""width_scale"": {
                        ""type"": ""number"",
                        ""description"": ""Tree width scale for place_trees. Default 1.""
                    },
                    ""rotation"": {
                        ""type"": ""number"",
                        ""description"": ""Tree rotation in degrees for place_trees. Default random.""
                    },
                    ""density"": {
                        ""type"": ""integer"",
                        ""description"": ""Detail density per cell for place_details. Default 1.""
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
                name: "terrain_op",
                description: "Terrain creation and sculpting. "
                    + "Operations: create, set_heightmap, smooth_heightmap, paint_texture, "
                    + "add_terrain_layer, place_trees, place_details, set_size, get_height. "
                    + "All mutations are undoable.",
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
                "terrain_op",
                arguments,
                (args, operation) => operation switch
                {
                    "create"            => TerrainOpHandlers.Create(args),
                    "set_heightmap"     => TerrainOpHandlers.SetHeightmap(args),
                    "smooth_heightmap"  => TerrainOpHandlers.SmoothHeightmap(args),
                    "paint_texture"     => TerrainOpHandlers.PaintTexture(args),
                    "add_terrain_layer" => TerrainOpHandlers.AddTerrainLayer(args),
                    "place_trees"       => TerrainOpHandlers.PlaceTrees(args),
                    "place_details"     => TerrainOpHandlers.PlaceDetails(args),
                    "set_size"          => TerrainOpHandlers.SetSize(args),
                    "get_height"        => TerrainOpHandlers.GetHeight(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create, set_heightmap, smooth_heightmap, paint_texture, add_terrain_layer, place_trees, place_details, set_size, get_height")
                },
                "create, set_heightmap, smooth_heightmap, paint_texture, add_terrain_layer, place_trees, place_details, set_size, get_height");
    }
}
