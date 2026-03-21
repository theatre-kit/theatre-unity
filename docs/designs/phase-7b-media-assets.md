# Design: Phase 7b — Director: 2D & Media Asset Tools

## Overview

Three asset tools for 2D workflows and audio: `texture_op` for texture
import settings and sprite configuration, `sprite_atlas_op` for sprite
atlas management, and `audio_mixer_op` for audio mixer hierarchies.

All tools live in `Editor/Tools/Director/` under `ToolGroup.DirectorAsset`.

---

## Architecture

```
Editor/Tools/Director/
  TextureOpTool.cs        — MCP compound tool: texture_op (4 operations)
  SpriteAtlasOpTool.cs    — MCP compound tool: sprite_atlas_op (4 operations)
  AudioMixerOpTool.cs     — MCP compound tool: audio_mixer_op (6 operations)
```

---

## Implementation Units

### Unit 1: TextureOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/TextureOpTool.cs`

**Namespace**: `Theatre.Editor.Tools.Director`

```csharp
public static class TextureOpTool
{
    private static readonly JToken s_inputSchema;
    static TextureOpTool();
    public static void Register(ToolRegistry registry);
    private static string Execute(JToken arguments);

    internal static string Import(JObject args);
    internal static string SetImportSettings(JObject args);
    internal static string CreateSprite(JObject args);
    internal static string SpriteSheet(JObject args);
}
```

**Registration**: name `"texture_op"`, group `ToolGroup.DirectorAsset`.

#### `import`
- Required: `asset_path` (path to an existing image file in Assets/)
- Optional: `settings` (JObject of import settings to apply)
- Verify file exists: `System.IO.File.Exists(fullPath)` or
  `AssetDatabase.LoadAssetAtPath<Texture>(assetPath) != null`
- If settings provided: get `TextureImporter` via
  `AssetImporter.GetAtPath(assetPath) as TextureImporter`
- Apply settings (see below), then `importer.SaveAndReimport()`
- Response: `{ "result": "ok", "operation": "import", "asset_path": "...", "width": N, "height": N }`

**Import settings** (all optional in `settings` JObject):

| JSON field | TextureImporter property | Type |
|-----------|-------------------------|------|
| `texture_type` | `textureType` | string → `TextureImporterType` enum ("default", "sprite", "normal_map", "editor_gui", "lightmap") |
| `filter_mode` | `filterMode` | string → `FilterMode` enum ("point", "bilinear", "trilinear") |
| `wrap_mode` | `wrapMode` | string → `TextureWrapMode` enum ("repeat", "clamp", "mirror") |
| `max_size` | `maxTextureSize` | int (32, 64, 128, 256, 512, 1024, 2048, 4096, 8192) |
| `compression` | `textureCompression` | string → `TextureImporterCompression` enum ("none", "low", "normal", "high") |
| `srgb` | `sRGBTexture` | bool |
| `read_write` | `isReadable` | bool |
| `generate_mipmaps` | `mipmapEnabled` | bool |
| `pixels_per_unit` | `spritePixelsPerUnit` | float (only when sprite) |

#### `set_import_settings`
- Required: `asset_path`, `settings` (JObject)
- Same as `import` but for an already-imported texture
- Get `TextureImporter`, apply settings, `SaveAndReimport()`
- Response with applied settings count

#### `create_sprite`
- Required: `asset_path`
- Optional: `pixels_per_unit` (default 100), `pivot` ([x,y] default [0.5, 0.5]),
  `sprite_mode` ("single" or "multiple", default "single")
- Get TextureImporter, set:
  - `textureType = TextureImporterType.Sprite`
  - `spriteImportMode = SpriteImportMode.Single` or `.Multiple`
  - `spritePixelsPerUnit = pixelsPerUnit`
  - `spritePivot = new Vector2(pivot[0], pivot[1])`
- `SaveAndReimport()`
- Response with sprite details

#### `sprite_sheet`
- Required: `asset_path`
- Required: `mode` — `"grid"` or `"manual"`
- For grid: `cell_size` ([width, height] in pixels), optional `offset`, `padding`
- For manual: `sprites` array of `{ name, rect: [x,y,w,h], pivot: [x,y] }`
- Get TextureImporter, set `textureType = Sprite`, `spriteImportMode = Multiple`
- For grid: set `spritesheet` via
  `TextureImporterSettings` + `SpriteMetaData[]` generated from grid
- For manual: build `SpriteMetaData[]` from provided sprites array
- `importer.spritesheet = spriteMetaData`
- `SaveAndReimport()`
- Response with sprite count

**JSON Schema** properties:
- `operation` (required)
- `asset_path`, `settings`, `pixels_per_unit`, `pivot`, `sprite_mode`
- `mode`, `cell_size`, `offset`, `padding`, `sprites`
- `dry_run`

**Acceptance Criteria**:
- [ ] `set_import_settings` changes filter mode and max size
- [ ] `create_sprite` converts texture to sprite type
- [ ] `sprite_sheet` with grid creates sprite metadata
- [ ] Invalid asset_path returns error

