using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor.Tools.Scene
{
    /// <summary>
    /// MCP tool: scene_delta
    /// Reports what changed since a previous frame or query.
    /// Stores baselines in a ring buffer for comparison.
    /// </summary>
    public static class SceneDeltaTool
    {
        private static readonly JToken s_inputSchema;
        private const int MaxBaselines = 5;

        /// <summary>
        /// A snapshot of tracked object states at a given frame.
        /// </summary>
        private sealed class Baseline
        {
            public int Frame;
            public float Time;
            public Dictionary<int, ObjectSnapshot> Objects; // keyed by instance_id
        }

        /// <summary>
        /// Snapshot of a single object's tracked properties.
        /// </summary>
        private sealed class ObjectSnapshot
        {
            public string Path;
            public int InstanceId;
            public Vector3 Position;
            public Vector3 EulerAngles;
            public Vector3 LocalScale;
            public Dictionary<string, JToken> TrackedProperties;
        }

        private static readonly List<Baseline> s_baselines = new();
        private static int s_lastBaselineFrame = -1;

        static SceneDeltaTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""since_frame"": {
                        ""type"": ""integer"",
                        ""description"": ""Compare against this frame number. If omitted, compares against the last scene_delta or scene_snapshot call.""
                    },
                    ""paths"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Limit to specific object paths.""
                    },
                    ""track"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Additional properties to track beyond transforms, e.g., ['current_hp', 'velocity'].""
                    },
                    ""budget"": {
                        ""type"": ""integer"",
                        ""default"": 1500,
                        ""minimum"": 100,
                        ""maximum"": 4000,
                        ""description"": ""Token budget for the response.""
                    }
                },
                ""required"": []
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "scene_delta",
                description: "What changed in the scene since the last query "
                    + "or a specific frame. Reports position/property changes, "
                    + "spawned objects, and destroyed objects. Call with no "
                    + "arguments to get changes since the last scene_delta call.",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageGameObject,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = true
                }
            ));
        }

        /// <summary>
        /// Store a baseline for the current frame. Called internally after
        /// scene_snapshot or scene_delta to establish comparison points.
        /// </summary>
        internal static void CaptureBaseline(
            string[] paths = null, string[] track = null)
        {
            int frame = UnityEngine.Time.frameCount;
            if (frame == s_lastBaselineFrame) return;

            var baseline = new Baseline
            {
                Frame = frame,
                Time = UnityEngine.Time.time,
                Objects = new Dictionary<int, ObjectSnapshot>()
            };

            var roots = ObjectResolver.GetAllRoots();
            foreach (var root in roots)
            {
                CaptureObject(baseline, root.transform, paths, track);
            }

            s_baselines.Add(baseline);
            if (s_baselines.Count > MaxBaselines)
                s_baselines.RemoveAt(0);

            s_lastBaselineFrame = frame;
        }

        private static void CaptureObject(
            Baseline baseline, Transform transform,
            string[] pathFilter, string[] track)
        {
            var path = ResponseHelpers.GetHierarchyPath(transform);

            // Apply path filter
            if (pathFilter != null && !pathFilter.Any(p => path == p || path.StartsWith(p + "/")))
            {
                // Path doesn't match filter — skip this object but still recurse children
                for (int i = 0; i < transform.childCount; i++)
                    CaptureObject(baseline, transform.GetChild(i), pathFilter, track);
                return;
            }

#pragma warning disable CS0618
            var instanceId = transform.gameObject.GetInstanceID();
#pragma warning restore CS0618

            var snapshot = new ObjectSnapshot
            {
                Path = path,
                InstanceId = instanceId,
                Position = transform.position,
                EulerAngles = transform.eulerAngles,
                LocalScale = transform.localScale,
                TrackedProperties = new Dictionary<string, JToken>()
            };

            // Read tracked properties
            if (track != null)
            {
                foreach (var propName in track)
                {
                    var value = ReadProperty(transform.gameObject, propName);
                    if (value != null)
                        snapshot.TrackedProperties[propName] = value;
                }
            }

            baseline.Objects[instanceId] = snapshot;

            for (int i = 0; i < transform.childCount; i++)
                CaptureObject(baseline, transform.GetChild(i), pathFilter, track);
        }

        private static JToken ReadProperty(GameObject go, string propName)
        {
            // Transform shortcuts
            switch (propName)
            {
                case "position":
                    return ResponseHelpers.ToJArray(go.transform.position);
                case "euler_angles":
                    return ResponseHelpers.ToJArray(go.transform.eulerAngles);
                case "local_scale":
                    return ResponseHelpers.ToJArray(go.transform.localScale);
                case "velocity":
                    var rb = go.GetComponent<Rigidbody>();
                    if (rb != null)
                        return ResponseHelpers.ToJArray(rb.linearVelocity);
                    return null;
            }

            // Generic SerializedProperty lookup
#if UNITY_EDITOR
            var components = go.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                var so = new SerializedObject(comp);
                var prop = so.FindProperty(propName);
                if (prop != null)
                {
                    switch (prop.propertyType)
                    {
                        case SerializedPropertyType.Integer: return prop.intValue;
                        case SerializedPropertyType.Float:
                            return Math.Round(prop.floatValue, 4);
                        case SerializedPropertyType.Boolean: return prop.boolValue;
                        case SerializedPropertyType.String: return prop.stringValue;
                        case SerializedPropertyType.Vector3:
                            return ResponseHelpers.ToJArray(prop.vector3Value);
                        default: return prop.propertyType.ToString();
                    }
                }
            }
