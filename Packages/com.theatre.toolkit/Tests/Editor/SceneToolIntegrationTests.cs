using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Theatre.Editor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Theatre.Tests.Editor
{
    /// <summary>
    /// Integration tests for scene awareness tools.
    /// Requires TestScene_Hierarchy.unity to be set up per the design.
    /// </summary>
    [TestFixture]
    public class SceneToolIntegrationTests
    {
        private const string TestScenePath =
            "Assets/Scenes/TestScene_Hierarchy.unity";

        [OneTimeSetUp]
        public void LoadTestScene()
        {
            EditorSceneManager.OpenScene(TestScenePath,
                OpenSceneMode.Single);
        }

        [Test]
        public void SceneSnapshot_ReturnsObjects()
        {
            var args = JToken.Parse(@"{ ""budget"": 2000 }");
            var result = CallTool("scene_snapshot", args);

            Assert.That(result, Does.Contain("\"scene\""));
            Assert.That(result, Does.Contain("\"objects\""));
            Assert.That(result, Does.Contain("\"Player\""));
            Assert.That(result, Does.Contain("\"frame\""));
            Assert.That(result, Does.Contain("\"budget\""));
        }

        [Test]
        public void SceneSnapshot_RespectsRadius()
        {
            // Player is at origin. Scouts are at ~[11, 0, 5]. Radius 3 should exclude scouts.
            var args = JToken.Parse(@"{ ""focus"": [0, 0, 0], ""radius"": 3, ""budget"": 2000 }");
            var result = CallTool("scene_snapshot", args);

            Assert.That(result, Does.Not.Contain("\"Scout_01\""));
        }

        [Test]
        public void SceneSnapshot_ClustersEnemies()
        {
            var args = JToken.Parse(@"{ ""budget"": 2000 }");
            var result = CallTool("scene_snapshot", args);

            // Should have cluster summaries
            Assert.That(result, Does.Contain("\"groups\""));
        }

        [Test]
        public void SceneHierarchy_ListRoots()
        {
            var args = JToken.Parse(@"{ ""operation"": ""list"" }");
            var result = CallTool("scene_hierarchy", args);

            Assert.That(result, Does.Contain("\"results\""));
            Assert.That(result, Does.Contain("\"TestScene_Hierarchy\""));
        }

        [Test]
        public void SceneHierarchy_ListChildren()
        {
            var args = JToken.Parse(@"{ ""operation"": ""list"", ""path"": ""/Enemies"" }");
            var result = CallTool("scene_hierarchy", args);

            Assert.That(result, Does.Contain("\"Scout_01\""));
            Assert.That(result, Does.Contain("\"Heavy_01\""));
        }

        [Test]
        public void SceneHierarchy_FindByPattern()
        {
            var args = JToken.Parse(@"{ ""operation"": ""find"", ""pattern"": ""Scout*"" }");
            var result = CallTool("scene_hierarchy", args);

            Assert.That(result, Does.Contain("\"Scout_01\""));
            Assert.That(result, Does.Contain("\"Scout_02\""));
            Assert.That(result, Does.Contain("\"Scout_03\""));
            Assert.That(result, Does.Not.Contain("\"Heavy_01\""));
        }

        [Test]
        public void SceneHierarchy_SearchByTag()
        {
            var args = JToken.Parse(@"{ ""operation"": ""search"", ""tag"": ""Player"" }");
            var result = CallTool("scene_hierarchy", args);

            Assert.That(result, Does.Contain("\"Player\""));
            Assert.That(result, Does.Not.Contain("\"Scout\""));
        }

        [Test]
        public void SceneInspect_ByPath()
        {
            var args = JToken.Parse(@"{ ""path"": ""/Player"", ""depth"": ""full"" }");
            var result = CallTool("scene_inspect", args);

            Assert.That(result, Does.Contain("\"path\":\"/Player\""));
            Assert.That(result, Does.Contain("\"instance_id\""));
            Assert.That(result, Does.Contain("\"tag\":\"Player\""));
            Assert.That(result, Does.Contain("\"components\""));
            Assert.That(result, Does.Contain("\"children\""));
        }

        [Test]
        public void SceneInspect_NotFound()
        {
            var args = JToken.Parse(@"{ ""path"": ""/DoesNotExist"" }");
            var result = CallTool("scene_inspect", args);

            Assert.That(result, Does.Contain("\"error\""));
            Assert.That(result, Does.Contain("\"gameobject_not_found\""));
            Assert.That(result, Does.Contain("\"suggestion\""));
        }

        [Test]
        public void SceneInspect_ComponentFilter()
        {
            var args = JToken.Parse(
                @"{ ""path"": ""/Player/Camera"", ""components"": [""Camera""], ""depth"": ""full"" }");
            var result = CallTool("scene_inspect", args);

            Assert.That(result, Does.Contain("\"Camera\""));
            // Transform should be filtered out
            Assert.That(result, Does.Not.Contain("\"type\":\"Transform\""));
        }

        // --- Helper ---

        private string CallTool(string toolName, JToken args)
        {
            var tool = TheatreServer.ToolRegistry?.GetTool(
                toolName,
                ToolGroup.Everything);
            Assert.IsNotNull(tool,
                $"Tool '{toolName}' not found in registry");
            return tool.Handler(args);
        }
    }
}
