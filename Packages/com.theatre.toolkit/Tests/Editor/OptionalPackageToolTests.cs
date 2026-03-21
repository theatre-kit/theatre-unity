using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Theatre.Tests.Editor
{
#if THEATRE_HAS_URP
    using Theatre.Editor.Tools.Director;
    using UnityEngine.Rendering.Universal;

    [TestFixture]
    public class RenderPipelineOpTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = "Assets/_TheatreTest_URP";
            if (!AssetDatabase.IsValidFolder(_tempDir))
                AssetDatabase.CreateFolder("Assets", "_TheatreTest_URP");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_tempDir);
        }

        [Test]
        public void CreateUrpAsset_ProducesAsset()
        {
            var path = _tempDir + "/TestPipeline.asset";
            var result = RenderPipelineOpTool.CreateUrpAsset(new JObject
            {
                ["asset_path"] = path
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""), $"Unexpected response: {result}");
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            Assert.IsNotNull(asset, $"Expected URP asset at {path}");
        }

        [Test]
        public void CreateUrpAsset_WithSettings_AppliesSettings()
        {
            var path = _tempDir + "/TestPipelineSettings.asset";
            var result = RenderPipelineOpTool.CreateUrpAsset(new JObject
            {
                ["asset_path"] = path,
                ["settings"] = new JObject
                {
                    ["hdr"] = true,
                    ["msaa"] = 4,
                    ["render_scale"] = 0.75f,
                    ["shadow_distance"] = 50f,
                    ["shadow_cascades"] = 2,
                    ["srp_batcher"] = true
                }
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""), $"Unexpected response: {result}");
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            Assert.IsNotNull(asset, $"Expected URP asset at {path}");
            Assert.IsTrue(asset.supportsHDR, "HDR should be enabled");
            Assert.AreEqual(4, asset.msaaSampleCount, "MSAA should be 4");
        }

        [Test]
        public void CreateRenderer_ProducesAsset()
        {
            var path = _tempDir + "/TestRenderer.asset";
            var result = RenderPipelineOpTool.CreateRenderer(new JObject
            {
                ["asset_path"] = path
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""), $"Unexpected response: {result}");
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            Assert.IsNotNull(asset, $"Expected UniversalRendererData at {path}");
        }

        [Test]
        public void SetQualitySettings_ModifiesExistingAsset()
        {
            // Create an asset first
            var path = _tempDir + "/TestPipelineModify.asset";
            RenderPipelineOpTool.CreateUrpAsset(new JObject { ["asset_path"] = path });

            var result = RenderPipelineOpTool.SetQualitySettings(new JObject
            {
                ["asset_path"] = path,
                ["settings"] = new JObject
                {
                    ["shadow_distance"] = 100f,
                    ["srp_batcher"] = false
                }
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""), $"Unexpected response: {result}");
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            Assert.AreEqual(100f, asset.shadowDistance, 0.01f, "Shadow distance should be 100");
        }

        [Test]
        public void CreateHdrpAsset_ReturnsPackageNotInstalled()
        {
            // HDRP is not installed in the test project — expect package_not_installed error
            var result = RenderPipelineOpTool.CreateHdrpAsset(new JObject
            {
                ["asset_path"] = _tempDir + "/TestHDRP.asset"
            });
            // Either HDRP is installed (ok) or not (package_not_installed)
            Assert.That(result,
                Does.Contain("package_not_installed").Or.Contain("\"result\":\"ok\""),
                $"Unexpected response: {result}");
        }

        [Test]
        public void CreateUrpAsset_MissingAssetPath_ReturnsError()
        {
            var result = RenderPipelineOpTool.CreateUrpAsset(new JObject());
            Assert.That(result, Does.Contain("invalid_parameter"), $"Expected error, got: {result}");
        }

        [Test]
        public void SetQualitySettings_MissingSettings_ReturnsError()
        {
            var path = _tempDir + "/Phantom.asset";
            var result = RenderPipelineOpTool.SetQualitySettings(new JObject
            {
                ["asset_path"] = path
                // no settings key
            });
            Assert.That(result, Does.Contain("invalid_parameter"), $"Expected error, got: {result}");
        }
    }
#endif

    // Addressables tests are omitted — package not installed in test project.
    // Tests would go here wrapped in #if THEATRE_HAS_ADDRESSABLES.
}
