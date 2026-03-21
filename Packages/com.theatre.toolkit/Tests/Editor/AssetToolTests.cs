using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Theatre.Editor.Tools.Director;
using UnityEditor;
using UnityEngine;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class MaterialOpTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = "Assets/_TheatreTest_Mat";
            if (!AssetDatabase.IsValidFolder(_tempDir))
                AssetDatabase.CreateFolder("Assets", "_TheatreTest_Mat");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_tempDir);
        }

        [Test]
        public void Create_WithShader_CreatesFile()
        {
            var path = _tempDir + "/TestMat.mat";
            var result = MaterialOpTool.Create(new JObject
            {
                ["asset_path"] = path,
                ["shader"] = "Standard"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Material>(path));
        }

        [Test]
        public void Create_UnknownShader_ReturnsError()
        {
            var result = MaterialOpTool.Create(new JObject
            {
                ["asset_path"] = _tempDir + "/Bad.mat",
                ["shader"] = "NonExistent/Shader/XYZ"
            });
            Assert.That(result, Does.Contain("shader_not_found"));
        }

        [Test]
        public void SetProperties_ModifiesMaterial()
        {
            var path = _tempDir + "/SetPropsMat.mat";
            MaterialOpTool.Create(new JObject { ["asset_path"] = path, ["shader"] = "Standard" });

            var result = MaterialOpTool.SetProperties(new JObject
            {
                ["asset_path"] = path,
                ["properties"] = new JObject
                {
                    ["_Metallic"] = 0.5f
                }
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("\"properties_set\""));
        }

        [Test]
        public void SetShader_ChangesShader()
        {
            var path = _tempDir + "/SetShaderMat.mat";
            MaterialOpTool.Create(new JObject { ["asset_path"] = path, ["shader"] = "Standard" });

            var result = MaterialOpTool.SetShader(new JObject
            {
                ["asset_path"] = path,
                ["shader"] = "Standard"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("\"old_shader\""));
            Assert.That(result, Does.Contain("\"shader\""));
        }

        [Test]
        public void ListProperties_ReturnsProps()
        {
            var path = _tempDir + "/ListTest.mat";
            MaterialOpTool.Create(new JObject { ["asset_path"] = path, ["shader"] = "Standard" });
            var result = MaterialOpTool.ListProperties(new JObject { ["asset_path"] = path });
            Assert.That(result, Does.Contain("\"properties\""));
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
        }

        [Test]
        public void DryRun_DoesNotCreateFile()
        {
            var path = _tempDir + "/DryRunMat.mat";
            var result = MaterialOpTool.Create(new JObject
            {
                ["asset_path"] = path,
                ["shader"] = "Standard",
                ["dry_run"] = true
            });
            Assert.That(result, Does.Contain("\"dry_run\":true"));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Material>(path));
        }
    }

    [TestFixture]
    public class ScriptableObjectOpTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = "Assets/_TheatreTest_SO";
            if (!AssetDatabase.IsValidFolder(_tempDir))
                AssetDatabase.CreateFolder("Assets", "_TheatreTest_SO");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_tempDir);
        }

        [Test]
        public void FindByType_FindsAssets()
        {
            var result = ScriptableObjectOpTool.FindByType(new JObject { ["type"] = "ScriptableObject" });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("\"assets\""));
        }

        [Test]
        public void Create_UnknownType_ReturnsError()
        {
            var result = ScriptableObjectOpTool.Create(new JObject
            {
                ["type"] = "FakeSOType999",
                ["asset_path"] = _tempDir + "/Fake.asset"
            });
            Assert.That(result, Does.Contain("type_not_found").Or.Contain("error"));
        }

        [Test]
        public void ListFields_NotFound_ReturnsError()
        {
            var result = ScriptableObjectOpTool.ListFields(new JObject
            {
                ["asset_path"] = _tempDir + "/NonExistent.asset"
            });
            Assert.That(result, Does.Contain("asset_not_found"));
        }

        [Test]
        public void ListFields_OnExistingAsset_ReturnsFields()
        {
            // Find an existing ScriptableObject asset in the project
            var guids = AssetDatabase.FindAssets("t:ScriptableObject");
            if (guids.Length == 0)
                Assert.Ignore("No ScriptableObject assets found in project — skipping");

            var assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var result = ScriptableObjectOpTool.ListFields(new JObject
            {
                ["asset_path"] = assetPath
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("\"fields\":"));
            Assert.That(result, Does.Contain("\"type\":"));
        }
    }

    [TestFixture]
    public class PhysicsMaterialOpTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = "Assets/_TheatreTest_PhysMat";
            if (!AssetDatabase.IsValidFolder(_tempDir))
                AssetDatabase.CreateFolder("Assets", "_TheatreTest_PhysMat");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_tempDir);
        }

        [Test]
        public void Create3D_CreatesPhysicMaterial()
        {
            var path = _tempDir + "/Test3D.physicMaterial";
            var result = PhysicsMaterialOpTool.Create(new JObject
            {
                ["asset_path"] = path,
                ["physics"] = "3d",
                ["friction"] = 0.5,
                ["bounciness"] = 0.8
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("\"physics\":\"3d\""));
        }

        [Test]
        public void Create2D_CreatesPhysicsMaterial2D()
        {
            var path = _tempDir + "/Test2D.physicsMaterial2D";
            var result = PhysicsMaterialOpTool.Create(new JObject
            {
                ["asset_path"] = path,
                ["physics"] = "2d",
                ["friction"] = 0.3,
                ["bounciness"] = 0.6
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("\"physics\":\"2d\""));
        }

        [Test]
        public void SetProperties_NotFound_ReturnsError()
        {
            var result = PhysicsMaterialOpTool.SetProperties(new JObject
            {
                ["asset_path"] = _tempDir + "/NonExistent.physicMaterial"
            });
            Assert.That(result, Does.Contain("asset_not_found"));
        }
    }
}
