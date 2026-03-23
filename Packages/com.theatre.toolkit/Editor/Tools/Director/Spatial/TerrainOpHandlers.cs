using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
using UnityEditor;
using Theatre.Editor.Tools.Director.Shared;

namespace Theatre.Editor.Tools.Director.Spatial
{
    /// <summary>
    /// Handlers for all terrain_op operations.
    /// Each method is internal and called by TerrainOpTool's dispatcher.
    /// </summary>
    internal static class TerrainOpHandlers
    {
        /// <summary>Create a new TerrainData asset and Terrain GameObject.</summary>
        internal static string Create(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".asset");
            if (pathError != null) return pathError;

            float width = args["width"]?.Value<float>() ?? 1000f;
            float height = args["height"]?.Value<float>() ?? 600f;
            float length = args["length"]?.Value<float>() ?? 1000f;
            int resolution = args["heightmap_resolution"]?.Value<int>() ?? 513;

            var terrainData = new TerrainData();
            terrainData.heightmapResolution = resolution;
            terrainData.size = new Vector3(width, height, length);

            DirectorHelpers.EnsureParentDirectory(assetPath);
            AssetDatabase.CreateAsset(terrainData, assetPath);
            AssetDatabase.SaveAssets();

            var go = Terrain.CreateTerrainGameObject(terrainData);

            var posArr = args["position"] as JArray;
            if (posArr != null && posArr.Count >= 3)
            {
                go.transform.position = new Vector3(
                    posArr[0].Value<float>(),
                    posArr[1].Value<float>(),
                    posArr[2].Value<float>());
            }

            Undo.RegisterCreatedObjectUndo(go, "Theatre terrain_op:create");

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create";
            response["asset_path"] = assetPath;
            ResponseHelpers.AddIdentity(response, go);
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Set terrain heights from a 2D float array.</summary>
        internal static string SetHeightmap(JObject args)
        {
            var terrain = ResolveTerrain(args["terrain_path"]?.Value<string>(), out var error);
            if (terrain == null) return error;

            var heightsToken = args["heights"] as JArray;
            if (heightsToken == null || heightsToken.Count == 0)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or empty 'heights' parameter",
                    "Provide a 2D array of floats (0.0-1.0), e.g. [[0.0, 0.1], [0.1, 0.2]]");

            var regionToken = args["region"] as JObject;
            int offsetX = regionToken?["x"]?.Value<int>() ?? 0;
            int offsetY = regionToken?["y"]?.Value<int>() ?? 0;

            int rows = heightsToken.Count;
            int cols = (heightsToken[0] as JArray)?.Count ?? 0;
            if (cols == 0)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Heights array rows must not be empty",
                    "Provide a 2D array of floats, e.g. [[0.0, 0.1], [0.1, 0.2]]");

            var data = new float[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                var row = heightsToken[r] as JArray;
                if (row == null) continue;
                for (int c = 0; c < cols && c < row.Count; c++)
                    data[r, c] = row[c].Value<float>();
            }

