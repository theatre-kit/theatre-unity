using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Theatre.Editor.Tools.Director;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEditor;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class TilemapOpTests
    {
        private GameObject _gridGo;
        private Tilemap _tilemap;

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject("TestGrid_Tilemap");
            _gridGo.AddComponent<Grid>();
            var tilemapGo = new GameObject("TestTilemap");
            tilemapGo.transform.SetParent(_gridGo.transform);
            _tilemap = tilemapGo.AddComponent<Tilemap>();
            tilemapGo.AddComponent<TilemapRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gridGo);
        }

        [Test]
        public void SetTile_MissingTilemap_ReturnsError()
        {
            var result = TilemapOpTool.SetTile(new JObject
            {
                ["tilemap_path"] = "/NonExistent_Tilemap",
                ["position"] = new JArray(0, 0, 0),
                ["tile_asset"] = "Assets/SomeTile.asset"
            });
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void SetTile_MissingTileAsset_ReturnsError()
        {
            var result = TilemapOpTool.SetTile(new JObject
            {
                ["tilemap_path"] = "/TestGrid_Tilemap/TestTilemap",
                ["position"] = new JArray(0, 0, 0),
                ["tile_asset"] = "Assets/NonExistentTile.asset"
            });
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void Clear_ClearsAllTiles()
        {
            var result = TilemapOpTool.Clear(new JObject
            {
                ["tilemap_path"] = "/TestGrid_Tilemap/TestTilemap"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
        }

        [Test]
        public void GetTile_EmptyPosition_ReturnsNullTile()
        {
            var result = TilemapOpTool.GetTile(new JObject
            {
                ["tilemap_path"] = "/TestGrid_Tilemap/TestTilemap",
                ["position"] = new JArray(0, 0, 0)
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
        }

        [Test]
        public void GetUsedTiles_EmptyTilemap_ReturnsZeroCount()
        {
            var result = TilemapOpTool.GetUsedTiles(new JObject
            {
                ["tilemap_path"] = "/TestGrid_Tilemap/TestTilemap"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("\"count\":0"));
        }

        [Test]
        public void Clear_MissingTilemapPath_ReturnsError()
        {
            var result = TilemapOpTool.Clear(new JObject());
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void SetTilemapLayer_ValidTilemap_SetsOrder()
        {
            var result = TilemapOpTool.SetTilemapLayer(new JObject
            {
                ["tilemap_path"] = "/TestGrid_Tilemap/TestTilemap",
                ["sorting_order"] = 5
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("\"sorting_order\":5"));
        }

        [Test]
        public void BoxFill_MissingTileAsset_ReturnsError()
        {
            var result = TilemapOpTool.BoxFill(new JObject
            {
                ["tilemap_path"] = "/TestGrid_Tilemap/TestTilemap",
                ["tile_asset"] = "Assets/NonExistent.asset",
                ["start"] = new JArray(0, 0, 0),
                ["end"] = new JArray(2, 2, 0)
            });
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void FloodFill_MissingTilemap_ReturnsError()
        {
            var result = TilemapOpTool.FloodFill(new JObject
            {
                ["tilemap_path"] = "/NonExistent",
                ["tile_asset"] = "Assets/SomeTile.asset",
                ["position"] = new JArray(0, 0, 0)
            });
            Assert.That(result, Does.Contain("error"));
        }
    }

    [TestFixture]
    public class NavMeshOpTests
    {
        [Test]
        public void Bake_SavedScene_CompletesWithoutError()
        {
            // Bake requires a saved scene — the auto-generated TestScene_Hierarchy has a path
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                // Scene not saved — bake should return scene_not_saved error
                var result = NavMeshOpTool.Bake(new JObject());
                Assert.That(result, Does.Contain("scene_not_saved"));
            }
            else
            {
                var result = NavMeshOpTool.Bake(new JObject());
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
            }
        }

        [Test]
        public void Bake_UnsavedScene_ReturnsError()
        {
            // Create a new unsaved scene to test the error path
            var newScene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Additive);
            try
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(newScene);
                var result = NavMeshOpTool.Bake(new JObject());
                Assert.That(result, Does.Contain("scene_not_saved"));
            }
            finally
            {
                // Restore original scene and close the temp one
                var scenes = UnityEngine.SceneManagement.SceneManager.sceneCount;
                for (int i = 0; i < scenes; i++)
                {
                    var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    if (!string.IsNullOrEmpty(s.path))
                    {
                        UnityEngine.SceneManagement.SceneManager.SetActiveScene(s);
                        break;
                    }
                }
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(newScene, true);
            }
        }

        [Test]
        public void AddLink_CreatesOffMeshLink()
        {
            var result = NavMeshOpTool.AddLink(new JObject
            {
                ["start"] = new JArray(0, 0, 0),
                ["end"] = new JArray(10, 0, 0)
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            // Cleanup
            var link = GameObject.Find("OffMeshLink");
            if (link != null) Object.DestroyImmediate(link);
        }

        [Test]
        public void AddModifier_MissingPath_ReturnsError()
        {
            var result = NavMeshOpTool.AddModifier(new JObject());
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void AddSurface_MissingPath_ReturnsError()
        {
            var result = NavMeshOpTool.AddSurface(new JObject());
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void SetArea_InvalidIndex_ReturnsError()
        {
            var result = NavMeshOpTool.SetArea(new JObject
            {
                ["index"] = 99,
                ["name"] = "TestArea",
                ["cost"] = 1.0f
            });
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void AddLink_MissingEnd_ReturnsError()
        {
            var result = NavMeshOpTool.AddLink(new JObject
            {
                ["start"] = new JArray(0, 0, 0)
            });
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void SetAgentType_MissingId_ReturnsError()
        {
            var result = NavMeshOpTool.SetAgentType(new JObject());
            Assert.That(result, Does.Contain("error"));
        }
    }
}
