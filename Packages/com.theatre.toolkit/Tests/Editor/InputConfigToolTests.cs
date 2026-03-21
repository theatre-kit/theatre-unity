using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

#if THEATRE_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

using Theatre.Editor.Tools.Director;

namespace Theatre.Tests.Editor
{
#if THEATRE_HAS_INPUT_SYSTEM
    [TestFixture]
    public class InputActionOpTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = "Assets/_TheatreTest_Input";
            if (!AssetDatabase.IsValidFolder(_tempDir))
                AssetDatabase.CreateFolder("Assets", "_TheatreTest_Input");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_tempDir);
        }

        [Test]
        public void CreateAsset_ProducesInputActionsFile()
        {
            var path = _tempDir + "/Test.inputactions";
            var result = InputActionOpTool.CreateAsset(new JObject { ["asset_path"] = path });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.IsTrue(System.IO.File.Exists(path));
        }

        [Test]
        public void AddActionMap_AddsMap()
        {
            var path = _tempDir + "/MapTest.inputactions";
            InputActionOpTool.CreateAsset(new JObject { ["asset_path"] = path });
            var result = InputActionOpTool.AddActionMap(new JObject
            {
                ["asset_path"] = path,
                ["name"] = "Gameplay"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
        }

        [Test]
        public void AddAction_AddsActionToMap()
        {
            var path = _tempDir + "/ActionTest.inputactions";
            InputActionOpTool.CreateAsset(new JObject { ["asset_path"] = path });
            InputActionOpTool.AddActionMap(new JObject { ["asset_path"] = path, ["name"] = "Player" });
            var result = InputActionOpTool.AddAction(new JObject
            {
                ["asset_path"] = path,
                ["action_map"] = "Player",
                ["name"] = "Jump",
                ["type"] = "button"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
        }

        [Test]
        public void ListActions_ReturnsMapsAndActions()
        {
            var path = _tempDir + "/ListTest.inputactions";
            InputActionOpTool.CreateAsset(new JObject { ["asset_path"] = path });
            InputActionOpTool.AddActionMap(new JObject { ["asset_path"] = path, ["name"] = "UI" });
            var result = InputActionOpTool.ListActions(new JObject { ["asset_path"] = path });
            Assert.That(result, Does.Contain("\"maps\""));
            Assert.That(result, Does.Contain("UI"));
        }
    }
#endif

    [TestFixture]
    public class LightingOpTests
    {
        [Test]
        public void SetAmbient_ChangesColor()
        {
            var original = RenderSettings.ambientLight;
            try
            {
                var result = LightingOpTool.SetAmbient(new JObject
                {
                    ["mode"] = "color",
                    ["color"] = new JArray(1, 0, 0, 1)
                });
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
            }
            finally
            {
                RenderSettings.ambientLight = original;
            }
        }

        [Test]
        public void SetFog_EnablesFog()
        {
            var originalFog = RenderSettings.fog;
            try
            {
                var result = LightingOpTool.SetFog(new JObject
                {
                    ["enabled"] = true,
                    ["mode"] = "linear"
                });
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
                Assert.IsTrue(RenderSettings.fog);
            }
            finally
            {
                RenderSettings.fog = originalFog;
            }
        }
    }

    [TestFixture]
    public class QualityOpTests
    {
        [Test]
        public void ListLevels_ReturnsQualityNames()
        {
            var result = QualityOpTool.ListLevels(new JObject());
            Assert.That(result, Does.Contain("\"levels\""));
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
        }

        [Test]
        public void SetLevel_ChangesActiveLevel()
        {
            var original = QualitySettings.GetQualityLevel();
            try
            {
                var result = QualityOpTool.SetLevel(new JObject { ["level"] = 0 });
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
            }
            finally
            {
                QualitySettings.SetQualityLevel(original, true);
            }
        }
    }

    [TestFixture]
    public class ProjectSettingsOpTests
    {
        [Test]
        public void SetTime_ChangesFixedTimestep()
        {
            var original = Time.fixedDeltaTime;
            try
            {
                var result = ProjectSettingsOpTool.SetTime(new JObject { ["fixed_timestep"] = 0.01f });
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
                Assert.AreEqual(0.01f, Time.fixedDeltaTime, 0.001f);
            }
            finally
            {
                Time.fixedDeltaTime = original;
            }
        }

        [Test]
        public void SetPlayer_ChangesCompanyName()
        {
            var original = PlayerSettings.companyName;
            try
            {
                var result = ProjectSettingsOpTool.SetPlayer(new JObject { ["company_name"] = "TheatreTestCo" });
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
                Assert.AreEqual("TheatreTestCo", PlayerSettings.companyName);
            }
            finally
            {
                PlayerSettings.companyName = original;
            }
        }

        [Test]
        public void SetTagsAndLayers_AddsTags()
        {
            var result = ProjectSettingsOpTool.SetTagsAndLayers(new JObject
            {
                ["add_tags"] = new JArray("TheatreTestTag_" + Guid.NewGuid().ToString("N").Substring(0, 8))
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("\"added_tags\""));
        }
    }
}
