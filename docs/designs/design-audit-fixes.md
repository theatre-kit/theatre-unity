# Design: Audit Fixes — Comprehensive Design Doc Corrections

## Overview

This design addresses all critical and minor issues found during the
design document audit. It covers fixes to CONTRACTS.md, ARCHITECTURE.md,
and all phase design documents (0–13). The fixes are grouped into
documentation updates (no code changes) since the issues are in design
docs, not implementation.

**Scope**: Fix 9 critical issues, ~20 minor issues, and several cross-
cutting documentation gaps across 23 design files and 4 reference docs.

**Not in scope**: Code changes. The codebase is already implemented using
Newtonsoft.Json and follows the correct patterns. These fixes align the
design docs with reality and fill specification gaps for unimplemented
phases.

**Post-refactor note**: Since this design was written, a code refactor
landed (commits c02fab3, 1944637, a691bac) that extracted
`CompoundToolDispatcher`, `StringUtils`, expanded `JsonParamParser`,
generalized `DirectorHelpers.ResolveType`, added `SetFields`, and fixed
Director `AddFrameContext` calls. These code changes do NOT invalidate
any units below — all units modify documentation, not code. The type
resolution precedence in Unit 10 already matches the refactored
`DirectorHelpers.ResolveType(typeName, baseType, label, out error)`
signature.

---

## Implementation Units

### Unit 1: CONTRACTS.md — Error Code Inventory Update

**File**: `docs/CONTRACTS.md`

Add the following error codes to the Error Codes table (after line 213):

```markdown
| `shader_not_found` | Named shader doesn't exist in project |
| `controller_not_found` | AnimatorController asset not found at path |
| `state_not_found` | Named state doesn't exist in controller layer |
| `track_not_found` | Named track doesn't exist in Timeline asset |
| `clip_index_out_of_range` | Clip index exceeds track's clip count |
| `navmesh_unavailable` | NavMesh not baked or AI Navigation package missing |
| `audio_mixer_api_unavailable` | AudioMixer internal API not accessible (fragile serialization path) |
| `blend_tree_not_found` | State does not contain a BlendTree motion |
| `operation_not_supported` | Operation exists but is unavailable in current context |
```

**Implementation Notes**:
- These codes are introduced in phases 7b–11 but were never added to the
  central error code table.
- Keep alphabetical grouping by domain: asset errors, animation errors,
  spatial errors.
- Each code must have a corresponding `suggestion` field when returned.

**Acceptance Criteria**:
- [ ] All error codes from phases 5–13 designs are present in CONTRACTS.md
- [ ] No duplicate codes in the table
- [ ] Every code has a one-line description

---

### Unit 2: CONTRACTS.md — Director Response Envelope Specification

**File**: `docs/CONTRACTS.md`

Add a new section after "Director Result Envelope" (after line 158):

```markdown
### Director Response Envelope (Full Specification)

All Director mutations return a consistent envelope. The specific fields
depend on the operation type:

**Hierarchy operations** (create_gameobject, reparent, duplicate, etc.):
```json
{
    "result": "ok",
    "operation": "create_gameobject",
    "path": "/Enemy",
    "instance_id": 19500,
    "frame": 4580,
    "time": 76.33,
    "play_mode": false
}
```

**Asset operations** (create_prefab, material_op:create, etc.):
```json
{
    "result": "ok",
    "operation": "create_prefab",
    "asset_path": "Assets/Prefabs/Enemy.prefab"
}
```

**Setting operations** (set_component, set_properties, etc.):
```json
{
    "result": "ok",
    "operation": "set_component",
    "path": "/Enemy",
    "component": "Health",
    "properties_set": 3,
    "errors": []
}
```

**Read-only Director operations** (list_overrides, list_tracks, etc.):
```json
{
    "result": "ok",
    "operation": "list_overrides",
    "results": [...]
}
```

**Rules**:
1. Always include `"result": "ok"` and `"operation": "<name>"`
2. Echo the primary resource identifier (`path`, `asset_path`, or
   `instance_id`) from the request
3. Include `frame`/`time`/`play_mode` only on hierarchy operations
   (scene objects have frame context; asset-only ops do not)
4. Complex nested data goes in named result fields, not a generic
   `"details"` wrapper — keep responses flat and scannable
5. Error responses follow the standard error envelope (code + message +
   suggestion)
```

**Acceptance Criteria**:
- [ ] Director response envelope rules are documented with examples
- [ ] Rules distinguish hierarchy vs asset vs setting vs read operations
- [ ] Frame context inclusion rule is explicit

---

