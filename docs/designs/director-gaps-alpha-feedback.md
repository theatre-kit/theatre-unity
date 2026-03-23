# Feature: Director Gaps (Alpha Feedback)

## Summary

An agent used Theatre to build a complete game scene and hit four concrete
gaps that blocked or confused the workflow. These are all in the Director
layer — the tools that mutate scenes and assets. Fixing them turns Theatre
from "excellent for observation, weaker for construction" into a complete
scene-building tool.

Source: first external agent session report (alpha feedback).

## Requirements

### 1. ObjectReference Property Writes

`set_property` (ActionSetProperty) and `SetProperties` (DirectorHelpers)
currently reject `SerializedPropertyType.ObjectReference` with
`"Unsupported property type: ObjectReference"`. Reading works —
`PropertySerializer.BuildObjectReference` returns `{instance_id, asset_path,
name, type}` — but writing is completely missing.

**Acceptance criteria:**

- Agent can assign a material to a MeshRenderer via `set_property` using
  an asset path string: `{"property": "material", "value": "Assets/Materials/Foo.mat"}`
- Agent can assign a mesh to a MeshFilter: `{"property": "mesh", "value": "Assets/Models/Cube.fbx"}`
- Agent can assign a prefab reference to a MonoBehaviour field:
  `{"property": "enemy_prefab", "value": "Assets/Prefabs/Enemy.prefab"}`
- Accepts asset path (string) as the primary input format
- Also accepts instance_id (int) as alternative input format
- Also accepts null to clear the reference
- Sub-asset support: `"Assets/Models/Cube.fbx::Cube"` (mesh inside FBX)
  or equivalent syntax for accessing sub-assets
- Type validation: if the asset at the path is not assignable to the
  property's expected type, return a clear error with the expected type
- Both `ActionSetProperty.SetPropertyValue` and
  `DirectorHelpers.SetPropertyValue` must support this (they are
  currently duplicated — design should consider unifying)
- `create_gameobject` component properties also benefit (they flow
  through `DirectorHelpers.SetProperties`)

### 2. Primitive GameObject Creation

`create_gameobject` (`SceneOpHandlers.CreateGameObject`) uses
`new GameObject(name)` which creates empties. Agents cannot create visible
mesh objects (cubes, spheres, planes) without writing C# code.

**Acceptance criteria:**

- New optional `primitive_type` parameter on `create_gameobject`
- Supported values: `"cube"`, `"sphere"`, `"capsule"`, `"cylinder"`,
  `"plane"`, `"quad"` (matches `PrimitiveType` enum)
- When set, creates the primitive via `GameObject.CreatePrimitive()` and
  renames to `name`
- All other parameters (parent, position, rotation, scale, tag, layer,
  components) still apply on top
- When `primitive_type` is omitted, behavior is unchanged (empty GO)
- Dry run validates the primitive type name

### 3. Tags & Layers Feedback

`SetTagsAndLayers` (`ProjectSettingsOpTool`) silently overwrites layer
slots and always reports success. The agent called it twice for the same
layer and got `added_layers: []` both times — unclear if it worked.

**Acceptance criteria:**

- Response distinguishes three states per item:
  - `"added"` — newly set
  - `"already_exists"` — slot already had this exact name (no-op)
  - `"overwritten"` — slot had a different name that was replaced
    (include `previous_name`)
- Tags: same three-state reporting (added / already_exists / skipped)
- Sorting layers: same pattern
- Response arrays use objects with `{name, status}` (and `previous_name`
  when overwritten) instead of bare strings/names

### 4. Edit Mode Method Invocation

`invoke_method` (`ActionInvokeMethod`) unconditionally requires Play Mode
(line 26). Agents wanting to run editor automation (static setup methods,
menu items) must use hacky `[InitializeOnLoad]` workarounds.

**Acceptance criteria:**

- `invoke_method` supports calling **static methods** in Edit Mode
  - When `component` is omitted and `type` + `method` are provided,
    invokes a static method by reflection
  - Allowed in Edit Mode (no Play Mode gate for static calls)
  - Instance method calls on components still require Play Mode
    (existing behavior preserved)
- New operation `run_menu_item` on `action` tool (or on `scene_op`)
  - Takes `menu_path` string (e.g. `"GameObject/3D Object/Cube"` or
    `"MyGame/Setup Assets"`)
  - Calls `EditorApplication.ExecuteMenuItem(path)`
  - Returns success/failure (ExecuteMenuItem returns bool)
  - Edit Mode only (menu items are editor concepts)
  - Validates the menu path exists before executing

## Scope

**In scope:**
- ObjectReference writes via SerializedProperty (asset path + instance_id)
- Primitive creation parameter on create_gameobject
- Tags & layers response clarity
- Static method invocation in Edit Mode
- run_menu_item operation
- Tests for all new functionality
- Schema updates for modified tools

**Out of scope:**
- Arbitrary C# code execution (too broad, security implications)
- Play Mode static method invocation (not requested, unclear use case)
- Bulk property assignment API (existing component properties array is sufficient)
- MaterialPropertyBlock support (separate feature for runtime material overrides)
- New tools — all changes extend existing tools

## Technical Context

- **Existing code**: Two duplicate `SetPropertyValue` methods —
  `ActionSetProperty` (line 120) and `DirectorHelpers` (line 212). Both
  need the ObjectReference case. Design should unify these.
- **Asset resolution**: `AssetDatabase.LoadAssetAtPath<T>()` for path-based
  lookups, `EditorUtility.InstanceIDToObject()` for instance_id. Sub-asset
  loading via `AssetDatabase.LoadAllAssetsAtPath()` + name match.
- **Primitive creation**: `GameObject.CreatePrimitive(PrimitiveType)` is
  the Unity API. Must be called on main thread. Returns a fully configured
  GO with MeshFilter, MeshRenderer, and default Collider.
- **Menu items**: `EditorApplication.ExecuteMenuItem(string)` returns bool.
  Runs synchronously on main thread.
- **Wire format**: All new fields must follow CONTRACTS.md — snake_case,
  no abbreviations, structured error objects.
- **Threading**: All these operations use Unity APIs — must run on main
  thread via `MainThreadDispatcher.Invoke()`.
- **Undo**: All mutations must go through `Undo` system. Primitive creation
  needs `Undo.RegisterCreatedObjectUndo`. Property changes go through
  `SerializedObject.ApplyModifiedProperties` (already undo-aware).

## Open Questions

- Should ObjectReference writes support GUID strings as an input format
  (in addition to asset paths and instance_ids)?
- For `run_menu_item`, should there be a safelist/blocklist of allowed
  menu paths, or is any valid menu path acceptable?
- Should `create_gameobject` with `primitive_type` strip the default
  Collider that `CreatePrimitive` adds (some agents may not want it)?
- The two `SetPropertyValue` methods are duplicated — should design unify
  them into a single shared implementation, or keep them separate for
  different error-handling needs?
