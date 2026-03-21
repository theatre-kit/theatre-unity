using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// spatial_query:raycast — cast a ray and report hits.
    /// Supports single-hit and all-hits modes.
    /// Requires Play Mode.
    /// </summary>
    internal static class SpatialQueryRaycast
    {
        internal static string Execute(JObject args)
        {
            var physicsMode = PhysicsMode.GetEffective(
                args["physics"]?.Value<string>());
            float maxDistance = args["max_distance"]?.Value<float>() ?? 1000f;
            bool all = args["all"]?.Value<bool>() ?? false;
            int layerMask = args["layer_mask"]?.Value<int>()
                ?? Physics.DefaultRaycastLayers;

            if (physicsMode == "2d")
                return Execute2D(args, maxDistance, all, layerMask);
            else
                return Execute3D(args, maxDistance, all, layerMask);
        }

        private static string Execute3D(
            JObject args, float maxDistance, bool all, int layerMask)
        {
            var origin = JsonParamParser.ParseVector3(args, "origin");
            if (!origin.HasValue)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'origin' parameter",
                    "Provide origin as [x, y, z]");

            var direction = JsonParamParser.ParseVector3(args, "direction");
            if (!direction.HasValue)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'direction' parameter",
                    "Provide direction as [x, y, z] (will be normalized)");

            var dir = direction.Value.normalized;

            if (all)
            {
                var hits = Physics.RaycastAll(
                    origin.Value, dir, maxDistance, layerMask);
                System.Array.Sort(hits,
                    (a, b) => a.distance.CompareTo(b.distance));
                return BuildMultiHitResponse3D(hits, origin.Value, dir);
            }
            else
            {
                bool hit = Physics.Raycast(
                    origin.Value, dir, out RaycastHit hitInfo,
                    maxDistance, layerMask);
                return BuildSingleHitResponse3D(
                    hit, hitInfo, origin.Value, dir);
            }
        }

        private static string Execute2D(
            JObject args, float maxDistance, bool all, int layerMask)
        {
            var origin = JsonParamParser.ParseVector2(args, "origin");
            if (!origin.HasValue)
            {
                var origin3 = JsonParamParser.ParseVector3(args, "origin");
                if (origin3.HasValue)
                    origin = new Vector2(origin3.Value.x, origin3.Value.y);
                else
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        "Missing or invalid 'origin'",
                        "Provide origin as [x, y] or [x, y, z]");
            }

            var direction = JsonParamParser.ParseVector2(
                args, "direction");
            if (!direction.HasValue)
            {
                var dir3 = JsonParamParser.ParseVector3(
                    args, "direction");
                if (dir3.HasValue)
                    direction = new Vector2(dir3.Value.x, dir3.Value.y);
                else
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        "Missing or invalid 'direction'",
                        "Provide direction as [x, y] or [x, y, z]");
            }

            var dir = direction.Value.normalized;

            if (all)
            {
                var hits = Physics2D.RaycastAll(
                    origin.Value, dir, maxDistance, layerMask);
                return BuildMultiHitResponse2D(hits, origin.Value, dir);
            }
            else
            {
                var hit = Physics2D.Raycast(
                    origin.Value, dir, maxDistance, layerMask);
                return BuildSingleHitResponse2D(
                    hit, origin.Value, dir);
            }
        }

        private static string BuildSingleHitResponse3D(
            bool didHit, RaycastHit hitInfo, Vector3 origin, Vector3 dir)
        {
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "raycast";
            response["origin"] = ResponseHelpers.ToJArray(origin);
            response["direction"] = ResponseHelpers.ToJArray(dir);

            var result = new JObject();
            result["hit"] = didHit;

            if (didHit)
            {
                result["point"] = ResponseHelpers.ToJArray(hitInfo.point);
                result["normal"] = ResponseHelpers.ToJArray(hitInfo.normal);
                result["distance"] = Math.Round(hitInfo.distance, 2);
                result["collider"] = SpatialQueryOverlap.BuildColliderEntry(
                    hitInfo.collider.gameObject,
                    hitInfo.collider.GetType().Name);
            }

            response["result"] = result;
            return response.ToString(Formatting.None);
        }

        private static string BuildMultiHitResponse3D(
            RaycastHit[] hits, Vector3 origin, Vector3 dir)
        {
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "raycast";
            response["origin"] = ResponseHelpers.ToJArray(origin);
            response["direction"] = ResponseHelpers.ToJArray(dir);

            var resultsArray = new JArray();
            foreach (var hit in hits)
            {
                var hitObj = new JObject();
                hitObj["point"] = ResponseHelpers.ToJArray(hit.point);
                hitObj["normal"] = ResponseHelpers.ToJArray(hit.normal);
                hitObj["distance"] = Math.Round(hit.distance, 2);
                hitObj["collider"] = SpatialQueryOverlap.BuildColliderEntry(
                    hit.collider.gameObject,
                    hit.collider.GetType().Name);
                resultsArray.Add(hitObj);
            }

            response["results"] = resultsArray;
            response["returned"] = resultsArray.Count;
            return response.ToString(Formatting.None);
        }

        private static string BuildSingleHitResponse2D(
            RaycastHit2D hit, Vector2 origin, Vector2 dir)
        {
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "raycast";
            response["origin"] = ResponseHelpers.ToJArray(origin);
            response["direction"] = ResponseHelpers.ToJArray(dir);

            var result = new JObject();
            result["hit"] = hit.collider != null;

            if (hit.collider != null)
            {
                result["point"] = ResponseHelpers.ToJArray(hit.point);
                result["normal"] = ResponseHelpers.ToJArray(hit.normal);
                result["distance"] = Math.Round(hit.distance, 2);
                result["collider"] = SpatialQueryOverlap.BuildColliderEntry(
                    hit.collider.gameObject,
                    hit.collider.GetType().Name);
            }

            response["result"] = result;
            return response.ToString(Formatting.None);
        }

        private static string BuildMultiHitResponse2D(
            RaycastHit2D[] hits, Vector2 origin, Vector2 dir)
        {
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "raycast";
            response["origin"] = ResponseHelpers.ToJArray(origin);
            response["direction"] = ResponseHelpers.ToJArray(dir);

            var resultsArray = new JArray();
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                var hitObj = new JObject();
                hitObj["point"] = ResponseHelpers.ToJArray(hit.point);
                hitObj["normal"] = ResponseHelpers.ToJArray(hit.normal);
                hitObj["distance"] = Math.Round(hit.distance, 2);
                hitObj["collider"] = SpatialQueryOverlap.BuildColliderEntry(
                    hit.collider.gameObject,
                    hit.collider.GetType().Name);
                resultsArray.Add(hitObj);
            }

            response["results"] = resultsArray;
            response["returned"] = resultsArray.Count;
            return response.ToString(Formatting.None);
        }
    }
}
