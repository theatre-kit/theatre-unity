using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Theatre.Editor.Tools.Director;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class AnimationClipOpTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = "Assets/_TheatreTest_Anim";
            if (!AssetDatabase.IsValidFolder(_tempDir))
                AssetDatabase.CreateFolder("Assets", "_TheatreTest_Anim");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_tempDir);
        }

        [Test]
        public void Create_ProducesAnimFile()
        {
            var path = _tempDir + "/TestClip.anim";
            var result = AnimationClipOpTool.Create(new JObject { ["asset_path"] = path });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AnimationClip>(path));
        }

        [Test]
        public void AddCurve_AddsCurveToClip()
        {
            var path = _tempDir + "/CurveClip.anim";
            AnimationClipOpTool.Create(new JObject { ["asset_path"] = path });
            var result = AnimationClipOpTool.AddCurve(new JObject
            {
                ["clip_path"] = path,
                ["property_name"] = "m_LocalPosition.x",
                ["type"] = "Transform",
                ["keyframes"] = new JArray
                {
                    new JObject { ["time"] = 0f, ["value"] = 0f },
                    new JObject { ["time"] = 1f, ["value"] = 5f }
                }
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
        }

        [Test]
        public void ListCurves_ReturnsBindings()
        {
            var path = _tempDir + "/ListClip.anim";
            AnimationClipOpTool.Create(new JObject { ["asset_path"] = path });
            AnimationClipOpTool.AddCurve(new JObject
            {
                ["clip_path"] = path,
                ["property_name"] = "m_LocalPosition.x",
                ["type"] = "Transform",
                ["keyframes"] = new JArray { new JObject { ["time"] = 0f, ["value"] = 0f } }
            });
            var result = AnimationClipOpTool.ListCurves(new JObject { ["clip_path"] = path });
            Assert.That(result, Does.Contain("\"curves\""));
            Assert.That(result, Does.Contain("m_LocalPosition.x"));
        }

        [Test]
        public void SetLoop_ConfiguresLoopTime()
        {
            var path = _tempDir + "/LoopClip.anim";
            AnimationClipOpTool.Create(new JObject { ["asset_path"] = path });
            var result = AnimationClipOpTool.SetLoop(new JObject
            {
                ["clip_path"] = path,
                ["loop_time"] = true
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
        }
    }

    [TestFixture]
    public class AnimatorControllerOpTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = "Assets/_TheatreTest_AnimCtrl";
            if (!AssetDatabase.IsValidFolder(_tempDir))
                AssetDatabase.CreateFolder("Assets", "_TheatreTest_AnimCtrl");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_tempDir);
        }

        [Test]
        public void Create_ProducesControllerFile()
        {
            var path = _tempDir + "/TestCtrl.controller";
            var result = AnimatorControllerOpTool.Create(new JObject { ["asset_path"] = path });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AnimatorController>(path));
        }

        [Test]
        public void AddParameter_AddsFloatParam()
        {
            var path = _tempDir + "/ParamCtrl.controller";
            AnimatorControllerOpTool.Create(new JObject { ["asset_path"] = path });
            var result = AnimatorControllerOpTool.AddParameter(new JObject
            {
                ["asset_path"] = path,
                ["name"] = "Speed",
                ["type"] = "float"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            Assert.AreEqual(1, ctrl.parameters.Length);
            Assert.AreEqual("Speed", ctrl.parameters[0].name);
        }

        [Test]
        public void AddState_AddsToStateMachine()
        {
            var path = _tempDir + "/StateCtrl.controller";
            AnimatorControllerOpTool.Create(new JObject { ["asset_path"] = path });
            var result = AnimatorControllerOpTool.AddState(new JObject
            {
                ["asset_path"] = path,
                ["name"] = "Walk"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
        }

        [Test]
        public void AddTransition_CreatesTransition()
        {
            var path = _tempDir + "/TransCtrl.controller";
            AnimatorControllerOpTool.Create(new JObject { ["asset_path"] = path });
            // Add two named states
            AnimatorControllerOpTool.AddState(new JObject { ["asset_path"] = path, ["name"] = "Idle" });
            AnimatorControllerOpTool.AddState(new JObject { ["asset_path"] = path, ["name"] = "Walk" });
            AnimatorControllerOpTool.AddParameter(new JObject
            {
                ["asset_path"] = path,
                ["name"] = "Speed",
                ["type"] = "float"
            });
            var result = AnimatorControllerOpTool.AddTransition(new JObject
            {
                ["asset_path"] = path,
                ["source_state"] = "Idle",
                ["destination_state"] = "Walk",
                ["has_exit_time"] = false,
                ["conditions"] = new JArray
                {
                    new JObject { ["parameter"] = "Speed", ["mode"] = "greater", ["threshold"] = 0.1 }
                }
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
        }

        [Test]
        public void ListStates_ReturnsStatesAndParams()
        {
            var path = _tempDir + "/ListCtrl.controller";
            AnimatorControllerOpTool.Create(new JObject { ["asset_path"] = path });
            AnimatorControllerOpTool.AddState(new JObject { ["asset_path"] = path, ["name"] = "Run" });
            var result = AnimatorControllerOpTool.ListStates(new JObject { ["asset_path"] = path });
            Assert.That(result, Does.Contain("\"states\""));
            Assert.That(result, Does.Contain("Run"));
        }
    }
}
