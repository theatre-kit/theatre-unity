using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// Registers Theatre settings under Project Settings > Theatre.
    /// </summary>
    public class TheatreSettingsProvider : SettingsProvider
    {
        private const string SettingsPath = "Project/Theatre";

        public TheatreSettingsProvider(string path, SettingsScope scope)
            : base(path, scope) { }

        /// <summary>Register this provider with the Unity Settings window.</summary>
        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new TheatreSettingsProvider(SettingsPath, SettingsScope.Project);
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            rootElement.style.paddingLeft   = 8;
            rootElement.style.paddingRight  = 8;
            rootElement.style.paddingTop    = 8;
            rootElement.style.paddingBottom = 8;

            // --- Server section ---
            rootElement.Add(MakeSectionHeader("Server"));

            var portField = new IntegerField("Port") { value = TheatreConfig.Port };
            portField.style.maxWidth = 200;
            portField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue > 0 && evt.newValue <= 65535)
                    TheatreConfig.Port = evt.newValue;
            });
            rootElement.Add(portField);

            // --- Tool Groups section ---
            rootElement.Add(MakeSectionHeader("Tool Groups"));
            rootElement.Add(BuildToolGroupToggles());

            // --- Recording section ---
            rootElement.Add(MakeSectionHeader("Recording"));

            var captureRateField = new IntegerField("Default Capture Rate (fps)") { value = 60 };
            captureRateField.style.maxWidth = 260;
            rootElement.Add(captureRateField);

            var storagePathField = new TextField("Storage Path") { value = "Library/Theatre/" };
            storagePathField.style.maxWidth = 320;
            rootElement.Add(storagePathField);
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static Label MakeSectionHeader(string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            label.style.marginTop    = 8;
            label.style.marginBottom = 4;
            label.style.borderBottomWidth = 1;
            label.style.borderBottomColor = new StyleColor(new UnityEngine.Color(0.35f, 0.35f, 0.35f));
            return label;
        }

        private static VisualElement BuildToolGroupToggles()
        {
            var container = new VisualElement();
            var groups    = TheatreConfig.EnabledGroups;

            var stageRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            stageRow.Add(MakeGroupToggle("StageGameObject",  ToolGroup.StageGameObject));
            stageRow.Add(MakeGroupToggle("StageQuery",       ToolGroup.StageQuery));
            stageRow.Add(MakeGroupToggle("StageWatch",       ToolGroup.StageWatch));
            stageRow.Add(MakeGroupToggle("StageAction",      ToolGroup.StageAction));
            stageRow.Add(MakeGroupToggle("StageRecording",   ToolGroup.StageRecording));
            container.Add(new Label("Stage") { style = { unityFontStyleAndWeight = UnityEngine.FontStyle.Italic } });
            container.Add(stageRow);

            var ecsRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            ecsRow.Add(MakeGroupToggle("ECSWorld",   ToolGroup.ECSWorld));
            ecsRow.Add(MakeGroupToggle("ECSEntity",  ToolGroup.ECSEntity));
            ecsRow.Add(MakeGroupToggle("ECSQuery",   ToolGroup.ECSQuery));
            ecsRow.Add(MakeGroupToggle("ECSAction",  ToolGroup.ECSAction));
            container.Add(new Label("ECS") { style = { unityFontStyleAndWeight = UnityEngine.FontStyle.Italic } });
            container.Add(ecsRow);

            var dirRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            dirRow.Add(MakeGroupToggle("DirectorScene",   ToolGroup.DirectorScene));
            dirRow.Add(MakeGroupToggle("DirectorPrefab",  ToolGroup.DirectorPrefab));
            dirRow.Add(MakeGroupToggle("DirectorAsset",   ToolGroup.DirectorAsset));
            dirRow.Add(MakeGroupToggle("DirectorAnim",    ToolGroup.DirectorAnim));
            dirRow.Add(MakeGroupToggle("DirectorSpatial", ToolGroup.DirectorSpatial));
            dirRow.Add(MakeGroupToggle("DirectorInput",   ToolGroup.DirectorInput));
            dirRow.Add(MakeGroupToggle("DirectorConfig",  ToolGroup.DirectorConfig));
            container.Add(new Label("Director") { style = { unityFontStyleAndWeight = UnityEngine.FontStyle.Italic } });
            container.Add(dirRow);

            // Presets
            var presets  = new List<string> { "GameObject Project", "ECS Project", "Stage Only", "Director Only", "Everything" };
            var dropdown = new DropdownField("Preset", presets, 0);
            dropdown.style.maxWidth = 280;
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

        private static Toggle MakeGroupToggle(string label, ToolGroup flag)
        {
            var toggle = new Toggle(label);
            toggle.value      = (TheatreConfig.EnabledGroups & flag) != 0;
            toggle.style.minWidth = 140;
            var capturedFlag  = flag;
            toggle.RegisterValueChangedCallback(evt =>
            {
                var g = TheatreConfig.EnabledGroups;
                if (evt.newValue) g |= capturedFlag;
                else              g &= ~capturedFlag;
                TheatreServer.SetEnabledGroups(g);
            });
            return toggle;
        }
    }
}
