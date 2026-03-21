using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
using UnityEngine.AI;

namespace Theatre.Editor
{
    /// <summary>
    /// spatial_query:path_distance — calculate NavMesh path distance.
    /// Works in both edit and play mode (NavMesh data is baked to an asset).
    /// Returns the path distance, or an error if no NavMesh is available.
    /// </summary>
    internal static class SpatialQueryPathDistance
    {
        private const float SampleRadius = 2.0f;

        internal static string Execute(JObject args)
        {
            var from = JsonParamParser.ParseVector3(args, "from");
            var to = JsonParamParser.ParseVector3(args, "to");

            if (!from.HasValue)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'from' parameter",
                    "Provide from as [x, y, z]");
            if (!to.HasValue)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'to' parameter",
                    "Provide to as [x, y, z]");

            int agentTypeId = args["agent_type_id"]?.Value<int>() ?? 0;

            // Verify NavMesh is available at start point
            if (!NavMesh.SamplePosition(
                from.Value, out NavMeshHit fromHit,
                SampleRadius, NavMesh.AllAreas))
            {
                return ResponseHelpers.ErrorResponse(
                    "navmesh_not_available",
                    $"No NavMesh found near start point "
                    + $"[{from.Value.x:F1}, {from.Value.y:F1}, "
                    + $"{from.Value.z:F1}]",
                    "Bake a NavMesh first: Window > AI > Navigation > Bake");
            }

            // Verify NavMesh at end point
            if (!NavMesh.SamplePosition(
                to.Value, out NavMeshHit toHit,
                SampleRadius, NavMesh.AllAreas))
            {
                return ResponseHelpers.ErrorResponse(
                    "navmesh_not_available",
                    $"No NavMesh found near end point "
                    + $"[{to.Value.x:F1}, {to.Value.y:F1}, "
                    + $"{to.Value.z:F1}]",
                    "Bake a NavMesh first: Window > AI > Navigation > Bake");
            }

            // Calculate path
            var path = new NavMeshPath();
            bool found = NavMesh.CalculatePath(
                fromHit.position, toHit.position,
                NavMesh.AllAreas, path);

            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "path_distance";

            var result = new JObject();
            result["from"] = ResponseHelpers.ToJArray(from.Value);
            result["to"] = ResponseHelpers.ToJArray(to.Value);
            result["straight_distance"] = Math.Round(
                Vector3.Distance(from.Value, to.Value), 2);

            if (found && path.status == NavMeshPathStatus.PathComplete)
            {
                float pathDist = CalculatePathLength(path);
                result["path_found"] = true;
                result["path_distance"] = Math.Round(pathDist, 2);
                result["path_status"] = "complete";

                // Include waypoints if path has corners
                if (path.corners.Length > 0)
                {
                    var waypoints = new JArray();
                    foreach (var corner in path.corners)
                    {
                        waypoints.Add(ResponseHelpers.ToJArray(corner));
                    }
                    result["waypoints"] = waypoints;
                    result["waypoint_count"] = path.corners.Length;
                }
            }
            else if (found
                && path.status == NavMeshPathStatus.PathPartial)
            {
                float pathDist = CalculatePathLength(path);
                result["path_found"] = true;
                result["path_distance"] = Math.Round(pathDist, 2);
                result["path_status"] = "partial";
            }
            else
            {
                result["path_found"] = false;
                result["path_status"] = "invalid";
            }

            response["result"] = result;
            return response.ToString(Formatting.None);
        }

        private static float CalculatePathLength(NavMeshPath path)
        {
            float length = 0f;
            var corners = path.corners;
            for (int i = 1; i < corners.Length; i++)
            {
                length += Vector3.Distance(corners[i - 1], corners[i]);
            }
            return length;
        }
    }
}
