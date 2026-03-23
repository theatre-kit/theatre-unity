using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEditor;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: animation_clip_op
    /// Compound tool for creating and modifying AnimationClip assets in the Unity Editor.
    /// Operations: create, add_curve, remove_curve, set_keyframe, set_events, set_loop, list_curves.
    /// </summary>
    public static class AnimationClipOpTool
    {
        private static readonly JToken s_inputSchema;

        static AnimationClipOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create"", ""add_curve"", ""remove_curve"", ""set_keyframe"", ""set_events"", ""set_loop"", ""list_curves""],
                        ""description"": ""The animation clip operation to perform.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for creating a new clip (e.g. 'Assets/Animations/MyClip.anim').""
                    },
                    ""clip_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path to an existing .anim clip.""
                    },
                    ""property_name"": {
                        ""type"": ""string"",
                        ""description"": ""The animated property name (e.g. 'm_LocalPosition.x').""
                    },
                    ""type"": {
                        ""type"": ""string"",
                        ""description"": ""Component type name that owns the property (e.g. 'Transform').""
                    },
                    ""relative_path"": {
                        ""type"": ""string"",
                        ""description"": ""Hierarchy path relative to the animated object root. Default empty string targets the root.""
                    },
                    ""keyframes"": {
                        ""type"": ""array"",
                        ""description"": ""Array of keyframe objects: [{time, value, in_tangent?, out_tangent?}].""
                    },
                    ""time"": {
                        ""type"": ""number"",
                        ""description"": ""Time in seconds for set_keyframe.""
                    },
                    ""value"": {
                        ""type"": ""number"",
                        ""description"": ""Value for set_keyframe.""
                    },
                    ""in_tangent"": {
                        ""type"": ""number"",
                        ""description"": ""In tangent for set_keyframe (optional).""
                    },
                    ""out_tangent"": {
                        ""type"": ""number"",
                        ""description"": ""Out tangent for set_keyframe (optional).""
                    },
                    ""events"": {
                        ""type"": ""array"",
                        ""description"": ""Array of event objects: [{time, function, int_param?, float_param?, string_param?}].""
                    },
                    ""frame_rate"": {
                        ""type"": ""number"",
                        ""description"": ""Clip frame rate (default 60).""
                    },
                    ""wrap_mode"": {
                        ""type"": ""string"",
                        ""enum"": [""default"", ""once"", ""loop"", ""ping_pong"", ""clamp_forever""],
                        ""description"": ""Clip wrap mode.""
                    },
                    ""legacy"": {
                        ""type"": ""boolean"",
                        ""description"": ""Whether to create a legacy AnimationClip (default false).""
                    },
                    ""loop_time"": {
                        ""type"": ""boolean"",
                        ""description"": ""Enable loop time setting.""
                    },
                    ""loop_pose"": {
                        ""type"": ""boolean"",
                        ""description"": ""Enable loop pose blending.""
                    },
                    ""cycle_offset"": {
                        ""type"": ""number"",
                        ""description"": ""Cycle offset for looping (0–1).""
                    },
                    ""dry_run"": {
                        ""type"": ""boolean"",
                        ""description"": ""If true, validate only — do not mutate.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "animation_clip_op",
                description: "Create and modify AnimationClip assets in the Unity Editor. "
                    + "Operations: create, add_curve, remove_curve, set_keyframe, set_events, set_loop, list_curves. "
                    + "Supports dry_run to validate without mutating.",
                inputSchema: s_inputSchema,
                group: ToolGroup.DirectorAnim,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = false
                }
            ));
        }

        private static string Execute(JToken arguments) =>
            CompoundToolDispatcher.Execute(
                "animation_clip_op",
                arguments,
                (args, operation) => operation switch
                {
                    "create"        => Create(args),
                    "add_curve"     => AddCurve(args),
                    "remove_curve"  => RemoveCurve(args),
                    "set_keyframe"  => SetKeyframe(args),
                    "set_events"    => SetEvents(args),
                    "set_loop"      => SetLoop(args),
                    "list_curves"   => ListCurves(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create, add_curve, remove_curve, set_keyframe, set_events, set_loop, list_curves")
                },
                "create, add_curve, remove_curve, set_keyframe, set_events, set_loop, list_curves");

        /// <summary>Create a new AnimationClip asset at the given path.</summary>
        internal static string Create(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".anim");
            if (pathError != null) return pathError;

            var dryRun = DirectorHelpers.CheckDryRun(args, () => (true, new List<string>()));
            if (dryRun != null) return dryRun;

            var clip = new AnimationClip();

            // frame_rate
            var frameRate = args["frame_rate"]?.Value<float>() ?? 60f;
            clip.frameRate = frameRate;

            // wrap_mode
            var wrapModeStr = args["wrap_mode"]?.Value<string>();
            if (!string.IsNullOrEmpty(wrapModeStr))
                clip.wrapMode = ParseWrapMode(wrapModeStr);

            // legacy
            var legacy = args["legacy"]?.Value<bool>() ?? false;
            clip.legacy = legacy;

            DirectorHelpers.EnsureParentDirectory(assetPath);
            AssetDatabase.CreateAsset(clip, assetPath);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create";
            response["asset_path"] = assetPath;
            response["frame_rate"] = clip.frameRate;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Add or replace an animation curve on a clip.</summary>
        internal static string AddCurve(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<AnimationClip>(
                args, out var clip, out var clipPath, ".anim", pathParam: "clip_path");
            if (loadError != null) return loadError;

            var propertyName = args["property_name"]?.Value<string>();
            if (string.IsNullOrEmpty(propertyName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'property_name' parameter",
                    "E.g. 'm_LocalPosition.x'");

            var typeName = args["type"]?.Value<string>();
            var componentType = DirectorHelpers.ResolveComponentType(typeName, out var typeError);
            if (typeError != null) return typeError;

            var keyframesToken = args["keyframes"] as JArray;
            if (keyframesToken == null || keyframesToken.Count == 0)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or empty 'keyframes' array",
                    "Provide at least one keyframe: [{\"time\": 0, \"value\": 0}]");

            var relativePath = args["relative_path"]?.Value<string>() ?? "";

            var keys = BuildKeyframes(keyframesToken);
            var curve = new AnimationCurve(keys);

            var binding = new EditorCurveBinding
            {
                path = relativePath,
                type = componentType,
                propertyName = propertyName
            };

            AnimationUtility.SetEditorCurve(clip, binding, curve);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);

            var allBindings = AnimationUtility.GetCurveBindings(clip);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_curve";
            response["clip_path"] = clipPath;
            response["property_name"] = propertyName;
            response["keyframe_count"] = keys.Length;
            response["curve_count"] = allBindings.Length;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Remove an animation curve from a clip.</summary>
        internal static string RemoveCurve(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<AnimationClip>(
                args, out var clip, out var clipPath, ".anim", pathParam: "clip_path");
            if (loadError != null) return loadError;

            var propertyName = args["property_name"]?.Value<string>();
            if (string.IsNullOrEmpty(propertyName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'property_name' parameter",
                    "E.g. 'm_LocalPosition.x'");

            var typeName = args["type"]?.Value<string>();
            var componentType = DirectorHelpers.ResolveComponentType(typeName, out var typeError);
            if (typeError != null) return typeError;

            var relativePath = args["relative_path"]?.Value<string>() ?? "";

            var binding = new EditorCurveBinding
            {
                path = relativePath,
                type = componentType,
                propertyName = propertyName
            };

            // Passing null removes the curve
            AnimationUtility.SetEditorCurve(clip, binding, null);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "remove_curve";
            response["clip_path"] = clipPath;
            response["property_name"] = propertyName;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Add or update a single keyframe on a curve.</summary>
        internal static string SetKeyframe(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<AnimationClip>(
                args, out var clip, out var clipPath, ".anim", pathParam: "clip_path");
            if (loadError != null) return loadError;

            var propertyName = args["property_name"]?.Value<string>();
            if (string.IsNullOrEmpty(propertyName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'property_name' parameter",
                    "E.g. 'm_LocalPosition.x'");

            var typeName = args["type"]?.Value<string>();
            var componentType = DirectorHelpers.ResolveComponentType(typeName, out var typeError);
            if (typeError != null) return typeError;

            if (args["time"] == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'time' parameter",
                    "Provide a time in seconds, e.g. 0.5");

            if (args["value"] == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'value' parameter",
                    "Provide the keyframe value, e.g. 1.0");

            var relativePath = args["relative_path"]?.Value<string>() ?? "";
            var time = args["time"].Value<float>();
            var value = args["value"].Value<float>();
            var inTangent = args["in_tangent"]?.Value<float>() ?? 0f;
            var outTangent = args["out_tangent"]?.Value<float>() ?? 0f;

            var binding = new EditorCurveBinding
            {
                path = relativePath,
                type = componentType,
                propertyName = propertyName
            };

            var curve = AnimationUtility.GetEditorCurve(clip, binding) ?? new AnimationCurve();
            curve.AddKey(new Keyframe(time, value, inTangent, outTangent));

            AnimationUtility.SetEditorCurve(clip, binding, curve);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_keyframe";
            response["clip_path"] = clipPath;
            response["property_name"] = propertyName;
            response["time"] = time;
            response["value"] = value;
            response["keyframe_count"] = curve.length;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Set the animation events on a clip, replacing all existing events.</summary>
        internal static string SetEvents(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<AnimationClip>(
                args, out var clip, out var clipPath, ".anim", pathParam: "clip_path");
            if (loadError != null) return loadError;

            var eventsToken = args["events"] as JArray;
            if (eventsToken == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'events' array",
                    "Provide an array of event objects: [{\"time\": 0.5, \"function\": \"OnFire\"}]");

            var events = new AnimationEvent[eventsToken.Count];
            for (int i = 0; i < eventsToken.Count; i++)
            {
                var ev = eventsToken[i] as JObject;
                if (ev == null) continue;

                events[i] = new AnimationEvent
                {
                    time = ev["time"]?.Value<float>() ?? 0f,
                    functionName = ev["function"]?.Value<string>() ?? "",
                    intParameter = ev["int_param"]?.Value<int>() ?? 0,
                    floatParameter = ev["float_param"]?.Value<float>() ?? 0f,
                    stringParameter = ev["string_param"]?.Value<string>() ?? ""
                };
            }

            AnimationUtility.SetAnimationEvents(clip, events);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_events";
            response["clip_path"] = clipPath;
            response["event_count"] = events.Length;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Configure loop settings on an animation clip.</summary>
        internal static string SetLoop(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<AnimationClip>(
                args, out var clip, out var clipPath, ".anim", pathParam: "clip_path");
            if (loadError != null) return loadError;

            var settings = AnimationUtility.GetAnimationClipSettings(clip);

            if (args["loop_time"] != null)
                settings.loopTime = args["loop_time"].Value<bool>();

            if (args["loop_pose"] != null)
                settings.loopBlend = args["loop_pose"].Value<bool>();

            if (args["cycle_offset"] != null)
                settings.cycleOffset = args["cycle_offset"].Value<float>();

            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_loop";
            response["clip_path"] = clipPath;
            response["loop_time"] = settings.loopTime;
            response["loop_pose"] = settings.loopBlend;
            response["cycle_offset"] = settings.cycleOffset;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>List all curve bindings on an animation clip.</summary>
        internal static string ListCurves(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<AnimationClip>(
                args, out var clip, out var clipPath, ".anim", pathParam: "clip_path");
            if (loadError != null) return loadError;

            var bindings = AnimationUtility.GetCurveBindings(clip);
            var curvesArray = new JArray();

            foreach (var binding in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                var curveObj = new JObject();
                curveObj["path"] = binding.path;
                curveObj["property"] = binding.propertyName;
                curveObj["type"] = binding.type != null ? binding.type.Name : "";
                curveObj["keyframe_count"] = curve != null ? curve.length : 0;
                curvesArray.Add(curveObj);
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "list_curves";
            response["clip_path"] = clipPath;
            response["curves"] = curvesArray;
            response["length"] = clip.length;
            response["frame_rate"] = clip.frameRate;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        // --- Helpers ---

        private static Keyframe[] BuildKeyframes(JArray keyframesToken)
        {
            var keys = new Keyframe[keyframesToken.Count];
            for (int i = 0; i < keyframesToken.Count; i++)
            {
                var kf = keyframesToken[i] as JObject;
                if (kf == null) continue;

                var time = kf["time"]?.Value<float>() ?? 0f;
                var value = kf["value"]?.Value<float>() ?? 0f;
                var inTangent = kf["in_tangent"]?.Value<float>() ?? 0f;
                var outTangent = kf["out_tangent"]?.Value<float>() ?? 0f;
                keys[i] = new Keyframe(time, value, inTangent, outTangent);
            }
            return keys;
        }

        private static WrapMode ParseWrapMode(string wrapModeStr)
        {
            return wrapModeStr?.ToLowerInvariant() switch
            {
                "once"          => WrapMode.Once,
                "loop"          => WrapMode.Loop,
                "ping_pong"     => WrapMode.PingPong,
                "clamp_forever" => WrapMode.ClampForever,
                _               => WrapMode.Default
            };
        }

    }
}
