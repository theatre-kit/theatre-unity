using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// spatial_query:nearest — find the N closest objects to a point.
    /// Uses the spatial index (works in both edit and play mode).
    /// </summary>
    internal static class SpatialQueryNearest
    {
        internal static string Execute(JObject args)
        {
            // Parse origin (required)
            var origin = ParseVector3(args, "origin");
            if (!origin.HasValue)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'origin' parameter",
                    "Provide origin as [x, y, z] array");
            }

            int count = args["count"]?.Value<int>() ?? 5;
            float maxDistance = args["max_distance"]?.Value<float>() ?? 0f;
            int budgetTokens = args["budget"]?.Value<int>()
                ?? TokenBudget.DefaultBudget;

            // Parse filters
            var includeComponents = ParseStringArray(args, "include_components");
            var excludeTags = ParseStringArray(args, "exclude_tags");

            // Build filter predicate
            Func<SpatialEntry, bool> filter = null;
            if (includeComponents != null || excludeTags != null)
            {
                filter = entry =>
                {
                    if (excludeTags != null)
                    {
                        foreach (var tag in excludeTags)
                        {
                            if (string.Equals(entry.Tag, tag,
                                StringComparison.OrdinalIgnoreCase))
                                return false;
                        }
                    }
                    if (includeComponents != null)
                    {
                        foreach (var required in includeComponents)
                        {
                            bool found = false;
                            if (entry.Components != null)
                            {
                                foreach (var comp in entry.Components)
                                {
                                    if (string.Equals(comp, required,
                                        StringComparison.OrdinalIgnoreCase))
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                            }
                            if (!found) return false;
                        }
                    }
                    return true;
                };
            }

            // Query spatial index
            var index = SpatialQueryTool.GetIndex();
            var results = index.Nearest(
                origin.Value, count, maxDistance, filter);

            // Build response
            var budget = new TokenBudget(budgetTokens);
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "nearest";
            response["origin"] = ResponseHelpers.ToJArray(origin.Value);

            var resultsArray = new JArray();
            int returned = 0;

            foreach (var result in results)
            {
                if (budget.IsExhausted) break;

                var entryObj = new JObject();
                entryObj["path"] = result.Entry.Path;
                entryObj["instance_id"] = result.Entry.InstanceId;
                entryObj["name"] = result.Entry.Name;
                entryObj["position"] = ResponseHelpers.ToJArray(
                    result.Entry.Position);
                entryObj["distance"] = Math.Round(result.Distance, 2);

                if (result.Entry.Components != null
                    && result.Entry.Components.Length > 0)
                {
                    var comps = new JArray();
                    foreach (var c in result.Entry.Components)
                    {
                        if (c != "Transform")
                            comps.Add(c);
                    }
                    if (comps.Count > 0)
                        entryObj["components"] = comps;
                }

                // Estimate cost before adding
                var json = entryObj.ToString(Formatting.None);
                if (budget.WouldExceed(json.Length))
                    break;

                resultsArray.Add(entryObj);
                budget.Add(json.Length);
                returned++;
            }

            response["results"] = resultsArray;
            response["returned"] = returned;

            bool truncated = returned < results.Count;
            response["budget"] = budget.ToBudgetJObject(
                truncated: truncated,
                reason: truncated ? "budget" : null,
                suggestion: truncated
                    ? "Reduce count or increase budget to see more results"
                    : null);

            return response.ToString(Formatting.None);
        }

        // --- Shared parsing helpers (used by other operations too) ---

        internal static Vector3? ParseVector3(JObject args, string field)
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

        internal static Vector2? ParseVector2(JObject args, string field)
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

        internal static string[] ParseStringArray(JObject args, string field)
        {
            var token = args[field];
            if (token == null || token.Type != JTokenType.Array)
                return null;
            var list = new List<string>();
            foreach (var item in (JArray)token)
                list.Add(item.Value<string>());
            return list.Count > 0 ? list.ToArray() : null;
        }
    }
}
