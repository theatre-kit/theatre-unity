using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;

namespace Theatre.Editor
{
    /// <summary>
    /// MCP tool: scene_inspect
    /// Deep inspection of a single GameObject — all components, serialized
    /// properties, references, hierarchy context.
    /// </summary>
    public static class SceneInspectTool
    {
        private static readonly JToken s_inputSchema;

        static SceneInspectTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""path"": {
                        ""type"": ""string"",
                        ""description"": ""Hierarchy path (e.g., '/Player'). Use this or instance_id.""
                    },
                    ""instance_id"": {
                        ""type"": ""integer"",
                        ""description"": ""InstanceID. Use this or path.""
                    },
                    ""components"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Filter to specific component types.""
                    },
                    ""depth"": {
                        ""type"": ""string"",
                        ""enum"": [""summary"", ""full"", ""properties""],
                        ""default"": ""summary"",
                        ""description"": ""Detail level: 'summary' (default), 'full', or 'properties' (includes hidden fields).""
                    },
                    ""budget"": {
                        ""type"": ""integer"",
                        ""default"": 1500,
                        ""minimum"": 100,
                        ""maximum"": 4000,
                        ""description"": ""Target token budget.""
                    }
                },
                ""required"": []
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "scene_inspect",
                description: "Deep inspection of a single GameObject. Returns "
                    + "all components with serialized properties. Use 'depth' to "
                    + "control detail level and 'components' to filter.",
                inputSchema: s_inputSchema,
                group: ToolGroup.StageGameObject,
                handler: Execute,
                annotations: new McpToolAnnotations
                {
                    ReadOnlyHint = true
                }
            ));
        }

        private static string Execute(JToken arguments)
        {
            // Parse parameters
            string path = null;
            int? instanceId = null;
            string[] componentFilter = null;
            var detail = PropertySerializer.DetailLevel.Summary;
            int budgetTokens = TokenBudget.DefaultBudget;

            if (arguments != null && arguments.Type == JTokenType.Object)
            {
                var args = (JObject)arguments;

                if (args["path"] != null)
                    path = args["path"].Value<string>();

                if (args["instance_id"] != null)
                    instanceId = args["instance_id"].Value<int>();

                var compToken = args["components"];
                if (compToken != null && compToken.Type == JTokenType.Array)
                {
                    var list = new List<string>();
                    foreach (var item in (JArray)compToken)
                        list.Add(item.Value<string>());
                    componentFilter = list.ToArray();
                }

                if (args["depth"] != null)
                {
                    detail = args["depth"].Value<string>() switch
                    {
                        "full" => PropertySerializer.DetailLevel.Full,
                        "properties" => PropertySerializer.DetailLevel.Properties,
                        _ => PropertySerializer.DetailLevel.Summary
                    };
                }

                if (args["budget"] != null)
                    budgetTokens = args["budget"].Value<int>();
            }

            // Resolve the target object
            var resolved = ObjectResolver.Resolve(path, instanceId);
            if (!resolved.Success)
            {
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode,
                    resolved.ErrorMessage,
                    resolved.Suggestion);
            }

            var go = resolved.GameObject;
            var transform = go.transform;
            var budget = new TokenBudget(budgetTokens);

            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);

            // Object identity
            response["path"] = ResponseHelpers.GetHierarchyPath(transform);
            response["instance_id"] = go.GetEntityId();

            // GameObject metadata
            response["tag"] = go.tag;
            response["layer"] = LayerMask.LayerToName(go.layer);
            response["static_flags"] = BuildStaticFlags(go);
            response["active_self"] = go.activeSelf;
            response["active_hierarchy"] = go.activeInHierarchy;
            response["scene"] = go.scene.name;

            // Prefab info
            BuildPrefabInfo(response, go);

            // Components
            response["components"] = PropertySerializer.SerializeComponents(
                go, detail, componentFilter, budget);

            // Children summary
            var childrenArray = new JArray();
            int childCount = Math.Min(transform.childCount, 20);
            for (int i = 0; i < childCount; i++)
            {
                var child = transform.GetChild(i);
                var childObj = new JObject();
                childObj["path"] = ResponseHelpers.GetHierarchyPath(child);
                childObj["instance_id"] = child.gameObject.GetEntityId();
                childObj["children_count"] = child.childCount;
                childrenArray.Add(childObj);
            }
            if (transform.childCount > 20)
            {
                var truncObj = new JObject();
                truncObj["_truncated"] = $"{transform.childCount - 20} more children";
                childrenArray.Add(truncObj);
            }
            response["children"] = childrenArray;

            // Budget info
            response["budget"] = budget.ToBudgetJObject(
                truncated: budget.IsExhausted,
                reason: budget.IsExhausted ? "budget" : null,
                suggestion: budget.IsExhausted
                    ? "Use 'components' filter to inspect specific components"
                    : null);

            return response.ToString(Formatting.None);
        }

        /// <summary>
        /// Build static flags as a JArray of flag names.
        /// </summary>
        private static JArray BuildStaticFlags(GameObject go)
        {
            var arr = new JArray();
#if UNITY_EDITOR
            var flags = UnityEditor.GameObjectUtility.GetStaticEditorFlags(go);

            if (flags.HasFlag(UnityEditor.StaticEditorFlags.ContributeGI))
                arr.Add("contribute_gi");
            if (flags.HasFlag(UnityEditor.StaticEditorFlags.OccluderStatic))
                arr.Add("occluder");
            if (flags.HasFlag(UnityEditor.StaticEditorFlags.OccludeeStatic))
                arr.Add("occludee");
            if (flags.HasFlag(UnityEditor.StaticEditorFlags.BatchingStatic))
                arr.Add("batching");
            if (flags.HasFlag(UnityEditor.StaticEditorFlags.NavigationStatic))
                arr.Add("navigation");
            if (flags.HasFlag(UnityEditor.StaticEditorFlags.OffMeshLinkGeneration))
                arr.Add("off_mesh_link");
            if (flags.HasFlag(UnityEditor.StaticEditorFlags.ReflectionProbeStatic))
                arr.Add("reflection_probe");
#endif
            return arr;
        }

        /// <summary>
        /// Add prefab instance information to the response object.
        /// </summary>
        private static void BuildPrefabInfo(JObject response, GameObject go)
        {
#if UNITY_EDITOR
            var isPrefab = UnityEditor.PrefabUtility.IsPartOfAnyPrefab(go);
            response["is_prefab_instance"] = isPrefab;
            if (isPrefab)
            {
                var prefabAsset = UnityEditor.PrefabUtility
                    .GetCorrespondingObjectFromOriginalSource(go);
                if (prefabAsset != null)
                {
                    var assetPath = UnityEditor.AssetDatabase
                        .GetAssetPath(prefabAsset);
                    if (!string.IsNullOrEmpty(assetPath))
                        response["prefab_asset"] = assetPath;
                }
            }
#else
            response["is_prefab_instance"] = false;
#endif
        }
    }
}
