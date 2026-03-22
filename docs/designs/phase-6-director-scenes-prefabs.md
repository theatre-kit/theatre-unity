# Design: Phase 6a — Director: Scenes & Prefabs

## Overview

First Director phase. Gives AI agents the ability to create and modify
scenes, GameObjects, components, and prefabs — everything whose file
formats are too fragile for agents to hand-write (GUIDs, binary
serialization, internal references).

All operations go through Unity's Undo system so the human can revert
any agent action with Ctrl+Z.

**Scope (6a)**: `scene_op` (10 operations), `prefab_op` (7 operations),
component type resolution, undo integration, asset path validation,
dry run mode.

**Deferred to 6b**: `batch` meta-tool (atomic multi-operation transactions).

**Exit criteria**: Agent can create a prefab with components, instantiate
it in a scene, apply overrides, and the human can undo it all with Ctrl+Z.

---

## Architecture

```
Editor/Tools/Director/
  SceneOpTool.cs             — MCP compound tool: scene_op (registration + dispatch)
  SceneOpHandlers.cs         — 10 operation handlers (create_scene through move_to_scene)
  PrefabOpTool.cs            — MCP compound tool: prefab_op (registration + dispatch)
  PrefabOpHandlers.cs        — 7 operation handlers (create_prefab through list_overrides)
  DirectorHelpers.cs         — Shared: type resolution, asset path validation, property setter, dry run
```

All files in `Editor/` assembly (namespace `Theatre.Editor.Tools.Director`).
Director operations require `UnityEditor` APIs (`AssetDatabase`,
`PrefabUtility`, `Undo`, `SerializedObject`, `SceneManagement`).

---

## Implementation Units

### Unit 1: DirectorHelpers — Shared Utilities

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/DirectorHelpers.cs`

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// Shared helpers for Director operations: type resolution, asset path
    /// validation, multi-property setting, and dry run support.
    /// </summary>
    internal static class DirectorHelpers
    {
        // --- Type Resolution ---

        /// <summary>
        /// Resolve a component type by name. Searches:
        /// 1. Exact match in UnityEngine/UnityEditor assemblies
        /// 2. Qualified name (e.g., "UnityEngine.UI.Image")
        /// 3. Script name search across all loaded assemblies
        /// Returns null if not found; populates error with details.
        /// If ambiguous, error lists candidates.
        /// </summary>
        public static Type ResolveComponentType(
            string typeName, out string error);

        // --- Asset Path Validation ---

        /// <summary>
        /// Validate an asset path. Must start with "Assets/" or "Packages/",
        /// use forward slashes, and have an appropriate extension.
        /// Returns null if valid, or an error response string if invalid.
        /// </summary>
        public static string ValidateAssetPath(
            string path, string requiredExtension = null);

        /// <summary>
        /// Check if an asset already exists at the path.
        /// Returns an error response if it does and overwrite is false.
        /// </summary>
        public static string CheckAssetConflict(
            string path, bool allowOverwrite = false);

        // --- Multi-Property Setter ---

        /// <summary>
        /// Set multiple properties on a component via SerializedObject.
        /// Properties is a JObject of key-value pairs.
        /// Returns (successCount, errors) where errors is a list of
        /// per-property error messages.
        /// </summary>
        public static (int successCount, List<string> errors)
            SetProperties(
                Component component,
                JObject properties);

        // --- Dry Run ---

        /// <summary>
        /// Check if dry_run is requested in args. If so, build and return
        /// a dry run response. Returns null if not a dry run.
        /// </summary>
        public static string CheckDryRun(
            JObject args,
            Func<(bool wouldSucceed, List<DryRunError> errors)> validator);

        // --- Undo Helpers ---

        /// <summary>
        /// Begin an undo group with a Theatre-prefixed name.
        /// Returns the group index for collapsing later.
        /// </summary>
        public static int BeginUndoGroup(string operationName);

        /// <summary>
        /// End and collapse the undo group.
        /// </summary>
        public static void EndUndoGroup(int groupIndex);
    }

    /// <summary>
    /// A single validation error for dry run responses.
    /// </summary>
    internal struct DryRunError
    {
        public string Field;
        public string Error;
        public string Value;
    }
}
```

