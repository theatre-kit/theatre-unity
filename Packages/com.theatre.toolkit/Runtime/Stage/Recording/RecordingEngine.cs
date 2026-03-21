using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Theatre.Stage
{
    /// <summary>
    /// Frame capture engine for recording GameObjects to SQLite.
    /// Persists state to SessionState for domain reload survival.
    /// </summary>
    public sealed class RecordingEngine
    {
        private const int FlushInterval = 10;   // frames between SQLite flushes
        private const int ResolveInterval = 60; // ticks between track re-resolution

        private Action<JObject> _notifyCallback;
        private RecordingState _activeState;
        private RecordingDb _activeDb;
        private List<ClipMetadata> _clipIndex;
        private int _nextId;

        // Per-recording working state
        private List<GameObject> _trackedObjects = new List<GameObject>();
        private Dictionary<int, ObjectFrame> _previousSnapshot;
        private List<FrameRecord> _frameBuffer = new List<FrameRecord>();
        private int _flushTick;
        private int _resolveTick;
        private ClipMetadata _activeMeta;

        /// <summary>Whether a recording is currently active.</summary>
        public bool IsRecording => _activeState != null;

        /// <summary>The active recording state, or null.</summary>
        public RecordingState ActiveState => _activeState;

        /// <summary>All completed clips.</summary>
        public List<ClipMetadata> ClipIndex => _clipIndex;

        /// <summary>
        /// Initialize the engine. Call once at startup.
        /// Restores persisted recording state from SessionState.
        /// </summary>
        public void Initialize(Action<JObject> notifyCallback)
        {
            _notifyCallback = notifyCallback;
            _nextId = RecordingPersistence.RestoreCounter();
            _clipIndex = RecordingPersistence.RestoreClipIndex();

            // Attempt to resume a recording that was active before domain reload
            var restored = RecordingPersistence.RestoreActive();
            if (restored != null)
            {
                if (File.Exists(restored.DbPath))
                {
                    try
                    {
                        _activeState = restored;
                        _activeDb = new RecordingDb(restored.DbPath);
                        _activeMeta = _activeDb.ReadMetadata();
                        ResolveTrackedObjects();
                        Debug.Log($"[Theatre] RecordingEngine: resumed clip '{restored.ClipId}' after domain reload");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Theatre] RecordingEngine: failed to resume recording after domain reload: {ex.Message}");
                        _activeState = null;
                        _activeDb?.Dispose();
                        _activeDb = null;
                        RecordingPersistence.ClearActive();
                    }
                }
                else
                {
                    Debug.LogWarning($"[Theatre] RecordingEngine: SQLite file '{restored.DbPath}' missing, clearing active recording");
                    RecordingPersistence.ClearActive();
                }
            }
        }

        /// <summary>
        /// Start a new recording clip. Returns null if already recording.
        /// </summary>
        public ClipMetadata Start(
            string label,
            string[] trackPaths = null,
            string[] trackComponents = null,
            int captureRate = 60)
        {
            if (_activeState != null) return null;

            _nextId++;
            var clipId = $"rec_{_nextId:D3}";

            // Ensure Library/Theatre directory exists
            var libDir = Path.Combine(Application.dataPath.Replace("/Assets", ""), "Library", "Theatre");
            Directory.CreateDirectory(libDir);

            var dbPath = Path.Combine(libDir, $"rec_{clipId}.sqlite3");

            _activeState = new RecordingState
            {
                ClipId = clipId,
                Label = label ?? clipId,
                DbPath = dbPath,
                CaptureRate = captureRate,
                TrackPaths = trackPaths,
                TrackComponents = trackComponents,
                StartFrame = Time.frameCount,
                StartTime = Time.realtimeSinceStartup,
                FrameCounter = 0,
                TickCounter = 0,
            };

            _activeDb = new RecordingDb(dbPath);

            _activeMeta = new ClipMetadata
            {
                ClipId = clipId,
                Label = label ?? clipId,
                Scene = SceneManager.GetActiveScene().name,
                StartFrame = _activeState.StartFrame,
                StartTime = _activeState.StartTime,
                CaptureRate = captureRate,
                TrackPaths = trackPaths,
                TrackComponents = trackComponents,
                FilePath = dbPath,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            };

            _activeDb.WriteMetadata(_activeMeta);
            RecordingPersistence.SaveCounter(_nextId);
            RecordingPersistence.SaveActive(_activeState);

            // Capture initial snapshot
            ResolveTrackedObjects();
            _previousSnapshot = FrameSerializer.CaptureFullSnapshot(_trackedObjects, trackComponents);
            if (_previousSnapshot.Count > 0)
            {
                var initialFrame = new FrameRecord
                {
                    Frame = _activeState.StartFrame,
                    Time = _activeState.StartTime,
                    Objects = _previousSnapshot,
                };
                _frameBuffer.Add(initialFrame);
                _activeState.FrameCounter++;
            }

            _flushTick = 0;
            _resolveTick = 0;

            // Fire SSE notification
            FireNotification("recording_started", new JObject
            {
                ["clip_id"] = clipId,
                ["label"] = _activeMeta.Label,
            });

            Debug.Log($"[Theatre] RecordingEngine: started clip '{clipId}'");
            return _activeMeta;
        }

        /// <summary>
        /// Stop the active recording. Returns the final metadata, or null if not recording.
        /// </summary>
        public ClipMetadata Stop()
        {
            if (_activeState == null) return null;

            // Flush remaining buffered frames
            if (_frameBuffer.Count > 0)
            {
                _activeDb.WriteFrames(_frameBuffer);
                _frameBuffer.Clear();
            }

            // Update metadata with final state
            _activeMeta.EndFrame = Time.frameCount;
            _activeMeta.EndTime = Time.realtimeSinceStartup;
            _activeMeta.FrameCount = _activeState.FrameCounter;
            _activeDb.UpdateMetadata(_activeMeta);

            // Add to clip index
            _clipIndex.Add(_activeMeta);
            RecordingPersistence.SaveClipIndex(_clipIndex);

            var finalMeta = _activeMeta;
            var clipId = _activeState.ClipId;
            var clipLabel = _activeState.Label;

            // Clean up
            _activeDb.Dispose();
            _activeDb = null;
            _activeState = null;
            _activeMeta = null;
            _trackedObjects.Clear();
            _previousSnapshot = null;
            _frameBuffer.Clear();

            RecordingPersistence.ClearActive();

            // Fire SSE notification
            FireNotification("recording_stopped", new JObject
            {
                ["clip_id"] = clipId,
                ["label"] = clipLabel,
                ["frame_count"] = finalMeta.FrameCount,
            });

            Debug.Log($"[Theatre] RecordingEngine: stopped clip '{clipId}' ({finalMeta.FrameCount} frames)");
            return finalMeta;
        }

        /// <summary>
        /// Insert a marker at the current frame. Returns null if not recording.
        /// </summary>
        public MarkerRecord InsertMarker(string label, JObject metadata = null)
        {
            if (_activeState == null) return null;

            var marker = new MarkerRecord
            {
                Frame = Time.frameCount,
                Time = Time.realtimeSinceStartup,
                Label = label ?? "marker",
                Metadata = metadata,
            };

            _activeDb.WriteMarker(marker);

            // Fire SSE notification
            FireNotification("recording_marker", new JObject
            {
                ["clip_id"] = _activeState.ClipId,
                ["marker_label"] = label,
                ["frame"] = marker.Frame,
            });

            return marker;
        }

        /// <summary>
        /// Called every editor update tick. Captures frames at the configured rate.
        /// </summary>
        public void Tick()
        {
            if (_activeState == null) return;
            if (!Application.isPlaying) return;

            _activeState.TickCounter++;
            _resolveTick++;

            // Re-resolve tracked objects periodically
            if (_resolveTick >= ResolveInterval)
            {
                ResolveTrackedObjects();
                _resolveTick = 0;
            }

            // Rate limiting: only capture when enough ticks have elapsed
            // CaptureRate is in fps; EditorApplication.update fires ~100-200/s
            // Use a simple modulo approach: skip ticks to approximate capture rate
            // Approximate: capture every (100 / captureRate) ticks
            // Since we don't know the exact tick rate, use a time-based approach
            float interval = 1f / _activeState.CaptureRate;
            float elapsed = Time.realtimeSinceStartup - _activeState.StartTime;
            int expectedFrames = (int)(elapsed * _activeState.CaptureRate);
            if (_activeState.FrameCounter > expectedFrames) return;

            // Capture delta frame
            var delta = FrameSerializer.CaptureDelta(_trackedObjects, _activeState.TrackComponents, _previousSnapshot);

            if (delta.Count > 0)
            {
                // Merge delta into previous snapshot for next frame comparison
                if (_previousSnapshot == null)
                    _previousSnapshot = new Dictionary<int, ObjectFrame>();
                foreach (var kvp in delta)
                {
                    if (!_previousSnapshot.TryGetValue(kvp.Key, out var existing))
                    {
                        _previousSnapshot[kvp.Key] = new ObjectFrame
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

                var record = new FrameRecord
                {
                    Frame = Time.frameCount,
                    Time = Time.realtimeSinceStartup,
                    Objects = delta,
                };
                _frameBuffer.Add(record);
            }

            _activeState.FrameCounter++;
            _flushTick++;

            // Flush to SQLite every FlushInterval frames
            if (_flushTick >= FlushInterval && _frameBuffer.Count > 0)
            {
                _activeDb.WriteFrames(_frameBuffer);
                _frameBuffer.Clear();
                _flushTick = 0;

                // Persist state in case of domain reload
                RecordingPersistence.SaveActive(_activeState);
            }
        }

        /// <summary>
        /// Shut down the engine. Stops any active recording and closes the database.
        /// </summary>
        public void Shutdown()
        {
            if (_activeState != null)
            {
                Stop();
            }
            _activeDb?.Dispose();
            _activeDb = null;
        }

        // --- Private helpers ---

        private void ResolveTrackedObjects()
        {
            _trackedObjects.Clear();

            if (_activeState == null) return;

            // If no track paths specified, track all root objects
            if (_activeState.TrackPaths == null || _activeState.TrackPaths.Length == 0)
            {
                _trackedObjects.AddRange(ObjectResolver.GetAllRoots());
                return;
            }

            // Resolve glob patterns
            foreach (var pattern in _activeState.TrackPaths)
            {
                if (string.IsNullOrEmpty(pattern)) continue;

                // Simple path: exact match
                if (!pattern.Contains("*") && !pattern.Contains("?"))
                {
                    var result = ObjectResolver.Resolve(path: pattern);
                    if (result.Success)
                        _trackedObjects.Add(result.GameObject);
                }
                else
                {
                    // Glob: walk hierarchy and match
                    var roots = ObjectResolver.GetAllRoots();
                    foreach (var root in roots)
                    {
                        CollectMatchingObjects(root, pattern, _trackedObjects);
                    }
                }
            }
        }

        private void CollectMatchingObjects(
            GameObject go,
            string pattern,
            List<GameObject> results)
        {
            if (go == null) return;

            var path = ResponseHelpers.GetHierarchyPath(go.transform);
            if (MatchesGlob(path, pattern))
                results.Add(go);

            foreach (Transform child in go.transform)
            {
                CollectMatchingObjects(child.gameObject, pattern, results);
            }
        }

        private static bool MatchesGlob(string path, string pattern)
        {
            // Convert glob to simple pattern matching:
            // * matches any sequence within a path segment
            // ** would match across segments (not implemented, treat as *)
            // For simplicity, use string.EndsWith check for patterns ending in /*
            if (pattern.EndsWith("/*"))
            {
                var prefix = pattern.Substring(0, pattern.Length - 2);
                return path.StartsWith(prefix + "/") || path == prefix;
            }
            if (pattern.EndsWith("/**") || pattern.EndsWith("/*"))
            {
                var prefix = pattern.Substring(0, pattern.LastIndexOf('/'));
                return path.StartsWith(prefix);
            }
            // Exact match or simple wildcard
            return path == pattern || path.StartsWith(pattern.Replace("*", ""));
        }

        private void FireNotification(string eventType, JObject extra)
        {
            if (_notifyCallback == null) return;
            var notif = extra ?? new JObject();
            notif["event_type"] = eventType;
            _notifyCallback.Invoke(notif);
        }
    }
}