---

### Unit 2: SpriteAtlasOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/SpriteAtlasOpTool.cs`

**Namespace**: `Theatre.Editor.Tools.Director`

```csharp
public static class SpriteAtlasOpTool
{
    private static readonly JToken s_inputSchema;
    static SpriteAtlasOpTool();
    public static void Register(ToolRegistry registry);
    private static string Execute(JToken arguments);

    internal static string Create(JObject args);
    internal static string AddEntries(JObject args);
    internal static string RemoveEntries(JObject args);
    internal static string Pack(JObject args);
}
```

**Registration**: name `"sprite_atlas_op"`, group `ToolGroup.DirectorAsset`.

#### `create`
- Required: `asset_path` (must end in `.spriteatlas`)
- Optional: `include_in_build` (bool, default true),
  `packing_settings` (JObject: `padding`, `enable_rotation`, `enable_tight_packing`)
- Create: `var atlas = new SpriteAtlas()`
- Set packing settings via `SpriteAtlasPackingSettings` struct
- Save: `AssetDatabase.CreateAsset(atlas, assetPath)`
- Response with asset_path

**SpriteAtlas API**:
- `using UnityEditor.U2D;` and `using UnityEngine.U2D;`
- `atlas.SetPackingSettings(packSettings)`
- `atlas.SetIncludeInBuild(includeInBuild)`

#### `add_entries`
- Required: `asset_path`, `entries` (string array of asset paths — folders or sprites)
- Load atlas: `AssetDatabase.LoadAssetAtPath<SpriteAtlas>(assetPath)`
- For each entry: load as `UnityEngine.Object`
- `atlas.Add(objects.ToArray())`
- `EditorUtility.SetDirty(atlas)`
- Response with added count

#### `remove_entries`
- Required: `asset_path`, `entries`
- Load atlas, load objects
- `atlas.Remove(objects.ToArray())`
- `EditorUtility.SetDirty(atlas)`
- Response with removed count

#### `pack`
- Required: `asset_path`
- Load atlas
- `SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget)`
- Response with pack result

**Acceptance Criteria**:
- [ ] `create` produces a .spriteatlas file
- [ ] `add_entries` adds sprites to an atlas
- [ ] `remove_entries` removes entries
- [ ] `pack` triggers atlas packing without error

---

### Unit 3: AudioMixerOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/AudioMixerOpTool.cs`

**Namespace**: `Theatre.Editor.Tools.Director`

```csharp
public static class AudioMixerOpTool
{
    private static readonly JToken s_inputSchema;
    static AudioMixerOpTool();
    public static void Register(ToolRegistry registry);
    private static string Execute(JToken arguments);

    internal static string Create(JObject args);
    internal static string AddGroup(JObject args);
    internal static string SetVolume(JObject args);
    internal static string AddEffect(JObject args);
    internal static string CreateSnapshot(JObject args);
    internal static string ExposeParameter(JObject args);
}
```

**Registration**: name `"audio_mixer_op"`, group `ToolGroup.DirectorAsset`.

**Important**: Unity's `AudioMixer` editor API is limited. Most operations
require `SerializedObject` manipulation of the mixer asset's internal
structure. The public API mainly covers:
- `AudioMixer` (the asset)
- `AudioMixerGroup` (groups within the mixer)
- `AudioMixer.FindMatchingGroups(string)`
- `AudioMixer.FindSnapshot(string)`

For operations that need internal access (add_group, add_effect,
create_snapshot, expose_parameter), use `SerializedObject` on the mixer
asset to access internal arrays. This is fragile but is the only approach
available without reflection into internal Unity APIs.

#### `create`
- Required: `asset_path` (must end in `.mixer`)
- Create via `UnityEditor.Audio.AudioMixerController` (this is the
  internal editor type that extends AudioMixer). Access it via:
  ```csharp
  var mixer = new UnityEditor.Audio.AudioMixerController();
  AssetDatabase.CreateAsset(mixer, assetPath);
  ```
  Actually, the simpler approach: `ObjectFactory.CreateInstance<AudioMixer>()`
  may not work. The reliable way is:
  ```csharp
  var mixer = UnityEngine.ScriptableObject.CreateInstance<UnityEditor.Audio.AudioMixerController>();
  AssetDatabase.CreateAsset(mixer, assetPath);
  ```
  If `AudioMixerController` is not accessible, fall back to:
  ```csharp
  // Use the same approach Unity's project window uses
  ProjectWindowUtil.CreateAsset(
      ScriptableObject.CreateInstance<AudioMixer>(), assetPath);
  ```
  **Best approach**: Try `ScriptableObject.CreateInstance` with the type
  resolved by name from Unity's assemblies. The type is
  `UnityEditor.Audio.AudioMixerController` in the `UnityEditor.CoreModule`
  assembly.
- Response with asset_path

#### `add_group`
- Required: `asset_path`, `name` (group name)
- Optional: `parent_group` (name of parent group, default "Master")
- Load mixer
- **Via AudioMixerController API** (if accessible):
  `mixer.AddChildGroup(parentGroup, name)`
