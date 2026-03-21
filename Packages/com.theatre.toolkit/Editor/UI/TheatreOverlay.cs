using Theatre.Editor.Tools.Recording;
using Theatre.Editor.Tools.Watch;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// Compact overlay in the Scene View corner showing Theatre status.
    /// Displays server running state, active watch count, and recording indicator.
    /// </summary>
    [Overlay(typeof(SceneView), "Theatre", defaultDisplay = true)]
    public class TheatreOverlay : Overlay
    {
        /// <summary>Create the panel content for the overlay.</summary>
        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.style.minWidth = 150;

            var status = new Label("● Server Running");
            status.schedule.Execute(() => UpdateStatus(status)).Every(2000);
            root.Add(status);

            var watches = new Label("Watches: 0 active");
            watches.schedule.Execute(() => UpdateWatches(watches)).Every(2000);
            root.Add(watches);

            var recording = new Label("");
            recording.schedule.Execute(() => UpdateRecording(recording)).Every(1000);
            root.Add(recording);

            // Run initial updates immediately
            UpdateStatus(status);
            UpdateWatches(watches);
            UpdateRecording(recording);

            return root;
        }

        private static void UpdateStatus(Label label)
        {
            if (TheatreServer.IsRunning)
                label.text = "● Server Running";
            else
                label.text = "● Server Stopped";
        }

        private static void UpdateWatches(Label label)
        {
            var count = WatchTool.GetEngine().Count;
            label.text = $"Watches: {count} active";
        }

        private static void UpdateRecording(Label label)
        {
            var engine = RecordingTool.GetEngine();
            if (engine.IsRecording)
                label.text = "Recording ●";
            else
                label.text = "";
        }
    }
}
