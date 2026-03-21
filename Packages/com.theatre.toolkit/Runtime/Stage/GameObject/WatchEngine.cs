using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Stage
{
    /// <summary>
    /// Evaluates active watches on a periodic interval and fires SSE
    /// notifications when conditions are met. Persists watch definitions
    /// to SessionState for domain reload survival.
    /// </summary>
    public sealed class WatchEngine
    {
        private const string SessionKey = "Theatre_Watches";
        private const string CounterKey = "Theatre_WatchCounter";
        private const int MaxWatches = 32;
        private const int DefaultPlayModeInterval = 10;
        private const int DefaultEditModeInterval = 60;

        private readonly List<WatchState> _watches = new();
        private int _nextId;
        private int _evalTick;
        private Action<JObject> _notifyCallback;

        /// <summary>Number of active watches.</summary>
        public int Count => _watches.Count;

        /// <summary>
        /// Initialize the engine. Call once at startup.
        /// Restores persisted watches from SessionState.
        /// </summary>
        /// <param name="notifyCallback">
        /// Called with the notification params JObject when a watch triggers.
        /// The caller is responsible for wrapping this in a JSON-RPC
        /// notification and pushing it to SSE.
        /// </param>
        public void Initialize(Action<JObject> notifyCallback)
        {
            _notifyCallback = notifyCallback;
            RestoreFromSessionState();
        }

        /// <summary>
        /// Called every editor update tick. Evaluates watches at the
        /// configured interval.
        /// </summary>
        public void Tick()
        {
            if (_watches.Count == 0) return;

            _evalTick++;
            int interval = Application.isPlaying
                ? DefaultPlayModeInterval
                : DefaultEditModeInterval;

            if (_evalTick % interval != 0) return;

            EvaluateAll();
        }

        /// <summary>
        /// Create a new watch. Returns the watch_id.
        /// </summary>
        public string Create(WatchDefinition def)
        {
            if (_watches.Count >= MaxWatches)
                return null; // Caller handles error response

            _nextId++;
            def.WatchId = $"w_{_nextId:D2}";
            def.CreatedFrame = Time.frameCount;

            var state = new WatchState
            {
                Definition = def,
                LastTriggeredAt = 0,
                TriggerCount = 0,
                PreviousValue = null,
                CachedInstanceId = 0,
                TargetResolved = false
            };

            // Initial target resolution
            if (!string.IsNullOrEmpty(def.Target) && def.Target != "*")
            {
                var result = ObjectResolver.Resolve(path: def.Target);
                if (result.Success)
                {
#pragma warning disable CS0618
                    state.CachedInstanceId = result.GameObject.GetInstanceID();
#pragma warning restore CS0618
                    state.TargetResolved = true;

                    // Snapshot initial property value for property_changed
                    if (def.Condition?.Type == "property_changed"
                        && !string.IsNullOrEmpty(def.Condition.Property))
                    {
                        state.PreviousValue = ReadTrackedProperty(
                            result.GameObject, def.Condition.Property);
                    }
                }
            }

            _watches.Add(state);
            PersistToSessionState();

            return def.WatchId;
        }

        /// <summary>
        /// Remove a watch by ID. Returns true if found and removed.
        /// </summary>
        public bool Remove(string watchId)
        {
            for (int i = 0; i < _watches.Count; i++)
            {
                if (_watches[i].Definition.WatchId == watchId)
                {
                    _watches.RemoveAt(i);
                    PersistToSessionState();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// List all active watches as JArray.
        /// </summary>
        public JArray ListAll()
        {
            var arr = new JArray();
            foreach (var ws in _watches)
            {
                var obj = new JObject();
                obj["watch_id"] = ws.Definition.WatchId;
                obj["target"] = ws.Definition.Target;
                obj["track"] = ws.Definition.Track != null
                    ? new JArray(ws.Definition.Track)
                    : null;
                if (ws.Definition.Label != null)
                    obj["label"] = ws.Definition.Label;
                obj["throttle_ms"] = ws.Definition.ThrottleMs;
                obj["trigger_count"] = ws.TriggerCount;
                obj["target_alive"] = ws.TargetResolved
                    && IsAlive(ws.CachedInstanceId);
                if (ws.Definition.Condition != null)
                    obj["condition"] = JObject.FromObject(ws.Definition.Condition);
                arr.Add(obj);
            }
            return arr;
        }

        /// <summary>
        /// Manually check a watch's current state without waiting for
        /// the polling loop.
        /// </summary>
        public JObject Check(string watchId)
        {
            foreach (var ws in _watches)
            {
                if (ws.Definition.WatchId != watchId) continue;

                var obj = new JObject();
                obj["watch_id"] = watchId;
                obj["target"] = ws.Definition.Target;
                obj["trigger_count"] = ws.TriggerCount;
                obj["target_alive"] = ws.TargetResolved
                    && IsAlive(ws.CachedInstanceId);

                // Evaluate condition right now
                var trigger = Evaluate(ws);
                obj["condition_met"] = trigger != null;
                if (trigger != null)
                    obj["current_value"] = trigger;

                ResponseHelpers.AddFrameContext(obj);
                return obj;
            }
            return null;
        }

        // --- Evaluation ---

        private void EvaluateAll()
        {
#if UNITY_EDITOR
            double now = EditorApplication.timeSinceStartup;
#else
            double now = Time.realtimeSinceStartup;
#endif

            for (int i = _watches.Count - 1; i >= 0; i--)
            {
                var ws = _watches[i];

                // Throttle check
                if (ws.LastTriggeredAt > 0)
                {
                    double elapsed = (now - ws.LastTriggeredAt) * 1000.0;
                    if (elapsed < ws.Definition.ThrottleMs)
                        continue;
                }

                var trigger = Evaluate(ws);
                if (trigger != null)
                {
                    ws.LastTriggeredAt = now;
                    ws.TriggerCount++;
                    FireNotification(ws, trigger);
                }
            }
        }

        /// <summary>
        /// Evaluate a single watch. Returns a JObject with trigger details
        /// if the condition is met, null otherwise.
        /// </summary>
        private JObject Evaluate(WatchState ws)
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

        private JObject EvaluateThreshold(WatchState ws)
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

        private JObject EvaluateProximity(WatchState ws)
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

        private JObject EvaluateRegion(WatchState ws)
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

        private JObject EvaluatePropertyChanged(WatchState ws)
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

        private JObject EvaluateDestroyed(WatchState ws)
        {
            if (!ws.TargetResolved) return null;
            if (IsAlive(ws.CachedInstanceId)) return null;

            var trigger = new JObject();
            trigger["condition"] = "destroyed";
            trigger["path"] = ws.Definition.Target;
            trigger["instance_id"] = ws.CachedInstanceId;
            return trigger;
        }

        private JObject EvaluateSpawned(WatchState ws)
        {
            // TODO: Track known root objects and detect new ones matching
            // the name_pattern glob. Implementation deferred to integration
            // with scene_delta's spawned tracking.
            return null;
        }

        // --- Helpers ---

        private GameObject ResolveTarget(WatchState ws)
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

        private static bool IsAlive(int instanceId)
        {
#if UNITY_EDITOR
            return EditorUtility.InstanceIDToObject(instanceId) != null;
#else
            return false;
#endif
        }

        private static void AddTargetFields(JObject obj, GameObject go)
        {
            obj["path"] = ResponseHelpers.GetHierarchyPath(go.transform);
#pragma warning disable CS0618
            obj["instance_id"] = go.GetInstanceID();
#pragma warning restore CS0618
        }

        /// <summary>
        /// Read a property value from any component on the GameObject,
        /// searching all components for the named property.
        /// </summary>
        private static JToken ReadTrackedProperty(
            GameObject go, string propertyName)
        {
#if UNITY_EDITOR
            var components = go.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                var so = new SerializedObject(comp);
                var prop = so.FindProperty(propertyName);
                if (prop == null)
                {
                    // Try with m_ prefix (Unity internal naming)
                    prop = so.FindProperty("m_" + ToPascalCase(propertyName));
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

        private static string ToPascalCase(string snakeCase)
        {
            if (string.IsNullOrEmpty(snakeCase)) return snakeCase;
            var parts = snakeCase.Split('_');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                    parts[i] = char.ToUpperInvariant(parts[i][0])
                             + parts[i].Substring(1);
            }
            return string.Join("", parts);
        }

        private void FireNotification(WatchState ws, JObject trigger)
        {
            var notif = new JObject();
            notif["watch_id"] = ws.Definition.WatchId;
            if (ws.Definition.Label != null)
                notif["label"] = ws.Definition.Label;
            notif["frame"] = Time.frameCount;
            notif["trigger"] = trigger;

            _notifyCallback?.Invoke(notif);
        }

        // --- Persistence ---

        private void PersistToSessionState()
        {
#if UNITY_EDITOR
            var defs = new List<WatchDefinition>();
            foreach (var ws in _watches)
                defs.Add(ws.Definition);

            var json = JsonConvert.SerializeObject(defs);
            SessionState.SetString(SessionKey, json);
            SessionState.SetInt(CounterKey, _nextId);
#endif
        }

        private void RestoreFromSessionState()
        {
#if UNITY_EDITOR
            _nextId = SessionState.GetInt(CounterKey, 0);
            var json = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                var defs = JsonConvert.DeserializeObject<List<WatchDefinition>>(json);
                if (defs == null) return;

                foreach (var def in defs)
                {
                    var state = new WatchState
                    {
                        Definition = def,
                        LastTriggeredAt = 0,
                        TriggerCount = 0
                    };
                    _watches.Add(state);
                }

                Debug.Log($"[Theatre] Restored {_watches.Count} watches after domain reload");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Theatre] Failed to restore watches: {ex.Message}");
            }
#endif
        }
    }
}