**Implementation Notes**:
- **Type Resolution Precedence** (applied in order, first match wins):

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
- **SetProperties** iterates JObject properties, for each: finds the
  `SerializedProperty`, determines its type, sets the value using the same
  switch logic as `ActionSetProperty.SetPropertyValue`. Call
  `so.ApplyModifiedProperties()` once at the end.
- **Property name resolution**: Try the property name as-is, then with
  `m_` prefix, then PascalCase, then `m_` + PascalCase (same 4-step
  fallback as `ActionSetProperty`).
- **Dry run**: When `args["dry_run"]?.Value<bool>() == true`, call the
  validator function but don't execute. Return:
  ```json
  { "dry_run": true, "would_succeed": true/false, "errors": [...] }
  ```

**Acceptance Criteria**:
- [ ] `ResolveComponentType("BoxCollider")` returns `typeof(BoxCollider)`
- [ ] `ResolveComponentType("UnityEngine.UI.Image")` returns the Image type
- [ ] `ResolveComponentType("NonExistent")` returns null with error
- [ ] `ResolveComponentType("Collider")` returns `type_ambiguous` if both 2D and 3D exist
- [ ] `ValidateAssetPath("Assets/Scenes/X.unity")` returns null (valid)
- [ ] `ValidateAssetPath("foo/bar")` returns error (no Assets/ prefix)
- [ ] `SetProperties` sets multiple properties in one call
- [ ] Dry run returns validation result without mutation

---

