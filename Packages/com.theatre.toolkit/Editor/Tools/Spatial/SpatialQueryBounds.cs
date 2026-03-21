using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor.Tools.Spatial
{
    /// <summary>
    /// spatial_query:bounds — get world-space bounding box of an object.
    /// Reads from Renderer, Collider, or combined bounds.
    /// Works in both edit and play mode.
    /// </summary>
    internal static class SpatialQueryBounds
    {
        internal static string Execute(JObject args)
        {
            var resolveError = ObjectResolver.ResolveFromArgs(args, out var go);
            if (resolveError != null) return resolveError;

            var source = args["source"]?.Value<string>() ?? "combined";

            Bounds? rendererBounds = null;
            Bounds? colliderBounds = null;

            // Gather renderer bounds
            if (source == "renderer" || source == "combined")
            {
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var combined = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                    {
                        combined.Encapsulate(renderers[i].bounds);
                    }
                    rendererBounds = combined;
                }
            }

            // Gather collider bounds
            if (source == "collider" || source == "combined")
            {
                colliderBounds = GetColliderBounds(go);
            }

            // Compute final bounds
            Bounds? finalBounds = null;
            if (source == "combined")
            {
                if (rendererBounds.HasValue && colliderBounds.HasValue)
                {
                    var b = rendererBounds.Value;
                    b.Encapsulate(colliderBounds.Value);
                    finalBounds = b;
                }
                else
                {
                    finalBounds = rendererBounds ?? colliderBounds;
                }
            }
            else if (source == "renderer")
            {
                finalBounds = rendererBounds;
            }
            else if (source == "collider")
            {
                finalBounds = colliderBounds;
            }

            if (!finalBounds.HasValue)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"No {source} bounds found on '{go.name}'",
                    source == "renderer"
                        ? "This object has no Renderer. Try source='collider' or 'combined'."
                        : source == "collider"
                            ? "This object has no Collider. Try source='renderer' or 'combined'."
                            : "This object has neither Renderer nor Collider.");
            }

            // Build response
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            response["operation"] = "bounds";

            var result = new JObject();
            ResponseHelpers.AddIdentity(result, go);
            result["source"] = source;
            result["center"] = ResponseHelpers.ToJArray(
                finalBounds.Value.center);
            result["size"] = ResponseHelpers.ToJArray(
                finalBounds.Value.size);
            result["min"] = ResponseHelpers.ToJArray(
                finalBounds.Value.min);
            result["max"] = ResponseHelpers.ToJArray(
                finalBounds.Value.max);
            result["extents"] = ResponseHelpers.ToJArray(
                finalBounds.Value.extents);

            response["result"] = result;
            return response.ToString(Formatting.None);
        }

        private static Bounds? GetColliderBounds(GameObject go)
        {
            Bounds? bounds = null;
            foreach (var col in go.GetComponentsInChildren<Collider>())
            {
                if (bounds == null)
                    bounds = col.bounds;
                else
                {
                    var b = bounds.Value;
                    b.Encapsulate(col.bounds);
                    bounds = b;
                }
            }
            foreach (var col in go.GetComponentsInChildren<Collider2D>())
            {
                if (bounds == null)
                    bounds = col.bounds;
                else
                {
                    var b = bounds.Value;
                    b.Encapsulate(col.bounds);
                    bounds = b;
                }
            }
            return bounds;
        }
    }
}
