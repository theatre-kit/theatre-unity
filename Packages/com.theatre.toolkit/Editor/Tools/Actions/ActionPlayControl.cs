using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor
{
    /// <summary>
    /// action:pause, action:step, action:unpause — play mode controls.
    /// </summary>
    internal static class ActionPlayControl
    {
        public static string ExecutePause(JObject args)
        {
            if (!Application.isPlaying)
                return ResponseHelpers.ErrorResponse(
                    "requires_play_mode",
                    "pause requires Play Mode",
                    "Enter Play Mode first");

#if UNITY_EDITOR
            EditorApplication.isPaused = true;
#endif

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "pause";
            response["paused"] = true;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string ExecuteStep(JObject args)
        {
            if (!Application.isPlaying)
                return ResponseHelpers.ErrorResponse(
                    "requires_play_mode",
                    "step requires Play Mode",
                    "Enter Play Mode first");

#if UNITY_EDITOR
            // Ensure paused first
            if (!EditorApplication.isPaused)
                EditorApplication.isPaused = true;

            EditorApplication.Step();
#endif

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "step";
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string ExecuteUnpause(JObject args)
        {
            if (!Application.isPlaying)
                return ResponseHelpers.ErrorResponse(
                    "requires_play_mode",
                    "unpause requires Play Mode",
                    "Enter Play Mode first");

#if UNITY_EDITOR
            EditorApplication.isPaused = false;
#endif

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "unpause";
            response["paused"] = false;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
