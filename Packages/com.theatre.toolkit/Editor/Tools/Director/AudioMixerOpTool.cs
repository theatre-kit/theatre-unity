using System;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEngine.Audio;
using UnityEditor;

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// MCP tool: audio_mixer_op
    /// Compound tool for creating and managing Audio Mixer assets.
    /// Operations: create, add_group, set_volume, add_effect, create_snapshot, expose_parameter.
    /// Note: Several operations require internal Unity API access via reflection.
    /// If internal APIs are unavailable, a clear error is returned with a manual workflow suggestion.
    /// </summary>
    public static class AudioMixerOpTool
    {
        private static readonly JToken s_inputSchema;

        // Cached controller type resolved via reflection
        private static Type s_controllerType;
        private static bool s_controllerTypeResolved;

        static AudioMixerOpTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create"", ""add_group"", ""set_volume"", ""add_effect"", ""create_snapshot"", ""expose_parameter""],
                        ""description"": ""The audio mixer operation to perform.""
                    },
                    ""asset_path"": {
                        ""type"": ""string"",
                        ""description"": ""Asset path for the mixer (must end in .mixer).""
                    },
                    ""name"": {
                        ""type"": ""string"",
                        ""description"": ""Group or snapshot name.""
                    },
                    ""parent_group"": {
                        ""type"": ""string"",
                        ""description"": ""Parent group name for add_group (default: 'Master').""
                    },
                    ""group"": {
                        ""type"": ""string"",
                        ""description"": ""Mixer group name for set_volume, add_effect, expose_parameter.""
                    },
                    ""volume"": {
                        ""type"": ""number"",
                        ""description"": ""Volume level in decibels for set_volume.""
                    },
                    ""effect"": {
                        ""type"": ""string"",
                        ""description"": ""Effect type name for add_effect (e.g. 'SFX Reverb', 'Echo').""
                    },
                    ""parameter"": {
                        ""type"": ""string"",
                        ""description"": ""Parameter name for expose_parameter.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "audio_mixer_op",
                description: "Create and manage Audio Mixer assets in the Unity Editor. "
                    + "Operations: create, add_group, set_volume, add_effect, create_snapshot, expose_parameter. "
                    + "Some operations require internal Unity API — a clear error is returned if unavailable.",
                inputSchema: s_inputSchema,
                group: ToolGroup.DirectorAsset,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = false
                }
            ));
        }

        private static string Execute(JToken arguments) =>
            CompoundToolDispatcher.Execute(
                "audio_mixer_op",
                arguments,
                (args, operation) => operation switch
                {
                    "create"           => Create(args),
                    "add_group"        => AddGroup(args),
                    "set_volume"       => SetVolume(args),
                    "add_effect"       => AddEffect(args),
                    "create_snapshot"  => CreateSnapshot(args),
                    "expose_parameter" => ExposeParameter(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create, add_group, set_volume, add_effect, create_snapshot, expose_parameter")
                },
                "create, add_group, set_volume, add_effect, create_snapshot, expose_parameter");

        /// <summary>Create a new Audio Mixer asset at the given path.</summary>
        internal static string Create(JObject args)
        {
            var assetPath = args["asset_path"]?.Value<string>();
            var pathError = DirectorHelpers.ValidateAssetPath(assetPath, ".mixer");
            if (pathError != null) return pathError;

            var conflictError = DirectorHelpers.CheckAssetConflict(assetPath);
            if (conflictError != null) return conflictError;

            var controllerType = GetControllerType();
            if (controllerType == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "internal_api_unavailable",
                    "AudioMixerController type not found in Unity assemblies",
                    "Use the Unity Editor Audio Mixer window (Window > Audio > Audio Mixer) to create mixers manually");
            }

            // Guard against Unity [Assert] spam: ScriptableObject.CreateInstance on a type
            // that lacks [ExtensionOfNativeClass] logs an assertion and returns null.
            // Check for the attribute by name to avoid referencing the internal type directly.
            bool hasNativeExtension = false;
            foreach (var attr in controllerType.GetCustomAttributes(false))
            {
                if (attr.GetType().Name == "ExtensionOfNativeClassAttribute")
                {
                    hasNativeExtension = true;
                    break;
                }
            }
            if (!hasNativeExtension)
            {
                return ResponseHelpers.ErrorResponse(
                    "internal_api_unavailable",
                    "AudioMixerController cannot be instantiated via ScriptableObject.CreateInstance in this Unity version",
                    "Use the Unity Editor Audio Mixer window (Window > Audio > Audio Mixer) to create mixers manually");
            }

            var mixer = ScriptableObject.CreateInstance(controllerType);
            if (mixer == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "internal_api_unavailable",
                    "Failed to create AudioMixerController instance",
                    "Use the Unity Editor Audio Mixer window (Window > Audio > Audio Mixer) to create mixers manually");
            }

            DirectorHelpers.EnsureParentDirectory(assetPath);
            AssetDatabase.CreateAsset(mixer, assetPath);
            Undo.RegisterCreatedObjectUndo(mixer, "Theatre audio_mixer_op:create");

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create";
            response["asset_path"] = assetPath;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Add a child group to the mixer.</summary>
        internal static string AddGroup(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<AudioMixer>(
                args, out var mixer, out var assetPath);
            if (loadError != null) return loadError;

            var groupName = args["name"]?.Value<string>();
            if (string.IsNullOrEmpty(groupName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'name' parameter",
                    "Provide the name for the new mixer group");

            var parentGroupName = args["parent_group"]?.Value<string>() ?? "Master";

            var parentGroups = mixer.FindMatchingGroups(parentGroupName);
            if (parentGroups == null || parentGroups.Length == 0)
                return ResponseHelpers.ErrorResponse(
                    "group_not_found",
                    $"Parent group '{parentGroupName}' not found in mixer",
                    "Check the parent group name. The default master group is named 'Master'.");

            // Try via reflection on the controller type
            var controllerType = mixer.GetType();
            var addChildMethod = controllerType.GetMethod(
                "AddChildGroup",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (addChildMethod != null)
            {
                try
                {
                    addChildMethod.Invoke(mixer, new object[] { parentGroups[0], groupName });
                    EditorUtility.SetDirty(mixer);
                    AssetDatabase.SaveAssets();

                    var response = new JObject();
                    response["result"] = "ok";
                    response["operation"] = "add_group";
                    response["asset_path"] = assetPath;
                    response["name"] = groupName;
                    response["parent_group"] = parentGroupName;
                    ResponseHelpers.AddFrameContext(response);
                    return response.ToString(Formatting.None);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Theatre] audio_mixer_op:add_group AddChildGroup failed: {ex.Message}");
                }
            }

            return ResponseHelpers.ErrorResponse(
                "internal_api_unavailable",
                "AudioMixer AddChildGroup internal API not accessible in this Unity version",
                "Use the Unity Editor Audio Mixer window (Window > Audio > Audio Mixer) to add groups manually");
        }

        /// <summary>Set volume for a named group (via exposed parameter or SerializedObject).</summary>
        internal static string SetVolume(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<AudioMixer>(
                args, out var mixer, out var assetPath);
            if (loadError != null) return loadError;

            var groupName = args["group"]?.Value<string>();
            if (string.IsNullOrEmpty(groupName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'group' parameter",
                    "Provide the name of the mixer group to set volume on");

            if (args["volume"] == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'volume' parameter",
                    "Provide a volume level in decibels (e.g. 0 for unity gain, -80 for silence)");

            float volume = args["volume"].Value<float>();

            var groups = mixer.FindMatchingGroups(groupName);
            if (groups == null || groups.Length == 0)
                return ResponseHelpers.ErrorResponse(
                    "group_not_found",
                    $"Group '{groupName}' not found in mixer",
                    "Group names are case-sensitive and use '/' separators for nested groups (e.g., 'Master/Music'). The root group is always 'Master'.");

            // Try setting via exposed parameter name convention
            bool volumeSet = false;
            var paramName = groupName + " Volume";
            if (mixer.SetFloat(paramName, volume))
            {
                volumeSet = true;
            }

            if (!volumeSet)
            {
                // Try via SerializedObject on the group to find attenuation fader
                var group = groups[0];
                var so = new SerializedObject(group);
                so.Update();

                // The volume fader on AudioMixerGroup is typically stored as "m_Volumes" or similar
                // Try common serialized property names
                var volumeProp = so.FindProperty("m_Volume");
                if (volumeProp == null)
                    volumeProp = so.FindProperty("volume");

                if (volumeProp != null && volumeProp.propertyType == SerializedPropertyType.Float)
                {
                    volumeProp.floatValue = volume;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(group);
                    AssetDatabase.SaveAssets();
                    volumeSet = true;
                }
            }

            if (!volumeSet)
            {
                return ResponseHelpers.ErrorResponse(
                    "internal_api_unavailable",
                    $"Could not set volume for group '{groupName}'. Volume parameter may not be exposed.",
                    $"Expose the volume parameter in the Audio Mixer window: right-click 'Volume' fader on group '{groupName}' and choose 'Expose Parameter'. Then the parameter name will be accessible.");
            }

            var result = new JObject();
            result["result"] = "ok";
            result["operation"] = "set_volume";
            result["asset_path"] = assetPath;
            result["group"] = groupName;
            result["volume"] = volume;
            ResponseHelpers.AddFrameContext(result);
            return result.ToString(Formatting.None);
        }

        /// <summary>Add an effect to a mixer group (best-effort via reflection).</summary>
        internal static string AddEffect(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<AudioMixer>(
                args, out var mixer, out var assetPath);
            if (loadError != null) return loadError;

            var groupName = args["group"]?.Value<string>();
            if (string.IsNullOrEmpty(groupName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'group' parameter",
                    "Provide the name of the mixer group to add the effect to");

            var effectName = args["effect"]?.Value<string>();
            if (string.IsNullOrEmpty(effectName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'effect' parameter",
                    "Provide an effect type name such as 'SFX Reverb', 'Echo', 'Low Pass'");

            var groups = mixer.FindMatchingGroups(groupName);
            if (groups == null || groups.Length == 0)
                return ResponseHelpers.ErrorResponse(
                    "group_not_found",
                    $"Group '{groupName}' not found in mixer",
                    "Check the group name");

            // Try reflection to find effect-adding method
            var controllerType = mixer.GetType();
            var addEffectMethod = controllerType.GetMethod(
                "AddEffect",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (addEffectMethod != null)
            {
                try
                {
                    addEffectMethod.Invoke(mixer, new object[] { groups[0], effectName });
                    EditorUtility.SetDirty(mixer);
                    AssetDatabase.SaveAssets();

                    var response = new JObject();
                    response["result"] = "ok";
                    response["operation"] = "add_effect";
                    response["asset_path"] = assetPath;
                    response["group"] = groupName;
                    response["effect"] = effectName;
                    ResponseHelpers.AddFrameContext(response);
                    return response.ToString(Formatting.None);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Theatre] audio_mixer_op:add_effect failed: {ex.Message}");
                }
            }

            return ResponseHelpers.ErrorResponse(
                "internal_api_unavailable",
                "AudioMixer add_effect requires internal Unity API that is not accessible in this Unity version",
                "Use the Unity Editor Audio Mixer window (Window > Audio > Audio Mixer) to add effects manually");
        }

        /// <summary>Create a new snapshot in the mixer (best-effort via reflection).</summary>
        internal static string CreateSnapshot(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<AudioMixer>(
                args, out var mixer, out var assetPath);
            if (loadError != null) return loadError;

            var snapshotName = args["name"]?.Value<string>();
            if (string.IsNullOrEmpty(snapshotName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'name' parameter",
                    "Provide a name for the new snapshot");

            // Try reflection to find snapshot creation method
            var controllerType = mixer.GetType();
            var createSnapshotMethod = controllerType.GetMethod(
                "CreateNewSnapshot",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (createSnapshotMethod != null)
            {
                try
                {
                    createSnapshotMethod.Invoke(mixer, new object[] { snapshotName });
                    EditorUtility.SetDirty(mixer);
                    AssetDatabase.SaveAssets();

                    var response = new JObject();
                    response["result"] = "ok";
                    response["operation"] = "create_snapshot";
                    response["asset_path"] = assetPath;
                    response["name"] = snapshotName;
                    ResponseHelpers.AddFrameContext(response);
                    return response.ToString(Formatting.None);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Theatre] audio_mixer_op:create_snapshot failed: {ex.Message}");
                }
            }

            return ResponseHelpers.ErrorResponse(
                "internal_api_unavailable",
                "AudioMixer create_snapshot requires internal Unity API that is not accessible in this Unity version",
                "Use the Unity Editor Audio Mixer window (Window > Audio > Audio Mixer) to create snapshots manually");
        }

        /// <summary>Expose a parameter on a mixer group (best-effort via reflection).</summary>
        internal static string ExposeParameter(JObject args)
        {
            var loadError = DirectorHelpers.LoadAsset<AudioMixer>(
                args, out var mixer, out var assetPath);
            if (loadError != null) return loadError;

            var groupName = args["group"]?.Value<string>();
            if (string.IsNullOrEmpty(groupName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'group' parameter",
                    "Provide the name of the mixer group");

            var parameterName = args["parameter"]?.Value<string>();
            if (string.IsNullOrEmpty(parameterName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'parameter' parameter",
                    "Provide the parameter name to expose (e.g. 'Volume', 'Pitch')");

            // Exposing parameters requires knowing the internal GUID of the effect parameter.
            // This is too fragile to implement reliably — return a clear error.
            return ResponseHelpers.ErrorResponse(
                "internal_api_unavailable",
                "AudioMixer expose_parameter requires internal Unity API that is not accessible in this Unity version",
                "Use the Unity Editor Audio Mixer window: right-click any parameter on a group effect and choose 'Expose Parameter'. "
                    + "The parameter can then be set via AudioMixer.SetFloat(parameterName, value) at runtime.");
        }

        // --- Helpers ---

        private static Type GetControllerType()
        {
            if (s_controllerTypeResolved) return s_controllerType;

            s_controllerTypeResolved = true;

            // Try UnityEditor.CoreModule first, then UnityEditor
            s_controllerType = Type.GetType(
                "UnityEditor.Audio.AudioMixerController, UnityEditor.CoreModule");
            if (s_controllerType == null)
                s_controllerType = Type.GetType(
                    "UnityEditor.Audio.AudioMixerController, UnityEditor");

            if (s_controllerType != null)
                Debug.Log($"[Theatre] AudioMixerController resolved: {s_controllerType.AssemblyQualifiedName}");
            else
                Debug.LogWarning("[Theatre] AudioMixerController type not found — audio_mixer_op:create will fail");

            return s_controllerType;
        }

    }
}
