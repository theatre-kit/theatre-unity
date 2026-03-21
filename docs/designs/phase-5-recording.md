# Design: Phase 5 — Stage: Recording

## Overview

Frame-by-frame capture system ("Dashcam") that records tracked GameObject
property values to SQLite, enabling temporal analysis of game state.
Agents can start/stop recordings, insert markers, query frame ranges,
diff arbitrary frames, and run structured analysis queries.

**Exit criteria**: Human reproduces a bug, agent scrubs through the
recording to find when and where things went wrong.

## Decisions

- **Storage**: Microsoft.Data.Sqlite bundled with native binaries in `Plugins/`
- **Analysis**: Level 2 — `query_range`, `diff_frames`, plus `analyze` with
  structured threshold/min/max predicates
- **Capture performance**: Hybrid serializer — fast-path for Transform +
  common components, fallback to SerializedProperty for custom MonoBehaviours

---

## Architecture

```
Runtime/Stage/Recording/
  RecordingTypes.cs       — ClipMetadata, FrameRecord, MarkerRecord, AnalyzeQuery
  RecordingEngine.cs      — Capture loop, tick dispatch, frame serialization
  RecordingDb.cs          — SQLite schema, reads, writes, analyze queries
  RecordingPersistence.cs — SessionState persistence for active recording state
  FrameSerializer.cs      — Hybrid fast-path + SerializedProperty serializer

Editor/Tools/
  RecordingTool.cs        — MCP compound tool (start, stop, marker, list_clips, etc.)

Plugins/                  — Native SQLite binaries (x86_64: .dll, .bundle, .so)
```

The Recording/ directory lives in Runtime assembly (same as WatchEngine,
SpatialIndex) so that `RecordingEngine` and `RecordingDb` don't depend
on UnityEditor. `FrameSerializer` needs `#if UNITY_EDITOR` for
`SerializedProperty` fallback. `RecordingTool` lives in Editor assembly
(same as all other tool handlers).

---

## Implementation Units

### Unit 1: SQLite Integration — Native Binaries + Assembly Config

**Files**:
- `Packages/com.theatre.toolkit/Plugins/x86_64/sqlite3.dll` (Windows)
- `Packages/com.theatre.toolkit/Plugins/x86_64/sqlite3.bundle` (macOS)
- `Packages/com.theatre.toolkit/Plugins/x86_64/libsqlite3.so` (Linux)
- `Packages/com.theatre.toolkit/Plugins/x86_64/*.meta` files with platform settings
- `Packages/com.theatre.toolkit/Plugins/Microsoft.Data.Sqlite.dll`
- `Packages/com.theatre.toolkit/Plugins/Microsoft.Data.Sqlite.dll.meta`
- `Packages/com.theatre.toolkit/Runtime/com.theatre.toolkit.runtime.asmdef` (update)
- `Packages/com.theatre.toolkit/Tests/Editor/com.theatre.toolkit.editor.tests.asmdef` (update)

**Approach**:

Download Microsoft.Data.Sqlite 8.0.x from NuGet. Extract the managed DLL
and platform-specific native SQLite binaries. Place in `Plugins/` with
correct `.meta` platform import settings.

Update the runtime asmdef:
```json
{
  "overrideReferences": true,
  "precompiledReferences": ["Microsoft.Data.Sqlite.dll"]
}
```

Update the test asmdef precompiledReferences:
```json
"precompiledReferences": [
  "nunit.framework.dll",
  "Newtonsoft.Json.dll",
  "Microsoft.Data.Sqlite.dll"
]
```

**Implementation Notes**:
- Microsoft.Data.Sqlite is a thin managed wrapper around native sqlite3.
  The managed DLL goes in `Plugins/` root (no platform subfolder). The
  native binaries go in `Plugins/x86_64/` with per-platform meta settings.
- macOS may need a universal binary (arm64 + x86_64) for Apple Silicon.
  Check if the NuGet package includes one; if not, build it or use the
  `SQLitePCLRaw.bundle_e_sqlite3` package which handles this.
- The `.meta` files for native binaries set `isPreloaded: 0` and
  platform-specific `CPU: x86_64` or `AnyCPU` as appropriate.

