using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Theatre.Editor.UI;
using UnityEngine;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class GizmoStateTests
    {
        [SetUp]
        public void SetUp()
        {
            GizmoState.Clear();
        }

        [Test]
        public void Add_IncreasesCount()
        {
            GizmoState.Add(new GizmoState.GizmoRequest
            {
                Type   = GizmoState.GizmoType.Nearest,
                Origin = Vector3.zero,
            });
            Assert.AreEqual(1, GizmoState.Requests.Count);
        }

        [Test]
        public void Clear_RemovesAll()
        {
            GizmoState.Add(new GizmoState.GizmoRequest());
            GizmoState.Clear();
            Assert.AreEqual(0, GizmoState.Requests.Count);
        }

        [Test]
        public void GetAlpha_FreshRequest_ReturnsNearOne()
        {
            var req = new GizmoState.GizmoRequest
            {
                CreatedAt = Time.realtimeSinceStartup,
            };
            var alpha = GizmoState.GetAlpha(req);
            Assert.Greater(alpha, 0.5f);
        }

        [Test]
        public void GetAlpha_ExpiredRequest_ReturnsZero()
        {
            var req = new GizmoState.GizmoRequest
            {
                CreatedAt = Time.realtimeSinceStartup - GizmoState.FadeDuration - 1f,
            };
            var alpha = GizmoState.GetAlpha(req);
            Assert.AreEqual(0f, alpha, 0.001f);
        }

        [Test]
        public void Enabled_False_RequestsStillStored()
        {
            GizmoState.Enabled = false;
            GizmoState.Add(new GizmoState.GizmoRequest { Type = GizmoState.GizmoType.Radius });
            Assert.AreEqual(1, GizmoState.Requests.Count);
            GizmoState.Enabled = true;
        }
    }

    [TestFixture]
    public class GizmoExtractorTests
    {
        [SetUp]
        public void SetUp()
        {
            GizmoState.Clear();
            GizmoState.Enabled = true;
        }

        [Test]
        public void TryExtract_SpatialRadius_AddsGizmo()
        {
            var args = new JObject
            {
                ["operation"] = "radius",
                ["origin"]    = new JArray(1, 2, 3),
                ["radius"]    = 10.0,
            };
            var result = "{\"result\":\"ok\",\"results\":[]}";
            var added  = GizmoExtractor.TryExtract("spatial_query", args, result);
            Assert.IsTrue(added);
            Assert.AreEqual(1, GizmoState.Requests.Count);
        }

        [Test]
        public void TryExtract_SpatialNearest_AddsGizmo()
        {
            var args = new JObject
            {
                ["operation"] = "nearest",
                ["origin"]    = new JArray(0, 0, 0),
            };
            var result = "{\"result\":\"ok\",\"results\":[{\"path\":\"/Cube\",\"position\":[1,0,0]}]}";
            var added  = GizmoExtractor.TryExtract("spatial_query", args, result);
            Assert.IsTrue(added);
            Assert.AreEqual(1, GizmoState.Requests.Count);
        }

        [Test]
        public void TryExtract_UnrelatedTool_ReturnsFalse()
        {
            var added = GizmoExtractor.TryExtract("scene_snapshot", new JObject(), "{}");
            Assert.IsFalse(added);
        }

        [Test]
        public void TryExtract_ActionTeleport_AddsGizmo()
        {
            var args   = new JObject { ["operation"] = "teleport" };
            var result = "{\"result\":\"ok\",\"previous_position\":[0,0,0],\"position\":[10,0,5]}";
            var added  = GizmoExtractor.TryExtract("action", args, result);
            Assert.IsTrue(added);
            Assert.AreEqual(1, GizmoState.Requests.Count);
        }

        [Test]
        public void TryExtract_Disabled_ReturnsFalse()
        {
            GizmoState.Enabled = false;
            var args   = new JObject { ["operation"] = "radius", ["origin"] = new JArray(0, 0, 0), ["radius"] = 5.0 };
            var added  = GizmoExtractor.TryExtract("spatial_query", args, "{}");
            Assert.IsFalse(added);
            Assert.AreEqual(0, GizmoState.Requests.Count);
        }
    }
}
