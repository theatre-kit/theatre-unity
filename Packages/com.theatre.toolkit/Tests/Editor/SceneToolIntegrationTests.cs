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
            if (!System.IO.File.Exists(TestScenePath))
            {
                Assert.Ignore(
                    $"Test scene not found: {TestScenePath}. "
                    + "Create it per Phase 2 design Unit 12 to enable these tests.");
            }
            EditorSceneManager.OpenScene(TestScenePath,
                OpenSceneMode.Single);
        }

        [Test]
        public void SceneSnapshot_ReturnsObjects()
        {
            var args = JToken.Parse(@"{ ""budget"": 4000 }");
            var result = CallTool("scene_snapshot", args);

            Assert.That(result, Does.Contain("\"scene\""));
            Assert.That(result, Does.Contain("\"objects\""));
            Assert.That(result, Does.Contain("Player"));
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

        [Test]
        public void SceneSnapshot_WithNonMatchingComponentFilter_ReturnsZeroObjects()
        {
            // include_components that matches nothing should return ok with 0 objects
            var args = JToken.Parse(@"{
                ""include_components"": [""NonExistentComponentXYZ""],
                ""budget"": 500
            }");
            var result = CallTool("scene_snapshot", args);

            Assert.That(result, Does.Contain("\"returned\":0").Or.Contain("\"returned\": 0"));
            // Should not be an error response
            Assert.That(result, Does.Not.Contain("\"error\""));
        }

        [Test]
        public void SceneHierarchy_PathOperation_ReturnsPathForKnownObject()
        {
            // First resolve /Player to get its instance_id
            var inspectArgs = JToken.Parse(@"{ ""path"": ""/Player"" }");
            var inspectResult = CallTool("scene_inspect", inspectArgs);
            var inspectObj = Newtonsoft.Json.Linq.JObject.Parse(inspectResult);
            var instanceId = inspectObj["instance_id"]?.Value<int>();
            Assert.IsNotNull(instanceId, "scene_inspect should return instance_id for /Player");

            // Now use scene_hierarchy:path to look up by instance_id
            var pathArgs = new Newtonsoft.Json.Linq.JObject
            {
                ["operation"] = "path",
                ["instance_id"] = instanceId
            };
            var result = CallTool("scene_hierarchy", pathArgs);

            Assert.That(result, Does.Contain("\"path\":\"/Player\"").Or.Contain("\"path\": \"/Player\""));
            Assert.That(result, Does.Contain("\"instance_id\""));
        }

        [Test]
        public void SceneHierarchy_PathOperation_UnknownInstanceId_ReturnsError()
        {
            var args = new Newtonsoft.Json.Linq.JObject
            {
                ["operation"] = "path",
                ["instance_id"] = -999999
            };
            var result = CallTool("scene_hierarchy", args);

            Assert.That(result, Does.Contain("\"error\""));
        }

        [Test]
        public void SceneHierarchy_PathOperation_MissingInstanceId_ReturnsError()
        {
            var args = JToken.Parse(@"{ ""operation"": ""path"" }");
            var result = CallTool("scene_hierarchy", args);

            Assert.That(result, Does.Contain("\"error\""));
            Assert.That(result, Does.Contain("instance_id"));
        }

        [Test]
        public void SceneHierarchy_SearchOperation_NoFilter_ReturnsError()
        {
            // search with no filter (no include_components, tag, or layer) should error
            var args = JToken.Parse(@"{ ""operation"": ""search"" }");
            var result = CallTool("scene_hierarchy", args);

            Assert.That(result, Does.Contain("\"error\""));
            Assert.That(result, Does.Contain("invalid_parameter"));
        }

        [Test]
        public void SceneInspect_WithSummaryDepth_ReturnsComponentsNoProperties()
        {
            var args = JToken.Parse(@"{ ""path"": ""/Player"", ""depth"": ""summary"" }");
            var result = CallTool("scene_inspect", args);

            Assert.That(result, Does.Contain("\"components\""));
            // summary depth should NOT include serialized properties
            Assert.That(result, Does.Not.Contain("\"properties\""));
        }

        [Test]
        public void SceneInspect_WithFullDepth_ReturnsComponentsWithProperties()
        {
            var args = JToken.Parse(@"{ ""path"": ""/Player"", ""depth"": ""full"" }");
            var result = CallTool("scene_inspect", args);

            Assert.That(result, Does.Contain("\"components\""));
            Assert.That(result, Does.Contain("\"properties\""));
        }

        [Test]
        public void SceneInspect_WithPropertiesDepth_ReturnsProperties()
        {
            var args = JToken.Parse(@"{ ""path"": ""/Player"", ""depth"": ""properties"" }");
            var result = CallTool("scene_inspect", args);

            Assert.That(result, Does.Contain("\"components\""));
            Assert.That(result, Does.Contain("\"properties\""));
        }

        [Test]
        public void SceneSnapshot_InvalidSceneName_ReturnsError()
        {
            var args = JToken.Parse(@"{ ""scene"": ""NonExistentSceneXYZ"" }");
            var result = CallTool("scene_snapshot", args);

            Assert.That(result, Does.Contain("\"error\""));
            Assert.That(result, Does.Contain("scene_not_loaded"));
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
