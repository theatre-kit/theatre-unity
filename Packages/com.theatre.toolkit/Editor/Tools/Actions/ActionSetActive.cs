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
            var path = args["path"]?.Value<string>();
            var instanceId = args["instance_id"]?.Value<int>();
            var active = args["active"];

            if (active == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'active' parameter (true/false)",
                    "Example: {\"operation\": \"set_active\", \"path\": \"/Enemy\", \"active\": false}");

            var resolved = ObjectResolver.Resolve(path, instanceId);
            if (!resolved.Success)
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);

            var go = resolved.GameObject;
            var previousActive = go.activeSelf;
            var newActive = active.ToObject<bool>();

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RecordObject(go, "Theatre SetActive");
#endif

            go.SetActive(newActive);

            var response = new JObject();
            response["result"] = "ok";
            response["path"] = ResponseHelpers.GetHierarchyPath(go.transform);
#pragma warning disable CS0618
            response["instance_id"] = go.GetInstanceID();
#pragma warning restore CS0618
            response["active"] = newActive;
            response["previous_active"] = previousActive;
            ResponseHelpers.AddFrameContext(response);

            return response.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
