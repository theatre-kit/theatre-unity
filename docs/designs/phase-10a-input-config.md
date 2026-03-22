# Design: Phase 10a — Director: Input & Project Settings

## Overview

Four tools for input configuration and project settings:
- `input_action_op` — Input System action maps, actions, bindings, composites
- `lighting_op` — ambient light, fog, skybox, probes, lightmap baking
- `quality_op` — quality levels, shadows, rendering settings
- `project_settings_op` — physics, time, player settings, tags and layers

`input_action_op` requires `com.unity.inputsystem` (installed as 1.19.0) —
guarded by `#if THEATRE_HAS_INPUT_SYSTEM`.

`lighting_op`, `quality_op`, and `project_settings_op` use built-in Unity
APIs (`RenderSettings`, `QualitySettings`, `Physics`, `Time`, `PlayerSettings`,
`TagManager`).

`input_action_op` registers under `ToolGroup.DirectorInput`.
The other three register under `ToolGroup.DirectorConfig`.

---

## Architecture

```
Editor/Tools/Director/
  InputActionOpTool.cs      — MCP tool: input_action_op (7 ops, #if guarded)
  LightingOpTool.cs         — MCP tool: lighting_op (6 ops)
  QualityOpTool.cs          — MCP tool: quality_op (4 ops)
  ProjectSettingsOpTool.cs  — MCP tool: project_settings_op (4 ops)
```

---

## Implementation Units

### Unit 1: Asmdef — Input System Version Define

**File**: `Packages/com.theatre.toolkit/Editor/com.theatre.toolkit.editor.asmdef` (modify)

Add to `versionDefines`:
```json
{
    "name": "com.unity.inputsystem",
    "expression": "",
    "define": "THEATRE_HAS_INPUT_SYSTEM"
}
```

Add to `references`: `"Unity.InputSystem"`, `"Unity.InputSystem.Editor"`

**Test asmdef**: same additions.

---

### Unit 2: InputActionOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/InputActionOpTool.cs`

**Entire file wrapped in `#if THEATRE_HAS_INPUT_SYSTEM`**.

```csharp
#if THEATRE_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEditor;

namespace Theatre.Editor.Tools.Director
{
    public static class InputActionOpTool
    {
        internal static string CreateAsset(JObject args);
        internal static string AddActionMap(JObject args);
        internal static string AddAction(JObject args);
        internal static string AddBinding(JObject args);
        internal static string AddComposite(JObject args);
        internal static string SetControlScheme(JObject args);
        internal static string ListActions(JObject args);
    }
}
#endif
```

**Registration**: name `"input_action_op"`, group `ToolGroup.DirectorInput`.

#### `create_asset`
- Required: `asset_path` (must end in `.inputactions`)
- `var asset = ScriptableObject.CreateInstance<InputActionAsset>()`
  Actually, `InputActionAsset` is a ScriptableObject. Create it and save:
  ```csharp
  var asset = ScriptableObject.CreateInstance<InputActionAsset>();
  var json = asset.ToJson();
  System.IO.File.WriteAllText(assetPath, json);
  AssetDatabase.ImportAsset(assetPath);
  ```
  InputActionAssets are saved as JSON files (`.inputactions`), not binary `.asset`.
- Response with asset_path

#### `add_action_map`
- Required: `asset_path`, `name`
- Load: `AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath)`
- `asset.AddActionMap(name)`
- Save: write `asset.ToJson()` to disk, reimport
- Response

#### `add_action`
- Required: `asset_path`, `action_map` (map name), `name`, `type` ("value"/"button"/"pass_through")
- Find map: `asset.FindActionMap(actionMap)`
- `map.AddAction(name, type: actionType)`
- Map type: "value"→`InputActionType.Value`, "button"→`.Button`, "pass_through"→`.PassThrough`
- Save + reimport
- Response

#### `add_binding`
- Required: `asset_path`, `action_map`, `action`, `path` (binding path like "<Keyboard>/space")
- Optional: `interactions`, `processors`
- Find action: `map.FindAction(actionName)`
- `action.AddBinding(path).WithInteractions(interactions).WithProcessors(processors)`
- Save + reimport
- Response

