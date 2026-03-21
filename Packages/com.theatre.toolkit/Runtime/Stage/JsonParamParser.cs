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
    }
}
