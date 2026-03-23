using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.AI;
#if THEATRE_HAS_AI_NAVIGATION
using Unity.AI.Navigation;
#endif

namespace Theatre.Editor.Tools.Director
{
    /// <summary>
    /// Handlers for all navmesh_op operations.
    /// Each method is internal and called by NavMeshOpTool's dispatcher.
    /// </summary>
    internal static class NavMeshOpHandlers
    {
        /// <summary>Bake the NavMesh for the current scene.</summary>
        internal static string Bake(JObject args)
        {
            // Unity requires the scene to be saved before NavMesh baking.
            // If the scene has no path (never saved), return an error —
            // the agent should save it first via scene_op:create_scene.
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(activeScene.path))
            {
                return ResponseHelpers.ErrorResponse(
                    "scene_not_saved",
                    "The active scene has not been saved to disk. NavMesh baking requires a saved scene.",
                    "Use scene_op with operation 'create_scene' to save the scene first, then call navmesh_op:bake");
            }

            // Auto-save if dirty (silently, since it already has a path)
            if (activeScene.isDirty)
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(activeScene);

            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "bake";
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Set a NavMesh area name and cost by index.</summary>
        internal static string SetArea(JObject args)
        {
            var indexToken = args["index"];
            if (indexToken == null || indexToken.Type == JTokenType.Null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'index' parameter",
                    "Provide a NavMesh area index from 0 to 31");

            int index = indexToken.Value<int>();
            if (index < 0 || index > 31)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Area index {index} is out of range",
                    "Valid NavMesh area indices are 0-31");

            var areaName = args["name"]?.Value<string>();
            if (string.IsNullOrEmpty(areaName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'name' parameter",
                    "Provide a name for the NavMesh area");

            float cost = args["cost"]?.Value<float>() ?? 1f;

            try
            {
                // Access NavMesh settings via SerializedObject
                var settingsObject = UnityEditor.AI.NavMeshBuilder.navMeshSettingsObject;
                if (settingsObject == null)
                    return ResponseHelpers.ErrorResponse(
                        "api_unavailable",
                        "NavMesh settings object is not accessible",
                        "This may be a Unity version compatibility issue");

                var so = new SerializedObject(settingsObject);
                var areas = so.FindProperty("areas");
                if (areas == null)
                    return ResponseHelpers.ErrorResponse(
                        "api_unavailable",
                        "Could not find 'areas' property in NavMesh settings",
                        "This may be a Unity version compatibility issue");

                var area = areas.GetArrayElementAtIndex(index);
                if (area == null)
                    return ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Area index {index} not found in NavMesh settings",
                        "Valid NavMesh area indices are 0-31");

                area.FindPropertyRelative("name").stringValue = areaName;
                area.FindPropertyRelative("cost").floatValue = cost;
                so.ApplyModifiedProperties();

                var response = new JObject();
                response["result"] = "ok";
                response["operation"] = "set_area";
                response["index"] = index;
                response["name"] = areaName;
                response["cost"] = cost;
                ResponseHelpers.AddFrameContext(response);
                return response.ToString(Formatting.None);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Theatre] navmesh_op:set_area failed to access settings: {ex.Message}");
                return ResponseHelpers.ErrorResponse(
                    "api_unavailable",
                    $"Could not modify NavMesh area settings: {ex.Message}",
                    "This operation requires access to NavMesh project settings");
            }
        }

        /// <summary>Add a NavMeshModifier component to a GameObject.</summary>
        internal static string AddModifier(JObject args)
        {
            var path = args["path"]?.Value<string>();
            if (string.IsNullOrEmpty(path))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'path' parameter",
                    "Provide the hierarchy path to a GameObject, e.g. '/Environment/Walls'");

            var resolved = ObjectResolver.Resolve(path: path);
            if (!resolved.Success)
                return ResponseHelpers.ErrorResponse(resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);

            var go = resolved.GameObject;

