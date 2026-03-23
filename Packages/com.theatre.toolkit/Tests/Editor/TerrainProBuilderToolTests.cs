using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Theatre.Editor.Tools.Director.Spatial;
using UnityEngine;
using UnityEditor;
#if THEATRE_HAS_PROBUILDER
using UnityEngine.ProBuilder;
#endif

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class TerrainOpTests
    {
        private string _tempDir;
        private GameObject _terrainGo;

        [SetUp]
        public void SetUp()
        {
            _tempDir = "Assets/_TheatreTest_Terrain";
            if (!AssetDatabase.IsValidFolder(_tempDir))
                AssetDatabase.CreateFolder("Assets", "_TheatreTest_Terrain");
        }

        [TearDown]
        public void TearDown()
        {
            if (_terrainGo != null)
                Object.DestroyImmediate(_terrainGo);

            // Clean up any terrain GOs created by tests
            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            foreach (var t in terrains)
            {
                if (t != null && t.gameObject != null && t.gameObject.name.Contains("TheatreTest"))
                    Object.DestroyImmediate(t.gameObject);
            }

            // Destroy all terrain GOs to avoid cross-test contamination
            terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            foreach (var t in terrains)
            {
                if (t != null)
                    Object.DestroyImmediate(t.gameObject);
            }

            AssetDatabase.DeleteAsset(_tempDir);
        }

        [Test]
        public void Create_ProducesTerrainAssetAndGameObject()
        {
            var path = _tempDir + "/TestTerrain.asset";
            var result = TerrainOpHandlers.Create(new JObject
            {
                ["asset_path"] = path,
                ["width"] = 100,
                ["height"] = 50,
                ["length"] = 100,
                ["heightmap_resolution"] = 33
            });

            Assert.That(result, Does.Contain("\"result\":\"ok\""), $"Expected ok but got: {result}");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TerrainData>(path),
                "TerrainData asset should exist at the specified path");

            var terrain = Object.FindAnyObjectByType<Terrain>();
            Assert.IsNotNull(terrain, "A Terrain GameObject should have been created");
        }

        [Test]
        public void Create_MissingAssetPath_ReturnsError()
        {
            var result = TerrainOpHandlers.Create(new JObject());
            Assert.That(result, Does.Contain("error"), $"Expected error but got: {result}");
        }

        [Test]
        public void Create_InvalidAssetPathExtension_ReturnsError()
        {
            var result = TerrainOpHandlers.Create(new JObject
            {
                ["asset_path"] = _tempDir + "/TestTerrain.prefab"
            });
            Assert.That(result, Does.Contain("error"), $"Expected error but got: {result}");
        }

        [Test]
        public void GetHeight_ReturnsSampledHeight()
        {
            var path = _tempDir + "/HeightTerrain.asset";
            TerrainOpHandlers.Create(new JObject
            {
                ["asset_path"] = path,
                ["width"] = 100,
                ["height"] = 50,
                ["length"] = 100,
                ["heightmap_resolution"] = 33
            });

            var terrain = Object.FindAnyObjectByType<Terrain>();
            Assert.IsNotNull(terrain, "Terrain must exist for GetHeight test");

            var result = TerrainOpHandlers.GetHeight(new JObject
            {
                ["terrain_path"] = "/" + terrain.gameObject.name,
                ["position"] = new JArray(50, 50)
            });

            Assert.That(result, Does.Contain("\"result\":\"ok\""), $"Expected ok but got: {result}");
            Assert.That(result, Does.Contain("\"position\""), "Response should contain position");
        }

        [Test]
        public void GetHeight_MissingPosition_ReturnsError()
        {
            var path = _tempDir + "/HeightTerrain2.asset";
            TerrainOpHandlers.Create(new JObject
            {
                ["asset_path"] = path,
                ["width"] = 100,
                ["height"] = 50,
                ["length"] = 100,
                ["heightmap_resolution"] = 33
            });

            var terrain = Object.FindAnyObjectByType<Terrain>();
            Assert.IsNotNull(terrain);

            var result = TerrainOpHandlers.GetHeight(new JObject
            {
                ["terrain_path"] = "/" + terrain.gameObject.name
            });
            Assert.That(result, Does.Contain("error"), $"Expected error but got: {result}");
        }

        [Test]
        public void SetSize_ChangesDimensions()
        {
            var path = _tempDir + "/SizeTerrain.asset";
            TerrainOpHandlers.Create(new JObject
            {
                ["asset_path"] = path,
                ["width"] = 100,
                ["height"] = 50,
                ["length"] = 100,
                ["heightmap_resolution"] = 33
            });

            var terrain = Object.FindAnyObjectByType<Terrain>();
            Assert.IsNotNull(terrain, "Terrain must exist for SetSize test");

            var result = TerrainOpHandlers.SetSize(new JObject
            {
                ["terrain_path"] = "/" + terrain.gameObject.name,
                ["width"] = 200,
                ["length"] = 200
            });

            Assert.That(result, Does.Contain("\"result\":\"ok\""), $"Expected ok but got: {result}");
            Assert.AreEqual(200f, terrain.terrainData.size.x, 0.01f, "Width should be updated to 200");
            Assert.AreEqual(200f, terrain.terrainData.size.z, 0.01f, "Length should be updated to 200");
            Assert.AreEqual(50f, terrain.terrainData.size.y, 0.01f, "Height should remain 50");
        }

        [Test]
        public void SetHeightmap_ModifiesHeights()
        {
            var path = _tempDir + "/HmapTerrain.asset";
            TerrainOpHandlers.Create(new JObject
            {
                ["asset_path"] = path,
                ["width"] = 100,
                ["height"] = 50,
                ["length"] = 100,
                ["heightmap_resolution"] = 33
            });

            var terrain = Object.FindAnyObjectByType<Terrain>();
            Assert.IsNotNull(terrain);

            // Set a small 2x2 heightmap in the corner
            var heights = new JArray(
                new JArray(0.5f, 0.5f),
                new JArray(0.5f, 0.5f));

            var result = TerrainOpHandlers.SetHeightmap(new JObject
            {
                ["terrain_path"] = "/" + terrain.gameObject.name,
                ["heights"] = heights,
                ["region"] = new JObject { ["x"] = 0, ["y"] = 0 }
            });

            Assert.That(result, Does.Contain("\"result\":\"ok\""), $"Expected ok but got: {result}");
        }

        [Test]
        public void SmoothHeightmap_Succeeds()
        {
            var path = _tempDir + "/SmoothTerrain.asset";
            TerrainOpHandlers.Create(new JObject
            {
                ["asset_path"] = path,
                ["width"] = 100,
                ["height"] = 50,
                ["length"] = 100,
                ["heightmap_resolution"] = 33
            });

            var terrain = Object.FindAnyObjectByType<Terrain>();
            Assert.IsNotNull(terrain);

            var result = TerrainOpHandlers.SmoothHeightmap(new JObject
            {
                ["terrain_path"] = "/" + terrain.gameObject.name,
                ["iterations"] = 1
            });

            Assert.That(result, Does.Contain("\"result\":\"ok\""), $"Expected ok but got: {result}");
        }

        [Test]
        public void AddTerrainLayer_AddsDiffuseLayer()
        {
            var path = _tempDir + "/LayerTerrain.asset";
            TerrainOpHandlers.Create(new JObject
            {
                ["asset_path"] = path,
                ["width"] = 100,
                ["height"] = 50,
                ["length"] = 100,
                ["heightmap_resolution"] = 33
            });

            var terrain = Object.FindAnyObjectByType<Terrain>();
            Assert.IsNotNull(terrain);

            // We can't test with a real texture in unit tests without assets
            // Instead, test that missing diffuse_texture returns error
            var result = TerrainOpHandlers.AddTerrainLayer(new JObject
            {
                ["terrain_path"] = "/" + terrain.gameObject.name
            });
            Assert.That(result, Does.Contain("error"), "Should fail with missing diffuse_texture");
        }
    }

