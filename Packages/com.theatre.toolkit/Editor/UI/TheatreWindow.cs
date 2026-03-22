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
    /// Main Theatre EditorWindow. Uses proper UI Toolkit patterns to display
    /// server status, per-tool toggles with descriptions, active watches,
    /// agent activity feed, and recording controls.
    /// </summary>
    public class TheatreWindow : EditorWindow
    {
        // -----------------------------------------------------------------------
        // Static data — tool descriptions and group definitions
        // -----------------------------------------------------------------------

        private static readonly Dictionary<string, string> s_toolDescriptions = new()
        {
            ["scene_snapshot"]            = "Budgeted spatial overview of GameObjects with positions and clustering.",
            ["scene_hierarchy"]           = "Navigate the Transform hierarchy. List, find, search, and path operations.",
            ["scene_inspect"]             = "Deep inspection of a single GameObject — all components and serialized properties.",
            ["scene_delta"]               = "Report what changed since a previous frame or query.",
            ["spatial_query"]             = "Spatial questions: nearest, radius, overlap, raycast, linecast, path distance, bounds.",
            ["watch"]                     = "Subscribe to changes or conditions. Triggers via SSE when conditions are met.",
            ["action"]                    = "Manipulate game state: teleport, set property, enable/disable, timescale, pause/step.",
            ["recording"]                 = "Frame-by-frame capture to SQLite. Query ranges, diff frames, analyze thresholds.",
            ["scene_op"]                  = "Create and modify scenes and GameObjects with Undo support.",
            ["prefab_op"]                 = "Full prefab lifecycle: create, instantiate, overrides, unpack, variants.",
            ["batch"]                     = "Execute multiple Director operations as a single atomic unit.",
            ["material_op"]               = "Create and modify materials — shader properties, colors, textures.",
            ["scriptable_object_op"]      = "Create and modify ScriptableObject instances.",
            ["physics_material_op"]       = "Create PhysicsMaterial (3D) or PhysicsMaterial2D assets.",
            ["texture_op"]                = "Configure texture import settings and sprite setup.",
            ["sprite_atlas_op"]           = "Create and manage SpriteAtlas assets.",
            ["audio_mixer_op"]            = "Create AudioMixer hierarchies with groups, effects, and snapshots.",
            ["render_pipeline_op"]        = "Create URP/HDRP pipeline assets and renderer data.",
            ["addressable_op"]            = "Manage Addressable groups, entries, labels, and analysis.",
            ["animation_clip_op"]         = "Create AnimationClips with curves, keyframes, and events.",
            ["animator_controller_op"]    = "Build AnimatorControllers with states, transitions, and parameters.",
            ["blend_tree_op"]             = "Create and configure blend trees within AnimatorControllers.",
            ["timeline_op"]               = "Create Timeline assets with tracks, clips, and bindings.",
            ["tilemap_op"]                = "Paint and manage 2D Tilemap assets.",
            ["navmesh_op"]                = "Configure and bake NavMesh — areas, modifiers, links, surfaces.",
            ["terrain_op"]                = "Create and sculpt terrain — heightmaps, textures, trees, details.",
            ["probuilder_op"]             = "Create and edit ProBuilder meshes — shapes, extrude, boolean, export.",
            ["input_action_op"]           = "Create Input System action maps, actions, bindings, and composites.",
            ["lighting_op"]               = "Configure ambient light, fog, skybox, probes, and lightmap baking.",
            ["quality_op"]                = "Switch quality levels and configure shadow/rendering settings.",
            ["project_settings_op"]       = "Configure physics, time, player settings, tags, and layers.",
            ["build_profile_op"]          = "Create and configure Build Profiles for different platforms.",
            ["ecs_world"]                 = "Overview of active ECS Worlds — entity counts, archetypes, systems.",
            ["ecs_snapshot"]              = "Spatial overview of ECS entities with LocalTransform components.",
            ["ecs_inspect"]               = "Deep inspection of a single entity's component data.",
            ["ecs_query"]                 = "Spatial queries on ECS entities: nearest, radius, AABB overlap.",
            ["ecs_action"]                = "Modify entity component data — set, add, remove, create, destroy.",
            ["theatre_status"]            = "Server health — status, enabled groups, Unity version, play mode.",
            ["unity_console"]             = "Read Unity Console with filtering, grep, dedup, and rollup.",
            ["unity_tests"]               = "Run EditMode/PlayMode tests and retrieve results via MCP.",
        };

        // (groupName, groupFlag(s), tool names)
        // Using ValueTuple — ToolGroup flag may be a combination for multi-flag groups.
        private static readonly (string groupName, ToolGroup flag, string[] tools)[] s_groups =
        {
            ("Scene Awareness", ToolGroup.StageGameObject,
                new[] { "scene_snapshot", "scene_hierarchy", "scene_inspect", "scene_delta", "theatre_status" }),
            ("Spatial Queries", ToolGroup.StageQuery,
                new[] { "spatial_query" }),
            ("Watches",         ToolGroup.StageWatch,
                new[] { "watch" }),
            ("Actions",         ToolGroup.StageAction,
                new[] { "action" }),
            ("Recording",       ToolGroup.StageRecording,
                new[] { "recording" }),
            ("ECS World",       ToolGroup.ECSWorld,
                new[] { "ecs_world", "ecs_snapshot" }),
            ("ECS Entity",      ToolGroup.ECSEntity,
                new[] { "ecs_inspect" }),
            ("ECS Query",       ToolGroup.ECSQuery,
                new[] { "ecs_query" }),
            ("ECS Action",      ToolGroup.ECSAction,
                new[] { "ecs_action" }),
            ("Scene & Prefabs", ToolGroup.DirectorScene | ToolGroup.DirectorPrefab,
                new[] { "scene_op", "prefab_op", "batch" }),
            ("Assets",          ToolGroup.DirectorAsset,
                new[] { "material_op", "scriptable_object_op", "physics_material_op",
                        "texture_op", "sprite_atlas_op", "audio_mixer_op",
                        "render_pipeline_op", "addressable_op" }),
            ("Animation",       ToolGroup.DirectorAnim,
                new[] { "animation_clip_op", "animator_controller_op", "blend_tree_op", "timeline_op" }),
            ("Spatial Building",ToolGroup.DirectorSpatial,
                new[] { "tilemap_op", "navmesh_op", "terrain_op", "probuilder_op" }),
            ("Input & Config",  ToolGroup.DirectorInput | ToolGroup.DirectorConfig,
                new[] { "input_action_op", "lighting_op", "quality_op",
                        "project_settings_op", "build_profile_op" }),
            ("Utilities",       ToolGroup.StageGameObject,
                new[] { "unity_console", "unity_tests" }),
        };

        // -----------------------------------------------------------------------
        // Periodic refresh
        // -----------------------------------------------------------------------

        private double _lastRefreshTime;
        private const double RefreshInterval = 2.0;

        // -----------------------------------------------------------------------
        // UI element references (updated by RefreshAll)
        // -----------------------------------------------------------------------

        private VisualElement _statusDot;
        private Label         _statusText;
        private Label         _agentLabel;
        private ScrollView    _watchesScroll;
        private ScrollView    _activityScroll;
        private Label         _sessionStatsLabel;
        private Label         _recordingStatusLabel;
        private ScrollView    _clipsScroll;

        // Avoid rebuilding activity list when nothing changed
        private int _lastActivityCount;

        // -----------------------------------------------------------------------
        // Entry point
        // -----------------------------------------------------------------------

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

        // -----------------------------------------------------------------------
        // CreateGUI — full rewrite
        // -----------------------------------------------------------------------

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft   = 6;
            root.style.paddingRight  = 6;
            root.style.paddingTop    = 4;
            root.style.paddingBottom = 6;

            // Outer ScrollView so the entire window scrolls
            var outerScroll = new ScrollView(ScrollViewMode.Vertical);
            outerScroll.viewDataKey = "theatre_outer_scroll";
            outerScroll.style.flexGrow = 1;
            root.Add(outerScroll);

            var content = outerScroll.contentContainer;

            // ── Section 1: Server Status ──────────────────────────────────────
            content.Add(MakeSectionHeader("Server"));
            content.Add(BuildStatusBar());

            // ── Section 2: Tool Groups ────────────────────────────────────────
            content.Add(MakeSectionHeader("Tool Groups"));
            content.Add(BuildPresetRow());
            content.Add(BuildToolGroups());

            // ── Section 3: Active Watches ─────────────────────────────────────
            content.Add(MakeSectionHeader("Active Watches"));
            _watchesScroll = new ScrollView(ScrollViewMode.Vertical);
            _watchesScroll.viewDataKey = "theatre_watches_scroll";
            _watchesScroll.style.maxHeight = 120;
            content.Add(_watchesScroll);

            // ── Section 4: Activity Feed ──────────────────────────────────────
            content.Add(MakeSectionHeader("Agent Activity"));
            _activityScroll = new ScrollView(ScrollViewMode.Vertical);
            _activityScroll.viewDataKey = "theatre_activity_scroll";
            _activityScroll.style.maxHeight = 160;
            _activityScroll.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.2f));
            content.Add(_activityScroll);

            _sessionStatsLabel = new Label();
            _sessionStatsLabel.style.marginTop  = 2;
            _sessionStatsLabel.style.fontSize   = 10;
            _sessionStatsLabel.style.color      = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
            content.Add(_sessionStatsLabel);

            // ── Section 5: Recording ──────────────────────────────────────────
            content.Add(MakeSectionHeader("Recording"));
            content.Add(BuildRecordingSection());

            // Initial populate
            RefreshAll();
        }

        // -----------------------------------------------------------------------
        // Section builders
        // -----------------------------------------------------------------------

        private VisualElement BuildStatusBar()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.flexWrap      = Wrap.Wrap;
            row.style.marginBottom  = 4;

            // Colored dot — plain Label, no Toggle
            _statusDot = new VisualElement();
            _statusDot.style.width        = 10;
            _statusDot.style.height       = 10;
            _statusDot.style.borderTopLeftRadius     = 5;
            _statusDot.style.borderTopRightRadius    = 5;
            _statusDot.style.borderBottomLeftRadius  = 5;
            _statusDot.style.borderBottomRightRadius = 5;
            _statusDot.style.marginRight  = 5;
            _statusDot.style.flexShrink   = 0;
            row.Add(_statusDot);

            _statusText = new Label();
            _statusText.style.flexGrow = 1;
            row.Add(_statusText);

            var copyBtn = new Button(CopyUrlToClipboard) { text = "Copy URL" };
            copyBtn.style.fontSize = 10;
            row.Add(copyBtn);

            _agentLabel = new Label();
            _agentLabel.style.marginLeft = 8;
            row.Add(_agentLabel);

            return row;
        }

        private static VisualElement BuildPresetRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 4;

            var label = new Label("Preset:");
            label.style.fontSize    = 10;
            label.style.marginRight = 6;
            label.style.flexShrink  = 0;
            row.Add(label);

            var presets = new List<string>
                { "GameObject Project", "ECS Project", "Stage Only", "Director Only", "Everything" };
            var current = TheatreConfig.EnabledGroups;
            int selectedIndex = current == ToolGroup.GameObjectProject ? 0 :
                                current == ToolGroup.ECSProject ? 1 :
                                current == ToolGroup.StageAll ? 2 :
                                current == ToolGroup.DirectorAll ? 3 :
                                current == ToolGroup.Everything ? 4 : 0;
            var dropdown = new DropdownField(presets, selectedIndex);
            dropdown.style.flexGrow   = 1;
            dropdown.style.maxWidth   = 220;
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
                // Clear per-tool overrides when selecting a preset
                TheatreConfig.DisabledTools.Clear();
                TheatreServer.SseManager?.NotifyToolsChanged();
                // Rebuild the window to reflect new toggle states
                var window = GetWindow<TheatreWindow>();
                if (window != null)
                {
                    window.rootVisualElement.Clear();
                    window.CreateGUI();
                }
            });
            row.Add(dropdown);

            return row;
        }

        private VisualElement BuildToolGroups()
        {
            var container = new VisualElement();

            // Top-level category foldouts
            var stageFoldout    = MakeCategoryFoldout("Stage",    "theatre_foldout_stage",    true);
            var ecsFoldout      = MakeCategoryFoldout("ECS",      "theatre_foldout_ecs",      false);
            var directorFoldout = MakeCategoryFoldout("Director", "theatre_foldout_director", false);

            var enabledGroups = TheatreConfig.EnabledGroups;
            var disabledTools = TheatreConfig.DisabledTools;

            foreach (var (groupName, flag, tools) in s_groups)
            {
                var parentFoldout = IsStageGroup(flag)    ? stageFoldout
                                  : IsECSGroup(flag)      ? ecsFoldout
                                                          : directorFoldout;

                var groupSection = BuildGroupSection(groupName, flag, tools, enabledGroups, disabledTools);
                parentFoldout.Add(groupSection);
            }

            container.Add(stageFoldout);
            container.Add(ecsFoldout);
            container.Add(directorFoldout);
            return container;
        }

        /// <summary>
        /// Builds one sub-group: a group-enable toggle header + per-tool rows.
        /// </summary>
        private static VisualElement BuildGroupSection(
            string groupName,
            ToolGroup flag,
            string[] tools,
            ToolGroup enabledGroups,
            HashSet<string> disabledTools)
        {
            var section = new VisualElement();
            section.style.marginBottom = 6;
            section.style.marginLeft   = 2;

            // ── Group header: [toggle] GroupName ─────────────────────────────
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems    = Align.Center;
            header.style.marginBottom  = 2;

            var groupToggle = new Toggle();
            groupToggle.style.flexShrink = 0;
            groupToggle.style.width      = 18;
            groupToggle.style.marginTop  = 0;
            groupToggle.value            = (enabledGroups & flag) != 0;
            groupToggle.tooltip          = $"Enable/disable all {groupName} tools";
            var capturedFlag = flag;
            groupToggle.RegisterValueChangedCallback(evt =>
            {
                var g = TheatreConfig.EnabledGroups;
                if (evt.newValue) g |= capturedFlag;
                else              g &= ~capturedFlag;
                TheatreServer.SetEnabledGroups(g);
            });
            header.Add(groupToggle);

            var groupLabel = new Label(groupName);
            groupLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            groupLabel.style.fontSize  = 11;
            groupLabel.style.marginLeft = 4;
            header.Add(groupLabel);

            section.Add(header);

            // ── Per-tool rows ─────────────────────────────────────────────────
            var toolList = new VisualElement();
            toolList.style.marginLeft = 4;

            foreach (var toolName in tools)
            {
                toolList.Add(BuildToolRow(toolName, disabledTools));
            }

            section.Add(toolList);
            return section;
        }

        /// <summary>
        /// One tool row: bare checkbox + right column (bold name + gray description).
        /// </summary>
        private static VisualElement BuildToolRow(string toolName, HashSet<string> disabledTools)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.FlexStart;
            row.style.marginBottom  = 3;

            // Bare checkbox — Toggle() with no label
            var toggle = new Toggle();
            toggle.style.flexShrink = 0;
            toggle.style.width      = 18;
            toggle.style.marginTop  = 2;
            toggle.value            = !disabledTools.Contains(toolName);
            var captured = toolName;
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                    TheatreConfig.DisabledTools.Remove(captured);
                else
                    TheatreConfig.DisabledTools.Add(captured);
                TheatreServer.SseManager?.NotifyToolsChanged();
            });
            row.Add(toggle);

            // Right column: name + description
            var textCol = new VisualElement();
            textCol.style.flexDirection = FlexDirection.Column;
            textCol.style.flexGrow      = 1;
            textCol.style.flexShrink    = 1;
            textCol.style.minWidth      = 0;   // CRITICAL — allows shrinking below content size
            textCol.style.marginLeft    = 4;

            var nameLabel = new Label(toolName);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.fontSize = 11;
            textCol.Add(nameLabel);

            if (s_toolDescriptions.TryGetValue(toolName, out var desc))
            {
                var descLabel = new Label(desc);
                descLabel.style.fontSize    = 10;
                descLabel.style.color       = new StyleColor(new Color(0.55f, 0.55f, 0.55f));
                descLabel.style.whiteSpace  = WhiteSpace.Normal;   // allow text wrap
                descLabel.style.overflow    = Overflow.Visible;
                textCol.Add(descLabel);
            }

            row.Add(textCol);
            return row;
        }

        private VisualElement BuildRecordingSection()
        {
            var container = new VisualElement();

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginBottom  = 4;

            var recordBtn = new Button(() =>
            {
                var engine = RecordingTool.GetEngine();
                if (!engine.IsRecording)
                    engine.Start("manual", null, null, 60);
            }) { text = "● Record" };
            recordBtn.style.color = new StyleColor(new Color(1f, 0.35f, 0.35f));

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
            _recordingStatusLabel.style.fontSize = 10;
            container.Add(_recordingStatusLabel);

            var clipsHeader = new Label("Recordings:");
            clipsHeader.style.marginTop = 4;
            clipsHeader.style.fontSize  = 10;
            clipsHeader.style.unityFontStyleAndWeight = FontStyle.Italic;
            container.Add(clipsHeader);

            _clipsScroll = new ScrollView(ScrollViewMode.Vertical);
            _clipsScroll.viewDataKey    = "theatre_clips_scroll";
            _clipsScroll.style.maxHeight = 100;
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
            if (_statusDot == null || _statusText == null) return;

            bool running   = TheatreServer.IsRunning;
            bool connected = TheatreServer.IsClientConnected;

            // Dot color
            _statusDot.style.backgroundColor = new StyleColor(
                running ? new Color(0.25f, 1f, 0.25f) : new Color(1f, 0.3f, 0.3f));

            // Status text
            string state = running ? "Running" : "Stopped";
            string port  = running ? $"  |  http://localhost:{TheatreConfig.Port}" : string.Empty;
            _statusText.text  = $"{state}{port}";
            _statusText.style.color = new StyleColor(
                running ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.7f, 0.4f, 0.4f));

            // Agent label
            _agentLabel.text  = connected ? "Agent Connected" : "Disconnected";
            _agentLabel.style.color = new StyleColor(
                connected ? new Color(0.3f, 1f, 0.3f) : new Color(0.55f, 0.55f, 0.55f));
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
                    _watchesScroll.Add(MakeDimLabel("No active watches."));
                    return;
                }

                foreach (var item in all)
                {
                    if (item is not JObject entry) continue;

                    var watchId      = entry["watch_id"]?.Value<string>() ?? "?";
                    var target       = entry["target"]?.Value<string>()   ?? "?";
                    var label        = entry["label"]?.Value<string>();
                    var triggerCount = entry["trigger_count"]?.Value<int>() ?? 0;
                    var condType     = entry["condition"]?["type"]?.Value<string>();

                    var text = $"{watchId}  {(label != null ? $"\"{label}\"  " : "")}{target}" +
                               $"{(condType != null ? $"  [{condType}]" : "")}  triggers:{triggerCount}";

                    var lbl = new Label(text);
                    lbl.style.fontSize = 10;
                    _watchesScroll.Add(lbl);
                }
            }
            catch
            {
                _watchesScroll.Add(MakeDimLabel("Watch engine unavailable."));
            }
        }

        private void RefreshActivity()
        {
            if (_activityScroll == null) return;

            var log = TheatreServer.ActivityLog;
            if (log == null) return;

            int count = log.Entries.Count;
            if (count == _lastActivityCount) return;
            _lastActivityCount = count;

            _activityScroll.Clear();

            foreach (var entry in log.Entries)
            {
                var toolName  = entry.ToolName ?? string.Empty;
                var operation = entry.Operation != null ? $" / {entry.Operation}" : string.Empty;
                var text      = $"{entry.Timestamp}  {toolName}{operation}  ~{entry.ResponseTokens}tok";

                var lbl = new Label(text);
                lbl.style.fontSize  = 10;
                lbl.style.paddingLeft   = 2;
                lbl.style.paddingRight  = 2;
                lbl.style.paddingTop    = 1;
                lbl.style.paddingBottom = 1;

                Color c;
                if (entry.IsError)
                    c = new Color(1f, 0.4f, 0.4f);
                else if (IsDirectorTool(toolName))
                    c = new Color(0.4f, 1f, 0.5f);
                else if (IsWatchTool(toolName))
                    c = new Color(1f, 0.9f, 0.3f);
                else
                    c = new Color(0.5f, 0.8f, 1f);

                lbl.style.color = new StyleColor(c);
                _activityScroll.Add(lbl);
            }

            // Scroll to bottom
            _activityScroll.scrollOffset = new Vector2(0, float.MaxValue);

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
                    var state   = engine.ActiveState;
                    var elapsed = (float)(Time.realtimeSinceStartup - state.StartTime);
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
                _recordingStatusLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
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
                    _clipsScroll.Add(MakeDimLabel("No recordings."));
                    return;
                }

                foreach (var clip in clips)
                {
                    var text = $"{clip.ClipId}  \"{clip.Label}\"  {clip.Scene}";
                    var lbl  = new Label(text);
                    lbl.style.fontSize = 10;
                    _clipsScroll.Add(lbl);
                }
            }
            catch
            {
                _clipsScroll.Add(MakeDimLabel("Could not load recordings."));
            }
        }

        // -----------------------------------------------------------------------
        // UI helpers
        // -----------------------------------------------------------------------

        private static Label MakeSectionHeader(string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize      = 12;
            label.style.marginTop     = 8;
            label.style.marginBottom  = 4;
            label.style.paddingBottom = 2;
            label.style.borderBottomWidth = 1;
            label.style.borderBottomColor = new StyleColor(new Color(0.35f, 0.35f, 0.35f));
            return label;
        }

        private static Foldout MakeCategoryFoldout(string text, string viewDataKey, bool defaultOpen)
        {
            var foldout = new Foldout
            {
                text        = text,
                viewDataKey = viewDataKey,
                value       = defaultOpen,
            };
            foldout.style.marginBottom = 4;
            // Indent foldout content slightly less than Unity default
            var content = foldout.Q<VisualElement>(className: "unity-foldout__content");
            if (content != null)
                content.style.marginLeft = 6;
            return foldout;
        }

        private static Label MakeDimLabel(string text)
        {
            var lbl = new Label(text);
            lbl.style.fontSize = 10;
            lbl.style.color    = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
            return lbl;
        }

        // -----------------------------------------------------------------------
        // Group classification helpers
        // -----------------------------------------------------------------------

        private static bool IsStageGroup(ToolGroup flag) =>
            (flag & ToolGroup.StageAll) != 0 &&
            (flag & ~ToolGroup.StageAll) == 0;

        private static bool IsECSGroup(ToolGroup flag) =>
            (flag & ToolGroup.ECSAll) != 0 &&
            (flag & ~ToolGroup.ECSAll) == 0;

        // -----------------------------------------------------------------------
        // Activity log colour helpers
        // -----------------------------------------------------------------------

        private void CopyUrlToClipboard()
        {
            if (TheatreServer.IsRunning)
            {
                var url = $"http://localhost:{TheatreConfig.Port}";
                EditorGUIUtility.systemCopyBuffer = url;
                Debug.Log($"[Theatre] Copied URL to clipboard: {url}");
            }
        }

        private static bool IsDirectorTool(string name) =>
            name.EndsWith("_op",    StringComparison.Ordinal)
            || name == "batch";

        private static bool IsWatchTool(string name) =>
            name == "watch" || name.StartsWith("watch_", StringComparison.Ordinal);
    }
}
