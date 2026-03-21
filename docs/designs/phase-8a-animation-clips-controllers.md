# Design: Phase 8a — Director: Animation Clips & Controllers

## Overview

Two animation tools: `animation_clip_op` for creating and modifying
AnimationClips (curves, keyframes, events, loop settings), and
`animator_controller_op` for building AnimatorControllers (state machines
with parameters, states, transitions, and layers).

All tools live in `Editor/Tools/Director/` under `ToolGroup.DirectorAnim`.

---

## Architecture

```
Editor/Tools/Director/
  AnimationClipOpTool.cs       — MCP tool: animation_clip_op (7 operations)
  AnimatorControllerOpTool.cs  — MCP tool: animator_controller_op (9 operations)
```

---

## Implementation Units

### Unit 1: AnimationClipOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/AnimationClipOpTool.cs`

**Namespace**: `Theatre.Editor.Tools.Director`

```csharp
public static class AnimationClipOpTool
{
    private static readonly JToken s_inputSchema;
    static AnimationClipOpTool();
    public static void Register(ToolRegistry registry);
    private static string Execute(JToken arguments);

    internal static string Create(JObject args);
    internal static string AddCurve(JObject args);
    internal static string RemoveCurve(JObject args);
    internal static string SetKeyframe(JObject args);
    internal static string SetEvents(JObject args);
    internal static string SetLoop(JObject args);
    internal static string ListCurves(JObject args);
}
```

**Registration**: name `"animation_clip_op"`, group `ToolGroup.DirectorAnim`, `ReadOnlyHint = false`.

#### `create`
- Required: `asset_path` (must end in `.anim`)
- Optional: `frame_rate` (float, default 60), `wrap_mode` ("default"/"once"/"loop"/"ping_pong"/"clamp_forever"), `legacy` (bool, default false)
- `var clip = new AnimationClip()`
- Set `clip.frameRate`, `clip.wrapMode`, `clip.legacy`
- `AssetDatabase.CreateAsset(clip, assetPath)`
- Response: `{ "result": "ok", "operation": "create", "asset_path": "..." }`

#### `add_curve`
- Required: `clip_path` (asset path to .anim), `property_name` (e.g. "m_LocalPosition.x"),
  `type` (component type name, e.g. "Transform"), `keyframes` (array)
- Optional: `relative_path` (hierarchy path relative to the animated object, default "")
- Each keyframe: `{ "time": float, "value": float, "in_tangent": float?, "out_tangent": float? }`
- Load clip, build `AnimationCurve` from keyframes:
  ```csharp
  var keys = new Keyframe[keyframes.Count];
  for (int i = 0; i < keyframes.Count; i++)
  {
      var kf = keyframes[i];
      keys[i] = new Keyframe(time, value, inTangent, outTangent);
  }
  var curve = new AnimationCurve(keys);
  ```
- Resolve component type via `DirectorHelpers.ResolveComponentType`
- `AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
  { path = relativePath, type = componentType, propertyName = propertyName }, curve)`
- `EditorUtility.SetDirty(clip)`
- Response with curve count

#### `remove_curve`
- Required: `clip_path`, `property_name`, `type`
- Optional: `relative_path` (default "")
- `AnimationUtility.SetEditorCurve(clip, binding, null)` — setting null removes the curve
- Response

#### `set_keyframe`
- Required: `clip_path`, `property_name`, `type`, `time`, `value`
- Optional: `relative_path`, `in_tangent`, `out_tangent`
- Get existing curve: `AnimationUtility.GetEditorCurve(clip, binding)`
- If null: create new curve with single keyframe
- Otherwise: `curve.AddKey(new Keyframe(time, value, inTangent, outTangent))`
- Set back: `AnimationUtility.SetEditorCurve(clip, binding, curve)`
- Response

#### `set_events`
- Required: `clip_path`, `events` (array)
- Each event: `{ "time": float, "function": string, "int_param": int?, "float_param": float?, "string_param": string? }`
- Build `AnimationEvent[]`:
  ```csharp
  var events = new AnimationEvent[eventArray.Count];
  for (int i = 0; i < eventArray.Count; i++)
  {
      events[i] = new AnimationEvent
      {
          time = time,
          functionName = function,
          intParameter = intParam ?? 0,
          floatParameter = floatParam ?? 0f,
          stringParameter = stringParam ?? ""
      };
  }
  AnimationUtility.SetAnimationEvents(clip, events);
  ```
- Response with event count

