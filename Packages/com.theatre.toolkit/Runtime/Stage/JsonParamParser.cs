using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Theatre.Stage
{
    /// <summary>
    /// Shared JSON parameter parsing helpers for MCP tool handlers.
    /// Centralizes repetitive arg-parsing patterns so each tool handler
    /// doesn't need its own copy.
    /// </summary>
    public static class JsonParamParser
    {
        /// <summary>
        /// Parse a [x, y, z] JSON array from an args object as a Vector3.
        /// Returns null if the field is missing, not an array, or has fewer than 3 elements.
        /// </summary>
        public static Vector3? ParseVector3(JObject args, string field)
        {
            var token = args[field];
            if (token == null || token.Type != JTokenType.Array)
                return null;
            var arr = (JArray)token;
            if (arr.Count < 3) return null;
            return new Vector3(
                arr[0].Value<float>(),
                arr[1].Value<float>(),
                arr[2].Value<float>());
        }

        /// <summary>
        /// Parse a [x, y] JSON array from an args object as a Vector2.
        /// Returns null if the field is missing, not an array, or has fewer than 2 elements.
        /// </summary>
        public static Vector2? ParseVector2(JObject args, string field)
        {
            var token = args[field];
            if (token == null || token.Type != JTokenType.Array)
                return null;
            var arr = (JArray)token;
            if (arr.Count < 2) return null;
            return new Vector2(
                arr[0].Value<float>(),
                arr[1].Value<float>());
        }

        /// <summary>
        /// Parse a JSON string array from an args object.
        /// Returns null if the field is missing or not an array.
        /// Returns null (not an empty array) if the array is empty.
        /// </summary>
        public static string[] ParseStringArray(JObject args, string field)
        {
            var token = args[field];
            if (token == null || token.Type != JTokenType.Array)
                return null;
            var arr = ((JArray)token).Select(item => item.Value<string>()).ToArray();
            return arr.Length > 0 ? arr : null;
        }

        /// <summary>
        /// Parse a required Vector3 parameter. Returns null on success
        /// (value written to out param), or an error response string on failure.
        /// </summary>
        public static string RequireVector3(
            JObject args, string field, out Vector3 value,
            string suggestion = null)
        {
            var parsed = ParseVector3(args, field);
            if (!parsed.HasValue)
            {
                value = default;
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Missing or invalid '{field}' parameter",
                    suggestion ?? $"Provide {field} as [x, y, z] array");
            }
            value = parsed.Value;
            return null;
        }

        /// <summary>
        /// Parse a required Vector2 parameter. Returns null on success,
        /// or an error response string on failure.
        /// </summary>
        public static string RequireVector2(
            JObject args, string field, out Vector2 value,
            string suggestion = null)
        {
            var parsed = ParseVector2(args, field);
            if (!parsed.HasValue)
            {
                value = default;
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Missing or invalid '{field}' parameter",
                    suggestion ?? $"Provide {field} as [x, y] array");
            }
            value = parsed.Value;
            return null;
        }

        /// <summary>
        /// Parse a required positive float parameter. Returns null on success,
        /// or an error response string on failure.
        /// </summary>
        public static string RequirePositiveFloat(
            JObject args, string field, out float value,
            string suggestion = null)
        {
            var token = args[field];
            if (token == null)
            {
                value = default;
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Missing required '{field}' parameter",
                    suggestion ?? $"Provide a positive {field} value");
            }
            value = token.Value<float>();
            if (value <= 0f)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"'{field}' must be positive, got {value}",
                    suggestion ?? $"Provide a positive {field} value");
            }
            return null;
        }

        /// <summary>
        /// Parse a Vector2 parameter, accepting either [x, y] or [x, y, z] (drops z).
        /// Returns null on success, or an error response string on failure.
        /// </summary>
        public static string RequireVector2WithFallback(
            JObject args, string field, out Vector2 value,
            string suggestion = null)
        {
            var v2 = ParseVector2(args, field);
            if (v2.HasValue) { value = v2.Value; return null; }

            var v3 = ParseVector3(args, field);
            if (v3.HasValue) { value = new Vector2(v3.Value.x, v3.Value.y); return null; }

            value = default;
            return ResponseHelpers.ErrorResponse(
                "invalid_parameter",
                $"Missing or invalid '{field}' parameter",
                suggestion ?? $"Provide {field} as [x, y] or [x, y, z] array");
        }
    }
}