### Unit 3: CONTRACTS.md — Dry Run Response Shape

**File**: `docs/CONTRACTS.md`

Add after the Token Budgeting section (after line 298):

```markdown
## Dry Run Mode

All Director operations support `dry_run: true`. When set, the server
validates inputs without mutating state.

### Dry Run Response

```json
{
    "dry_run": true,
    "would_succeed": true,
    "operation": "create_gameobject"
}
```

### Dry Run with Validation Errors

```json
{
    "dry_run": true,
    "would_succeed": false,
    "operation": "create_gameobject",
    "errors": [
        {
            "field": "components[0].type",
            "error": "type_not_found",
            "value": "NonExistentComponent"
        }
    ]
}
```

### Rules

- `dry_run` responses are NOT MCP errors — they return as successful
  tool results with `"dry_run": true`
- `would_succeed` is `true` only if the operation would complete
  without error
- `errors` array is present only when `would_succeed` is `false`
- Each error has `field` (JSON path to the problematic input),
  `error` (error code from the Error Codes table), and `value`
  (the invalid value for context)
- Batch operations propagate `dry_run` to all inner operations and
  return per-operation dry run results
```

**Acceptance Criteria**:
- [ ] Dry run response shape is fully specified
- [ ] Batch dry run behavior is documented
- [ ] Distinction from error responses is clear

---

### Unit 4: CONTRACTS.md — Token Budget Algorithm

**File**: `docs/CONTRACTS.md`

Add to the Token Budgeting section (after "Hard Cap" on line 298):

```markdown
### Token Estimation Algorithm

Theatre estimates response token count as:

    estimated_tokens = json_string.Length / 4

This approximation (4 characters per token) is conservative for English
text and JSON. It overestimates slightly for repetitive JSON structures
(field names, brackets), which is preferred — responses stay within budget
rather than exceeding it.

The `TokenBudget` class tracks consumed tokens incrementally as the
response is built:

```csharp
var budget = new TokenBudget(requestedBudget);

// Check before adding each item
if (budget.WouldExceed(itemJson.Length))
{
    // Stop adding items, report truncation
    break;
}
budget.Add(itemJson);
```

The hard cap of 4000 tokens (~16,000 characters) applies regardless of
the requested budget.
```

**Acceptance Criteria**:
- [ ] Token estimation formula is documented
- [ ] TokenBudget usage pattern is shown
- [ ] Hard cap is restated in context

---

### Unit 5: CONTRACTS.md — Watch Notification Payload

**File**: `docs/CONTRACTS.md`

Expand the Notification Format section (line 346) with watch trigger payload details:

```markdown
### Watch Triggered Notification Payload

```json
{
    "jsonrpc": "2.0",
    "method": "notifications/theatre/watch_triggered",
    "params": {
        "watch_id": "w_01",
        "label": "enemy_low_health",
        "frame": 5200,
        "time": 86.67,
        "trigger": {
            "path": "/Enemies/Scout_02",
            "instance_id": 14820,
            "condition": "threshold",
            "property": "current_hp",
            "value": 22,
            "threshold": 25
        }
    }
}
```

**Fields**:
- `watch_id` (string): The watch identifier from `watch:create` response
- `label` (string, optional): Human-readable label if provided at creation
- `frame` (int): Frame number when the trigger fired
- `time` (float): Game time when the trigger fired
- `trigger.path` (string): Hierarchy path of the triggering object
- `trigger.instance_id` (int): InstanceID of the triggering object
- `trigger.condition` (string): The condition type that fired
- Remaining trigger fields vary by condition type
```

**Acceptance Criteria**:
- [ ] Watch notification payload is fully documented with all fields
- [ ] Field types are specified
- [ ] Condition-dependent fields noted

---

### Unit 6: CONTRACTS.md — Persistence Layer Rules

**File**: `docs/CONTRACTS.md`

Add after the State Management notes or at the end:

```markdown
## Persistence Layers

Theatre uses two persistence mechanisms with distinct lifecycles:

| Layer | Survives Domain Reload | Survives Editor Restart | Use For |
|---|---|---|---|
| `SessionState` | Yes | No | Transient session data: watches, active recording, activity log, MCP session ID |
| `EditorPrefs` | Yes | Yes | Permanent config: welcome dialog shown, user preferences |

**Rules**:
- Short-lived data that should reset on editor restart → `SessionState`
- Permanent settings that persist across restarts → `EditorPrefs`
- `TheatreConfig` properties (Port, EnabledGroups) use `SessionState`
  for fast access but `TheatreSettingsProvider` writes to
  `EditorPrefs` for persistence
- Watch definitions: `SessionState` (via `WatchPersistence`)
- Recording clip index: `SessionState` (via `RecordingPersistence`)
- Tool group presets, gizmo settings: `EditorPrefs`
- Welcome dialog "don't show again": `EditorPrefs`
```

