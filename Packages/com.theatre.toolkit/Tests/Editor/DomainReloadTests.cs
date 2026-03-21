using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Theatre.Stage;
using UnityEditor;

namespace Theatre.Tests.Editor
{
    /// <summary>
    /// Stress tests for SessionState-backed persistence that must survive
    /// Unity domain reloads. Exercises round-trip serialization at scale
    /// to catch truncation, counter corruption, or JSON encoding failures.
    /// </summary>
    [TestFixture]
    public class DomainReloadStressTests
    {
        private const string WatchesKey = "Theatre_Watches";
        private const string CounterKey = "Theatre_WatchCounter";

        [SetUp]
        public void SetUp()
        {
            // Clean slate before each test
            SessionState.EraseString(WatchesKey);
            SessionState.EraseInt(CounterKey);
        }

        [TearDown]
        public void TearDown()
        {
            // Always clean up to avoid leaking state into other tests
            SessionState.EraseString(WatchesKey);
            SessionState.EraseInt(CounterKey);
        }

        // -------------------------------------------------------------------
        // Watch engine persistence at scale
        // -------------------------------------------------------------------

        [Test]
        public void WatchEngine_32Watches_SurvivesPersistAndRestore()
        {
            // Arrange: create engine and fill to the 32-watch limit
            var engine = new WatchEngine();
            engine.Initialize(_ => { });

            for (int i = 0; i < 32; i++)
            {
                var def = new WatchDefinition
                {
                    Target = $"/Object_{i:D2}",
                    ThrottleMs = 500 + i * 10,
                    Label = $"watch_{i:D2}"
                };
                // Use a condition type that doesn't need a scene object
                def.Condition = new WatchCondition
                {
                    Type = "spawned",
                    NamePattern = $"Enemy_{i:D2}_*"
                };
                var id = engine.Create(def);
                Assert.IsNotNull(id, $"Watch {i} should succeed (not over limit)");
            }

            Assert.AreEqual(32, engine.Count,
                "Engine should hold 32 watches before restore");

            // Verify the 33rd is rejected
            var over = engine.Create(new WatchDefinition
            {
                Target = "*",
                Condition = new WatchCondition { Type = "spawned" },
                ThrottleMs = 500
            });
            Assert.IsNull(over, "33rd watch should be rejected");

            // Act: simulate domain reload by creating a fresh engine that
            // reads from the same SessionState slot
            var restored = new WatchEngine();
            restored.Initialize(_ => { });

            // Assert
            Assert.AreEqual(32, restored.Count,
                "Restored engine should have all 32 watches");

            var list = restored.ListAll();
            Assert.AreEqual(32, list.Count);

            // Spot-check a few entries
            var first = (JObject)list[0];
            Assert.AreEqual("w_01", first["watch_id"].Value<string>());
            Assert.AreEqual("/Object_00", first["target"].Value<string>());
            Assert.AreEqual("watch_00", first["label"].Value<string>());
            Assert.AreEqual(500, first["throttle_ms"].Value<int>());

            var last = (JObject)list[31];
            Assert.AreEqual("w_32", last["watch_id"].Value<string>());
            Assert.AreEqual("/Object_31", last["target"].Value<string>());
            Assert.AreEqual("watch_31", last["label"].Value<string>());
            Assert.AreEqual(810, last["throttle_ms"].Value<int>());
        }

