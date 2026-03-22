# Design: Phase 4 — Stage: Watches & Actions

## Overview

Agent can subscribe to changes, get notified when conditions are met, and
manipulate game state for debugging. After this phase, an AI agent can watch
for "enemy HP below 25", receive an SSE notification when it happens, teleport
the player, pause the game, step frames, set properties on components, and
query what changed since the last snapshot.

**Key components:**
- `watch` compound tool — create, remove, list, check operations with six
  condition types (threshold, proximity, entered_region, property_changed,
  destroyed, spawned)
- Watch engine — polling loop on `EditorApplication.update`, evaluates
  conditions, fires SSE notifications, respects throttle intervals
- Watch persistence — `SessionState` JSON serialization survives domain reloads
- `action` compound tool — teleport, set_property, set_active, set_timescale,
  pause, step, unpause, invoke_method operations
- `scene_delta` tool — reports what changed since a previous frame or query

**Exit criteria:** Agent can watch for "enemy HP below 25", get notified when
it happens, teleport the player, pause the game, and step frames. Watches
persist across domain reloads. Actions are undoable where applicable.

---

## Architecture Decisions

### Watch Engine: Polling on EditorApplication.update

The watch engine hooks into `EditorApplication.update` and evaluates active
watch conditions on a configurable frame interval (default: every 10 frames
in play mode, every 60 frames in edit mode). This avoids per-frame overhead
while keeping responsiveness adequate for debugging workflows.

**Why polling, not events?** Unity has no generic property-change event system.
`SerializedProperty` doesn't fire callbacks. Component fields don't have
change notifications. Polling is the only universal approach that works for
arbitrary properties on arbitrary components.

**Throttle enforcement:** Each watch stores `last_triggered_at` (realtime).
After triggering, the watch skips evaluation until `throttle_ms` has elapsed.
This prevents notification floods for oscillating values.

**Frame counting:** The engine tracks a `_evalFrame` counter. It increments
each `update` tick and evaluates watches when `_evalFrame % evalInterval == 0`.
In play mode, `Time.frameCount` provides the frame number for notifications.

### Watch Conditions

Six condition types, each with its own evaluator:

| Condition | Target Required | Play Mode Required | Evaluates |
|---|---|---|---|
| `threshold` | Yes (path) | No | Property value crosses below/above threshold |
| `proximity` | Yes (path) | No | Distance to another object within/beyond range |
| `entered_region` | Yes (path) | No | Object position inside/outside AABB |
| `property_changed` | Yes (path) | No | Property value differs from last snapshot |
| `destroyed` | Yes (path) | No | Target GameObject becomes null |
| `spawned` | No (global) | Yes | New root objects matching name pattern appear |

All conditions except `spawned` require a `target` path. `spawned` watches the
scene root list for new objects matching `name_pattern`.

### Watch Persistence via SessionState

Watch definitions (not runtime state) are serialized to `SessionState` as a
JSON array under the key `"Theatre_Watches"`. On domain reload:

1. `[InitializeOnLoad]` static constructor in `WatchEngine` reads the key
2. Deserializes watch definitions, assigns the same `watch_id` values
3. Resets runtime state (`last_triggered_at`, cached property values)
4. Watches resume evaluation on the next update tick

This means watches survive script recompilation but lose 1-2 evaluation cycles
during the reload gap. Triggered-during-gap notifications are not replayed —
the agent can use `watch:check` to poll manually if needed.

**Watch ID scheme:** Sequential counter `w_01`, `w_02`, etc. The counter is
persisted alongside watch definitions. After domain reload, new watches
continue from the last counter value.

Watch ID counter is stored in `SessionState.SetInt("Theatre_WatchCounter", value)`.
Watch definitions are serialized to `SessionState.SetString("Theatre_Watches", json)`
via `WatchPersistence`.

**Watch limit:** Maximum 20 concurrent watches. This prevents runaway memory
and CPU usage from agents creating watches in a loop. The error code
`watch_limit_reached` is returned when the limit is hit.

### Action Tool: Compound with Play Mode Gates

`action` is a compound tool under `ToolGroup.StageAction` with eight
operations. Some require play mode, some work in both modes:

| Operation | Play Mode Required | Undo Support | Notes |
|---|---|---|---|
| `teleport` | No | Yes | Sets `Transform.position` (and optional rotation) |
| `set_property` | No | Yes | Via `SerializedObject`/`SerializedProperty` |
| `set_active` | No | Yes | `GameObject.SetActive()` with `Undo.RecordObject` |
| `set_timescale` | Yes | No | `Time.timeScale` — runtime only, no undo |
| `pause` | Yes | No | `EditorApplication.isPaused = true` |
| `step` | Yes | No | `EditorApplication.Step()` — single frame advance |
| `unpause` | Yes | No | `EditorApplication.isPaused = false` |
| `invoke_method` | Yes | No | Reflection-based method call, no undo |

**Undo integration:** Operations that modify serialized state use
`Undo.RecordObject` before mutation. This lets the human Ctrl+Z any agent
action. `set_timescale` and play mode controls are runtime-only and not
undoable.

