using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Theatre.Editor.Tools.Scene
{
    /// <summary>
    /// MCP tool: scene_snapshot
    /// Returns a token-budgeted overview of GameObjects with positions,
    /// organized by proximity to a focus point, with clustering summaries.
    /// </summary>
    public static class SceneSnapshotTool
    {
        private static readonly JToken s_inputSchema;

        static SceneSnapshotTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""focus"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""minItems"": 3,
                        ""maxItems"": 3,
                        ""description"": ""Center point [x, y, z] for spatial organization. Defaults to main camera position.""
                    },
                    ""radius"": {
                        ""type"": ""number"",
                        ""description"": ""Limit to objects within this radius of focus.""
                    },
                    ""include_components"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Filter: only include objects with these component types.""
                    },
                    ""exclude_inactive"": {
                        ""type"": ""boolean"",
                        ""default"": true,
                        ""description"": ""Skip disabled GameObjects.""
                    },
                    ""max_depth"": {
                        ""type"": ""integer"",
                        ""default"": 3,
                        ""minimum"": 0,
                        ""maximum"": 20,
                        ""description"": ""Hierarchy depth limit for nested objects.""
                    },
                    ""budget"": {
                        ""type"": ""integer"",
                        ""default"": 1500,
                        ""minimum"": 100,
                        ""maximum"": 4000,
                        ""description"": ""Target response size in tokens.""
                    },
                    ""scene"": {
                        ""type"": ""string"",
                        ""description"": ""Specific scene name to snapshot. Omit for all loaded scenes.""
                    }
                },
                ""required"": []
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "scene_snapshot",
                description: "Budgeted spatial overview of GameObjects in the "
                    + "scene with positions, component lists, and clustering "
                    + "summaries. Use this for initial scene understanding.",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageGameObject,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = true
                }
            ));
        }

        private static string Execute(JToken arguments)
        {
            // Parse parameters
            Vector3? focus = null;
            float? radius = null;
            string[] includeComponents = null;
            bool excludeInactive = true;
            int maxDepth = 3;
            int budgetTokens = TokenBudget.DefaultBudget;
            string sceneName = null;

            if (arguments != null && arguments.Type == JTokenType.Object)
            {
                var args = (JObject)arguments;

                focus = JsonParamParser.ParseVector3(args, "focus");

                if (args["radius"] != null)
                    radius = args["radius"].Value<float>();

                includeComponents = JsonParamParser.ParseStringArray(
                    args, "include_components");

                if (args["exclude_inactive"] != null)
                    excludeInactive = args["exclude_inactive"].Value<bool>();

                if (args["max_depth"] != null)
                    maxDepth = args["max_depth"].Value<int>();

                if (args["budget"] != null)
                    budgetTokens = args["budget"].Value<int>();

                if (args["scene"] != null)
                    sceneName = args["scene"].Value<string>();
            }

            // Default focus: main camera position
            if (!focus.HasValue)
            {
                var cam = Camera.main;
                focus = cam != null ? cam.transform.position : Vector3.zero;
            }

            // Determine scene scope
            var roots = new List<Transform>();
            if (sceneName != null)
            {
                var scene = SceneManager.GetSceneByName(sceneName);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    return ResponseHelpers.ErrorResponse(
                        "scene_not_loaded",
                        $"Scene '{sceneName}' is not loaded",
                        "Use scene_hierarchy to see loaded scenes");
                }
                roots.AddRange(scene.GetRootGameObjects().Select(go => go.transform));
            }
            else
            {
                roots.AddRange(ObjectResolver.GetAllRoots().Select(go => go.transform));
            }

            // Walk hierarchy
            var entries = HierarchyWalker.Walk(
                roots, maxDepth, excludeInactive, focus, radius);

            // Apply component filter
            if (includeComponents != null)
            {
                entries = FilterByComponents(entries, includeComponents);
            }

            // Sort by distance from focus
            entries.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Compute clusters
            var clusters = Clustering.Compute(entries);

            // Build budgeted response
            var budget = new TokenBudget(budgetTokens);

            var response = new JObject();

            // Scene info
            response["scene"] = sceneName ?? SceneManager.GetActiveScene().name;
            ResponseHelpers.AddFrameContext(response);
            ResponseHelpers.AddEditingContext(response);
            response["focus"] = ResponseHelpers.ToJArray(focus.Value);

            // Summary
            var summary = new JObject();
            summary["total_objects"] = entries.Count;

            // Write clusters
            if (clusters.Count > 0)
            {
                var groups = new JArray();
                foreach (var cluster in clusters)
                {
                    var clusterObj = new JObject();
                    clusterObj["label"] = cluster.Label;
                    clusterObj["center"] = ResponseHelpers.ToJArray(cluster.Center);
                    clusterObj["spread"] = Math.Round(cluster.Spread, 2);
                    clusterObj["count"] = cluster.Count;
                    groups.Add(clusterObj);
                }
                summary["groups"] = groups;
            }

            response["summary"] = summary;

            // Objects (budgeted)
            var objectsArray = new JArray();
            int returned = 0;

            foreach (var entry in entries)
            {
                if (budget.IsExhausted) break;

                var estimatedCost = TokenBudget.EstimateEntryTokens(entry);
                if (budget.WouldExceed(estimatedCost * 4))
                    break;

                var entryObj = new JObject();
                entryObj["path"] = entry.Path;
                entryObj["instance_id"] = entry.InstanceId;
                entryObj["position"] = ResponseHelpers.ToJArray(entry.Position);
                entryObj["active"] = entry.Active;
                entryObj["children_count"] = entry.ChildrenCount;
                entryObj["distance"] = Math.Round(entry.Distance, 2);

                // Components list
                if (entry.Components != null && entry.Components.Length > 0)
                {
                    var comps = new JArray();
                    foreach (var comp in entry.Components)
                    {
                        if (comp != "Transform") // Always present, skip noise
                            comps.Add(comp);
                    }
                    if (comps.Count > 0)
                        entryObj["components"] = comps;
                }

                objectsArray.Add(entryObj);
                returned++;
                budget.Add(estimatedCost * 4);
            }

            response["objects"] = objectsArray;
            response["returned"] = returned;

            // Budget info
            response["budget"] = budget.ToBudgetJObject(
                truncated: returned < entries.Count,
                reason: returned < entries.Count ? "budget" : null,
                suggestion: returned < entries.Count
                    ? "Increase budget or use radius/include_components to narrow scope"
                    : null);

            return response.ToString(Formatting.None);
        }

        private static List<HierarchyEntry> FilterByComponents(
            List<HierarchyEntry> entries,
            string[] requiredComponents)
        {
            return entries.Where(entry =>
                entry.Components != null &&
                requiredComponents.All(required =>
                    entry.Components.Any(comp =>
                        string.Equals(comp, required, StringComparison.OrdinalIgnoreCase))))
                .ToList();
        }
    }
}