            Undo.RecordObject(terrain.terrainData, "Theatre terrain_op:set_heightmap");
            terrain.terrainData.SetHeights(offsetX, offsetY, data);
            EditorUtility.SetDirty(terrain.terrainData);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_heightmap";
            response["rows"] = rows;
            response["columns"] = cols;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Smooth terrain heightmap with a 3x3 box blur.</summary>
        internal static string SmoothHeightmap(JObject args)
        {
            var terrain = ResolveTerrain(args["terrain_path"]?.Value<string>(), out var error);
            if (terrain == null) return error;

            int iterations = args["iterations"]?.Value<int>() ?? 1;
            if (iterations < 1) iterations = 1;

            var regionToken = args["region"] as JObject;
            var td = terrain.terrainData;
            int totalRes = td.heightmapResolution;

            int rx = regionToken?["x"]?.Value<int>() ?? 0;
            int ry = regionToken?["y"]?.Value<int>() ?? 0;
            int rw = regionToken?["width"]?.Value<int>() ?? totalRes;
            int rh = regionToken?["height"]?.Value<int>() ?? totalRes;

            // Clamp region to valid range
            rx = Math.Max(0, Math.Min(rx, totalRes - 1));
            ry = Math.Max(0, Math.Min(ry, totalRes - 1));
            rw = Math.Max(1, Math.Min(rw, totalRes - rx));
            rh = Math.Max(1, Math.Min(rh, totalRes - ry));

            Undo.RecordObject(td, "Theatre terrain_op:smooth_heightmap");

            for (int iter = 0; iter < iterations; iter++)
            {
                // Read the full region plus 1-cell border for sampling
                int sampleX = Math.Max(0, rx - 1);
                int sampleY = Math.Max(0, ry - 1);
                int sampleW = Math.Min(totalRes - sampleX, rw + 2);
                int sampleH = Math.Min(totalRes - sampleY, rh + 2);

                float[,] src = td.GetHeights(sampleX, sampleY, sampleW, sampleH);
                float[,] dst = new float[rh, rw];

                for (int row = 0; row < rh; row++)
                {
                    for (int col = 0; col < rw; col++)
                    {
                        // Offset into the src array (which has 1-cell border)
                        int srcRow = (rx > 0 ? 1 : 0) + row;
                        int srcCol = (ry > 0 ? 1 : 0) + col;

                        float sum = 0f;
                        int count = 0;
                        for (int dr = -1; dr <= 1; dr++)
                        {
                            for (int dc = -1; dc <= 1; dc++)
                            {
                                int nr = srcRow + dr;
                                int nc = srcCol + dc;
                                if (nr >= 0 && nr < sampleH && nc >= 0 && nc < sampleW)
                                {
                                    sum += src[nr, nc];
                                    count++;
                                }
                            }
                        }
                        dst[row, col] = count > 0 ? sum / count : src[srcRow, srcCol];
                    }
                }

                td.SetHeights(rx, ry, dst);
            }

            EditorUtility.SetDirty(td);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "smooth_heightmap";
            response["iterations"] = iterations;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Paint a texture layer at world positions.</summary>
        internal static string PaintTexture(JObject args)
        {
            var terrain = ResolveTerrain(args["terrain_path"]?.Value<string>(), out var error);
            if (terrain == null) return error;

            var layerIndexToken = args["layer_index"];
            if (layerIndexToken == null || layerIndexToken.Type == JTokenType.Null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'layer_index' parameter",
                    "Provide the terrain layer index to paint (0-based)");

            int layerIndex = layerIndexToken.Value<int>();
            var td = terrain.terrainData;

            if (layerIndex < 0 || layerIndex >= td.terrainLayers.Length)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Layer index {layerIndex} is out of range (terrain has {td.terrainLayers.Length} layers)",
                    "Add layers first with add_terrain_layer, or check the layer count");

            var positionsToken = args["positions"] as JArray;
            if (positionsToken == null || positionsToken.Count == 0)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or empty 'positions' parameter",
                    "Provide an array of [x, z] world positions");

            float opacity = args["opacity"]?.Value<float>() ?? 1f;
            int brushSize = args["brush_size"]?.Value<int>() ?? 5;
            if (brushSize < 1) brushSize = 1;

            int alphaW = td.alphamapWidth;
            int alphaH = td.alphamapHeight;
            int layerCount = td.alphamapLayers;
            float[,,] alphas = td.GetAlphamaps(0, 0, alphaW, alphaH);

            Vector3 terrainPos = terrain.transform.position;
            Vector3 terrainSize = td.size;

