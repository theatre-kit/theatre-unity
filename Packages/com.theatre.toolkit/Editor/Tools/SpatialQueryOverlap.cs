using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// spatial_query:overlap — physics overlap query.
    /// Finds all colliders intersecting a shape (sphere, box, capsule).
    /// Requires Play Mode.
    /// </summary>
    internal static class SpatialQueryOverlap
    {
        internal static string Execute(JObject args)
        {
            var shape = args["shape"]?.Value<string>();
            if (string.IsNullOrEmpty(shape))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'shape' parameter",
                    "Provide shape: 'sphere', 'circle', 'box', or 'capsule'");
            }

            var physicsMode = PhysicsMode.GetEffective(
                args["physics"]?.Value<string>());
            int layerMask = args["layer_mask"]?.Value<int>()
                ?? Physics.DefaultRaycastLayers;

            if (physicsMode == "2d")
                return Execute2D(args, shape, layerMask);
            else
                return Execute3D(args, shape, layerMask);
        }

        private static string Execute3D(
            JObject args, string shape, int layerMask)
        {
            var center = SpatialQueryNearest.ParseVector3(args, "center");
            if (!center.HasValue)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'center' parameter",
                    "Provide center as [x, y, z] array");
            }

            Collider[] colliders;

            switch (shape)
            {
                case "sphere":
                {
                    float radius = ParseRadius(args);
                    if (radius <= 0f)
                        return RadiusError();
                    colliders = Physics.OverlapSphere(
                        center.Value, radius, layerMask);
                    break;
                }
                case "box":
                {
                    var halfExtents = SpatialQueryNearest.ParseVector3(
                        args, "size");
                    if (!halfExtents.HasValue)
                    {
                        return ResponseHelpers.ErrorResponse(
                            "invalid_parameter",
                            "Missing 'size' (half-extents) for box overlap",
                            "Provide size as [x, y, z] half-extents array");
                    }
                    colliders = Physics.OverlapBox(
                        center.Value, halfExtents.Value,
                        Quaternion.identity, layerMask);
                    break;
                }
                case "capsule":
                {
                    var size = SpatialQueryNearest.ParseVector3(args, "size");
                    if (!size.HasValue)
                    {
                        return ResponseHelpers.ErrorResponse(
                            "invalid_parameter",
                            "Missing 'size' for capsule overlap",
                            "Provide size as [radius, height, 0] array");
                    }
                    float capsuleRadius = size.Value.x;
                    float height = size.Value.y;
                    var point0 = center.Value
                        + Vector3.up * (height * 0.5f - capsuleRadius);
                    var point1 = center.Value
                        - Vector3.up * (height * 0.5f - capsuleRadius);
                    colliders = Physics.OverlapCapsule(
                        point0, point1, capsuleRadius, layerMask);
                    break;
                }
                default:
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Invalid 3D shape '{shape}'",
                        "Valid 3D shapes: 'sphere', 'box', 'capsule'");
            }

            return BuildOverlapResponse(colliders, null, shape);
        }

        private static string Execute2D(
            JObject args, string shape, int layerMask)
        {
            var center2D = SpatialQueryNearest.ParseVector2(args, "center");
            if (!center2D.HasValue)
            {
                // Try parsing as Vector3 and take XY
                var center3D = SpatialQueryNearest.ParseVector3(args, "center");
                if (center3D.HasValue)
                    center2D = new Vector2(center3D.Value.x, center3D.Value.y);
                else
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        "Missing or invalid 'center' parameter",
                        "Provide center as [x, y] or [x, y, z] array");
            }

            Collider2D[] colliders;

            switch (shape)
            {
                case "circle":
                case "sphere":
                {
                    float radius = ParseRadius(args);
                    if (radius <= 0f) return RadiusError();
                    colliders = Physics2D.OverlapCircleAll(
                        center2D.Value, radius, layerMask);
                    break;
                }
                case "box":
                {
                    var size2D = SpatialQueryNearest.ParseVector2(args, "size");
                    if (!size2D.HasValue)
                    {
                        var size3D = SpatialQueryNearest.ParseVector3(
                            args, "size");
                        if (size3D.HasValue)
                            size2D = new Vector2(
                                size3D.Value.x, size3D.Value.y);
                    }
                    if (!size2D.HasValue)
                        return ResponseHelpers.ErrorResponse(
                            "invalid_parameter",
                            "Missing 'size' for box overlap",
                            "Provide size as [x, y] half-extents");
                    colliders = Physics2D.OverlapBoxAll(
                        center2D.Value, size2D.Value * 2f, 0f, layerMask);
                    break;
                }
                case "capsule":
                {
                    var size2D = SpatialQueryNearest.ParseVector2(args, "size");
                    if (!size2D.HasValue)
                        return ResponseHelpers.ErrorResponse(
                            "invalid_parameter",
                            "Missing 'size' for capsule overlap",
                            "Provide size as [width, height]");
                    colliders = Physics2D.OverlapCapsuleAll(
                        center2D.Value, size2D.Value,
                        CapsuleDirection2D.Vertical, 0f, layerMask);
                    break;
                }
                default:
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Invalid 2D shape '{shape}'",
                        "Valid 2D shapes: 'circle', 'box', 'capsule'");
            }

            return BuildOverlapResponse(null, colliders, shape);
        }

        private static string BuildOverlapResponse(
            Collider[] colliders3D, Collider2D[] colliders2D, string shape)
        {
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "overlap";
            response["shape"] = shape;

            var resultsArray = new JArray();

            if (colliders3D != null)
            {
                foreach (var col in colliders3D)
                {
                    if (col == null) continue;
                    resultsArray.Add(BuildColliderEntry(
                        col.gameObject, col.GetType().Name));
                }
            }

            if (colliders2D != null)
            {
                foreach (var col in colliders2D)
                {
                    if (col == null) continue;
                    resultsArray.Add(BuildColliderEntry(
                        col.gameObject, col.GetType().Name));
                }
            }

            response["results"] = resultsArray;
            response["returned"] = resultsArray.Count;

            return response.ToString(Formatting.None);
        }

        internal static JObject BuildColliderEntry(
            GameObject go, string colliderType)
        {
            var entry = new JObject();
            entry["path"] = ResponseHelpers.GetHierarchyPath(go.transform);
            #pragma warning disable CS0618
            entry["instance_id"] = go.GetInstanceID();
            #pragma warning restore CS0618
            entry["tag"] = go.tag;
            entry["layer"] = LayerMask.LayerToName(go.layer);
            entry["collider_type"] = colliderType;
            return entry;
        }

        private static float ParseRadius(JObject args)
        {
            // Try size array first (for sphere/circle: [radius])
            var sizeToken = args["size"];
            if (sizeToken != null && sizeToken.Type == JTokenType.Array)
            {
                var arr = (JArray)sizeToken;
                if (arr.Count >= 1)
                    return arr[0].Value<float>();
            }
            // Fall back to radius field
            return args["radius"]?.Value<float>() ?? 0f;
        }

        private static string RadiusError()
        {
            return ResponseHelpers.ErrorResponse(
                "invalid_parameter",
                "Missing or invalid radius for sphere/circle overlap",
                "Provide size as [radius] or set 'radius' parameter");
        }
    }
}
