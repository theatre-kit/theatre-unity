using System;
using Newtonsoft.Json.Linq;
using Theatre;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Stage
{
    /// <summary>
    /// Evaluates watch conditions against live scene state.
    /// All Evaluate* methods return a JObject trigger payload when the
    /// condition is met, or null if not.
    /// </summary>
    internal static class WatchConditions
    {
        /// <summary>
        /// Evaluate a single watch. Returns a JObject with trigger details
        /// if the condition is met, null otherwise.
        /// </summary>
        public static JObject Evaluate(WatchState ws)
        {
            var def = ws.Definition;
            var condition = def.Condition;
            if (condition == null) return null;

            switch (condition.Type)
            {
                case "threshold":
                    return EvaluateThreshold(ws);
                case "proximity":
                    return EvaluateProximity(ws);
                case "entered_region":
                    return EvaluateRegion(ws);
                case "property_changed":
                    return EvaluatePropertyChanged(ws);
                case "destroyed":
                    return EvaluateDestroyed(ws);
                case "spawned":
                    return EvaluateSpawned(ws);
                default:
                    return null;
            }
        }

        private static JObject EvaluateThreshold(WatchState ws)
        {
            var go = ResolveTarget(ws);
            if (go == null) return null;

            var value = ReadTrackedPropertyFloat(go, ws.Definition.Condition.Property);
            if (!value.HasValue) return null;

            bool triggered = false;
            if (ws.Definition.Condition.Below.HasValue
                && value.Value < ws.Definition.Condition.Below.Value)
                triggered = true;
            if (ws.Definition.Condition.Above.HasValue
                && value.Value > ws.Definition.Condition.Above.Value)
                triggered = true;

            if (!triggered) return null;

            var trigger = new JObject();
            trigger["condition"] = "threshold";
            trigger["property"] = ws.Definition.Condition.Property;
            trigger["value"] = Math.Round(value.Value, 4);
            if (ws.Definition.Condition.Below.HasValue)
                trigger["threshold"] = ws.Definition.Condition.Below.Value;
            if (ws.Definition.Condition.Above.HasValue)
                trigger["threshold"] = ws.Definition.Condition.Above.Value;
            AddTargetFields(trigger, go);
            return trigger;
        }

        private static JObject EvaluateProximity(WatchState ws)
        {
            var go = ResolveTarget(ws);
            if (go == null) return null;

            var otherResult = ObjectResolver.Resolve(
                path: ws.Definition.Condition.Target);
            if (!otherResult.Success) return null;

            float distance = Vector3.Distance(
                go.transform.position,
                otherResult.GameObject.transform.position);

            bool triggered = false;
            if (ws.Definition.Condition.Within.HasValue
                && distance <= ws.Definition.Condition.Within.Value)
                triggered = true;
            if (ws.Definition.Condition.Beyond.HasValue
                && distance > ws.Definition.Condition.Beyond.Value)
                triggered = true;

            if (!triggered) return null;

            var trigger = new JObject();
            trigger["condition"] = "proximity";
            trigger["distance"] = Math.Round(distance, 2);
            if (ws.Definition.Condition.Within.HasValue)
                trigger["within"] = ws.Definition.Condition.Within.Value;
            if (ws.Definition.Condition.Beyond.HasValue)
                trigger["beyond"] = ws.Definition.Condition.Beyond.Value;
            trigger["other_path"] = ws.Definition.Condition.Target;
            AddTargetFields(trigger, go);
            return trigger;
        }

        private static JObject EvaluateRegion(WatchState ws)
        {
            var go = ResolveTarget(ws);
            if (go == null) return null;

            var min = ws.Definition.Condition.Min;
            var max = ws.Definition.Condition.Max;
            if (min == null || max == null || min.Length < 3 || max.Length < 3)
                return null;

            var pos = go.transform.position;
            bool inside = pos.x >= min[0] && pos.x <= max[0]
                       && pos.y >= min[1] && pos.y <= max[1]
                       && pos.z >= min[2] && pos.z <= max[2];

            if (!inside) return null;

            var trigger = new JObject();
            trigger["condition"] = "entered_region";
            trigger["position"] = ResponseHelpers.ToJArray(pos);
            trigger["min"] = new JArray(min);
            trigger["max"] = new JArray(max);
            AddTargetFields(trigger, go);
            return trigger;
        }

        private static JObject EvaluatePropertyChanged(WatchState ws)
        {
            var go = ResolveTarget(ws);
            if (go == null) return null;

            var propName = ws.Definition.Condition.Property;
            var currentValue = ReadTrackedProperty(go, propName);
            if (currentValue == null) return null;

            if (ws.PreviousValue != null
                && JToken.DeepEquals(ws.PreviousValue, currentValue))
                return null;

            var trigger = new JObject();
            trigger["condition"] = "property_changed";
            trigger["property"] = propName;
            trigger["from"] = ws.PreviousValue;
            trigger["to"] = currentValue;
            AddTargetFields(trigger, go);

            ws.PreviousValue = currentValue;
            return trigger;
        }

        private static JObject EvaluateDestroyed(WatchState ws)
        {
            if (!ws.TargetResolved) return null;
            if (IsAlive(ws.CachedInstanceId)) return null;

            var trigger = new JObject();
            trigger["condition"] = "destroyed";
            trigger["path"] = ws.Definition.Target;
            trigger["instance_id"] = ws.CachedInstanceId;
            return trigger;
        }

        private static JObject EvaluateSpawned(WatchState ws)
        {
            // TODO: Track known root objects and detect new ones matching
            // the name_pattern glob. Implementation deferred to integration
            // with scene_delta's spawned tracking.
            return null;
        }

        // --- Helpers ---

        internal static GameObject ResolveTarget(WatchState ws)
        {
            if (ws.TargetResolved && IsAlive(ws.CachedInstanceId))
            {
#if UNITY_EDITOR
                var obj = EditorUtility.InstanceIDToObject(ws.CachedInstanceId);
                if (obj is GameObject go) return go;
                if (obj is Component comp) return comp.gameObject;
#endif
            }

            // Re-resolve by path
            var result = ObjectResolver.Resolve(path: ws.Definition.Target);
            if (!result.Success) return null;

            var resolved = result.GameObject;
#pragma warning disable CS0618
            ws.CachedInstanceId = resolved.GetInstanceID();
#pragma warning restore CS0618
            ws.TargetResolved = true;
            return resolved;
        }

        internal static bool IsAlive(int instanceId)
        {
#if UNITY_EDITOR
            return EditorUtility.InstanceIDToObject(instanceId) != null;
#else
            return false;
#endif
        }

        private static void AddTargetFields(JObject obj, GameObject go)
        {
            ResponseHelpers.AddIdentity(obj, go);
        }

        /// <summary>
        /// Read a property value from any component on the GameObject,
        /// searching all components for the named property.
        /// </summary>
        internal static JToken ReadTrackedProperty(
            GameObject go, string propertyName)
        {
#if UNITY_EDITOR
            var components = go.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                var so = new SerializedObject(comp);
                SerializedProperty prop = null;
                foreach (var candidate in StringUtils.GetPropertyNameCandidates(propertyName))
                {
                    prop = so.FindProperty(candidate);
                    if (prop != null) break;
                }
                if (prop != null)
                {
                    return GetPropertyValueAsToken(prop);
                }
            }
#endif
            return null;
        }

        /// <summary>
        /// Read a float property from any component on the GameObject.
        /// </summary>
        private static float? ReadTrackedPropertyFloat(
            GameObject go, string propertyName)
        {
            var token = ReadTrackedProperty(go, propertyName);
            if (token == null) return null;
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return token.ToObject<float>();
            return null;
        }

#if UNITY_EDITOR
        private static JToken GetPropertyValueAsToken(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue;
                case SerializedPropertyType.Float:
                    return Math.Round(prop.floatValue, 4);
                case SerializedPropertyType.Boolean:
                    return prop.boolValue;
                case SerializedPropertyType.String:
                    return prop.stringValue;
                case SerializedPropertyType.Vector3:
                    return ResponseHelpers.ToJArray(prop.vector3Value);
                case SerializedPropertyType.Vector2:
                    return ResponseHelpers.ToJArray(prop.vector2Value);
                default:
                    return prop.propertyType.ToString();
            }
        }
#endif

    }
}
