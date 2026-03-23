using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Theatre.Editor.Tools.Director.Animation;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class BlendTreeOpTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = "Assets/_TheatreTest_BlendTree";
            if (!AssetDatabase.IsValidFolder(_tempDir))
                AssetDatabase.CreateFolder("Assets", "_TheatreTest_BlendTree");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_tempDir);
        }

        [Test]
        public void Create_ReplacesStateMotionWithBlendTree()
        {
            // Create controller with a state first
            var ctrlPath = _tempDir + "/BT_Ctrl.controller";
            AnimatorControllerOpHandlers.Create(new JObject { ["asset_path"] = ctrlPath });
            AnimatorControllerOpHandlers.AddState(new JObject { ["asset_path"] = ctrlPath, ["name"] = "Blend" });
            AnimatorControllerOpHandlers.AddParameter(new JObject { ["asset_path"] = ctrlPath, ["name"] = "Speed", ["type"] = "float" });

            var result = BlendTreeOpTool.Create(new JObject
            {
                ["controller_path"] = ctrlPath,
                ["state_name"] = "Blend",
                ["blend_type"] = "1d",
                ["parameter"] = "Speed"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));

            // Verify the state has a blend tree motion
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            Assert.IsNotNull(controller);
            var state = controller.layers[0].stateMachine.states;
            AnimatorState blendState = null;
            foreach (var cs in state)
            {
                if (cs.state.name == "Blend") { blendState = cs.state; break; }
            }
            Assert.IsNotNull(blendState);
            Assert.IsInstanceOf<BlendTree>(blendState.motion);
        }

        [Test]
        public void AddMotion_AddsClipToBlendTree()
        {
            var ctrlPath = _tempDir + "/BT_Motion.controller";
            var clipPath = _tempDir + "/BT_Clip.anim";
            AnimatorControllerOpHandlers.Create(new JObject { ["asset_path"] = ctrlPath });
            AnimatorControllerOpHandlers.AddState(new JObject { ["asset_path"] = ctrlPath, ["name"] = "Blend" });
            AnimatorControllerOpHandlers.AddParameter(new JObject { ["asset_path"] = ctrlPath, ["name"] = "Speed", ["type"] = "float" });
            BlendTreeOpTool.Create(new JObject
            {
                ["controller_path"] = ctrlPath,
                ["state_name"] = "Blend",
                ["blend_type"] = "1d",
                ["parameter"] = "Speed"
            });
            AnimationClipOpTool.Create(new JObject { ["asset_path"] = clipPath });

            var result = BlendTreeOpTool.AddMotion(new JObject
            {
                ["controller_path"] = ctrlPath,
                ["state_name"] = "Blend",
                ["clip_path"] = clipPath,
                ["threshold"] = 0.5
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("\"child_count\":1"));
        }

        [Test]
        public void SetBlendType_ChangesType()
        {
            var ctrlPath = _tempDir + "/BT_Type.controller";
            AnimatorControllerOpHandlers.Create(new JObject { ["asset_path"] = ctrlPath });
            AnimatorControllerOpHandlers.AddState(new JObject { ["asset_path"] = ctrlPath, ["name"] = "Walk" });
            BlendTreeOpTool.Create(new JObject
            {
                ["controller_path"] = ctrlPath,
                ["state_name"] = "Walk",
                ["blend_type"] = "1d"
            });

            var result = BlendTreeOpTool.SetBlendType(new JObject
            {
                ["controller_path"] = ctrlPath,
                ["state_name"] = "Walk",
                ["blend_type"] = "2d_freeform_cartesian"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));

            // Verify
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            AnimatorState walkState = null;
            foreach (var cs in controller.layers[0].stateMachine.states)
            {
                if (cs.state.name == "Walk") { walkState = cs.state; break; }
            }
            Assert.IsNotNull(walkState);
            var tree = walkState.motion as BlendTree;
            Assert.IsNotNull(tree);
            Assert.AreEqual(BlendTreeType.FreeformCartesian2D, tree.blendType);
        }

        [Test]
        public void SetParameter_ChangesBlendParam()
        {
            var ctrlPath = _tempDir + "/BT_Param.controller";
            AnimatorControllerOpHandlers.Create(new JObject { ["asset_path"] = ctrlPath });
            AnimatorControllerOpHandlers.AddState(new JObject { ["asset_path"] = ctrlPath, ["name"] = "Move" });
            AnimatorControllerOpHandlers.AddParameter(new JObject { ["asset_path"] = ctrlPath, ["name"] = "Speed", ["type"] = "float" });
            AnimatorControllerOpHandlers.AddParameter(new JObject { ["asset_path"] = ctrlPath, ["name"] = "Dir", ["type"] = "float" });
            BlendTreeOpTool.Create(new JObject
            {
                ["controller_path"] = ctrlPath,
                ["state_name"] = "Move",
                ["blend_type"] = "1d",
                ["parameter"] = "Speed"
            });

            // Change the blend parameter
            var result = BlendTreeOpTool.SetParameter(new JObject
            {
                ["controller_path"] = ctrlPath,
                ["state_name"] = "Move",
                ["parameter"] = "Dir"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("\"parameter\":\"Dir\""));

            // Verify
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            AnimatorState moveState = null;
            foreach (var cs in ctrl.layers[0].stateMachine.states)
            {
                if (cs.state.name == "Move") { moveState = cs.state; break; }
            }
            Assert.IsNotNull(moveState);
            var bt = moveState.motion as BlendTree;
            Assert.IsNotNull(bt);
            Assert.AreEqual("Dir", bt.blendParameter);
        }
    }
}

