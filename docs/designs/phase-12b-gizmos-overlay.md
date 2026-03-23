# Design: Phase 12b — Scene View Gizmos & Overlay

## Overview

Visual feedback in the Scene View: gizmo drawings for agent queries,
watches, and actions; a compact overlay showing Theatre status; and a
first-run welcome dialog.

Uses `Handles` API via `SceneView.duringSceneGui` for gizmo drawing,
`SceneView.AddOverlayToActiveView` for the overlay, and
`EditorWindow` for the welcome dialog.

---

## Architecture

```
Editor/UI/
  GizmoRenderer.cs         — Draws query/watch/action gizmos in Scene View
  GizmoState.cs            — Ring buffer of recent gizmo requests with fade
  TheatreOverlay.cs        — Scene View overlay (compact status badge)
  WelcomeDialog.cs         — First-run dialog with .mcp.json snippet
```

---

## Implementation Units

### Unit 1: GizmoState — Gizmo Request Buffer

**File**: `Packages/com.theatre.toolkit/Editor/UI/GizmoState.cs`

**Namespace**: `Theatre.Editor.UI`

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// Stores recent gizmo visualization requests with auto-fade.
    /// Each request has a type, geometry data, and creation time.
    /// Requests older than the fade duration are removed.
    /// </summary>
    public static class GizmoState
    {
        /// <summary>How long query gizmos persist (seconds).</summary>
        public static float FadeDuration { get; set; } = 3f;

        /// <summary>Global toggle for all gizmos.</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>Opacity multiplier (0-1).</summary>
        public static float Opacity { get; set; } = 0.7f;

        public enum GizmoType
        {
            Nearest,      // wire sphere at origin + lines to results
            Radius,       // wire sphere showing search radius
            Overlap,      // wire box/sphere/capsule
            Raycast,      // line + hit point
            Linecast,     // line between points
            Bounds,       // wire box
            WatchProximity,  // dashed circle
            WatchRegion,     // semi-transparent box
            Teleport,     // trail from old to new position
        }

        public struct GizmoRequest
        {
            public GizmoType Type;
            public float CreatedAt;      // Time.realtimeSinceStartup
            public Vector3 Origin;
            public Vector3 End;          // or direction for raycast
            public float Radius;
            public Vector3 Size;         // for box shapes
            public Vector3[] Points;     // for multi-point (nearest results, etc.)
            public bool Hit;             // for raycast/linecast
            public Color Color;
        }

        private static readonly List<GizmoRequest> _requests = new();

        /// <summary>Active requests (auto-pruned on access).</summary>
        public static IReadOnlyList<GizmoRequest> Requests
        {
            get
            {
                Prune();
                return _requests;
            }
        }

        /// <summary>Add a new gizmo request.</summary>
        public static void Add(GizmoRequest request);

        /// <summary>Clear all requests.</summary>
        public static void Clear();

        /// <summary>Remove expired requests.</summary>
        private static void Prune();

        /// <summary>Get fade alpha for a request (1.0 fresh → 0.0 expired).</summary>
        public static float GetAlpha(GizmoRequest request);
    }
}
```

**Implementation Notes**:
- `Prune()` removes requests where `Time.realtimeSinceStartup - CreatedAt > FadeDuration`
- `GetAlpha()` returns `1.0 - (elapsed / FadeDuration)` clamped to [0,1], multiplied by `Opacity`
- Watch gizmos don't fade (they persist while the watch is active) — use a very large `CreatedAt` future time or a separate list
- `Add()` is called from tool handlers. To hook this in, the `ActivityLog.Record()` method (or a new hook in `TheatreServer`) should also call `GizmoState.Add()` with appropriate geometry extracted from the tool call args and results.

**Hook into tool execution**: Add gizmo data extraction after spatial query/watch/action tool calls. The simplest approach: in `TheatreServer.ExecuteToolOnMainThread`, after recording activity, also check if the tool is a gizmo-relevant tool and extract geometry from the args/result.

---

### Unit 2: GizmoRenderer — Scene View Drawing

**File**: `Packages/com.theatre.toolkit/Editor/UI/GizmoRenderer.cs`

**Namespace**: `Theatre.Editor.UI`

```csharp
using UnityEditor;
using UnityEngine;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// Draws Theatre gizmos in the Scene View via Handles API.
    /// Registered via SceneView.duringSceneGui.
    /// </summary>
    [InitializeOnLoad]
    public static class GizmoRenderer
    {
        static GizmoRenderer()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView);
    }
}
```

**Drawing per GizmoType**:

| GizmoType | Drawing |
|-----------|---------|
| `Nearest` | Wire sphere at Origin (cyan), lines from Origin to each Point (cyan, fading) |
| `Radius` | Wire sphere at Origin with Radius (yellow, transparent) |
| `Overlap` | Wire cube at Origin with Size (green, transparent) |
| `Raycast` | Line from Origin in direction End * Radius length (red). If Hit: sphere at hit point (red) |
| `Linecast` | Line from Origin to End. Green if !Hit (clear), red if Hit (blocked) |
| `Bounds` | Wire cube at Origin with Size (white) |
| `WatchProximity` | Dashed wire sphere at Origin with Radius (orange) |
| `WatchRegion` | Semi-transparent cube from Origin to End (orange) |
| `Teleport` | Dotted line from Origin to End (magenta), sphere at End |

**Implementation Notes**:
- All drawing uses `Handles.color` with alpha from `GizmoState.GetAlpha(request)`
- Wire sphere: `Handles.DrawWireDisc(center, Vector3.up, radius)` + two more axes
  Or use `Handles.RadiusHandle` or `Gizmos.DrawWireSphere` (but Gizmos only works in `OnDrawGizmos`, not `duringSceneGui` — use Handles).
- For wire sphere: draw 3 wire discs (XY, XZ, YZ planes):
  ```csharp
  Handles.DrawWireDisc(center, Vector3.up, radius);
  Handles.DrawWireDisc(center, Vector3.right, radius);
  Handles.DrawWireDisc(center, Vector3.forward, radius);
  ```
- Wire cube: `Handles.DrawWireCube(center, size)`
- Line: `Handles.DrawLine(from, to)`
- Dotted line: `Handles.DrawDottedLine(from, to, dashSize)`
- Sphere marker: `Handles.SphereHandleCap(0, pos, Quaternion.identity, size, EventType.Repaint)`
- Force Scene View repaint: `SceneView.RepaintAll()` when new gizmos are added
- Check `GizmoState.Enabled` before drawing anything

---

### Unit 3: Gizmo Hook — Extract Geometry from Tool Calls

**File**: `Packages/com.theatre.toolkit/Editor/UI/GizmoExtractor.cs`

**Namespace**: `Theatre.Editor.UI`

```csharp
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// Extracts gizmo geometry from tool call args and results.
    /// Called after each spatial_query, watch, or action tool call.
    /// </summary>
    internal static class GizmoExtractor
    {
        /// <summary>
        /// Try to extract a gizmo from a tool call.
        /// Returns true if a gizmo was added to GizmoState.
        /// </summary>
        public static bool TryExtract(
            string toolName, JToken args, string resultJson);
    }
}
```

**Extraction rules**:

| Tool | Operation | Extracts |
|------|-----------|----------|
| `spatial_query` | `nearest` | Nearest: origin from args, result positions from results |
| `spatial_query` | `radius` | Radius: origin + radius from args |
| `spatial_query` | `overlap` | Overlap: center + size from args |
| `spatial_query` | `raycast` | Raycast: origin + direction from args, hit point from result |
| `spatial_query` | `linecast` | Linecast: from + to from args, hit from result |
| `spatial_query` | `bounds` | Bounds: center + size from result |
| `watch` | `create` (proximity) | WatchProximity: target position + within/beyond distance |
| `watch` | `create` (region) | WatchRegion: min + max from condition |
| `action` | `teleport` | Teleport: previous_position + position from result |

**Implementation Notes**:
- Parse `args` and `resultJson` to extract coordinates
- For `nearest`: parse `result.results[].position` array
- For `raycast`: parse `result.result.point` for hit location
- Use `JsonParamParser.ParseVector3` for coordinate parsing where possible
- Call `SceneView.RepaintAll()` after adding a gizmo

**Hook**: Call `GizmoExtractor.TryExtract()` from `TheatreServer.ExecuteToolOnMainThread` right after `s_activityLog.Record()`.

---

### Unit 4: TheatreOverlay — Scene View Status Badge

**File**: `Packages/com.theatre.toolkit/Editor/UI/TheatreOverlay.cs`

**Namespace**: `Theatre.Editor.UI`

```csharp
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// Compact overlay in the Scene View corner showing Theatre status.
    /// </summary>
    [Overlay(typeof(SceneView), "Theatre", defaultDisplay = true)]
    public class TheatreOverlay : Overlay
    {
        public override VisualElement CreatePanelContent();
    }
}
```

**Content**:
```
┌─ Theatre ──────────┐
│  ● Agent Connected  │
│  Watches: 3 active  │
│  Recording ●        │
└─────────────────────┘
```

- Green dot if server running, red if stopped
- Watch count from engine
- "Recording ●" (red dot) if recording active, omit if not
- Updates via `schedule.Execute` every 2 seconds

**Implementation Notes**:
- `[Overlay]` attribute registers with Unity's overlay system
- `defaultDisplay = true` shows it by default
- Use `VisualElement` with `Label` children
- Minimal — just enough to know Theatre is active

---

### Unit 5: WelcomeDialog — First-Run Dialog

**File**: `Packages/com.theatre.toolkit/Editor/UI/WelcomeDialog.cs`

**Namespace**: `Theatre.Editor.UI`

```csharp
using UnityEditor;
using UnityEngine;

