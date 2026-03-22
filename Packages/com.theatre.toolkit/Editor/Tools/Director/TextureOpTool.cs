using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEditor;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: texture_op
    /// Compound tool for managing Texture import settings and Sprite configuration.
    /// Operations: import, set_import_settings, create_sprite, sprite_sheet.
    /// </summary>
    public static class TextureOpTool
    {
        private static readonly JToken s_inputSchema;

        static TextureOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""import"", ""set_import_settings"", ""create_sprite"", ""sprite_sheet""],
                        ""description"": ""The texture operation to perform.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for the texture (e.g. 'Assets/Textures/MyTex.png').""
                    },
                    ""settings"": {
                        ""type"": ""object"",
                        ""description"": ""Import settings: texture_type, filter_mode, wrap_mode, max_size, compression, srgb, read_write, generate_mipmaps, pixels_per_unit.""
                    },
                    ""pixels_per_unit"": {
                        ""type"": ""number"",
                        ""description"": ""Pixels per unit for sprite import (default 100).""
                    },
                    ""pivot"": {
                        ""type"": ""array"",
                        ""description"": ""Sprite pivot [x, y] in 0..1 range (default [0.5, 0.5]).""
                    },
                    ""sprite_mode"": {
                        ""type"": ""string"",
                        ""enum"": [""single"", ""multiple""],
                        ""description"": ""Sprite import mode: single or multiple.""
                    },
                    ""mode"": {
                        ""type"": ""string"",
                        ""enum"": [""grid"", ""manual""],
                        ""description"": ""Sprite sheet slice mode: grid or manual.""
                    },
                    ""cell_size"": {
                        ""type"": ""array"",
                        ""description"": ""Grid cell size [width, height] in pixels.""
                    },
                    ""offset"": {
                        ""type"": ""array"",
                        ""description"": ""Grid offset [x, y] in pixels.""
                    },
                    ""padding"": {
                        ""type"": ""array"",
                        ""description"": ""Grid padding [x, y] in pixels.""
                    },
                    ""sprites"": {
                        ""type"": ""array"",
                        ""description"": ""Manual sprite definitions: [{name, rect:[x,y,w,h], pivot:[x,y]}].""
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
                name: "texture_op",
                description: "Manage Texture import settings and Sprite configuration in the Unity Editor. "
                    + "Operations: import, set_import_settings, create_sprite, sprite_sheet. "
                    + "Supports dry_run to validate without mutating.",
                inputSchema: s_inputSchema,
                group: ToolGroup.DirectorAsset,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = false
                }
            ));
        }

        private static string Execute(JToken arguments) =>
            CompoundToolDispatcher.Execute(
                "texture_op",
                arguments,
                (args, operation) => operation switch
                {
                    "import"               => Import(args),
                    "set_import_settings"  => SetImportSettings(args),
                    "create_sprite"        => CreateSprite(args),
                    "sprite_sheet"         => SpriteSheet(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: import, set_import_settings, create_sprite, sprite_sheet")
                },
                "import, set_import_settings, create_sprite, sprite_sheet");

        /// <summary>Import/verify a texture and optionally apply import settings.</summary>
        internal static string Import(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath);
            if (pathError != null) return pathError;

            var texture = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
            if (texture == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Texture not found at '{assetPath}'",
                    "Ensure the file exists and is a supported image format (.png, .jpg, .tga, etc.)");

            var settings = args["settings"] as JObject;
            int settingsApplied = 0;
            if (settings != null && settings.HasValues)
            {
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    settingsApplied = ApplyImportSettings(importer, settings);
                    importer.SaveAndReimport();
                }
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "import";
            response["asset_path"] = assetPath;
            response["width"] = texture.width;
            response["height"] = texture.height;
            if (settingsApplied > 0)
                response["settings_applied"] = settingsApplied;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Apply import settings to an existing texture.</summary>
        internal static string SetImportSettings(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath);
            if (pathError != null) return pathError;

            var settings = args["settings"] as JObject;
            if (settings == null || !settings.HasValues)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'settings' parameter",
                    "Provide a 'settings' object with import setting key/value pairs");

            var texture = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
            if (texture == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Texture not found at '{assetPath}'",
                    "Ensure the file exists and has been imported into the project");

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return ResponseHelpers.ErrorResponse(
                    "importer_not_found",
                    $"No TextureImporter found for '{assetPath}'",
                    "Ensure the asset is a texture type supported by TextureImporter");

            int settingsApplied = ApplyImportSettings(importer, settings);
            importer.SaveAndReimport();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_import_settings";
            response["asset_path"] = assetPath;
            response["settings_applied"] = settingsApplied;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Convert a texture to Sprite type with the given settings.</summary>
        internal static string CreateSprite(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath);
            if (pathError != null) return pathError;

            var texture = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
            if (texture == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Texture not found at '{assetPath}'",
                    "Ensure the file exists and is a supported image format");

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return ResponseHelpers.ErrorResponse(
                    "importer_not_found",
                    $"No TextureImporter found for '{assetPath}'",
                    "Ensure the asset is a texture type supported by TextureImporter");

            float pixelsPerUnit = args["pixels_per_unit"]?.Value<float>() ?? 100f;

            Vector2 pivot = new Vector2(0.5f, 0.5f);
            if (args["pivot"] is JArray pivotArr && pivotArr.Count >= 2)
                pivot = new Vector2(pivotArr[0].Value<float>(), pivotArr[1].Value<float>());

            var spriteModeStr = args["sprite_mode"]?.Value<string>() ?? "single";
            var spriteImportMode = spriteModeStr == "multiple"
                ? SpriteImportMode.Multiple
                : SpriteImportMode.Single;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = spriteImportMode;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.spritePivot = pivot;
            importer.SaveAndReimport();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create_sprite";
            response["asset_path"] = assetPath;
            response["sprite_mode"] = spriteModeStr;
            response["pixels_per_unit"] = pixelsPerUnit;
            response["pivot"] = new JArray(Math.Round(pivot.x, 3), Math.Round(pivot.y, 3));
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Configure sprite sheet slicing on a texture (grid or manual).</summary>
        internal static string SpriteSheet(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath);
            if (pathError != null) return pathError;

            var mode = args["mode"]?.Value<string>();
            if (string.IsNullOrEmpty(mode) || (mode != "grid" && mode != "manual"))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'mode' parameter",
                    "Valid modes: 'grid' or 'manual'");

            var texture = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
            if (texture == null)
                return ResponseHelpers.ErrorResponse(
                    "asset_not_found",
                    $"Texture not found at '{assetPath}'",
                    "Ensure the file exists and is a supported image format");

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return ResponseHelpers.ErrorResponse(
                    "importer_not_found",
                    $"No TextureImporter found for '{assetPath}'",
                    "Ensure the asset is a texture type supported by TextureImporter");

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;

            SpriteMetaData[] metaData;

            if (mode == "grid")
            {
                var cellSizeArr = args["cell_size"] as JArray;
                if (cellSizeArr == null || cellSizeArr.Count < 2)
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        "Missing required 'cell_size' parameter for grid mode",
                        "Provide 'cell_size': [width, height] in pixels");

                float cellW = cellSizeArr[0].Value<float>();
                float cellH = cellSizeArr[1].Value<float>();
                float offsetX = 0f, offsetY = 0f, paddingX = 0f, paddingY = 0f;

                if (args["offset"] is JArray offsetArr && offsetArr.Count >= 2)
                {
                    offsetX = offsetArr[0].Value<float>();
                    offsetY = offsetArr[1].Value<float>();
                }
                if (args["padding"] is JArray paddingArr && paddingArr.Count >= 2)
                {
                    paddingX = paddingArr[0].Value<float>();
                    paddingY = paddingArr[1].Value<float>();
                }

                int texWidth = texture.width;
                int texHeight = texture.height;
                var metaList = new List<SpriteMetaData>();
                int index = 0;

                for (float y = texHeight - cellH - offsetY; y >= offsetY - 0.001f; y -= cellH + paddingY)
                {
                    for (float x = offsetX; x + cellW <= texWidth + 0.001f; x += cellW + paddingX)
                    {
                        var meta = new SpriteMetaData();
                        meta.name = $"sprite_{index++}";
                        meta.rect = new Rect(x, y, cellW, cellH);
                        meta.alignment = 0; // center
                        meta.pivot = new Vector2(0.5f, 0.5f);
                        metaList.Add(meta);
                    }
                }

                metaData = metaList.ToArray();
            }
            else // manual
            {
                var spritesArr = args["sprites"] as JArray;
                if (spritesArr == null || spritesArr.Count == 0)
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        "Missing required 'sprites' array for manual mode",
                        "Provide 'sprites': [{\"name\":\"...\", \"rect\":[x,y,w,h], \"pivot\":[x,y]}]");

                var metaList = new List<SpriteMetaData>();
                foreach (var spriteToken in spritesArr)
                {
                    if (!(spriteToken is JObject spriteObj)) continue;

                    var meta = new SpriteMetaData();
                    meta.name = spriteObj["name"]?.Value<string>() ?? $"sprite_{metaList.Count}";

                    if (spriteObj["rect"] is JArray rectArr && rectArr.Count >= 4)
                        meta.rect = new Rect(
                            rectArr[0].Value<float>(), rectArr[1].Value<float>(),
                            rectArr[2].Value<float>(), rectArr[3].Value<float>());

                    meta.alignment = 0;
                    meta.pivot = new Vector2(0.5f, 0.5f);
                    if (spriteObj["pivot"] is JArray pivotArr && pivotArr.Count >= 2)
                        meta.pivot = new Vector2(pivotArr[0].Value<float>(), pivotArr[1].Value<float>());

                    metaList.Add(meta);
                }
                metaData = metaList.ToArray();
            }

            importer.spritesheet = metaData;
            importer.SaveAndReimport();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "sprite_sheet";
            response["asset_path"] = assetPath;
            response["mode"] = mode;
            response["sprite_count"] = metaData.Length;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        // --- Helpers ---

        private static int ApplyImportSettings(TextureImporter importer, JObject settings)
        {
            int count = 0;

            var textureTypeStr = settings["texture_type"]?.Value<string>();
            if (textureTypeStr != null)
            {
                switch (textureTypeStr)
                {
                    case "default":    importer.textureType = TextureImporterType.Default;   count++; break;
                    case "sprite":     importer.textureType = TextureImporterType.Sprite;    count++; break;
                    case "normal_map": importer.textureType = TextureImporterType.NormalMap; count++; break;
                    case "editor_gui": importer.textureType = TextureImporterType.GUI;       count++; break;
                    case "lightmap":   importer.textureType = TextureImporterType.Lightmap;  count++; break;
                }
            }

            var filterModeStr = settings["filter_mode"]?.Value<string>();
            if (filterModeStr != null)
            {
                switch (filterModeStr)
                {
                    case "point":     importer.filterMode = FilterMode.Point;     count++; break;
                    case "bilinear":  importer.filterMode = FilterMode.Bilinear;  count++; break;
                    case "trilinear": importer.filterMode = FilterMode.Trilinear; count++; break;
                }
            }

            var wrapModeStr = settings["wrap_mode"]?.Value<string>();
            if (wrapModeStr != null)
            {
                switch (wrapModeStr)
                {
                    case "repeat": importer.wrapMode = TextureWrapMode.Repeat; count++; break;
                    case "clamp":  importer.wrapMode = TextureWrapMode.Clamp;  count++; break;
                    case "mirror": importer.wrapMode = TextureWrapMode.Mirror; count++; break;
                }
            }

            if (settings["max_size"] != null)
            {
                importer.maxTextureSize = settings["max_size"].Value<int>();
                count++;
            }

            var compressionStr = settings["compression"]?.Value<string>();
            if (compressionStr != null)
            {
                switch (compressionStr)
                {
                    case "none":   importer.textureCompression = TextureImporterCompression.Uncompressed; count++; break;
                    case "low":    importer.textureCompression = TextureImporterCompression.CompressedLQ; count++; break;
                    case "normal": importer.textureCompression = TextureImporterCompression.Compressed;   count++; break;
                    case "high":   importer.textureCompression = TextureImporterCompression.CompressedHQ; count++; break;
                }
            }

            if (settings["srgb"] != null)
            {
                importer.sRGBTexture = settings["srgb"].Value<bool>();
                count++;
            }

            if (settings["read_write"] != null)
            {
                importer.isReadable = settings["read_write"].Value<bool>();
                count++;
            }

            if (settings["generate_mipmaps"] != null)
            {
                importer.mipmapEnabled = settings["generate_mipmaps"].Value<bool>();
                count++;
            }

            if (settings["pixels_per_unit"] != null)
            {
                importer.spritePixelsPerUnit = settings["pixels_per_unit"].Value<float>();
                count++;
            }

            return count;
        }
    }
}
