using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor.Tools.Spatial
{
    /// <summary>
    /// spatial_query:nearest — find the N closest objects to a point.
    /// Uses the spatial index (works in both edit and play mode).
    /// </summary>
    internal static class SpatialQueryNearest
    {
        internal static string Execute(JObject args)
        {
            var error = JsonParamParser.RequireVector3(args, "origin", out var origin);
            if (error != null) return error;

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
            var results = index.Nearest(origin, count, maxDistance, filter);

            return SpatialResultBuilder.BuildBudgetedResponse(
                "nearest", results, budgetTokens,
                r => { r["origin"] = ResponseHelpers.ToJArray(origin); },
                "Reduce count or increase budget to see more results");
        }
    }
}