#### `set_loop`
- Required: `clip_path`
- Optional: `loop_time` (bool, default true), `loop_pose` (bool), `cycle_offset` (float)
- `AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip)`
- `settings.loopTime = loopTime`
- `settings.loopBlend = loopPose ?? settings.loopBlend`
- `settings.cycleOffset = cycleOffset ?? settings.cycleOffset`
- `AnimationUtility.SetAnimationClipSettings(clip, settings)`
- Response

#### `list_curves` (READ-ONLY)
- Required: `clip_path`
- Load clip
- `var bindings = AnimationUtility.GetCurveBindings(clip)`
- For each binding: `{ "path": binding.path, "property": binding.propertyName, "type": binding.type.Name, "keyframe_count": curve.length }`
- Response: `{ "result": "ok", "curves": [...], "length": clip.length, "frame_rate": clip.frameRate }`

**JSON Schema** properties:
- `operation` (required, enum of 7 ops)
- `asset_path`, `clip_path`, `property_name`, `type`, `relative_path`
- `keyframes`, `time`, `value`, `in_tangent`, `out_tangent`
- `events`, `frame_rate`, `wrap_mode`, `legacy`
- `loop_time`, `loop_pose`, `cycle_offset`
- `dry_run`

**Acceptance Criteria**:
- [ ] `create` produces a .anim file
- [ ] `add_curve` adds a position curve with keyframes
- [ ] `remove_curve` removes the curve
- [ ] `set_keyframe` adds a keyframe to an existing curve
- [ ] `set_events` adds animation events
- [ ] `set_loop` configures loop settings
- [ ] `list_curves` returns all curve bindings

---

### Unit 2: AnimatorControllerOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/AnimatorControllerOpTool.cs`

**Namespace**: `Theatre.Editor.Tools.Director`

```csharp
using UnityEditor.Animations;

public static class AnimatorControllerOpTool
{
    private static readonly JToken s_inputSchema;
    static AnimatorControllerOpTool();
    public static void Register(ToolRegistry registry);
    private static string Execute(JToken arguments);

    internal static string Create(JObject args);
    internal static string AddParameter(JObject args);
    internal static string AddState(JObject args);
    internal static string SetStateClip(JObject args);
    internal static string AddTransition(JObject args);
    internal static string SetTransitionConditions(JObject args);
    internal static string SetDefaultState(JObject args);
    internal static string AddLayer(JObject args);
    internal static string ListStates(JObject args);
}
```

**Registration**: name `"animator_controller_op"`, group `ToolGroup.DirectorAnim`.

#### `create`
- Required: `asset_path` (must end in `.controller`)
- `var controller = AnimatorController.CreateAnimatorControllerAtPath(assetPath)`
- This automatically creates a controller with one layer ("Base Layer")
  and an empty state machine
- Response with asset_path

#### `add_parameter`
- Required: `asset_path`, `name`, `type` ("float"/"int"/"bool"/"trigger")
- Load: `AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath)`
- Map type string to `AnimatorControllerParameterType`:
  "float"→`.Float`, "int"→`.Int`, "bool"→`.Bool`, "trigger"→`.Trigger`
- `controller.AddParameter(name, paramType)`
- Optional: `default_value` — set after adding:
  ```csharp
  var param = controller.parameters[^1];
  param.defaultFloat / defaultInt / defaultBool = value;
  ```
- Response

#### `add_state`
- Required: `asset_path`, `name`
- Optional: `layer` (int, default 0), `position` ([x,y] for visual position in editor)
- `var layer = controller.layers[layerIndex]`
- `var state = layer.stateMachine.AddState(name, position)`
- `EditorUtility.SetDirty(controller)`
- Response with state name and layer

#### `set_state_clip`
- Required: `asset_path`, `state_name`, `clip_path`
- Optional: `layer` (default 0)
- Find state by name in the layer's state machine
- Load clip: `AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath)`
- `state.motion = clip`
- Response

#### `add_transition`
- Required: `asset_path`, `source_state`, `destination_state`
- Optional: `layer` (default 0), `has_exit_time` (bool, default true),
  `exit_time` (float, default 0.75), `transition_duration` (float, default 0.25),
  `conditions` (array of condition objects)
- Find source and destination states by name
- `var transition = sourceState.AddTransition(destState)`
- `transition.hasExitTime = hasExitTime`
- `transition.exitTime = exitTime`
- `transition.duration = transitionDuration`
- For each condition: `{ "parameter": string, "mode": string, "threshold": float? }`
  - Mode mapping: "if"→`AnimatorConditionMode.If`, "if_not"→`.IfNot`,
    "greater"→`.Greater`, "less"→`.Less`, "equals"→`.Equals`, "not_equals"→`.NotEqual`
  - `transition.AddCondition(mode, threshold, parameterName)`
