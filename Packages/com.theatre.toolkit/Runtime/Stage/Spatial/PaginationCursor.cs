using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.SceneManagement;

namespace Theatre.Stage
{
    /// <summary>
    /// Opaque pagination cursor. Encoded as base64 JSON.
    /// Cursors expire after 60 seconds or when the active scene changes.
    /// </summary>
    public sealed class PaginationCursor
    {
        /// <summary>Time-to-live in seconds.</summary>
        public const int TtlSeconds = 60;

        /// <summary>Tool name this cursor belongs to.</summary>
        public string Tool { get; set; }

        /// <summary>Operation within the tool (e.g., "list", "find").</summary>
        public string Operation { get; set; }

        /// <summary>Offset into the result set.</summary>
        public int Offset { get; set; }

        /// <summary>Creation timestamp (Unix seconds).</summary>
        public long Timestamp { get; set; }

        /// <summary>Scene name at creation time (for expiry detection).</summary>
        public string SceneName { get; set; }

        /// <summary>
        /// Encode this cursor as an opaque base64 string.
        /// </summary>
        public string Encode()
        {
            var obj = new JObject();
            obj["tool"] = Tool;
            obj["op"] = Operation;
            obj["offset"] = Offset;
            obj["ts"] = Timestamp;
            obj["scene"] = SceneName;
            var json = obj.ToString(Formatting.None);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        /// Decode a cursor string. Returns null if the cursor is invalid
        /// or expired.
        /// </summary>
        /// <param name="encoded">Base64-encoded cursor string.</param>
        /// <param name="currentScene">Current active scene name for expiry check.</param>
        /// <returns>Decoded cursor, or null if invalid/expired.</returns>
        public static PaginationCursor Decode(string encoded, string currentScene)
        {
            if (string.IsNullOrEmpty(encoded)) return null;

            try
            {
                var bytes = Convert.FromBase64String(encoded);
                var json = Encoding.UTF8.GetString(bytes);
                var root = JToken.Parse(json) as JObject;
                if (root == null) return null;

                var cursor = new PaginationCursor
                {
                    Tool = root["tool"]?.Value<string>(),
                    Operation = root["op"]?.Value<string>(),
                    Offset = root["offset"]?.Value<int>() ?? 0,
                    Timestamp = root["ts"]?.Value<long>() ?? 0,
                    SceneName = root["scene"]?.Value<string>()
                };

                // Check expiry
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (now - cursor.Timestamp > TtlSeconds)
                    return null; // Expired

                // Check scene change
                if (cursor.SceneName != currentScene)
                    return null; // Scene changed

                return cursor;
            }
            catch
            {
                return null; // Invalid format
            }
        }

        /// <summary>
        /// Create a cursor for the next page.
        /// </summary>
        /// <param name="tool">Tool name.</param>
        /// <param name="operation">Operation name.</param>
        /// <param name="nextOffset">Offset for the next page.</param>
        /// <returns>Encoded cursor string.</returns>
        public static string Create(string tool, string operation, int nextOffset)
        {
            var cursor = new PaginationCursor
            {
                Tool = tool,
                Operation = operation,
                Offset = nextOffset,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                SceneName = SceneManager.GetActiveScene().name
            };
            return cursor.Encode();
        }

        /// <summary>
        /// Build a JObject containing pagination metadata.
        /// </summary>
        public static JObject BuildPaginationJObject(
            string cursorString,
            bool hasMore,
            int returned,
            int? total = null)
        {
            var obj = new JObject();
            if (cursorString != null)
                obj["cursor"] = cursorString;
            obj["has_more"] = hasMore;
            obj["returned"] = returned;
            if (total.HasValue)
                obj["total"] = total.Value;
            return obj;
        }
    }
}