**Acceptance Criteria**:
- [ ] `using Microsoft.Data.Sqlite;` compiles in Runtime assembly
- [ ] `new SqliteConnection("Data Source=:memory:")` opens without error
- [ ] `SqliteCommand.ExecuteNonQuery()` creates a table and inserts a row
- [ ] Compiles on Windows, macOS, and Linux (if all three binaries present)

---

### Unit 2: RecordingTypes — Data Model

**File**: `Packages/com.theatre.toolkit/Runtime/Stage/Recording/RecordingTypes.cs`

```csharp
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
```

**Acceptance Criteria**:
- [ ] All types compile
- [ ] `ClipMetadata` round-trips through `JsonConvert.SerializeObject` / `DeserializeObject`
- [ ] All JSON field names are snake_case per CONTRACTS.md

---

### Unit 3: RecordingDb — SQLite Schema and CRUD

**File**: `Packages/com.theatre.toolkit/Runtime/Stage/Recording/RecordingDb.cs`

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Theatre.Stage
{
    /// <summary>
    /// SQLite database for a single recording clip.
    /// One database file per clip, stored in Library/Theatre/.
    /// </summary>
    public sealed class RecordingDb : IDisposable
    {
        /// <summary>Open or create a recording database at the given path.</summary>
        public RecordingDb(string dbPath);

        /// <summary>Create the schema tables if they don't exist.</summary>
        public void EnsureSchema();

        // --- Write Operations (called during recording) ---

        /// <summary>Write metadata for this clip.</summary>
        public void WriteMetadata(ClipMetadata metadata);

        /// <summary>Update metadata (e.g., end_frame, frame_count on stop).</summary>
        public void UpdateMetadata(ClipMetadata metadata);

        /// <summary>
        /// Write a batch of frames. Uses a transaction for performance.
        /// Each frame's Objects dictionary is serialized as JSON.
        /// </summary>
        public void WriteFrames(List<FrameRecord> frames);

        /// <summary>Insert a marker.</summary>
        public void WriteMarker(MarkerRecord marker);

        // --- Read Operations (called by tool queries) ---

        /// <summary>Read clip metadata.</summary>
        public ClipMetadata ReadMetadata();

        /// <summary>Read all markers for this clip.</summary>
        public List<MarkerRecord> ReadMarkers();

        /// <summary>
        /// Query frames in a range. Returns full snapshots (not deltas) by
        /// replaying deltas from the start. Caller specifies which paths and
        /// properties to include.
        /// </summary>
        /// <param name="fromFrame">Start frame (inclusive).</param>
        /// <param name="toFrame">End frame (inclusive).</param>
        /// <param name="paths">Filter to these object paths (null = all).</param>
        /// <param name="properties">Filter to these properties (null = all).</param>
        /// <returns>List of reconstructed frame records.</returns>
        public List<FrameRecord> QueryRange(
            int fromFrame, int toFrame,
            string[] paths = null, string[] properties = null);

        /// <summary>
        /// Get full reconstructed state at a specific frame by replaying
        /// deltas from the beginning.
        /// </summary>
        public FrameRecord ReconstructFrame(int frame);

        /// <summary>
        /// Diff two frames: returns only properties that differ between them.
        /// </summary>
        public FrameRecord DiffFrames(int frameA, int frameB);

        // --- Analysis Operations ---

        /// <summary>
        /// Run a threshold analysis: find frames where a property crosses
        /// the given threshold(s).
        /// </summary>
        public AnalyzeResult AnalyzeThreshold(AnalyzeQuery query);

        /// <summary>
        /// Find the min or max value of a property across a frame range.
        /// </summary>
        public AnalyzeResult AnalyzeMinMax(AnalyzeQuery query);

        /// <summary>
        /// Count the number of times a property value changed in a range.
        /// </summary>
        public AnalyzeResult AnalyzeCountChanges(AnalyzeQuery query);

        public void Dispose();
    }
}
```

**SQLite Schema** (3 tables):

```sql
CREATE TABLE IF NOT EXISTS metadata (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
-- Stores ClipMetadata as individual key-value pairs for easy updates.
-- Keys: clip_id, label, scene, start_frame, start_time, end_frame,
--        end_time, frame_count, capture_rate, track_paths, track_components,
--        created_at

CREATE TABLE IF NOT EXISTS frames (
    frame   INTEGER PRIMARY KEY,
    time    REAL NOT NULL,
    data    TEXT NOT NULL   -- JSON: { "<instance_id>": { "path": "...", "properties": {...} } }
);
-- Each row is a delta frame: only changed properties since previous frame.
-- Frame 0 (or the first frame) is a full snapshot.

CREATE TABLE IF NOT EXISTS markers (
    frame    INTEGER NOT NULL,
    time     REAL NOT NULL,
    label    TEXT NOT NULL,
    metadata TEXT   -- JSON, nullable
);
```

**Implementation Notes**:
- **WAL mode**: Enable `PRAGMA journal_mode=WAL;` on open for concurrent
  read/write without blocking.
- **Batch writes**: `WriteFrames()` wraps all inserts in a single transaction
  for performance.
- **Delta reconstruction**: `ReconstructFrame(frame)` reads all delta frames
  from 0 to `frame` and merges them. This is O(n) in frame count. For clips
  up to ~18000 frames (5 min at 60fps), this is acceptable. If needed later,
  add keyframes every N frames (not in Phase 5 scope).
- **Frame data format**: The `data` column stores JSON where keys are
  instance_id strings, values are `ObjectFrame` objects. Only changed
  properties are stored per frame (delta compression).
- **Path filtering**: `QueryRange` with paths filter does a post-query
  filter on the deserialized JSON, not SQL-level filtering (the data column
  is opaque JSON). This is fine for the expected data volumes.
- **File location**: `Library/Theatre/rec_{clipId}.sqlite3`. The `Library/`
  folder is gitignored, local, and survives editor restarts.

**Acceptance Criteria**:
- [ ] Schema created on first open
- [ ] WriteFrames + ReadMetadata round-trips correctly
- [ ] WriteMarker + ReadMarkers returns markers in frame order
- [ ] ReconstructFrame applies deltas correctly (test: write 3 delta frames,
      reconstruct frame 3, verify merged state)
- [ ] DiffFrames returns only properties that differ
- [ ] AnalyzeThreshold finds correct crossing frames
- [ ] AnalyzeMinMax returns correct extremal value and frame
- [ ] Database file is created in Library/Theatre/

---

### Unit 4: FrameSerializer — Hybrid Fast-Path + SerializedProperty

**File**: `Packages/com.theatre.toolkit/Runtime/Stage/Recording/FrameSerializer.cs`

```csharp
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
            string[] trackComponents);

        /// <summary>
        /// Capture a delta frame: only properties that changed since
        /// the previous snapshot. Returns null entries for objects
        /// with no changes.
        /// </summary>
        public static Dictionary<int, ObjectFrame> CaptureDelta(
            List<GameObject> trackedObjects,
            string[] trackComponents,
            Dictionary<int, ObjectFrame> previousSnapshot);

        /// <summary>
        /// Fast-path: read Transform properties directly.
        /// Avoids SerializedProperty overhead (~10x faster).
        /// </summary>
        internal static void CaptureTransform(
            Transform t, Dictionary<string, JToken> props);

        /// <summary>
        /// Fast-path: read Rigidbody properties directly.
        /// </summary>
        internal static void CaptureRigidbody(
            Rigidbody rb, Dictionary<string, JToken> props);

        /// <summary>
        /// Fast-path: read Rigidbody2D properties directly.
        /// </summary>
        internal static void CaptureRigidbody2D(
            Rigidbody2D rb, Dictionary<string, JToken> props);

        /// <summary>
        /// Fallback: read properties via SerializedProperty.
        /// Used for custom MonoBehaviours and uncommon built-in types.
        /// </summary>
        internal static void CaptureViaSerializedProperty(
            Component comp, Dictionary<string, JToken> props);
    }
}
```

**Fast-path property sets**:

| Component | Properties captured |
|-----------|-------------------|
| `Transform` | `position`, `euler_angles`, `local_position`, `local_euler_angles`, `local_scale` |
| `Rigidbody` | `velocity`, `angular_velocity`, `is_kinematic`, `mass` |
| `Rigidbody2D` | `velocity`, `angular_velocity`, `is_kinematic`, `mass` |

All other tracked components use the SerializedProperty fallback
(guarded by `#if UNITY_EDITOR`).