#if THEATRE_HAS_AI_NAVIGATION
            var modifier = go.GetComponent<NavMeshModifier>();
            if (modifier == null)
                modifier = Undo.AddComponent<NavMeshModifier>(go);

            var areaToken = args["area"];
            if (areaToken != null && areaToken.Type != JTokenType.Null)
                modifier.area = areaToken.Value<int>();

            var ignoreToken = args["ignore_from_build"];
            if (ignoreToken != null && ignoreToken.Type != JTokenType.Null)
                modifier.ignoreFromBuild = ignoreToken.Value<bool>();

            var affectToken = args["affect_children"];
            if (affectToken != null && affectToken.Type != JTokenType.Null)
                modifier.applyToChildren = affectToken.Value<bool>();

            EditorUtility.SetDirty(go);
#else
            // NavMeshModifier is in com.unity.ai.navigation — use reflection fallback
            var modifierType = FindAINavigationType("NavMeshModifier");
            if (modifierType == null)
                return ResponseHelpers.ErrorResponse(
                    "package_not_installed",
                    "NavMeshModifier type not found. The 'com.unity.ai.navigation' package is not installed.",
                    "Install 'com.unity.ai.navigation' via the Package Manager to use add_modifier");

            var existing = go.GetComponent(modifierType);
            if (existing == null)
            {
                Undo.AddComponent(go, modifierType);
                existing = go.GetComponent(modifierType);
            }

            // Set optional properties via reflection
            var areaToken = args["area"];
            if (areaToken != null && areaToken.Type != JTokenType.Null)
            {
                var areaProp = modifierType.GetProperty("area",
                    BindingFlags.Public | BindingFlags.Instance);
                areaProp?.SetValue(existing, areaToken.Value<int>());
            }

            var ignoreToken = args["ignore_from_build"];
            if (ignoreToken != null && ignoreToken.Type != JTokenType.Null)
            {
                var ignoreProp = modifierType.GetProperty("ignoreFromBuild",
                    BindingFlags.Public | BindingFlags.Instance);
                ignoreProp?.SetValue(existing, ignoreToken.Value<bool>());
            }

            var affectToken = args["affect_children"];
            if (affectToken != null && affectToken.Type != JTokenType.Null)
            {
                var affectProp = modifierType.GetProperty("AffectChildren",
                    BindingFlags.Public | BindingFlags.Instance)
                    ?? modifierType.GetProperty("affectChildren",
                    BindingFlags.Public | BindingFlags.Instance);
                affectProp?.SetValue(existing, affectToken.Value<bool>());
            }

            EditorUtility.SetDirty(go);
