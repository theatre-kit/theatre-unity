#if THEATRE_HAS_URP || THEATRE_HAS_HDRP
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEditor;
#if THEATRE_HAS_URP
using UnityEngine.Rendering.Universal;
using UnityEditor.Rendering.Universal;
#endif
#if THEATRE_HAS_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: render_pipeline_op
    /// Compound tool for creating and configuring URP/HDRP render pipeline assets.
    /// Operations: create_urp_asset, create_hdrp_asset, set_quality_settings,
    ///             create_renderer, add_renderer_feature.
    /// Only registered when URP or HDRP is installed.
    /// </summary>
    public static class RenderPipelineOpTool
    {
        private static readonly JToken s_inputSchema;

        static RenderPipelineOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create_urp_asset"", ""create_hdrp_asset"", ""set_quality_settings"", ""create_renderer"", ""add_renderer_feature""],
                        ""description"": ""The render pipeline operation to perform.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for the new or existing pipeline asset (must end in .asset).""
                    },
                    ""renderer_type"": {
                        ""type"": ""string"",
                        ""enum"": [""forward"", ""deferred""],
                        ""description"": ""Renderer type for create_renderer. Defaults to 'forward'.""
                    },
                    ""feature_type"": {
                        ""type"": ""string"",
                        ""description"": ""Fully-qualified ScriptableRendererFeature type name for add_renderer_feature.""
                    },
                    ""settings"": {
                        ""type"": ""object"",
                        ""description"": ""Pipeline settings. Supported keys: hdr (bool), msaa (int 1/2/4/8), render_scale (float), shadow_distance (float), shadow_cascades (int 1-4), srp_batcher (bool).""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "render_pipeline_op",
                description: "Create and configure Universal Render Pipeline (URP) assets and renderer data. "
                    + "Operations: create_urp_asset, create_hdrp_asset, set_quality_settings, create_renderer, add_renderer_feature. "
                    + "Requires URP or HDRP package.",
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
                "render_pipeline_op",
                arguments,
                (args, operation) => operation switch
                {
                    "create_urp_asset"      => CreateUrpAsset(args),
                    "create_hdrp_asset"     => CreateHdrpAsset(args),
                    "set_quality_settings"  => SetQualitySettings(args),
                    "create_renderer"       => CreateRenderer(args),
                    "add_renderer_feature"  => AddRendererFeature(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create_urp_asset, create_hdrp_asset, set_quality_settings, create_renderer, add_renderer_feature")
                },
                "create_urp_asset, create_hdrp_asset, set_quality_settings, create_renderer, add_renderer_feature");

        /// <summary>Create a new URP pipeline asset at the given path.</summary>
        internal static string CreateUrpAsset(JObject args)
        {
#if THEATRE_HAS_URP
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".asset");
            if (pathError != null) return pathError;

            var asset = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
            if (asset == null)
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    "Failed to create UniversalRenderPipelineAsset instance",
                    "Check that the URP package is properly installed");

            // Apply optional settings
            var settings = args["settings"] as JObject;
            if (settings != null)
                ApplyUrpSettings(asset, settings);

            DirectorHelpers.EnsureParentDirectory(assetPath);
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create_urp_asset";
            response["asset_path"] = assetPath;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "package_not_installed",
                "URP is not installed",
                "Install com.unity.render-pipelines.universal via the Package Manager");
#endif
        }

        /// <summary>Create a new HDRP pipeline asset at the given path.</summary>
        internal static string CreateHdrpAsset(JObject args)
        {
#if THEATRE_HAS_HDRP
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".asset");
            if (pathError != null) return pathError;

            var asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
            if (asset == null)
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    "Failed to create HDRenderPipelineAsset instance",
                    "Check that the HDRP package is properly installed");

            DirectorHelpers.EnsureParentDirectory(assetPath);
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create_hdrp_asset";
            response["asset_path"] = assetPath;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "package_not_installed",
                "HDRP is not installed",
                "Install com.unity.render-pipelines.high-definition via the Package Manager");
#endif
        }

        /// <summary>Modify pipeline settings on an existing URP asset.</summary>
        internal static string SetQualitySettings(JObject args)
        {
#if THEATRE_HAS_URP
            var settings = args["settings"] as JObject;
            if (settings == null || !settings.HasValues)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'settings' parameter",
                    "Provide a 'settings' object with keys such as hdr, msaa, render_scale, shadow_distance");

            var loadError = DirectorHelpers.LoadAsset<UniversalRenderPipelineAsset>(
                args, out var asset, out var assetPath, ".asset");
            if (loadError != null) return loadError;

            Undo.RecordObject(asset, "Theatre render_pipeline_op:set_quality_settings");
            ApplyUrpSettings(asset, settings);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_quality_settings";
            response["asset_path"] = assetPath;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "package_not_installed",
                "URP is not installed",
                "Install com.unity.render-pipelines.universal via the Package Manager");
