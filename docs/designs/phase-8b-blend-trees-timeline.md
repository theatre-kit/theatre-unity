# Design: Phase 8b — Director: Blend Trees & Timeline

## Overview

Two animation tools: `blend_tree_op` for creating and configuring blend
trees within AnimatorControllers, and `timeline_op` for creating Timeline
assets with tracks, clips, markers, and bindings.

`blend_tree_op` uses `UnityEditor.Animations` (already available from 8a).
`timeline_op` requires `com.unity.timeline` (installed in test project
as 1.8.11) — guarded by `#if THEATRE_HAS_TIMELINE` via `versionDefines`.

All tools live in `Editor/Tools/Director/` under `ToolGroup.DirectorAnim`.

---

## Architecture

```
Editor/Tools/Director/
  BlendTreeOpTool.cs   — MCP tool: blend_tree_op (5 operations)
  TimelineOpTool.cs    — MCP tool: timeline_op (7 operations, #if guarded)
```

---

## Implementation Units

### Unit 1: Asmdef — Timeline Version Define

**File**: `Packages/com.theatre.toolkit/Editor/com.theatre.toolkit.editor.asmdef` (modify)

Add to the existing `versionDefines` array:
```json
{
    "name": "com.unity.timeline",
    "expression": "",
    "define": "THEATRE_HAS_TIMELINE"
}
```

Add assembly reference for Timeline:
```json
"Unity.Timeline",
"Unity.Timeline.Editor"
```
(to the `references` array, alongside the existing URP references)

Also update the **test asmdef** with the same `versionDefines` entry and
Timeline assembly references.

**Acceptance Criteria**:
- [ ] `THEATRE_HAS_TIMELINE` defined when com.unity.timeline installed
- [ ] Compilation succeeds when Timeline is absent

---

### Unit 2: BlendTreeOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/BlendTreeOpTool.cs`

**Namespace**: `Theatre.Editor.Tools.Director`

```csharp
using UnityEditor.Animations;

public static class BlendTreeOpTool
{
    private static readonly JToken s_inputSchema;
    static BlendTreeOpTool();
    public static void Register(ToolRegistry registry);
    private static string Execute(JToken arguments);

    internal static string Create(JObject args);
    internal static string AddMotion(JObject args);
    internal static string SetBlendType(JObject args);
    internal static string SetParameter(JObject args);
    internal static string SetThresholds(JObject args);
}
```

**Registration**: name `"blend_tree_op"`, group `ToolGroup.DirectorAnim`.

#### `create`
- Required: `controller_path` (the .controller asset), `state_name` (state to replace with blend tree)
- Optional: `layer` (int, default 0), `blend_type` ("1d"/"2d_simple_directional"/"2d_freeform_directional"/"2d_freeform_cartesian"/"direct"), `parameter` (string)
- Load controller, find state by name
- Create blend tree: `controller.CreateBlendTreeInController(stateName, out BlendTree tree, layer)`
  Actually, the API is: find the state, then set `state.motion = new BlendTree()` and configure it.
  Better: use `AnimatorController.CreateBlendTreeInController()` if available,
  or manually create:
  ```csharp
  var tree = new BlendTree();
  tree.blendType = blendType;
  tree.blendParameter = parameter;
  state.motion = tree;
  // Add tree as sub-asset of the controller
  AssetDatabase.AddObjectToAsset(tree, controllerPath);
  ```
- Map blend type strings:
  "1d"→`BlendTreeType.Simple1D`, "2d_simple_directional"→`.SimpleDirectional2D`,
  "2d_freeform_directional"→`.FreeformDirectional2D`, "2d_freeform_cartesian"→`.FreeformCartesian2D`,
  "direct"→`.Direct`
- Response with state_name and blend_type

#### `add_motion`
- Required: `controller_path`, `state_name`, `clip_path` (animation clip asset path)
- Optional: `layer` (default 0), `threshold` (float), `position` ([x,y] for 2D blends), `time_scale` (float, default 1)
- Find state, get its `BlendTree` motion
- Load clip, `tree.AddChild(clip, threshold)`
  Or for 2D: `tree.AddChild(clip, new Vector2(x, y))`
- Response

#### `set_blend_type`
- Required: `controller_path`, `state_name`, `blend_type`
- Optional: `layer`
- Find blend tree, set `tree.blendType`
- Response

#### `set_parameter`
- Required: `controller_path`, `state_name`, `parameter`
- Optional: `layer`, `parameter_y` (for 2D blend types)
- `tree.blendParameter = parameter`
- If 2D and parameter_y: `tree.blendParameterY = parameterY`
- Response

