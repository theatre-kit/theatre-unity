using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Theatre.Stage
{
    /// <summary>
    /// Supported watch condition types.
    /// </summary>
    public enum WatchConditionType
    {
        Threshold,
        Proximity,
        EnteredRegion,
        PropertyChanged,
        Destroyed,
        Spawned
    }

    /// <summary>
    /// A watch condition definition. Deserialized from the "condition" field
    /// in watch:create params.
    /// </summary>
    public sealed class WatchCondition
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>Property name for threshold/property_changed.</summary>
        [JsonProperty("property", NullValueHandling = NullValueHandling.Ignore)]
        public string Property { get; set; }

        /// <summary>Threshold: trigger when value drops below this.</summary>
        [JsonProperty("below", NullValueHandling = NullValueHandling.Ignore)]
        public float? Below { get; set; }

        /// <summary>Threshold: trigger when value rises above this.</summary>
        [JsonProperty("above", NullValueHandling = NullValueHandling.Ignore)]
        public float? Above { get; set; }

        /// <summary>Proximity: path of the other object.</summary>
        [JsonProperty("target", NullValueHandling = NullValueHandling.Ignore)]
        public string Target { get; set; }

        /// <summary>Proximity: trigger when distance is within this.</summary>
        [JsonProperty("within", NullValueHandling = NullValueHandling.Ignore)]
        public float? Within { get; set; }

        /// <summary>Proximity: trigger when distance exceeds this.</summary>
        [JsonProperty("beyond", NullValueHandling = NullValueHandling.Ignore)]
        public float? Beyond { get; set; }

        /// <summary>Region: AABB min corner [x,y,z].</summary>
        [JsonProperty("min", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Min { get; set; }

        /// <summary>Region: AABB max corner [x,y,z].</summary>
        [JsonProperty("max", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Max { get; set; }

        /// <summary>Spawned: name pattern (glob).</summary>
        [JsonProperty("name_pattern", NullValueHandling = NullValueHandling.Ignore)]
        public string NamePattern { get; set; }
    }

    /// <summary>
    /// A complete watch definition — condition + metadata.
    /// Serialized to SessionState for domain reload persistence.
    /// </summary>
    public sealed class WatchDefinition
    {
        [JsonProperty("watch_id")]
        public string WatchId { get; set; }

        [JsonProperty("target")]
        public string Target { get; set; }

        [JsonProperty("track")]
        public string[] Track { get; set; }

        [JsonProperty("condition", NullValueHandling = NullValueHandling.Ignore)]
        public WatchCondition Condition { get; set; }

        [JsonProperty("throttle_ms")]
        public int ThrottleMs { get; set; } = 500;

        [JsonProperty("label", NullValueHandling = NullValueHandling.Ignore)]
        public string Label { get; set; }

        /// <summary>Frame when this watch was created.</summary>
        [JsonProperty("created_frame")]
        public int CreatedFrame { get; set; }
    }

    /// <summary>
    /// Runtime state for an active watch. Not persisted — rebuilt after
    /// domain reload.
    /// </summary>
    public sealed class WatchState
    {
        /// <summary>The watch definition.</summary>
        public WatchDefinition Definition;

        /// <summary>Realtime when the watch last triggered.</summary>
        public double LastTriggeredAt;

        /// <summary>Total trigger count.</summary>
        public int TriggerCount;

        /// <summary>
        /// Cached previous value for property_changed condition.
        /// Stored as JToken for type-agnostic comparison.
        /// </summary>
        public JToken PreviousValue;

        /// <summary>
        /// Cached instance_id of the target object.
        /// Used for destroyed detection and fast re-resolve.
        /// </summary>
        public int CachedInstanceId;

        /// <summary>Whether the target was resolved at least once.</summary>
        public bool TargetResolved;
    }
}
