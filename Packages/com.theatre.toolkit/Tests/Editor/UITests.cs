using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Theatre.Editor.UI;
using UnityEditor;
using UnityEngine;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class ActivityLogTests
    {
        private ActivityLog _log;

        [SetUp]
        public void SetUp()
        {
            _log = new ActivityLog();
            SessionState.EraseString("Theatre_ActivityLog");
            SessionState.EraseInt("Theatre_ActivityCalls");
            SessionState.EraseInt("Theatre_ActivityTokens");
        }

        [Test]
        public void Record_AddsEntry()
        {
            _log.Record("scene_snapshot", new JObject(), "{\"result\":\"ok\"}");
            Assert.AreEqual(1, _log.Entries.Count);
            Assert.AreEqual("scene_snapshot", _log.Entries[0].ToolName);
        }

        [Test]
        public void Record_ExtractsOperation()
        {
            _log.Record("action", new JObject { ["operation"] = "teleport" }, "{}");
            Assert.AreEqual("teleport", _log.Entries[0].Operation);
        }

        [Test]
        public void Record_DetectsError()
        {
            _log.Record("test", new JObject(), "{\"error\":{\"code\":\"x\"}}");
            Assert.IsTrue(_log.Entries[0].IsError);
        }

        [Test]
        public void Record_ExceedsMax_RemovesOldest()
        {
            for (int i = 0; i < ActivityLog.MaxEntries + 10; i++)
                _log.Record("tool" + i, new JObject(), "{}");
            Assert.AreEqual(ActivityLog.MaxEntries, _log.Entries.Count);
        }

        [Test]
        public void Record_NewestFirst()
        {
            _log.Record("first", new JObject(), "{}");
            _log.Record("second", new JObject(), "{}");
            Assert.AreEqual("second", _log.Entries[0].ToolName);
            Assert.AreEqual("first",  _log.Entries[1].ToolName);
        }

        [Test]
        public void TotalCalls_Increments()
        {
            _log.Record("a", new JObject(), "{}");
            _log.Record("b", new JObject(), "{}");
            Assert.AreEqual(2, _log.TotalCalls);
        }

        [Test]
        public void SaveRestore_RoundTrips()
        {
            _log.Record("test_tool", new JObject(), "{\"result\":\"ok\"}");
            _log.Save();
            var restored = new ActivityLog();
            restored.Restore();
            Assert.AreEqual(1, restored.Entries.Count);
            Assert.AreEqual("test_tool", restored.Entries[0].ToolName);
        }

        [Test]
        public void SaveRestore_PreservesCounters()
        {
            _log.Record("a", new JObject(), "{\"result\":\"ok\"}");
            _log.Record("b", new JObject(), "{\"result\":\"ok\"}");
            _log.Save();
            var restored = new ActivityLog();
            restored.Restore();
            Assert.AreEqual(2, restored.TotalCalls);
        }

        [Test]
        public void Clear_ResetsAll()
        {
            _log.Record("a", new JObject(), "{}");
            _log.Clear();
            Assert.AreEqual(0, _log.Entries.Count);
            Assert.AreEqual(0, _log.TotalCalls);
            Assert.AreEqual(0, _log.TotalTokens);
        }
    }

    [TestFixture]
    public class TheatreWindowTests
    {
        [Test]
        public void ShowWindow_OpensWithoutError()
        {
            var window = EditorWindow.GetWindow<TheatreWindow>();
            Assert.IsNotNull(window);
            window.Close();
        }
    }
}
