using System;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// action:set_timescale — change Time.timeScale.
    /// </summary>
    internal static class ActionSetTimescale
    {
        public static string Execute(JObject args)
        {
            var error = ResponseHelpers.RequirePlayMode("set_timescale");
            if (error != null) return error;

            var timescale = args["timescale"];
            if (timescale == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'timescale' parameter",
                    "Provide a number 0.0-100.0, e.g., {\"operation\": \"set_timescale\", \"timescale\": 0.5}");

            var newScale = timescale.ToObject<float>();
            if (newScale < 0f || newScale > 100f)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"timescale {newScale} out of range [0.0, 100.0]",
                    "Use 0 to freeze, 1 for normal speed, >1 for fast-forward");

            var previousScale = Time.timeScale;
            Time.timeScale = newScale;

            var response = new JObject();
            response["result"] = "ok";
            response["timescale"] = Math.Round(newScale, 4);
            response["previous_timescale"] = Math.Round(previousScale, 4);
            ResponseHelpers.AddFrameContext(response);

            return response.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
