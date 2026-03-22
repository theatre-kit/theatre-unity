using System;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor.Tools.Spatial
{
    /// <summary>
    /// spatial_query:radius — find all objects within a radius of a point.
    /// Uses the spatial index (works in both edit and play mode).
    /// </summary>
    internal static class SpatialQueryRadius
    {
        internal static string Execute(JObject args)
        {
            var error = JsonParamParser.RequireVector3(args, "origin", out var origin);
            if (error != null) return error;

            error = JsonParamParser.RequirePositiveFloat(args, "radius", out var radius);
            if (error != null) return error;

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
            var results = index.Radius(origin, radius, filter, sortBy);

            return SpatialResultBuilder.BuildBudgetedResponse(
                "radius", results, budgetTokens,
                r =>
                {
                    r["origin"] = ResponseHelpers.ToJArray(origin);
                    r["radius"] = Math.Round(radius, 2);
                },
                "Reduce radius or increase budget");
        }
    }
}