**Implementation Notes**:
- **Delta compression**: `CaptureDelta` compares each property value
  against `previousSnapshot` using `JToken.DeepEquals`. Only changed
  values are included in the result. This means most frames are very small.
- **Object pooling**: Reuse `Dictionary<string, JToken>` instances via
  a pool to reduce GC pressure. Pre-allocate for the expected number of
  tracked objects.
- **Track filtering**: If `trackComponents` is specified, only capture
  properties from those component types. If null, capture Transform + all
  components on the object.
- **Destroyed objects**: If a previously tracked object is now null
  (destroyed), emit a sentinel entry: `{ "path": "...", "destroyed": true }`.

**Acceptance Criteria**:
- [ ] CaptureFullSnapshot returns correct property values for a
      known test GameObject
- [ ] CaptureDelta returns empty dict for unchanged objects
- [ ] CaptureDelta detects position changes correctly
- [ ] Fast-path Transform capture matches SerializedProperty output
      (values equal within rounding tolerance)
- [ ] Track component filtering only captures specified types

---

### Unit 5: RecordingEngine — Capture Loop and Lifecycle

**File**: `Packages/com.theatre.toolkit/Runtime/Stage/Recording/RecordingEngine.cs`

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Theatre.Stage
{
    /// <summary>
    /// Manages the recording lifecycle: start, capture frames, stop.
    /// Hooks into EditorApplication.update for periodic capture.
    /// Persists active state to SessionState for domain reload survival.
    /// </summary>
    public sealed class RecordingEngine
    {
        /// <summary>Whether a recording is currently active.</summary>
        public bool IsRecording { get; }

        /// <summary>The active recording state, or null.</summary>
        public RecordingState ActiveState { get; }

        /// <summary>
        /// Initialize the engine. Restores active recording state from
        /// SessionState if one was interrupted by a domain reload.
        /// </summary>
        /// <param name="notifyCallback">
        /// Called with notification params when recording events occur
        /// (started, stopped, marker). Caller wraps in JSON-RPC notification.
        /// </param>
        public void Initialize(Action<JObject> notifyCallback);

        /// <summary>
        /// Start a new recording.
        /// </summary>
        /// <param name="label">Human-readable label for the clip.</param>
        /// <param name="trackPaths">Glob patterns for objects to track (null = all).</param>
        /// <param name="trackComponents">Component types to capture (null = all + Transform).</param>
        /// <param name="captureRate">Frames per second (default 60).</param>
        /// <returns>ClipMetadata for the new clip, or null if already recording.</returns>
        public ClipMetadata Start(
            string label,
            string[] trackPaths = null,
            string[] trackComponents = null,
            int captureRate = 60);

        /// <summary>
        /// Stop the active recording.
        /// </summary>
        /// <returns>Final ClipMetadata with end frame/time/count, or null if not recording.</returns>
        public ClipMetadata Stop();

        /// <summary>
        /// Insert a marker at the current frame.
        /// </summary>
        /// <returns>The marker record, or null if not recording.</returns>
        public MarkerRecord InsertMarker(string label, JObject metadata = null);

        /// <summary>
        /// Called every editor update tick. Captures a frame if the
        /// capture interval has elapsed.
        /// </summary>
        public void Tick();

        /// <summary>
        /// Shut down — close any open database, finalize recording.
        /// Called on editor quit or server stop.
        /// </summary>
        public void Shutdown();
    }
}
```

**Implementation Notes**:
- **Tick rate control**: Track `_tickCounter` and only capture when
  `_tickCounter % (editorFrameRate / captureRate)` == 0. Since Unity
  editor runs at variable rate, use `Time.realtimeSinceStartup` to
  maintain actual capture rate regardless of editor framerate.
- **Frame buffer**: Accumulate captured frames in a `List<FrameRecord>`
  buffer. Flush to SQLite every 10 frames (configurable). This batches
  writes for performance.
- **Buffer flush on stop**: `Stop()` flushes remaining buffered frames
  before closing the database.
- **Object discovery**: On `Start()`, resolve `trackPaths` globs against
  the current scene hierarchy using `ObjectResolver.GetAllRoots()` and
  `HierarchyWalker.Find()`. Cache the resolved `List<GameObject>`.
  Re-resolve periodically (every ~60 ticks) to pick up spawned/destroyed
  objects.
- **Domain reload**: `RecordingPersistence` saves `RecordingState` to
  `SessionState`. On `Initialize()`, if a state is found, reopen the
  SQLite database and resume recording from where it left off. Brief
  gap during reload (~100ms) is acceptable.
- **Notifications**: Fire SSE notifications:
  - `notifications/theatre/recording_started` with `{ clip_id, label }`
  - `notifications/theatre/recording_stopped` with `{ clip_id, label, duration, frame_count }`
  - `notifications/theatre/recording_marker` with `{ clip_id, frame, label }`

**Acceptance Criteria**:
- [ ] Start creates a new SQLite file in Library/Theatre/
- [ ] Tick captures frames at approximately the configured rate
- [ ] Stop finalizes metadata and closes the database
- [ ] InsertMarker adds marker to the active database
- [ ] Delta compression: only changed properties stored per frame
- [ ] Frame buffer flushes in batches (verify batch size via frame count)
- [ ] Domain reload: recording resumes after simulated reload
- [ ] SSE notifications fire for start/stop/marker events

---

### Unit 6: RecordingPersistence — SessionState Survival

**File**: `Packages/com.theatre.toolkit/Runtime/Stage/Recording/RecordingPersistence.cs`

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Stage
{
    /// <summary>
    /// Persists active recording state and clip index to SessionState
    /// for domain reload survival. Follows the same pattern as WatchPersistence.
    /// </summary>
    internal static class RecordingPersistence
    {
        private const string ActiveKey = "Theatre_ActiveRecording";
        private const string ClipIndexKey = "Theatre_RecordingClips";

        /// <summary>Save active recording state.</summary>
        public static void SaveActive(RecordingState state);

        /// <summary>Clear active recording state.</summary>
        public static void ClearActive();

        /// <summary>Restore active recording state. Returns null if none.</summary>
        public static RecordingState RestoreActive();

        /// <summary>Save the clip index (list of all completed clip metadata).</summary>
        public static void SaveClipIndex(List<ClipMetadata> clips);

        /// <summary>Restore the clip index.</summary>
        public static List<ClipMetadata> RestoreClipIndex();
    }
}
```

