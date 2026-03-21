using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor
{
    /// <summary>
    /// action:set_active — enable/disable a GameObject.
    /// </summary>
    internal static class ActionSetActive
    {
        public static string Execute(JObject args)
        {
            var active = args["active"];

            if (active == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'active' parameter (true/false)",
                    "Example: {\"operation\": \"set_active\", \"path\": \"/Enemy\", \"active\": false}");

            var resolveError = ObjectResolver.ResolveFromArgs(args, out var go);
            if (resolveError != null) return resolveError;
            var previousActive = go.activeSelf;
            var newActive = active.ToObject<bool>();

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RecordObject(go, "Theatre SetActive");
#endif

            go.SetActive(newActive);

            var response = new JObject();
            response["result"] = "ok";
            ResponseHelpers.AddIdentity(response, go);
            response["active"] = newActive;
            response["previous_active"] = previousActive;
            ResponseHelpers.AddFrameContext(response);

            return response.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