            int paintedCount = 0;
            foreach (var posToken in positionsToken)
            {
                var posArr = posToken as JArray;
                if (posArr == null || posArr.Count < 2) continue;

                float wx = posArr[0].Value<float>();
                float wz = posArr[1].Value<float>();

                // Convert world to normalized terrain coords
                float nx = (wx - terrainPos.x) / terrainSize.x;
                float nz = (wz - terrainPos.z) / terrainSize.z;

                // Convert normalized to alphamap coords
                int ax = Mathf.RoundToInt(nx * alphaW);
                int az = Mathf.RoundToInt(nz * alphaH);

                int half = brushSize / 2;
                for (int dz = -half; dz <= half; dz++)
                {
                    for (int dx = -half; dx <= half; dx++)
                    {
                        int px = ax + dx;
                        int pz = az + dz;
                        if (px < 0 || px >= alphaW || pz < 0 || pz >= alphaH) continue;

                        // Normalize existing weights minus target, then blend
                        float existingTarget = alphas[pz, px, layerIndex];
                        float newTarget = Mathf.Clamp01(existingTarget + opacity);
                        float delta = newTarget - existingTarget;

                        // Redistribute delta from other layers
                        float otherSum = 1f - existingTarget;
                        if (otherSum > 0f)
                        {
                            for (int l = 0; l < layerCount; l++)
                            {
                                if (l == layerIndex) continue;
                                alphas[pz, px, l] = Mathf.Clamp01(
                                    alphas[pz, px, l] - delta * (alphas[pz, px, l] / otherSum));
                            }
                        }
                        alphas[pz, px, layerIndex] = newTarget;
                    }
                }
                paintedCount++;
            }

            Undo.RecordObject(td, "Theatre terrain_op:paint_texture");
            td.SetAlphamaps(0, 0, alphas);
            EditorUtility.SetDirty(td);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "paint_texture";
            response["painted"] = paintedCount;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Add a terrain layer with a diffuse texture.</summary>
        internal static string AddTerrainLayer(JObject args)
        {
            var terrain = ResolveTerrain(args["terrain_path"]?.Value<string>(), out var error);
            if (terrain == null) return error;

            var diffusePath = args["diffuse_texture"]?.Value<string>();
            if (string.IsNullOrEmpty(diffusePath))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'diffuse_texture' parameter",
                    "Provide the asset path to a Texture2D for the terrain layer");

            var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(diffusePath);
            if (diffuse == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Texture not found at '{diffusePath}'",
                    "Check the asset path is correct and the asset is a Texture2D");

            var layer = new TerrainLayer();
            layer.diffuseTexture = diffuse;

            var normalPath = args["normal_texture"]?.Value<string>();
            if (!string.IsNullOrEmpty(normalPath))
            {
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                if (normal != null) layer.normalMapTexture = normal;
            }

            var tileSizeArr = args["tile_size"] as JArray;
            layer.tileSize = tileSizeArr != null && tileSizeArr.Count >= 2
                ? new Vector2(tileSizeArr[0].Value<float>(), tileSizeArr[1].Value<float>())
                : new Vector2(15f, 15f);

            var tileOffsetArr = args["tile_offset"] as JArray;
            layer.tileOffset = tileOffsetArr != null && tileOffsetArr.Count >= 2
                ? new Vector2(tileOffsetArr[0].Value<float>(), tileOffsetArr[1].Value<float>())
                : Vector2.zero;

            var td = terrain.terrainData;
            var existingLayers = td.terrainLayers;
            var newLayers = new TerrainLayer[existingLayers.Length + 1];
            Array.Copy(existingLayers, newLayers, existingLayers.Length);
            newLayers[existingLayers.Length] = layer;

