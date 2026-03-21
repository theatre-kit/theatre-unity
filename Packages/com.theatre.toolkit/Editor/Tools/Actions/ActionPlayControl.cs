using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor.Tools.Actions
{
    /// <summary>
    /// action:pause, action:step, action:unpause — play mode controls.
    /// </summary>
    internal static class ActionPlayControl
    {
        public static string ExecutePause(JObject args)
        {
            var error = ResponseHelpers.RequirePlayMode("pause");
            if (error != null) return error;

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
            var error = ResponseHelpers.RequirePlayMode("step");
            if (error != null) return error;

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
            var error = ResponseHelpers.RequirePlayMode("unpause");
            if (error != null) return error;

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
