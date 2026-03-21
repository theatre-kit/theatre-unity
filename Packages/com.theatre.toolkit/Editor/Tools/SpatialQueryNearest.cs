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
            var origin = JsonParamParser.ParseVector3(args, "origin");
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
            var includeComponents = JsonParamParser.ParseStringArray(args, "include_components");
            var excludeTags = JsonParamParser.ParseStringArray(args, "exclude_tags");

            // Build filter predicate
            var filter = SpatialEntryFilter.Build(includeComponents, excludeTags);

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

            var (resultsArray, returned, truncated) =
                SpatialResultBuilder.BuildResultsArray(results, budget);

            response["results"] = resultsArray;
            response["returned"] = returned;
            response["budget"] = budget.ToBudgetJObject(
                truncated: truncated,
                reason: truncated ? "budget" : null,
                suggestion: truncated
                    ? "Reduce count or increase budget to see more results"
                    : null);

            return response.ToString(Formatting.None);
        }
    }
}