        [Test]
        public void WatchEngine_Counter_ResumesAfterRestore()
        {
            // Arrange: create some watches, delete one, create more
            var engine1 = new WatchEngine();
            engine1.Initialize(_ => { });

            var id1 = engine1.Create(new WatchDefinition
            {
                Target = "/Alpha",
                Condition = new WatchCondition { Type = "spawned" },
                ThrottleMs = 500
            });
            var id2 = engine1.Create(new WatchDefinition
            {
                Target = "/Beta",
                Condition = new WatchCondition { Type = "spawned" },
                ThrottleMs = 500
            });

            Assert.AreEqual("w_01", id1);
            Assert.AreEqual("w_02", id2);

            // Remove first watch
            engine1.Remove(id1);
            Assert.AreEqual(1, engine1.Count);

            // Act: restore into new engine
            var engine2 = new WatchEngine();
            engine2.Initialize(_ => { });

            Assert.AreEqual(1, engine2.Count,
                "After removing one, only one watch should restore");

            // New watch should get id w_03 (counter not reset)
            var id3 = engine2.Create(new WatchDefinition
            {
                Target = "/Gamma",
                Condition = new WatchCondition { Type = "spawned" },
                ThrottleMs = 500
            });

            Assert.AreEqual("w_03", id3,
                "Counter should continue from last value, not reset to 1");
        }

        [Test]
        public void WatchEngine_AllConditionTypes_RoundTrip()
        {
            // Arrange: one watch of each condition type
            var engine1 = new WatchEngine();
            engine1.Initialize(_ => { });

            var conditions = new[]
            {
                new WatchCondition { Type = "threshold", Property = "health", Below = 25f },
                new WatchCondition { Type = "proximity", Target = "/Enemy", Within = 5f },
                new WatchCondition
                {
                    Type = "entered_region",
                    Min = new[] { -10f, 0f, -10f },
                    Max = new[] { 10f, 5f, 10f }
                },
                new WatchCondition { Type = "property_changed", Property = "is_active" },
                new WatchCondition { Type = "destroyed" },
                new WatchCondition { Type = "spawned", NamePattern = "Enemy_*" }
            };

            var createdIds = new List<string>();
            for (int i = 0; i < conditions.Length; i++)
            {
                var def = new WatchDefinition
                {
                    Target = i < 5 ? $"/Object_{i}" : "*",
                    Condition = conditions[i],
                    ThrottleMs = 100 * (i + 1)
                };
                createdIds.Add(engine1.Create(def));
            }

            Assert.AreEqual(conditions.Length, engine1.Count);

            // Act: domain reload simulation
            var engine2 = new WatchEngine();
            engine2.Initialize(_ => { });

            Assert.AreEqual(conditions.Length, engine2.Count,
                "All condition types should survive domain reload");

            var list = engine2.ListAll();
            for (int i = 0; i < conditions.Length; i++)
            {
                var entry = (JObject)list[i];
                var condition = (JObject)entry["condition"];
                Assert.IsNotNull(condition,
                    $"Watch {i} ({conditions[i].Type}) should have condition after restore");
                Assert.AreEqual(conditions[i].Type,
                    condition["type"].Value<string>(),
                    $"Condition type should round-trip for watch {i}");
            }

            // Verify threshold-specific fields
            var thresholdEntry = (JObject)((JObject)list[0])["condition"];
            Assert.AreEqual("health", thresholdEntry["property"].Value<string>());
            Assert.AreEqual(25f, thresholdEntry["below"].Value<float>(), 0.001f);

            // Verify proximity fields
            var proximityEntry = (JObject)((JObject)list[1])["condition"];
            Assert.AreEqual("/Enemy", proximityEntry["target"].Value<string>());
            Assert.AreEqual(5f, proximityEntry["within"].Value<float>(), 0.001f);

            // Verify spawned pattern
            var spawnedEntry = (JObject)((JObject)list[5])["condition"];
            Assert.AreEqual("Enemy_*", spawnedEntry["name_pattern"].Value<string>());
        }