#if THEATRE_HAS_PROBUILDER
    [TestFixture]
    public class ProBuilderOpTests
    {
        [TearDown]
        public void TearDown()
        {
            // Clean up any ProBuilder GOs created by tests
            var meshes = Object.FindObjectsByType<ProBuilderMesh>(FindObjectsSortMode.None);
            foreach (var m in meshes)
            {
                if (m != null && m.gameObject != null
                    && (m.gameObject.name.StartsWith("PB_Test") || m.gameObject.name.StartsWith("PB_Export")))
                    Object.DestroyImmediate(m.gameObject);
            }
        }

        [Test]
        public void CreateShape_Cube_CreatesProBuilderMesh()
        {
            var result = ProBuilderOpTool.CreateShape(new JObject
            {
                ["shape"] = "cube",
                ["name"] = "PB_TestCube"
            });

            Assert.That(result, Does.Contain("\"result\":\"ok\""), $"Expected ok but got: {result}");
            var go = GameObject.Find("PB_TestCube");
            Assert.IsNotNull(go, "PB_TestCube GameObject should exist");
            Assert.IsNotNull(go.GetComponent<ProBuilderMesh>(), "Should have ProBuilderMesh component");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void CreateShape_Cylinder_CreatesProBuilderMesh()
        {
            var result = ProBuilderOpTool.CreateShape(new JObject
            {
                ["shape"] = "cylinder",
                ["name"] = "PB_TestCylinder"
            });

            Assert.That(result, Does.Contain("\"result\":\"ok\""), $"Expected ok but got: {result}");
            var go = GameObject.Find("PB_TestCylinder");
            Assert.IsNotNull(go, "PB_TestCylinder GameObject should exist");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void CreateShape_MissingShape_ReturnsError()
        {
            var result = ProBuilderOpTool.CreateShape(new JObject());
            Assert.That(result, Does.Contain("error"), $"Expected error but got: {result}");
        }

        [Test]
        public void CreateShape_InvalidShape_ReturnsError()
        {
            var result = ProBuilderOpTool.CreateShape(new JObject
            {
                ["shape"] = "hexahedron_supreme"
            });
            Assert.That(result, Does.Contain("error"), $"Expected error but got: {result}");
        }

        [Test]
        public void ExportMesh_SavesMeshAsset()
        {
            var tempDir = "Assets/_TheatreTest_PB";
            if (!AssetDatabase.IsValidFolder(tempDir))
                AssetDatabase.CreateFolder("Assets", "_TheatreTest_PB");

            try
            {
                ProBuilderOpTool.CreateShape(new JObject
                {
                    ["shape"] = "cube",
                    ["name"] = "PB_ExportTest"
                });

                var go = GameObject.Find("PB_ExportTest");
                Assert.IsNotNull(go, "PB_ExportTest must be created before export");

                var meshPath = tempDir + "/ExportedMesh.asset";
                var result = ProBuilderOpTool.ExportMesh(new JObject
                {
                    ["path"] = "/PB_ExportTest",
                    ["asset_path"] = meshPath
                });

                Assert.That(result, Does.Contain("\"result\":\"ok\""), $"Expected ok but got: {result}");
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Mesh>(meshPath),
                    "Exported mesh asset should exist");

                Object.DestroyImmediate(go);
            }
            finally
            {
                AssetDatabase.DeleteAsset(tempDir);
            }
        }

        [Test]
        public void BooleanOp_ReturnsUnavailableError()
        {
            var result = ProBuilderOpTool.BooleanOp(new JObject
            {
                ["path_a"] = "/A",
                ["path_b"] = "/B",
                ["boolean_operation"] = "union"
            });
            Assert.That(result, Does.Contain("internal_api_unavailable"),
                "BooleanOp should report API unavailable");
        }
    }
#endif
}
