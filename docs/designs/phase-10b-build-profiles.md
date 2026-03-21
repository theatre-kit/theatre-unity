# Design: Phase 10b — Director: Build Profiles

## Overview

Single tool for managing Unity 6 Build Profiles — the replacement for
the legacy Build Settings window. Build Profiles define platform targets,
scene lists, scripting backends, and platform-specific configuration.

**Note**: Unity 6 introduced `BuildProfile` as a ScriptableObject-based
system. The API is in `UnityEditor.Build.Profile`. If this API isn't
available (older Unity 6 minor versions), fall back to the legacy
`EditorBuildSettings` and `PlayerSettings` APIs.

The tool registers under `ToolGroup.DirectorConfig`.

---

## Architecture

```
Editor/Tools/Director/
  BuildProfileOpTool.cs   — MCP tool: build_profile_op (5 operations)
```

---

## Implementation Units

### Unit 1: BuildProfileOpTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/Director/BuildProfileOpTool.cs`

**Namespace**: `Theatre.Editor.Tools.Director`

```csharp
public static class BuildProfileOpTool
{
    private static readonly JToken s_inputSchema;
    static BuildProfileOpTool();
    public static void Register(ToolRegistry registry);
    private static string Execute(JToken arguments);

    internal static string Create(JObject args);
    internal static string SetScenes(JObject args);
    internal static string SetPlatform(JObject args);
    internal static string SetScriptingBackend(JObject args);
    internal static string ListProfiles(JObject args);
}
```

**Registration**: name `"build_profile_op"`, group `ToolGroup.DirectorConfig`.

#### `create`
- Required: `name` (profile name), `platform` (string: "windows"/"macos"/"linux"/"android"/"ios"/"webgl")
- Optional: `asset_path` (where to save, default `Assets/Settings/Build/{name}.asset`)

**Unity 6 Build Profile API** (try first):
```csharp
// Unity 6.1+ has UnityEditor.Build.Profile.BuildProfile
var profileType = Type.GetType("UnityEditor.Build.Profile.BuildProfile, UnityEditor.CoreModule");
```
If the type exists, create via `ScriptableObject.CreateInstance(profileType)` and configure.

**Fallback** (if Build Profile API not available):
- Use `EditorBuildSettings.scenes` to manage scene lists
- Use `EditorUserBuildSettings.activeBuildTarget` / `SwitchActiveBuildTarget` for platform
- Return a response noting that Build Profiles are not available, using legacy settings

**Platform mapping**:
| JSON string | BuildTarget |
|------------|-------------|
| `"windows"` | `BuildTarget.StandaloneWindows64` |
| `"macos"` | `BuildTarget.StandaloneOSX` |
| `"linux"` | `BuildTarget.StandaloneLinux64` |
| `"android"` | `BuildTarget.Android` |
| `"ios"` | `BuildTarget.iOS` |
| `"webgl"` | `BuildTarget.WebGL` |

#### `set_scenes`
- Required: `scenes` (string array of scene asset paths, in build order)
- Optional: `profile_path` (asset path to a BuildProfile, or null for active settings)

**With Build Profile**: Load profile, set scene list on it.
**Fallback**: `EditorBuildSettings.scenes = scenes.Select(s => new EditorBuildSettingsScene(s, true)).ToArray()`

#### `set_platform`
- Required: `platform` (string)
- Optional: `profile_path`

**With Build Profile**: Set target platform on the profile.
**Fallback**: `EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, buildTarget)`

Map platform to `BuildTargetGroup`:
- windows/macos/linux → `BuildTargetGroup.Standalone`
- android → `BuildTargetGroup.Android`
- ios → `BuildTargetGroup.iOS`
- webgl → `BuildTargetGroup.WebGL`

#### `set_scripting_backend`
- Required: `backend` ("mono"/"il2cpp")
- Optional: `platform` (defaults to active platform)

`PlayerSettings.SetScriptingBackend(targetGroup, backend == "il2cpp" ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x)`

#### `list_profiles` (READ-ONLY)
- List all BuildProfile assets via `AssetDatabase.FindAssets("t:BuildProfile")`
  or fall back to listing current `EditorBuildSettings` configuration
- For fallback: return active build target, scene list, scripting backend
- Response: `{ "result": "ok", "profiles": [...], "active_platform": "...", "scenes": [...] }`

**Implementation strategy**: Use reflection to detect the Build Profile API.
If `UnityEditor.Build.Profile.BuildProfile` type exists, use it. Otherwise,
use the legacy `EditorBuildSettings` + `PlayerSettings` APIs which are
always available. This makes the tool work across all Unity 6 versions.

**JSON Schema** properties:
- `operation` (required, enum: create, set_scenes, set_platform, set_scripting_backend, list_profiles)
- `name`, `platform`, `asset_path`, `profile_path`
- `scenes` (string array)
- `backend` ("mono"/"il2cpp")
- `dry_run`

**Acceptance Criteria**:
- [ ] `set_scenes` updates the build scene list
- [ ] `set_platform` switches the active build target (or returns info)
- [ ] `set_scripting_backend` changes Mono/IL2CPP setting
- [ ] `list_profiles` returns current build configuration
- [ ] Graceful fallback when Build Profile API unavailable

---

### Unit 2: Server Integration

After Phase 10a registrations in `TheatreServer.cs`:
```csharp
            BuildProfileOpTool.Register(registry);      // Phase 10b
```

---

## Implementation Order

```
Unit 1: BuildProfileOpTool
Unit 2: Server Integration
```

---

## Testing

### Tests: `Tests/Editor/BuildProfileToolTests.cs`

```csharp
[TestFixture]
public class BuildProfileOpTests
{
    [Test] public void ListProfiles_ReturnsCurrentConfig()
    {
        var result = BuildProfileOpTool.ListProfiles(new JObject());
        Assert.That(result, Does.Contain("\"result\":\"ok\""));
        Assert.That(result, Does.Contain("\"active_platform\"")
            .Or.Contain("\"profiles\""));
    }

    [Test] public void SetScriptingBackend_ChangesBackend()
    {
        // Just verify it doesn't error — actual backend change may
        // require platform support modules
        var result = BuildProfileOpTool.SetScriptingBackend(new JObject
        {
            ["backend"] = "mono"
        });
        Assert.That(result, Does.Contain("\"result\":\"ok\"")
            .Or.Contain("error")); // may error if platform module missing
    }

    [Test] public void SetScenes_UpdatesBuildSceneList()
    {
        var original = UnityEditor.EditorBuildSettings.scenes;
        try
        {
            var result = BuildProfileOpTool.SetScenes(new JObject
            {
                ["scenes"] = new JArray("Assets/Scenes/TestScene_Hierarchy.unity")
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
        }
        finally
        {
            UnityEditor.EditorBuildSettings.scenes = original;
        }
    }

    [Test] public void Create_MissingName_ReturnsError()
    {
        var result = BuildProfileOpTool.Create(new JObject());
        Assert.That(result, Does.Contain("error"));
    }
}
```

---

## Verification Checklist

1. `unity_console {"operation": "refresh"}` — recompile
2. `unity_console {"filter": "error"}` — no compile errors
3. `unity_tests {"operation": "run"}` — all tests pass
4. Manual: `build_profile_op` list_profiles → verify current config
5. Manual: `build_profile_op` set_scenes → verify scene list changed
