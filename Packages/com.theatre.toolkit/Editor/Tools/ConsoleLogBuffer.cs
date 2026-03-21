using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// Captures Unity Console log entries in a ring buffer.
    /// Subscribes via Application.logMessageReceived on [InitializeOnLoad].
    /// Supports grep filtering, deduplication, and rollup of repeated messages.
    /// </summary>
    [UnityEditor.InitializeOnLoad]
    public static class ConsoleLogBuffer
    {
        /// <summary>A single captured log entry.</summary>
        public sealed class LogEntry
        {
            public string Message;
            public string StackTrace;
            public LogType Type;
            public DateTime Timestamp;
            /// <summary>
            /// How many consecutive identical messages were rolled up into this entry.
            /// 1 means no duplicates. >1 means this entry represents N occurrences.
            /// </summary>
            public int RepeatCount;

            public LogEntry(string message, string stackTrace, LogType type)
            {
                Message = message;
                StackTrace = stackTrace;
                Type = type;
                Timestamp = DateTime.UtcNow;
                RepeatCount = 1;
            }
        }

        private static readonly List<LogEntry> s_entries = new();
        private static readonly object s_lock = new();
        private const int MaxEntries = 1000;

        static ConsoleLogBuffer()
        {
            Application.logMessageReceived += OnLogMessage;
        }

        private static void OnLogMessage(
            string message, string stackTrace, LogType type)
        {
            lock (s_lock)
            {
                // Dedup: if the last entry has the same message and type,
                // increment its repeat count instead of adding a new entry.
                if (s_entries.Count > 0)
                {
                    var last = s_entries[s_entries.Count - 1];
                    if (last.Message == message && last.Type == type)
                    {
                        last.RepeatCount++;
                        last.Timestamp = DateTime.UtcNow;
                        return;
                    }
                }

                if (s_entries.Count >= MaxEntries)
                    s_entries.RemoveAt(0);
                s_entries.Add(new LogEntry(message, stackTrace, type));
            }
        }

        /// <summary>
        /// Query log entries with filtering options.
        /// </summary>
        /// <param name="count">Max entries to return (most recent first).</param>
        /// <param name="typeFilter">
        /// Filter by log type: "error", "warning", "log", "exception", or null for all.
        /// </param>
        /// <param name="grep">
        /// Regex or substring filter on message text. Null for no filter.
        /// </param>
        /// <param name="grepIsRegex">
        /// If true, treat grep as regex. If false, case-insensitive substring match.
        /// </param>
        public static List<LogEntry> Query(
            int count = 50,
            string typeFilter = null,
            string grep = null,
            bool grepIsRegex = false)
        {
            Regex grepRegex = null;
            if (grep != null && grepIsRegex)
            {
                try { grepRegex = new Regex(grep, RegexOptions.IgnoreCase); }
                catch { /* invalid regex — fall back to substring */ }
            }

            lock (s_lock)
            {
                var result = new List<LogEntry>();
                for (int i = s_entries.Count - 1;
                     i >= 0 && result.Count < count; i--)
                {
                    var entry = s_entries[i];

                    // Type filter
                    if (typeFilter != null && !MatchesType(entry.Type, typeFilter))
                        continue;

                    // Grep filter
                    if (grep != null)
                    {
                        if (grepRegex != null)
                        {
                            if (!grepRegex.IsMatch(entry.Message))
                                continue;
                        }
                        else
                        {
                            if (entry.Message.IndexOf(grep,
                                StringComparison.OrdinalIgnoreCase) < 0)
                                continue;
                        }
                    }

                    result.Add(entry);
                }
                return result;
            }
        }

        /// <summary>
        /// Get a rollup summary: counts by log type and top repeated messages.
        /// </summary>
        public static (int logs, int warnings, int errors, int exceptions,
            List<(string message, int count, LogType type)> topRepeated)
            GetSummary(int topN = 5)
        {
            lock (s_lock)
            {
                int logs = 0, warnings = 0, errors = 0, exceptions = 0;
                var messageCounts = new Dictionary<string, (int count, LogType type)>();

                foreach (var entry in s_entries)
                {
                    switch (entry.Type)
                    {
                        case LogType.Log: logs += entry.RepeatCount; break;
                        case LogType.Warning: warnings += entry.RepeatCount; break;
                        case LogType.Error: errors += entry.RepeatCount; break;
                        case LogType.Exception: exceptions += entry.RepeatCount; break;
                        case LogType.Assert: errors += entry.RepeatCount; break;
                    }

                    // Track unique messages for top-repeated
                    var key = entry.Message.Length > 100
                        ? entry.Message.Substring(0, 100) : entry.Message;
                    if (messageCounts.TryGetValue(key, out var existing))
                        messageCounts[key] = (existing.count + entry.RepeatCount,
                            entry.Type);
                    else
                        messageCounts[key] = (entry.RepeatCount, entry.Type);
                }

                // Sort by count descending, take topN
                var sorted = messageCounts
                    .Select(kv => (message: kv.Key, count: kv.Value.count, type: kv.Value.type))
                    .OrderByDescending(x => x.count)
                    .Take(topN)
                    .ToList();

                return (logs, warnings, errors, exceptions, sorted);
            }
        }

        /// <summary>Total unique entries in buffer (not counting repeats).</summary>
        public static int Count
        {
            get { lock (s_lock) return s_entries.Count; }
        }

        /// <summary>Clear all entries.</summary>
        public static void Clear()
        {
            lock (s_lock) s_entries.Clear();
        }

        private static bool MatchesType(LogType type, string filter)
        {
            return filter switch
            {
                "error" => type == LogType.Error || type == LogType.Assert,
                "warning" => type == LogType.Warning,
                "log" => type == LogType.Log,
                "exception" => type == LogType.Exception,
                _ => true
            };
        }
    }
}