#### `set_thresholds`
- Required: `controller_path`, `state_name`, `thresholds` (float array)
- Optional: `layer`
- Find blend tree from state

**Implementation**: `BlendTree.children` returns a copy (value-type
array of `ChildMotion` structs). Modifying the copy and assigning back
via `tree.children = modified` **does work** in Unity — the property
setter replaces the internal array. However, this must be done within
an Undo-aware context:

```csharp
Undo.RecordObject(tree, "Theatre Set Thresholds");
var children = tree.children;
for (int i = 0; i < children.Length && i < thresholds.Length; i++)
    children[i].threshold = thresholds[i];
tree.children = children;
EditorUtility.SetDirty(tree);
AssetDatabase.SaveAssetIfDirty(tree);
```

**Fallback** — If direct assignment causes issues in specific Unity
versions, use `SerializedObject`:

```csharp
var so = new SerializedObject(tree);
var childrenProp = so.FindProperty("m_Childs");
for (int i = 0; i < childrenProp.arraySize && i < thresholds.Length; i++)
{
    var child = childrenProp.GetArrayElementAtIndex(i);
    child.FindPropertyRelative("m_Threshold").floatValue = thresholds[i];
}
so.ApplyModifiedProperties();
```

The `SerializedObject` path is the safer fallback. Implementer should
try direct assignment first and fall back to SerializedObject if Unity
logs warnings or throws.

- Response with updated threshold count

**JSON Schema** properties:
- `operation` (required, enum of 5 ops)
- `controller_path`, `state_name`, `layer`
- `blend_type`, `parameter`, `parameter_y`
- `clip_path`, `threshold`, `position`, `time_scale`
- `thresholds` (array of floats)
- `dry_run`

**Acceptance Criteria**:
- [ ] `create` replaces a state's motion with a BlendTree
- [ ] `add_motion` adds clips to the blend tree
- [ ] `set_blend_type` changes the blend type
- [ ] `set_parameter` assigns blend parameters
- [ ] `set_thresholds` updates per-child thresholds

---

### Unit 3: TimelineOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/TimelineOpTool.cs`

**Entire file wrapped in `#if THEATRE_HAS_TIMELINE`**.

```csharp
#if THEATRE_HAS_TIMELINE
using UnityEngine.Timeline;
using UnityEditor.Timeline;

namespace Theatre.Editor.Tools.Director
{
    public static class TimelineOpTool
    {
        private static readonly JToken s_inputSchema;
        static TimelineOpTool();
        public static void Register(ToolRegistry registry);
        private static string Execute(JToken arguments);

        internal static string Create(JObject args);
        internal static string AddTrack(JObject args);
        internal static string AddClip(JObject args);
        internal static string SetClipProperties(JObject args);
        internal static string AddMarker(JObject args);
        internal static string BindTrack(JObject args);
        internal static string ListTracks(JObject args);
    }
}
#endif
```

**Registration**: name `"timeline_op"`, group `ToolGroup.DirectorAnim`.

#### `create`
- Required: `asset_path` (must end in `.playable`)
- Optional: `frame_rate` (double, default 60), `duration` (double)
- `var asset = ScriptableObject.CreateInstance<TimelineAsset>()`
- Set frame rate via `asset.editorSettings.frameRate = frameRate`
  (or use SerializedObject if not directly settable)
- `AssetDatabase.CreateAsset(asset, assetPath)`
- Response

#### `add_track`
- Required: `asset_path`, `track_type`
- Track types: "animation"→`AnimationTrack`, "activation"→`ActivationTrack`,
  "audio"→`AudioTrack`, "signal"→`SignalTrack`, "control"→`ControlTrack`
- Optional: `name` (track name), `parent_track` (name of group track to nest under)
- Load TimelineAsset
- `asset.CreateTrack(trackType, parentTrack, name)`
- Response with track name and type

#### `add_clip`
- Required: `asset_path`, `track_name`, `start` (double, seconds), `duration` (double, seconds)
- Optional: `clip_asset_path` (for animation clips: path to .anim file)
- Find track by name
- `var clip = track.CreateClip<AnimationPlayableAsset>()` (or appropriate type)
  Actually, for different track types:
  - AnimationTrack: `track.CreateClip<AnimationPlayableAsset>()`, then set
    `((AnimationPlayableAsset)clip.asset).clip = loadedAnimClip`
  - AudioTrack: `track.CreateClip<AudioPlayableAsset>()`, set audio clip
  - ActivationTrack: `track.CreateClip<ActivationPlayableAsset>()`
  - ControlTrack: `track.CreateClip<ControlPlayableAsset>()`
- `clip.start = start; clip.duration = duration;`
- Response