**Implementation Notes**:
- Follows `WatchPersistence` pattern exactly: `SessionState.SetString` /
  `GetString` with `JsonConvert.SerializeObject`.
- Clip index is a lightweight list of `ClipMetadata` objects (not the
  actual frame data, which lives in SQLite files).
- Active recording state includes the `DbPath` so the engine can reopen
  the correct SQLite file after domain reload.

**Acceptance Criteria**:
- [ ] SaveActive + RestoreActive round-trips RecordingState correctly
- [ ] ClearActive makes RestoreActive return null
- [ ] SaveClipIndex + RestoreClipIndex round-trips a list of ClipMetadata

---

### Unit 7: RecordingTool — MCP Compound Tool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/RecordingTool.cs`

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// MCP tool: recording
    /// Compound tool for frame-by-frame game state capture and analysis.
    /// Operations: start, stop, marker, list_clips, delete_clip,
    ///             query_range, diff_frames, clip_info, analyze.
    /// </summary>
    public static class RecordingTool
    {
        private static readonly JToken s_inputSchema;
        private static RecordingEngine s_engine;

        static RecordingTool();

        public static void Register(ToolRegistry registry);

        /// <summary>Get or create the RecordingEngine.</summary>
        internal static RecordingEngine GetEngine();

        /// <summary>Shut down engine on server stop.</summary>
        internal static void Shutdown();

        private static string Execute(JToken arguments);

        // --- Operation handlers ---
        private static string ExecuteStart(JObject args);
        private static string ExecuteStop(JObject args);
        private static string ExecuteMarker(JObject args);
        private static string ExecuteListClips(JObject args);
        private static string ExecuteDeleteClip(JObject args);
        private static string ExecuteQueryRange(JObject args);
        private static string ExecuteDiffFrames(JObject args);
        private static string ExecuteClipInfo(JObject args);
        private static string ExecuteAnalyze(JObject args);

        /// <summary>SSE notification callback.</summary>
        private static void OnRecordingEvent(JObject notifParams);
    }
}
```

**JSON Schema** (input_schema for the compound tool):

```json
{
  "type": "object",
  "properties": {
    "operation": {
      "type": "string",
      "enum": ["start", "stop", "marker", "list_clips", "delete_clip",
               "query_range", "diff_frames", "clip_info", "analyze"],
      "description": "The recording operation to perform."
    },
    "label": {
      "type": "string",
      "description": "Used by start (clip name) and marker (marker name)."
    },
    "track_paths": {
      "type": "array", "items": { "type": "string" },
      "description": "Glob patterns for objects to record. Used by start."
    },
    "track_components": {
      "type": "array", "items": { "type": "string" },
      "description": "Component types to capture. Used by start."
    },
    "capture_rate": {
      "type": "integer", "default": 60, "minimum": 1, "maximum": 120,
      "description": "Capture rate in fps. Used by start."
    },
    "clip_id": {
      "type": "string",
      "description": "Clip identifier. Used by delete_clip, query_range, diff_frames, clip_info, analyze."
    },
    "from_frame": {
      "type": "integer",
      "description": "Start frame for range queries."
    },
    "to_frame": {
      "type": "integer",
      "description": "End frame for range queries."
    },
    "frame_a": {
      "type": "integer",
      "description": "First frame for diff_frames."
    },
    "frame_b": {
      "type": "integer",
      "description": "Second frame for diff_frames."
    },
    "paths": {
      "type": "array", "items": { "type": "string" },
      "description": "Filter by object paths. Used by query_range."
    },
    "properties": {
      "type": "array", "items": { "type": "string" },
      "description": "Filter by property names. Used by query_range."
    },
    "query": {
      "type": "string", "enum": ["threshold", "min", "max", "count_changes"],
      "description": "Analysis query type. Used by analyze."
    },
    "path": {
      "type": "string",
      "description": "Target path. Used by analyze."
    },
    "property": {
      "type": "string",
      "description": "Target property name. Used by analyze."
    },
    "below": { "type": "number", "description": "Threshold: below value. Used by analyze." },
    "above": { "type": "number", "description": "Threshold: above value. Used by analyze." },
    "metadata": {
      "type": "object",
      "description": "Arbitrary metadata to attach to a marker."
    },
    "budget": {
      "type": "integer", "default": 1500, "minimum": 100, "maximum": 4000,
      "description": "Token budget for range query responses."
    }
  },
  "required": ["operation"]
}
```

**Registration**:
```csharp
registry.Register(new ToolRegistration(
    name: "recording",
    description: "Record and analyze game state frame-by-frame. "
        + "Start/stop recordings, insert markers, query frame ranges, "
        + "compare frames, and run analysis queries on captured data.",
    inputSchema: s_inputSchema,
    group: ToolGroup.StageRecording,
    handler: Execute,
    annotations: new McpToolAnnotations { ReadOnlyHint = false }
));
```

**Response shapes** (per operation):

`start`:
```json
{
  "result": "ok",
  "clip_id": "rec_001",
  "label": "wall_clip_repro",
  "capture_rate": 60,
  "track_paths": ["/Player", "/Environment/Walls/*"],
  "track_components": ["Transform", "Health"],
  "frame": 4580,
  "time": 76.33,
  "play_mode": true
}
```

`stop`:
```json
{
  "result": "ok",
  "clip_id": "rec_001",
  "label": "wall_clip_repro",
  "duration": 12.4,
  "frame_count": 744,
  "file_size_bytes": 847000,
  "frame": 5324,
  "time": 88.73,
  "play_mode": true
}
```

`marker`:
```json
{
  "result": "ok",
  "clip_id": "rec_001",
  "marker": { "frame": 4600, "time": 76.66, "label": "bug_start" },
  "frame": 4600,
  "time": 76.66,
  "play_mode": true
}
```

`list_clips`:
```json
{
  "clips": [
    { "clip_id": "rec_001", "label": "...", "duration": 12.4, "frame_count": 744, ... }
  ],
  "count": 1
}
```

`clip_info`:
```json
{
  "clip_id": "rec_001",
  "label": "wall_clip_repro",
  "scene": "SampleScene",
  "duration": 12.4,
  "frame_count": 744,
  "capture_rate": 60,
  "track_paths": [...],
  "track_components": [...],
  "markers": [
    { "frame": 4600, "time": 76.66, "label": "bug_start" }
  ],
  "file_size_bytes": 847000,
  "created_at": "2026-03-20T14:10:00Z"
}
```

`query_range`:
```json
{
  "clip_id": "rec_001",
  "from_frame": 100,
  "to_frame": 200,
  "frames": [
    {
      "frame": 100, "time": 1.67,
      "objects": [
        { "path": "/Player", "instance_id": 10240,
          "properties": { "position": [0, 1, 0], "current_hp": 100 } }
      ]
    }
  ],
  "returned": 50,
  "total_frames": 100,
  "budget": { "requested": 1500, "used": 1200, "truncated": true, ... }
}
```

`diff_frames`:
```json
{
  "clip_id": "rec_001",
  "frame_a": 100,
  "frame_b": 200,
  "elapsed_seconds": 1.67,
  "changes": [
    {
      "path": "/Player", "instance_id": 10240,
      "changed": {
        "position": { "from": [0, 1, 0], "to": [2, 1, 3] },
        "current_hp": { "from": 100, "to": 73 }
      }
    }
  ]
}
```

`analyze`:
```json
{
  "clip_id": "rec_001",
  "query": "threshold",
  "path": "/Player",
  "property": "current_hp",
  "matches": [
    { "frame": 847, "time": 14.1, "value": 23 },
    { "frame": 912, "time": 15.2, "value": 18 }
  ]
}
```

**Implementation Notes**:
- `start` requires play mode (`ResponseHelpers.RequirePlayMode`)
- `stop`, `marker` require an active recording
- `list_clips`, `clip_info`, `query_range`, `diff_frames`, `analyze`
  work on completed clips (open the SQLite file read-only)
- `delete_clip` removes the SQLite file and updates the clip index
- `query_range` uses `TokenBudget` for response sizing
- Clip ID format: `"rec_" + _nextId.ToString("D3")` (rec_001, rec_002, ...)
- Counter persisted in SessionState via `RecordingPersistence`

**Acceptance Criteria**:
- [ ] All 9 operations dispatch correctly
- [ ] `start` returns error if already recording
- [ ] `start` returns error if not in play mode
- [ ] `stop` returns error if not recording
- [ ] `marker` returns error if not recording
- [ ] `query_range` respects token budget and truncates
- [ ] `analyze` threshold query returns correct crossing frames
- [ ] `delete_clip` removes file and updates index
- [ ] `clip_info` includes markers
- [ ] All responses include frame context per response-building pattern

---

### Unit 8: Server Integration — Registration and Lifecycle

**Files**:
- `Packages/com.theatre.toolkit/Editor/TheatreServer.cs` (modify)

**Changes**:
1. Add `RecordingTool.Register(registry);` to `RegisterBuiltInTools()`
2. Add `RecordingTool.Shutdown();` to `StopServer()`
3. Hook `EditorApplication.update += RecordingTool.GetEngine().Tick;`
   in `RecordingTool.GetEngine()` (lazy init, same pattern as WatchTool)

**Acceptance Criteria**:
- [ ] `recording` tool appears in `tools/list` when StageRecording is enabled
- [ ] Recording engine ticks during play mode
- [ ] Server stop cleans up recording engine

---

## Implementation Order

```
Unit 1: SQLite Integration (native binaries + asmdef)
  └─ Unit 2: RecordingTypes (data model, no dependencies)
     └─ Unit 3: RecordingDb (SQLite CRUD, depends on types + sqlite)
     └─ Unit 4: FrameSerializer (capture logic, depends on types)
        └─ Unit 5: RecordingEngine (orchestrates db + serializer)
           └─ Unit 6: RecordingPersistence (SessionState, depends on types)
              └─ Unit 7: RecordingTool (MCP tool, depends on engine)
                 └─ Unit 8: Server Integration (wiring)
