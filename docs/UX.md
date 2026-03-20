# Unity Theatre — UX Design

## Interaction Surfaces

Theatre has two users interacting through four surfaces:

| User | Surface | Interaction |
|---|---|---|
| AI Agent | MCP Tools (via HTTP) | Structured queries, mutations, recordings |
| Human Developer | Theatre Panel (EditorWindow) | Status, config, activity feed, recording timeline |
| Human Developer | Scene View Gizmos | Spatial visualization of agent queries and watches |
| Human Developer | Project Settings | Persistent configuration, tool group toggles |

The human and agent share a common workspace (the Unity project and running
game) and a common timeline (recordings). The editor UI shows what the agent
is doing; the agent's tools show what the game is doing.

---

## Theatre Panel (EditorWindow)

The main Theatre window, accessible via `Window > Theatre` or a toolbar
button. Built with UI Toolkit (UXML + USS). Dockable like any Unity panel.

### Layout

```
┌─ Theatre ──────────────────────────────────────────────────────┐
│                                                                 │
│  ● Server Running  |  http://localhost:9078  |  Agent Connected  │
│                                                                 │
│  ┌─ Tool Groups ─────────────────────────────────────────────┐  │
│  │  [■] Stage: GameObject  [■] Queries  [■] Watches          │  │
│  │  [ ] Stage: ECS                                           │  │
│  │  [■] Director: Scene    [■] Prefabs  [■] Assets           │  │
│  │  [■] Director: Anim     [ ] Spatial  [ ] Input            │  │
│  │  [ ] Director: Config                                      │  │
│  │                                                [Presets ▾] │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                 │
│  ┌─ Recording ───────────────────────────────────────────────┐  │
│  │  [● Record]  [■ Stop]  [⚑ Mark]        Status: Recording  │  │
│  │                                                            │  │
│  │  ┌──────────────────────────────────────────────────────┐  │  │
│  │  │  ▼  ▼     ▼            ▼  ▼▼                    ▼   │  │  │
│  │  │──●──●─────●────────────●──●●────────────────────●───│  │  │
│  │  │  ▲ markers              ▲ agent queries              │  │  │
│  │  │  0s        5s        10s        15s        20s       │  │  │
│  │  └──────────────────────────────────────────────────────┘  │  │
│  │                                                            │  │
│  │  ⏱ 00:22.4  |  Frame 1344  |  847 KB  |  60 fps capture   │  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                 │
│  ┌─ Active Watches ──────────────────────────────────────────┐  │
│  │  ● w_01  "enemy_hp"      /Enemies/Scout*  current_hp<25   │  │
│  │  ● w_02  "player_near"   /Player          proximity<5.0   │  │
│  │  ○ w_03  "door_opened"   /Door_01         is_open=true    │  │
│  │  Triggers: 3 (last: 2s ago)                                │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                 │
│  ┌─ Agent Activity ──────────────────────────────────────────┐  │
│  │  14:22:01  scene_snapshot    focus=[0,1,0]  r=50   340tok  │  │
│  │  14:22:03  scene_inspect     /Player         full   820tok  │  │
│  │  14:22:05  spatial_query     nearest  n=5          210tok  │  │
│  │  14:22:08  watch:create      "enemy_hp"             50tok  │  │
│  │  14:22:12  prefab_op         instantiate Enemy      90tok  │  │
│  │  14:22:14  action:teleport   /Enemy → [10,0,5]      60tok  │  │
│  │                                                            │  │
│  │  Session: 14 calls  |  2,400 tokens  |  Connected 12m      │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                 │
│  ┌─ Recordings Library ──────────────────────────────────────┐  │
│  │  wall_clip_repro      12.4s   Today 14:10   847 KB  [×]   │  │
│  │  patrol_test_03        8.1s   Today 13:45   523 KB  [×]   │  │
│  │  physics_debug        22.0s   Mar 18        1.2 MB  [×]   │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Panel Sections

#### Server Status Bar

- Connection indicator (running/stopped/error)
- Server URL (clickable to copy)
- Agent connection status (connected/disconnected, agent name if available)
- Quick-access buttons: restart server, copy URL, open settings

#### Tool Groups

Checkbox grid for enabling/disabling tool groups. Changes take effect
immediately — the server sends `tools/list_changed` notification to the
agent.

**Presets dropdown:**
- "GameObject Project" — Stage GameObject + Director (no ECS)
- "ECS Project" — Stage ECS + Director (no GameObject)
- "Stage Only" — observation tools, no Director
- "Director Only" — mutation tools, no Stage
- "Everything" — all tools active
- "Custom" — shown when manual selection doesn't match a preset

#### Recording Section

- Record/Stop/Mark buttons
- **Timeline scrubber**: visual timeline showing the recording duration
  with markers (human-placed) and agent query indicators (when the agent
  queried this recording's data). Click to seek. Drag to select a range.
- Stats: duration, frame count, file size, capture rate

The timeline is interactive — clicking a marker shows what was happening
at that moment. Selecting a range shows a diff summary.

#### Active Watches

Live list of all agent watches. Each entry shows:
- Status indicator (active ●, triggered recently, expired ○)
- Watch ID and label
- Target path/pattern
- Condition summary
- Last trigger time

Clicking a watch highlights its target in the Scene view (if the target
exists) and draws the condition region (radius for proximity, box for region).

#### Agent Activity Feed

Scrolling log of every MCP tool call the agent makes. Each entry shows:
- Timestamp
- Tool name
- Key parameters (abbreviated)
- Response token count

Color-coded by tool type: blue for Stage reads, green for Director writes,
yellow for watches, red for errors.

Double-clicking an entry shows the full request/response JSON in a detail
pane.

#### Recordings Library

List of saved recording clips. Each entry shows label, duration, date,
file size, and a delete button. Click to load into the timeline scrubber.

---

## Scene View Gizmos

Theatre draws directly into Unity's Scene view using `Handles` and custom
`SceneView.duringSceneGui` callbacks. These visualizations update in real
time as the agent makes queries.

### What Gets Drawn

#### Query Visualizations (fade after 3 seconds)

| Query Type | Gizmo |
|---|---|
| `nearest` | Wire sphere at origin + lines to each result, labeled with distance |
| `radius` | Wire sphere showing search radius, result objects highlighted |
| `overlap` | Wire box/sphere/capsule showing the overlap region |
| `raycast` | Line from origin in direction, hit point marker, normal arrow |
| `linecast` | Line between points, hit marker if blocked, green if clear |
| `path_distance` | NavMesh path drawn as connected line segments |
| `bounds` | Wire box showing object bounds |

#### Watch Visualizations (persistent while watch is active)

| Watch Type | Gizmo |
|---|---|
| Proximity | Dashed circle/sphere around target at trigger distance |
| Region | Semi-transparent box showing watched region |
| Property threshold | Icon above watched object, changes color on trigger |
| Tracked object | Subtle outline highlight on watched GameObjects |

#### Action Visualizations (brief flash)

| Action | Gizmo |
|---|---|
| Teleport | Ghost trail from old position to new position |
| Set property | Brief pulse on the affected object |

### Gizmo Controls

The Theatre panel has gizmo toggle buttons:

- **Show Query Gizmos**: on/off
- **Show Watch Gizmos**: on/off
- **Gizmo Duration**: how long query visualizations persist (default 3s)
- **Gizmo Opacity**: transparency slider

### Scene View Overlay

A small overlay in the corner of the Scene view (using `SceneView.AddOverlayToActiveView`)
shows:

```
┌─ Theatre ──────────┐
│  ● Agent Connected  │
│  Watches: 3 active  │
│  Recording ●        │
└─────────────────────┘
```

Minimal — just enough to know Theatre is active without opening the full panel.

---

## Project Settings Integration

Theatre registers a `SettingsProvider` under `Project > Theatre` in Unity's
Project Settings window. This is where persistent configuration lives.

### Settings Categories

#### Server

| Setting | Type | Default | Description |
|---|---|---|---|
| Port | `int` | `9078` | HTTP server port |
| Auto-Start | `bool` | `true` | Start server on editor load |
| Allow Remote | `bool` | `false` | Accept connections from non-localhost |
| Max Connections | `int` | `1` | Concurrent MCP client limit |

#### Tool Groups

Same checkbox grid as the Theatre Panel, but stored as project-level
settings (saved to `ProjectSettings/TheatreSettings.asset`).

#### Recording

| Setting | Type | Default | Description |
|---|---|---|---|
| Default Capture Rate | `int` | `60` | Frames per second |
| Max Recording Duration | `float` | `300` | Seconds (0 = unlimited) |
| Storage Path | `string` | `"Library/Theatre/"` | Where recordings are saved |
| Auto-Record in Play Mode | `bool` | `false` | Start recording when Play starts |
| Components to Track | `string[]` | `["Transform"]` | Default tracked component types |

#### Stage

| Setting | Type | Default | Description |
|---|---|---|---|
| Default Token Budget | `int` | `1500` | Default response budget |
| Snapshot Clustering | `bool` | `true` | Group nearby objects in snapshots |
| Cluster Distance | `float` | `5.0` | Grouping threshold |
| Delta Frame Window | `int` | `300` | Frames to keep for delta queries |
| Max Watch Count | `int` | `20` | Maximum concurrent watches |

#### Director

| Setting | Type | Default | Description |
|---|---|---|---|
| Dry Run Default | `bool` | `false` | Default all ops to dry-run |
| Confirm Destructive | `bool` | `true` | Require confirmation for delete ops |
| Undo Group Size | `int` | `1` | Operations per undo group (1 = each op is undoable) |

---

## Keyboard Shortcuts

Registered via Unity's ShortcutManager (`[Shortcut]` attribute):

| Shortcut | Action | Context |
|---|---|---|
| `F8` | Toggle recording | Global (Play Mode) |
| `F9` | Insert marker | Global (Play Mode, while recording) |
| `Ctrl+Shift+T` | Open/focus Theatre Panel | Global |
| `Ctrl+Shift+G` | Toggle gizmos | Scene View |

Shortcuts are rebindable through Unity's standard Shortcuts window
(`Edit > Shortcuts`).

---

## Notifications & Toasts

Theatre uses Unity's built-in notification system for important events:

| Event | Display |
|---|---|
| Agent connected | Toast: "Theatre: Agent connected" |
| Agent disconnected | Toast: "Theatre: Agent disconnected" |
| Watch triggered | Console log (not toast — too frequent) |
| Recording started | Toast: "Theatre: Recording started" |
| Recording stopped | Toast + Console: "Recording saved: {label} ({duration}s)" |
| Server error | Console error + status bar turns red |
| Director destructive op | Console warning: "Theatre: Agent deleted /Path/To/Object" |

Director mutations that modify assets are logged to the Console with a
`[Theatre]` prefix so the human can see what the agent changed without
opening the Theatre panel.

---

## First-Run Experience

When Theatre is first installed:

1. Package imports, assemblies compile
2. `[InitializeOnLoad]` starts the server
3. A welcome dialog appears:
   - "Theatre is running on http://localhost:9078"
   - "Add this to your agent's .mcp.json:" (copyable config snippet)
   - "Open Theatre Panel" button
   - "Don't show again" checkbox
4. Theatre Panel auto-opens in a default dock position

---

## Error States & Recovery

| State | UI Indication | Recovery |
|---|---|---|
| Port in use | Status bar: red, "Port 9078 in use" | Settings: change port, or auto-increment |
| Server crash | Status bar: red, "Server stopped" | "Restart" button in panel |
| Domain reload | Status bar: yellow briefly, "Restarting..." | Automatic — server restarts |
| No agent connected | Status bar: gray, "Waiting for agent" | Normal — show .mcp.json config hint |
| Play mode transition | Brief "Switching..." | Automatic — tools adapt to new mode |
