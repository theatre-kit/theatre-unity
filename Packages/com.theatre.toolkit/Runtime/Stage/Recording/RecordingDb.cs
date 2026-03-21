using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Theatre.Stage
{
    /// <summary>
    /// SQLite database for a single recording clip.
    /// One database file per clip, stored in Library/Theatre/.
    /// </summary>
    public sealed class RecordingDb : IDisposable
    {
        private SqliteConnection _connection;
        private bool _disposed;

        static RecordingDb()
        {
            try
            {
                SQLitePCL.Batteries_V2.Init();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Theatre] SQLitePCL Batteries_V2.Init() failed: {ex.Message}. Trying fallback.");
                try
                {
                    SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());
                }
                catch (Exception ex2)
                {
                    Debug.LogError($"[Theatre] SQLitePCL fallback init also failed: {ex2.Message}");
                }
            }
        }

        /// <summary>Open or create a recording database at the given path.</summary>
        public RecordingDb(string dbPath)
        {
            _connection = new SqliteConnection($"Data Source={dbPath}");
            _connection.Open();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL;";
                cmd.ExecuteNonQuery();
            }

            EnsureSchema();
        }

        /// <summary>Create the schema tables if they don't exist.</summary>
        public void EnsureSchema()
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS metadata (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS frames (
    frame   INTEGER PRIMARY KEY,
    time    REAL NOT NULL,
    data    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS markers (
    frame    INTEGER NOT NULL,
    time     REAL NOT NULL,
    label    TEXT NOT NULL,
    metadata TEXT
);
";
                cmd.ExecuteNonQuery();
            }
        }

        // --- Write Operations ---

        /// <summary>Write metadata for this clip.</summary>
        public void WriteMetadata(ClipMetadata metadata)
        {
            WriteMetadataInternal(metadata);
        }

        /// <summary>Update metadata (e.g., end_frame, frame_count on stop).</summary>
        public void UpdateMetadata(ClipMetadata metadata)
        {
            WriteMetadataInternal(metadata);
        }

        private void WriteMetadataInternal(ClipMetadata metadata)
        {
            using (var tx = _connection.BeginTransaction())
            {
                WriteMetaValue("clip_id", metadata.ClipId, tx);
                WriteMetaValue("label", metadata.Label, tx);
                WriteMetaValue("scene", metadata.Scene, tx);
                WriteMetaValue("start_frame", metadata.StartFrame.ToString(), tx);
                WriteMetaValue("start_time", metadata.StartTime.ToString("R"), tx);
                WriteMetaValue("end_frame", metadata.EndFrame.ToString(), tx);
                WriteMetaValue("end_time", metadata.EndTime.ToString("R"), tx);
                WriteMetaValue("frame_count", metadata.FrameCount.ToString(), tx);
                WriteMetaValue("capture_rate", metadata.CaptureRate.ToString(), tx);
                WriteMetaValue("track_paths",
                    metadata.TrackPaths != null ? JsonConvert.SerializeObject(metadata.TrackPaths) : "[]", tx);
                WriteMetaValue("track_components",
                    metadata.TrackComponents != null ? JsonConvert.SerializeObject(metadata.TrackComponents) : "[]", tx);
                WriteMetaValue("file_path", metadata.FilePath ?? "", tx);
                WriteMetaValue("created_at", metadata.CreatedAt ?? "", tx);
                tx.Commit();
            }
        }

        private void WriteMetaValue(string key, string value, SqliteTransaction tx)
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR REPLACE INTO metadata (key, value) VALUES (@key, @value)";
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@value", value ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Write a batch of frames. Uses a transaction for performance.
        /// Each frame's Objects dictionary is serialized as JSON.
        /// </summary>
        public void WriteFrames(List<FrameRecord> frames)
        {
            if (frames == null || frames.Count == 0) return;

            using (var tx = _connection.BeginTransaction())
            {
                foreach (var frame in frames)
                {
                    var data = SerializeFrameData(frame.Objects);
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "INSERT OR REPLACE INTO frames (frame, time, data) VALUES (@frame, @time, @data)";
                        cmd.Parameters.AddWithValue("@frame", frame.Frame);
                        cmd.Parameters.AddWithValue("@time", (double)frame.Time);
                        cmd.Parameters.AddWithValue("@data", data);
                        cmd.ExecuteNonQuery();
                    }
                }
                tx.Commit();
            }
        }

        /// <summary>Insert a marker.</summary>
        public void WriteMarker(MarkerRecord marker)
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO markers (frame, time, label, metadata) VALUES (@frame, @time, @label, @metadata)";
                cmd.Parameters.AddWithValue("@frame", marker.Frame);
                cmd.Parameters.AddWithValue("@time", (double)marker.Time);
                cmd.Parameters.AddWithValue("@label", marker.Label ?? "");
                cmd.Parameters.AddWithValue("@metadata",
                    marker.Metadata != null ? marker.Metadata.ToString(Formatting.None) : (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        // --- Read Operations ---

        /// <summary>Read clip metadata.</summary>
        public ClipMetadata ReadMetadata()
        {
            var dict = new Dictionary<string, string>();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT key, value FROM metadata";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        dict[reader.GetString(0)] = reader.GetString(1);
                    }
                }
            }

            if (dict.Count == 0) return null;

            return new ClipMetadata
            {
                ClipId = GetMetaString(dict, "clip_id"),
                Label = GetMetaString(dict, "label"),
                Scene = GetMetaString(dict, "scene"),
                StartFrame = GetMetaInt(dict, "start_frame"),
                StartTime = GetMetaFloat(dict, "start_time"),
                EndFrame = GetMetaInt(dict, "end_frame"),
                EndTime = GetMetaFloat(dict, "end_time"),
                FrameCount = GetMetaInt(dict, "frame_count"),
                CaptureRate = GetMetaInt(dict, "capture_rate"),
                TrackPaths = GetMetaStringArray(dict, "track_paths"),
                TrackComponents = GetMetaStringArray(dict, "track_components"),
                FilePath = GetMetaString(dict, "file_path"),
                CreatedAt = GetMetaString(dict, "created_at"),
            };
        }

        /// <summary>Read all markers for this clip.</summary>
        public List<MarkerRecord> ReadMarkers()
        {
            var markers = new List<MarkerRecord>();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT frame, time, label, metadata FROM markers ORDER BY frame ASC";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        JObject meta = null;
                        if (!reader.IsDBNull(3))
                        {
                            var metaJson = reader.GetString(3);
                            if (!string.IsNullOrEmpty(metaJson))
                                meta = JObject.Parse(metaJson);
                        }
                        markers.Add(new MarkerRecord
                        {
                            Frame = reader.GetInt32(0),
                            Time = (float)reader.GetDouble(1),
                            Label = reader.GetString(2),
                            Metadata = meta,
                        });
                    }
                }
            }
            return markers;
        }

        /// <summary>
        /// Query frames in a range. Returns full snapshots (not deltas) by
        /// replaying deltas from the start.
        /// </summary>
        public List<FrameRecord> QueryRange(
            int fromFrame, int toFrame,
            string[] paths = null, string[] properties = null)
        {
            // Read all delta frames from 0 up to toFrame, then filter
            var allDeltas = ReadRawFrames(0, toFrame);

            // Build running snapshot, collect requested range
            var snapshot = new Dictionary<int, ObjectFrame>();
            var results = new List<FrameRecord>();

            foreach (var (frame, time, objects) in allDeltas)
            {
                // Merge delta into snapshot
                MergeIntoSnapshot(snapshot, objects);

                if (frame >= fromFrame && frame <= toFrame)
                {
                    var filteredObjects = FilterSnapshot(snapshot, paths, properties);
                    results.Add(new FrameRecord
                    {
                        Frame = frame,
                        Time = time,
                        Objects = filteredObjects,
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Get full reconstructed state at a specific frame by replaying
        /// deltas from the beginning.
        /// </summary>
        public FrameRecord ReconstructFrame(int frame)
        {
            var allDeltas = ReadRawFrames(0, frame);
            var snapshot = new Dictionary<int, ObjectFrame>();

            float lastTime = 0f;
            foreach (var (f, time, objects) in allDeltas)
            {
                MergeIntoSnapshot(snapshot, objects);
                lastTime = time;
            }

            return new FrameRecord
            {
                Frame = frame,
                Time = lastTime,
                Objects = new Dictionary<int, ObjectFrame>(snapshot),
            };
        }

        /// <summary>
        /// Diff two frames: returns only properties that differ between them.
        /// </summary>
        public FrameRecord DiffFrames(int frameA, int frameB)
        {
            var recA = ReconstructFrame(frameA);
            var recB = ReconstructFrame(frameB);

            var diffObjects = new Dictionary<int, ObjectFrame>();

            // Find all instance IDs present in either frame
            var allIds = new HashSet<int>();
            if (recA.Objects != null) foreach (var id in recA.Objects.Keys) allIds.Add(id);
            if (recB.Objects != null) foreach (var id in recB.Objects.Keys) allIds.Add(id);

            foreach (var id in allIds)
            {
                recA.Objects.TryGetValue(id, out var frameAObj);
                recB.Objects.TryGetValue(id, out var frameBObj);

                var diffProps = new Dictionary<string, JToken>();

                // Properties in frameB
                if (frameBObj?.Properties != null)
                {
                    foreach (var kvp in frameBObj.Properties)
                    {
                        JToken aVal = null;
                        frameAObj?.Properties?.TryGetValue(kvp.Key, out aVal);
                        if (aVal == null || !JToken.DeepEquals(aVal, kvp.Value))
                        {
                            diffProps[kvp.Key] = kvp.Value;
                        }
                    }
                }

                // Properties only in frameA (removed in frameB)
                if (frameAObj?.Properties != null)
                {
                    foreach (var kvp in frameAObj.Properties)
                    {
                        if (frameBObj?.Properties == null || !frameBObj.Properties.ContainsKey(kvp.Key))
                        {
                            diffProps[kvp.Key] = JValue.CreateNull();
                        }
                    }
                }

                if (diffProps.Count > 0)
                {
                    diffObjects[id] = new ObjectFrame
                    {
                        Path = frameBObj?.Path ?? frameAObj?.Path,
                        InstanceId = id,
                        Properties = diffProps,
                    };
                }
            }

            return new FrameRecord
            {
                Frame = frameB,
                Time = recB.Time,
                Objects = diffObjects,
            };
        }

        // --- Analysis Operations ---

        /// <summary>
        /// Run a threshold analysis: find frames where a property crosses
        /// the given threshold(s).
        /// </summary>
        public AnalyzeResult AnalyzeThreshold(AnalyzeQuery query)
        {
            var matches = new List<AnalyzeMatch>();
            var frames = GetFramesForAnalysis(query);

            foreach (var (frame, time, objects) in frames)
            {
                foreach (var kvp in objects)
                {
                    var obj = kvp.Value;
                    if (query.Path != null && obj.Path != query.Path) continue;
                    if (obj.Properties == null) continue;

                    if (!obj.Properties.TryGetValue(query.Property, out var val)) continue;

                    float? numVal = TryGetNumericValue(val);
                    if (numVal == null) continue;

                    bool crosses = false;
                    if (query.Above.HasValue && numVal.Value > query.Above.Value) crosses = true;
                    if (query.Below.HasValue && numVal.Value < query.Below.Value) crosses = true;

                    if (crosses)
                    {
                        matches.Add(new AnalyzeMatch { Frame = frame, Time = time, Value = val });
                    }
                }
            }

            return new AnalyzeResult
            {
                Query = query.Query,
                Path = query.Path,
                Property = query.Property,
                Matches = matches,
            };
        }

        /// <summary>
        /// Find the min or max value of a property across a frame range.
        /// </summary>
        public AnalyzeResult AnalyzeMinMax(AnalyzeQuery query)
        {
            var frames = GetFramesForAnalysis(query);
            bool isMin = query.Query == "min";

            float? extremalVal = null;
            int extremalFrame = 0;
            float extremalTime = 0f;

            foreach (var (frame, time, objects) in frames)
            {
                foreach (var kvp in objects)
                {
                    var obj = kvp.Value;
                    if (query.Path != null && obj.Path != query.Path) continue;
                    if (obj.Properties == null) continue;

                    if (!obj.Properties.TryGetValue(query.Property, out var val)) continue;

                    float? numVal = TryGetNumericValue(val);
                    if (numVal == null) continue;

                    if (extremalVal == null ||
                        (isMin && numVal.Value < extremalVal.Value) ||
                        (!isMin && numVal.Value > extremalVal.Value))
                    {
                        extremalVal = numVal.Value;
                        extremalFrame = frame;
                        extremalTime = time;
                    }
                }
            }

            return new AnalyzeResult
            {
                Query = query.Query,
                Path = query.Path,
                Property = query.Property,
                Matches = new List<AnalyzeMatch>(),
                Value = extremalVal,
                Frame = extremalFrame,
            };
        }

        /// <summary>
        /// Count the number of times a property value changed in a range.
        /// </summary>
        public AnalyzeResult AnalyzeCountChanges(AnalyzeQuery query)
        {
            var frames = GetFramesForAnalysis(query);
            var matches = new List<AnalyzeMatch>();

            JToken lastVal = null;

            foreach (var (frame, time, objects) in frames)
            {
                foreach (var kvp in objects)
                {
                    var obj = kvp.Value;
                    if (query.Path != null && obj.Path != query.Path) continue;
                    if (obj.Properties == null) continue;

                    if (!obj.Properties.TryGetValue(query.Property, out var val)) continue;

                    if (lastVal != null && !JToken.DeepEquals(lastVal, val))
                    {
                        matches.Add(new AnalyzeMatch { Frame = frame, Time = time, Value = val });
                    }
                    lastVal = val;
                }
            }

            return new AnalyzeResult
            {
                Query = query.Query,
                Path = query.Path,
                Property = query.Property,
                Matches = matches,
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
        }

        // --- Private Helpers ---

        private string SerializeFrameData(Dictionary<int, ObjectFrame> objects)
        {
            if (objects == null) return "{}";
            var obj = new JObject();
            foreach (var kvp in objects)
            {
                var frameObj = new JObject();
                frameObj["path"] = kvp.Value.Path;
                if (kvp.Value.Properties != null)
                {
                    var propsObj = new JObject();
                    foreach (var p in kvp.Value.Properties)
                        propsObj[p.Key] = p.Value;
                    frameObj["properties"] = propsObj;
                }
                obj[kvp.Key.ToString()] = frameObj;
            }
            return obj.ToString(Formatting.None);
        }

        private Dictionary<int, ObjectFrame> DeserializeFrameData(string json)
        {
            if (string.IsNullOrEmpty(json)) return new Dictionary<int, ObjectFrame>();
            var result = new Dictionary<int, ObjectFrame>();
            var obj = JObject.Parse(json);
            foreach (var prop in obj.Properties())
            {
                if (!int.TryParse(prop.Name, out int instanceId)) continue;
                var frameObj = (JObject)prop.Value;
                var of = new ObjectFrame
                {
                    InstanceId = instanceId,
                    Path = frameObj["path"]?.Value<string>(),
                    Properties = new Dictionary<string, JToken>(),
                };
                var propsToken = frameObj["properties"] as JObject;
                if (propsToken != null)
                {
                    foreach (var p in propsToken.Properties())
                        of.Properties[p.Name] = p.Value;
                }
                result[instanceId] = of;
            }
            return result;
        }

        private List<(int frame, float time, Dictionary<int, ObjectFrame> objects)> ReadRawFrames(
            int fromFrame, int toFrame)
        {
            var result = new List<(int, float, Dictionary<int, ObjectFrame>)>();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT frame, time, data FROM frames WHERE frame >= @from AND frame <= @to ORDER BY frame ASC";
                cmd.Parameters.AddWithValue("@from", fromFrame);
                cmd.Parameters.AddWithValue("@to", toFrame);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int frame = reader.GetInt32(0);
                        float time = (float)reader.GetDouble(1);
                        var objects = DeserializeFrameData(reader.GetString(2));
                        result.Add((frame, time, objects));
                    }
                }
            }
            return result;
        }

        private List<(int frame, float time, Dictionary<int, ObjectFrame> objects)> GetFramesForAnalysis(
            AnalyzeQuery query)
        {
            // Reconstruct snapshot for each frame in range
            int fromFrame = query.FromFrame ?? 0;
            int toFrame = query.ToFrame ?? int.MaxValue;

            // Read raw frames in range (deltas), but for analysis we need reconstructed state
            var rawFrames = ReadRawFrames(0, toFrame == int.MaxValue ? GetMaxFrame() : toFrame);
            var snapshot = new Dictionary<int, ObjectFrame>();
            var result = new List<(int, float, Dictionary<int, ObjectFrame>)>();

            foreach (var (frame, time, objects) in rawFrames)
            {
                MergeIntoSnapshot(snapshot, objects);
                if (frame >= fromFrame)
                {
                    // Deep-copy: ObjectFrame is a reference type, so a shallow
                    // Dictionary copy shares the same instances. We need each
                    // frame to have independent property snapshots.
                    var copy = new Dictionary<int, ObjectFrame>();
                    foreach (var kvp in snapshot)
                    {
                        copy[kvp.Key] = new ObjectFrame
                        {
                            Path = kvp.Value.Path,
                            InstanceId = kvp.Value.InstanceId,
                            Properties = new Dictionary<string, JToken>(kvp.Value.Properties)
                        };
                    }
                    result.Add((frame, time, copy));
                }
            }
            return result;
        }

        private int GetMaxFrame()
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT MAX(frame) FROM frames";
                var result = cmd.ExecuteScalar();
                if (result == DBNull.Value || result == null) return 0;
                return Convert.ToInt32(result);
            }
        }

        private void MergeIntoSnapshot(
            Dictionary<int, ObjectFrame> snapshot,
            Dictionary<int, ObjectFrame> delta)
        {
            if (delta == null) return;
            foreach (var kvp in delta)
            {
                if (!snapshot.TryGetValue(kvp.Key, out var existing))
                {
                    snapshot[kvp.Key] = new ObjectFrame
                    {
                        InstanceId = kvp.Key,
                        Path = kvp.Value.Path,
                        Properties = new Dictionary<string, JToken>(kvp.Value.Properties ?? new Dictionary<string, JToken>()),
                    };
                }
                else
                {
                    if (kvp.Value.Path != null) existing.Path = kvp.Value.Path;
                    if (kvp.Value.Properties != null)
                    {
                        foreach (var p in kvp.Value.Properties)
                            existing.Properties[p.Key] = p.Value;
                    }
                }
            }
        }

        private Dictionary<int, ObjectFrame> FilterSnapshot(
            Dictionary<int, ObjectFrame> snapshot,
            string[] paths, string[] properties)
        {
            if (paths == null && properties == null)
                return new Dictionary<int, ObjectFrame>(snapshot);

            var result = new Dictionary<int, ObjectFrame>();
            foreach (var kvp in snapshot)
            {
                var obj = kvp.Value;
                if (paths != null)
                {
                    bool found = false;
                    foreach (var p in paths)
                        if (obj.Path == p) { found = true; break; }
                    if (!found) continue;
                }

                if (properties == null)
                {
                    result[kvp.Key] = obj;
                    continue;
                }

                var filteredProps = new Dictionary<string, JToken>();
                if (obj.Properties != null)
                {
                    foreach (var prop in properties)
                    {
                        if (obj.Properties.TryGetValue(prop, out var val))
                            filteredProps[prop] = val;
                    }
                }
                result[kvp.Key] = new ObjectFrame
                {
                    InstanceId = obj.InstanceId,
                    Path = obj.Path,
                    Properties = filteredProps,
                };
            }
            return result;
        }

        private static float? TryGetNumericValue(JToken val)
        {
            if (val == null) return null;
            try
            {
                if (val.Type == JTokenType.Float || val.Type == JTokenType.Integer)
                    return val.Value<float>();
                if (val.Type == JTokenType.Array)
                {
                    // For vectors, use the first element (e.g., x position)
                    var arr = (JArray)val;
                    if (arr.Count > 0) return arr[0].Value<float>();
                }
            }
            catch { }
            return null;
        }

        private static string GetMetaString(Dictionary<string, string> dict, string key)
            => dict.TryGetValue(key, out var v) ? v : null;

        private static int GetMetaInt(Dictionary<string, string> dict, string key)
        {
            if (dict.TryGetValue(key, out var v) && int.TryParse(v, out int result))
                return result;
            return 0;
        }

        private static float GetMetaFloat(Dictionary<string, string> dict, string key)
        {
            if (dict.TryGetValue(key, out var v) && float.TryParse(v,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float result))
                return result;
            return 0f;
        }

        private static string[] GetMetaStringArray(Dictionary<string, string> dict, string key)
        {
            if (!dict.TryGetValue(key, out var v)) return new string[0];
            try { return JsonConvert.DeserializeObject<string[]>(v); }
            catch { return new string[0]; }
        }
    }
}
