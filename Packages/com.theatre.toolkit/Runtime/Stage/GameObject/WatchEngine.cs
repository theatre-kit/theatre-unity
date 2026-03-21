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
    /// Evaluates active watches on a periodic interval and fires SSE
    /// notifications when conditions are met. Persists watch definitions
    /// to SessionState for domain reload survival.
    /// </summary>
    public sealed class WatchEngine
    {
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
            var (watches, nextId) = WatchPersistence.Restore();
            _watches.AddRange(watches);
            _nextId = nextId;
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
                        state.PreviousValue = WatchConditions.ReadTrackedProperty(
                            result.GameObject, def.Condition.Property);
                    }
                }
            }

            _watches.Add(state);
            WatchPersistence.Save(_watches, _nextId);

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
                    WatchPersistence.Save(_watches, _nextId);
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
                    && WatchConditions.IsAlive(ws.CachedInstanceId);
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
                    && WatchConditions.IsAlive(ws.CachedInstanceId);

                // Evaluate condition right now
                var trigger = WatchConditions.Evaluate(ws);
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

                var trigger = WatchConditions.Evaluate(ws);
                if (trigger != null)
                {
                    ws.LastTriggeredAt = now;
                    ws.TriggerCount++;
                    FireNotification(ws, trigger);
                }
            }
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
    }
}
