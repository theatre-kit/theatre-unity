using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using Theatre.Editor.Tools.Recording;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// Keyboard shortcuts for Theatre operations.
    /// Registered via Unity's ShortcutManager — rebindable under Edit > Shortcuts.
    /// </summary>
    public static class TheatreShortcuts
    {
        /// <summary>
        /// Toggle recording on/off. Default binding: F8.
        /// </summary>
        [Shortcut("Theatre/Toggle Recording", KeyCode.F8)]
        public static void ToggleRecording()
        {
            var engine = RecordingTool.GetEngine();
            if (engine.IsRecording)
                engine.Stop();
            else
                engine.Start("keyboard_recording", null, null, 60);
        }

        /// <summary>
        /// Insert a marker into the active recording. Default binding: F9.
        /// </summary>
        [Shortcut("Theatre/Insert Marker", KeyCode.F9)]
        public static void InsertMarker()
        {
            var engine = RecordingTool.GetEngine();
            if (engine.IsRecording)
                engine.InsertMarker("keyboard_marker");
        }

        /// <summary>
        /// Open the Theatre panel. Default binding: Ctrl+Shift+T (Cmd+Shift+T on macOS).
        /// </summary>
        [Shortcut("Theatre/Open Panel", KeyCode.T,
            ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        public static void OpenPanel()
        {
            TheatreWindow.ShowWindow();
        }
    }
}