- Response with transition details

#### `set_transition_conditions`
- Required: `asset_path`, `source_state`, `destination_state`, `conditions`
- Optional: `layer` (default 0)
- Find the transition between the two states
- Clear existing conditions, add new ones
- Response

#### `set_default_state`
- Required: `asset_path`, `state_name`
- Optional: `layer` (default 0)
- `stateMachine.defaultState = state`
- Response

#### `add_layer`
- Required: `asset_path`, `name`
- Optional: `blend_mode` ("override"/"additive", default "override"),
  `weight` (float, default 1.0), `mask_path` (avatar mask asset path)
- `controller.AddLayer(name)`
- Set blend mode and weight on the new layer
- Response

#### `list_states` (READ-ONLY)
- Required: `asset_path`
- Optional: `layer` (default 0)
- List all states with their clips, transitions, and positions
- For each state: `{ "name": "...", "clip": "..." or null, "is_default": bool, "transitions": [...] }`
- For each transition: `{ "destination": "...", "has_exit_time": bool, "conditions": [...] }`
- Also list parameters
- Response

**JSON Schema** properties:
- `operation` (required, enum of 9 ops)
- `asset_path`, `name`, `type`, `default_value`
- `state_name`, `clip_path`, `layer`, `position`
- `source_state`, `destination_state`
- `has_exit_time`, `exit_time`, `transition_duration`, `conditions`
- `blend_mode`, `weight`, `mask_path`
- `dry_run`

**Acceptance Criteria**:
- [ ] `create` produces a .controller file with Base Layer
- [ ] `add_parameter` adds float/int/bool/trigger parameters
- [ ] `add_state` adds a state to the state machine
- [ ] `set_state_clip` assigns a clip to a state
- [ ] `add_transition` creates a transition with conditions
- [ ] `set_default_state` changes the entry state
- [ ] `add_layer` adds a new animation layer
- [ ] `list_states` returns states, transitions, and parameters

---

### Unit 3: Server Integration

**File**: `Packages/com.theatre.toolkit/Editor/TheatreServer.cs` (modify)

Add after Phase 7c registrations (inside the `#if` block or after it):
```csharp
AnimationClipOpTool.Register(registry);         // Phase 8a
AnimatorControllerOpTool.Register(registry);    // Phase 8a
```

**Acceptance Criteria**:
- [ ] Both tools appear in `tools/list` when DirectorAnim is enabled

---

## Implementation Order

```
Unit 1: AnimationClipOpTool (independent)
Unit 2: AnimatorControllerOpTool (independent, can reference clips)
Unit 3: Server Integration
```

---

## Testing

### Tests: `Tests/Editor/AnimationToolTests.cs`

```csharp
[TestFixture]
public class AnimationClipOpTests
{
    private string _tempDir;
    [SetUp] public void SetUp() { /* create Assets/_TheatreTest_Anim */ }
    [TearDown] public void TearDown() { /* delete */ }

    [Test] public void Create_ProducesAnimFile() { }
    [Test] public void AddCurve_AddsPositionCurve() { }
    [Test] public void RemoveCurve_RemovesCurve() { }
    [Test] public void SetKeyframe_AddsToExistingCurve() { }
    [Test] public void SetLoop_ConfiguresLoopTime() { }
    [Test] public void ListCurves_ReturnsCurveBindings() { }
}

[TestFixture]
public class AnimatorControllerOpTests
{
    private string _tempDir;
    [SetUp] public void SetUp() { /* create Assets/_TheatreTest_AnimCtrl */ }
    [TearDown] public void TearDown() { /* delete */ }

    [Test] public void Create_ProducesControllerFile() { }
    [Test] public void AddParameter_AddsFloatParam() { }
    [Test] public void AddState_AddsToStateMachine() { }
    [Test] public void AddTransition_CreatesTransitionWithCondition() { }
    [Test] public void ListStates_ReturnsStatesAndTransitions() { }
}
```

---

## Verification Checklist

1. `unity_console {"operation": "refresh"}` — recompile
2. `unity_console {"filter": "error"}` — no compile errors
3. `unity_tests {"operation": "run"}` — all tests pass
4. Manual: create clip → add_curve → list_curves → verify
5. Manual: create controller → add_parameter → add_state → add_transition → list_states