#### `add_composite`
- Required: `asset_path`, `action_map`, `action`, `composite_type` (e.g. "2DVector", "1DAxis")
- Required: `bindings` (JObject mapping part names to paths: `{"up":"<Keyboard>/w", "down":"<Keyboard>/s", ...}`)
- Find action
- `var composite = action.AddCompositeBinding(compositeType)`
- For each binding: `composite.With(partName, path)`
- Save + reimport
- Response

#### `set_control_scheme`
- Required: `asset_path`, `name`
- Optional: `devices` (string array of device requirements like "<Keyboard>", "<Mouse>")
- `asset.AddControlScheme(name)` then add device requirements
- Save + reimport

#### `list_actions` (READ-ONLY)
- Required: `asset_path`
- Load asset, iterate maps → actions → bindings
- Response: `{ "result": "ok", "maps": [{ "name": "...", "actions": [{ "name": "...", "type": "...", "bindings": [...] }] }] }`

**Important**: InputActionAsset modifications use a builder pattern.
After ANY modification, you must serialize and write back:
```csharp
File.WriteAllText(assetPath, asset.ToJson());
AssetDatabase.ImportAsset(assetPath);
```

---

### Unit 3: LightingOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/LightingOpTool.cs`

**Registration**: name `"lighting_op"`, group `ToolGroup.DirectorConfig`.

6 operations: `set_ambient`, `set_fog`, `set_skybox`, `add_light_probe_group`, `add_reflection_probe`, `bake`

#### `set_ambient`
- Optional: `mode` ("color"/"gradient"/"skybox"), `color` ([r,g,b,a]),
  `sky_color`, `equator_color`, `ground_color` (for gradient), `intensity` (float)
- `RenderSettings.ambientMode = AmbientMode.Flat/Trilight/Skybox`
- `RenderSettings.ambientLight = color` (for flat)
- `RenderSettings.ambientSkyColor/ambientEquatorColor/ambientGroundColor` (for gradient)
- `RenderSettings.ambientIntensity = intensity`

#### `set_fog`
- Optional: `enabled` (bool), `mode` ("linear"/"exponential"/"exponential_squared"),
  `color` ([r,g,b,a]), `density` (float), `start_distance`, `end_distance`
- `RenderSettings.fog = enabled`
- `RenderSettings.fogMode = FogMode.Linear/Exponential/ExponentialSquared`
- Set corresponding properties

#### `set_skybox`
- Required: `material` (asset path to skybox Material)
- `RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>(path)`

#### `add_light_probe_group`
- Required: `path` (hierarchy path to attach to, or create new GO)
- Optional: `positions` (array of [x,y,z] probe positions), `name`
- Create or resolve GO, add `LightProbeGroup` component
- `group.probePositions = positions` (Vector3[])
- Response

#### `add_reflection_probe`
- Optional: `position` ([x,y,z]), `size` ([x,y,z] box extent),
  `resolution` (int), `name`
- Create GO with `ReflectionProbe` component
- Set `probe.size`, `probe.resolution`
- Response

#### `bake`
- `Lightmapping.BakeAsync()` — triggers async lightmap bake
- Or `Lightmapping.Bake()` for synchronous
- Response: `{ "result": "ok", "operation": "bake", "status": "started" }`

**Bake is fire-and-forget.** Returns `{ "result": "ok", "operation": "bake", "status": "started" }`
immediately without waiting for completion. The agent can poll via `theatre_status`
to check if baking is complete (future: add `lighting_op:bake_status` operation).

---

### Unit 4: QualityOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/QualityOpTool.cs`

**Registration**: name `"quality_op"`, group `ToolGroup.DirectorConfig`.

4 operations: `set_level`, `set_shadow_settings`, `set_rendering`, `list_levels`

#### `set_level`
- Required: `level` (int or string name)
- `QualitySettings.SetQualityLevel(level)` or find by name

#### `set_shadow_settings`
- Optional: `distance` (float), `resolution` (string: "low"/"medium"/"high"/"very_high"),
  `cascades` (int 0/1/2/4)
- `QualitySettings.shadowDistance = distance`
- `QualitySettings.shadowResolution = ShadowResolution.Low/Medium/High/VeryHigh`
- `QualitySettings.shadowCascades = cascades`

#### `set_rendering`
- Optional: `lod_bias` (float), `pixel_light_count` (int),
  `texture_quality` (int 0=full, 1=half, 2=quarter, 3=eighth),
  `anisotropic_filtering` ("disable"/"enable"/"force_enable"),
  `vsync` (int 0/1/2)
