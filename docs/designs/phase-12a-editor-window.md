# Design: Phase 12a — Editor Window & Settings

## Overview

The human-facing Theatre panel: an EditorWindow built with UI Toolkit
showing server status, tool group toggles, active watches, agent activity
feed, recording controls, and a recordings library. Plus a Project
Settings provider and keyboard shortcuts.

**Key difference from all previous phases**: This is visual UI, not MCP
tool handlers. Uses UI Toolkit (UXML + USS), `EditorWindow`,
`SettingsProvider`, and `ShortcutManager`.

---

## Architecture

```
Editor/
  UI/
    TheatreWindow.cs          — Main EditorWindow
    TheatreSettingsProvider.cs — Project Settings > Theatre
    ActivityLog.cs            — Ring buffer of agent tool calls
    TheatreShortcuts.cs       — Keyboard shortcut definitions
  UI/Resources/
    TheatreWindow.uxml        — UI layout
    TheatreWindow.uss         — Styles
```

---

## Implementation Units

### Unit 1: ActivityLog — Agent Activity Ring Buffer

**File**: `Packages/com.theatre.toolkit/Editor/UI/ActivityLog.cs`

**Namespace**: `Theatre.Editor.UI`

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Theatre.Editor.UI
{
    /// <summary>
    /// Ring buffer that records the last N MCP tool calls for display
    /// in the Theatre panel. Persisted to SessionState for domain
    /// reload survival.
    /// </summary>
    public sealed class ActivityLog
    {
        public const int MaxEntries = 100;
        private const string SessionKey = "Theatre_ActivityLog";

        /// <summary>A single tool call log entry.</summary>
        public struct Entry
        {
            public string Timestamp;    // HH:mm:ss
            public string ToolName;
            public string Operation;    // extracted from args if compound tool
            public int ResponseTokens;  // estimated from response length
            public bool IsError;
        }

        private readonly List<Entry> _entries = new();

        /// <summary>All entries, newest first.</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>Total tool calls in this session.</summary>
        public int TotalCalls { get; private set; }

        /// <summary>Total estimated tokens in this session.</summary>
        public int TotalTokens { get; private set; }

        /// <summary>Record a tool call.</summary>
        public void Record(string toolName, JToken args, string resultJson);

        /// <summary>Clear all entries.</summary>
        public void Clear();

        /// <summary>Persist to SessionState.</summary>
        public void Save();

        /// <summary>Restore from SessionState.</summary>
        public void Restore();
    }
}
```

**Implementation Notes**:
- `Record()` extracts `operation` from args if present (for compound tools)
- Estimates response tokens as `resultJson.Length / 4`
- Checks for `"error"` key in result to set `IsError`
- Inserts at index 0 (newest first), removes from end if > MaxEntries
- Persist/restore via `SessionState.SetString` with JSON serialization

**Hook**: The `ActivityLog.Record()` method needs to be called from `McpRouter`
or `TheatreServer` after every `tools/call` completes. Add a static
`ActivityLog` instance to `TheatreServer` and call `Record()` in
`ExecuteToolOnMainThread` after the handler returns.

---

### Unit 2: TheatreWindow — Main EditorWindow

### UI Framework

The Theatre panel MVP is built **programmatically via C# UIElements**
(no UXML/USS files). This is intentional:

- Faster iteration during development — no separate markup files
- Easier to keep in sync with data model changes
- UXML/USS can be adopted later for theming and layout refinement

The UX.md wireframes show the **target layout**, not a UXML
specification. The MVP implements the same sections (status bar, tool
groups, watches, activity feed, recordings) using C# `VisualElement`
construction.

### Timeline Scrubber

The timeline scrubber shown in UX.md is **Phase 12a scope** but
implemented as a **basic label-only version** for MVP:
- Shows duration, frame count, file size as text labels
- Interactive timeline with markers and seek is deferred to Phase 13
  polish (or a separate Phase 12c if needed)

### WelcomeDialog Deduplication

`WelcomeDialog` uses `EditorPrefs.GetBool("Theatre_WelcomeShown", false)`
to prevent showing twice. The `[InitializeOnLoadMethod]` callback checks
this flag before displaying. Since `EditorPrefs` persists across domain
reloads, the dialog shows exactly once per editor installation.

**File**: `Packages/com.theatre.toolkit/Editor/UI/TheatreWindow.cs`

**Namespace**: `Theatre.Editor.UI`

```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// Main Theatre EditorWindow. Displays server status, tool toggles,
    /// watches, activity feed, and recording controls.
    /// </summary>
    public class TheatreWindow : EditorWindow
    {
        [MenuItem("Window/Theatre", priority = 100)]
        public static void ShowWindow();

        private void CreateGUI();
        private void OnEnable();
        private void OnDisable();

        // Periodic refresh (status, watches, activity)
        private void Update();
    }
}
```

**UI Sections** (built programmatically via C# UIElements, no UXML file needed for MVP):

#### Status Bar
```
● Server Running  |  http://localhost:9078  |  Agent Connected
```
- Green dot when running, red when stopped
- URL is clickable (copies to clipboard)
- Agent status from `TheatreServer.IsClientConnected`

#### Tool Group Toggles
```
[■] Stage: GameObject  [■] Queries  [■] Watches  [■] Actions  [■] Recording
[ ] Stage: ECS
[■] Director: Scene  [■] Prefabs  [■] Assets  [■] Anim  [■] Spatial  [■] Input  [■] Config
                                                                      [Presets ▾]
