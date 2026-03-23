using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEditor;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: tilemap_op
    /// Compound tool for 2D tilemap painting and management in the Unity Editor.
    /// Operations: set_tile, set_tiles, box_fill, flood_fill, clear, get_tile,
    /// get_used_tiles, create_rule_tile, set_tilemap_layer.
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
                    "set_tile"          => SetTile(args),
                    "set_tiles"         => SetTiles(args),
                    "box_fill"          => BoxFill(args),
                    "flood_fill"        => FloodFill(args),
                    "clear"             => Clear(args),
                    "get_tile"          => GetTile(args),
                    "get_used_tiles"    => GetUsedTiles(args),
                    "create_rule_tile"  => CreateRuleTile(args),
                    "set_tilemap_layer" => SetTilemapLayer(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: set_tile, set_tiles, box_fill, flood_fill, clear, get_tile, get_used_tiles, create_rule_tile, set_tilemap_layer")
                },
                "set_tile, set_tiles, box_fill, flood_fill, clear, get_tile, get_used_tiles, create_rule_tile, set_tilemap_layer");

        // --- Sub-handlers ---

        /// <summary>Set a single tile at a cell position.</summary>
        internal static string SetTile(JObject args)
        {
            var tilemap = ResolveTilemap(args["tilemap_path"]?.Value<string>(), out var error);
            if (tilemap == null) return error;

            var pos = ParseCellPosition(args["position"] as JArray, out var posError);
            if (posError != null) return posError;

            var tileAsset = args["tile_asset"]?.Value<string>();
            if (string.IsNullOrEmpty(tileAsset))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'tile_asset' parameter",
                    "Provide an asset path to a TileBase asset, e.g. 'Assets/Tiles/GrassTile.asset'");

            var tile = AssetDatabase.LoadAssetAtPath<TileBase>(tileAsset);
            if (tile == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Tile asset not found at '{tileAsset}'",
                    "Check the asset path is correct and the asset is a TileBase type");

            Undo.RecordObject(tilemap, "Theatre tilemap_op:set_tile");
            tilemap.SetTile(pos, tile);
            tilemap.RefreshAllTiles();
            EditorUtility.SetDirty(tilemap);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_tile";
            response["position"] = new JArray(pos.x, pos.y, pos.z);
            response["tile_asset"] = tileAsset;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Batch-set multiple tiles using the same tile asset.</summary>
        internal static string SetTiles(JObject args)
        {
            var tilemap = ResolveTilemap(args["tilemap_path"]?.Value<string>(), out var error);
            if (tilemap == null) return error;

            var tileAsset = args["tile_asset"]?.Value<string>();
            if (string.IsNullOrEmpty(tileAsset))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'tile_asset' parameter",
                    "Provide an asset path to a TileBase asset");

            var positionsToken = args["positions"] as JArray;
            if (positionsToken == null || positionsToken.Count == 0)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or empty 'positions' parameter",
                    "Provide an array of cell positions, e.g. [[0,0,0],[1,0,0]]");

            var tile = AssetDatabase.LoadAssetAtPath<TileBase>(tileAsset);
            if (tile == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Tile asset not found at '{tileAsset}'",
                    "Check the asset path is correct and the asset is a TileBase type");

            int count = positionsToken.Count;
            var posArray = new Vector3Int[count];
            var tileArray = new TileBase[count];

            for (int i = 0; i < count; i++)
            {
                var posArr = positionsToken[i] as JArray;
                var p = ParseCellPosition(posArr, out var pError);
                if (pError != null) return pError;
                posArray[i] = p;
                tileArray[i] = tile;
            }

            Undo.RecordObject(tilemap, "Theatre tilemap_op:set_tiles");
            tilemap.SetTiles(posArray, tileArray);
            tilemap.RefreshAllTiles();
            EditorUtility.SetDirty(tilemap);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_tiles";
            response["count"] = count;
            response["tile_asset"] = tileAsset;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Fill a rectangular region with the given tile.</summary>
        internal static string BoxFill(JObject args)
        {
            var tilemap = ResolveTilemap(args["tilemap_path"]?.Value<string>(), out var error);
            if (tilemap == null) return error;

            var tileAsset = args["tile_asset"]?.Value<string>();
            if (string.IsNullOrEmpty(tileAsset))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'tile_asset' parameter",
                    "Provide an asset path to a TileBase asset");

            var startPos = ParseCellPosition(args["start"] as JArray, out var startError);
            if (startError != null) return startError;

            var endPos = ParseCellPosition(args["end"] as JArray, out var endError);
            if (endError != null) return endError;

            var tile = AssetDatabase.LoadAssetAtPath<TileBase>(tileAsset);
            if (tile == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Tile asset not found at '{tileAsset}'",
                    "Check the asset path is correct and the asset is a TileBase type");

            int minX = Math.Min(startPos.x, endPos.x);
            int maxX = Math.Max(startPos.x, endPos.x);
            int minY = Math.Min(startPos.y, endPos.y);
            int maxY = Math.Max(startPos.y, endPos.y);
            int z = startPos.z;

            int totalCount = (maxX - minX + 1) * (maxY - minY + 1);
            var posArray = new Vector3Int[totalCount];
            var tileArray = new TileBase[totalCount];
            int idx = 0;
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    posArray[idx] = new Vector3Int(x, y, z);
                    tileArray[idx] = tile;
                    idx++;
                }
            }

            Undo.RecordObject(tilemap, "Theatre tilemap_op:box_fill");
            tilemap.SetTiles(posArray, tileArray);
            tilemap.RefreshAllTiles();
            EditorUtility.SetDirty(tilemap);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "box_fill";
            response["start"] = new JArray(minX, minY, z);
            response["end"] = new JArray(maxX, maxY, z);
            response["count"] = totalCount;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Flood-fill from a position with the given tile.</summary>
        internal static string FloodFill(JObject args)
        {
            var tilemap = ResolveTilemap(args["tilemap_path"]?.Value<string>(), out var error);
            if (tilemap == null) return error;

            var tileAsset = args["tile_asset"]?.Value<string>();
            if (string.IsNullOrEmpty(tileAsset))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'tile_asset' parameter",
                    "Provide an asset path to a TileBase asset");

            var pos = ParseCellPosition(args["position"] as JArray, out var posError);
            if (posError != null) return posError;

            var tile = AssetDatabase.LoadAssetAtPath<TileBase>(tileAsset);
            if (tile == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Tile asset not found at '{tileAsset}'",
                    "Check the asset path is correct and the asset is a TileBase type");

            Undo.RecordObject(tilemap, "Theatre tilemap_op:flood_fill");
            tilemap.FloodFill(pos, tile);
            tilemap.RefreshAllTiles();
            EditorUtility.SetDirty(tilemap);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "flood_fill";
            response["position"] = new JArray(pos.x, pos.y, pos.z);
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Clear all tiles or a specific region.</summary>
        internal static string Clear(JObject args)
        {
            var tilemap = ResolveTilemap(args["tilemap_path"]?.Value<string>(), out var error);
            if (tilemap == null) return error;

            var regionToken = args["region"] as JObject;
            Undo.RecordObject(tilemap, "Theatre tilemap_op:clear");

            if (regionToken != null)
            {
                // Region clear
                var startPos = ParseCellPosition(regionToken["start"] as JArray, out var startError);
                if (startError != null) return startError;

                var endPos = ParseCellPosition(regionToken["end"] as JArray, out var endError);
                if (endError != null) return endError;

                int minX = Math.Min(startPos.x, endPos.x);
                int maxX = Math.Max(startPos.x, endPos.x);
                int minY = Math.Min(startPos.y, endPos.y);
                int maxY = Math.Max(startPos.y, endPos.y);
                int z = startPos.z;

                int totalCount = (maxX - minX + 1) * (maxY - minY + 1);
                var posArray = new Vector3Int[totalCount];
                var tileArray = new TileBase[totalCount]; // all null
                int idx = 0;
                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        posArray[idx] = new Vector3Int(x, y, z);
                        idx++;
                    }
                }

                tilemap.SetTiles(posArray, tileArray);
                tilemap.RefreshAllTiles();
                EditorUtility.SetDirty(tilemap);

                var regionResponse = new JObject();
                regionResponse["result"] = "ok";
                regionResponse["operation"] = "clear";
                regionResponse["region"] = true;
                regionResponse["count"] = totalCount;
                ResponseHelpers.AddFrameContext(regionResponse);
                return regionResponse.ToString(Formatting.None);
            }

            // Full clear
            tilemap.ClearAllTiles();
            EditorUtility.SetDirty(tilemap);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "clear";
            response["region"] = false;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Read the tile at a cell position.</summary>
        internal static string GetTile(JObject args)
        {
            var tilemap = ResolveTilemap(args["tilemap_path"]?.Value<string>(), out var error);
            if (tilemap == null) return error;

            var pos = ParseCellPosition(args["position"] as JArray, out var posError);
            if (posError != null) return posError;

            var tile = tilemap.GetTile(pos);
            string tileAssetPath = null;
            if (tile != null)
                tileAssetPath = AssetDatabase.GetAssetPath(tile);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "get_tile";
            response["position"] = new JArray(pos.x, pos.y, pos.z);
            response["tile"] = tileAssetPath != null ? (JToken)tileAssetPath : JValue.CreateNull();
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>List all occupied tile positions (budget-limited).</summary>
        internal static string GetUsedTiles(JObject args)
        {
            var tilemap = ResolveTilemap(args["tilemap_path"]?.Value<string>(), out var error);
            if (tilemap == null) return error;

            int budget = args["budget"]?.Value<int>() ?? 1500;

            tilemap.CompressBounds();
            var bounds = tilemap.cellBounds;

            var tiles = new JArray();
            int total = 0;
            int returned = 0;
            int estimatedLength = 50; // base overhead

            for (int z = bounds.zMin; z < bounds.zMax; z++)
            {
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    for (int x = bounds.xMin; x < bounds.xMax; x++)
                    {
                        var pos = new Vector3Int(x, y, z);
                        var tile = tilemap.GetTile(pos);
                        if (tile == null) continue;

                        total++;
                        if (estimatedLength >= budget) continue;

                        var assetPath = AssetDatabase.GetAssetPath(tile);
                        var entry = new JObject();
                        entry["position"] = new JArray(x, y, z);
                        entry["tile"] = assetPath ?? tile.name;

                        var entryStr = entry.ToString(Formatting.None);
                        estimatedLength += entryStr.Length + 2;
                        if (estimatedLength >= budget) continue;

                        tiles.Add(entry);
                        returned++;
                    }
                }
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "get_used_tiles";
            response["tiles"] = tiles;
            response["count"] = total;
            response["returned"] = returned;
            if (returned < total)
                response["truncated"] = true;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Create a new RuleTile asset.</summary>
        internal static string CreateRuleTile(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".asset");
            if (pathError != null) return pathError;

            // Dry run
            var dryRun = DirectorHelpers.CheckDryRun(args, () => (true, new List<string>()));
            if (dryRun != null) return dryRun;

            // RuleTile is in UnityEngine.Tilemaps via com.unity.2d.tilemap.extras
            // Use reflection in case the type isn't available
            var ruleTileType = Type.GetType("UnityEngine.Tilemaps.RuleTile, Unity.2D.Tilemap.Extras")
                ?? Type.GetType("UnityEngine.Tilemaps.RuleTile, UnityEngine.Tilemaps.Extras")
                ?? FindTypeInLoadedAssemblies("RuleTile");

            if (ruleTileType == null)
                return ResponseHelpers.ErrorResponse(
                    "package_not_installed",
                    "RuleTile type not found. The 'com.unity.2d.tilemap.extras' package may not be installed.",
                    "Install 'com.unity.2d.tilemap.extras' via the Package Manager");

            var tile = (ScriptableObject)ScriptableObject.CreateInstance(ruleTileType);

            // Set default sprite if provided
            var defaultSpritePath = args["default_sprite"]?.Value<string>();
            if (!string.IsNullOrEmpty(defaultSpritePath))
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(defaultSpritePath);
                if (sprite != null)
                {
                    var spriteField = ruleTileType.GetField("m_DefaultSprite",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    spriteField?.SetValue(tile, sprite);
                }
            }

            DirectorHelpers.EnsureParentDirectory(assetPath);
            AssetDatabase.CreateAsset(tile, assetPath);
            AssetDatabase.SaveAssets();
            Undo.RegisterCreatedObjectUndo(tile, "Theatre tilemap_op:create_rule_tile");

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create_rule_tile";
            response["asset_path"] = assetPath;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Set sorting layer, order, or material on a TilemapRenderer.</summary>
        internal static string SetTilemapLayer(JObject args)
        {
            var tilemap = ResolveTilemap(args["tilemap_path"]?.Value<string>(), out var error);
            if (tilemap == null) return error;

            var renderer = tilemap.GetComponent<TilemapRenderer>();
            if (renderer == null)
                return ResponseHelpers.ErrorResponse(
                    "component_not_found",
                    $"No TilemapRenderer found on '{args["tilemap_path"]?.Value<string>()}'",
                    "Ensure the GameObject with the Tilemap also has a TilemapRenderer component");

            Undo.RecordObject(renderer, "Theatre tilemap_op:set_tilemap_layer");

            var sortingLayer = args["sorting_layer"]?.Value<string>();
            if (!string.IsNullOrEmpty(sortingLayer))
                renderer.sortingLayerName = sortingLayer;

            var sortingOrderToken = args["sorting_order"];
            if (sortingOrderToken != null && sortingOrderToken.Type != JTokenType.Null)
                renderer.sortingOrder = sortingOrderToken.Value<int>();

            var materialPath = args["material"]?.Value<string>();
            if (!string.IsNullOrEmpty(materialPath))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (mat == null)
                    return ResponseHelpers.ErrorResponse(
                        "asset_not_found",
                        $"Material not found at '{materialPath}'",
                        "Check the asset path is correct and the asset is a Material");
                renderer.material = mat;
            }

            EditorUtility.SetDirty(renderer);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_tilemap_layer";
            response["sorting_layer"] = renderer.sortingLayerName;
            response["sorting_order"] = renderer.sortingOrder;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        // --- Helpers ---

        /// <summary>
        /// Resolve a Tilemap component from a hierarchy path.
        /// Returns null and sets error on failure.
        /// </summary>
        private static Tilemap ResolveTilemap(string tilemapPath, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(tilemapPath))
            {
                error = ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'tilemap_path' parameter",
                    "Provide the hierarchy path to a GameObject with a Tilemap component, e.g. '/Grid/Tilemap'");
                return null;
            }

            var resolved = ObjectResolver.Resolve(path: tilemapPath);
            if (!resolved.Success)
            {
                error = ResponseHelpers.ErrorResponse(resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);
                return null;
            }

            var tilemap = resolved.GameObject.GetComponent<Tilemap>();
            if (tilemap == null)
            {
                error = ResponseHelpers.ErrorResponse(
                    "component_not_found",
                    $"No Tilemap component found on '{tilemapPath}'",
                    "Ensure the target GameObject has a Tilemap component");
                return null;
            }

            return tilemap;
        }

        /// <summary>
        /// Parse a cell position from a JArray [x, y] or [x, y, z].
        /// Returns the parsed Vector3Int, or sets posError on failure.
        /// </summary>
        private static Vector3Int ParseCellPosition(JArray arr, out string posError)
        {
            posError = null;
            if (arr == null || arr.Count < 2)
            {
                posError = ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Position must be a [x, y] or [x, y, z] array",
                    "Example: [3, 5] or [3, 5, 0]");
                return Vector3Int.zero;
            }

            int x = arr[0].Value<int>();
            int y = arr[1].Value<int>();
            int z = arr.Count >= 3 ? arr[2].Value<int>() : 0;
            return new Vector3Int(x, y, z);
        }

        /// <summary>
        /// Search all loaded assemblies for a type by simple name.
        /// Used to locate RuleTile without knowing its exact assembly.
        /// </summary>
        private static Type FindTypeInLoadedAssemblies(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.Name == typeName && typeof(ScriptableObject).IsAssignableFrom(type))
                            return type;
                    }
                }
                catch (System.Reflection.ReflectionTypeLoadException)
                {
                    // Skip assemblies that fail to enumerate
                }
            }
            return null;
        }

    }
}