```

Units 2-4 can be parallelized after Unit 1 is complete. Units 5-8 are
sequential.

---

## Testing

### Unit Tests: `Tests/Editor/RecordingTests.cs`

```csharp
[TestFixture]
public class RecordingDbTests
{
    // Setup: create temp SQLite db in Library/Theatre/test_*.sqlite3
    // Teardown: delete temp db file

    [Test] public void Schema_CreatedOnOpen() { /* verify tables exist */ }
    [Test] public void WriteMetadata_ReadMetadata_RoundTrips() { }
    [Test] public void WriteFrames_QueryRange_ReturnsFrames() { }
    [Test] public void DeltaReconstruction_MergesCorrectly() { }
    [Test] public void DiffFrames_ReturnsOnlyChanges() { }
    [Test] public void WriteMarker_ReadMarkers_InOrder() { }
    [Test] public void AnalyzeThreshold_FindsCrossings() { }
    [Test] public void AnalyzeMinMax_FindsExtremalFrame() { }
    [Test] public void AnalyzeCountChanges_CountsCorrectly() { }
}

[TestFixture]
public class FrameSerializerTests
{
    // Setup: create test GameObjects with known properties
    // Teardown: DestroyImmediate

    [Test] public void CaptureFullSnapshot_IncludesTransform() { }
    [Test] public void CaptureDelta_EmptyForUnchanged() { }
    [Test] public void CaptureDelta_DetectsPositionChange() { }
    [Test] public void TrackComponentFilter_OnlyCapturesSpecified() { }
    [Test] public void DestroyedObject_EmitsSentinel() { }
}

