using UnityEditor;
using UnityEngine;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// Draws Theatre gizmos in the Scene View via the Handles API.
    /// Registered automatically via SceneView.duringSceneGui on editor load.
    /// </summary>
    [InitializeOnLoad]
    public static class GizmoRenderer
    {
        static GizmoRenderer()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!GizmoState.Enabled) return;

            var requests = GizmoState.Requests;
            for (int i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                var alpha   = GizmoState.GetAlpha(request);
                if (alpha <= 0f) continue;

                var baseColor = request.Color;
                baseColor.a   = alpha;
                Handles.color = baseColor;

                switch (request.Type)
                {
                    case GizmoState.GizmoType.Nearest:
                        DrawNearest(request, baseColor, alpha);
                        break;
                    case GizmoState.GizmoType.Radius:
                        DrawRadius(request);
                        break;
                    case GizmoState.GizmoType.Overlap:
                        DrawOverlap(request);
                        break;
                    case GizmoState.GizmoType.Raycast:
                        DrawRaycast(request, baseColor, alpha);
                        break;
                    case GizmoState.GizmoType.Linecast:
                        DrawLinecast(request, baseColor, alpha);
                        break;
                    case GizmoState.GizmoType.Bounds:
                        DrawBounds(request);
                        break;
                    case GizmoState.GizmoType.WatchProximity:
                        DrawWatchProximity(request);
                        break;
                    case GizmoState.GizmoType.WatchRegion:
                        DrawWatchRegion(request);
                        break;
                    case GizmoState.GizmoType.Teleport:
                        DrawTeleport(request, baseColor, alpha);
                        break;
                }
            }
        }

        private static void DrawNearest(GizmoState.GizmoRequest r, Color baseColor, float alpha)
        {
            // Small marker disc at origin
            DrawWireSphere(r.Origin, 0.5f);

            // Lines to each result point
            if (r.Points != null)
            {
                foreach (var pt in r.Points)
                    Handles.DrawLine(r.Origin, pt);
            }
        }

        private static void DrawRadius(GizmoState.GizmoRequest r)
        {
            DrawWireSphere(r.Origin, r.Radius);
        }

        private static void DrawOverlap(GizmoState.GizmoRequest r)
        {
            Handles.DrawWireCube(r.Origin, r.Size);
        }

        private static void DrawRaycast(GizmoState.GizmoRequest r, Color baseColor, float alpha)
        {
            Handles.DrawLine(r.Origin, r.End);
            if (r.Hit)
            {
                var hitColor = baseColor;
                hitColor.a   = alpha;
                Handles.color = hitColor;
                Handles.SphereHandleCap(0, r.End, Quaternion.identity, 0.3f, EventType.Repaint);
            }
        }

        private static void DrawLinecast(GizmoState.GizmoRequest r, Color baseColor, float alpha)
        {
            Handles.DrawLine(r.Origin, r.End);
        }

        private static void DrawBounds(GizmoState.GizmoRequest r)
        {
            Handles.DrawWireCube(r.Origin, r.Size);
        }

        private static void DrawWatchProximity(GizmoState.GizmoRequest r)
        {
            // Dashed circles on three axes
            Handles.DrawWireDisc(r.Origin, Vector3.up, r.Radius);
            Handles.DrawWireDisc(r.Origin, Vector3.right, r.Radius);
            Handles.DrawWireDisc(r.Origin, Vector3.forward, r.Radius);
        }

        private static void DrawWatchRegion(GizmoState.GizmoRequest r)
        {
            var center = (r.Origin + r.End) * 0.5f;
            var size   = r.End - r.Origin;
            // Ensure positive extents
            size.x = Mathf.Abs(size.x);
            size.y = Mathf.Abs(size.y);
            size.z = Mathf.Abs(size.z);
            Handles.DrawWireCube(center, size);
        }

        private static void DrawTeleport(GizmoState.GizmoRequest r, Color baseColor, float alpha)
        {
            Handles.DrawDottedLine(r.Origin, r.End, 4f);
            Handles.SphereHandleCap(0, r.End, Quaternion.identity, 0.4f, EventType.Repaint);
        }

        // --- Helpers ---

        private static void DrawWireSphere(Vector3 center, float radius)
        {
            Handles.DrawWireDisc(center, Vector3.up, radius);
            Handles.DrawWireDisc(center, Vector3.right, radius);
            Handles.DrawWireDisc(center, Vector3.forward, radius);
        }
    }
}
