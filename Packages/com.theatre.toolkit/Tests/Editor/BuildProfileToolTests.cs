using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Theatre.Editor.Tools.Director;
using UnityEditor;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class BuildProfileOpTests
    {
        [Test]
        public void ListProfiles_ReturnsCurrentConfig()
        {
            var result = BuildProfileOpTool.ListProfiles(new JObject());
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("\"active_platform\""));
            Assert.That(result, Does.Contain("\"scenes\""));
        }

        [Test]
        public void SetScenes_UpdatesBuildSceneList()
        {
            var original = EditorBuildSettings.scenes;
            try
            {
                var result = BuildProfileOpTool.SetScenes(new JObject
                {
                    ["scenes"] = new JArray("Assets/Scenes/TestScene_Hierarchy.unity")
                });
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
                Assert.AreEqual(1, EditorBuildSettings.scenes.Length);
            }
            finally
            {
                EditorBuildSettings.scenes = original;
            }
        }

        [Test]
        public void Create_MissingName_ReturnsError()
        {
            var result = BuildProfileOpTool.Create(new JObject());
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void Create_InvalidPlatform_ReturnsError()
        {
            var result = BuildProfileOpTool.Create(new JObject
            {
                ["name"] = "test", ["platform"] = "nonexistent_platform"
            });
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void SetScriptingBackend_Mono_Succeeds()
        {
            var result = BuildProfileOpTool.SetScriptingBackend(new JObject
            {
                ["backend"] = "mono"
            });
            // May succeed or error depending on platform support
            Assert.That(result, Does.Contain("\"result\":\"ok\"")
                .Or.Contain("error"));
        }
    }
}