[TestFixture]
public class RecordingPersistenceTests
{
    // Setup: clear SessionState keys
    // Teardown: clear SessionState keys

    [Test] public void SaveRestore_ActiveRecording_RoundTrips() { }
    [Test] public void ClearActive_MakesRestoreNull() { }
    [Test] public void SaveRestore_ClipIndex_RoundTrips() { }
}

[TestFixture]
public class RecordingEngineTests
{
    // Note: These need Play Mode or simulated Play Mode state.
    // Simplest: test the non-Play-Mode parts (persistence, metadata)
    // and add integration tests for capture later.

    [Test] public void Start_CreatesDbFile() { }
    [Test] public void Start_WhileRecording_ReturnsNull() { }
    [Test] public void Stop_WhileNotRecording_ReturnsNull() { }
    [Test] public void InsertMarker_WhileNotRecording_ReturnsNull() { }
}
```

### Integration Tests (MCP round-trip)

Test via `SceneToolIntegrationTests` pattern — direct tool handler invocation:

```csharp
[TestFixture]
public class RecordingToolIntegrationTests
{
    [Test] public void Start_NotPlayMode_ReturnsError() { }
    [Test] public void ListClips_Empty_ReturnsEmptyArray() { }
    [Test] public void ClipInfo_UnknownClip_ReturnsError() { }
}
```

Full capture integration tests require Play Mode and are deferred to
PlayMode test runner (Phase 5 stretch goal).

---

## Verification Checklist

After implementation:
1. `unity_console {"operation": "refresh"}` — recompile
2. `unity_console {"filter": "error"}` — verify no compile errors
3. `unity_tests {"operation": "run"}` — run all EditMode tests
4. `unity_tests {"operation": "results"}` — verify pass
5. Manual: enter Play Mode, call `recording:start`, wait 2s, call
   `recording:stop`, call `recording:clip_info`, call `recording:query_range`
6. Verify SQLite file exists in `Library/Theatre/`
7. Verify `recording:list_clips` returns the clip
8. Verify `recording:analyze` with threshold query returns results
9. Verify `recording:delete_clip` removes the file
