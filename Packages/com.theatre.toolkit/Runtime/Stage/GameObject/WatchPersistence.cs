using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Stage
{
    /// <summary>
    /// Persists watch definitions to SessionState for domain reload survival.
    /// </summary>
    internal static class WatchPersistence
    {
        private const string SessionKey = "Theatre_Watches";
        private const string CounterKey = "Theatre_WatchCounter";

        /// <summary>Save watch definitions and counter to SessionState.</summary>
        public static void Save(List<WatchState> watches, int nextId)
        {
#if UNITY_EDITOR
            var defs = watches.Select(ws => ws.Definition).ToList();

            var json = JsonConvert.SerializeObject(defs);
            SessionState.SetString(SessionKey, json);
            SessionState.SetInt(CounterKey, nextId);
#endif
        }

        /// <summary>
        /// Restore watches from SessionState. Returns the list of watch states
        /// and the next ID counter.
        /// </summary>
        public static (List<WatchState> watches, int nextId) Restore()
        {
            var watches = new List<WatchState>();
            int nextId = 0;

#if UNITY_EDITOR
            nextId = SessionState.GetInt(CounterKey, 0);
            var json = SessionState.GetString(SessionKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var defs = JsonConvert.DeserializeObject<List<WatchDefinition>>(json);
                    if (defs != null)
                    {
                        watches.AddRange(defs.Select(def => new WatchState
                        {
                            Definition = def,
                            LastTriggeredAt = 0,
                            TriggerCount = 0
                        }));

                        Debug.Log($"[Theatre] Restored {watches.Count} watches after domain reload");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Theatre] Failed to restore watches: {ex.Message}");
                }
            }
#endif

            return (watches, nextId);
        }
    }
}
