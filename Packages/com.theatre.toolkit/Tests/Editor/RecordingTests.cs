using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Tests.Editor
{
    // -----------------------------------------------------------------------
    // RecordingTypesTests
    // -----------------------------------------------------------------------

    [TestFixture]
    public class RecordingTypesTests
    {
        [Test]
        public void ClipMetadata_Serialization_RoundTrips()
        {
            var meta = new ClipMetadata
            {
                ClipId = "clip_001",
                Label = "Test Recording",
                Scene = "SampleScene",
                StartFrame = 100,
                StartTime = 1.5f,
                EndFrame = 200,
                EndTime = 3.5f,
                FrameCount = 100,
                CaptureRate = 10,
                TrackPaths = new[] { "/Player", "/Enemy" },
                TrackComponents = new[] { "Transform", "Rigidbody" },
                FilePath = "Library/Theatre/rec_clip_001.sqlite3",
                CreatedAt = "2026-03-20T00:00:00Z",
            };

            var json = JsonConvert.SerializeObject(meta);
            var restored = JsonConvert.DeserializeObject<ClipMetadata>(json);

            Assert.AreEqual(meta.ClipId, restored.ClipId);
            Assert.AreEqual(meta.Label, restored.Label);
            Assert.AreEqual(meta.Scene, restored.Scene);
            Assert.AreEqual(meta.StartFrame, restored.StartFrame);
            Assert.AreEqual(meta.StartTime, restored.StartTime, 0.001f);
            Assert.AreEqual(meta.EndFrame, restored.EndFrame);
            Assert.AreEqual(meta.EndTime, restored.EndTime, 0.001f);
            Assert.AreEqual(meta.FrameCount, restored.FrameCount);
            Assert.AreEqual(meta.CaptureRate, restored.CaptureRate);
            Assert.AreEqual(meta.TrackPaths.Length, restored.TrackPaths.Length);
            Assert.AreEqual(meta.TrackPaths[0], restored.TrackPaths[0]);
            Assert.AreEqual(meta.TrackPaths[1], restored.TrackPaths[1]);
            Assert.AreEqual(meta.TrackComponents.Length, restored.TrackComponents.Length);
            Assert.AreEqual(meta.TrackComponents[0], restored.TrackComponents[0]);
            Assert.AreEqual(meta.FilePath, restored.FilePath);
            Assert.AreEqual(meta.CreatedAt, restored.CreatedAt);

            // Verify JSON field names are snake_case
            Assert.That(json, Does.Contain("clip_id"));
            Assert.That(json, Does.Contain("start_frame"));
            Assert.That(json, Does.Contain("capture_rate"));
            Assert.That(json, Does.Contain("track_paths"));
            Assert.That(json, Does.Contain("track_components"));
            Assert.That(json, Does.Contain("file_path"));
            Assert.That(json, Does.Contain("created_at"));

            // Duration is computed, should NOT appear in JSON
            Assert.That(json, Does.Not.Contain("Duration"));
            Assert.That(json, Does.Not.Contain("duration"));
        }

        [Test]
        public void ClipMetadata_Duration_ComputedCorrectly()
        {
            var meta = new ClipMetadata { StartTime = 1.0f, EndTime = 4.5f };
            Assert.AreEqual(3.5f, meta.Duration, 0.001f);
        }
    }

    // -----------------------------------------------------------------------
    // RecordingDbTests
    // -----------------------------------------------------------------------

    [TestFixture]
    public class RecordingDbTests
    {
        private string _dbPath;
        private RecordingDb _db;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Application.temporaryCachePath, $"test_{System.Guid.NewGuid():N}.sqlite3");
            _db = new RecordingDb(_dbPath);
        }

        [TearDown]
        public void TearDown()
        {
            _db?.Dispose();
            _db = null;
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }

        [Test]
        public void Schema_CreatedOnOpen()
        {
            // Just opening the DB should create the tables — verified by the fact
            // that subsequent operations don't throw "no such table" errors
            Assert.DoesNotThrow(() =>
            {
                var markers = _db.ReadMarkers();
                Assert.IsNotNull(markers);
            });
            Assert.DoesNotThrow(() =>
            {
                var meta = _db.ReadMetadata();
                // meta is null when no rows written yet
            });
        }

        [Test]
        public void WriteMetadata_ReadMetadata_RoundTrips()
        {
            var meta = new ClipMetadata
            {
                ClipId = "clip_abc",
                Label = "Round-trip test",
                Scene = "TestScene",
                StartFrame = 10,
                StartTime = 0.5f,
                EndFrame = 110,
                EndTime = 5.5f,
                FrameCount = 100,
                CaptureRate = 20,
                TrackPaths = new[] { "/Player" },
                TrackComponents = new[] { "Transform" },
                FilePath = "/tmp/test.sqlite3",
                CreatedAt = "2026-01-01T00:00:00Z",
            };

            _db.WriteMetadata(meta);
            var restored = _db.ReadMetadata();

            Assert.IsNotNull(restored);
            Assert.AreEqual(meta.ClipId, restored.ClipId);
            Assert.AreEqual(meta.Label, restored.Label);
            Assert.AreEqual(meta.Scene, restored.Scene);
            Assert.AreEqual(meta.StartFrame, restored.StartFrame);
            Assert.AreEqual(meta.StartTime, restored.StartTime, 0.001f);
            Assert.AreEqual(meta.EndFrame, restored.EndFrame);
            Assert.AreEqual(meta.EndTime, restored.EndTime, 0.001f);
            Assert.AreEqual(meta.FrameCount, restored.FrameCount);
            Assert.AreEqual(meta.CaptureRate, restored.CaptureRate);
            Assert.IsNotNull(restored.TrackPaths);
            Assert.AreEqual(1, restored.TrackPaths.Length);
            Assert.AreEqual("/Player", restored.TrackPaths[0]);
        }

        [Test]
        public void WriteFrames_QueryRange_ReturnsFrames()
        {
            var frames = new List<FrameRecord>
            {
                new FrameRecord
                {
                    Frame = 0, Time = 0f,
                    Objects = new Dictionary<int, ObjectFrame>
                    {
                        { 100, new ObjectFrame { Path = "/Player", InstanceId = 100,
                            Properties = new Dictionary<string, JToken>
                            {
                                { "position", new JArray(0f, 0f, 0f) }
                            }
                        }}
                    }
                },
                new FrameRecord
                {
                    Frame = 1, Time = 0.1f,
                    Objects = new Dictionary<int, ObjectFrame>
                    {
                        { 100, new ObjectFrame { Path = "/Player", InstanceId = 100,
                            Properties = new Dictionary<string, JToken>
                            {
                                { "position", new JArray(1f, 0f, 0f) }
                            }
                        }}
                    }
                },
                new FrameRecord
                {
                    Frame = 2, Time = 0.2f,
                    Objects = new Dictionary<int, ObjectFrame>
                    {
                        { 100, new ObjectFrame { Path = "/Player", InstanceId = 100,
                            Properties = new Dictionary<string, JToken>
                            {
                                { "position", new JArray(2f, 0f, 0f) }
                            }
                        }}
                    }
                },
            };

            _db.WriteFrames(frames);
            var results = _db.QueryRange(0, 2);

            Assert.IsNotNull(results);
            Assert.AreEqual(3, results.Count);
            Assert.AreEqual(0, results[0].Frame);
            Assert.AreEqual(1, results[1].Frame);
            Assert.AreEqual(2, results[2].Frame);
        }

        [Test]
        public void DeltaReconstruction_MergesCorrectly()
        {
            // Frame 0: full snapshot with position and rotation
            var frame0 = new FrameRecord
            {
                Frame = 0, Time = 0f,
                Objects = new Dictionary<int, ObjectFrame>
                {
                    { 42, new ObjectFrame { Path = "/Box", InstanceId = 42,
                        Properties = new Dictionary<string, JToken>
                        {
                            { "position", new JArray(0f, 0f, 0f) },
                            { "rotation", new JArray(0f, 0f, 0f) },
                        }
                    }}
                }
            };
            // Frame 1: only position changed
            var frame1 = new FrameRecord
            {
                Frame = 1, Time = 0.1f,
                Objects = new Dictionary<int, ObjectFrame>
                {
                    { 42, new ObjectFrame { Path = "/Box", InstanceId = 42,
                        Properties = new Dictionary<string, JToken>
                        {
                            { "position", new JArray(5f, 0f, 0f) },
                        }
                    }}
                }
            };
            // Frame 2: only rotation changed
            var frame2 = new FrameRecord
            {
                Frame = 2, Time = 0.2f,
                Objects = new Dictionary<int, ObjectFrame>
                {
                    { 42, new ObjectFrame { Path = "/Box", InstanceId = 42,
                        Properties = new Dictionary<string, JToken>
                        {
                            { "rotation", new JArray(0f, 90f, 0f) },
                        }
                    }}
                }
            };

            _db.WriteFrames(new List<FrameRecord> { frame0, frame1, frame2 });
            var reconstructed = _db.ReconstructFrame(2);

            Assert.IsNotNull(reconstructed);
            Assert.IsNotNull(reconstructed.Objects);
            Assert.IsTrue(reconstructed.Objects.ContainsKey(42));

            var obj = reconstructed.Objects[42];
            Assert.IsTrue(obj.Properties.ContainsKey("position"));
            Assert.IsTrue(obj.Properties.ContainsKey("rotation"));

            // Position should be from frame 1 (5, 0, 0)
            var pos = obj.Properties["position"] as JArray;
            Assert.IsNotNull(pos);
            Assert.AreEqual(5f, pos[0].Value<float>(), 0.001f);

            // Rotation should be from frame 2 (0, 90, 0)
            var rot = obj.Properties["rotation"] as JArray;
            Assert.IsNotNull(rot);
            Assert.AreEqual(90f, rot[1].Value<float>(), 0.001f);
        }

        [Test]
        public void DiffFrames_ReturnsOnlyChanges()
        {
            var frame0 = new FrameRecord
            {
                Frame = 0, Time = 0f,
                Objects = new Dictionary<int, ObjectFrame>
                {
                    { 10, new ObjectFrame { Path = "/Cube", InstanceId = 10,
                        Properties = new Dictionary<string, JToken>
                        {
                            { "position", new JArray(0f, 0f, 0f) },
                            { "scale", new JArray(1f, 1f, 1f) },
                        }
                    }}
                }
            };
            var frame1 = new FrameRecord
            {
                Frame = 1, Time = 0.1f,
                Objects = new Dictionary<int, ObjectFrame>
                {
                    { 10, new ObjectFrame { Path = "/Cube", InstanceId = 10,
                        Properties = new Dictionary<string, JToken>
                        {
                            { "position", new JArray(3f, 0f, 0f) },
                            // scale unchanged
                        }
                    }}
                }
            };

            _db.WriteFrames(new List<FrameRecord> { frame0, frame1 });
            var diff = _db.DiffFrames(0, 1);

            Assert.IsNotNull(diff);
            Assert.IsNotNull(diff.Objects);
            Assert.IsTrue(diff.Objects.ContainsKey(10));

            var obj = diff.Objects[10];
            // Position changed — should be in diff
            Assert.IsTrue(obj.Properties.ContainsKey("position"),
                "position should be in diff because it changed");
        }

        [Test]
        public void WriteMarker_ReadMarkers_InOrder()
        {
            _db.WriteMarker(new MarkerRecord { Frame = 50, Time = 5.0f, Label = "second" });
            _db.WriteMarker(new MarkerRecord { Frame = 10, Time = 1.0f, Label = "first" });

            var markers = _db.ReadMarkers();

            Assert.IsNotNull(markers);
            Assert.AreEqual(2, markers.Count);
            // Should be in frame order
            Assert.AreEqual("first", markers[0].Label);
            Assert.AreEqual(10, markers[0].Frame);
            Assert.AreEqual("second", markers[1].Label);
            Assert.AreEqual(50, markers[1].Frame);
        }

        [Test]
        public void AnalyzeThreshold_FindsCrossings()
        {
            // Write frames where health crosses below 50
            var frames = new List<FrameRecord>();
            float[] healthValues = { 100f, 80f, 45f, 30f, 60f };
            for (int i = 0; i < healthValues.Length; i++)
            {
                frames.Add(new FrameRecord
                {
                    Frame = i, Time = i * 0.1f,
                    Objects = new Dictionary<int, ObjectFrame>
                    {
                        { 1, new ObjectFrame { Path = "/Player", InstanceId = 1,
                            Properties = new Dictionary<string, JToken>
                            {
                                { "health", new JValue(healthValues[i]) }
                            }
                        }}
                    }
                });
            }
            _db.WriteFrames(frames);

            var query = new AnalyzeQuery
            {
                Query = "threshold",
                Path = "/Player",
                Property = "health",
                Below = 50f,
            };
            var result = _db.AnalyzeThreshold(query);

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Matches);
            // Frames 2 (45) and 3 (30) are below 50
            Assert.GreaterOrEqual(result.Matches.Count, 2);
        }

        [Test]
        public void AnalyzeMinMax_FindsExtremalFrame()
        {
            var frames = new List<FrameRecord>();
            float[] speedValues = { 1f, 5f, 3f, 9f, 2f };
            for (int i = 0; i < speedValues.Length; i++)
            {
                frames.Add(new FrameRecord
                {
                    Frame = i, Time = i * 0.1f,
                    Objects = new Dictionary<int, ObjectFrame>
                    {
                        { 1, new ObjectFrame { Path = "/Player", InstanceId = 1,
                            Properties = new Dictionary<string, JToken>
                            {
                                { "speed", new JValue(speedValues[i]) }
                            }
                        }}
                    }
                });
            }
            _db.WriteFrames(frames);

            // Find max speed
            var maxQuery = new AnalyzeQuery
            {
                Query = "max",
                Path = "/Player",
                Property = "speed",
            };
            var maxResult = _db.AnalyzeMinMax(maxQuery);

            Assert.IsNotNull(maxResult);
            Assert.IsTrue(maxResult.Value.HasValue);
            Assert.AreEqual(9f, maxResult.Value.Value, 0.001f);
            Assert.AreEqual(3, maxResult.Frame); // frame index 3 has value 9

            // Find min speed
            var minQuery = new AnalyzeQuery
            {
                Query = "min",
                Path = "/Player",
                Property = "speed",
            };
            var minResult = _db.AnalyzeMinMax(minQuery);

            Assert.IsNotNull(minResult);
            Assert.IsTrue(minResult.Value.HasValue);
            Assert.AreEqual(1f, minResult.Value.Value, 0.001f);
            Assert.AreEqual(0, minResult.Frame); // frame index 0 has value 1
        }
    }

    // -----------------------------------------------------------------------
    // FrameSerializerTests
    // -----------------------------------------------------------------------

    [TestFixture]
    public class FrameSerializerTests
    {
        private GameObject _testObject;

        [SetUp]
        public void SetUp()
        {
            _testObject = new GameObject("SerializerTestObject");
            _testObject.transform.position = new Vector3(1f, 2f, 3f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_testObject != null)
                Object.DestroyImmediate(_testObject);
            _testObject = null;
        }

        [Test]
        public void CaptureFullSnapshot_IncludesTransform()
        {
            var tracked = new List<GameObject> { _testObject };
            var snapshot = FrameSerializer.CaptureFullSnapshot(tracked, null);

            Assert.IsNotNull(snapshot);
            Assert.AreEqual(1, snapshot.Count);

#pragma warning disable CS0618
            var instanceId = _testObject.GetInstanceID();
#pragma warning restore CS0618

            Assert.IsTrue(snapshot.ContainsKey(instanceId));
            var frame = snapshot[instanceId];
            Assert.IsNotNull(frame.Properties);
            Assert.IsTrue(frame.Properties.ContainsKey("position"),
                "position should be captured");

            var pos = frame.Properties["position"] as JArray;
            Assert.IsNotNull(pos);
            Assert.AreEqual(3, pos.Count);
            Assert.AreEqual(1f, pos[0].Value<float>(), 0.01f);
            Assert.AreEqual(2f, pos[1].Value<float>(), 0.01f);
            Assert.AreEqual(3f, pos[2].Value<float>(), 0.01f);
        }

        [Test]
        public void CaptureDelta_EmptyForUnchanged()
        {
            var tracked = new List<GameObject> { _testObject };

            // First snapshot (full)
            var snapshot1 = FrameSerializer.CaptureFullSnapshot(tracked, null);

            // Delta with no changes
            var delta = FrameSerializer.CaptureDelta(tracked, null, snapshot1);

            // No changes => the object should not appear in the delta
            Assert.IsNotNull(delta);
            Assert.AreEqual(0, delta.Count,
                "Delta should be empty when nothing changed");
        }

        [Test]
        public void CaptureDelta_DetectsPositionChange()
        {
            var tracked = new List<GameObject> { _testObject };

            // First snapshot
            var snapshot1 = FrameSerializer.CaptureFullSnapshot(tracked, null);

            // Move the object
            _testObject.transform.position = new Vector3(10f, 20f, 30f);

            // Delta should include position
            var delta = FrameSerializer.CaptureDelta(tracked, null, snapshot1);

            Assert.IsNotNull(delta);
            Assert.AreEqual(1, delta.Count, "Delta should contain the moved object");

#pragma warning disable CS0618
            var instanceId = _testObject.GetInstanceID();
#pragma warning restore CS0618

            Assert.IsTrue(delta.ContainsKey(instanceId));
            var frame = delta[instanceId];
            Assert.IsTrue(frame.Properties.ContainsKey("position"),
                "position should be in delta after move");

            var pos = frame.Properties["position"] as JArray;
            Assert.IsNotNull(pos);
            Assert.AreEqual(10f, pos[0].Value<float>(), 0.01f);
        }
    }
}