### Unit 2: SceneOpTool — Registration and Dispatch

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/SceneOpTool.cs`

```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: scene_op
    /// Compound tool for scene and GameObject operations.
    /// Operations: create_scene, load_scene, unload_scene,
    ///   create_gameobject, delete_gameobject, reparent, duplicate,
    ///   set_component, remove_component, move_to_scene.
    /// </summary>
    public static class SceneOpTool
    {
        private static readonly JToken s_inputSchema;

        static SceneOpTool();

        public static void Register(ToolRegistry registry);

        private static string Execute(JToken arguments);
    }
}
```

**Registration**:
```csharp
registry.Register(new ToolRegistration(
    name: "scene_op",
    description: "Create and modify scenes and GameObjects. "
        + "Operations: create_scene, load_scene, unload_scene, "
        + "create_gameobject, delete_gameobject, reparent, duplicate, "
        + "set_component, remove_component, move_to_scene. "
        + "All operations support Undo and optional dry_run.",
    inputSchema: s_inputSchema,
    group: ToolGroup.DirectorScene,
    handler: Execute,
    annotations: new McpToolAnnotations { ReadOnlyHint = false }
));
```

**Dispatch** follows compound-tool-dispatch pattern:
```csharp
return operation switch
{
    "create_scene"      => SceneOpHandlers.CreateScene(args),
    "load_scene"        => SceneOpHandlers.LoadScene(args),
    "unload_scene"      => SceneOpHandlers.UnloadScene(args),
    "create_gameobject" => SceneOpHandlers.CreateGameObject(args),
    "delete_gameobject" => SceneOpHandlers.DeleteGameObject(args),
    "reparent"          => SceneOpHandlers.Reparent(args),
    "duplicate"         => SceneOpHandlers.Duplicate(args),
    "set_component"     => SceneOpHandlers.SetComponent(args),
    "remove_component"  => SceneOpHandlers.RemoveComponent(args),
    "move_to_scene"     => SceneOpHandlers.MoveToScene(args),
    _ => ResponseHelpers.ErrorResponse(...)
};
```

**JSON Schema** — large schema with all parameters. Key properties:
- `operation` (required, enum of all 10 operations)
- `path` / `instance_id` (target object for most ops)
- `name`, `parent`, `position`, `rotation_euler`, `scale` (for create_gameobject)
- `components` (array of `{type, properties}` for create_gameobject)
- `component`, `properties`, `add_if_missing` (for set_component)
- `new_parent`, `sibling_index`, `world_position_stays` (for reparent)
- `new_name`, `count`, `offset` (for duplicate)
- `paths`, `target_scene` (for move_to_scene)
- `template`, `open` (for create_scene)
- `mode` (for load_scene)
- `scene` (for unload_scene)
- `dry_run` (boolean, all operations)

**Acceptance Criteria**:
- [ ] All 10 operations dispatch correctly
- [ ] Unknown operation returns error with valid operation list
- [ ] Top-level try/catch logs `[Theatre]` prefixed error

---

### Unit 3: SceneOpHandlers — The 10 Scene Operations

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/SceneOpHandlers.cs`

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// Handlers for each scene_op operation.
    /// </summary>
    internal static class SceneOpHandlers
    {
        public static string CreateScene(JObject args);
        public static string LoadScene(JObject args);
        public static string UnloadScene(JObject args);
        public static string CreateGameObject(JObject args);
        public static string DeleteGameObject(JObject args);
        public static string Reparent(JObject args);
        public static string Duplicate(JObject args);
        public static string SetComponent(JObject args);
        public static string RemoveComponent(JObject args);
        public static string MoveToScene(JObject args);
    }
}
```

**Operation details:**

#### `CreateScene`
- Validate `path` via `DirectorHelpers.ValidateAssetPath(path, ".unity")`
- Check `DirectorHelpers.CheckAssetConflict(path)`
- Use `EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects)` or
  `NewSceneSetup.EmptyScene` based on `template`
- Save via `EditorSceneManager.SaveScene(scene, path)`
- If `open` is false, close and reopen previous scene
- Response: `{ "result": "ok", "operation": "create_scene", "path": "Assets/...", "scene": "Level2" }`

#### `LoadScene`
- Validate scene exists at `path` via `AssetDatabase.LoadAssetAtPath`
- `mode`: `"single"` → `OpenSceneMode.Single`, `"additive"` → `OpenSceneMode.Additive`
- Use `EditorSceneManager.OpenScene(path, mode)`
- Response: `{ "result": "ok", "operation": "load_scene", "scene": "...", "mode": "..." }`

#### `UnloadScene`
- Find scene by name or path via `SceneManager.GetSceneByName/GetSceneByPath`
- Verify it's loaded and not the only scene
- `EditorSceneManager.CloseScene(scene, removeScene: true)`
- Response: `{ "result": "ok", "operation": "unload_scene", "scene": "..." }`

#### `CreateGameObject`
- Required: `name`
- Create: `new GameObject(name)`
- If `parent`: resolve via `ObjectResolver.Resolve`, set parent
- If `position`/`rotation_euler`/`scale`: set `localPosition`, `localEulerAngles`, `localScale`
- If `components`: for each ComponentSpec, resolve type via `DirectorHelpers.ResolveComponentType`,
  `go.AddComponent(type)`, set properties via `DirectorHelpers.SetProperties`
- If `tag`/`layer`: set on the GameObject
- `Undo.RegisterCreatedObjectUndo(go, "Theatre CreateGameObject")`
- Response: `{ "result": "ok", "operation": "create_gameobject", "path": "/...", "instance_id": ..., "components_added": [...] }`
- `AddFrameContext` on response

**Position/Rotation Coordinate Space**: When `parent` is specified,
`position`, `rotation_euler`, and `scale` are applied as **local coordinates**
(relative to parent). When no parent is specified, they are **world coordinates**
(since root objects' local = world). This matches Unity's `Transform.localPosition`
behavior on newly created objects.

#### `DeleteGameObject`
- Resolve target via `ObjectResolver.ResolveFromArgs`
- `Undo.DestroyObjectImmediate(go)` for edit mode
- In play mode, use `Object.Destroy(go)` (not undoable in play mode)
- Response: `{ "result": "ok", "operation": "delete_gameobject", "path": "...", "instance_id": ... }`

#### `Reparent`
- Resolve `path` target and `new_parent` (if specified)
- `Undo.SetTransformParent(go.transform, newParent, "Theatre Reparent")`
- If `world_position_stays` (default true), use the overload parameter
- If `sibling_index` specified: `go.transform.SetSiblingIndex(index)`
- Response with old and new parent paths

#### `Duplicate`
- Resolve target
- `count` (default 1), `offset` (default null), `new_name`
- For each copy: `Undo.RegisterCreatedObjectUndo(Instantiate(go), ...)`
- Apply offset to each copy: `copy.transform.position += offset * i`
- Response with array of created objects (path + instance_id)

**Duplicate Offset Semantics**: `offset` is applied as a **world-space**
additive displacement per copy. Copy N gets:
`sourceWorldPosition + offset * (N + 1)`. This creates an evenly spaced line
of copies. Example: `"count": 3, "offset": [2, 0, 0]` creates copies at
worldPos+[2,0,0], worldPos+[4,0,0], worldPos+[6,0,0].

#### `SetComponent`
- Resolve target via `ObjectResolver.ResolveFromArgs`
- Resolve component type via `DirectorHelpers.ResolveComponentType`
- Find existing component or add if `add_if_missing` (default true)
- If adding: `Undo.AddComponent(go, type)`
- Set properties via `DirectorHelpers.SetProperties(component, properties)`
- Response: `{ "result": "ok", "operation": "set_component", "path": "...", "component": "...", "properties_set": 3, "errors": [] }`

#### `RemoveComponent`
- Resolve target and find component
- Cannot remove Transform
- `Undo.DestroyObjectImmediate(component)`
- Response with removed component type

#### `MoveToScene`
- Resolve each path in `paths` array
- Find target scene by name via `SceneManager.GetSceneByName`
- `SceneManager.MoveGameObjectToScene(go, targetScene)` for each root object
- Only root objects can be moved between scenes — child objects return error
- Response with count of moved objects

**Response envelope** for all operations:
```json
{
    "result": "ok",
    "operation": "<operation_name>",
    "<resource fields>": "...",
    "frame": ..., "time": ..., "play_mode": ...
}
```

**Acceptance Criteria**:
- [ ] CreateScene creates a .unity file at the specified path
- [ ] CreateGameObject creates object with name, components, and properties
- [ ] DeleteGameObject removes the object and is undoable
- [ ] Reparent moves object to new parent preserving world position
- [ ] SetComponent adds component if missing, sets multiple properties
- [ ] RemoveComponent removes the component (error if Transform)
- [ ] Duplicate creates N copies with offset
- [ ] MoveToScene moves root objects to target scene
- [ ] All operations register Undo
- [ ] All operations support dry_run

---

### Unit 4: PrefabOpTool — Registration and Dispatch

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/PrefabOpTool.cs`