#endif

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_modifier";
            ResponseHelpers.AddIdentity(response, go);
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Create an OffMeshLink between two world positions.</summary>
        internal static string AddLink(JObject args)
        {
            var startArr = args["start"] as JArray;
            var endArr = args["end"] as JArray;

            if (startArr == null || startArr.Count < 3)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'start' parameter",
                    "Provide start as [x, y, z] world position");

            if (endArr == null || endArr.Count < 3)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing or invalid 'end' parameter",
                    "Provide end as [x, y, z] world position");

            var startPos = new Vector3(
                startArr[0].Value<float>(),
                startArr[1].Value<float>(),
                startArr[2].Value<float>());
            var endPos = new Vector3(
                endArr[0].Value<float>(),
                endArr[1].Value<float>(),
                endArr[2].Value<float>());

            bool bidirectional = args["bidirectional"]?.Value<bool>() ?? true;
            int area = args["area"]?.Value<int>() ?? 0;
            float width = args["width"]?.Value<float>() ?? 0f;

            var go = new GameObject("OffMeshLink");

            var startGo = new GameObject("Start");
            startGo.transform.SetParent(go.transform, false);
            startGo.transform.position = startPos;

            var endGo = new GameObject("End");
            endGo.transform.SetParent(go.transform, false);
            endGo.transform.position = endPos;

            var link = go.AddComponent<OffMeshLink>();
            link.startTransform = startGo.transform;
            link.endTransform = endGo.transform;
            link.biDirectional = bidirectional;
            link.area = area;

            // Optional: parent the link
            var parentPath = args["parent_path"]?.Value<string>();
            if (!string.IsNullOrEmpty(parentPath))
            {
                var parentResolved = ObjectResolver.Resolve(path: parentPath);
                if (parentResolved.Success)
                    go.transform.SetParent(parentResolved.GameObject.transform, true);
            }

            Undo.RegisterCreatedObjectUndo(go, "Theatre navmesh_op:add_link");

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_link";
            response["start"] = new JArray(
                Math.Round(startPos.x, 3),
                Math.Round(startPos.y, 3),
                Math.Round(startPos.z, 3));
            response["end"] = new JArray(
                Math.Round(endPos.x, 3),
                Math.Round(endPos.y, 3),
                Math.Round(endPos.z, 3));
            response["bidirectional"] = bidirectional;
            ResponseHelpers.AddIdentity(response, go);
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Set agent type settings (radius, height, etc.).</summary>
        internal static string SetAgentType(JObject args)
        {
            var agentTypeIdToken = args["agent_type_id"];
            if (agentTypeIdToken == null || agentTypeIdToken.Type == JTokenType.Null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'agent_type_id' parameter",
                    "Provide the agent type ID to modify");

            int agentTypeId = agentTypeIdToken.Value<int>();

            try
            {
                var settingsObject = UnityEditor.AI.NavMeshBuilder.navMeshSettingsObject;
                if (settingsObject == null)
                    return ResponseHelpers.ErrorResponse(
                        "api_unavailable",
                        "NavMesh settings object is not accessible",
                        "This may be a Unity version compatibility issue");

                var so = new SerializedObject(settingsObject);
                var agents = so.FindProperty("m_Settings");
                if (agents == null)
                    agents = so.FindProperty("settings");

                if (agents == null)
                    return ResponseHelpers.ErrorResponse(
                        "api_unavailable",
                        "Could not find agent settings property in NavMesh settings",
                        "This may be a Unity version compatibility issue");

                SerializedProperty agentProp = null;
                for (int i = 0; i < agents.arraySize; i++)
                {
                    var element = agents.GetArrayElementAtIndex(i);
                    var idProp = element.FindPropertyRelative("agentTypeID")
                        ?? element.FindPropertyRelative("agentTypeName");
                    if (idProp != null && idProp.propertyType == SerializedPropertyType.Integer
                        && idProp.intValue == agentTypeId)
                    {
                        agentProp = element;
                        break;
                    }
                }

                if (agentProp == null)
                    return ResponseHelpers.ErrorResponse(
                        "agent_type_not_found",
                        $"Agent type with ID {agentTypeId} not found in NavMesh settings",
                        "Use agent_type_id 0 for the default Humanoid agent. Custom agent types are defined in Navigation settings (Window > AI > Navigation).");

                void TrySetFloat(string propName, JToken token)
                {
                    if (token == null || token.Type == JTokenType.Null) return;
                    var p = agentProp.FindPropertyRelative(propName);
                    if (p != null) p.floatValue = token.Value<float>();
                }

                TrySetFloat("agentRadius", args["radius"]);
                TrySetFloat("agentHeight", args["height"]);
                TrySetFloat("agentClimb", args["step_height"]);
                TrySetFloat("agentSlope", args["max_slope"]);

                so.ApplyModifiedProperties();

                var response = new JObject();
                response["result"] = "ok";
                response["operation"] = "set_agent_type";
                response["agent_type_id"] = agentTypeId;
                ResponseHelpers.AddFrameContext(response);
                return response.ToString(Formatting.None);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Theatre] navmesh_op:set_agent_type failed: {ex.Message}");
                return ResponseHelpers.ErrorResponse(
                    "api_unavailable",
                    $"Could not modify agent type settings: {ex.Message}",
                    "This operation requires access to NavMesh project settings");
            }
        }

        /// <summary>Add a NavMeshSurface component to a GameObject.</summary>
        internal static string AddSurface(JObject args)
        {
            var path = args["path"]?.Value<string>();
            if (string.IsNullOrEmpty(path))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'path' parameter",
                    "Provide the hierarchy path to a GameObject, e.g. '/NavMeshPlane'");

            var resolved = ObjectResolver.Resolve(path: path);
            if (!resolved.Success)
                return ResponseHelpers.ErrorResponse(resolved.ErrorCode, resolved.ErrorMessage, resolved.Suggestion);

            var go = resolved.GameObject;

