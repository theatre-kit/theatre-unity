using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// Extracts gizmo geometry from tool call args and results.
    /// Called after each spatial_query, watch, or action tool call.
    /// </summary>
    internal static class GizmoExtractor
    {
        /// <summary>
        /// Try to extract a gizmo from a tool call.
        /// Returns true if a gizmo was added to GizmoState.
        /// </summary>
        public static bool TryExtract(string toolName, JToken args, string resultJson)
        {
            if (!GizmoState.Enabled) return false;

            var argsObj = args as JObject;
            if (argsObj == null) return false;
            var operation = argsObj["operation"]?.Value<string>();

            switch (toolName)
            {
                case "spatial_query": return ExtractSpatialQuery(operation, argsObj, resultJson);
                case "action":        return ExtractAction(operation, argsObj, resultJson);
                case "watch":         return ExtractWatch(operation, argsObj, resultJson);
                default:              return false;
            }
        }

        // --- Spatial Query ---

        private static bool ExtractSpatialQuery(string operation, JObject args, string resultJson)
        {
            switch (operation)
            {
                case "nearest":  return ExtractNearest(args, resultJson);
                case "radius":   return ExtractRadius(args);
                case "overlap":  return ExtractOverlap(args);
                case "raycast":  return ExtractRaycast(args, resultJson);
                case "linecast": return ExtractLinecast(args, resultJson);
                case "bounds":   return ExtractBounds(resultJson);
                default:         return false;
            }
        }

        private static bool ExtractNearest(JObject args, string resultJson)
        {
            var origin = ParseVector3(args["origin"]);
            if (origin == null) return false;

            var points = new System.Collections.Generic.List<Vector3>();
            try
            {
                var result = JObject.Parse(resultJson);
                var results = result["results"] as JArray;
                if (results != null)
                {
                    foreach (var item in results)
                    {
                        var pos = ParseVector3(item["position"]);
                        if (pos.HasValue) points.Add(pos.Value);
                    }
                }
            }
            catch { /* malformed result — still add origin gizmo */ }

            GizmoState.Add(new GizmoState.GizmoRequest
            {
                Type   = GizmoState.GizmoType.Nearest,
                Origin = origin.Value,
                Points = points.ToArray(),
                Color  = Color.cyan,
            });
            SceneView.RepaintAll();
            return true;
        }

        private static bool ExtractRadius(JObject args)
        {
            var origin = ParseVector3(args["origin"]);
            if (origin == null) return false;
            var radius = args["radius"]?.Value<float>() ?? 5f;

            GizmoState.Add(new GizmoState.GizmoRequest
            {
                Type   = GizmoState.GizmoType.Radius,
                Origin = origin.Value,
                Radius = radius,
                Color  = Color.yellow,
            });
            SceneView.RepaintAll();
            return true;
        }

        private static bool ExtractOverlap(JObject args)
        {
            var center = ParseVector3(args["center"]);
            if (center == null) return false;
            var size = ParseVector3(args["size"]) ?? Vector3.one;

            GizmoState.Add(new GizmoState.GizmoRequest
            {
                Type   = GizmoState.GizmoType.Overlap,
                Origin = center.Value,
                Size   = size,
                Color  = Color.green,
            });
            SceneView.RepaintAll();
            return true;
        }

        private static bool ExtractRaycast(JObject args, string resultJson)
        {
            var origin    = ParseVector3(args["origin"]);
            var direction = ParseVector3(args["direction"]);
            if (origin == null || direction == null) return false;

            var maxDistance = args["max_distance"]?.Value<float>() ?? 100f;
            var end  = origin.Value + direction.Value.normalized * maxDistance;
            var hit  = false;

            try
            {
                var result = JObject.Parse(resultJson);
                var inner  = result["result"];
                hit = inner?["hit"]?.Value<bool>() ?? false;
                var point = ParseVector3(inner?["point"]);
                if (hit && point.HasValue) end = point.Value;
            }
            catch { /* use calculated end */ }

            GizmoState.Add(new GizmoState.GizmoRequest
            {
                Type   = GizmoState.GizmoType.Raycast,
                Origin = origin.Value,
                End    = end,
                Hit    = hit,
                Color  = Color.red,
            });
            SceneView.RepaintAll();
            return true;
        }

        private static bool ExtractLinecast(JObject args, string resultJson)
        {
            var from = ParseVector3(args["from"]);
            var to   = ParseVector3(args["to"]);
            if (from == null || to == null) return false;

            var blocked = false;
            try
            {
                var result  = JObject.Parse(resultJson);
                var inner   = result["result"];
                blocked = inner?["blocked"]?.Value<bool>() ?? false;
            }
            catch { /* no hit info */ }

            GizmoState.Add(new GizmoState.GizmoRequest
            {
                Type   = GizmoState.GizmoType.Linecast,
                Origin = from.Value,
                End    = to.Value,
                Hit    = blocked,
                Color  = blocked ? Color.red : Color.green,
            });
            SceneView.RepaintAll();
            return true;
        }

        private static bool ExtractBounds(string resultJson)
        {
            try
            {
                var result = JObject.Parse(resultJson);
                var inner  = result["result"];
                var center = ParseVector3(inner?["center"]);
                var size   = ParseVector3(inner?["size"]);
                if (center == null || size == null) return false;

                GizmoState.Add(new GizmoState.GizmoRequest
                {
                    Type   = GizmoState.GizmoType.Bounds,
                    Origin = center.Value,
                    Size   = size.Value,
                    Color  = Color.white,
                });
                SceneView.RepaintAll();
                return true;
            }
            catch { return false; }
        }

        // --- Action ---

        private static bool ExtractAction(string operation, JObject args, string resultJson)
        {
            if (operation != "teleport") return false;

            try
            {
                var result   = JObject.Parse(resultJson);
                var prevPos  = ParseVector3(result["previous_position"]);
                var newPos   = ParseVector3(result["position"]);
                if (prevPos == null || newPos == null) return false;

                GizmoState.Add(new GizmoState.GizmoRequest
                {
                    Type   = GizmoState.GizmoType.Teleport,
                    Origin = prevPos.Value,
                    End    = newPos.Value,
                    Color  = Color.magenta,
                });
                SceneView.RepaintAll();
                return true;
            }
            catch { return false; }
        }

        // --- Watch ---

        private static bool ExtractWatch(string operation, JObject args, string resultJson)
        {
            if (operation != "create") return false;

            try
            {
                var condition = args["condition"] as JObject;
                if (condition == null) return false;

                var conditionType = condition["type"]?.Value<string>();

                if (conditionType == "proximity" || conditionType == "distance")
                {
                    // proximity condition: origin from target object position in result, radius from within/beyond
                    var result = JObject.Parse(resultJson);
                    var origin = ParseVector3(result["position"]);
                    var within = condition["within"]?.Value<float>() ?? condition["beyond"]?.Value<float>() ?? 5f;
                    if (origin == null) return false;

                    GizmoState.Add(new GizmoState.GizmoRequest
                    {
                        Type   = GizmoState.GizmoType.WatchProximity,
                        Origin = origin.Value,
                        Radius = within,
                        Color  = new Color(1f, 0.5f, 0f), // orange
                    });
                    SceneView.RepaintAll();
                    return true;
                }

                if (conditionType == "region")
                {
                    var min = ParseVector3(condition["min"]);
                    var max = ParseVector3(condition["max"]);
                    if (min == null || max == null) return false;

                    GizmoState.Add(new GizmoState.GizmoRequest
                    {
                        Type   = GizmoState.GizmoType.WatchRegion,
                        Origin = min.Value,
                        End    = max.Value,
                        Color  = new Color(1f, 0.5f, 0f), // orange
                    });
                    SceneView.RepaintAll();
                    return true;
                }
            }
            catch { /* ignore malformed args */ }

            return false;
        }

        // --- Helpers ---

        private static Vector3? ParseVector3(JToken token)
        {
            if (token == null) return null;
            var arr = token as JArray;
            if (arr == null || arr.Count < 3) return null;
            try
            {
                return new Vector3(
                    arr[0].Value<float>(),
                    arr[1].Value<float>(),
                    arr[2].Value<float>());
            }
            catch { return null; }
        }
    }
}