```csharp
namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: prefab_op
    /// Compound tool for prefab lifecycle operations.
    /// Operations: create_prefab, instantiate, apply_overrides,
    ///   revert_overrides, unpack, create_variant, list_overrides.
    /// </summary>
    public static class PrefabOpTool
    {
        private static readonly JToken s_inputSchema;

        static PrefabOpTool();

        public static void Register(ToolRegistry registry);

        private static string Execute(JToken arguments);
    }
}
```

**Registration**: Group `ToolGroup.DirectorPrefab`, `ReadOnlyHint = false`.

**Dispatch**:
```csharp
return operation switch
{
    "create_prefab"    => PrefabOpHandlers.CreatePrefab(args),
    "instantiate"      => PrefabOpHandlers.Instantiate(args),
    "apply_overrides"  => PrefabOpHandlers.ApplyOverrides(args),
    "revert_overrides" => PrefabOpHandlers.RevertOverrides(args),
    "unpack"           => PrefabOpHandlers.Unpack(args),
    "create_variant"   => PrefabOpHandlers.CreateVariant(args),
    "list_overrides"   => PrefabOpHandlers.ListOverrides(args),
    _ => ResponseHelpers.ErrorResponse(...)
};
```

**Acceptance Criteria**:
- [ ] All 7 operations dispatch correctly
- [ ] Registration uses `ToolGroup.DirectorPrefab`

---

