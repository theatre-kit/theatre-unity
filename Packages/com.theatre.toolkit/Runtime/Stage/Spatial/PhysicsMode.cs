using UnityEngine;
using UnityEngine.SceneManagement;

namespace Theatre.Stage
{
    /// <summary>
    /// Detects and caches whether the project uses 2D or 3D physics.
    /// Provides the per-query physics parameter override logic.
    /// </summary>
    public static class PhysicsMode
    {
        private static string s_cachedDefault;
        private static string s_cachedSceneName;

        /// <summary>
        /// Get the effective physics mode for a query.
        /// </summary>
        /// <param name="perQueryOverride">
        /// Per-query override: "3d", "2d", or null (use default).
        /// </param>
        /// <returns>"3d" or "2d".</returns>
        public static string GetEffective(string perQueryOverride)
        {
            if (perQueryOverride == "3d" || perQueryOverride == "2d")
                return perQueryOverride;
            return GetDefault();
        }

        /// <summary>
        /// Get the project-level default physics mode.
        /// Auto-detected from scene content, cached per scene.
        /// </summary>
        public static string GetDefault()
        {
            var sceneName = SceneManager.GetActiveScene().name;
            if (s_cachedDefault != null && s_cachedSceneName == sceneName)
                return s_cachedDefault;

            s_cachedSceneName = sceneName;
            s_cachedDefault = Detect();
            return s_cachedDefault;
        }

        /// <summary>
        /// Invalidate the cached default (called on scene change).
        /// </summary>
        public static void Invalidate()
        {
            s_cachedDefault = null;
            s_cachedSceneName = null;
        }

        /// <summary>
        /// Detect physics mode by scanning for 2D vs 3D physics components.
        /// </summary>
        private static string Detect()
        {
            bool has3D = Object.FindAnyObjectByType<Collider>() != null
                      || Object.FindAnyObjectByType<Rigidbody>() != null;
            bool has2D = Object.FindAnyObjectByType<Collider2D>() != null
                      || Object.FindAnyObjectByType<Rigidbody2D>() != null;

            if (has2D && !has3D) return "2d";
            // Default to 3D for mixed or empty scenes
            return "3d";
        }

        /// <summary>
        /// Check if play mode is required for the given operation and
        /// return an error response if not in play mode.
        /// Returns null if play mode is not required or is satisfied.
        /// </summary>
        public static string CheckPlayModeRequired(string operation)
        {
            bool needsPlayMode = operation == "overlap"
                              || operation == "raycast"
                              || operation == "linecast";

            if (needsPlayMode && !Application.isPlaying)
            {
                return ResponseHelpers.ErrorResponse(
                    "requires_play_mode",
                    $"The '{operation}' operation requires Play Mode because it uses the physics engine",
                    "Enter Play Mode first, or use 'nearest'/'radius' for transform-based queries in Edit Mode");
            }

            return null;
        }
    }
}
