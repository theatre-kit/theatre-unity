#if THEATRE_HAS_TIMELINE
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEditor;
using UnityEngine.Timeline;
using UnityEngine.Playables;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: timeline_op
    /// Compound tool for creating and configuring Timeline assets.
    /// Operations: create, add_track, add_clip, set_clip_properties, add_marker, bind_track, list_tracks.
    /// Requires com.unity.timeline to be installed (guarded by THEATRE_HAS_TIMELINE).
    /// </summary>
    public static class TimelineOpTool
    {
        private static readonly JToken s_inputSchema;

        static TimelineOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create"", ""add_track"", ""add_clip"", ""set_clip_properties"", ""add_marker"", ""bind_track"", ""list_tracks""],
                        ""description"": ""The timeline operation to perform.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path to the TimelineAsset (.playable file).""
                    },
                    ""frame_rate"": {
                        ""type"": ""number"",
                        ""description"": ""Frame rate for the timeline (default 60).""
                    },
                    ""track_type"": {
                        ""type"": ""string"",
                        ""enum"": [""animation"", ""activation"", ""audio"", ""control"", ""signal""],
                        ""description"": ""Type of track to add.""
                    },
                    ""track_name"": {
                        ""type"": ""string"",
                        ""description"": ""Name of the track (for add_track: name to give; for others: track to find by name).""
                    },
                    ""name"": {
                        ""type"": ""string"",
                        ""description"": ""Alias for track_name when adding a track.""
                    },
                    ""start"": {
                        ""type"": ""number"",
                        ""description"": ""Clip start time in seconds.""
                    },
                    ""duration"": {
                        ""type"": ""number"",
                        ""description"": ""Clip duration in seconds.""
                    },
                    ""clip_asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Path to an AnimationClip (.anim) to assign to an animation track clip.""
                    },
                    ""clip_index"": {
                        ""type"": ""integer"",
                        ""description"": ""Zero-based index of the clip on the track (for set_clip_properties).""
                    },
                    ""speed"": {
                        ""type"": ""number"",
                        ""description"": ""Playback speed multiplier for the clip.""
                    },
                    ""blend_in"": {
                        ""type"": ""number"",
                        ""description"": ""Ease-in duration in seconds.""
                    },
                    ""blend_out"": {
                        ""type"": ""number"",
                        ""description"": ""Ease-out duration in seconds.""
                    },
                    ""time"": {
                        ""type"": ""number"",
                        ""description"": ""Time in seconds for adding a marker.""
                    },
                    ""object_path"": {
                        ""type"": ""string"",
                        ""description"": ""Scene hierarchy path of the GameObject to bind to a track.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "timeline_op",
                description: "Create and configure Timeline assets (.playable). "
                    + "Operations: create, add_track, add_clip, set_clip_properties, add_marker, bind_track, list_tracks. "
                    + "Requires com.unity.timeline to be installed.",
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
                "timeline_op",
                arguments,
                (args, operation) => operation switch
                {
                    "create"               => Create(args),
                    "add_track"            => AddTrack(args),
                    "add_clip"             => AddClip(args),
                    "set_clip_properties"  => SetClipProperties(args),
                    "add_marker"           => AddMarker(args),
                    "bind_track"           => BindTrack(args),
                    "list_tracks"          => ListTracks(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create, add_track, add_clip, set_clip_properties, add_marker, bind_track, list_tracks")
                },
                "create, add_track, add_clip, set_clip_properties, add_marker, bind_track, list_tracks");

        /// <summary>Create a new TimelineAsset (.playable file).</summary>
        internal static string Create(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".playable");
            if (pathError != null) return pathError;

            DirectorHelpers.EnsureParentDirectory(assetPath);

            var asset = ScriptableObject.CreateInstance<TimelineAsset>();

            var frameRate = args["frame_rate"]?.Value<double>() ?? 60.0;
            asset.editorSettings.frameRate = frameRate;

            AssetDatabase.CreateAsset(asset, assetPath);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create";
            response["asset_path"] = assetPath;
            response["frame_rate"] = frameRate;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Add a track to a TimelineAsset.</summary>
        internal static string AddTrack(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<TimelineAsset>(
                args, out var asset, out var assetPath, ".playable");
            if (loadError != null) return loadError;

            var trackTypeStr = args["track_type"]?.Value<string>();
            if (string.IsNullOrEmpty(trackTypeStr))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'track_type' parameter",
                    "Valid values: animation, activation, audio, control, signal");

            var trackType = ParseTrackType(trackTypeStr);
            if (trackType == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Unknown track_type '{trackTypeStr}'",
                    "Valid values: animation, activation, audio, control, signal");

            // Accept either "name" or "track_name" for the track name
            var trackName = args["name"]?.Value<string>() ?? args["track_name"]?.Value<string>() ?? trackTypeStr;

            var track = asset.CreateTrack(trackType, null, trackName);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_track";
            response["asset_path"] = assetPath;
            response["track_name"] = track.name;
            response["track_type"] = trackTypeStr;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Add a clip to a track on a TimelineAsset.</summary>
        internal static string AddClip(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<TimelineAsset>(
                args, out var asset, out var assetPath, ".playable");
            if (loadError != null) return loadError;

            var trackName = args["track_name"]?.Value<string>();
            if (string.IsNullOrEmpty(trackName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'track_name' parameter",
                    "Provide the name of the track to add a clip to");

            if (args["start"] == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'start' parameter",
                    "Provide the clip start time in seconds");

            if (args["duration"] == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'duration' parameter",
                    "Provide the clip duration in seconds");

            var track = FindTrackByName(asset, trackName);
            if (track == null)
                return ResponseHelpers.ErrorResponse(
                    "not_found",
                    $"Track '{trackName}' not found in timeline",
                    "Add the track first with timeline_op:add_track, or check the name");

            var start = args["start"].Value<double>();
            var duration = args["duration"].Value<double>();

            var clip = track.CreateDefaultClip();
            clip.start = start;
            clip.duration = duration;

            // If an animation clip asset is provided and the track is AnimationTrack
            var clipAssetPath = args["clip_asset_path"]?.Value<string>();
            if (!string.IsNullOrEmpty(clipAssetPath) && track is AnimationTrack)
            {
                var animClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipAssetPath);
                if (animClip != null && clip.asset is AnimationPlayableAsset animPlayable)
                    animPlayable.clip = animClip;
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_clip";
            response["asset_path"] = assetPath;
            response["track_name"] = trackName;
            response["start"] = start;
            response["duration"] = duration;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Set properties on an existing clip on a track.</summary>
        internal static string SetClipProperties(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<TimelineAsset>(
                args, out var asset, out var assetPath, ".playable");
            if (loadError != null) return loadError;

            var trackName = args["track_name"]?.Value<string>();
            if (string.IsNullOrEmpty(trackName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'track_name' parameter",
                    "Provide the name of the track containing the clip");

            if (args["clip_index"] == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'clip_index' parameter",
                    "Provide the zero-based index of the clip on the track");

            var track = FindTrackByName(asset, trackName);
            if (track == null)
                return ResponseHelpers.ErrorResponse(
                    "not_found",
                    $"Track '{trackName}' not found in timeline",
                    "Check the track name");

            var clipIndex = args["clip_index"].Value<int>();
            var clips = new List<TimelineClip>(track.GetClips());
            if (clipIndex < 0 || clipIndex >= clips.Count)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Clip index {clipIndex} is out of range (track has {clips.Count} clips)",
                    "Use a valid clip index");

            var clip = clips[clipIndex];

            if (args["start"] != null)   clip.start    = args["start"].Value<double>();
            if (args["duration"] != null) clip.duration = args["duration"].Value<double>();
            if (args["speed"] != null)    clip.timeScale = args["speed"].Value<double>();
            if (args["blend_in"] != null) clip.blendInDuration  = args["blend_in"].Value<double>();
            if (args["blend_out"] != null) clip.blendOutDuration = args["blend_out"].Value<double>();

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_clip_properties";
            response["asset_path"] = assetPath;
            response["track_name"] = trackName;
            response["clip_index"] = clipIndex;
            response["start"] = clip.start;
            response["duration"] = clip.duration;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Add a marker to a timeline or specific track.</summary>
        internal static string AddMarker(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<TimelineAsset>(
                args, out var asset, out var assetPath, ".playable");
            if (loadError != null) return loadError;

            if (args["time"] == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'time' parameter",
                    "Provide the time in seconds for the marker");

            var time = args["time"].Value<double>();
            var trackName = args["track_name"]?.Value<string>();

            TrackAsset targetTrack = null;
            if (!string.IsNullOrEmpty(trackName))
                targetTrack = FindTrackByName(asset, trackName);

            IMarker marker;
            if (targetTrack is SignalTrack signalTrack)
            {
                // Add a SignalEmitter to the signal track
                marker = signalTrack.CreateMarker<SignalEmitter>(time);
            }
            else
            {
                // Add to the timeline's marker track (root markers)
                asset.CreateMarkerTrack();
                marker = asset.markerTrack.CreateMarker<SignalEmitter>(time);
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_marker";
            response["asset_path"] = assetPath;
            response["time"] = time;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Bind a track to a scene object via the PlayableDirector.</summary>
        internal static string BindTrack(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<TimelineAsset>(
                args, out var asset, out var assetPath, ".playable");
            if (loadError != null) return loadError;

            var trackName = args["track_name"]?.Value<string>();
            if (string.IsNullOrEmpty(trackName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'track_name' parameter",
                    "Provide the name of the track to bind");

            var objectPath = args["object_path"]?.Value<string>();
            if (string.IsNullOrEmpty(objectPath))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'object_path' parameter",
                    "Provide the scene hierarchy path of the object to bind");

            var track = FindTrackByName(asset, trackName);
            if (track == null)
                return ResponseHelpers.ErrorResponse(
                    "not_found",
                    $"Track '{trackName}' not found in timeline",
                    "Check the track name");

            // Resolve the scene object
            var resolveResult = ObjectResolver.Resolve(objectPath, null);
            if (!resolveResult.Success)
                return ResponseHelpers.ErrorResponse(
                    resolveResult.ErrorCode,
                    resolveResult.ErrorMessage ?? $"GameObject not found at path '{objectPath}'",
                    resolveResult.Suggestion ?? "Check the hierarchy path is correct");

            var go = resolveResult.GameObject;

            // Find a PlayableDirector in the scene that uses this TimelineAsset
            var directors = UnityEngine.Object.FindObjectsByType<PlayableDirector>(FindObjectsSortMode.None);
            PlayableDirector director = null;
            foreach (var d in directors)
            {
                if (d.playableAsset == asset)
                {
                    director = d;
                    break;
                }
            }

            if (director == null)
                return ResponseHelpers.ErrorResponse(
                    "not_found",
                    $"No PlayableDirector in the scene references '{assetPath}'",
                    "Add a PlayableDirector component to a GameObject and assign this TimelineAsset to it");

            director.SetGenericBinding(track, go);
            EditorUtility.SetDirty(director);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "bind_track";
            response["asset_path"] = assetPath;
            response["track_name"] = trackName;
            response["object_path"] = objectPath;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>List all tracks and their clips in a TimelineAsset.</summary>
        internal static string ListTracks(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<TimelineAsset>(
                args, out var asset, out var assetPath, ".playable");
            if (loadError != null) return loadError;

            var tracksArray = new JArray();
            foreach (var track in asset.GetOutputTracks())
            {
                var trackObj = new JObject();
                trackObj["name"] = track.name;
                trackObj["type"] = track.GetType().Name;

                var clipsArray = new JArray();
                foreach (var clip in track.GetClips())
                {
                    clipsArray.Add(new JObject
                    {
                        ["display_name"] = clip.displayName,
                        ["start"] = clip.start,
                        ["duration"] = clip.duration
                    });
                }
                trackObj["clips"] = clipsArray;
                trackObj["clip_count"] = clipsArray.Count;
                tracksArray.Add(trackObj);
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "list_tracks";
            response["asset_path"] = assetPath;
            response["tracks"] = tracksArray;
            response["track_count"] = tracksArray.Count;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        // --- Helpers ---

        private static TrackAsset FindTrackByName(TimelineAsset asset, string name)
        {
            foreach (var track in asset.GetOutputTracks())
            {
                if (track.name == name)
                    return track;
            }
            return null;
        }

        private static Type ParseTrackType(string trackTypeStr)
        {
            return trackTypeStr?.ToLowerInvariant() switch
            {
                "animation" => typeof(AnimationTrack),
                "activation" => typeof(ActivationTrack),
                "audio" => typeof(AudioTrack),
                "control" => typeof(ControlTrack),
                "signal" => typeof(SignalTrack),
                _ => null
            };
        }

    }
}
#endif