**Acceptance Criteria**:
- [ ] Both persistence layers documented with lifecycle rules
- [ ] Examples map each data type to correct layer

---

### Unit 7: ARCHITECTURE.md — Optional Package Dependencies Update

**File**: `docs/ARCHITECTURE.md`

Update the Optional Package Dependencies table (line 744) to add the
missing package:

```markdown
| Director Tool Group | Required Package |
|---|---|
| Tilemap operations | `com.unity.2d.tilemap` (built-in) |
| NavMesh operations | `com.unity.ai.navigation` |
| ProBuilder operations | `com.unity.probuilder` |
| Timeline operations | `com.unity.timeline` |
| Addressable operations | `com.unity.addressables` |
| Input System operations | `com.unity.inputsystem` |
| ECS tools | `com.unity.entities` |
```

**Acceptance Criteria**:
- [ ] `com.unity.ai.navigation` is listed in the optional packages table
- [ ] All packages that have `#if THEATRE_HAS_*` guards are listed

---

### Unit 8: Phase 0/1/2 Designs — Fix JSON Serialization References

**Files**:
- `docs/designs/phase-0-scaffold.md`
- `docs/designs/phase-1-mcp-core.md`
- `docs/designs/phase-2-scene-awareness.md`

Replace all references to `System.Text.Json` with `Newtonsoft.Json`:

In **phase-0-scaffold.md**, find/replace:
- `System.Text.Json` → `Newtonsoft.Json`
- `JsonSerializer.Serialize` → `JsonConvert.SerializeObject`
- `JsonSerializer.Deserialize` → `JsonConvert.DeserializeObject`
- `JsonElement` → `JToken`
- `[JsonPropertyName("x")]` → `[JsonProperty("x")]`

In **phase-1-mcp-core.md**, the `JsonRpc.cs` unit uses `System.Text.Json`
extensively (the `[JsonPropertyName]` attributes, `JsonSerializer` calls).
Replace all with Newtonsoft equivalents:
- `using System.Text.Json;` → `using Newtonsoft.Json;`
- `using System.Text.Json.Serialization;` → `using Newtonsoft.Json.Linq;`
- `[JsonPropertyName("x")]` → `[JsonProperty("x")]`
- `[JsonIgnore(Condition = ...)]` → `[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]`
  or `[JsonIgnore]`
- `JsonSerializer.Serialize(obj)` → `JsonConvert.SerializeObject(obj)`
- `JsonSerializer.Deserialize<T>(json)` → `JsonConvert.DeserializeObject<T>(json)`

In **phase-2-scene-awareness.md**, same pattern for any `System.Text.Json`
usage.

**Implementation Notes**:
- The codebase already uses Newtonsoft exclusively (confirmed by reading
  `ResponseHelpers.cs`, `DirectorHelpers.cs`, `RecordingTypes.cs`).
  These design doc fixes align the designs with the implementation.
