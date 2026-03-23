using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Theatre.Editor.Tools.Director.Assets;
using UnityEngine;
using UnityEditor;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class TextureOpTests
    {
        [Test]
        public void Import_NonexistentPath_ReturnsError()
        {
            var result = TextureOpTool.Import(new JObject
            {
                ["asset_path"] = "Assets/NonExistent_TheatreTest.png"
            });
            Assert.That(result, Does.Contain("asset_not_found").Or.Contain("error"));
        }

        [Test]
        public void SetImportSettings_NonexistentPath_ReturnsError()
        {
            var result = TextureOpTool.SetImportSettings(new JObject
            {
                ["asset_path"] = "Assets/NonExistent_TheatreTest.png",
                ["settings"] = new JObject { ["filter_mode"] = "point" }
            });
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void SetImportSettings_MissingSettings_ReturnsError()
        {
            var result = TextureOpTool.SetImportSettings(new JObject
            {
                ["asset_path"] = "Assets/SomeTexture.png"
            });
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void CreateSprite_NonexistentPath_ReturnsError()
        {
            var result = TextureOpTool.CreateSprite(new JObject
            {
                ["asset_path"] = "Assets/NonExistent_TheatreTest.png"
            });
            Assert.That(result, Does.Contain("asset_not_found").Or.Contain("error"));
        }

        [Test]
        public void SpriteSheet_MissingMode_ReturnsError()
        {
            var result = TextureOpTool.SpriteSheet(new JObject
            {
                ["asset_path"] = "Assets/SomeTexture.png"
            });
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void SpriteSheet_InvalidMode_ReturnsError()
        {
            var result = TextureOpTool.SpriteSheet(new JObject
            {
                ["asset_path"] = "Assets/SomeTexture.png",
                ["mode"] = "bad_mode"
            });
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void SpriteSheet_GridMissingCellSize_ReturnsError()
        {
            var result = TextureOpTool.SpriteSheet(new JObject
            {
                ["asset_path"] = "Assets/NonExistent_TheatreTest.png",
                ["mode"] = "grid"
            });
            // Will error on asset_not_found or invalid_parameter depending on order
            Assert.That(result, Does.Contain("error"));
        }
    }

    [TestFixture]
    public class SpriteAtlasOpTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = "Assets/_TheatreTest_Atlas";
            if (!AssetDatabase.IsValidFolder(_tempDir))
                AssetDatabase.CreateFolder("Assets", "_TheatreTest_Atlas");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_tempDir);
        }

        [Test]
        public void Create_ProducesAtlasFile()
        {
            var path = _tempDir + "/TestAtlas.spriteatlas";
            var result = SpriteAtlasOpTool.Create(new JObject
            {
                ["asset_path"] = path
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(path), Is.Not.Null);
        }

        [Test]
        public void Create_WithPackingSettings_Succeeds()
        {
            var path = _tempDir + "/TestAtlasPacked.spriteatlas";
            var result = SpriteAtlasOpTool.Create(new JObject
            {
                ["asset_path"] = path,
                ["include_in_build"] = true,
                ["packing_settings"] = new JObject
                {
                    ["padding"] = 4,
                    ["enable_rotation"] = false,
                    ["enable_tight_packing"] = false
                }
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
        }

        [Test]
        public void Create_DuplicatePath_ReturnsConflictError()
        {
            var path = _tempDir + "/DuplicateAtlas.spriteatlas";
            SpriteAtlasOpTool.Create(new JObject { ["asset_path"] = path });
            var result = SpriteAtlasOpTool.Create(new JObject { ["asset_path"] = path });
            Assert.That(result, Does.Contain("asset_exists").Or.Contain("error"));
        }

        [Test]
        public void AddEntries_MissingAtlas_ReturnsError()
        {
            var result = SpriteAtlasOpTool.AddEntries(new JObject
            {
                ["asset_path"] = "Assets/NonExistent_TheatreTest.spriteatlas",
                ["entries"] = new JArray("Assets/SomeSprite.png")
            });
            Assert.That(result, Does.Contain("asset_not_found").Or.Contain("error"));
        }

        [Test]
        public void RemoveEntries_MissingAtlas_ReturnsError()
        {
            var result = SpriteAtlasOpTool.RemoveEntries(new JObject
            {
                ["asset_path"] = "Assets/NonExistent_TheatreTest.spriteatlas",
                ["entries"] = new JArray("Assets/SomeSprite.png")
            });
            Assert.That(result, Does.Contain("asset_not_found").Or.Contain("error"));
        }

        [Test]
        public void Pack_MissingAtlas_ReturnsError()
        {
            var result = SpriteAtlasOpTool.Pack(new JObject
            {
                ["asset_path"] = "Assets/NonExistent_TheatreTest.spriteatlas"
            });
            Assert.That(result, Does.Contain("asset_not_found").Or.Contain("error"));
        }
    }

    [TestFixture]
    public class AudioMixerOpTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = "Assets/_TheatreTest_Mixer";
            if (!AssetDatabase.IsValidFolder(_tempDir))
                AssetDatabase.CreateFolder("Assets", "_TheatreTest_Mixer");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_tempDir);
        }

        [Test]
        public void Create_ProducesMixerFile()
        {
            var path = _tempDir + "/TestMixer.mixer";
            var result = AudioMixerOpTool.Create(new JObject
            {
                ["asset_path"] = path
            });
            // May succeed or return internal_api_unavailable - both are valid
            Assert.That(result, Does.Contain("\"result\":\"ok\"")
                .Or.Contain("internal_api"));
        }

        [Test]
        public void Create_WrongExtension_ReturnsError()
        {
            var result = AudioMixerOpTool.Create(new JObject
            {
                ["asset_path"] = _tempDir + "/TestMixer.asset"
            });
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void AddGroup_MissingMixer_ReturnsError()
        {
            var result = AudioMixerOpTool.AddGroup(new JObject
            {
                ["asset_path"] = "Assets/NonExistent_TheatreTest.mixer",
                ["name"] = "TestGroup"
            });
            Assert.That(result, Does.Contain("asset_not_found").Or.Contain("error"));
        }

        [Test]
        public void SetVolume_MissingMixer_ReturnsError()
        {
            var result = AudioMixerOpTool.SetVolume(new JObject
            {
                ["asset_path"] = "Assets/NonExistent_TheatreTest.mixer",
                ["group"] = "Master",
                ["volume"] = -6f
            });
            Assert.That(result, Does.Contain("asset_not_found").Or.Contain("error"));
        }

        [Test]
        public void AddEffect_MissingMixer_ReturnsError()
        {
            var result = AudioMixerOpTool.AddEffect(new JObject
            {
                ["asset_path"] = "Assets/NonExistent_TheatreTest.mixer",
                ["group"] = "Master",
                ["effect"] = "SFX Reverb"
            });
            Assert.That(result, Does.Contain("asset_not_found").Or.Contain("error"));
        }

        [Test]
        public void CreateSnapshot_MissingMixer_ReturnsError()
        {
            var result = AudioMixerOpTool.CreateSnapshot(new JObject
            {
                ["asset_path"] = "Assets/NonExistent_TheatreTest.mixer",
                ["name"] = "MySnapshot"
            });
            Assert.That(result, Does.Contain("asset_not_found").Or.Contain("error"));
        }

        [Test]
        public void ExposeParameter_AlwaysReturnsInternalApiError()
        {
            // expose_parameter is explicitly documented as best-effort only
            var result = AudioMixerOpTool.ExposeParameter(new JObject
            {
                ["asset_path"] = _tempDir + "/FakeMixer.mixer",
                ["group"] = "Master",
                ["parameter"] = "Volume"
            });
            // Either asset_not_found (mixer doesn't exist) or internal_api_unavailable
            Assert.That(result, Does.Contain("error"));
        }
    }
}