#if THEATRE_HAS_TIMELINE
namespace Theatre.Tests.Editor
{
    using UnityEngine.Timeline;

    [TestFixture]
    public class TimelineOpTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = "Assets/_TheatreTest_Timeline";
            if (!AssetDatabase.IsValidFolder(_tempDir))
                AssetDatabase.CreateFolder("Assets", "_TheatreTest_Timeline");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_tempDir);
        }

        [Test]
        public void Create_ProducesPlayableFile()
        {
            var path = _tempDir + "/Test.playable";
            var result = TimelineOpTool.Create(new JObject { ["asset_path"] = path });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TimelineAsset>(path));
        }

        [Test]
        public void AddTrack_AddsAnimationTrack()
        {
            var path = _tempDir + "/TrackTest.playable";
            TimelineOpTool.Create(new JObject { ["asset_path"] = path });
            var result = TimelineOpTool.AddTrack(new JObject
            {
                ["asset_path"] = path,
                ["track_type"] = "animation",
                ["name"] = "PlayerAnim"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));

            var asset = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            bool found = false;
            foreach (var track in asset.GetOutputTracks())
            {
                if (track.name == "PlayerAnim") { found = true; break; }
            }
            Assert.IsTrue(found, "PlayerAnim track should exist");
        }

        [Test]
        public void ListTracks_ReturnsTrackInfo()
        {
            var path = _tempDir + "/ListTest.playable";
            TimelineOpTool.Create(new JObject { ["asset_path"] = path });
            TimelineOpTool.AddTrack(new JObject
            {
                ["asset_path"] = path,
                ["track_type"] = "animation",
                ["name"] = "Anim1"
            });
            var result = TimelineOpTool.ListTracks(new JObject { ["asset_path"] = path });
            Assert.That(result, Does.Contain("\"tracks\""));
            Assert.That(result, Does.Contain("Anim1"));
        }

        [Test]
        public void AddClip_AddsClipToTrack()
        {
            var path = _tempDir + "/ClipTest.playable";
            TimelineOpTool.Create(new JObject { ["asset_path"] = path });
            TimelineOpTool.AddTrack(new JObject
            {
                ["asset_path"] = path,
                ["track_type"] = "activation",
                ["name"] = "Act1"
            });
            var result = TimelineOpTool.AddClip(new JObject
            {
                ["asset_path"] = path,
                ["track_name"] = "Act1",
                ["start"] = 0.0,
                ["duration"] = 2.0
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));

            var asset = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            TrackAsset act1 = null;
            foreach (var track in asset.GetOutputTracks())
            {
                if (track.name == "Act1") { act1 = track; break; }
            }
            Assert.IsNotNull(act1);
            int clipCount = 0;
            foreach (var _ in act1.GetClips()) clipCount++;
            Assert.AreEqual(1, clipCount);
        }

        [Test]
        public void SetClipProperties_ModifiesTiming()
        {
            var path = _tempDir + "/SetClipProps.playable";
            TimelineOpTool.Create(new JObject { ["asset_path"] = path });
            TimelineOpTool.AddTrack(new JObject
            {
                ["asset_path"] = path,
                ["track_type"] = "activation",
                ["name"] = "TimingTrack"
            });
            TimelineOpTool.AddClip(new JObject
            {
                ["asset_path"] = path,
                ["track_name"] = "TimingTrack",
                ["start"] = 0.0,
                ["duration"] = 2.0
            });

            var result = TimelineOpTool.SetClipProperties(new JObject
            {
                ["asset_path"] = path,
                ["track_name"] = "TimingTrack",
                ["clip_index"] = 0,
                ["start"] = 1.0,
                ["duration"] = 3.0
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));

            var parsed = JObject.Parse(result);
            Assert.AreEqual(1.0, (double)parsed["start"], 0.001);
            Assert.AreEqual(3.0, (double)parsed["duration"], 0.001);
        }

        [Test]
        public void AddTrack_MultipleTypes()
        {
            var path = _tempDir + "/MultiTrack.playable";
            TimelineOpTool.Create(new JObject { ["asset_path"] = path });

            TimelineOpTool.AddTrack(new JObject
            {
                ["asset_path"] = path,
                ["track_type"] = "animation",
                ["name"] = "AnimTrack"
            });
            TimelineOpTool.AddTrack(new JObject
            {
                ["asset_path"] = path,
                ["track_type"] = "activation",
                ["name"] = "ActTrack"
            });

            var result = TimelineOpTool.ListTracks(new JObject { ["asset_path"] = path });
            Assert.That(result, Does.Contain("AnimTrack"));
            Assert.That(result, Does.Contain("ActTrack"));

            // Verify asset has both tracks
            var asset = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            int trackCount = 0;
            foreach (var _ in asset.GetOutputTracks()) trackCount++;
            Assert.AreEqual(2, trackCount, "Should have 2 tracks");
        }
    }
}
#endif