**Teleport:** Sets `transform.position` directly. Optionally sets
`transform.eulerAngles` if `rotation_euler` is provided. In play mode with
a `Rigidbody`, also calls `rigidbody.MovePosition()` to avoid physics
desync. Does NOT use `Undo` in play mode (runtime objects aren't undoable).
Uses `Undo.RecordObject` in edit mode.

**set_property:** Reuses the type mapping from `PropertySerializer` in reverse.
Accepts JSON values and writes them via `SerializedProperty` with type
coercion. Handles int, float, bool, string, Vector2/3/4, Color, Quaternion,
enum (by string name), and ObjectReference (by instance_id).

**invoke_method:** Uses `System.Reflection` to find and call public methods on
components. Limited to methods with 0-3 parameters of simple types (string,
int, float, bool). Returns the method's return value (if any) serialized to
JSON. If the method returns void, the response confirms execution.

Supported parameter types: `int`, `float`, `bool`, `string`,
`Vector2`, `Vector3`, `Color`. Other types return `invalid_parameter`.

### scene_delta: Frame-Based Change Detection

`scene_delta` compares current scene state against a stored baseline. The
baseline is captured automatically on each `scene_snapshot` or `scene_delta`
call, keyed by frame number.

**Storage:** A ring buffer of the last 5 baselines, each storing per-object
property snapshots. This allows `since_frame` lookups within a reasonable
window without unbounded memory growth.

**What is tracked:** Transform properties (position, euler_angles, local_scale)
are always tracked. Additional properties are tracked only if the agent
specifies them via the `track` parameter. Component property values are read
via `SerializedProperty`.

**Spawned/destroyed detection:** The baseline stores the set of known
`instance_id` values. On delta query, any new instance_id is reported as
spawned; any missing instance_id is reported as destroyed.

**Assembly placement:** `scene_delta` is registered as a separate tool under
`ToolGroup.StageGameObject` (it is scene awareness, not a watch).

---

## Implementation Units

### Unit 1: Watch Definition Types

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/GameObject/WatchTypes.cs`

Data types for watch definitions, conditions, and state. No logic — just
serializable structures used by the engine and tool.

```csharp
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
```

**Implementation Notes:**
- `WatchCondition` uses a flat structure with nullable fields rather than a
  discriminated union. This matches the JSON wire format and avoids complex
  polymorphic deserialization with Newtonsoft.
- `WatchState` is separate from `WatchDefinition` because state is not
  persisted. On domain reload, definitions restore but state resets.
- `PreviousValue` as `JToken` enables type-agnostic comparison via
  `JToken.DeepEquals()`.

**Acceptance Criteria:**
- [ ] `WatchDefinition` round-trips through `JsonConvert.SerializeObject` /
  `DeserializeObject`
- [ ] All `WatchCondition` types deserialize correctly from the JSON examples
  in STAGE-SURFACE.md
- [ ] `WatchState` fields initialize to sensible defaults (0, null, false)

---

### Unit 2: Watch Engine

**File:** `Packages/com.theatre.toolkit/Runtime/Stage/GameObject/WatchEngine.cs`

The core polling loop that evaluates watch conditions and fires SSE
notifications. Hooks into `EditorApplication.update`.

```csharp
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
            double now = EditorApplication.timeSinceStartup;

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
```

**Implementation Notes:**
- `EditorApplication.timeSinceStartup` is used for throttle timing because
  `Time.realtimeSinceStartup` resets on play mode transitions.
- Target resolution is cached by `instance_id` for fast re-lookup. Falls back
  to path-based resolution if the cached ID becomes invalid (object
  re-created after domain reload).
- `ReadTrackedProperty` searches all components on the GameObject, not just
  one. The property name is matched against `SerializedProperty.name` with
  a fallback to Unity's `m_PropertyName` convention.
- `GetInstanceID()` usage is wrapped in `#pragma warning disable CS0618`
  per the deprecated API rules.
- `spawned` evaluation is stubbed — full implementation requires integration
  with scene_delta's root object tracking (Unit 7).

**Acceptance Criteria:**
- [ ] Watch with `threshold` condition triggers when a float property drops
  below the threshold value
- [ ] Watch with `proximity` condition triggers when two objects are within
  the specified distance
- [ ] Watch with `entered_region` condition triggers when an object enters
  the AABB
- [ ] Watch with `property_changed` condition triggers when a property value
  changes between evaluation cycles
- [ ] Watch with `destroyed` condition triggers when the target object is
  destroyed
- [ ] Throttle prevents re-triggering within `throttle_ms`
- [ ] Watches persist across simulated domain reload (SessionState round-trip)
- [ ] Watch counter persists — new watches after reload get sequential IDs
- [ ] Maximum 32 watches enforced

---

### Unit 3: Watch Tool (MCP Registration)

**File:** `Packages/com.theatre.toolkit/Editor/Tools/WatchTool.cs`

MCP tool registration and request dispatch for `watch`. Follows the compound
tool pattern from `SpatialQueryTool`.

```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// MCP tool: watch
    /// Compound tool for change subscriptions.
    /// Operations: create, remove, list, check.
    /// </summary>
    public static class WatchTool
    {
        private static readonly JToken s_inputSchema;
        private static WatchEngine s_engine;

        static WatchTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create"", ""remove"", ""list"", ""check""],
                        ""description"": ""The watch operation to perform.""
                    },
                    ""target"": {
                        ""type"": ""string"",
                        ""description"": ""Hierarchy path of the object to watch, or '*' for global watches. Required for create.""
                    },
                    ""track"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Properties to track: ['position', 'current_hp']. Required for create.""
                    },
                    ""condition"": {
                        ""type"": ""object"",
                        ""description"": ""Trigger condition. Types: threshold, proximity, entered_region, property_changed, destroyed, spawned."",
                        ""properties"": {
                            ""type"": {
                                ""type"": ""string"",
                                ""enum"": [""threshold"", ""proximity"", ""entered_region"", ""property_changed"", ""destroyed"", ""spawned""]
                            },
                            ""property"": { ""type"": ""string"" },
                            ""below"": { ""type"": ""number"" },
                            ""above"": { ""type"": ""number"" },
                            ""target"": { ""type"": ""string"" },
                            ""within"": { ""type"": ""number"" },
                            ""beyond"": { ""type"": ""number"" },
                            ""min"": { ""type"": ""array"", ""items"": { ""type"": ""number"" } },
                            ""max"": { ""type"": ""array"", ""items"": { ""type"": ""number"" } },
                            ""name_pattern"": { ""type"": ""string"" }
                        }
                    },
                    ""throttle_ms"": {
                        ""type"": ""integer"",
                        ""default"": 500,
                        ""description"": ""Min interval between notifications in milliseconds.""
                    },
                    ""label"": {
                        ""type"": ""string"",
                        ""description"": ""Human-readable label for this watch.""
                    },
                    ""watch_id"": {
                        ""type"": ""string"",
                        ""description"": ""Watch ID. Required for remove and check.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "watch",
                description: "Subscribe to changes or conditions on GameObjects. "
                    + "Use 'create' to set up a watch with a condition "
                    + "(threshold, proximity, entered_region, property_changed, "
                    + "destroyed, spawned). Watches fire notifications via SSE "
                    + "when conditions are met. Use 'check' to poll manually.",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageWatch,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = false
                }
            ));
        }

        /// <summary>
        /// Get or initialize the watch engine.
        /// </summary>
        internal static WatchEngine GetEngine()
        {
            if (s_engine == null)
            {
                s_engine = new WatchEngine();
                s_engine.Initialize(OnWatchTriggered);

                // Hook into editor update for polling
                UnityEditor.EditorApplication.update += s_engine.Tick;
            }
            return s_engine;
        }

        /// <summary>
        /// Teardown — remove update hook. Called on server shutdown.
        /// </summary>
        internal static void Shutdown()
        {
            if (s_engine != null)
            {
                UnityEditor.EditorApplication.update -= s_engine.Tick;
                s_engine = null;
            }
        }

        private static void OnWatchTriggered(JObject notifParams)
        {
            var notification = JsonRpcResponse.Notification(
                "notifications/theatre/watch_triggered", notifParams);
            TheatreServer.SseManager?.PushNotification(notification);
        }

        private static string Execute(JToken arguments)
        {
            if (arguments == null || arguments.Type != JTokenType.Object)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Arguments must be a JSON object with an 'operation' field",
                    "Provide {\"operation\": \"create\", \"target\": \"/Player\", ...}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: create, remove, list, check");
            }

            try
            {
                return operation switch
                {
                    "create" => ExecuteCreate(args),
                    "remove" => ExecuteRemove(args),
                    "list" => ExecuteList(args),
                    "check" => ExecuteCheck(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create, remove, list, check")
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] watch:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"watch:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }

        private static string ExecuteCreate(JObject args)
        {
            var target = args["target"]?.Value<string>();
            if (string.IsNullOrEmpty(target))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'target' parameter",
                    "Provide a hierarchy path like '/Player' or '*' for global watches");
            }

            var condition = args["condition"]?.ToObject<WatchCondition>();
            if (condition == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'condition' parameter",
                    "Provide a condition object with 'type' field: threshold, "
                    + "proximity, entered_region, property_changed, destroyed, spawned");
            }

            var track = args["track"]?.ToObject<string[]>();
            var throttleMs = args["throttle_ms"]?.Value<int>() ?? 500;
            var label = args["label"]?.Value<string>();

            var def = new WatchDefinition
            {
                Target = target,
                Track = track,
                Condition = condition,
                ThrottleMs = throttleMs,
                Label = label
            };

            var engine = GetEngine();
            var watchId = engine.Create(def);

            if (watchId == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "watch_limit_reached",
                    $"Maximum {MaxWatches} concurrent watches reached",
                    "Remove unused watches with watch:remove before creating new ones");
            }

            var response = new JObject();
            response["result"] = "ok";
            response["watch_id"] = watchId;
            response["target"] = target;
            if (track != null)
                response["track"] = new JArray(track);
            response["condition"] = JObject.FromObject(condition);
            response["throttle_ms"] = throttleMs;
            if (label != null)
                response["label"] = label;
            response["active_watches"] = engine.Count;
            ResponseHelpers.AddFrameContext(response);

            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        private const int MaxWatches = 32;

        private static string ExecuteRemove(JObject args)
        {
            var watchId = args["watch_id"]?.Value<string>();
            if (string.IsNullOrEmpty(watchId))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'watch_id' parameter",
                    "Provide the watch_id returned from watch:create");
            }

            var engine = GetEngine();
            if (!engine.Remove(watchId))
            {
                return ResponseHelpers.ErrorResponse(
                    "gameobject_not_found",
                    $"No watch found with watch_id '{watchId}'",
                    "Use watch:list to see active watches");
            }

            var response = new JObject();
            response["result"] = "ok";
            response["watch_id"] = watchId;
            response["active_watches"] = engine.Count;
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string ExecuteList(JObject args)
        {
            var engine = GetEngine();
            var response = new JObject();
            response["results"] = engine.ListAll();
            response["active_watches"] = engine.Count;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string ExecuteCheck(JObject args)
        {
            var watchId = args["watch_id"]?.Value<string>();
            if (string.IsNullOrEmpty(watchId))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'watch_id' parameter",
                    "Provide the watch_id returned from watch:create");
            }

            var engine = GetEngine();
            var result = engine.Check(watchId);
            if (result == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "gameobject_not_found",
                    $"No watch found with watch_id '{watchId}'",
                    "Use watch:list to see active watches");
            }

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
```

**Implementation Notes:**
- `WatchTool` follows the same static class + `Register` + compound operation
  pattern as `SpatialQueryTool`.
- The engine is lazily initialized on first use and hooks into
  `EditorApplication.update` for the polling loop.
- `Shutdown()` is called from `TheatreServer.StopServer()` to unhook the
  update callback.
- SSE notifications are fired through the existing `SseStreamManager` via
  `TheatreServer.SseManager.PushNotification()`.
- The `remove` response echoes `watch_id` (not a boolean) per the contracts
  rule.

**Acceptance Criteria:**
- [ ] `watch:create` with valid params returns `watch_id` and echoes input
  fields
- [ ] `watch:create` at max capacity returns `watch_limit_reached` error
- [ ] `watch:remove` with valid `watch_id` returns `result: "ok"` and echoes
  the `watch_id`
- [ ] `watch:remove` with unknown ID returns error
- [ ] `watch:list` returns all active watches with status
- [ ] `watch:check` evaluates the condition immediately and returns current
  state
- [ ] SSE notification is pushed when a watch triggers during evaluation

---

### Unit 4: Action Tool

**File:** `Packages/com.theatre.toolkit/Editor/Tools/ActionTool.cs`

MCP tool registration and compound dispatch for `action`. Eight operations.

```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// MCP tool: action
    /// Compound tool for game state manipulation.
    /// Operations: teleport, set_property, set_active, set_timescale,
    ///             pause, step, unpause, invoke_method.
    /// </summary>
    public static class ActionTool
    {
        private static readonly JToken s_inputSchema;

        static ActionTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""teleport"", ""set_property"", ""set_active"",
                                   ""set_timescale"", ""pause"", ""step"",
                                   ""unpause"", ""invoke_method""],
                        ""description"": ""The action to perform.""
                    },
                    ""path"": {
                        ""type"": ""string"",
                        ""description"": ""Target GameObject path. Required for teleport, set_property, set_active, invoke_method.""
                    },
                    ""instance_id"": {
                        ""type"": ""integer"",
                        ""description"": ""Target GameObject instance_id (alternative to path).""
                    },
                    ""position"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""World position [x,y,z]. Used by teleport.""
                    },
                    ""rotation_euler"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""description"": ""Euler angles [x,y,z] in degrees. Optional for teleport.""
                    },
                    ""component"": {
                        ""type"": ""string"",
                        ""description"": ""Component type name. Used by set_property, invoke_method.""
                    },
                    ""property"": {
                        ""type"": ""string"",
                        ""description"": ""Property name. Used by set_property.""
                    },
                    ""value"": {
                        ""description"": ""Value to set. Used by set_property, set_active, set_timescale.""
                    },
                    ""active"": {
                        ""type"": ""boolean"",
                        ""description"": ""Enable/disable state. Used by set_active.""
                    },
                    ""timescale"": {
                        ""type"": ""number"",
                        ""description"": ""Time.timeScale value (0.0-100.0). Used by set_timescale.""
                    },
                    ""method"": {
                        ""type"": ""string"",
                        ""description"": ""Method name to call. Used by invoke_method.""
                    },
                    ""arguments"": {
                        ""type"": ""array"",
                        ""description"": ""Method arguments [arg1, arg2, ...]. Used by invoke_method. Max 3 args, simple types only.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "action",
                description: "Manipulate game state for debugging. Teleport "
                    + "objects, set component properties, enable/disable "
                    + "GameObjects, control time and play mode, call methods. "
                    + "pause/step/unpause/set_timescale require Play Mode. "
                    + "teleport/set_property/set_active work in both modes.",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageAction,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = false
                }
            ));
        }

        private static string Execute(JToken arguments)
        {
            if (arguments == null || arguments.Type != JTokenType.Object)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Arguments must be a JSON object with an 'operation' field",
                    "Provide {\"operation\": \"teleport\", \"path\": \"/Player\", ...}");
            }

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: teleport, set_property, set_active, "
                    + "set_timescale, pause, step, unpause, invoke_method");
            }

            try
            {
                return operation switch
                {
                    "teleport" => ActionTeleport.Execute(args),
                    "set_property" => ActionSetProperty.Execute(args),
                    "set_active" => ActionSetActive.Execute(args),
                    "set_timescale" => ActionSetTimescale.Execute(args),
                    "pause" => ActionPlayControl.ExecutePause(args),
                    "step" => ActionPlayControl.ExecuteStep(args),
                    "unpause" => ActionPlayControl.ExecuteUnpause(args),
                    "invoke_method" => ActionInvokeMethod.Execute(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: teleport, set_property, set_active, "
                        + "set_timescale, pause, step, unpause, invoke_method")
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] action:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"action:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }
    }
}
```

**Implementation Notes:**
- Follows the same pattern as `SpatialQueryTool`: static class, compound
  dispatch via operation string, per-operation handler classes.
- Each operation is implemented in a separate static class (Units 5a-5d)
  for maintainability.
- `ReadOnlyHint = false` since actions mutate state.

---

### Unit 5a: Action — Teleport

**File:** `Packages/com.theatre.toolkit/Editor/Tools/Actions/ActionTeleport.cs`

```csharp
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor
{
    /// <summary>
    /// action:teleport — move a GameObject to a position.
    /// </summary>
    internal static class ActionTeleport
    {
        public static string Execute(JObject args)
        {
            var path = args["path"]?.Value<string>();
            var instanceId = args["instance_id"]?.Value<int>();
            var posArr = args["position"] as JArray;

            if (posArr == null || posArr.Count < 3)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'position' — provide [x, y, z]",
                    "Example: {\"operation\": \"teleport\", \"path\": \"/Player\", \"position\": [10, 0, 5]}");
            }

            var resolved = ObjectResolver.Resolve(path, instanceId);
            if (!resolved.Success)
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);

            var go = resolved.GameObject;
            var newPos = new Vector3(
                posArr[0].Value<float>(),
                posArr[1].Value<float>(),
                posArr[2].Value<float>());

            var oldPos = go.transform.position;

            // Undo in edit mode, direct set in play mode
            if (!Application.isPlaying)
            {
#if UNITY_EDITOR
                Undo.RecordObject(go.transform, "Theatre Teleport");
#endif
            }

            go.transform.position = newPos;

            // Handle optional rotation
            Vector3? oldRot = null;
            var rotArr = args["rotation_euler"] as JArray;
            if (rotArr != null && rotArr.Count >= 3)
            {
                oldRot = go.transform.eulerAngles;
                go.transform.eulerAngles = new Vector3(
                    rotArr[0].Value<float>(),
                    rotArr[1].Value<float>(),
                    rotArr[2].Value<float>());
            }

            // In play mode with Rigidbody, sync physics
            if (Application.isPlaying)
            {
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.MovePosition(newPos);
                }
                var rb2d = go.GetComponent<Rigidbody2D>();
                if (rb2d != null)
                {
                    rb2d.MovePosition(new Vector2(newPos.x, newPos.y));
                }
            }

            // Build response
            var response = new JObject();
            response["result"] = "ok";
            response["path"] = ResponseHelpers.GetHierarchyPath(go.transform);
#pragma warning disable CS0618
            response["instance_id"] = go.GetInstanceID();
#pragma warning restore CS0618
            response["position"] = ResponseHelpers.ToJArray(newPos);
            response["previous_position"] = ResponseHelpers.ToJArray(oldPos);
            if (rotArr != null)
            {
                response["rotation_euler"] = ResponseHelpers.ToJArray(
                    go.transform.eulerAngles);
                if (oldRot.HasValue)
                    response["previous_rotation_euler"] = ResponseHelpers.ToJArray(
                        oldRot.Value);
            }
            ResponseHelpers.AddFrameContext(response);

            return response.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
```

**Acceptance Criteria:**
- [ ] Teleport moves object to specified position
- [ ] Optional `rotation_euler` sets rotation
- [ ] Response echoes new and previous position
- [ ] Edit mode: Undo.RecordObject called, Ctrl+Z reverts
- [ ] Play mode with Rigidbody: `rb.MovePosition()` called for physics sync
- [ ] Play mode with Rigidbody2D: `rb2d.MovePosition()` called
- [ ] Missing target returns `gameobject_not_found` error

---

### Unit 5b: Action — SetProperty

**File:** `Packages/com.theatre.toolkit/Editor/Tools/Actions/ActionSetProperty.cs`

Sets a component property via `SerializedObject`/`SerializedProperty`.
Reuses type mapping from `PropertySerializer`.

```csharp
using System;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor
{
    /// <summary>
    /// action:set_property — set a serialized property on a component.
    /// </summary>
    internal static class ActionSetProperty
    {
        public static string Execute(JObject args)
        {
            var path = args["path"]?.Value<string>();
            var instanceId = args["instance_id"]?.Value<int>();
            var componentName = args["component"]?.Value<string>();
            var propertyName = args["property"]?.Value<string>();
            var value = args["value"];

            if (string.IsNullOrEmpty(componentName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'component' parameter",
                    "Provide the component type name, e.g., 'Health', 'Transform'");

            if (string.IsNullOrEmpty(propertyName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'property' parameter",
                    "Provide the property name, e.g., 'current_hp', 'position'");

            if (value == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'value' parameter",
                    "Provide the value to set");

            var resolved = ObjectResolver.Resolve(path, instanceId);
            if (!resolved.Success)
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);

            var go = resolved.GameObject;

            // Find the component
            Component component = null;
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (string.Equals(comp.GetType().Name, componentName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    component = comp;
                    break;
                }
            }

            if (component == null)
                return ResponseHelpers.ErrorResponse(
                    "component_not_found",
                    $"Component '{componentName}' not found on '{path ?? go.name}'",
                    "Use scene_inspect to list all components on this GameObject");

#if UNITY_EDITOR
            var so = new SerializedObject(component);

            // Try direct name, then with m_ prefix
            var prop = so.FindProperty(propertyName);
            if (prop == null)
                prop = so.FindProperty("m_" + PropertySerializer.ToSnakeCase(propertyName)
                    .Replace("_", ""));
            if (prop == null)
                prop = so.FindProperty(ToPascalCase(propertyName));
            if (prop == null)
                prop = so.FindProperty("m_" + ToPascalCase(propertyName));

            if (prop == null)
                return ResponseHelpers.ErrorResponse(
                    "property_not_found",
                    $"Property '{propertyName}' not found on component '{componentName}'",
                    "Use scene_inspect with component filter to see available properties");

            // Read previous value
            var previousValue = ReadCurrentValue(prop);

            // Set the value
            if (!SetPropertyValue(prop, value, out var setError))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Cannot set '{propertyName}': {setError}",
                    "Check the property type and provide a compatible value");

            so.ApplyModifiedProperties();
#endif

            var response = new JObject();
            response["result"] = "ok";
            response["path"] = ResponseHelpers.GetHierarchyPath(go.transform);
#pragma warning disable CS0618
            response["instance_id"] = go.GetInstanceID();
#pragma warning restore CS0618
            response["component"] = componentName;
            response["property"] = propertyName;
            response["value"] = value;
#if UNITY_EDITOR
            response["previous_value"] = previousValue;
#endif
            ResponseHelpers.AddFrameContext(response);

            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

#if UNITY_EDITOR
        private static JToken ReadCurrentValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue;
                case SerializedPropertyType.Float: return Math.Round(prop.floatValue, 4);
                case SerializedPropertyType.Boolean: return prop.boolValue;
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.Vector2:
                    return ResponseHelpers.ToJArray(prop.vector2Value);
                case SerializedPropertyType.Vector3:
                    return ResponseHelpers.ToJArray(prop.vector3Value);
                case SerializedPropertyType.Color:
                    return ResponseHelpers.ToJArray(prop.colorValue);
                case SerializedPropertyType.Enum:
                    return prop.enumDisplayNames.Length > prop.enumValueIndex
                        ? prop.enumDisplayNames[prop.enumValueIndex]
                        : prop.enumValueIndex.ToString();
                default: return prop.propertyType.ToString();
            }
        }

        private static bool SetPropertyValue(
            SerializedProperty prop, JToken value, out string error)
        {
            error = null;
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        prop.intValue = value.ToObject<int>();
                        return true;

                    case SerializedPropertyType.Float:
                        prop.floatValue = value.ToObject<float>();
                        return true;

                    case SerializedPropertyType.Boolean:
                        prop.boolValue = value.ToObject<bool>();
                        return true;

                    case SerializedPropertyType.String:
                        prop.stringValue = value.ToObject<string>();
                        return true;

                    case SerializedPropertyType.Vector2:
                        if (value is JArray v2 && v2.Count >= 2)
                        {
                            prop.vector2Value = new Vector2(
                                v2[0].Value<float>(), v2[1].Value<float>());
                            return true;
                        }
                        error = "Vector2 requires [x, y] array";
                        return false;

                    case SerializedPropertyType.Vector3:
                        if (value is JArray v3 && v3.Count >= 3)
                        {
                            prop.vector3Value = new Vector3(
                                v3[0].Value<float>(),
                                v3[1].Value<float>(),
                                v3[2].Value<float>());
                            return true;
                        }
                        error = "Vector3 requires [x, y, z] array";
                        return false;

                    case SerializedPropertyType.Vector4:
                        if (value is JArray v4 && v4.Count >= 4)
                        {
                            prop.vector4Value = new Vector4(
                                v4[0].Value<float>(),
                                v4[1].Value<float>(),
                                v4[2].Value<float>(),
                                v4[3].Value<float>());
                            return true;
                        }
                        error = "Vector4 requires [x, y, z, w] array";
                        return false;

                    case SerializedPropertyType.Color:
                        if (value is JArray c && c.Count >= 4)
                        {
                            prop.colorValue = new Color(
                                c[0].Value<float>(), c[1].Value<float>(),
                                c[2].Value<float>(), c[3].Value<float>());
                            return true;
                        }
                        error = "Color requires [r, g, b, a] array";
                        return false;

                    case SerializedPropertyType.Quaternion:
                        if (value is JArray q && q.Count >= 4)
                        {
                            prop.quaternionValue = new Quaternion(
                                q[0].Value<float>(), q[1].Value<float>(),
                                q[2].Value<float>(), q[3].Value<float>());
                            return true;
                        }
                        error = "Quaternion requires [x, y, z, w] array";
                        return false;

                    case SerializedPropertyType.Enum:
                        var enumStr = value.ToObject<string>();
                        for (int i = 0; i < prop.enumDisplayNames.Length; i++)
                        {
                            if (string.Equals(prop.enumDisplayNames[i],
                                enumStr, StringComparison.OrdinalIgnoreCase))
                            {
                                prop.enumValueIndex = i;
                                return true;
                            }
                        }
                        error = $"Unknown enum value '{enumStr}'. "
                              + $"Valid: {string.Join(", ", prop.enumDisplayNames)}";
                        return false;

                    default:
                        error = $"Unsupported property type: {prop.propertyType}";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
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
    }
}
```

**Acceptance Criteria:**
- [ ] Sets int, float, bool, string properties correctly
- [ ] Sets Vector2, Vector3, Vector4, Color, Quaternion from arrays
- [ ] Sets enum by display name string
- [ ] Response echoes component, property, value, and previous_value
- [ ] Returns `component_not_found` if component doesn't exist
- [ ] Returns `property_not_found` if property doesn't exist
- [ ] `SerializedObject.ApplyModifiedProperties()` registers Undo

---

### Unit 5c: Action — SetActive, SetTimescale, Play Controls

**File:** `Packages/com.theatre.toolkit/Editor/Tools/Actions/ActionSetActive.cs`

```csharp
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor
{
    /// <summary>
    /// action:set_active — enable/disable a GameObject.
    /// </summary>
    internal static class ActionSetActive
    {
        public static string Execute(JObject args)
        {
            var path = args["path"]?.Value<string>();
            var instanceId = args["instance_id"]?.Value<int>();
            var active = args["active"];

            if (active == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'active' parameter (true/false)",
                    "Example: {\"operation\": \"set_active\", \"path\": \"/Enemy\", \"active\": false}");

            var resolved = ObjectResolver.Resolve(path, instanceId);
            if (!resolved.Success)
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);

            var go = resolved.GameObject;
            var previousActive = go.activeSelf;
            var newActive = active.ToObject<bool>();

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RecordObject(go, "Theatre SetActive");
#endif

            go.SetActive(newActive);

            var response = new JObject();
            response["result"] = "ok";
            response["path"] = ResponseHelpers.GetHierarchyPath(go.transform);
#pragma warning disable CS0618
            response["instance_id"] = go.GetInstanceID();
#pragma warning restore CS0618
            response["active"] = newActive;
            response["previous_active"] = previousActive;
            ResponseHelpers.AddFrameContext(response);

            return response.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
```

**File:** `Packages/com.theatre.toolkit/Editor/Tools/Actions/ActionSetTimescale.cs`

```csharp
using System;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// action:set_timescale — change Time.timeScale.
    /// </summary>
    internal static class ActionSetTimescale
    {
        public static string Execute(JObject args)
        {
            if (!Application.isPlaying)
                return ResponseHelpers.ErrorResponse(
                    "requires_play_mode",
                    "set_timescale requires Play Mode",
                    "Enter Play Mode first");

            var timescale = args["timescale"];
            if (timescale == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'timescale' parameter",
                    "Provide a number 0.0-100.0, e.g., {\"operation\": \"set_timescale\", \"timescale\": 0.5}");

            var newScale = timescale.ToObject<float>();
            if (newScale < 0f || newScale > 100f)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"timescale {newScale} out of range [0.0, 100.0]",
                    "Use 0 to freeze, 1 for normal speed, >1 for fast-forward");

            var previousScale = Time.timeScale;
            Time.timeScale = newScale;

            var response = new JObject();
            response["result"] = "ok";
            response["timescale"] = Math.Round(newScale, 4);
            response["previous_timescale"] = Math.Round(previousScale, 4);
            ResponseHelpers.AddFrameContext(response);

            return response.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
```

**File:** `Packages/com.theatre.toolkit/Editor/Tools/Actions/ActionPlayControl.cs`

```csharp
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor
{
    /// <summary>
    /// action:pause, action:step, action:unpause — play mode controls.
    /// </summary>
    internal static class ActionPlayControl
    {
        public static string ExecutePause(JObject args)
        {
            if (!Application.isPlaying)
                return ResponseHelpers.ErrorResponse(
                    "requires_play_mode",
                    "pause requires Play Mode",
                    "Enter Play Mode first");

#if UNITY_EDITOR
            EditorApplication.isPaused = true;
#endif

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "pause";
            response["paused"] = true;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string ExecuteStep(JObject args)
        {
            if (!Application.isPlaying)
                return ResponseHelpers.ErrorResponse(
                    "requires_play_mode",
                    "step requires Play Mode",
                    "Enter Play Mode first");

#if UNITY_EDITOR
            // Ensure paused first
            if (!EditorApplication.isPaused)
                EditorApplication.isPaused = true;

            EditorApplication.Step();
#endif

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "step";
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string ExecuteUnpause(JObject args)
        {
            if (!Application.isPlaying)
                return ResponseHelpers.ErrorResponse(
                    "requires_play_mode",
                    "unpause requires Play Mode",
                    "Enter Play Mode first");

#if UNITY_EDITOR
            EditorApplication.isPaused = false;
#endif

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "unpause";
            response["paused"] = false;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
```

**Acceptance Criteria:**
- [ ] `set_active` toggles `GameObject.SetActive()` and echoes old/new state
- [ ] `set_active` in edit mode calls `Undo.RecordObject`
- [ ] `set_timescale` changes `Time.timeScale` and echoes old/new values
- [ ] `set_timescale` rejects values outside [0, 100]
- [ ] `pause`/`step`/`unpause` all require play mode
- [ ] `step` auto-pauses if not already paused, then steps one frame
- [ ] All play mode operations return `requires_play_mode` error in edit mode

---

### Unit 5d: Action — InvokeMethod

**File:** `Packages/com.theatre.toolkit/Editor/Tools/Actions/ActionInvokeMethod.cs`

Uses reflection to call public methods on components with simple parameter
types.

```csharp
using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// action:invoke_method — call a public method on a component via reflection.
    /// Limited to methods with 0-3 parameters of simple types.
    /// </summary>
    internal static class ActionInvokeMethod
    {
        private static readonly Type[] AllowedParamTypes = new[]
        {
            typeof(string), typeof(int), typeof(float),
            typeof(bool), typeof(double), typeof(long)
        };

        private const int MaxArgs = 3;

        public static string Execute(JObject args)
        {
            if (!Application.isPlaying)
                return ResponseHelpers.ErrorResponse(
                    "requires_play_mode",
                    "invoke_method requires Play Mode",
                    "Enter Play Mode first — method invocation modifies runtime state");

            var path = args["path"]?.Value<string>();
            var instanceId = args["instance_id"]?.Value<int>();
            var componentName = args["component"]?.Value<string>();
            var methodName = args["method"]?.Value<string>();
            var methodArgs = args["arguments"] as JArray;

            if (string.IsNullOrEmpty(componentName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'component' parameter",
                    "Provide the component type name");

            if (string.IsNullOrEmpty(methodName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'method' parameter",
                    "Provide the method name to invoke");

            if (methodArgs != null && methodArgs.Count > MaxArgs)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"invoke_method supports at most {MaxArgs} arguments, got {methodArgs.Count}",
                    "Simplify the call or invoke a wrapper method");

            var resolved = ObjectResolver.Resolve(path, instanceId);
            if (!resolved.Success)
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);

            var go = resolved.GameObject;

            // Find the component
            Component component = null;
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (string.Equals(comp.GetType().Name, componentName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    component = comp;
                    break;
                }
            }

            if (component == null)
                return ResponseHelpers.ErrorResponse(
                    "component_not_found",
                    $"Component '{componentName}' not found on '{path ?? go.name}'",
                    "Use scene_inspect to list all components on this GameObject");

            // Find the method
            var type = component.GetType();
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);

            MethodInfo targetMethod = null;
            int argCount = methodArgs?.Count ?? 0;

            foreach (var method in methods)
            {
                if (method.Name != methodName) continue;
                var parameters = method.GetParameters();
                if (parameters.Length != argCount) continue;

                bool allAllowed = true;
                foreach (var p in parameters)
                {
                    if (!IsAllowedType(p.ParameterType))
                    {
                        allAllowed = false;
                        break;
                    }
                }
                if (allAllowed)
                {
                    targetMethod = method;
                    break;
                }
            }

            if (targetMethod == null)
                return ResponseHelpers.ErrorResponse(
                    "property_not_found",
                    $"No public method '{methodName}' with {argCount} simple-type parameters found on '{componentName}'",
                    "invoke_method only supports string, int, float, bool parameters. "
                    + "Use scene_inspect to check available methods.");

            // Convert arguments
            object[] convertedArgs = null;
            if (argCount > 0)
            {
                convertedArgs = new object[argCount];
                var parameters = targetMethod.GetParameters();
                for (int i = 0; i < argCount; i++)
                {
                    try
                    {
                        convertedArgs[i] = methodArgs[i].ToObject(parameters[i].ParameterType);
                    }
                    catch (Exception ex)
                    {
                        return ResponseHelpers.ErrorResponse(
                            "invalid_parameter",
                            $"Cannot convert argument {i} to {parameters[i].ParameterType.Name}: {ex.Message}",
                            $"Parameter '{parameters[i].Name}' expects {parameters[i].ParameterType.Name}");
                    }
                }
            }

            // Invoke
            object returnValue;
            try
            {
                returnValue = targetMethod.Invoke(component, convertedArgs);
            }
            catch (TargetInvocationException ex)
            {
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"Method '{methodName}' threw: {ex.InnerException?.Message ?? ex.Message}",
                    "Check the Unity Console for the full stack trace");
            }

            // Build response
            var response = new JObject();
            response["result"] = "ok";
            response["path"] = ResponseHelpers.GetHierarchyPath(go.transform);
#pragma warning disable CS0618
            response["instance_id"] = go.GetInstanceID();
#pragma warning restore CS0618
            response["component"] = componentName;
            response["method"] = methodName;

            if (targetMethod.ReturnType != typeof(void) && returnValue != null)
            {
                try
                {
                    response["return_value"] = JToken.FromObject(returnValue);
                }
                catch
                {
                    response["return_value"] = returnValue.ToString();
                }
            }

            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static bool IsAllowedType(Type type)
        {
            foreach (var allowed in AllowedParamTypes)
            {
                if (type == allowed) return true;
            }
            return false;
        }
    }
}
```

**Acceptance Criteria:**
- [ ] Invokes a zero-argument public method successfully
- [ ] Invokes a method with 1-3 simple-type arguments
- [ ] Returns method's return value in response
- [ ] Rejects methods with >3 arguments
- [ ] Rejects methods with complex parameter types (GameObject, etc.)
- [ ] Returns `requires_play_mode` in edit mode
- [ ] Catches and reports `TargetInvocationException`

---

### Unit 6: scene_delta Tool

**File:** `Packages/com.theatre.toolkit/Editor/Tools/SceneDeltaTool.cs`

Tracks changes between scene states. Stores baselines in a ring buffer.

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor
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
            if (pathFilter != null)
            {
                bool match = false;
                foreach (var p in pathFilter)
                {
                    if (path == p || path.StartsWith(p + "/"))
                    {
                        match = true;
                        break;
                    }
                }
                if (!match) goto children;
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

            children:
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
```

**Implementation Notes:**
- Ring buffer of 5 baselines prevents unbounded memory growth.
- Transform properties (position, euler_angles, local_scale) are always
  compared. Additional properties only if specified via `track`.
- First call returns an empty delta with a `note` explaining that a baseline
  has been captured.
- `velocity` is handled as a special case reading `Rigidbody.linearVelocity`
  (Unity 6 renamed `velocity` to `linearVelocity`).

**Acceptance Criteria:**
- [ ] First call returns empty changes with baseline capture note
- [ ] Second call returns position changes for objects that moved
- [ ] `spawned` lists objects that appear between baselines
- [ ] `destroyed` lists objects that disappear between baselines
- [ ] `track` parameter enables tracking additional properties
- [ ] `paths` parameter filters to specific objects
- [ ] `since_frame` looks up a specific baseline frame
- [ ] Ring buffer limits to 5 baselines

---

### Unit 7: Server Integration

**File changes:** `Packages/com.theatre.toolkit/Editor/TheatreServer.cs`

Wire the new tools into the server's `RegisterBuiltInTools` and teardown.

**Changes to `RegisterBuiltInTools`:**

```csharp
private static void RegisterBuiltInTools(ToolRegistry registry)
{
    TheatreStatusTool.Register(registry);
    SceneSnapshotTool.Register(registry);
    SceneHierarchyTool.Register(registry);
    SceneInspectTool.Register(registry);
    UnityConsoleTool.Register(registry);
    UnityTestsTool.Register(registry);
    SpatialQueryTool.Register(registry);  // Phase 3
    WatchTool.Register(registry);         // Phase 4
    ActionTool.Register(registry);        // Phase 4
    SceneDeltaTool.Register(registry);    // Phase 4
}
```

**Changes to `StopServer`:**

```csharp
public static void StopServer()
{
    WatchTool.Shutdown();  // Phase 4: unhook update callback
    s_sseManager?.Dispose();
    s_transport?.Stop();
    s_transport = null;
    s_router = null;
    s_mcpRouter = null;
    s_toolRegistry = null;
    s_sseManager = null;
}
```

**Acceptance Criteria:**
- [ ] `watch`, `action`, and `scene_delta` appear in `tools/list` response
  when their groups are enabled
- [ ] `WatchTool.Shutdown()` called on server stop
- [ ] Watch engine re-initializes after domain reload (via `GetEngine()`)
- [ ] Watches restored from SessionState after domain reload

---

## File Summary

| File | Assembly | Purpose |
|---|---|---|
| `Runtime/Stage/GameObject/WatchTypes.cs` | Runtime | Watch data types (definition, condition, state) |
| `Runtime/Stage/GameObject/WatchEngine.cs` | Runtime | Polling loop, condition evaluation, persistence |
| `Editor/Tools/WatchTool.cs` | Editor | MCP tool registration and dispatch for `watch` |
| `Editor/Tools/ActionTool.cs` | Editor | MCP tool registration and dispatch for `action` |
| `Editor/Tools/Actions/ActionTeleport.cs` | Editor | `action:teleport` implementation |
| `Editor/Tools/Actions/ActionSetProperty.cs` | Editor | `action:set_property` implementation |
| `Editor/Tools/Actions/ActionSetActive.cs` | Editor | `action:set_active` implementation |
| `Editor/Tools/Actions/ActionSetTimescale.cs` | Editor | `action:set_timescale` implementation |
| `Editor/Tools/Actions/ActionPlayControl.cs` | Editor | `action:pause/step/unpause` implementation |
| `Editor/Tools/Actions/ActionInvokeMethod.cs` | Editor | `action:invoke_method` implementation |
| `Editor/Tools/SceneDeltaTool.cs` | Editor | `scene_delta` tool with baseline ring buffer |
| `Editor/TheatreServer.cs` | Editor | Registration and lifecycle changes |

---

## Testing Plan

### Unit Tests (EditMode)

| Test | Validates |
|---|---|
| `WatchDefinition_Serialization_RoundTrips` | SessionState JSON persistence |
| `WatchCondition_AllTypes_Deserialize` | Each condition type from JSON |
| `WatchEngine_Create_AssignsSequentialIds` | `w_01`, `w_02`, ... pattern |
| `WatchEngine_Create_RejectsOverLimit` | Returns null at 32 watches |
| `WatchEngine_Remove_PersistsToSessionState` | Removal updates stored JSON |
| `SetPropertyValue_AllTypes` | int, float, bool, string, Vector3, enum, Color |
| `ToPascalCase_ConvertsSnakeCase` | `current_hp` to `CurrentHp` |
| `SceneDelta_FirstCall_ReturnsEmptyBaseline` | Baseline note in response |
| `SceneDelta_RingBuffer_LimitsTo5` | Old baselines evicted |

### Integration Tests (EditMode, test scene)

| Test | Validates |
|---|---|
| `Watch_Threshold_TriggersOnValueChange` | Engine evaluates and fires notification |
| `Watch_Proximity_TriggersWithinRange` | Distance calculation and trigger |
| `Watch_Destroyed_TriggersOnObjectDestroy` | Null check detection |
| `Watch_Throttle_PreventsRefire` | Time-based suppression |
| `Action_Teleport_MovesObject` | Position matches after teleport |
| `Action_SetProperty_ChangesValue` | SerializedProperty written |
| `Action_SetActive_TogglesState` | `activeSelf` changes |
| `SceneDelta_DetectsPositionChange` | Move object between calls |
| `SceneDelta_DetectsSpawned` | Instantiate between calls |
| `SceneDelta_DetectsDestroyed` | Destroy between calls |

### MCP Integration Tests

| Test | Validates |
|---|---|
| `Watch_Create_ReturnsWatchId` | Full round-trip via HTTP |
| `Watch_List_ShowsActiveWatches` | List response shape |
| `Action_Teleport_RoundTrip` | HTTP → main thread → position set |
| `SceneDelta_RoundTrip` | HTTP → baseline → changes |

---

## Wire Format Examples

### watch:create request

```json
{
  "operation": "create",
  "target": "/Enemies/Scout_02",
  "track": ["position", "current_hp"],
  "condition": {
    "type": "threshold",
    "property": "current_hp",
    "below": 25
  },
  "throttle_ms": 1000,
  "label": "enemy_low_health"
}
```

### watch:create response

```json
{
  "result": "ok",
  "watch_id": "w_01",
  "target": "/Enemies/Scout_02",
  "track": ["position", "current_hp"],
  "condition": {
    "type": "threshold",
    "property": "current_hp",
    "below": 25.0
  },
  "throttle_ms": 1000,
  "label": "enemy_low_health",
  "active_watches": 1,
  "frame": 4500,
  "time": 75.0,
  "play_mode": true
}
```

### Watch trigger notification (SSE)

```json
{
  "jsonrpc": "2.0",
  "method": "notifications/theatre/watch_triggered",
  "params": {
    "watch_id": "w_01",
    "label": "enemy_low_health",
    "frame": 5200,
    "trigger": {
      "condition": "threshold",
      "property": "current_hp",
      "value": 22,
      "threshold": 25,
      "path": "/Enemies/Scout_02",
      "instance_id": 14820
    }
  }
}
```

### watch:remove response

```json
{
  "result": "ok",
  "watch_id": "w_01",
  "active_watches": 0
}
```

### action:teleport response

```json
{
  "result": "ok",
  "path": "/Player",
  "instance_id": 10240,
  "position": [10.0, 0.0, 5.0],
  "previous_position": [0.0, 1.05, 0.0],
  "rotation_euler": [0.0, 180.0, 0.0],
  "previous_rotation_euler": [0.0, 90.0, 0.0],
  "frame": 4580,
  "time": 76.33,
  "play_mode": true
}
```

### action:set_property response

```json
{
  "result": "ok",
  "path": "/Enemies/Scout_02",
  "instance_id": 14820,
  "component": "Health",
  "property": "current_hp",
  "value": 100,
  "previous_value": 22,
  "frame": 4580,
  "time": 76.33,
  "play_mode": true
}
```

### scene_delta response

```json
{
  "from_frame": 4500,
  "to_frame": 4580,
  "elapsed_seconds": 1.33,
  "changes": [
    {
      "path": "/Player",
      "instance_id": 10240,
      "changed": {
        "position": {
          "from": [0.0, 1.0, -2.0],
          "to": [0.0, 1.05, 0.0]
        }
      }
    }
  ],
  "spawned": [],
  "destroyed": [
    {
      "path": "/Projectiles/Bullet_47",
      "instance_id": 18920
    }
  ],
  "frame": 4580,
  "time": 76.33,
  "play_mode": true
}
```

---

## Implementation Order

1. **Unit 1** — WatchTypes (data structures, no dependencies)
2. **Unit 2** — WatchEngine (depends on Unit 1, ObjectResolver, ResponseHelpers)
3. **Unit 3** — WatchTool (depends on Units 1-2, SseStreamManager)
4. **Unit 5a** — ActionTeleport (standalone, depends on ObjectResolver)
5. **Unit 5b** — ActionSetProperty (standalone, depends on ObjectResolver, PropertySerializer)
6. **Unit 5c** — ActionSetActive, ActionSetTimescale, ActionPlayControl (standalone)
7. **Unit 5d** — ActionInvokeMethod (standalone, depends on ObjectResolver)
8. **Unit 4** — ActionTool (dispatch shell, depends on Units 5a-5d)
9. **Unit 6** — SceneDeltaTool (standalone, depends on ObjectResolver, ResponseHelpers)
10. **Unit 7** — Server integration (depends on all above)