#### `set_clip_properties`
- Required: `asset_path`, `track_name`, `clip_index` (int)
- Optional: `start`, `duration`, `speed` (double), `blend_in` (double), `blend_out` (double)
- Find track, get `track.GetClips()`, index into it
- Set properties on the `TimelineClip`
- Response

#### `add_marker`
- Required: `asset_path`, `time` (double)
- Optional: `track_name` (if null, add to timeline root), `label` (string)
- For signal markers: use `SignalEmitter` if a signal track
- For generic markers: timeline root markers
- This is simplified — just add a `SignalEmitter` or use the marker track
- Best effort implementation

#### `bind_track`
- Required: `asset_path`, `track_name`, `object_path` (scene hierarchy path)
- This sets the track binding — which GameObject the track controls
- Note: bindings are stored on the `PlayableDirector` component, not the
  TimelineAsset. This operation should find the PlayableDirector in the
  scene that uses this timeline and set the binding.
- `ObjectResolver.Resolve(objectPath)` to get the GO
- Find PlayableDirector in scene that references this TimelineAsset
- `director.SetGenericBinding(track, resolvedObject)`
- If no director found, return error with suggestion

#### `list_tracks` (READ-ONLY)
- Required: `asset_path`
- Load TimelineAsset
- `asset.GetOutputTracks()` — iterate all tracks
- For each track: name, type, clip count, duration
- For each clip: start, duration, display name
- Response with tracks array

#### Undo Integration

All TimelineOpTool mutation operations (`add_track`, `add_clip`,
`set_clip_properties`, `add_marker`, `bind_track`) wrap their changes
in `DirectorHelpers.BeginUndoGroup` / `EndUndoGroup` with a
`"Theatre: <operation_name>"` label. The `create` operation uses
`Undo.RegisterCreatedObjectUndo`. Read-only `list_tracks` has no undo.

#### Missing Error Codes

- `bind_track` when no PlayableDirector found:
  `"gameobject_not_found"` with suggestion
  `"Add a PlayableDirector component to a scene object and assign this Timeline asset"`
- `add_track` with invalid track type:
  `"invalid_parameter"` with suggestion listing valid track types

**Acceptance Criteria**:
- [ ] `create` produces a .playable file
- [ ] `add_track` adds an AnimationTrack to the timeline
- [ ] `add_clip` adds a clip with start/duration
- [ ] `set_clip_properties` modifies timing
- [ ] `list_tracks` returns track and clip info
- [ ] Tool hidden when com.unity.timeline not installed

---

### Unit 4: Server Integration

**File**: `Packages/com.theatre.toolkit/Editor/TheatreServer.cs` (modify)

After Phase 8a registrations, add:
```csharp
            BlendTreeOpTool.Register(registry);             // Phase 8b
#if THEATRE_HAS_TIMELINE
            TimelineOpTool.Register(registry);              // Phase 8b
#endif
```

---

## Implementation Order

```
Unit 1: Asmdef updates (Timeline versionDefines + references)
Unit 2: BlendTreeOpTool (uses UnityEditor.Animations, no extra deps)
Unit 3: TimelineOpTool (#if THEATRE_HAS_TIMELINE)
Unit 4: Server Integration
```

---

## Testing

### Tests: `Tests/Editor/BlendTreeTimelineTests.cs`

```csharp
[TestFixture]
public class BlendTreeOpTests
{
    private string _tempDir;
    [SetUp] public void SetUp() { /* Assets/_TheatreTest_BlendTree */ }
    [TearDown] public void TearDown() { /* delete */ }

    [Test] public void Create_ReplacesStateWithBlendTree() { }
    [Test] public void AddMotion_AddsClipToTree() { }
    [Test] public void SetBlendType_ChangesType() { }
}

#if THEATRE_HAS_TIMELINE
[TestFixture]
public class TimelineOpTests
{
    private string _tempDir;
    [SetUp] public void SetUp() { /* Assets/_TheatreTest_Timeline */ }
    [TearDown] public void TearDown() { /* delete */ }

    [Test] public void Create_ProducesPlayableFile() { }
    [Test] public void AddTrack_AddsAnimationTrack() { }
    [Test] public void AddClip_AddsClipToTrack() { }
    [Test] public void ListTracks_ReturnsTrackInfo() { }
}
#endif
```

---

## Verification Checklist

1. `unity_console {"operation": "refresh"}` — recompile
2. `unity_console {"filter": "error"}` — no compile errors
3. `unity_tests {"operation": "run"}` — all tests pass
4. Manual: create controller → add state → blend_tree_op create → add_motion
5. Manual: timeline_op create → add_track → add_clip → list_tracks