- **Via SerializedObject fallback**:
  Use `new SerializedObject(mixer)`, find the internal groups array,
  create a new group entry
- Response with group name

#### `set_volume`
- Required: `asset_path`, `group` (group name), `volume` (float, decibels)
- Load mixer, find group: `mixer.FindMatchingGroups(groupName)`
- Set volume via exposed parameter or `AudioMixerGroup` serialized property
- The volume parameter is typically named "Volume" on the group's attenuation
- Use `mixer.SetFloat(paramName, volume)` if parameter is exposed, or
  set via SerializedObject on the group's output level
- Response with group and volume

#### `add_effect`
- Required: `asset_path`, `group` (group name), `effect` (effect type name)
- Effect types: "SFX Reverb", "Echo", "Chorus", "Distortion", "Low Pass",
  "High Pass", "Flange", "Pitch Shifter", "Compressor", "Limiter"
- This requires `SerializedObject` manipulation of the mixer group's
  internal effects array
- Response with group and effect added

#### `create_snapshot`
- Required: `asset_path`, `name` (snapshot name)
- Via `AudioMixerController`: `mixer.CreateNewSnapshot(name)`
- Or via SerializedObject: add to the snapshots array
- Response with snapshot name

#### `expose_parameter`
- Required: `asset_path`, `group` (group name), `parameter` (parameter name)
- Via `AudioMixerController`: `mixer.ExposeParameter(parameterGUID)`
- This is complex — each effect parameter has an internal GUID. Finding
  the right one requires walking the serialized data.
- If this proves too fragile, return an error suggesting manual exposure
  via the Unity Editor

**Implementation Strategy**:
For audio_mixer_op, prioritize `create`, `add_group`, and `set_volume`
(the most useful operations). For `add_effect`, `create_snapshot`, and
`expose_parameter`, implement best-effort with a clear error if the
internal API isn't accessible: `"Audio mixer internal API not accessible
in this Unity version. Use the Unity Editor's Audio Mixer window instead."`

**JSON Schema** properties:
- `operation` (required)
- `asset_path`, `name`, `parent_group`, `group`, `volume`, `effect`, `parameter`
- `dry_run`

**Acceptance Criteria**:
- [ ] `create` produces a .mixer file
- [ ] `add_group` adds a child group to the mixer
- [ ] `set_volume` sets a group's volume level
- [ ] `add_effect` adds an effect or returns a clear error
- [ ] `create_snapshot` creates a snapshot or returns a clear error
- [ ] `expose_parameter` exposes a parameter or returns a clear error

---

### Unit 4: Server Integration

**File**: `Packages/com.theatre.toolkit/Editor/TheatreServer.cs` (modify)

Add after Phase 7a registrations:
```csharp
TextureOpTool.Register(registry);        // Phase 7b
SpriteAtlasOpTool.Register(registry);    // Phase 7b
AudioMixerOpTool.Register(registry);     // Phase 7b
```

---

## Implementation Order

```
Unit 1: TextureOpTool (independent)
Unit 2: SpriteAtlasOpTool (independent)
Unit 3: AudioMixerOpTool (independent, most complex)
Unit 4: Server Integration
```

All three tools are independent — they don't depend on each other.

---

## Testing

### Tests: `Tests/Editor/MediaAssetToolTests.cs`

```csharp
[TestFixture]
public class TextureOpTests
{
    private string _tempDir;
    [SetUp] public void SetUp() { /* create Assets/_TheatreTest_Tex */ }
    [TearDown] public void TearDown() { /* delete temp dir */ }

    [Test] public void SetImportSettings_ChangesFilterMode() { }
    [Test] public void CreateSprite_SetsTextureTypeToSprite() { }
    [Test] public void Import_NonexistentPath_ReturnsError() { }
}

[TestFixture]
public class SpriteAtlasOpTests
{
    private string _tempDir;
    [SetUp] public void SetUp() { /* create Assets/_TheatreTest_Atlas */ }
    [TearDown] public void TearDown() { /* delete temp dir */ }

    [Test] public void Create_ProducesAtlasFile() { }
    [Test] public void AddEntries_AddsToAtlas() { }
}

[TestFixture]
public class AudioMixerOpTests
{
    private string _tempDir;
    [SetUp] public void SetUp() { /* create Assets/_TheatreTest_Mixer */ }
    [TearDown] public void TearDown() { /* delete temp dir */ }

    [Test] public void Create_ProducesMixerFile() { }
    [Test] public void AddGroup_CreatesChildGroup() { }
}
```

---

## Verification Checklist

1. `unity_console {"operation": "refresh"}` — recompile
2. `unity_console {"filter": "error"}` — no compile errors
3. `unity_tests {"operation": "run"}` — all tests pass
4. Manual: call `texture_op` `create_sprite` on a test image
5. Manual: call `sprite_atlas_op` `create` + `add_entries`
6. Manual: call `audio_mixer_op` `create` + `add_group`
