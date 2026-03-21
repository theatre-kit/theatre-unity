using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Theatre.Stage
{
    /// <summary>
    /// Metadata for a recording clip. Persisted to SQLite metadata table
    /// and to SessionState for domain reload survival.
    /// </summary>
    public sealed class ClipMetadata
    {
        [JsonProperty("clip_id")]
        public string ClipId { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("scene")]
        public string Scene { get; set; }

        [JsonProperty("start_frame")]
        public int StartFrame { get; set; }

        [JsonProperty("start_time")]
        public float StartTime { get; set; }

        [JsonProperty("end_frame")]
        public int EndFrame { get; set; }

        [JsonProperty("end_time")]
        public float EndTime { get; set; }

        [JsonProperty("frame_count")]
        public int FrameCount { get; set; }

        [JsonProperty("capture_rate")]
        public int CaptureRate { get; set; }

        [JsonProperty("track_paths")]
        public string[] TrackPaths { get; set; }

        [JsonProperty("track_components")]
        public string[] TrackComponents { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }

        /// <summary>Duration in seconds.</summary>
        [JsonIgnore]
        public float Duration => EndTime - StartTime;
    }

    /// <summary>
    /// A single frame's captured property values for all tracked objects.
    /// Stored as delta-compressed JSON in SQLite.
    /// </summary>
    public sealed class FrameRecord
    {
        public int Frame;
        public float Time;

        /// <summary>
        /// Per-object property snapshots, keyed by instance_id.
        /// Only properties that CHANGED since previous frame are included
        /// (delta compression).
        /// </summary>
        public Dictionary<int, ObjectFrame> Objects;
    }

    /// <summary>
    /// A single object's property values for one frame.
    /// </summary>
    public sealed class ObjectFrame
    {
        public string Path;
        public int InstanceId;

        /// <summary>
        /// Changed property values, keyed by property name (snake_case).
        /// Values are JTokens matching the CONTRACTS.md wire format.
        /// </summary>
        public Dictionary<string, JToken> Properties;
    }

    /// <summary>
    /// A marker placed during recording.
    /// </summary>
    public sealed class MarkerRecord
    {
        [JsonProperty("frame")]
        public int Frame { get; set; }

        [JsonProperty("time")]
        public float Time { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("metadata")]
        public JObject Metadata { get; set; }
    }

    /// <summary>
    /// Structured analysis query for the analyze operation.
    /// </summary>
    public sealed class AnalyzeQuery
    {
        [JsonProperty("query")]
        public string Query { get; set; }  // "threshold", "min", "max", "count_changes"

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("property")]
        public string Property { get; set; }

        [JsonProperty("below")]
        public float? Below { get; set; }

        [JsonProperty("above")]
        public float? Above { get; set; }

        [JsonProperty("from_frame")]
        public int? FromFrame { get; set; }

        [JsonProperty("to_frame")]
        public int? ToFrame { get; set; }
    }

    /// <summary>
    /// Result of an analysis query.
    /// </summary>
    public sealed class AnalyzeResult
    {
        [JsonProperty("query")]
        public string Query { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("property")]
        public string Property { get; set; }

        [JsonProperty("matches")]
        public List<AnalyzeMatch> Matches { get; set; }

        /// <summary>For min/max queries: the extremal value.</summary>
        [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)]
        public float? Value { get; set; }

        /// <summary>For min/max queries: the frame at which it occurred.</summary>
        [JsonProperty("frame", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int Frame { get; set; }
    }

    /// <summary>
    /// A single match from a threshold analysis query.
    /// </summary>
    public sealed class AnalyzeMatch
    {
        [JsonProperty("frame")]
        public int Frame { get; set; }

        [JsonProperty("time")]
        public float Time { get; set; }

        [JsonProperty("value")]
        public JToken Value { get; set; }
    }

    /// <summary>
    /// Active recording state — what the engine needs to capture each tick.
    /// Not persisted to SQLite; persisted to SessionState for domain reload.
    /// </summary>
    public sealed class RecordingState
    {
        public string ClipId;
        public string Label;
        public string DbPath;
        public int CaptureRate;
        public string[] TrackPaths;    // glob patterns
        public string[] TrackComponents;
        public int StartFrame;
        public float StartTime;
        public int FrameCounter;       // frames captured so far
        public int TickCounter;        // update ticks since start (for rate limiting)
    }
}
