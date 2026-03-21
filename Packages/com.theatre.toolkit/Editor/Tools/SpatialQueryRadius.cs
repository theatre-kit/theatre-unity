using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// spatial_query:radius — find all objects within a radius of a point.
    /// Uses the spatial index (works in both edit and play mode).
    /// </summary>
    internal static class SpatialQueryRadius
    {
        internal static string Execute(JObject args)
        {
            var origin = SpatialQueryNearest.ParseVector3(args, "origin");
            if (!origin.HasValue)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'origin' parameter",
                    "Provide origin as [x, y, z] array");
            }

            var radiusVal = args["radius"]?.Value<float>();
            if (!radiusVal.HasValue || radiusVal.Value <= 0f)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'radius' parameter",
                    "Provide a positive radius value");
            }

            string sortBy = args["sort_by"]?.Value<string>() ?? "distance";
            int budgetTokens = args["budget"]?.Value<int>()
                ?? TokenBudget.DefaultBudget;

            var includeComponents = SpatialQueryNearest.ParseStringArray(
                args, "include_components");
            var excludeTags = SpatialQueryNearest.ParseStringArray(
                args, "exclude_tags");

            // Build filter
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

            var index = SpatialQueryTool.GetIndex();
            var results = index.Radius(
                origin.Value, radiusVal.Value, filter, sortBy);

            // Build response
            var budget = new TokenBudget(budgetTokens);
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "radius";
            response["origin"] = ResponseHelpers.ToJArray(origin.Value);
            response["radius"] = Math.Round(radiusVal.Value, 2);

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

                var json = entryObj.ToString(Formatting.None);
                if (budget.WouldExceed(json.Length))
                    break;

                resultsArray.Add(entryObj);
                budget.Add(json.Length);
                returned++;
            }

            response["results"] = resultsArray;
            response["total"] = results.Count;
            response["returned"] = returned;

            bool truncated = returned < results.Count;
            response["budget"] = budget.ToBudgetJObject(
                truncated: truncated,
                reason: truncated ? "budget" : null,
                suggestion: truncated
                    ? "Reduce radius or increase budget"
                    : null);

            return response.ToString(Formatting.None);
        }
    }
}
