# Design: Phase 7c — Director: Optional-Package Asset Tools

## Overview

Two asset tools that depend on optional Unity packages:
- `render_pipeline_op` — requires URP (`com.unity.render-pipelines.universal`)
  or HDRP (`com.unity.render-pipelines.high-definition`)
- `addressable_op` — requires Addressables (`com.unity.addressables`)

Both tools auto-hide from `tools/list` when their required package is not
installed. They use `#if` define constraints based on assembly presence
rather than runtime package checks.

---

## Package Detection Strategy

Unity's asmdef system supports `versionDefines` — when a package is
installed, a scripting define symbol is automatically set. We use this to
conditionally compile the tool code.

In the **editor asmdef** (`com.theatre.toolkit.editor.asmdef`), add:

```json
"versionDefines": [
    {
        "name": "com.unity.render-pipelines.universal",
        "expression": "",
        "define": "THEATRE_HAS_URP"
    },
    {
        "name": "com.unity.render-pipelines.high-definition",
        "expression": "",
        "define": "THEATRE_HAS_HDRP"
    },
    {
        "name": "com.unity.addressables",
        "expression": "",
        "define": "THEATRE_HAS_ADDRESSABLES"
    }
]
```

Each tool file wraps its entire body in `#if THEATRE_HAS_*`. The
`Register()` method is only called from `TheatreServer` inside the same
`#if` guard. When the package isn't installed, the class doesn't exist
and no tool is registered — it's invisible to agents.

---

## Architecture

```
Editor/Tools/Director/
  RenderPipelineOpTool.cs   — MCP tool: render_pipeline_op (guarded by #if THEATRE_HAS_URP || THEATRE_HAS_HDRP)
  AddressableOpTool.cs      — MCP tool: addressable_op (guarded by #if THEATRE_HAS_ADDRESSABLES)
```

---

## Implementation Units

### Unit 1: Editor Asmdef — Version Defines

**File**: `Packages/com.theatre.toolkit/Editor/com.theatre.toolkit.editor.asmdef` (modify)

Add `versionDefines` array (currently empty `[]`):

```json
{
  "versionDefines": [
    {
      "name": "com.unity.render-pipelines.universal",
      "expression": "",
      "define": "THEATRE_HAS_URP"
    },
    {
      "name": "com.unity.render-pipelines.high-definition",
      "expression": "",
      "define": "THEATRE_HAS_HDRP"
    },
    {
      "name": "com.unity.addressables",
      "expression": "",
      "define": "THEATRE_HAS_ADDRESSABLES"
    }
  ]
}
```

Also add assembly references needed for URP/HDRP/Addressables. Since the
editor asmdef has `overrideReferences: false`, it auto-resolves DLLs. But
it needs **assembly references** for URP:

```json
"references": [
    "com.theatre.toolkit.runtime",
    "Unity.RenderPipelines.Universal.Runtime",
    "Unity.RenderPipelines.Universal.Editor"
]
```

Wait — adding hard references to URP assemblies would break compilation
when URP is not installed. Instead, keep `references` as-is and use
`Type.GetType()` / reflection for URP APIs, similar to how
`AudioMixerOpTool` works. Alternatively, wrap the asmdef references in
`versionDefines` somehow — but asmdef `references` don't support
conditional compilation.

**Better approach**: Don't add URP assembly references to the main editor
asmdef. Instead, use **reflection** for all URP/HDRP/Addressables API
calls, just like AudioMixerOpTool uses reflection for AudioMixerController.
The `#if THEATRE_HAS_URP` guard ensures the code only compiles when URP
is installed, and within that guard we can use `using` statements for
URP types. But the asmdef needs the assembly reference...

**Final approach**: Add the URP/Addressables assembly references as
**optional references** using `versionDefines` in the asmdef. Unity
supports this: when the asmdef lists a reference and that assembly
doesn't exist, the reference is silently ignored IF the reference is
guarded by a `defineConstraints` or the file using it is wrapped in `#if`.

Actually, the simplest correct approach for Unity:
1. Add the assembly references to the editor asmdef
2. Wrap ALL code in the tool files with `#if THEATRE_HAS_*`
3. Wrap the registration calls in TheatreServer with the same `#if`
4. If the package is not installed, Unity won't resolve the assembly
   reference but the code that uses it is `#if`-ed out, so no error

This is the standard Unity pattern for optional package dependencies.

**Acceptance Criteria**:
- [ ] `THEATRE_HAS_URP` is defined when URP is installed
- [ ] `THEATRE_HAS_ADDRESSABLES` is defined when Addressables is installed
- [ ] Compilation succeeds when packages are missing (defines not set)

