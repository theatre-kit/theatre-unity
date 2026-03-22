using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor.Tools.Spatial
{
    /// <summary>
    /// spatial_query:linecast — test line-of-sight between two points.
    /// Returns whether the line is blocked and the first hit point.
    /// Requires Play Mode.
    /// </summary>
    internal static class SpatialQueryLinecast
    {
        internal static string Execute(JObject args)
        {
            var physicsMode = PhysicsMode.GetEffective(
                args["physics"]?.Value<string>());
            int layerMask = args["layer_mask"]?.Value<int>()
                ?? Physics.DefaultRaycastLayers;

            if (physicsMode == "2d")
                return Execute2D(args, layerMask);
            else
                return Execute3D(args, layerMask);
        }

        private static string Execute3D(JObject args, int layerMask)
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

            bool blocked = Physics.Linecast(
                from.Value, to.Value, out RaycastHit hitInfo, layerMask);

            return BuildResponse(
                blocked, from.Value, to.Value,
                blocked ? (Vector3?)hitInfo.point : null,
                blocked ? (Vector3?)hitInfo.normal : null,
                blocked ? (float?)hitInfo.distance : null,
                blocked ? hitInfo.collider?.gameObject : null,
                blocked ? hitInfo.collider?.GetType().Name : null);
        }

        private static string Execute2D(JObject args, int layerMask)
        {
            // Allow Vector3 input, take XY
            var error = JsonParamParser.RequireVector2WithFallback(
                args, "from", out var from2D,
                "Provide from as [x, y] or [x, y, z]");
            if (error != null) return error;
            error = JsonParamParser.RequireVector2WithFallback(
                args, "to", out var to2D,
                "Provide to as [x, y] or [x, y, z]");
            if (error != null) return error;

            var hit = Physics2D.Linecast(from2D, to2D, layerMask);

            bool blocked = hit.collider != null;

            return BuildResponse(
                blocked,
                new Vector3(from2D.x, from2D.y, 0),
                new Vector3(to2D.x, to2D.y, 0),
                blocked ? (Vector3?)new Vector3(
                    hit.point.x, hit.point.y, 0) : null,
                blocked ? (Vector3?)new Vector3(
                    hit.normal.x, hit.normal.y, 0) : null,
                blocked ? (float?)hit.distance : null,
                blocked ? hit.collider?.gameObject : null,
                blocked ? hit.collider?.GetType().Name : null);
        }

        private static string BuildResponse(
            bool blocked, Vector3 from, Vector3 to,
            Vector3? hitPoint, Vector3? hitNormal, float? hitDistance,
            GameObject hitObject, string colliderType)
        {
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "linecast";

            var result = new JObject();
            result["blocked"] = blocked;
            result["from"] = ResponseHelpers.ToJArray(from);
            result["to"] = ResponseHelpers.ToJArray(to);
            result["distance"] = Math.Round(
                Vector3.Distance(from, to), 2);

            if (blocked)
            {
                result["hit_point"] = ResponseHelpers.ToJArray(
                    hitPoint.Value);
                result["hit_normal"] = ResponseHelpers.ToJArray(
                    hitNormal.Value);
                result["hit_distance"] = Math.Round(hitDistance.Value, 2);
                if (hitObject != null)
                {
                    result["collider"] = SpatialQueryOverlap
                        .BuildColliderEntry(hitObject, colliderType);
                }
            }

            response["result"] = result;
            return response.ToString(Formatting.None);
        }
    }
}