#endif
        }

        /// <summary>Create a new URP renderer data asset.</summary>
        internal static string CreateRenderer(JObject args)
        {
#if THEATRE_HAS_URP
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".asset");
            if (pathError != null) return pathError;

            // UniversalRendererData is the unified renderer for both forward and deferred in URP 14+
            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            if (rendererData == null)
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    "Failed to create UniversalRendererData instance",
                    "Check that the URP package is properly installed");

            DirectorHelpers.EnsureParentDirectory(assetPath);
            AssetDatabase.CreateAsset(rendererData, assetPath);
            AssetDatabase.SaveAssets();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create_renderer";
            response["asset_path"] = assetPath;
            response["renderer_type"] = args["renderer_type"]?.Value<string>() ?? "forward";
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "package_not_installed",
                "URP is not installed",
                "Install com.unity.render-pipelines.universal via the Package Manager");
#endif
        }

        /// <summary>Add a ScriptableRendererFeature to an existing renderer data asset.</summary>
        internal static string AddRendererFeature(JObject args)
        {
#if THEATRE_HAS_URP
            var loadError = DirectorHelpers.LoadAsset<UniversalRendererData>(
                args, out var rendererData, out var assetPath, ".asset");
            if (loadError != null) return loadError;

            var featureTypeName = args["feature_type"]?.Value<string>();
            if (string.IsNullOrEmpty(featureTypeName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'feature_type' parameter",
                    "Provide the fully-qualified type name of a ScriptableRendererFeature");

            // Resolve the feature type from all loaded assemblies
            Type featureType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                featureType = assembly.GetType(featureTypeName);
                if (featureType != null) break;
            }

            if (featureType == null)
                return ResponseHelpers.ErrorResponse(
                    "type_not_found",
                    $"ScriptableRendererFeature type '{featureTypeName}' not found",
                    "Provide the fully-qualified type name (e.g. 'UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion')");

            if (!typeof(ScriptableRendererFeature).IsAssignableFrom(featureType))
                return ResponseHelpers.ErrorResponse(
                    "type_invalid",
                    $"Type '{featureTypeName}' does not inherit from ScriptableRendererFeature",
                    "Feature type must be a ScriptableRendererFeature subclass");

            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(featureType);
            if (feature == null)
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"Failed to create instance of '{featureTypeName}'",
                    "Check the Unity Console for details");

            feature.name = featureType.Name;
            Undo.RecordObject(rendererData, "Theatre render_pipeline_op:add_renderer_feature");

            // Add feature as a sub-asset and add it to the renderer's features list via SerializedObject
            AssetDatabase.AddObjectToAsset(feature, assetPath);
            var so = new SerializedObject(rendererData);
            var featuresProperty = so.FindProperty("m_RendererFeatures");
            if (featuresProperty == null)
                featuresProperty = so.FindProperty("rendererFeatures");

            if (featuresProperty == null)
            {
                UnityEngine.Object.DestroyImmediate(feature, true);
                return ResponseHelpers.ErrorResponse(
                    "internal_api_unavailable",
                    "Could not find renderer features list on renderer data asset",
                    "The URP internal API may have changed. Try adding the feature manually in the Inspector");
            }

            featuresProperty.arraySize += 1;
            featuresProperty.GetArrayElementAtIndex(featuresProperty.arraySize - 1).objectReferenceValue = feature;
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_renderer_feature";
            response["asset_path"] = assetPath;
            response["feature_type"] = featureTypeName;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
#else
            return ResponseHelpers.ErrorResponse(
                "package_not_installed",
                "URP is not installed",
                "Install com.unity.render-pipelines.universal via the Package Manager");
#endif
        }

        // --- Helpers ---

#if THEATRE_HAS_URP
        private static void ApplyUrpSettings(UniversalRenderPipelineAsset asset, JObject settings)
        {
            if (settings["hdr"] is JToken hdrToken && hdrToken.Type == JTokenType.Boolean)
                asset.supportsHDR = hdrToken.Value<bool>();

            if (settings["msaa"] is JToken msaaToken &&
                (msaaToken.Type == JTokenType.Integer || msaaToken.Type == JTokenType.Float))
            {
                var msaa = msaaToken.Value<int>();
                // URP accepts 1, 2, 4, 8
                if (msaa == 1 || msaa == 2 || msaa == 4 || msaa == 8)
                    asset.msaaSampleCount = msaa;
            }

            if (settings["render_scale"] is JToken rsToken &&
                (rsToken.Type == JTokenType.Float || rsToken.Type == JTokenType.Integer))
                asset.renderScale = Mathf.Clamp(rsToken.Value<float>(), 0.1f, 2f);

            if (settings["shadow_distance"] is JToken sdToken &&
                (sdToken.Type == JTokenType.Float || sdToken.Type == JTokenType.Integer))
                asset.shadowDistance = Mathf.Max(0f, sdToken.Value<float>());

            if (settings["shadow_cascades"] is JToken scToken &&
                (scToken.Type == JTokenType.Integer || scToken.Type == JTokenType.Float))
            {
                var cascades = scToken.Value<int>();
                if (cascades >= 1 && cascades <= 4)
                    asset.shadowCascadeCount = cascades;
            }

            if (settings["srp_batcher"] is JToken srpToken && srpToken.Type == JTokenType.Boolean)
                asset.useSRPBatcher = srpToken.Value<bool>();
        }
#endif

    }
}
#endif
