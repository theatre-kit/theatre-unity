using System.Collections.Generic;
using UnityEngine;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// Stores recent gizmo visualization requests with auto-fade.
    /// Each request has a type, geometry data, and creation time.
    /// Requests older than the fade duration are removed on access.
    /// </summary>
    public static class GizmoState
    {
        /// <summary>How long query gizmos persist (seconds).</summary>
        public static float FadeDuration = 3f;

        /// <summary>Global toggle for all gizmos.</summary>
        public static bool Enabled = true;

        /// <summary>Opacity multiplier (0-1).</summary>
        public static float Opacity = 0.7f;

        /// <summary>Types of gizmo visualizations.</summary>
        public enum GizmoType
        {
            Nearest,
            Radius,
            Overlap,
            Raycast,
            Linecast,
            Bounds,
            WatchProximity,
            WatchRegion,
            Teleport,
        }

        /// <summary>A single gizmo visualization request.</summary>
        public struct GizmoRequest
        {
            /// <summary>The type of gizmo to draw.</summary>
            public GizmoType Type;
            /// <summary>Time.realtimeSinceStartup when this was created.</summary>
            public float CreatedAt;
            /// <summary>Primary origin point.</summary>
            public Vector3 Origin;
            /// <summary>End point, direction, or secondary position.</summary>
            public Vector3 End;
            /// <summary>Radius for sphere/disc shapes.</summary>
            public float Radius;
            /// <summary>Size for box shapes.</summary>
            public Vector3 Size;
            /// <summary>Multi-point data (nearest results, path points, etc.).</summary>
            public Vector3[] Points;
            /// <summary>Whether a raycast/linecast hit something.</summary>
            public bool Hit;
            /// <summary>Base color (alpha is applied separately via GetAlpha).</summary>
            public Color Color;
        }

        private static readonly List<GizmoRequest> _requests = new();

        /// <summary>Active requests (auto-pruned on access).</summary>
        public static IReadOnlyList<GizmoRequest> Requests
        {
            get
            {
                Prune();
                return _requests;
            }
        }

        /// <summary>Add a new gizmo request. Sets CreatedAt to current time.</summary>
        public static void Add(GizmoRequest r)
        {
            r.CreatedAt = Time.realtimeSinceStartup;
            _requests.Add(r);
        }

        /// <summary>Clear all requests immediately.</summary>
        public static void Clear()
        {
            _requests.Clear();
        }

        /// <summary>Remove expired requests.</summary>
        private static void Prune()
        {
            _requests.RemoveAll(r => Time.realtimeSinceStartup - r.CreatedAt > FadeDuration);
        }

        /// <summary>
        /// Get fade alpha for a request (1.0 fresh → 0.0 expired), multiplied by Opacity.
        /// </summary>
        public static float GetAlpha(GizmoRequest r)
        {
            return Mathf.Clamp01(1f - (Time.realtimeSinceStartup - r.CreatedAt) / FadeDuration) * Opacity;
        }
    }
}