#if THEATRE_HAS_AI_NAVIGATION
            var surface = go.GetComponent<NavMeshSurface>();
            if (surface == null)
                surface = Undo.AddComponent<NavMeshSurface>(go);

            var collectObjects = args["collect_objects"]?.Value<string>();
            if (!string.IsNullOrEmpty(collectObjects))
            {
                try
                {
                    surface.collectObjects = (CollectObjects)Enum.Parse(
                        typeof(CollectObjects), DirectorHelpers.ToPascalCase(collectObjects), true);
                }
                catch { /* best effort — ignore unknown values */ }
            }

            var useGeometry = args["use_geometry"]?.Value<string>();
            if (!string.IsNullOrEmpty(useGeometry))
            {
                try
                {
                    surface.useGeometry = (NavMeshCollectGeometry)Enum.Parse(
                        typeof(NavMeshCollectGeometry), DirectorHelpers.ToPascalCase(useGeometry), true);
                }
                catch { /* best effort — ignore unknown values */ }
            }

            EditorUtility.SetDirty(go);
#else
            // NavMeshSurface is in com.unity.ai.navigation — use reflection fallback
            var surfaceType = FindAINavigationType("NavMeshSurface");
            if (surfaceType == null)
                return ResponseHelpers.ErrorResponse(
                    "package_not_installed",
                    "NavMeshSurface type not found. The 'com.unity.ai.navigation' package is not installed.",
                    "Install 'com.unity.ai.navigation' via the Package Manager to use add_surface");

            var existing = go.GetComponent(surfaceType);
            if (existing == null)
            {
                Undo.AddComponent(go, surfaceType);
                existing = go.GetComponent(surfaceType);
            }

            // Set optional properties via reflection
            var collectObjectsFallback = args["collect_objects"]?.Value<string>();
            if (!string.IsNullOrEmpty(collectObjectsFallback))
            {
                var collectProp = surfaceType.GetProperty("collectObjects",
                    BindingFlags.Public | BindingFlags.Instance);
                if (collectProp != null)
                {
                    var enumType = collectProp.PropertyType;
                    if (enumType.IsEnum)
                    {
                        try
                        {
                            var enumVal = Enum.Parse(enumType, DirectorHelpers.ToPascalCase(collectObjectsFallback), true);
                            collectProp.SetValue(existing, enumVal);
                        }
                        catch { /* best effort */ }
                    }
                }
            }

            var useGeometryFallback = args["use_geometry"]?.Value<string>();
            if (!string.IsNullOrEmpty(useGeometryFallback))
            {
                var geomProp = surfaceType.GetProperty("useGeometry",
                    BindingFlags.Public | BindingFlags.Instance);
                if (geomProp != null)
                {
                    var enumType = geomProp.PropertyType;
                    if (enumType.IsEnum)
                    {
                        try
                        {
                            var enumVal = Enum.Parse(enumType, DirectorHelpers.ToPascalCase(useGeometryFallback), true);
                            geomProp.SetValue(existing, enumVal);
                        }
                        catch { /* best effort */ }
                    }
                }
            }

            EditorUtility.SetDirty(go);
#endif

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_surface";
            ResponseHelpers.AddIdentity(response, go);
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        // --- Helpers ---

        /// <summary>
        /// Find a type from the Unity.AI.Navigation package by simple name.
        /// Returns null if the package is not installed.
        /// </summary>
        internal static Type FindAINavigationType(string typeName)
        {
            // Try known assembly names for the AI Navigation package
            var candidates = new[]
            {
                $"Unity.AI.Navigation.{typeName}, Unity.AI.Navigation",
                $"UnityEngine.AI.{typeName}, Unity.AI.Navigation",
                $"Unity.AI.Navigation.{typeName}, Unity.AI.Navigation.Runtime",
            };

            foreach (var candidate in candidates)
            {
                var t = Type.GetType(candidate);
                if (t != null) return t;
            }

            // Fall back to scanning all loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.Name == typeName && typeof(Component).IsAssignableFrom(type))
                            return type;
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip assemblies that fail to enumerate
                }
            }

            return null;
        }
    }
}
