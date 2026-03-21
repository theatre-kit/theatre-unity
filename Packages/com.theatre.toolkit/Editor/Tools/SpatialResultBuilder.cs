using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;

namespace Theatre.Editor
{
    /// <summary>
    /// Shared builder for spatial query result arrays with token budgeting.
    /// Centralizes the entry-object construction and budget-tracking loop
    /// used by nearest and radius query handlers.
    /// </summary>
    internal static class SpatialResultBuilder
    {
        /// <summary>
        /// Build a budgeted JArray of spatial result entry objects.
        /// </summary>
        /// <param name="results">Ordered list of spatial results to render.</param>
        /// <param name="budget">Token budget that controls how many entries are included.</param>
        /// <returns>
        /// A tuple of: the populated result array, the count of entries returned,
        /// and a flag indicating whether results were truncated due to budget.
        /// </returns>
        public static (JArray results, int returned, bool truncated) BuildResultsArray(
            IReadOnlyList<SpatialResult> results,
            TokenBudget budget)
        {
            var arr = new JArray();
            int returned = 0;

            foreach (var result in results)
            {
                if (budget.IsExhausted) break;

                var entryObj = new JObject();
                entryObj["path"] = result.Entry.Path;
                entryObj["instance_id"] = result.Entry.InstanceId;
                entryObj["name"] = result.Entry.Name;
                entryObj["position"] = ResponseHelpers.ToJArray(result.Entry.Position);
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

                arr.Add(entryObj);
                budget.Add(json.Length);
                returned++;
            }

            bool truncated = returned < results.Count;
            return (arr, returned, truncated);
        }
    }
}