#endif
            return null;
        }

        private static string Execute(JToken arguments)
        {
            var args = arguments as JObject ?? new JObject();
            var sinceFrame = args["since_frame"]?.Value<int>();
            var paths = args["paths"]?.ToObject<string[]>();
            var track = args["track"]?.ToObject<string[]>();

            // Find the baseline to compare against
            Baseline baseline = null;
            if (sinceFrame.HasValue)
            {
                foreach (var b in s_baselines)
                {
                    if (b.Frame == sinceFrame.Value)
                    {
                        baseline = b;
                        break;
                    }
                }
                if (baseline == null)
                {
                    // Find closest
                    foreach (var b in s_baselines)
                    {
                        if (b.Frame <= sinceFrame.Value)
                            baseline = b;
                    }
                }
            }
            else if (s_baselines.Count > 0)
            {
                baseline = s_baselines[s_baselines.Count - 1];
            }

            // Capture current state as new baseline
            CaptureBaseline(paths, track);
            var current = s_baselines[s_baselines.Count - 1];

            if (baseline == null || baseline == current)
            {
                // No previous baseline — return current state summary
                var firstResponse = new JObject();
                firstResponse["from_frame"] = current.Frame;
                firstResponse["to_frame"] = current.Frame;
                firstResponse["elapsed_seconds"] = 0;
                firstResponse["changes"] = new JArray();
                firstResponse["spawned"] = new JArray();
                firstResponse["destroyed"] = new JArray();
                firstResponse["note"] = "First call — baseline captured. "
                    + "Call again to see changes.";
                ResponseHelpers.AddFrameContext(firstResponse);
                return firstResponse.ToString(Formatting.None);
            }

            // Compare baseline vs current
            var changes = new JArray();
            var spawned = new JArray();
            var destroyed = new JArray();

            // Detect changes and destroyed
            foreach (var kvp in baseline.Objects)
            {
                int id = kvp.Key;
                var oldSnap = kvp.Value;

                if (!current.Objects.ContainsKey(id))
                {
                    var d = new JObject();
                    d["path"] = oldSnap.Path;
                    d["instance_id"] = oldSnap.InstanceId;
                    destroyed.Add(d);
                    continue;
                }

                var newSnap = current.Objects[id];
                var changed = new JObject();
                bool hasChanges = false;

                // Compare position
                if (Vector3.Distance(oldSnap.Position, newSnap.Position) > 0.001f)
                {
                    var posChange = new JObject();
                    posChange["from"] = ResponseHelpers.ToJArray(oldSnap.Position);
                    posChange["to"] = ResponseHelpers.ToJArray(newSnap.Position);
                    changed["position"] = posChange;
                    hasChanges = true;
                }

                // Compare euler angles
                if (Vector3.Distance(oldSnap.EulerAngles, newSnap.EulerAngles) > 0.01f)
                {
                    var rotChange = new JObject();
                    rotChange["from"] = ResponseHelpers.ToJArray(oldSnap.EulerAngles);
                    rotChange["to"] = ResponseHelpers.ToJArray(newSnap.EulerAngles);
                    changed["euler_angles"] = rotChange;
                    hasChanges = true;
                }

                // Compare scale
                if (Vector3.Distance(oldSnap.LocalScale, newSnap.LocalScale) > 0.001f)
                {
                    var scaleChange = new JObject();
                    scaleChange["from"] = ResponseHelpers.ToJArray(oldSnap.LocalScale);
                    scaleChange["to"] = ResponseHelpers.ToJArray(newSnap.LocalScale);
                    changed["local_scale"] = scaleChange;
                    hasChanges = true;
                }

                // Compare tracked properties
                foreach (var propKvp in newSnap.TrackedProperties)
                {
                    JToken oldVal = null;
                    oldSnap.TrackedProperties?.TryGetValue(propKvp.Key, out oldVal);

                    if (oldVal == null || !JToken.DeepEquals(oldVal, propKvp.Value))
                    {
                        var propChange = new JObject();
                        propChange["from"] = oldVal;
                        propChange["to"] = propKvp.Value;
                        changed[propKvp.Key] = propChange;
                        hasChanges = true;
                    }
                }

                if (hasChanges)
                {
                    var entry = new JObject();
                    entry["path"] = newSnap.Path;
                    entry["instance_id"] = newSnap.InstanceId;
                    entry["changed"] = changed;
                    changes.Add(entry);
                }
            }

            // Detect spawned
            foreach (var kvp in current.Objects)
            {
                if (!baseline.Objects.ContainsKey(kvp.Key))
                {
                    var s = new JObject();
                    s["path"] = kvp.Value.Path;
                    s["instance_id"] = kvp.Value.InstanceId;
                    s["position"] = ResponseHelpers.ToJArray(kvp.Value.Position);
                    spawned.Add(s);
                }
            }

            var response = new JObject();
            response["from_frame"] = baseline.Frame;
            response["to_frame"] = current.Frame;
            response["elapsed_seconds"] = Math.Round(
                current.Time - baseline.Time, 2);
            response["changes"] = changes;
            response["spawned"] = spawned;
            response["destroyed"] = destroyed;
            ResponseHelpers.AddFrameContext(response);

            return response.ToString(Formatting.None);
        }
    }
}
