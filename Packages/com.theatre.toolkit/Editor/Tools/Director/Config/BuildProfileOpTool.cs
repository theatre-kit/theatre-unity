using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEditor;

namespace Theatre.Editor.Tools.Director.Config
{
    /// <summary>
    /// MCP tool: build_profile_op
    /// Compound tool for managing Unity 6 Build Profiles and legacy build settings.
    /// Operations: create, set_scenes, set_platform, set_scripting_backend, list_profiles.
    /// Uses reflection to detect the Unity 6 Build Profile API and falls back to
    /// legacy EditorBuildSettings / PlayerSettings when not available.
    /// </summary>
    public static class BuildProfileOpTool
    {
        private static readonly JToken s_inputSchema;

        static BuildProfileOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create"", ""set_scenes"", ""set_platform"", ""set_scripting_backend"", ""list_profiles""],
                        ""description"": ""The build profile operation to perform.""
                    },
                    ""name"": {
                        ""type"": ""string"",
                        ""description"": ""Profile name (required for create).""
                    },
                    ""platform"": {
                        ""type"": ""string"",
                        ""enum"": [""windows"", ""macos"", ""linux"", ""android"", ""ios"", ""webgl""],
                        ""description"": ""Target platform.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for the new BuildProfile (optional, used with create).""
                    },
                    ""profile_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path to an existing BuildProfile (optional).""
                    },
                    ""scenes"": {
                        ""type"": ""array"",
                        ""items"": {""type"": ""string""},
                        ""description"": ""Scene asset paths in build order (required for set_scenes).""
                    },
                    ""backend"": {
                        ""type"": ""string"",
                        ""enum"": [""mono"", ""il2cpp""],
                        ""description"": ""Scripting backend (required for set_scripting_backend).""
                    },
                    ""dry_run"": {
                        ""type"": ""boolean"",
                        ""description"": ""If true, validate but do not apply changes.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "build_profile_op",
                description: "Manage Unity Build Profiles and build settings. Operations: create, set_scenes, set_platform, set_scripting_backend, list_profiles.",
                inputSchema: s_inputSchema,
                group: ToolGroup.DirectorConfig,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = false
                }
            ));
        }

        private static string Execute(JToken arguments) =>
            CompoundToolDispatcher.Execute(
                "build_profile_op",
                arguments,
                (args, operation) => operation switch
                {
                    "create"                => Create(args),
                    "set_scenes"            => SetScenes(args),
                    "set_platform"          => SetPlatform(args),
                    "set_scripting_backend" => SetScriptingBackend(args),
                    "list_profiles"         => ListProfiles(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create, set_scenes, set_platform, set_scripting_backend, list_profiles")
                },
                "create, set_scenes, set_platform, set_scripting_backend, list_profiles");

        // ---------------------------------------------------------------
        // Operations
        // ---------------------------------------------------------------

        /// <summary>Create a new build profile or configure the active build target (legacy fallback).</summary>
        internal static string Create(JObject args)
        {
            var name = args["name"]?.Value<string>();
            if (string.IsNullOrEmpty(name))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'name' parameter",
                    "Provide a profile name, e.g. {\"name\": \"MyProfile\", \"platform\": \"windows\"}");

            var platformStr = args["platform"]?.Value<string>();
            if (string.IsNullOrEmpty(platformStr))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'platform' parameter",
                    "Valid platforms: windows, macos, linux, android, ios, webgl");

            if (!ResolvePlatform(platformStr, out var buildTarget, out var buildTargetGroup, out var platformError))
                return platformError;

            // Try Unity 6 Build Profile API first (available in Unity 6.1+)
            var profileType = Type.GetType("UnityEditor.Build.Profile.BuildProfile, UnityEditor.CoreModule");
            if (profileType != null)
            {
                try
                {
                    var assetPath = args["asset_path"]?.Value<string>()
                        ?? $"Assets/Settings/Build/{name}.asset";

                    // Ensure directory exists
                    var dir = System.IO.Path.GetDirectoryName(assetPath);
                    if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                    {
                        var parts = dir.Split('/');
                        var current = parts[0];
                        for (int i = 1; i < parts.Length; i++)
                        {
                            var next = current + "/" + parts[i];
                            if (!AssetDatabase.IsValidFolder(next))
                                AssetDatabase.CreateFolder(current, parts[i]);
                            current = next;
                        }
                    }

                    var profile = ScriptableObject.CreateInstance(profileType);
                    AssetDatabase.CreateAsset(profile, assetPath);
                    AssetDatabase.SaveAssets();

                    var response = new JObject();
                    response["result"] = "ok";
                    response["operation"] = "create";
                    response["name"] = name;
                    response["platform"] = platformStr;
                    response["asset_path"] = assetPath;
                    response["api"] = "build_profile";
                    ResponseHelpers.AddFrameContext(response);
                    return response.ToString(Formatting.None);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Theatre] Build Profile API failed, falling back to legacy: {ex.Message}");
                    // Fall through to legacy path
                }
            }

            // Legacy fallback: switch active build target
            var dryRun = args["dry_run"]?.Value<bool>() ?? false;
            if (!dryRun)
                EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget);

            var legacyResponse = new JObject();
            legacyResponse["result"] = "ok";
            legacyResponse["operation"] = "create";
            legacyResponse["name"] = name;
            legacyResponse["platform"] = platformStr;
            legacyResponse["api"] = "legacy";
            legacyResponse["note"] = dryRun
                ? "dry_run — no changes made"
                : "Build Profile API not available; switched active build target using legacy settings";
            ResponseHelpers.AddFrameContext(legacyResponse);
            return legacyResponse.ToString(Formatting.None);
        }

        /// <summary>Set the scene list in EditorBuildSettings.</summary>
        internal static string SetScenes(JObject args)
        {
            var scenesToken = args["scenes"] as JArray;
            if (scenesToken == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'scenes' parameter",
                    "Provide an array of scene asset paths, e.g. {\"scenes\": [\"Assets/Scenes/Main.unity\"]}");

            var scenePaths = scenesToken.Select(s => s.Value<string>()).ToArray();

            var dryRun = args["dry_run"]?.Value<bool>() ?? false;
            if (!dryRun)
            {
                EditorBuildSettings.scenes = scenePaths
                    .Select(p => new EditorBuildSettingsScene(p, true))
                    .ToArray();
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_scenes";
            response["scene_count"] = scenePaths.Length;
            response["scenes"] = new JArray(scenePaths.Cast<object>().ToArray());
            if (dryRun) response["note"] = "dry_run — no changes made";
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Switch the active build target platform.</summary>
        internal static string SetPlatform(JObject args)
        {
            var platformStr = args["platform"]?.Value<string>();
            if (string.IsNullOrEmpty(platformStr))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'platform' parameter",
                    "Valid platforms: windows, macos, linux, android, ios, webgl");

            if (!ResolvePlatform(platformStr, out var buildTarget, out var buildTargetGroup, out var platformError))
                return platformError;

            var dryRun = args["dry_run"]?.Value<bool>() ?? false;
            if (!dryRun)
                EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_platform";
            response["platform"] = platformStr;
            response["build_target"] = buildTarget.ToString();
            if (dryRun) response["note"] = "dry_run — no changes made";
            else response["note"] = "Platform switch may trigger a script recompile";
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Set the scripting backend (Mono or IL2CPP) for a platform.</summary>
        internal static string SetScriptingBackend(JObject args)
        {
            var backend = args["backend"]?.Value<string>();
            if (string.IsNullOrEmpty(backend))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'backend' parameter",
                    "Valid values: mono, il2cpp");

            if (backend != "mono" && backend != "il2cpp")
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Unknown backend '{backend}'",
                    "Valid values: mono, il2cpp");

            // Determine target group — use specified platform or fall back to active
            BuildTargetGroup targetGroup;
            var platformStr = args["platform"]?.Value<string>();
            if (!string.IsNullOrEmpty(platformStr))
            {
                if (!ResolvePlatform(platformStr, out _, out targetGroup, out var platformError))
                    return platformError;
            }
            else
            {
                targetGroup = BuildPipeline.GetBuildTargetGroup(
                    EditorUserBuildSettings.activeBuildTarget);
            }

            var impl = backend == "il2cpp"
                ? ScriptingImplementation.IL2CPP
                : ScriptingImplementation.Mono2x;

            var dryRun = args["dry_run"]?.Value<bool>() ?? false;
            if (!dryRun)
                PlayerSettings.SetScriptingBackend(targetGroup, impl);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "set_scripting_backend";
            response["backend"] = backend;
            response["target_group"] = targetGroup.ToString();
            if (dryRun) response["note"] = "dry_run — no changes made";
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>List current build configuration and any BuildProfile assets.</summary>
        internal static string ListProfiles(JObject args)
        {
            var activeBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            var activeGroup = BuildPipeline.GetBuildTargetGroup(activeBuildTarget);
            var activePlatform = BuildTargetToString(activeBuildTarget);

            // Scene list
            var scenes = EditorBuildSettings.scenes;
            var scenesArray = new JArray();
            foreach (var scene in scenes)
            {
                var sceneObj = new JObject();
                sceneObj["path"] = scene.path;
                sceneObj["enabled"] = scene.enabled;
                scenesArray.Add(sceneObj);
            }

            // Scripting backend
            var backend = PlayerSettings.GetScriptingBackend(activeGroup);
            var backendStr = backend == ScriptingImplementation.IL2CPP ? "il2cpp" : "mono";

            // Try to find BuildProfile assets
            var profilesArray = new JArray();
            try
            {
                var guids = AssetDatabase.FindAssets("t:BuildProfile");
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var profileObj = new JObject();
                    profileObj["asset_path"] = path;
                    profileObj["name"] = System.IO.Path.GetFileNameWithoutExtension(path);
                    profilesArray.Add(profileObj);
                }
            }
            catch
            {
                // BuildProfile type may not exist — ignore
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "list_profiles";
            response["active_platform"] = activePlatform;
            response["active_build_target"] = activeBuildTarget.ToString();
            response["scripting_backend"] = backendStr;
            response["scenes"] = scenesArray;
            response["profiles"] = profilesArray;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static bool ResolvePlatform(string platform, out BuildTarget target, out BuildTargetGroup group, out string error)
        {
            error = null;
            switch (platform?.ToLowerInvariant())
            {
                case "windows": target = BuildTarget.StandaloneWindows64; group = BuildTargetGroup.Standalone; return true;
                case "macos":   target = BuildTarget.StandaloneOSX;       group = BuildTargetGroup.Standalone; return true;
                case "linux":   target = BuildTarget.StandaloneLinux64;   group = BuildTargetGroup.Standalone; return true;
                case "android": target = BuildTarget.Android;             group = BuildTargetGroup.Android;    return true;
                case "ios":     target = BuildTarget.iOS;                 group = BuildTargetGroup.iOS;        return true;
                case "webgl":   target = BuildTarget.WebGL;               group = BuildTargetGroup.WebGL;      return true;
                default:
                    target = default;
                    group = default;
                    error = ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown platform '{platform}'",
                        "Valid platforms: windows, macos, linux, android, ios, webgl");
                    return false;
            }
        }

        private static string BuildTargetToString(BuildTarget target)
        {
            return target switch
            {
                BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => "windows",
                BuildTarget.StandaloneOSX     => "macos",
                BuildTarget.StandaloneLinux64 => "linux",
                BuildTarget.Android           => "android",
                BuildTarget.iOS               => "ios",
                BuildTarget.WebGL             => "webgl",
                _                             => target.ToString().ToLowerInvariant()
            };
        }
    }
}