namespace Theatre.Editor.UI
{
    /// <summary>
    /// First-run welcome dialog shown when Theatre is installed.
    /// Shows the server URL and .mcp.json configuration snippet.
    /// </summary>
    public class WelcomeDialog : EditorWindow
    {
        private const string ShownKey = "Theatre_WelcomeShown";

        [InitializeOnLoadMethod]
        private static void CheckFirstRun();

        public static void Show();
    }
}
```

**Content**:
```
Theatre is running on http://localhost:9078

Add this to your agent's .mcp.json:

{
  "mcpServers": {
    "theatre": {
      "type": "http",
      "url": "http://localhost:9078/mcp"
    }
  }
}

[Copy to Clipboard]  [Open Theatre Panel]  [Don't show again]
```

**Implementation Notes**:
- `CheckFirstRun()`: Check `EditorPrefs.GetBool(ShownKey, false)`. If false,
  show dialog via `EditorApplication.delayCall`.
- "Don't show again": Sets `EditorPrefs.SetBool(ShownKey, true)`
- "Copy to Clipboard": `EditorGUIUtility.systemCopyBuffer = mcpJson`
- "Open Theatre Panel": `TheatreWindow.ShowWindow()`
- Window size: ~400x300, not resizable
- Use `EditorPrefs` (not `SessionState`) — persists across editor restarts

---

### Unit 6: TheatreServer Integration — Gizmo Hook

**File**: `Packages/com.theatre.toolkit/Editor/TheatreServer.cs` (modify)

After the `s_activityLog.Record(...)` call in `ExecuteToolOnMainThread`, add:
```csharp
GizmoExtractor.TryExtract(toolName, arguments, result);
```

Add `using Theatre.Editor.UI;` (already added in 12a, should be there).

---

## Implementation Order

```
Unit 1: GizmoState (data layer — no dependencies)
Unit 3: GizmoExtractor (depends on GizmoState)
Unit 2: GizmoRenderer (depends on GizmoState, draws in Scene View)
Unit 4: TheatreOverlay (independent)
Unit 5: WelcomeDialog (independent)
Unit 6: Server Integration (hook GizmoExtractor)
```

---

## Testing

Gizmo rendering can't be tested in EditMode (requires Scene View
interaction). Test the data layer and extraction logic.

### Tests: `Tests/Editor/GizmoTests.cs`

```csharp
[TestFixture]
public class GizmoStateTests
{
    [SetUp] public void SetUp() { GizmoState.Clear(); }

    [Test] public void Add_IncreasesCount() { }
    [Test] public void Prune_RemovesExpired() { }
    [Test] public void GetAlpha_FreshRequestReturnsOne() { }
    [Test] public void GetAlpha_HalfwayReturnsFifty() { }
    [Test] public void Enabled_False_RequestsStillStored() { }
}

[TestFixture]
public class GizmoExtractorTests
{
    [SetUp] public void SetUp() { GizmoState.Clear(); }

    [Test] public void TryExtract_SpatialNearest_AddsGizmo() { }
    [Test] public void TryExtract_SpatialRadius_AddsGizmo() { }
    [Test] public void TryExtract_ActionTeleport_AddsGizmo() { }
    [Test] public void TryExtract_UnrelatedTool_ReturnsFalse() { }
}
```

---

## Verification Checklist

1. `unity_console {"operation": "refresh"}` — recompile
2. `unity_console {"filter": "error"}` — no compile errors
3. `unity_tests {"operation": "run"}` — all tests pass
4. Manual: Call `spatial_query:nearest` → see wire sphere in Scene View
5. Manual: Call `action:teleport` → see trail line in Scene View
6. Manual: Gizmos fade after 3 seconds
7. Manual: Overlay visible in Scene View corner
8. Delete `Theatre_WelcomeShown` from EditorPrefs → welcome dialog appears
