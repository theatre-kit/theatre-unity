using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Theatre.Editor.Tools.Watch;
using Theatre.Editor.Tools.Recording;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// Main Theatre EditorWindow. Displays server status, tool group toggles,
    /// active watches, agent activity feed, and recording controls.
    /// </summary>
    public class TheatreWindow : EditorWindow
    {
        // --- Periodic refresh ---
        private double _lastRefreshTime;
        private const double RefreshInterval = 2.0;

        // --- UI element references ---
        private Label     _statusLabel;
        private Label     _agentLabel;
        private ScrollView _watchesScroll;
        private ScrollView _activityScroll;
        private Label     _sessionStatsLabel;
        private Label     _recordingStatusLabel;
        private ScrollView _clipsScroll;

        // Track last activity count to avoid rebuilding the whole list on every refresh
        private int _lastActivityCount;

        [MenuItem("Window/Theatre", priority = 100)]
        public static void ShowWindow()
        {
            GetWindow<TheatreWindow>("Theatre");
        }

        private void OnEnable()
        {
            EditorApplication.update += OnUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnUpdate;
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft   = 6;
            root.style.paddingRight  = 6;
            root.style.paddingTop    = 6;
            root.style.paddingBottom = 6;

            // --- Status Bar ---
            root.Add(MakeSectionHeader("Server"));
            var statusRow = new VisualElement();
            statusRow.style.flexDirection = FlexDirection.Row;
            statusRow.style.flexWrap      = Wrap.Wrap;
            statusRow.style.marginBottom  = 4;

            _statusLabel = new Label();
            _statusLabel.style.flexGrow = 1;
            statusRow.Add(_statusLabel);

            var copyUrlButton = new Button(CopyUrlToClipboard) { text = "Copy URL" };
            copyUrlButton.style.fontSize = 10;
            statusRow.Add(copyUrlButton);

            _agentLabel = new Label();
            _agentLabel.style.marginLeft = 6;
            statusRow.Add(_agentLabel);

            root.Add(statusRow);

            // --- Tool Group Toggles ---
            root.Add(MakeSectionHeader("Tool Groups"));
            root.Add(BuildToolGroupToggles());

            // --- Active Watches ---
            root.Add(MakeSectionHeader("Active Watches"));
            _watchesScroll = new ScrollView { style = { maxHeight = 120 } };
            root.Add(_watchesScroll);

            // --- Agent Activity Feed ---
            root.Add(MakeSectionHeader("Agent Activity"));
            _activityScroll = new ScrollView { style = { maxHeight = 160 } };
            root.Add(_activityScroll);

            _sessionStatsLabel = new Label();
            _sessionStatsLabel.style.marginTop    = 2;
            _sessionStatsLabel.style.fontSize     = 10;
            _sessionStatsLabel.style.color        = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
            root.Add(_sessionStatsLabel);

            // --- Recording ---
            root.Add(MakeSectionHeader("Recording"));
            root.Add(BuildRecordingSection());

            // Initial populate
            RefreshAll();
        }

        // -----------------------------------------------------------------------
        // Builder helpers
        // -----------------------------------------------------------------------

        private static Label MakeSectionHeader(string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop    = 6;
            label.style.marginBottom = 2;
            label.style.borderBottomWidth = 1;
            label.style.borderBottomColor = new StyleColor(new Color(0.35f, 0.35f, 0.35f));
            return label;
        }

        private static VisualElement BuildToolGroupToggles()
        {
            var container = new VisualElement();

            var groups = TheatreConfig.EnabledGroups;

            // Stage group row
            var stageRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            stageRow.Add(MakeGroupToggle("GameObject",  ToolGroup.StageGameObject, ref groups));
            stageRow.Add(MakeGroupToggle("Queries",     ToolGroup.StageQuery,      ref groups));
            stageRow.Add(MakeGroupToggle("Watches",     ToolGroup.StageWatch,      ref groups));
            stageRow.Add(MakeGroupToggle("Actions",     ToolGroup.StageAction,     ref groups));
            stageRow.Add(MakeGroupToggle("Recording",   ToolGroup.StageRecording,  ref groups));
            container.Add(new Label("Stage:") { style = { fontSize = 10, color = new StyleColor(new Color(0.5f, 0.8f, 1f)) } });
            container.Add(stageRow);

            // ECS row
            var ecsRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            ecsRow.Add(MakeGroupToggle("ECS World",   ToolGroup.ECSWorld,  ref groups));
            ecsRow.Add(MakeGroupToggle("ECS Entity",  ToolGroup.ECSEntity, ref groups));
            ecsRow.Add(MakeGroupToggle("ECS Query",   ToolGroup.ECSQuery,  ref groups));
            ecsRow.Add(MakeGroupToggle("ECS Action",  ToolGroup.ECSAction, ref groups));
            container.Add(new Label("ECS:") { style = { fontSize = 10, color = new StyleColor(new Color(0.5f, 0.8f, 1f)) } });
            container.Add(ecsRow);

            // Director row
            var dirRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            dirRow.Add(MakeGroupToggle("Scene",    ToolGroup.DirectorScene,   ref groups));
            dirRow.Add(MakeGroupToggle("Prefabs",  ToolGroup.DirectorPrefab,  ref groups));
            dirRow.Add(MakeGroupToggle("Assets",   ToolGroup.DirectorAsset,   ref groups));
            dirRow.Add(MakeGroupToggle("Anim",     ToolGroup.DirectorAnim,    ref groups));
            dirRow.Add(MakeGroupToggle("Spatial",  ToolGroup.DirectorSpatial, ref groups));
            dirRow.Add(MakeGroupToggle("Input",    ToolGroup.DirectorInput,   ref groups));
            dirRow.Add(MakeGroupToggle("Config",   ToolGroup.DirectorConfig,  ref groups));
            container.Add(new Label("Director:") { style = { fontSize = 10, color = new StyleColor(new Color(0.5f, 1f, 0.6f)) } });
            container.Add(dirRow);

            // Presets dropdown
            var presets = new List<string> { "GameObject Project", "ECS Project", "Stage Only", "Director Only", "Everything", "Custom" };
            var dropdown = new DropdownField("Presets", presets, 0);
            dropdown.style.maxWidth = 240;
            dropdown.RegisterValueChangedCallback(evt =>
            {
                ToolGroup preset = evt.newValue switch
                {
                    "GameObject Project" => ToolGroup.GameObjectProject,
                    "ECS Project"        => ToolGroup.ECSProject,
                    "Stage Only"         => ToolGroup.StageAll,
                    "Director Only"      => ToolGroup.DirectorAll,
                    "Everything"         => ToolGroup.Everything,
                    _                    => TheatreConfig.EnabledGroups,
                };
                TheatreServer.SetEnabledGroups(preset);
            });
            container.Add(dropdown);

            return container;
        }

        private static Toggle MakeGroupToggle(string label, ToolGroup flag, ref ToolGroup current)
        {
            var toggle = new Toggle(label);
            toggle.value = (current & flag) != 0;
            toggle.style.minWidth = 90;
            // Capture flag in local for closure
            var capturedFlag = flag;
            toggle.RegisterValueChangedCallback(evt =>
            {
                var g = TheatreConfig.EnabledGroups;
                if (evt.newValue) g |= capturedFlag;
                else              g &= ~capturedFlag;
                TheatreServer.SetEnabledGroups(g);
            });
            return toggle;
        }

        private VisualElement BuildRecordingSection()
        {
            var container = new VisualElement();

            var btnRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };

            var recordBtn = new Button(() =>
            {
                var engine = RecordingTool.GetEngine();
                if (!engine.IsRecording)
                    engine.Start("manual", null, null, 60);
            }) { text = "● Record" };
            recordBtn.style.color = new StyleColor(new Color(1f, 0.3f, 0.3f));

            var stopBtn = new Button(() =>
            {
                var engine = RecordingTool.GetEngine();
                if (engine.IsRecording)
                    engine.Stop();
            }) { text = "■ Stop" };

            var markBtn = new Button(() =>
            {
                var engine = RecordingTool.GetEngine();
                if (engine.IsRecording)
                    engine.InsertMarker("manual_marker");
            }) { text = "⚑ Mark" };

            btnRow.Add(recordBtn);
            btnRow.Add(stopBtn);
            btnRow.Add(markBtn);
            container.Add(btnRow);

            _recordingStatusLabel = new Label("Idle");
            _recordingStatusLabel.style.marginTop = 2;
            _recordingStatusLabel.style.fontSize  = 10;
            container.Add(_recordingStatusLabel);

            container.Add(new Label("Recordings:") { style = { marginTop = 4, fontSize = 10, unityFontStyleAndWeight = FontStyle.Italic } });
            _clipsScroll = new ScrollView { style = { maxHeight = 100 } };
            container.Add(_clipsScroll);

            return container;
        }

        // -----------------------------------------------------------------------
        // Refresh
        // -----------------------------------------------------------------------

        private void OnUpdate()
        {
            if (EditorApplication.timeSinceStartup - _lastRefreshTime >= RefreshInterval)
            {
                _lastRefreshTime = EditorApplication.timeSinceStartup;
                RefreshAll();
            }
        }

        private void RefreshAll()
        {
            RefreshStatusBar();
            RefreshWatches();
            RefreshActivity();
            RefreshRecording();
        }

        private void RefreshStatusBar()
        {
            if (_statusLabel == null) return;

            bool running = TheatreServer.IsRunning;
            string dot   = running ? "●" : "○";
            string state = running ? "Running" : "Stopped";
            string port  = running ? $" | http://localhost:{TheatreConfig.Port}" : string.Empty;
            _statusLabel.text  = $"{dot} {state}{port}";
            _statusLabel.style.color = new StyleColor(running ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.3f, 0.3f));

            bool connected = TheatreServer.IsClientConnected;
            _agentLabel.text  = connected ? "Agent Connected" : "Agent Disconnected";
            _agentLabel.style.color = new StyleColor(connected ? new Color(0.3f, 1f, 0.3f) : new Color(0.7f, 0.7f, 0.7f));
        }

        private void RefreshWatches()
        {
            if (_watchesScroll == null) return;
            _watchesScroll.Clear();

            try
            {
                var engine = WatchTool.GetEngine();
                var all    = engine.ListAll();
                if (all.Count == 0)
                {
                    _watchesScroll.Add(new Label("No active watches.") { style = { fontSize = 10, color = new StyleColor(new Color(0.6f, 0.6f, 0.6f)) } });
                    return;
                }

                foreach (var item in all)
                {
                    var entry = item as JObject;
                    if (entry == null) continue;

                    var watchId      = entry["watch_id"]?.Value<string>() ?? "?";
                    var target       = entry["target"]?.Value<string>()   ?? "?";
                    var label        = entry["label"]?.Value<string>();
                    var triggerCount = entry["trigger_count"]?.Value<int>() ?? 0;
                    var condType     = entry["condition"]?["type"]?.Value<string>();

                    var text = $"{watchId}  {(label != null ? $"\"{label}\"  " : "")}{target}" +
                               $"{(condType != null ? $"  [{condType}]" : "")}  triggers:{triggerCount}";

                    _watchesScroll.Add(new Label(text) { style = { fontSize = 10 } });
                }
            }
            catch
            {
                _watchesScroll.Add(new Label("Watch engine unavailable.") { style = { fontSize = 10 } });
            }
        }

        private void RefreshActivity()
        {
            if (_activityScroll == null) return;

            var log = TheatreServer.ActivityLog;
            if (log == null) return;

            int count = log.Entries.Count;
            if (count == _lastActivityCount) return; // nothing new
            _lastActivityCount = count;

            _activityScroll.Clear();

            foreach (var entry in log.Entries)
            {
                var toolName  = entry.ToolName ?? string.Empty;
                var operation = entry.Operation != null ? $" / {entry.Operation}" : string.Empty;
                var text      = $"{entry.Timestamp}  {toolName}{operation}  ~{entry.ResponseTokens}tok";

                var lbl = new Label(text);
                lbl.style.fontSize = 10;

                if (entry.IsError)
                    lbl.style.color = new StyleColor(new Color(1f, 0.4f, 0.4f));
                else if (IsDirectorTool(toolName))
                    lbl.style.color = new StyleColor(new Color(0.4f, 1f, 0.5f));
                else if (IsWatchTool(toolName))
                    lbl.style.color = new StyleColor(new Color(1f, 0.9f, 0.3f));
                else
                    lbl.style.color = new StyleColor(new Color(0.5f, 0.8f, 1f));

                _activityScroll.Add(lbl);
            }

            if (_sessionStatsLabel != null)
                _sessionStatsLabel.text = $"Session: {log.TotalCalls} calls | {log.TotalTokens:N0} tokens";
        }

        private void RefreshRecording()
        {
            if (_recordingStatusLabel == null) return;

            try
            {
                var engine = RecordingTool.GetEngine();
                if (engine.IsRecording && engine.ActiveState != null)
                {
                    var state    = engine.ActiveState;
                    var elapsed  = (float)(Time.realtimeSinceStartup - state.StartTime);
                    _recordingStatusLabel.text = $"Recording — {elapsed:F1}s, {state.FrameCounter} frames";
                    _recordingStatusLabel.style.color = new StyleColor(new Color(1f, 0.4f, 0.4f));
                }
                else
                {
                    _recordingStatusLabel.text = "Idle";
                    _recordingStatusLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
                }
            }
            catch
            {
                _recordingStatusLabel.text = "Recording engine unavailable.";
            }

            RefreshClips();
        }

        private void RefreshClips()
        {
            if (_clipsScroll == null) return;
            _clipsScroll.Clear();

            try
            {
                var clips = RecordingPersistence.RestoreClipIndex();
                if (clips == null || clips.Count == 0)
                {
                    _clipsScroll.Add(new Label("No recordings.") { style = { fontSize = 10, color = new StyleColor(new Color(0.6f, 0.6f, 0.6f)) } });
                    return;
                }

                foreach (var clip in clips)
                {
                    var text = $"{clip.ClipId}  \"{clip.Label}\"  {clip.Scene}";
                    _clipsScroll.Add(new Label(text) { style = { fontSize = 10 } });
                }
            }
            catch
            {
                _clipsScroll.Add(new Label("Could not load recordings.") { style = { fontSize = 10 } });
            }
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private void CopyUrlToClipboard()
        {
            if (TheatreServer.IsRunning)
            {
                EditorGUIUtility.systemCopyBuffer = $"http://localhost:{TheatreConfig.Port}";
                Debug.Log($"[Theatre] Copied URL to clipboard: http://localhost:{TheatreConfig.Port}");
            }
        }

        private static bool IsDirectorTool(string name) =>
            name.StartsWith("scene_op", StringComparison.Ordinal)
            || name.StartsWith("prefab_op", StringComparison.Ordinal)
            || name.StartsWith("material_op", StringComparison.Ordinal)
            || name.StartsWith("texture_op", StringComparison.Ordinal)
            || name.StartsWith("audio_mixer_op", StringComparison.Ordinal)
            || name.StartsWith("animation_clip_op", StringComparison.Ordinal)
            || name.StartsWith("animator_controller_op", StringComparison.Ordinal)
            || name.StartsWith("blend_tree_op", StringComparison.Ordinal)
            || name.StartsWith("timeline_op", StringComparison.Ordinal)
            || name.StartsWith("tilemap_op", StringComparison.Ordinal)
            || name.StartsWith("navmesh_op", StringComparison.Ordinal)
            || name.StartsWith("scriptable_object_op", StringComparison.Ordinal)
            || name.StartsWith("physics_material_op", StringComparison.Ordinal)
            || name.StartsWith("sprite_atlas_op", StringComparison.Ordinal)
            || name.StartsWith("render_pipeline_op", StringComparison.Ordinal)
            || name.StartsWith("lighting_op", StringComparison.Ordinal)
            || name.StartsWith("quality_op", StringComparison.Ordinal)
            || name.StartsWith("project_settings_op", StringComparison.Ordinal)
            || name.StartsWith("build_profile_op", StringComparison.Ordinal)
            || name.StartsWith("input_action_op", StringComparison.Ordinal);

        private static bool IsWatchTool(string name) =>
            name == "watch" || name.StartsWith("watch_", StringComparison.Ordinal);
    }
}
