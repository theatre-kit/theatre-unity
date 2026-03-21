using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Stage
{
    /// <summary>
    /// Captures per-frame property snapshots for tracked GameObjects.
    /// Uses fast-path direct access for Transform and common built-in
    /// components; falls back to SerializedProperty for custom MonoBehaviours.
    /// </summary>
    public static class FrameSerializer
    {
        /// <summary>
        /// Capture a full snapshot of all tracked objects.
        /// Returns a dictionary keyed by instance_id.
        /// </summary>
        public static Dictionary<int, ObjectFrame> CaptureFullSnapshot(
            List<GameObject> trackedObjects,
            string[] trackComponents)
        {
            if (trackedObjects == null) return new Dictionary<int, ObjectFrame>();

            var result = new Dictionary<int, ObjectFrame>(trackedObjects.Count);
            foreach (var go in trackedObjects)
            {
                if (go == null) continue;

#pragma warning disable CS0618
                int instanceId = go.GetInstanceID();
#pragma warning restore CS0618

                var props = new Dictionary<string, JToken>();
                CaptureComponents(go, trackComponents, props);

                result[instanceId] = new ObjectFrame
                {
                    Path = ResponseHelpers.GetHierarchyPath(go.transform),
                    InstanceId = instanceId,
                    Properties = props,
                };
            }
            return result;
        }

        /// <summary>
        /// Capture a delta frame: only properties that changed since
        /// the previous snapshot. Objects with no changes are omitted.
        /// </summary>
        public static Dictionary<int, ObjectFrame> CaptureDelta(
            List<GameObject> trackedObjects,
            string[] trackComponents,
            Dictionary<int, ObjectFrame> previousSnapshot)
        {
            if (trackedObjects == null) return new Dictionary<int, ObjectFrame>();

            var result = new Dictionary<int, ObjectFrame>();

            // Track which IDs we've seen (to detect destroyed objects)
            var seenIds = new HashSet<int>();

            foreach (var go in trackedObjects)
            {
                if (go == null)
                {
                    // Object destroyed — emit sentinel if it was in previous snapshot
                    // We can't get its ID anymore; skip (the caller should track destroyed objects)
                    continue;
                }

#pragma warning disable CS0618
                int instanceId = go.GetInstanceID();
#pragma warning restore CS0618

                seenIds.Add(instanceId);

                var props = new Dictionary<string, JToken>();
                CaptureComponents(go, trackComponents, props);

                // Compare against previous snapshot
                previousSnapshot?.TryGetValue(instanceId, out var prevFrame);

                var changedProps = new Dictionary<string, JToken>();
                foreach (var kvp in props)
                {
                    JToken prevVal = null;
                    prevFrame?.Properties?.TryGetValue(kvp.Key, out prevVal);
                    if (prevVal == null || !JToken.DeepEquals(prevVal, kvp.Value))
                    {
                        changedProps[kvp.Key] = kvp.Value;
                    }
                }

                if (changedProps.Count > 0)
                {
                    result[instanceId] = new ObjectFrame
                    {
                        Path = ResponseHelpers.GetHierarchyPath(go.transform),
                        InstanceId = instanceId,
                        Properties = changedProps,
                    };
                }
            }

            // Detect destroyed objects that were in previous snapshot
            if (previousSnapshot != null)
            {
                foreach (var kvp in previousSnapshot)
                {
                    if (!seenIds.Contains(kvp.Key))
                    {
                        result[kvp.Key] = new ObjectFrame
                        {
                            Path = kvp.Value.Path,
                            InstanceId = kvp.Key,
                            Properties = new Dictionary<string, JToken>
                            {
                                { "destroyed", JValue.CreateString("true") },
                            },
                        };
                    }
                }
            }

            return result;
        }

        private static void CaptureComponents(
            GameObject go,
            string[] trackComponents,
            Dictionary<string, JToken> props)
        {
            bool captureAll = trackComponents == null || trackComponents.Length == 0;

            // Transform — always captured unless filtered out
            if (captureAll || ContainsComponent(trackComponents, "Transform"))
            {
                CaptureTransform(go.transform, props);
            }

            if (captureAll || ContainsComponent(trackComponents, "Rigidbody"))
            {
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) CaptureRigidbody(rb, props);
            }

            if (captureAll || ContainsComponent(trackComponents, "Rigidbody2D"))
            {
                var rb2d = go.GetComponent<Rigidbody2D>();
                if (rb2d != null) CaptureRigidbody2D(rb2d, props);
            }

            // Fallback for other components
#if UNITY_EDITOR
            if (trackComponents != null)
            {
                foreach (var compName in trackComponents)
                {
                    if (compName == "Transform" || compName == "Rigidbody" || compName == "Rigidbody2D")
                        continue;
                    var comps = go.GetComponents<Component>();
                    foreach (var comp in comps)
                    {
                        if (comp == null) continue;
                        if (comp.GetType().Name == compName)
                        {
                            CaptureViaSerializedProperty(comp, props);
                        }
                    }
                }
            }
#endif
        }

        private static bool ContainsComponent(string[] trackComponents, string name)
        {
            if (trackComponents == null) return false;
            foreach (var c in trackComponents)
                if (c == name) return true;
            return false;
        }

        /// <summary>
        /// Fast-path: read Transform properties directly.
        /// Avoids SerializedProperty overhead (~10x faster).
        /// </summary>
        internal static void CaptureTransform(Transform t, Dictionary<string, JToken> props)
        {
            props["position"] = ResponseHelpers.ToJArray(t.position);
            props["euler_angles"] = ResponseHelpers.ToJArray(t.eulerAngles);
            props["local_position"] = ResponseHelpers.ToJArray(t.localPosition);
            props["local_euler_angles"] = ResponseHelpers.ToJArray(t.localEulerAngles);
            props["local_scale"] = ResponseHelpers.ToJArray(t.localScale);
        }

        /// <summary>
        /// Fast-path: read Rigidbody properties directly.
        /// </summary>
        internal static void CaptureRigidbody(Rigidbody rb, Dictionary<string, JToken> props)
        {
            props["velocity"] = ResponseHelpers.ToJArray(rb.linearVelocity);
            props["angular_velocity"] = ResponseHelpers.ToJArray(rb.angularVelocity);
            props["is_kinematic"] = new JValue(rb.isKinematic);
            props["mass"] = new JValue(Math.Round(rb.mass, 4));
        }

        /// <summary>
        /// Fast-path: read Rigidbody2D properties directly.
        /// </summary>
        internal static void CaptureRigidbody2D(Rigidbody2D rb, Dictionary<string, JToken> props)
        {
            props["velocity"] = ResponseHelpers.ToJArray(rb.linearVelocity);
            props["angular_velocity"] = new JValue(Math.Round(rb.angularVelocity, 4));
            props["is_kinematic"] = new JValue(rb.isKinematic);
            props["mass"] = new JValue(Math.Round(rb.mass, 4));
        }

#if UNITY_EDITOR
        /// <summary>
        /// Fallback: read properties via SerializedProperty.
        /// Used for custom MonoBehaviours and uncommon built-in types.
        /// </summary>
        internal static void CaptureViaSerializedProperty(
            Component comp, Dictionary<string, JToken> props)
        {
            if (comp == null) return;
            try
            {
                using (var so = new SerializedObject(comp))
                {
                    var prop = so.GetIterator();
                    if (!prop.NextVisible(true)) return;

                    do
                    {
                        if (prop.name == "m_Script") continue;
                        var token = SerializedPropertyToJToken(prop);
                        if (token != null)
                        {
                            props[prop.name] = token;
                        }
                    }
                    while (prop.NextVisible(false));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Theatre] FrameSerializer: SerializedProperty fallback failed for {comp.GetType().Name}: {ex.Message}");
            }
        }

        private static JToken SerializedPropertyToJToken(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return new JValue(prop.intValue);
                case SerializedPropertyType.Float:
                    return new JValue(Math.Round(prop.floatValue, 4));
                case SerializedPropertyType.Boolean:
                    return new JValue(prop.boolValue);
                case SerializedPropertyType.String:
                    return new JValue(prop.stringValue);
                case SerializedPropertyType.Enum:
                    return new JValue(prop.enumNames[prop.enumValueIndex]);
                case SerializedPropertyType.Vector2:
                    return ResponseHelpers.ToJArray(prop.vector2Value);
                case SerializedPropertyType.Vector3:
                    return ResponseHelpers.ToJArray(prop.vector3Value);
                case SerializedPropertyType.Color:
                    return ResponseHelpers.ToJArray(prop.colorValue);
                case SerializedPropertyType.ObjectReference:
                    if (prop.objectReferenceValue != null)
                        return new JValue(prop.objectReferenceValue.name);
                    return JValue.CreateNull();
                default:
                    return null;
            }
        }
#endif
    }
}