        [Test]
        public void WatchDefinition_DirectSerialization_32Entries_RoundTrips()
        {
            // Test the raw serialization path used by WatchEngine.PersistToSessionState
            // without involving SessionState (pure unit test).
            var defs = new List<WatchDefinition>();

            for (int i = 0; i < 32; i++)
            {
                defs.Add(new WatchDefinition
                {
                    WatchId = $"w_{i + 1:D2}",
                    Target = $"/Scene/Level_{i / 8}/Group_{i % 8}/Object_{i}",
                    Track = new[] { "position", "rotation", "health" },
                    Condition = new WatchCondition
                    {
                        Type = "threshold",
                        Property = $"stat_{i}",
                        Below = i * 3.14f,
                        Above = i * 6.28f
                    },
                    ThrottleMs = 100 + i * 50,
                    Label = $"monitor_object_{i:D2}",
                    CreatedFrame = 1000 + i
                });
            }

            // Serialize (same as WatchEngine does)
            var json = JsonConvert.SerializeObject(defs);

            // Verify the JSON is not corrupted (no truncation)
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Length > 1000,
                "32 watch definitions should produce a non-trivial JSON blob");

            // Deserialize
            var restored = JsonConvert.DeserializeObject<List<WatchDefinition>>(json);

            Assert.IsNotNull(restored);
            Assert.AreEqual(32, restored.Count, "All 32 definitions should round-trip");

            for (int i = 0; i < 32; i++)
            {
                var original = defs[i];
                var r = restored[i];

                Assert.AreEqual(original.WatchId, r.WatchId, $"WatchId mismatch at {i}");
                Assert.AreEqual(original.Target, r.Target, $"Target mismatch at {i}");
                Assert.AreEqual(original.ThrottleMs, r.ThrottleMs, $"ThrottleMs mismatch at {i}");
                Assert.AreEqual(original.Label, r.Label, $"Label mismatch at {i}");
                Assert.AreEqual(original.CreatedFrame, r.CreatedFrame,
                    $"CreatedFrame mismatch at {i}");

                Assert.IsNotNull(r.Track, $"Track should not be null at {i}");
                Assert.AreEqual(3, r.Track.Length, $"Track length mismatch at {i}");
                Assert.AreEqual("position", r.Track[0]);

                Assert.IsNotNull(r.Condition, $"Condition should not be null at {i}");
                Assert.AreEqual(original.Condition.Type, r.Condition.Type,
                    $"Condition.Type mismatch at {i}");
                Assert.AreEqual(original.Condition.Property, r.Condition.Property,
                    $"Condition.Property mismatch at {i}");
                Assert.IsTrue(r.Condition.Below.HasValue, $"Below should have value at {i}");
                Assert.AreEqual(original.Condition.Below.Value,
                    r.Condition.Below.Value, 0.001f, $"Below value mismatch at {i}");
            }
        }

        [Test]
        public void WatchEngine_EmptyState_RestoresClean()
        {
            // Restoring with no persisted data should produce an empty engine,
            // not throw or produce garbage.
            var engine = new WatchEngine();
            engine.Initialize(_ => { });

            Assert.AreEqual(0, engine.Count, "Fresh restore with no state should be empty");

            var list = engine.ListAll();
            Assert.AreEqual(0, list.Count);

            // Should be able to create a watch normally
            var id = engine.Create(new WatchDefinition
            {
                Target = "*",
                Condition = new WatchCondition { Type = "spawned" },
                ThrottleMs = 500
            });

            Assert.IsNotNull(id);
            Assert.AreEqual("w_01", id);
        }

        [Test]
        public void WatchEngine_CorruptedSessionState_RecoversSilently()
        {
            // If SessionState contains invalid JSON (e.g. from a prior crash),
            // the engine should recover silently with an empty state.
            SessionState.SetString(WatchesKey, "{ this is not valid json [[[");
            SessionState.SetInt(CounterKey, 7);

            var engine = new WatchEngine();
            engine.Initialize(_ => { });

            // Should recover with 0 watches, not throw
            Assert.AreEqual(0, engine.Count,
                "Corrupted JSON should result in empty state, not exception");

            // Counter may or may not reset — the important thing is it works
            // and can create new watches
            var id = engine.Create(new WatchDefinition
            {
                Target = "*",
                Condition = new WatchCondition { Type = "spawned" },
                ThrottleMs = 500
            });

            Assert.IsNotNull(id, "Should be able to create watches after corrupt restore");
        }
    }
}
