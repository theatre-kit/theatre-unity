using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor
{
    /// <summary>
    /// action:teleport — move a GameObject to a position.
    /// </summary>
    internal static class ActionTeleport
    {
        public static string Execute(JObject args)
        {
            var posArr = args["position"] as JArray;

            if (posArr == null || posArr.Count < 3)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'position' — provide [x, y, z]",
                    "Example: {\"operation\": \"teleport\", \"path\": \"/Player\", \"position\": [10, 0, 5]}");
            }

            var resolveError = ObjectResolver.ResolveFromArgs(args, out var go);
            if (resolveError != null) return resolveError;
            var newPos = new Vector3(
                posArr[0].Value<float>(),
                posArr[1].Value<float>(),
                posArr[2].Value<float>());

            var oldPos = go.transform.position;

            // Undo in edit mode, direct set in play mode
            if (!Application.isPlaying)
            {
#if UNITY_EDITOR
                Undo.RecordObject(go.transform, "Theatre Teleport");
#endif
            }

            go.transform.position = newPos;

            // Handle optional rotation
            Vector3? oldRot = null;
            var rotArr = args["rotation_euler"] as JArray;
            if (rotArr != null && rotArr.Count >= 3)
            {
                oldRot = go.transform.eulerAngles;
                go.transform.eulerAngles = new Vector3(
                    rotArr[0].Value<float>(),
                    rotArr[1].Value<float>(),
                    rotArr[2].Value<float>());
            }

            // In play mode with Rigidbody, sync physics
            if (Application.isPlaying)
            {
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.MovePosition(newPos);
                }
                var rb2d = go.GetComponent<Rigidbody2D>();
                if (rb2d != null)
                {
                    rb2d.MovePosition(new Vector2(newPos.x, newPos.y));
                }
            }

            // Build response
            var response = new JObject();
            response["result"] = "ok";
            response["path"] = ResponseHelpers.GetHierarchyPath(go.transform);
#pragma warning disable CS0618
            response["instance_id"] = go.GetInstanceID();
#pragma warning restore CS0618
            response["position"] = ResponseHelpers.ToJArray(newPos);
            response["previous_position"] = ResponseHelpers.ToJArray(oldPos);
            if (rotArr != null)
            {
                response["rotation_euler"] = ResponseHelpers.ToJArray(
                    go.transform.eulerAngles);
                if (oldRot.HasValue)
                    response["previous_rotation_euler"] = ResponseHelpers.ToJArray(
                        oldRot.Value);
            }
            ResponseHelpers.AddFrameContext(response);

            return response.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
