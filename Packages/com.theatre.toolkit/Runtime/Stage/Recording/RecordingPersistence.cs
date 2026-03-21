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
    /// Persists recording state to SessionState for domain reload survival.
    /// </summary>
    public static class RecordingPersistence
    {
        private const string ActiveKey = "Theatre_ActiveRecording";
        private const string ClipIndexKey = "Theatre_RecordingClips";
        private const string CounterKey = "Theatre_RecordingCounter";

        /// <summary>Save active recording state.</summary>
        public static void SaveActive(RecordingState state)
        {
#if UNITY_EDITOR
            if (state == null)
            {
                SessionState.EraseString(ActiveKey);
                return;
            }
            var json = JsonConvert.SerializeObject(state);
            SessionState.SetString(ActiveKey, json);
#endif
        }

        /// <summary>Clear active recording state.</summary>
        public static void ClearActive()
        {
#if UNITY_EDITOR
            SessionState.EraseString(ActiveKey);
#endif
        }

        /// <summary>Restore active recording state. Returns null if none.</summary>
        public static RecordingState RestoreActive()
        {
#if UNITY_EDITOR
            var json = SessionState.GetString(ActiveKey, "");
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonConvert.DeserializeObject<RecordingState>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Theatre] Failed to restore active recording: {ex.Message}");
                return null;
            }
#else
            return null;
#endif
        }

        /// <summary>Save the clip index (list of all completed clip metadata).</summary>
        public static void SaveClipIndex(List<ClipMetadata> clips)
        {
#if UNITY_EDITOR
            if (clips == null || clips.Count == 0)
            {
                SessionState.EraseString(ClipIndexKey);
                return;
            }
            var json = JsonConvert.SerializeObject(clips);
            SessionState.SetString(ClipIndexKey, json);
#endif
        }

        /// <summary>Restore the clip index.</summary>
        public static List<ClipMetadata> RestoreClipIndex()
        {
            var clips = new List<ClipMetadata>();
#if UNITY_EDITOR
            var json = SessionState.GetString(ClipIndexKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var restored = JsonConvert.DeserializeObject<List<ClipMetadata>>(json);
                    if (restored != null)
                        clips.AddRange(restored);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Theatre] Failed to restore clip index: {ex.Message}");
                }
            }
#endif
            return clips;
        }

        /// <summary>Save the recording counter (next clip ID).</summary>
        public static void SaveCounter(int nextId)
        {
#if UNITY_EDITOR
            SessionState.SetInt(CounterKey, nextId);
#endif
        }

        /// <summary>Restore the recording counter.</summary>
        public static int RestoreCounter()
        {
#if UNITY_EDITOR
            return SessionState.GetInt(CounterKey, 0);
#else
            return 0;
#endif
        }
    }
}
