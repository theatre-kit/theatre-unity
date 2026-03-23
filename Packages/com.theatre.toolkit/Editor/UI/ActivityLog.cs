using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// Ring buffer that records the last N MCP tool calls for display
    /// in the Theatre panel. Persisted to SessionState for domain
    /// reload survival.
    /// </summary>
    public sealed class ActivityLog
    {
        public const int MaxEntries = 100;
        private const string SessionKeyLog    = "Theatre_ActivityLog";
        private const string SessionKeyCalls  = "Theatre_ActivityCalls";
        private const string SessionKeyChars = "Theatre_ActivityTokens";

        /// <summary>A single tool call log entry.</summary>
        public struct Entry
        {
            /// <summary>Time formatted as HH:mm:ss.</summary>
            public string Timestamp;
            /// <summary>The MCP tool name.</summary>
            public string ToolName;
            /// <summary>The operation, extracted from args["operation"] if present.</summary>
            public string Operation;
            /// <summary>Response size in characters.</summary>
            public int ResponseChars;
            /// <summary>True if the result JSON contains an "error" key.</summary>
            public bool IsError;
        }

        private readonly List<Entry> _entries = new();

        /// <summary>All entries, newest first.</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>Total tool calls recorded this session.</summary>
        public int TotalCalls { get; private set; }

        /// <summary>Total response characters this session.</summary>
        public int TotalChars { get; private set; }

        /// <summary>
        /// Record a tool call. Inserts at index 0 (newest first).
        /// Removes oldest entries if the buffer exceeds MaxEntries.
        /// </summary>
        public void Record(string toolName, JToken args, string resultJson)
        {
            var operation = args?["operation"]?.Value<string>();
            var chars     = resultJson?.Length ?? 0;
            var isError   = resultJson != null && resultJson.Contains("\"error\"");

            var entry = new Entry
            {
                Timestamp      = DateTime.Now.ToString("HH:mm:ss"),
                ToolName       = toolName ?? string.Empty,
                Operation      = operation,
                ResponseChars = chars,
                IsError        = isError,
            };

            _entries.Insert(0, entry);

            while (_entries.Count > MaxEntries)
                _entries.RemoveAt(_entries.Count - 1);

            TotalCalls++;
            TotalChars += chars;
        }

        /// <summary>Clear all entries and reset counters.</summary>
        public void Clear()
        {
            _entries.Clear();
            TotalCalls = 0;
            TotalChars = 0;
        }

        /// <summary>Persist entries and counters to SessionState.</summary>
        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_entries);
                SessionState.SetString(SessionKeyLog,    json);
                SessionState.SetInt(SessionKeyCalls,  TotalCalls);
                SessionState.SetInt(SessionKeyChars, TotalChars);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Theatre] ActivityLog.Save failed: {ex.Message}");
            }
        }

        /// <summary>Restore entries and counters from SessionState.</summary>
        public void Restore()
        {
            try
            {
                var json = SessionState.GetString(SessionKeyLog, null);
                if (!string.IsNullOrEmpty(json))
                {
                    var restored = JsonConvert.DeserializeObject<List<Entry>>(json);
                    if (restored != null)
                    {
                        _entries.Clear();
                        _entries.AddRange(restored);
                    }
                }

                TotalCalls  = SessionState.GetInt(SessionKeyCalls,  0);
                TotalChars = SessionState.GetInt(SessionKeyChars, 0);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Theatre] ActivityLog.Restore failed: {ex.Message}");
            }
        }
    }
}
