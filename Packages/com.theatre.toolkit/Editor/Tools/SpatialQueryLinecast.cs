using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor
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
            var from = SpatialQueryNearest.ParseVector3(args, "from");
            var to = SpatialQueryNearest.ParseVector3(args, "to");

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
            var from2D = SpatialQueryNearest.ParseVector2(args, "from");
            var to2D = SpatialQueryNearest.ParseVector2(args, "to");

            // Allow Vector3 input, take XY
            if (!from2D.HasValue)
            {
                var f3 = SpatialQueryNearest.ParseVector3(args, "from");
                if (f3.HasValue) from2D = new Vector2(f3.Value.x, f3.Value.y);
            }
            if (!to2D.HasValue)
            {
                var t3 = SpatialQueryNearest.ParseVector3(args, "to");
                if (t3.HasValue) to2D = new Vector2(t3.Value.x, t3.Value.y);
            }

            if (!from2D.HasValue)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'from' parameter",
                    "Provide from as [x, y] or [x, y, z]");
            if (!to2D.HasValue)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'to' parameter",
                    "Provide to as [x, y] or [x, y, z]");

            var hit = Physics2D.Linecast(
                from2D.Value, to2D.Value, layerMask);

            bool blocked = hit.collider != null;

            return BuildResponse(
                blocked,
                new Vector3(from2D.Value.x, from2D.Value.y, 0),
                new Vector3(to2D.Value.x, to2D.Value.y, 0),
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