            Undo.RecordObject(td, "Theatre terrain_op:add_terrain_layer");
            td.terrainLayers = newLayers;
            EditorUtility.SetDirty(td);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_terrain_layer";
            response["layer_index"] = existingLayers.Length;
            response["diffuse_texture"] = diffusePath;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Place tree instances at world positions.</summary>
        internal static string PlaceTrees(JObject args)
        {
            var terrain = ResolveTerrain(args["terrain_path"]?.Value<string>(), out var error);
            if (terrain == null) return error;

            var prefabPath = args["prefab"]?.Value<string>();
            if (string.IsNullOrEmpty(prefabPath))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'prefab' parameter",
                    "Provide the asset path to a tree prefab");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Prefab not found at '{prefabPath}'",
                    "Check the asset path is correct and the asset is a GameObject prefab");

            var positionsToken = args["positions"] as JArray;
            if (positionsToken == null || positionsToken.Count == 0)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or empty 'positions' parameter",
                    "Provide an array of [x, z] world positions");

            float heightScale = args["height_scale"]?.Value<float>() ?? 1f;
            float widthScale = args["width_scale"]?.Value<float>() ?? 1f;
            bool hasRotation = args["rotation"] != null && args["rotation"].Type != JTokenType.Null;
            float rotationRad = hasRotation
                ? args["rotation"].Value<float>() * Mathf.Deg2Rad
                : 0f;

            var td = terrain.terrainData;
            Vector3 terrainPos = terrain.transform.position;
            Vector3 terrainSize = td.size;

            // Find or add tree prototype
            var prototypes = td.treePrototypes;
            int protoIndex = -1;
            for (int i = 0; i < prototypes.Length; i++)
            {
                if (prototypes[i].prefab == prefab)
                {
                    protoIndex = i;
                    break;
                }
            }

            if (protoIndex < 0)
            {
                var newProtos = new TreePrototype[prototypes.Length + 1];
                Array.Copy(prototypes, newProtos, prototypes.Length);
                newProtos[prototypes.Length] = new TreePrototype { prefab = prefab };
                td.treePrototypes = newProtos;
                protoIndex = prototypes.Length;
            }

            // Build new tree instances
            var existingTrees = td.treeInstances;
            var newTrees = new TreeInstance[positionsToken.Count];
            var rng = new System.Random();
            int placedCount = 0;

            foreach (var posToken in positionsToken)
            {
                var posArr = posToken as JArray;
                if (posArr == null || posArr.Count < 2) continue;

                float wx = posArr[0].Value<float>();
                float wz = posArr[1].Value<float>();

                float nx = (wx - terrainPos.x) / terrainSize.x;
                float nz = (wz - terrainPos.z) / terrainSize.z;
                float ny = terrain.SampleHeight(new Vector3(wx, 0f, wz)) / terrainSize.y;

                var inst = new TreeInstance
                {
                    position = new Vector3(nx, ny, nz),
                    heightScale = heightScale,
                    widthScale = widthScale,
                    rotation = hasRotation ? rotationRad : (float)(rng.NextDouble() * Math.PI * 2),
                    color = Color.white,
                    lightmapColor = Color.white,
                    prototypeIndex = protoIndex
                };
                newTrees[placedCount++] = inst;
            }

            // Combine existing + new
            var combined = new TreeInstance[existingTrees.Length + placedCount];
            Array.Copy(existingTrees, combined, existingTrees.Length);
            Array.Copy(newTrees, 0, combined, existingTrees.Length, placedCount);

            Undo.RecordObject(td, "Theatre terrain_op:place_trees");
            td.SetTreeInstances(combined, true);
            EditorUtility.SetDirty(td);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "place_trees";
            response["placed"] = placedCount;
            response["prototype_index"] = protoIndex;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Place detail instances at world positions on a detail layer.</summary>
        internal static string PlaceDetails(JObject args)
        {
            var terrain = ResolveTerrain(args["terrain_path"]?.Value<string>(), out var error);
            if (terrain == null) return error;

            var layerIndexToken = args["layer_index"];
            if (layerIndexToken == null || layerIndexToken.Type == JTokenType.Null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'layer_index' parameter",
                    "Provide the detail layer index to paint (0-based)");

            int layerIndex = layerIndexToken.Value<int>();
            var td = terrain.terrainData;

            if (layerIndex < 0 || layerIndex >= td.detailPrototypes.Length)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Detail layer index {layerIndex} is out of range (terrain has {td.detailPrototypes.Length} detail layers)",
                    "Check the detail layer count in the terrain inspector");

            var positionsToken = args["positions"] as JArray;
            if (positionsToken == null || positionsToken.Count == 0)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or empty 'positions' parameter",
                    "Provide an array of [x, z] world positions");

            int density = args["density"]?.Value<int>() ?? 1;
            if (density < 0) density = 0;

            int detailW = td.detailWidth;
            int detailH = td.detailHeight;
            int[,] layer = td.GetDetailLayer(0, 0, detailW, detailH, layerIndex);

            Vector3 terrainPos = terrain.transform.position;
            Vector3 terrainSize = td.size;
            int paintedCount = 0;

            foreach (var posToken in positionsToken)
            {
                var posArr = posToken as JArray;
                if (posArr == null || posArr.Count < 2) continue;

                float wx = posArr[0].Value<float>();
                float wz = posArr[1].Value<float>();

                float nx = (wx - terrainPos.x) / terrainSize.x;
                float nz = (wz - terrainPos.z) / terrainSize.z;

                int dx = Mathf.RoundToInt(nx * detailW);
                int dz = Mathf.RoundToInt(nz * detailH);

                if (dx < 0 || dx >= detailW || dz < 0 || dz >= detailH) continue;

                layer[dz, dx] = density;
                paintedCount++;
            }

            Undo.RecordObject(td, "Theatre terrain_op:place_details");
            td.SetDetailLayer(0, 0, layerIndex, layer);
            EditorUtility.SetDirty(td);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "place_details";
            response["painted"] = paintedCount;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Change the size of a terrain.</summary>
        internal static string SetSize(JObject args)
        {
            var terrain = ResolveTerrain(args["terrain_path"]?.Value<string>(), out var error);
            if (terrain == null) return error;

            var td = terrain.terrainData;
            var current = td.size;

            float newWidth = args["width"]?.Value<float>() ?? current.x;
            float newHeight = args["height"]?.Value<float>() ?? current.y;
            float newLength = args["length"]?.Value<float>() ?? current.z;

            Undo.RecordObject(td, "Theatre terrain_op:set_size");
            td.size = new Vector3(newWidth, newHeight, newLength);
            EditorUtility.SetDirty(td);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_size";
            response["width"] = newWidth;
            response["height"] = newHeight;
            response["length"] = newLength;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Sample the terrain height at a world XZ position. Read-only.</summary>
        internal static string GetHeight(JObject args)
        {
            var terrain = ResolveTerrain(args["terrain_path"]?.Value<string>(), out var error);
            if (terrain == null) return error;

            var posArr = args["position"] as JArray;
            if (posArr == null || posArr.Count < 2)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'position' parameter",
                    "Provide [x, z] world position, e.g. [50, 50]");

            float wx = posArr[0].Value<float>();
            float wz = posArr[1].Value<float>();

            float sampledHeight = terrain.SampleHeight(new Vector3(wx, 0f, wz));

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "get_height";
            response["position"] = new JArray(
                Math.Round(wx, 3),
                Math.Round(sampledHeight, 4),
                Math.Round(wz, 3));
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        // --- Helpers ---

        /// <summary>
        /// Resolve a Terrain component from a hierarchy path.
        /// Returns null and sets error on failure.
        /// </summary>
        internal static Terrain ResolveTerrain(string terrainPath, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(terrainPath))
            {
                error = ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'terrain_path' parameter",
                    "Provide the hierarchy path to a GameObject with a Terrain component, e.g. '/Terrain'");
                return null;
            }

            var resolved = ObjectResolver.Resolve(path: terrainPath);
            if (!resolved.Success)
            {
                error = ResponseHelpers.ErrorResponse(resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);
                return null;
            }

            var terrain = resolved.GameObject.GetComponent<Terrain>();
            if (terrain == null)
            {
                error = ResponseHelpers.ErrorResponse(
                    "component_not_found",
                    $"No Terrain component found on '{terrainPath}'",
                    "Ensure the target GameObject has a Terrain component");
                return null;
            }

            return terrain;
        }
    }
}