```
- Toggle elements for each `ToolGroup` flag
- Presets dropdown: "GameObject Project", "ECS Project", "Stage Only", "Director Only", "Everything"
- Changes take effect immediately via `TheatreConfig.EnabledGroups`

#### Active Watches
```
● w_01  "enemy_hp"      /Enemies/Scout*  current_hp<25
● w_02  "player_near"   /Player          proximity<5.0
Triggers: 3 (last: 2s ago)
```
- Read from `WatchTool.GetEngine().ListAll()`
- Refresh every 2 seconds

#### Agent Activity Feed
```
14:22:01  scene_snapshot    focus=[0,1,0]  r=50   340tok
14:22:03  scene_inspect     /Player         full   820tok
14:22:05  spatial_query     nearest  n=5          210tok
```
- Scrolling list from `ActivityLog.Entries`
- Color-coded: blue for Stage, green for Director, yellow for Watch, red for errors
- Shows: timestamp, tool name, key params (abbreviated), token count

#### Recording Section
```
[● Record]  [■ Stop]  [⚑ Mark]        Status: Idle
⏱ --:--  |  -- frames  |  -- KB
```
- Record/Stop/Mark buttons control `RecordingTool.GetEngine()`
- Status: "Idle", "Recording", with live stats
- Below: recordings library list

#### Recordings Library
```
wall_clip_repro      12.4s   Today 14:10   847 KB  [×]
patrol_test_03        8.1s   Today 13:45   523 KB  [×]
```
- Read from `RecordingPersistence.RestoreClipIndex()`
- Delete button removes clip

#### Session Stats
```
Session: 14 calls  |  2,400 tokens  |  Connected 12m
```

**Implementation Notes**:
- Build UI programmatically using `VisualElement`, `Label`, `Button`, `Toggle`, `ScrollView`
- Use `EditorApplication.update` or `schedule.Execute` for periodic refresh
- USS styling inline via `.style` properties (no separate .uss file for MVP)
- The window should be dockable (standard `EditorWindow` behavior)
- Use `TheatreServer` statics for all server state queries

---

### Unit 3: TheatreSettingsProvider — Project Settings

**File**: `Packages/com.theatre.toolkit/Editor/UI/TheatreSettingsProvider.cs`

**Namespace**: `Theatre.Editor.UI`

```csharp
using UnityEditor;
using UnityEngine.UIElements;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// Registers Theatre settings under Project Settings > Theatre.
    /// </summary>
    public class TheatreSettingsProvider : SettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider();

        public TheatreSettingsProvider(string path, SettingsScope scope)
            : base(path, scope) { }

        public override void OnActivateGUI(string searchContext,
            VisualElement rootElement);
    }
}
```

**Settings categories**:

#### Server
- Port (int field, default 9078)
- Auto-Start (toggle, default true)

#### Tool Groups
- Same toggles as the Theatre Window (shared logic)
- Presets dropdown

#### Recording
- Default Capture Rate (int, default 60)
- Storage Path (string, default "Library/Theatre/")

**Implementation Notes**:
- `SettingsProvider` path: `"Project/Theatre"`
- Scope: `SettingsScope.Project`
- Values stored via `TheatreConfig` (backed by SessionState/EditorPrefs)
- Changes trigger server restart if port changes

---

### Unit 4: TheatreShortcuts — Keyboard Shortcuts

**File**: `Packages/com.theatre.toolkit/Editor/UI/TheatreShortcuts.cs`

**Namespace**: `Theatre.Editor.UI`

```csharp
using UnityEditor;
using UnityEditor.ShortcutManagement;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// Keyboard shortcuts for Theatre operations.
    /// Registered via Unity's ShortcutManager.
    /// </summary>
    public static class TheatreShortcuts
    {
        [Shortcut("Theatre/Toggle Recording", KeyCode.F8)]
        public static void ToggleRecording();

        [Shortcut("Theatre/Insert Marker", KeyCode.F9)]
        public static void InsertMarker();

        [Shortcut("Theatre/Open Panel", KeyCode.T,
            ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        public static void OpenPanel();
    }
}
```

**Implementation Notes**:
- `ToggleRecording`: If recording active, stop. Otherwise start with default settings.
- `InsertMarker`: If recording active, insert marker with "manual_marker" label.
- `OpenPanel`: `TheatreWindow.ShowWindow()`
- Shortcuts are rebindable via Unity's `Edit > Shortcuts` window

---

### Unit 5: TheatreServer Integration — Activity Logging Hook

**File**: `Packages/com.theatre.toolkit/Editor/TheatreServer.cs` (modify)

Add activity logging to `ExecuteToolOnMainThread`:

```csharp
private static ActivityLog s_activityLog = new();

public static ActivityLog ActivityLog => s_activityLog;

private static string ExecuteToolOnMainThread(string toolName, JToken arguments)
{
    return MainThreadDispatcher.Invoke(() =>
    {
        // ... existing tool execution ...
        var result = tool.Handler(arguments);

        // Log activity
        s_activityLog.Record(toolName, arguments, result);

        return result;
    });
}
```

Also restore activity log in `StartServer()`:
```csharp
s_activityLog = new ActivityLog();
s_activityLog.Restore();
```

---

## Implementation Order

```
Unit 1: ActivityLog (no UI dependencies)
Unit 5: TheatreServer integration (hook activity logging)
Unit 2: TheatreWindow (main UI — depends on ActivityLog)
Unit 3: TheatreSettingsProvider (independent)
Unit 4: TheatreShortcuts (independent)
```

---

## Testing

UI testing in Unity is limited — EditorWindows can't easily be tested in
EditMode tests. Focus on testing the ActivityLog (data layer) and verifying
the window opens without errors.

### Tests: `Tests/Editor/UITests.cs`

```csharp
[TestFixture]
public class ActivityLogTests
{
    [Test] public void Record_AddsEntry() { }
    [Test] public void Record_ExceedsMax_RemovesOldest() { }
    [Test] public void Record_ExtractsOperation() { }
    [Test] public void Record_DetectsError() { }
    [Test] public void SaveRestore_RoundTrips() { }
    [Test] public void TotalCalls_Increments() { }
}

[TestFixture]
public class TheatreWindowTests
{
    [Test] public void ShowWindow_OpensWithoutError()
    {
        var window = EditorWindow.GetWindow<TheatreWindow>();
        Assert.IsNotNull(window);
        window.Close();
    }
}
```

---

## Verification Checklist

1. `unity_console {"operation": "refresh"}` — recompile
2. `unity_console {"filter": "error"}` — no compile errors
3. `unity_tests {"operation": "run"}` — all tests pass
4. Manual: `Window > Theatre` opens the panel
5. Manual: Panel shows server status (running, port)
6. Manual: Tool toggles work (enable/disable groups)
7. Manual: Activity feed updates when calling MCP tools
8. Manual: `Project Settings > Theatre` shows settings
9. Manual: F8 toggles recording, Ctrl+Shift+T opens panel