### Unit 5: PrefabOpHandlers — The 7 Prefab Operations

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/PrefabOpHandlers.cs`

```csharp
namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// Handlers for each prefab_op operation.
    /// </summary>
    internal static class PrefabOpHandlers
    {
        public static string CreatePrefab(JObject args);
        public static string Instantiate(JObject args);
        public static string ApplyOverrides(JObject args);
        public static string RevertOverrides(JObject args);
        public static string Unpack(JObject args);
        public static string CreateVariant(JObject args);
        public static string ListOverrides(JObject args);
    }
}
```

**Operation details:**

#### `CreatePrefab`
- Resolve `source_path` to a scene GameObject
- Validate `asset_path` (must end in `.prefab`, start with `Assets/`)
- Ensure parent directory exists: `Directory.CreateDirectory(Path.GetDirectoryName(fullPath))`
- `PrefabUtility.SaveAsPrefabAsset(go, assetPath, out success)`
- Response: `{ "result": "ok", "operation": "create_prefab", "asset_path": "...", "source_path": "..." }`

#### `Instantiate`
- Load prefab: `AssetDatabase.LoadAssetAtPath<GameObject>(prefab_path)`
- Error if not found: `prefab_not_found`
- `PrefabUtility.InstantiatePrefab(prefab)` as `GameObject`
- Set parent, position, rotation if specified
- If `name` specified, rename
- `Undo.RegisterCreatedObjectUndo(instance, "Theatre Instantiate")`
- Response: `{ "result": "ok", "operation": "instantiate", "path": "/...", "instance_id": ..., "prefab_path": "..." }`

#### `ApplyOverrides`
- Resolve `instance_path` to a GameObject
- Verify it's a prefab instance: `PrefabUtility.GetPrefabInstanceStatus(go) == PrefabInstanceStatus.Connected`
- If not: error `not_prefab_instance`
- `scope`:
  - `"all"`: `PrefabUtility.ApplyPrefabInstance(go, InteractionMode.UserAction)`
  - `"properties"` / `"added_components"` / `"added_objects"`: use
    `PrefabUtility.GetPropertyModifications` / `GetAddedComponents` /
    `GetAddedGameObjects` and apply selectively
- Response with applied override count

#### `RevertOverrides`
- Same resolution and validation as ApplyOverrides
- `scope`:
  - `"all"`: `PrefabUtility.RevertPrefabInstance(go, InteractionMode.UserAction)`
  - Selective: `PrefabUtility.RevertPropertyModifications` etc.
- Response with reverted count

#### `Unpack`
- Resolve instance, verify it's a prefab instance
- `mode`:
  - `"outermost"`: `PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.UserAction)`
  - `"completely"`: `PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.UserAction)`
- Response: `{ "result": "ok", "operation": "unpack", "path": "...", "mode": "..." }`

#### `CreateVariant`
- Load base prefab from `base_prefab` path
- Create variant: `PrefabUtility.InstantiatePrefab(basePrefab)` →
  modify if `overrides` specified → `PrefabUtility.SaveAsPrefabAsset(instance, asset_path)`
- Clean up the temp instance
- Response with asset_path and base_prefab

#### `ListOverrides`
- Resolve instance, verify prefab
- `PrefabUtility.GetPropertyModifications(go)` — collect property changes
- `PrefabUtility.GetAddedComponents(go)` — added components
- `PrefabUtility.GetAddedGameObjects(go)` — added child objects
- `PrefabUtility.GetRemovedComponents(go)` — removed components
- Response:
  ```json
  {
      "result": "ok",
      "operation": "list_overrides",
      "instance_path": "/...",
      "prefab_asset": "Assets/...",
      "property_modifications": [...],
      "added_components": [...],
      "added_game_objects": [...],
      "removed_components": [...]
  }
  ```
  This is a read-only operation: `ReadOnlyHint` doesn't apply per-operation
  (it's per-tool), but the implementation just reads — no undo needed.

**Acceptance Criteria**:
- [ ] CreatePrefab saves a .prefab file from a scene object
- [ ] Instantiate places a prefab instance in the scene
- [ ] ApplyOverrides writes overrides back to the prefab asset
- [ ] RevertOverrides restores instance to match its prefab
- [ ] Unpack disconnects from the prefab
- [ ] CreateVariant creates a prefab variant asset
- [ ] ListOverrides returns all override categories
- [ ] All mutating operations register Undo
- [ ] Not-a-prefab-instance returns `not_prefab_instance` error

---

### Unit 6: Server Integration

**File**: `Packages/com.theatre.toolkit/Editor/TheatreServer.cs` (modify)

1. Add `using Theatre.Editor.Tools.Director;`
2. In `RegisterBuiltInTools()`, add:
   ```csharp
   SceneOpTool.Register(registry);   // Phase 6
   PrefabOpTool.Register(registry);  // Phase 6
   ```

**Acceptance Criteria**:
- [ ] `scene_op` appears in `tools/list` when DirectorScene is enabled
- [ ] `prefab_op` appears in `tools/list` when DirectorPrefab is enabled
- [ ] Both hidden when their groups are disabled

---

## Implementation Order

```
Unit 1: DirectorHelpers (shared utilities — no dependencies)
  └─ Unit 2: SceneOpTool (registration shell)
  └─ Unit 4: PrefabOpTool (registration shell)
     └─ Unit 3: SceneOpHandlers (depends on helpers + tool shell)
     └─ Unit 5: PrefabOpHandlers (depends on helpers + tool shell)
        └─ Unit 6: Server Integration (depends on both tools)
