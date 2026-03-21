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

        [Test]
        public void RemoveCurve_RemovesCurve()
        {
            var path = _tempDir + "/RemoveCurveClip.anim";
            AnimationClipOpTool.Create(new JObject { ["asset_path"] = path });
            // Add a curve first
            AnimationClipOpTool.AddCurve(new JObject
            {
                ["clip_path"] = path,
                ["property_name"] = "m_LocalPosition.x",
                ["type"] = "Transform",
                ["keyframes"] = new JArray
                {
                    new JObject { ["time"] = 0f, ["value"] = 0f },
                    new JObject { ["time"] = 1f, ["value"] = 1f }
                }
            });

            // Verify it exists
            var beforeResult = AnimationClipOpTool.ListCurves(new JObject { ["clip_path"] = path });
            Assert.That(beforeResult, Does.Contain("m_LocalPosition.x"));

            // Remove it
            var result = AnimationClipOpTool.RemoveCurve(new JObject
            {
                ["clip_path"] = path,
                ["property_name"] = "m_LocalPosition.x",
                ["type"] = "Transform"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));

            // Verify it's gone
            var afterResult = AnimationClipOpTool.ListCurves(new JObject { ["clip_path"] = path });
            Assert.That(afterResult, Does.Not.Contain("m_LocalPosition.x"));
        }

        [Test]
        public void SetKeyframe_AddsToExistingCurve()
        {
            var path = _tempDir + "/SetKeyframeClip.anim";
            AnimationClipOpTool.Create(new JObject { ["asset_path"] = path });
            // Create a curve with 2 keyframes
            AnimationClipOpTool.AddCurve(new JObject
            {
                ["clip_path"] = path,
                ["property_name"] = "m_LocalPosition.y",
                ["type"] = "Transform",
                ["keyframes"] = new JArray
                {
                    new JObject { ["time"] = 0f, ["value"] = 0f },
                    new JObject { ["time"] = 2f, ["value"] = 10f }
                }
            });

            // Add a 3rd keyframe via set_keyframe
            var result = AnimationClipOpTool.SetKeyframe(new JObject
            {
                ["clip_path"] = path,
                ["property_name"] = "m_LocalPosition.y",
                ["type"] = "Transform",
                ["time"] = 1f,
                ["value"] = 5f
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            // keyframe_count should be 3 (0, 1, 2 seconds)
            var parsed = JObject.Parse(result);
            Assert.GreaterOrEqual((int)parsed["keyframe_count"], 3);
        }

        [Test]
        public void SetEvents_AddsAnimationEvents()
        {
            var path = _tempDir + "/EventsClip.anim";
            AnimationClipOpTool.Create(new JObject { ["asset_path"] = path });

            var result = AnimationClipOpTool.SetEvents(new JObject
            {
                ["clip_path"] = path,
                ["events"] = new JArray
                {
                    new JObject { ["time"] = 0.5f, ["function"] = "OnFire" },
                    new JObject { ["time"] = 1.0f, ["function"] = "OnReload", ["int_param"] = 3 }
                }
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            var parsed = JObject.Parse(result);
            Assert.AreEqual(2, (int)parsed["event_count"]);
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

        [Test]
        public void SetStateClip_AssignsClipToState()
        {
            var ctrlPath = _tempDir + "/SetClipCtrl.controller";
            var clipPath = _tempDir + "/SetClipAnim.anim";
            AnimatorControllerOpTool.Create(new JObject { ["asset_path"] = ctrlPath });
            AnimatorControllerOpTool.AddState(new JObject { ["asset_path"] = ctrlPath, ["name"] = "Walk" });
            AnimationClipOpTool.Create(new JObject { ["asset_path"] = clipPath });

            var result = AnimatorControllerOpTool.SetStateClip(new JObject
            {
                ["asset_path"] = ctrlPath,
                ["state_name"] = "Walk",
                ["clip_path"] = clipPath
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));

            // Verify the state has the clip assigned
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            AnimatorState walkState = null;
            foreach (var cs in ctrl.layers[0].stateMachine.states)
            {
                if (cs.state.name == "Walk") { walkState = cs.state; break; }
            }
            Assert.IsNotNull(walkState);
            Assert.IsNotNull(walkState.motion, "State should have a clip assigned");
        }

        [Test]
        public void SetDefaultState_ChangesEntryState()
        {
            var path = _tempDir + "/DefaultStateCtrl.controller";
            AnimatorControllerOpTool.Create(new JObject { ["asset_path"] = path });
            AnimatorControllerOpTool.AddState(new JObject { ["asset_path"] = path, ["name"] = "Idle" });
            AnimatorControllerOpTool.AddState(new JObject { ["asset_path"] = path, ["name"] = "Walk" });

            var result = AnimatorControllerOpTool.SetDefaultState(new JObject
            {
                ["asset_path"] = path,
                ["state_name"] = "Walk"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));

            // Verify Walk is the default
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            var defaultState = ctrl.layers[0].stateMachine.defaultState;
            Assert.IsNotNull(defaultState);
            Assert.AreEqual("Walk", defaultState.name);
        }

        [Test]
        public void AddLayer_CreatesNewLayer()
        {
            var path = _tempDir + "/LayerCtrl.controller";
            AnimatorControllerOpTool.Create(new JObject { ["asset_path"] = path });

            // Newly created controller has 1 layer ("Base Layer")
            var ctrlBefore = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            Assert.AreEqual(1, ctrlBefore.layers.Length, "Should start with 1 layer");

            var result = AnimatorControllerOpTool.AddLayer(new JObject
            {
                ["asset_path"] = path,
                ["name"] = "UpperBody"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));

            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            Assert.AreEqual(2, ctrl.layers.Length, "Should now have 2 layers");
            Assert.AreEqual("UpperBody", ctrl.layers[1].name);
        }
    }
}
