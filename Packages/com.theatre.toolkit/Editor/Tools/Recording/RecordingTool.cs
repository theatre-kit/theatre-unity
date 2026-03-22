using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Editor.Tools;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;

namespace Theatre.Editor.Tools.Recording
{
    /// <summary>
    /// MCP tool: recording
    /// Compound tool for frame-by-frame game state capture and analysis.
    /// Operations: start, stop, marker, list_clips, delete_clip,
    ///             query_range, diff_frames, clip_info, analyze.
    /// </summary>
    public static class RecordingTool
    {
        private static readonly JToken s_inputSchema;
        private static RecordingEngine s_engine;

        static RecordingTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""start"", ""stop"", ""marker"", ""list_clips"", ""delete_clip"",
                                   ""query_range"", ""diff_frames"", ""clip_info"", ""analyze""],
                        ""description"": ""The recording operation to perform.""
                    },
                    ""label"": {
                        ""type"": ""string"",
                        ""description"": ""Used by start (clip name) and marker (marker name).""
                    },
                    ""track_paths"": {
                        ""type"": ""array"", ""items"": { ""type"": ""string"" },
                        ""description"": ""Glob patterns for objects to record. Used by start.""
                    },
                    ""track_components"": {
                        ""type"": ""array"", ""items"": { ""type"": ""string"" },
                        ""description"": ""Component types to capture. Used by start.""
                    },
                    ""capture_rate"": {
                        ""type"": ""integer"", ""default"": 60, ""minimum"": 1, ""maximum"": 120,
                        ""description"": ""Capture rate in fps. Used by start.""
                    },
                    ""clip_id"": {
                        ""type"": ""string"",
                        ""description"": ""Clip identifier. Used by delete_clip, query_range, diff_frames, clip_info, analyze.""
                    },
                    ""from_frame"": {
                        ""type"": ""integer"",
                        ""description"": ""Start frame for range queries.""
                    },
                    ""to_frame"": {
                        ""type"": ""integer"",
                        ""description"": ""End frame for range queries.""
                    },
                    ""frame_a"": {
                        ""type"": ""integer"",
                        ""description"": ""First frame for diff_frames.""
                    },
                    ""frame_b"": {
                        ""type"": ""integer"",
                        ""description"": ""Second frame for diff_frames.""
                    },
                    ""paths"": {
                        ""type"": ""array"", ""items"": { ""type"": ""string"" },
                        ""description"": ""Filter by object paths. Used by query_range.""
                    },
                    ""properties"": {
                        ""type"": ""array"", ""items"": { ""type"": ""string"" },
                        ""description"": ""Filter by property names. Used by query_range.""
                    },
                    ""query"": {
                        ""type"": ""string"", ""enum"": [""threshold"", ""min"", ""max"", ""count_changes""],
                        ""description"": ""Analysis query type. Used by analyze.""
                    },
                    ""path"": {
                        ""type"": ""string"",
                        ""description"": ""Target path. Used by analyze.""
                    },
                    ""property"": {
                        ""type"": ""string"",
                        ""description"": ""Target property name. Used by analyze.""
                    },
                    ""below"": { ""type"": ""number"", ""description"": ""Threshold: below value. Used by analyze."" },
                    ""above"": { ""type"": ""number"", ""description"": ""Threshold: above value. Used by analyze."" },
                    ""metadata"": {
                        ""type"": ""object"",
                        ""description"": ""Arbitrary metadata to attach to a marker.""
                    },
                    ""budget"": {
                        ""type"": ""integer"", ""default"": 1500, ""minimum"": 100, ""maximum"": 4000,
                        ""description"": ""Token budget for range query responses.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "recording",
                description: "Record and analyze game state frame-by-frame. "
                    + "Start/stop recordings, insert markers, query frame ranges, "
                    + "compare frames, and run analysis queries on captured data.",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageRecording,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = false
                }
            ));
        }

        /// <summary>Get or create the RecordingEngine.</summary>
        internal static RecordingEngine GetEngine()
        {
            if (s_engine == null)
            {
                s_engine = new RecordingEngine();
                s_engine.Initialize(OnRecordingEvent);
                UnityEditor.EditorApplication.update += s_engine.Tick;
            }
            return s_engine;
        }

        /// <summary>Shut down the engine on server stop.</summary>
        internal static void Shutdown()
        {
            if (s_engine != null)
            {
                UnityEditor.EditorApplication.update -= s_engine.Tick;
                s_engine.Shutdown();
                s_engine = null;
            }
        }

        private static void OnRecordingEvent(JObject notifParams)
        {
            string method = notifParams["event_type"]?.Value<string>() ?? "recording_event";
            notifParams.Remove("event_type");
            var notification = JsonRpcResponse.Notification(
                $"notifications/theatre/{method}", notifParams);
            TheatreServer.SseManager?.PushNotification(notification);
        }

        private static string Execute(JToken arguments) =>
            CompoundToolDispatcher.Execute(
                "recording",
                arguments,
                (args, operation) => operation switch
                {
                    "start"       => ExecuteStart(args),
                    "stop"        => ExecuteStop(args),
                    "marker"      => ExecuteMarker(args),
                    "list_clips"  => ExecuteListClips(args),
                    "delete_clip" => ExecuteDeleteClip(args),
                    "query_range" => ExecuteQueryRange(args),
                    "diff_frames" => ExecuteDiffFrames(args),
                    "clip_info"   => ExecuteClipInfo(args),
                    "analyze"     => ExecuteAnalyze(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: start, stop, marker, list_clips, delete_clip, query_range, diff_frames, clip_info, analyze")
                },
                "start, stop, marker, list_clips, delete_clip, query_range, diff_frames, clip_info, analyze");

        private static string ExecuteStart(JObject args)
        {
            var playModeError = ResponseHelpers.RequirePlayMode("recording:start");
            if (playModeError != null) return playModeError;

            var label = args["label"]?.Value<string>();
            var trackPaths = args["track_paths"]?.ToObject<string[]>();
            var trackComponents = args["track_components"]?.ToObject<string[]>();
            var captureRate = args["capture_rate"]?.Value<int>() ?? 60;

            var engine = GetEngine();
            var meta = engine.Start(label, trackPaths, trackComponents, captureRate);

            if (meta == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "recording_in_progress",
                    "A recording is already in progress",
                    "Call recording:stop first, then recording:start a new clip");
            }

            var response = new JObject();
            response["result"] = "ok";
            response["clip_id"] = meta.ClipId;
            response["label"] = meta.Label;
            response["capture_rate"] = meta.CaptureRate;
            if (meta.TrackPaths != null)
                response["track_paths"] = new JArray(meta.TrackPaths);
            if (meta.TrackComponents != null)
                response["track_components"] = new JArray(meta.TrackComponents);
            ResponseHelpers.AddFrameContext(response);

            return response.ToString(Formatting.None);
        }

        private static string ExecuteStop(JObject args)
        {
            var engine = GetEngine();
            var meta = engine.Stop();

            if (meta == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "no_active_recording",
                    "No recording is currently active",
                    "Call recording:start first to begin a recording");
            }

            long fileSizeBytes = 0;
            if (!string.IsNullOrEmpty(meta.FilePath) && File.Exists(meta.FilePath))
            {
                try { fileSizeBytes = new FileInfo(meta.FilePath).Length; }
                catch { }
            }

            var response = new JObject();
            response["result"] = "ok";
            response["clip_id"] = meta.ClipId;
            response["label"] = meta.Label;
            response["duration"] = Math.Round(meta.Duration, 2);
            response["frame_count"] = meta.FrameCount;
            response["file_size_bytes"] = fileSizeBytes;
            ResponseHelpers.AddFrameContext(response);

            return response.ToString(Formatting.None);
        }

        private static string ExecuteMarker(JObject args)
        {
            var label = args["label"]?.Value<string>() ?? "marker";
            var metadataToken = args["metadata"] as JObject;

            var engine = GetEngine();
            var marker = engine.InsertMarker(label, metadataToken);

            if (marker == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "no_active_recording",
                    "No recording is currently active",
                    "Call recording:start first to begin a recording");
            }

            var response = new JObject();
            response["result"] = "ok";
            response["clip_id"] = engine.ActiveState?.ClipId;

            var markerObj = new JObject();
            markerObj["frame"] = marker.Frame;
            markerObj["time"] = Math.Round(marker.Time, 2);
            markerObj["label"] = marker.Label;
            if (marker.Metadata != null)
                markerObj["metadata"] = marker.Metadata;
            response["marker"] = markerObj;

            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        private static string ExecuteListClips(JObject args)
        {
            var engine = GetEngine();
            var clips = engine.ClipIndex;

            var clipsArray = new JArray();
            foreach (var clip in clips)
            {
                var obj = new JObject();
                obj["clip_id"] = clip.ClipId;
                obj["label"] = clip.Label;
                obj["scene"] = clip.Scene;
                obj["duration"] = Math.Round(clip.Duration, 2);
                obj["frame_count"] = clip.FrameCount;
                obj["capture_rate"] = clip.CaptureRate;
                obj["created_at"] = clip.CreatedAt;
                obj["file_path"] = clip.FilePath;
                clipsArray.Add(obj);
            }

            var response = new JObject();
            response["clips"] = clipsArray;
            response["count"] = clips.Count;
            return response.ToString(Formatting.None);
        }

        private static string ExecuteDeleteClip(JObject args)
        {
            var clipId = args["clip_id"]?.Value<string>();
            if (string.IsNullOrEmpty(clipId))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'clip_id' parameter",
                    "Provide the clip_id from recording:list_clips");
            }

            var engine = GetEngine();
            var clips = engine.ClipIndex;

            ClipMetadata found = null;
            for (int i = 0; i < clips.Count; i++)
            {
                if (clips[i].ClipId == clipId)
                {
                    found = clips[i];
                    clips.RemoveAt(i);
                    break;
                }
            }

            if (found == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "clip_not_found",
                    $"No clip found with clip_id '{clipId}'",
                    "Use recording:list_clips to see available clips");
            }

            // Delete the SQLite file
            if (!string.IsNullOrEmpty(found.FilePath) && File.Exists(found.FilePath))
            {
                try { File.Delete(found.FilePath); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Theatre] Failed to delete clip file '{found.FilePath}': {ex.Message}");
                }
            }

            RecordingPersistence.SaveClipIndex(clips);

            var response = new JObject();
            response["result"] = "ok";
            response["clip_id"] = clipId;
            return response.ToString(Formatting.None);
        }

        private static string ExecuteQueryRange(JObject args)
        {
            var clipId = args["clip_id"]?.Value<string>();
            if (string.IsNullOrEmpty(clipId))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'clip_id' parameter",
                    "Provide the clip_id from recording:list_clips");
            }

            var clipPath = FindClipPath(clipId);
            if (clipPath == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "clip_not_found",
                    $"No clip found with clip_id '{clipId}'",
                    "Use recording:list_clips to see available clips");
            }

            if (!File.Exists(clipPath))
            {
                return ResponseHelpers.ErrorResponse(
                    "clip_file_missing",
                    $"Clip file '{clipPath}' does not exist",
                    "The recording file may have been moved or deleted");
            }

            var fromFrame = args["from_frame"]?.Value<int>() ?? 0;
            var toFrame = args["to_frame"]?.Value<int>() ?? int.MaxValue;
            var paths = args["paths"]?.ToObject<string[]>();
            var properties = args["properties"]?.ToObject<string[]>();
            var budgetSize = args["budget"]?.Value<int>() ?? TokenBudget.DefaultBudget;

            using (var db = new RecordingDb(clipPath))
            {
                var frames = db.QueryRange(fromFrame, toFrame, paths, properties);
                var budget = new TokenBudget(budgetSize);

                var framesArray = new JArray();
                bool truncated = false;

                foreach (var frame in frames)
                {
                    var frameObj = new JObject();
                    frameObj["frame"] = frame.Frame;
                    frameObj["time"] = Math.Round(frame.Time, 3);

                    var objectsArray = new JArray();
                    if (frame.Objects != null)
                    {
                        foreach (var kvp in frame.Objects)
                        {
                            var objEntry = new JObject();
                            objEntry["path"] = kvp.Value.Path;
                            objEntry["instance_id"] = kvp.Key;
                            if (kvp.Value.Properties != null)
                            {
                                var propsObj = new JObject();
                                foreach (var p in kvp.Value.Properties)
                                    propsObj[p.Key] = p.Value;
                                objEntry["properties"] = propsObj;
                            }
                            objectsArray.Add(objEntry);
                        }
                    }
                    frameObj["objects"] = objectsArray;

                    var frameJson = frameObj.ToString(Formatting.None);
                    if (budget.WouldExceed(frameJson.Length))
                    {
                        truncated = true;
                        break;
                    }
                    budget.Add(frameJson.Length);
                    framesArray.Add(frameObj);
                }

                var response = new JObject();
                response["clip_id"] = clipId;
                response["frames"] = framesArray;
                response["frame_count"] = framesArray.Count;
                response["budget"] = budget.ToBudgetJObject(
                    truncated,
                    truncated ? "budget_exhausted" : null,
                    truncated ? "Reduce frame range or increase budget" : null);
                return response.ToString(Formatting.None);
            }
        }

        private static string ExecuteDiffFrames(JObject args)
        {
            var clipId = args["clip_id"]?.Value<string>();
            if (string.IsNullOrEmpty(clipId))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'clip_id' parameter",
                    "Provide the clip_id from recording:list_clips");
            }

            var frameA = args["frame_a"]?.Value<int>();
            var frameB = args["frame_b"]?.Value<int>();

            if (frameA == null || frameB == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'frame_a' and 'frame_b' parameters",
                    "Provide two frame numbers to compare");
            }

            var clipPath = FindClipPath(clipId);
            if (clipPath == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "clip_not_found",
                    $"No clip found with clip_id '{clipId}'",
                    "Use recording:list_clips to see available clips");
            }

            if (!File.Exists(clipPath))
            {
                return ResponseHelpers.ErrorResponse(
                    "clip_file_missing",
                    $"Clip file '{clipPath}' does not exist",
                    "The recording file may have been moved or deleted");
            }

            using (var db = new RecordingDb(clipPath))
            {
                var diff = db.DiffFrames(frameA.Value, frameB.Value);

                var objectsArray = new JArray();
                if (diff.Objects != null)
                {
                    foreach (var kvp in diff.Objects)
                    {
                        var objEntry = new JObject();
                        objEntry["path"] = kvp.Value.Path;
                        objEntry["instance_id"] = kvp.Key;
                        if (kvp.Value.Properties != null)
                        {
                            var propsObj = new JObject();
                            foreach (var p in kvp.Value.Properties)
                                propsObj[p.Key] = p.Value;
                            objEntry["properties"] = propsObj;
                        }
                        objectsArray.Add(objEntry);
                    }
                }

                var response = new JObject();
                response["clip_id"] = clipId;
                response["frame_a"] = frameA.Value;
                response["frame_b"] = frameB.Value;
                response["changed_objects"] = objectsArray;
                response["changed_object_count"] = objectsArray.Count;
                return response.ToString(Formatting.None);
            }
        }

        private static string ExecuteClipInfo(JObject args)
        {
            var clipId = args["clip_id"]?.Value<string>();
            if (string.IsNullOrEmpty(clipId))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'clip_id' parameter",
                    "Provide the clip_id from recording:list_clips");
            }

            var clipPath = FindClipPath(clipId);
            if (clipPath == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "clip_not_found",
                    $"No clip found with clip_id '{clipId}'",
                    "Use recording:list_clips to see available clips");
            }

            if (!File.Exists(clipPath))
            {
                return ResponseHelpers.ErrorResponse(
                    "clip_file_missing",
                    $"Clip file '{clipPath}' does not exist",
                    "The recording file may have been moved or deleted");
            }

            using (var db = new RecordingDb(clipPath))
            {
                var meta = db.ReadMetadata();
                var markers = db.ReadMarkers();

                var response = new JObject();
                if (meta != null)
                {
                    response["clip_id"] = meta.ClipId;
                    response["label"] = meta.Label;
                    response["scene"] = meta.Scene;
                    response["duration"] = Math.Round(meta.Duration, 2);
                    response["start_frame"] = meta.StartFrame;
                    response["end_frame"] = meta.EndFrame;
                    response["frame_count"] = meta.FrameCount;
                    response["capture_rate"] = meta.CaptureRate;
                    response["created_at"] = meta.CreatedAt;
                    if (meta.TrackPaths != null)
                        response["track_paths"] = new JArray(meta.TrackPaths);
                    if (meta.TrackComponents != null)
                        response["track_components"] = new JArray(meta.TrackComponents);
                }

                var markersArray = new JArray();
                foreach (var m in markers)
                {
                    var mObj = new JObject();
                    mObj["frame"] = m.Frame;
                    mObj["time"] = Math.Round(m.Time, 3);
                    mObj["label"] = m.Label;
                    if (m.Metadata != null)
                        mObj["metadata"] = m.Metadata;
                    markersArray.Add(mObj);
                }
                response["markers"] = markersArray;
                response["marker_count"] = markersArray.Count;

                long fileSizeBytes = 0;
                try { fileSizeBytes = new FileInfo(clipPath).Length; }
                catch { }
                response["file_size_bytes"] = fileSizeBytes;

                return response.ToString(Formatting.None);
            }
        }

        private static string ExecuteAnalyze(JObject args)
        {
            var clipId = args["clip_id"]?.Value<string>();
            if (string.IsNullOrEmpty(clipId))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'clip_id' parameter",
                    "Provide the clip_id from recording:list_clips");
            }

            var queryType = args["query"]?.Value<string>();
            if (string.IsNullOrEmpty(queryType))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'query' parameter",
                    "Valid query types: threshold, min, max, count_changes");
            }

            var clipPath = FindClipPath(clipId);
            if (clipPath == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "clip_not_found",
                    $"No clip found with clip_id '{clipId}'",
                    "Use recording:list_clips to see available clips");
            }

            if (!File.Exists(clipPath))
            {
                return ResponseHelpers.ErrorResponse(
                    "clip_file_missing",
                    $"Clip file '{clipPath}' does not exist",
                    "The recording file may have been moved or deleted");
            }

            var query = new AnalyzeQuery
            {
                Query = queryType,
                Path = args["path"]?.Value<string>(),
                Property = args["property"]?.Value<string>(),
                Below = args["below"]?.Value<float>(),
                Above = args["above"]?.Value<float>(),
                FromFrame = args["from_frame"]?.Value<int>(),
                ToFrame = args["to_frame"]?.Value<int>(),
            };

            if (string.IsNullOrEmpty(query.Property))
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'property' parameter for analyze",
                    "Provide the property name to analyze (e.g. 'position', 'health')");
            }

            using (var db = new RecordingDb(clipPath))
            {
                AnalyzeResult result;
                switch (queryType)
                {
                    case "threshold":
                        result = db.AnalyzeThreshold(query);
                        break;
                    case "min":
                    case "max":
                        result = db.AnalyzeMinMax(query);
                        break;
                    case "count_changes":
                        result = db.AnalyzeCountChanges(query);
                        break;
                    default:
                        return ResponseHelpers.ErrorResponse(
                            "invalid_parameter",
                            $"Unknown query type '{queryType}'",
                            "Valid query types: threshold, min, max, count_changes");
                }

                var response = new JObject();
                response["clip_id"] = clipId;
                response["query"] = result.Query;
                response["path"] = result.Path;
                response["property"] = result.Property;

                if (result.Value.HasValue)
                {
                    response["value"] = result.Value.Value;
                    response["frame"] = result.Frame;
                }

                var matchesArray = new JArray();
                if (result.Matches != null)
                {
                    foreach (var m in result.Matches)
                    {
                        var mObj = new JObject();
                        mObj["frame"] = m.Frame;
                        mObj["time"] = Math.Round(m.Time, 3);
                        mObj["value"] = m.Value;
                        matchesArray.Add(mObj);
                    }
                }
                response["matches"] = matchesArray;
                response["match_count"] = matchesArray.Count;

                return response.ToString(Formatting.None);
            }
        }

        // --- Private helpers ---

        private static string FindClipPath(string clipId)
        {
            var engine = GetEngine();
            foreach (var clip in engine.ClipIndex)
            {
                if (clip.ClipId == clipId)
                    return clip.FilePath;
            }
            return null;
        }
    }
}