- Set corresponding `QualitySettings.*` properties

#### `list_levels` (READ-ONLY)
- `QualitySettings.names` — iterate all levels
- For each: report shadow settings, rendering settings
- Response

---

### Unit 5: ProjectSettingsOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/ProjectSettingsOpTool.cs`

**Registration**: name `"project_settings_op"`, group `ToolGroup.DirectorConfig`.

4 operations: `set_physics`, `set_time`, `set_player`, `set_tags_and_layers`

#### `set_physics`
- Optional: `gravity` ([x,y,z]), `default_material` (asset path),
  `bounce_threshold` (float), `default_solver_iterations` (int)
- `Physics.gravity = gravity`
- `Physics.bounceThreshold = threshold`
- For layer collision matrix: complex but can use
  `Physics.IgnoreLayerCollision(layer1, layer2, ignore)`

#### `set_time`
- Optional: `fixed_timestep` (float), `maximum_timestep` (float),
  `time_scale` (float)
- `Time.fixedDeltaTime = fixedTimestep`
- `Time.maximumDeltaTime = maximumTimestep`

#### `set_player`
- Optional: `company_name`, `product_name`, `version`
- `PlayerSettings.companyName = ...`
- `PlayerSettings.productName = ...`
- `PlayerSettings.bundleVersion = ...`

#### `set_tags_and_layers`
- Optional: `add_tags` (string array), `add_sorting_layers` (string array),
  `add_layers` (array of `{ "index": int, "name": string }`)
- Tags: use `SerializedObject` on `TagManager` asset:
  ```csharp
  var tagManager = new SerializedObject(
      AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
  var tags = tagManager.FindProperty("tags");
  ```
- Sorting layers: `tags.FindProperty("m_SortingLayers")`
- Layers: `tagManager.FindProperty("layers")` — set specific indices
- Response with what was added

---

### Unit 6: Server Integration

```csharp
#if THEATRE_HAS_INPUT_SYSTEM
            InputActionOpTool.Register(registry);       // Phase 10a
#endif
            LightingOpTool.Register(registry);          // Phase 10a
            QualityOpTool.Register(registry);           // Phase 10a
            ProjectSettingsOpTool.Register(registry);   // Phase 10a
```

---

## Implementation Order

```
Unit 1: Asmdef (Input System versionDefine)
Unit 2: InputActionOpTool (#if guarded)
Unit 3: LightingOpTool (independent)
Unit 4: QualityOpTool (independent)
Unit 5: ProjectSettingsOpTool (independent)
Unit 6: Server Integration
```

---

## Testing

### Tests: `Tests/Editor/InputConfigToolTests.cs`

```csharp
#if THEATRE_HAS_INPUT_SYSTEM
[TestFixture]
public class InputActionOpTests
{
    private string _tempDir;
    [SetUp]/[TearDown] — temp asset dir

    [Test] public void CreateAsset_ProducesInputActionsFile() { }
    [Test] public void AddActionMap_AddsMap() { }
    [Test] public void AddAction_AddsActionToMap() { }
    [Test] public void ListActions_ReturnsMapsAndActions() { }
}
#endif

[TestFixture]
public class LightingOpTests
{
    [Test] public void SetAmbient_ChangesAmbientColor() { }
    [Test] public void SetFog_EnablesFog() { }
}

[TestFixture]
public class QualityOpTests
{
    [Test] public void ListLevels_ReturnsQualityNames() { }
    [Test] public void SetLevel_ChangesActiveLevel() { }
}

[TestFixture]
public class ProjectSettingsOpTests
{
    [Test] public void SetTime_ChangesFixedTimestep() { }
    [Test] public void SetPlayer_ChangesCompanyName() { }
    [Test] public void SetTagsAndLayers_AddsTags() { }
}
```

---

## Verification Checklist

1. `unity_console {"operation": "refresh"}` — recompile
2. `unity_console {"filter": "error"}` — no compile errors
3. `unity_tests {"operation": "run"}` — all tests pass
4. Manual: `input_action_op` create_asset → add_action_map → add_action → add_composite (WASD)
5. Manual: `lighting_op` set_ambient → set_fog
6. Manual: `quality_op` list_levels → set_shadow_settings
7. Manual: `project_settings_op` set_tags_and_layers
