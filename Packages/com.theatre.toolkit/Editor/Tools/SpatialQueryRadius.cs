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
            var origin = JsonParamParser.ParseVector3(args, "origin");
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

            var includeComponents = JsonParamParser.ParseStringArray(
                args, "include_components");
            var excludeTags = JsonParamParser.ParseStringArray(
                args, "exclude_tags");

            // Build filter
            var filter = SpatialEntryFilter.Build(includeComponents, excludeTags);

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

            var (resultsArray, returned, truncated) =
                SpatialResultBuilder.BuildResultsArray(results, budget);

            response["results"] = resultsArray;
            response["total"] = results.Count;
            response["returned"] = returned;
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
