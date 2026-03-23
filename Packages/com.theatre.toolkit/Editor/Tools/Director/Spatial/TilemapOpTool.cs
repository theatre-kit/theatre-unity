using Newtonsoft.Json.Linq;
using Theatre.Editor.Tools;
using Theatre.Stage;
using Theatre.Transport;

namespace Theatre.Editor.Tools.Director.Spatial
{
    /// <summary>
    /// MCP tool: tilemap_op
    /// Compound tool for 2D tilemap painting and management in the Unity Editor.
    /// Operations: set_tile, set_tiles, box_fill, flood_fill, clear, get_tile,
    /// get_used_tiles, create_rule_tile, set_tilemap_layer.
    /// Handlers are in <see cref="TilemapOpHandlers"/>.
    /// </summary>
    public static class TilemapOpTool
    {
        private static readonly JToken s_inputSchema;

        static TilemapOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""set_tile"", ""set_tiles"", ""box_fill"", ""flood_fill"", ""clear"",
                                   ""get_tile"", ""get_used_tiles"", ""create_rule_tile"", ""set_tilemap_layer""],
                        ""description"": ""The tilemap operation to perform.""
                    },
                    ""tilemap_path"": {
                        ""type"": ""string"",
                        ""description"": ""Hierarchy path to the GameObject with a Tilemap component (e.g. '/Grid/Tilemap').""
                    },
                    ""position"": {
                        ""type"": ""array"",
                        ""description"": ""Cell position as [x, y] or [x, y, z].""
                    },
                    ""tile_asset"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path to a TileBase asset (e.g. 'Assets/Tiles/GrassTile.asset').""
                    },
                    ""positions"": {
                        ""type"": ""array"",
                        ""description"": ""Array of cell positions for set_tiles, each as [x, y, z].""
                    },
                    ""start"": {
                        ""type"": ""array"",
                        ""description"": ""Start cell position [x, y] or [x, y, z] for box_fill or clear region.""
                    },
                    ""end"": {
                        ""type"": ""array"",
                        ""description"": ""End cell position [x, y] or [x, y, z] for box_fill or clear region.""
                    },
                    ""region"": {
                        ""type"": ""object"",
                        ""description"": ""Optional region for clear: { 'start': [x,y,z], 'end': [x,y,z] }."",
                        ""properties"": {
                            ""start"": { ""type"": ""array"" },
                            ""end"": { ""type"": ""array"" }
                        }
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for create_rule_tile (must end in .asset).""
                    },
                    ""default_sprite"": {
                        ""type"": ""string"",
                        ""description"": ""Optional asset path to a Sprite for the rule tile default sprite.""
                    },
                    ""sorting_layer"": {
                        ""type"": ""string"",
                        ""description"": ""Sorting layer name for set_tilemap_layer.""
                    },
                    ""sorting_order"": {
                        ""type"": ""integer"",
                        ""description"": ""Sorting order for set_tilemap_layer.""
                    },
                    ""material"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path to a Material for set_tilemap_layer.""
                    },
                    ""budget"": {
                        ""type"": ""integer"",
                        ""description"": ""Token budget for get_used_tiles response (default 1500).""
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
                name: "tilemap_op",
                description: "2D tilemap painting and management. "
                    + "Operations: set_tile, set_tiles, box_fill, flood_fill, clear, "
                    + "get_tile, get_used_tiles, create_rule_tile, set_tilemap_layer. "
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
                "tilemap_op",
                arguments,
                (args, operation) => operation switch
                {
                    "set_tile"          => TilemapOpHandlers.SetTile(args),
                    "set_tiles"         => TilemapOpHandlers.SetTiles(args),
                    "box_fill"          => TilemapOpHandlers.BoxFill(args),
                    "flood_fill"        => TilemapOpHandlers.FloodFill(args),
                    "clear"             => TilemapOpHandlers.Clear(args),
                    "get_tile"          => TilemapOpHandlers.GetTile(args),
                    "get_used_tiles"    => TilemapOpHandlers.GetUsedTiles(args),
                    "create_rule_tile"  => TilemapOpHandlers.CreateRuleTile(args),
                    "set_tilemap_layer" => TilemapOpHandlers.SetTilemapLayer(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: set_tile, set_tiles, box_fill, flood_fill, clear, get_tile, get_used_tiles, create_rule_tile, set_tilemap_layer")
                },
                "set_tile, set_tiles, box_fill, flood_fill, clear, get_tile, get_used_tiles, create_rule_tile, set_tilemap_layer");
    }
}