```

Units 1-2-4 can be done first (foundations). Then 3 and 5 can be
parallelized. Unit 6 is last (wiring).

---

## Testing

### Unit Tests: `Tests/Editor/DirectorSceneTests.cs`

```csharp
[TestFixture]
public class DirectorHelpersTests
{
    [Test] public void ResolveComponentType_BuiltIn_ReturnsType() { }
    [Test] public void ResolveComponentType_Unknown_ReturnsNull() { }
    [Test] public void ValidateAssetPath_Valid_ReturnsNull() { }
    [Test] public void ValidateAssetPath_NoAssetsPrefix_ReturnsError() { }
    [Test] public void ValidateAssetPath_WrongExtension_ReturnsError() { }
    [Test] public void SetProperties_MultipleProps_SetsAll() { }
}

[TestFixture]
public class SceneOpTests
{
    // Setup: create a temp test scene
    // Teardown: destroy test objects, reload original scene

    [Test] public void CreateGameObject_WithName_CreatesAtRoot() { }
    [Test] public void CreateGameObject_WithParent_CreatesAsChild() { }
    [Test] public void CreateGameObject_WithComponents_AddsComponents() { }
    [Test] public void DeleteGameObject_RemovesFromScene() { }
    [Test] public void Reparent_MovesToNewParent() { }
    [Test] public void SetComponent_AddsAndSetsProperties() { }
    [Test] public void RemoveComponent_RemovesFromObject() { }
    [Test] public void RemoveComponent_Transform_ReturnsError() { }
    [Test] public void Duplicate_CreatesCopies() { }
    [Test] public void DryRun_DoesNotMutate() { }
}

[TestFixture]
public class PrefabOpTests
{
    // Setup: create temp scene + temp prefab directory
    // Teardown: cleanup temp assets

    [Test] public void CreatePrefab_SavesAsset() { }
    [Test] public void Instantiate_PlacesInScene() { }
    [Test] public void ListOverrides_ReturnsModifications() { }
    [Test] public void Unpack_DisconnectsFromPrefab() { }
    [Test] public void NotPrefabInstance_ReturnsError() { }
}
```

### Integration Tests

Direct tool handler invocation (same pattern as Phase 4-5 tests):

```csharp
[TestFixture]
public class DirectorToolIntegrationTests
{
    [Test] public void SceneOp_UnknownOperation_ReturnsError() { }
    [Test] public void PrefabOp_UnknownOperation_ReturnsError() { }
    [Test] public void SceneOp_CreateGameObject_ReturnsIdentity() { }
}
```

---

## Verification Checklist

1. `unity_console {"operation": "refresh"}` — recompile
2. `unity_console {"filter": "error"}` — no compile errors
3. `unity_tests {"operation": "run"}` — all tests pass
4. Manual: call `scene_op` `create_gameobject` → verify object in scene
5. Manual: call `scene_op` `set_component` → verify component with properties
6. Manual: Ctrl+Z → verify undo works
7. Manual: call `prefab_op` `create_prefab` → verify .prefab file
8. Manual: call `prefab_op` `instantiate` → verify instance in scene
9. Manual: call with `dry_run: true` → verify no side effects