- The `readOnlyHint` field name in `McpToolAnnotations` uses camelCase
  per the MCP protocol specification (not Theatre's wire format). This
  is correct — MCP protocol fields follow MCP conventions. NOT a bug.

**Acceptance Criteria**:
- [ ] Zero references to `System.Text.Json` in any design file
- [ ] All JSON examples use Newtonsoft types (JObject, JArray, JToken)
- [ ] `[JsonProperty]` used instead of `[JsonPropertyName]`

---

### Unit 9: Phase 5 Design — Recording Delta & Thread Safety Addendum

**File**: `docs/designs/phase-5-recording.md`

Add a new section after the Architecture section:

```markdown
## Delta Compression Specification

### Frame Types

- **Frame 0** (full snapshot): Every tracked property for every tracked
  object is captured. This is the baseline for delta reconstruction.
- **Subsequent frames** (delta): Only properties whose values differ
  from the previous frame are stored. If no properties changed for an
  object, that object is omitted from the frame entirely.
- **Keyframes**: Every 300 frames (configurable via
  `RecordingEngine.KeyframeInterval`), a full snapshot is captured
  regardless of changes. This bounds worst-case reconstruction cost.

### Frame Reconstruction

To reconstruct the full state at frame N:

1. Find the nearest keyframe at or before N (frame 0, 300, 600, ...)
2. Start with the keyframe's full property set
3. Apply each subsequent delta frame up to N
4. Unobserved properties retain their last-seen value

**Example**:
```
Frame 0 (full):  { "/Player": { position: [1,2,3], hp: 100 } }
Frame 1 (delta): { "/Player": { position: [1.1,2,3] } }
Frame 2 (delta): {}  // nothing changed
Reconstruct(2) → { "/Player": { position: [1.1,2,3], hp: 100 } }
```

### Flush Strategy

- Frames buffer in memory (`_frameBuffer`, capacity 10)
- Flushed to SQLite every `FlushInterval` frames (default 10) or
  every 0.5 seconds, whichever comes first
- On domain reload: buffered-but-unflushed frames are **lost**
  (typically 0–9 frames, ~150ms at 60fps)
- On resume after reload: recording continues from
  `metadata.EndFrame + 1`, where EndFrame reflects the last
  flushed frame
- Agents can detect the gap via `query_range` — missing frames
  return no data rather than interpolated values

### Thread Safety

RecordingEngine methods have the following threading constraints:

| Method | Thread | Notes |
|---|---|---|
| `Initialize()` | Main thread | Called from TheatreServer startup |
| `StartRecording()` | Main thread | HTTP handlers must use `MainThreadDispatcher.Invoke()` |
| `StopRecording()` | Main thread | Same |
| `InsertMarker()` | Main thread | Same |
| `Tick()` | Main thread | Called from `EditorApplication.update` |
| `IsRecording` (getter) | Any thread | Read-only bool, safe without lock |
| `ActiveState` (getter) | Any thread | Reference read is atomic in .NET |
| `ClipIndex` (getter) | Main thread | List not thread-safe for enumeration |

The RecordingTool handler (HTTP thread pool) calls `StartRecording`,
`StopRecording`, `InsertMarker` via `MainThreadDispatcher.Invoke()`.
Read-only queries (`list_clips`, `clip_info`, `query_range`,
`diff_frames`, `analyze`) open their own `RecordingDb` read connection
and can run on the HTTP thread directly — SQLite WAL mode supports
concurrent readers.
```

Also add to RecordingPersistence section:

```markdown
### SessionState Keys

| Key | Type | Content |
|---|---|---|
| `Theatre_Recording_Active` | string (JSON) | Serialized `RecordingState` or empty |
| `Theatre_Recording_Counter` | int | Next clip ID counter |
| `Theatre_Recording_ClipIndex` | string (JSON) | Serialized `List<ClipMetadata>` |

Only one recording can be active at a time. Starting a second returns
`recording_in_progress` error.
```

**Acceptance Criteria**:
- [ ] Delta compression semantics fully specified (frame 0, deltas, keyframes)
- [ ] Frame reconstruction algorithm documented with example
- [ ] Flush strategy and domain reload gap explicitly stated
- [ ] Thread safety table covers all public methods
- [ ] SessionState keys documented

---

### Unit 10: Phase 6a Design — Component Type Resolution Precedence

**File**: `docs/designs/phase-6-director-scenes-prefabs.md`

Replace the type resolution implementation notes (around line 144-149)
with this explicit precedence specification:

```markdown
**Type Resolution Precedence** (applied in order, first match wins):

1. **Fully qualified exact match**: Input contains `.` (e.g.,
   `"UnityEngine.UI.Image"`) → `assembly.GetType(typeName)` across
   all assemblies. Returns immediately if found.

2. **Short name unique match**: Input has no `.` (e.g., `"BoxCollider"`)
   → scan all assemblies for types where `type.Name == typeName` AND
   `typeof(Component).IsAssignableFrom(type)`. If exactly one match,
   return it.

3. **Short name ambiguous**: If step 2 finds multiple matches, return
   `type_ambiguous` error with all candidates listed by full qualified
   name:
   ```json
   {
       "error": {
           "code": "type_ambiguous",
           "message": "Multiple types match 'Image': UnityEngine.UI.Image, UnityEngine.UIElements.Image",
           "suggestion": "Use the fully qualified name: 'UnityEngine.UI.Image'"
       }
   }
   ```

4. **No match**: Return `type_not_found`:
   ```json
   {
       "error": {
           "code": "type_not_found",
           "message": "No Component type named 'NonExistent' found in any loaded assembly",
           "suggestion": "Use scene_inspect to see component types on existing objects. Check spelling and namespace."
       }
   }
   ```

**Notes**:
- Assembly-qualified names (e.g., `"Health, Assembly-CSharp"`) are NOT
  supported — use namespace-qualified names only
- The same resolution logic applies to `ResolveScriptableObjectType`
  but checks `typeof(ScriptableObject).IsAssignableFrom(type)` instead
- Resolution result is NOT cached — assemblies can change on recompile

**Position/Rotation Coordinate Space for CreateGameObject**:

When `parent` is specified, `position`, `rotation_euler`, and `scale`
are applied as **local coordinates** (relative to parent). When no
parent is specified, they are **world coordinates** (since root objects'
local = world). This matches Unity's `Transform.localPosition` behavior
on newly created objects.

**Duplicate Offset Semantics**:

`offset` is applied as a **world-space** additive displacement per copy.
Copy N gets: `sourceWorldPosition + offset * (N + 1)`. This creates an
evenly spaced line of copies. Example:

```json
{
    "operation": "duplicate",
    "path": "/Enemy",
    "count": 3,
    "offset": [2, 0, 0]
}
```
Creates 3 copies at worldPos+[2,0,0], worldPos+[4,0,0], worldPos+[6,0,0].
```

**Acceptance Criteria**:
- [ ] Type resolution has explicit 4-step precedence
- [ ] Ambiguous match error includes all candidate qualified names
- [ ] Position coordinate space documented (local when parented)
- [ ] Duplicate offset semantics documented with example

---

### Unit 11: Phase 6b Design — Batch Tool Group Membership

**File**: `docs/designs/phase-6b-batch.md`

Add clarification to the registration section:

```markdown
**Tool Group**: `ToolGroup.DirectorScene`

The `batch` tool belongs to `DirectorScene` because it orchestrates
scene-level operations. If `DirectorScene` is disabled, `batch` is
hidden from `tools/list`.

**Inner tool group enforcement**: Each operation within a batch is
validated against its own tool group. A batch can only contain tools
whose groups are currently enabled. If an inner operation's group is
disabled, the batch fails immediately with:

```json
{
    "result": "error",
    "operation": "batch",
    "error": {
        "code": "operation_not_supported",
        "message": "Tool 'prefab_op' is not available (DirectorPrefab group disabled)",
        "suggestion": "Enable the DirectorPrefab tool group or remove prefab operations from the batch"
    }
}
```
```

**Acceptance Criteria**:
- [ ] Batch tool group membership is explicit
- [ ] Inner tool group filtering semantics documented
- [ ] Error example for disabled inner tool provided

---

### Unit 12: Phase 7b Design — Audio Mixer API Probe

**File**: `docs/designs/phase-7b-media-assets.md`

Add API availability probe to the AudioMixerOpTool section:

```markdown
#### API Availability Probe

At registration time, AudioMixerOpTool tests whether the internal
SerializedObject path works by creating a temporary AudioMixer and
attempting to read its internal group structure:

```csharp
private static bool ProbeInternalApi()
{
    var temp = ScriptableObject.CreateInstance<AudioMixer>();
    try
    {
        var so = new SerializedObject(temp);
        var groups = so.FindProperty("m_MasterGroup");
        return groups != null;
    }
    catch
    {
        return false;
    }
    finally
    {
        Object.DestroyImmediate(temp);
    }
}
```

**If probe fails**: The tool still registers but fragile operations
(`add_group`, `add_effect`, `create_snapshot`, `expose_parameter`)
return `audio_mixer_api_unavailable` error immediately:

```json
{
    "error": {
        "code": "audio_mixer_api_unavailable",
        "message": "AudioMixer internal API not accessible in this Unity version. Read-only operations (create, set_volume) are available.",
        "suggestion": "Use 'create' and 'set_volume' operations, or modify the mixer manually in the Unity Editor"
    }
}
```

**If probe succeeds**: All 6 operations work normally.

This probe runs once at server startup (during tool registration).
The result is cached in a static `bool s_internalApiAvailable` field.
```

**Acceptance Criteria**:
- [ ] Probe function specified with exact SerializedObject path
- [ ] Failure behavior documented (error, not hidden)
- [ ] Cached result avoids repeated probing

---

### Unit 13: Phase 8b Design — BlendTree Children Assignment Fix

**File**: `docs/designs/phase-8b-blend-trees-timeline.md`

Replace the `set_thresholds` implementation (around line 128-139) with:

```markdown
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
```

Also add a note to `TimelineOpTool`:

```markdown
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
```

**Acceptance Criteria**:
- [ ] BlendTree children assignment documented with both direct and
      SerializedObject approaches
- [ ] Undo.RecordObject called before modification
- [ ] Timeline undo integration specified
- [ ] Timeline error codes specified

---

### Unit 14: Phase 11 Design — ECS Raycast Scope Decision

**File**: `docs/designs/phase-11-ecs.md`

Add to the EcsQueryTool section:

```markdown
### Raycast Support (Deferred)

ROADMAP Phase 11 lists `ecs_query` with raycast support. This is
**deferred to a future phase** because:

1. ECS raycasting requires `Unity.Physics` or `Havok.Physics` — these
   are separate packages from `com.unity.entities`
2. Most ECS projects don't have Unity Physics installed (many use
   custom physics or DOTS physics alternatives)
3. The three index-based queries (nearest, radius, overlap) cover the
   majority of spatial debugging use cases

**When Unity Physics is installed**: A future phase will add:
- `ecs_query:raycast` — single/multi-hit against physics world
- `ecs_query:linecast` — line-of-sight check

These will be guarded by `#if THEATRE_HAS_UNITY_PHYSICS` with a
separate `versionDefines` entry for `com.unity.physics`.

**When Unity Physics is NOT installed**: Calling raycast/linecast
returns:
```json
{
    "error": {
        "code": "package_not_installed",
        "message": "ECS raycast requires com.unity.physics package",
        "suggestion": "Install com.unity.physics via Package Manager, or use ecs_query:nearest as an alternative"
    }
}
```

Update ROADMAP.md Phase 11 to reflect this:
```
- [x] `ecs_query` — nearest, radius, overlap over entities with LocalTransform
- [ ] `ecs_query` — raycast, linecast (requires com.unity.physics, deferred)
```
```

Also add implementation guidance for unknown component reading:

```markdown
### Unknown Component Data Reading (EcsHelpers.ReadEntityComponents)

For components whose types are known at compile time (LocalTransform,
LocalToWorld, etc.), use direct typed access:

```csharp
if (em.HasComponent<LocalTransform>(entity))
{
    var lt = em.GetComponentData<LocalTransform>(entity);
    componentObj["position"] = ResponseHelpers.ToJArray(
        new Vector3(lt.Position.x, lt.Position.y, lt.Position.z));
    componentObj["rotation"] = ResponseHelpers.QuaternionToJArray(lt.Rotation);
    componentObj["scale"] = lt.Scale;
}
```

For unknown component types, use TypeManager + unsafe reads:

```csharp
// 1. Get all component types on the entity
var componentTypes = em.GetComponentTypes(entity);

// 2. For each type, get type info from TypeManager
foreach (var ct in componentTypes)
{
    var typeIndex = ct.TypeIndex;
    var typeInfo = TypeManager.GetTypeInfo(typeIndex);
    var typeName = typeInfo.DebugTypeName.ToString();

    // Skip zero-size (tag) components — they have no data
    if (typeInfo.IsZeroSized)
    {
        result[typeName] = new JObject { ["_tag"] = true };
        continue;
    }

    // Skip buffer, shared, managed components — too complex for MVP
    if (typeInfo.Category != TypeManager.TypeCategory.ComponentData)
    {
        result[typeName] = new JObject { ["_category"] = typeInfo.Category.ToString() };
        continue;
    }

    // 3. Read raw bytes via EntityManager
    // GetComponentDataRawRO returns a void* to the component data
    unsafe
    {
        var ptr = em.GetComponentDataRawRO(entity, typeIndex);
        var size = typeInfo.TypeSize;

        // 4. Use reflection on the managed Type to read fields
        var managedType = typeInfo.Type;
        if (managedType != null)
        {
            var fields = managedType.GetFields(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance);
            var obj = new JObject();
            foreach (var field in fields)
            {
                // Marshal field value from raw pointer
                var offset = System.Runtime.InteropServices.Marshal
                    .OffsetOf(managedType, field.Name).ToInt32();
                obj[ToSnakeCase(field.Name)] = ReadFieldValue(
                    (byte*)ptr + offset, field.FieldType);
            }
            result[typeName] = obj;
        }
        else
        {
            result[typeName] = new JObject { ["_raw_size"] = size };
        }
    }
}
```

**ReadFieldValue helper** handles: `int`, `float`, `bool`, `float3`,
`float4`, `quaternion`, `Entity` (serialize as `{index, version}`).
Unknown field types are reported as `"<TypeName>"` string.

**Safety**: This requires `allowUnsafeCode: true` in the editor asmdef.
Currently `false` — needs to be set to `true` for Phase 11.

**Limitations**:
- Managed components (class-based) are not readable via raw pointer
- Buffer components (DynamicBuffer) return count only, not contents
- Shared components return the shared value index, not the value
- Field offset via `Marshal.OffsetOf` may not match actual layout
  for all blittable structs — verify on key ECS types and add known-
  type fast paths for common Unity.Transforms and Unity.Physics types
```

**Acceptance Criteria**:
- [ ] ECS raycast explicitly deferred with justification
- [ ] ROADMAP update specified
- [ ] Unknown component reading has full implementation sketch
- [ ] Safety notes (allowUnsafeCode, limitations) documented

---

### Unit 15: Phase 9b Design — Phase Dependency Clarification

**File**: `docs/designs/phase-9b-terrain-probuilder.md`

Add note to Unit 4 (NavMeshOpTool Update):

```markdown
**Phase Dependency**: This unit modifies Phase 9a's `NavMeshOpTool` to
add terrain integration. Phase 9b must be implemented after Phase 9a.
This is consistent with the ROADMAP's sequential phase ordering — Phase
9a (Tilemap & NavMesh) is a prerequisite for Phase 9b (Terrain &
ProBuilder).

If Phase 9a has not been implemented yet, skip this unit and add the
terrain-NavMesh integration later as a follow-up.
```

**Acceptance Criteria**:
- [ ] Dependency on Phase 9a is explicit

---

### Unit 16: Phase 12a Design — UI Framework Clarification

**File**: `docs/designs/phase-12a-editor-window.md`

Add clarification to TheatreWindow section:

```markdown
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
```

**Acceptance Criteria**:
- [ ] UI framework choice (code-only) justified
- [ ] Timeline scrubber MVP scope clarified
- [ ] Welcome dialog dedup mechanism documented

---

### Unit 17: Phase 13 Design — Stress Test Limit Fix

**File**: `docs/designs/phase-13-polish-release.md`

Fix the watch count in stress tests to match configured max:

```markdown
**Watch stress test**: Create 20 watches (matching `TheatreConfig`
max watch count, not 32). Verify all persist across domain reload.
```

Also fix tool count reference:

```markdown
**Tool Reference**: Table of all registered tools. The exact count
depends on which optional packages are installed. With all packages:
~40 tools. Without optional packages: ~28 tools. The README should
say "40+ tools" rather than citing a specific number.
```

**Acceptance Criteria**:
- [ ] Stress test uses correct max watch count (20)
- [ ] Tool count is approximate, not hardcoded

---

### Unit 18: ROADMAP.md — Phase 11 Raycast Correction

**File**: `ROADMAP.md`

Update Phase 11 checklist item for ecs_query:

Replace:
```markdown
- [ ] `ecs_query` — nearest, radius, overlap, raycast over entities
  with LocalTransform
```

With:
```markdown
- [ ] `ecs_query` — nearest, radius, overlap over entities
  with LocalTransform
- [ ] `ecs_query` — raycast, linecast (requires com.unity.physics)
```

**Acceptance Criteria**:
- [ ] ECS raycast is a separate checklist item
- [ ] Dependency on com.unity.physics is noted

---

### Unit 19: Minor Design Fixes (Batch)

These are small fixes across multiple design files. Apply as a single
editing pass:

**Phase 3** (`docs/designs/phase-3-spatial-queries.md`):
- Add `navmesh_unavailable` to error handling section for `path_distance`
- Add note: "Layer names are mapped to layer masks via
  `LayerMask.NameToLayer(name)`. If the name doesn't match a defined
  layer, return `invalid_parameter` error."

**Phase 4** (`docs/designs/phase-4-watches-actions.md`):
- Add `invoke_method` supported parameter types:
  "Supported parameter types: `int`, `float`, `bool`, `string`,
  `Vector2`, `Vector3`, `Color`. Other types return `invalid_parameter`."
- Add watch ID persistence: "Watch ID counter is stored in
  `SessionState.SetInt("Theatre_WatchCounter", value)`. Watch
  definitions are serialized to
  `SessionState.SetString("Theatre_Watches", json)` via
  `WatchPersistence`."

**Phase 7b** (`docs/designs/phase-7b-media-assets.md`):
- Fix sprite pivot format: all vectors use array format per CONTRACTS.
  `"pivot": [0.5, 0.5]` not `"pivot": {"x": 0.5, "y": 0.5}`.

**Phase 10a** (`docs/designs/phase-10a-input-config.md`):
- Add note to `lighting_op:bake`: "Bake is fire-and-forget. Returns
  `{ result: 'ok', operation: 'bake', status: 'started' }` immediately.
  The agent can poll via `theatre_status` to check if baking is
  complete (future: add `lighting_op:bake_status` operation)."

**Acceptance Criteria**:
- [ ] Phase 3: layer mask conversion and navmesh error documented
- [ ] Phase 4: invoke_method types and watch persistence documented
- [ ] Phase 7b: sprite pivot uses array format
- [ ] Phase 10a: bake async behavior documented

---

## Implementation Order

```
1. Unit 1:  CONTRACTS.md — Error codes (foundational reference)
2. Unit 2:  CONTRACTS.md — Director response envelope
3. Unit 3:  CONTRACTS.md — Dry run response shape
4. Unit 4:  CONTRACTS.md — Token budget algorithm
5. Unit 5:  CONTRACTS.md — Watch notification payload
6. Unit 6:  CONTRACTS.md — Persistence layer rules
7. Unit 7:  ARCHITECTURE.md — Optional packages table
8. Unit 8:  Phase 0/1/2 — JSON serialization fix (bulk find/replace)
9. Unit 9:  Phase 5 — Recording delta + thread safety addendum
10. Unit 10: Phase 6a — Type resolution precedence + coordinates
11. Unit 11: Phase 6b — Batch tool group membership
12. Unit 12: Phase 7b — Audio mixer API probe
13. Unit 13: Phase 8b — BlendTree fix + Timeline undo
14. Unit 14: Phase 11 — ECS raycast scope + component reading
15. Unit 15: Phase 9b — Phase dependency clarification
16. Unit 16: Phase 12a — UI framework + timeline scope
17. Unit 17: Phase 13 — Stress test limits
18. Unit 18: ROADMAP.md — Phase 11 raycast split
19. Unit 19: Minor fixes (batch across multiple files)
```

Units 1-7 (reference docs) should be done first — they establish the
canonical spec that all phase designs reference. Units 8-19 can be
done in any order after that.

---

## Testing

No code tests — these are documentation-only changes. Verification is
manual:

### Verification Checklist

1. `grep -r "System.Text.Json" docs/designs/` — returns zero results
2. All error codes mentioned in any design file exist in CONTRACTS.md
   error table
3. ARCHITECTURE.md optional packages table matches the
   `versionDefines` in the editor asmdef
4. Phase 11 ROADMAP entry matches Phase 11 design scope (no raycast
   in MVP)
5. Phase 5 design includes delta compression spec, thread safety table,
   and SessionState keys
6. Phase 6a design includes type resolution precedence (4 steps) and
   coordinate space rules
7. Phase 8b design includes both direct and SerializedObject approaches
   for BlendTree children
8. Phase 13 stress test uses max watch count of 20 (not 32)

---

## Issue-to-Unit Mapping

For traceability, here's how each audit issue maps to a fix unit:

### Critical Issues

| Issue | Unit | Description |
|---|---|---|
| C1 — JSON serialization | Unit 8 | Fix System.Text.Json refs in phase 0/1/2 designs |
| C2 — ECS raycast scope | Units 14, 18 | Defer raycast, update ROADMAP |
| C3 — ECS component reading | Unit 14 | Add implementation sketch |
| C4 — Recording delta semantics | Unit 9 | Full delta compression spec |
| C5 — Recording thread safety | Unit 9 | Thread safety table |
| C6 — Component type resolution | Unit 10 | 4-step precedence rules |
| C7 — Audio mixer API fragility | Unit 12 | API probe at registration |
| C8 — BlendTree children assignment | Unit 13 | Direct + SerializedObject fallback |
| C9 — Director response envelope | Unit 2 | Full envelope specification |

### Minor Issues

| Issue | Unit |
|---|---|
| Missing error codes in CONTRACTS.md | Unit 1 |
| Token budget algorithm undocumented | Unit 4 |
| Watch notification payload undocumented | Unit 5 |
| Persistence layer rules undocumented | Unit 6 |
| `com.unity.ai.navigation` missing from ARCHITECTURE.md | Unit 7 |
| Batch tool group membership unclear | Unit 11 |
| Phase 9b dependency on 9a unclear | Unit 15 |
| Phase 12a UI framework choice unclear | Unit 16 |
| Phase 13 stress test uses wrong watch count | Unit 17 |
| CreateGameObject coordinate space | Unit 10 |
| Duplicate offset semantics | Unit 10 |
| Layer mask conversion (Phase 3) | Unit 19 |
| invoke_method parameter types (Phase 4) | Unit 19 |
| Watch ID persistence mechanism (Phase 4) | Unit 19 |
| Sprite pivot format (Phase 7b) | Unit 19 |
| Bake async behavior (Phase 10a) | Unit 19 |
| readOnlyHint naming | N/A (false positive — MCP spec uses camelCase) |
| Timeline undo grouping | Unit 13 |
| Timeline error codes | Unit 13 |
| Dry run response shape | Unit 3 |