---

### Unit 2: RenderPipelineOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/RenderPipelineOpTool.cs`

**Entire file wrapped in `#if THEATRE_HAS_URP || THEATRE_HAS_HDRP`**.

```csharp
#if THEATRE_HAS_URP || THEATRE_HAS_HDRP
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEditor;
#if THEATRE_HAS_URP
using UnityEngine.Rendering.Universal;
using UnityEditor.Rendering.Universal;
#endif

namespace Theatre.Editor.Tools.Director
{
    public static class RenderPipelineOpTool
    {
        private static readonly JToken s_inputSchema;
        static RenderPipelineOpTool();
        public static void Register(ToolRegistry registry);
        private static string Execute(JToken arguments);

        internal static string CreateUrpAsset(JObject args);
        internal static string CreateHdrpAsset(JObject args);
        internal static string SetQualitySettings(JObject args);
        internal static string CreateRenderer(JObject args);
        internal static string AddRendererFeature(JObject args);
    }
}
#endif
```

**Registration**: name `"render_pipeline_op"`, group `ToolGroup.DirectorAsset`.

#### `create_urp_asset`
- Required: `asset_path` (must end in `.asset`)
- Optional: `settings` (JObject with rendering settings)
- `#if THEATRE_HAS_URP`:
  `var asset = UniversalRenderPipelineAsset.Create()` or
  `ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>()`
- Apply settings if provided (shadow distance, cascade count, MSAA, HDR, etc.)
- `AssetDatabase.CreateAsset(asset, assetPath)`
- Response with asset_path

**URP settings** (all optional):
| JSON field | URP property | Type |
|-----------|-------------|------|
| `hdr` | `supportsHDR` | bool |
| `msaa` | `msaaSampleCount` (1/2/4/8) | int |
| `render_scale` | `renderScale` | float |
| `shadow_distance` | `shadowDistance` | float |
| `shadow_cascades` | `shadowCascadeCount` | int (1/2/3/4) |
| `srp_batcher` | `useSRPBatcher` | bool |

#### `create_hdrp_asset`
- Required: `asset_path`
- `#if THEATRE_HAS_HDRP`:
  `ScriptableObject.CreateInstance<HDRenderPipelineAsset>()`
- If HDRP not installed: return error `"package_not_installed"` with
  suggestion to install `com.unity.render-pipelines.high-definition`

#### `set_quality_settings`
- Required: `asset_path`, `settings` (JObject)
- Load the pipeline asset
- Apply settings via the same property mapping
- `EditorUtility.SetDirty(asset)`

#### `create_renderer`
- Required: `asset_path` (for new renderer asset)
- Optional: `renderer_type` ("forward"/"deferred", default "forward")
- `#if THEATRE_HAS_URP`:
  `ScriptableObject.CreateInstance<UniversalRendererData>()`
  or `ForwardRendererData` depending on type
- Save as asset

#### `add_renderer_feature`
- Required: `asset_path` (renderer asset path), `feature_type` (string)
- Load renderer data, resolve feature type, add via
  `ScriptableRendererFeature.Create()` pattern
- This is complex — use `SerializedObject` on the renderer to add to
  the features list, similar to how Unity's editor does it

**Acceptance Criteria**:
- [ ] `create_urp_asset` produces a URP pipeline asset (when URP installed)
- [ ] `set_quality_settings` modifies pipeline settings
- [ ] Tool is invisible when neither URP nor HDRP is installed
- [ ] `create_hdrp_asset` returns `package_not_installed` when HDRP absent

---

### Unit 3: AddressableOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/AddressableOpTool.cs`

**Entire file wrapped in `#if THEATRE_HAS_ADDRESSABLES`**.

```csharp
#if THEATRE_HAS_ADDRESSABLES
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace Theatre.Editor.Tools.Director
{
    public static class AddressableOpTool
    {
        private static readonly JToken s_inputSchema;
        static AddressableOpTool();
        public static void Register(ToolRegistry registry);
        private static string Execute(JToken arguments);

        internal static string CreateGroup(JObject args);
        internal static string AddEntry(JObject args);
        internal static string RemoveEntry(JObject args);
        internal static string SetLabels(JObject args);
        internal static string ListGroups(JObject args);
        internal static string Analyze(JObject args);
    }
}
#endif
```

**Registration**: name `"addressable_op"`, group `ToolGroup.DirectorAsset`.

#### `create_group`
- Required: `name` (group name)
- Optional: `schemas` (array, default `["BundledAssetGroupSchema", "ContentUpdateGroupSchema"]`)
- Optional: `packing_mode` ("pack_together"/"pack_separately"/"pack_together_by_label")
- Get settings: `AddressableAssetSettingsDefaultObject.Settings`
- If null: `AddressableAssetSettingsDefaultObject.GetSettings(true)` to create
- `settings.CreateGroup(name, false, false, true, null, schemaTypes)`
- Response with group name

#### `add_entry`
- Required: `asset_path`, `group` (group name)
- Optional: `address` (string, defaults to asset path), `labels` (string array)
- Get settings, find group by name
- `var guid = AssetDatabase.AssetPathToGUID(assetPath)`
- `var entry = settings.CreateOrMoveEntry(guid, group, false, false)`
- If `address`: `entry.SetAddress(address)`
- If `labels`: for each label, `entry.SetLabel(label, true)`
- Response with address and labels

#### `remove_entry`
- Required: `asset_path`
- Get GUID, find entry via `settings.FindAssetEntry(guid)`
- `settings.RemoveAssetEntry(guid)`
- Response

#### `set_labels`
- Required: `asset_path`, `labels` (string array)
- Optional: `replace` (bool, default true — if false, adds to existing)
- Find entry, clear existing labels if replace, add new labels
- Response

#### `list_groups`
- Read-only. Get settings, iterate `settings.groups`
- For each group: name, schema types, entry count
- Optionally list entries per group (with budget)
- Response: `{ "result": "ok", "groups": [...] }`

#### `analyze`
- Run `AnalyzeSystem.AnalyzeAsync()` or the synchronous variant
- Return analysis rules and their results
- This is complex — implement as best-effort. If the API isn't
  straightforward, return an error suggesting manual analysis

**Acceptance Criteria**:
- [ ] Tool is invisible when Addressables package not installed
- [ ] `create_group` creates an addressable group (when package installed)
- [ ] `add_entry` marks an asset as addressable
- [ ] `list_groups` returns group information

---

### Unit 4: Server Integration

**File**: `Packages/com.theatre.toolkit/Editor/TheatreServer.cs` (modify)

Add after Phase 7b registrations, with `#if` guards:

```csharp
#if THEATRE_HAS_URP || THEATRE_HAS_HDRP
            RenderPipelineOpTool.Register(registry);     // Phase 7c
#endif
#if THEATRE_HAS_ADDRESSABLES
            AddressableOpTool.Register(registry);        // Phase 7c
#endif
```

**Acceptance Criteria**:
- [ ] Tools register when packages are installed
- [ ] No compile errors when packages are missing
- [ ] `tools/list` shows the tools only when packages present

---

## Implementation Order

```
Unit 1: Editor asmdef version defines
  └─ Unit 2: RenderPipelineOpTool (depends on THEATRE_HAS_URP define)
  └─ Unit 3: AddressableOpTool (depends on THEATRE_HAS_ADDRESSABLES define)
     └─ Unit 4: Server Integration
```

---

## Testing

### Tests: `Tests/Editor/OptionalPackageToolTests.cs`

Tests must also be guarded by `#if` since they reference types that
only exist when the package is installed.

```csharp
#if THEATRE_HAS_URP
[TestFixture]
public class RenderPipelineOpTests
{
    private string _tempDir;
    [SetUp] public void SetUp() { /* create Assets/_TheatreTest_URP */ }
    [TearDown] public void TearDown() { /* delete */ }

    [Test] public void CreateUrpAsset_ProducesAsset() { }
    [Test] public void SetQualitySettings_ModifiesAsset() { }
}
#endif

#if THEATRE_HAS_ADDRESSABLES
[TestFixture]
public class AddressableOpTests
{
    [Test] public void CreateGroup_CreatesGroup() { }
    [Test] public void ListGroups_ReturnsGroups() { }
}
#endif
```

The test asmdef also needs the `versionDefines` for `THEATRE_HAS_URP`
and `THEATRE_HAS_ADDRESSABLES`. Add them to the test asmdef too.

---

## Verification Checklist

1. `unity_console {"operation": "refresh"}` — recompile
2. `unity_console {"filter": "error"}` — no compile errors
3. `unity_tests {"operation": "run"}` — all tests pass
4. Verify `render_pipeline_op` appears in tools/list (URP is installed)
5. Verify `addressable_op` does NOT appear (Addressables not installed)
6. Manual: call `render_pipeline_op` `create_urp_asset` → verify .asset file
