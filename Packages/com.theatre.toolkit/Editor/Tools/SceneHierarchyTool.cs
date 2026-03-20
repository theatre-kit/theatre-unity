using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Theatre.Editor
{
    /// <summary>
    /// MCP tool: scene_hierarchy
    /// Navigate the Transform hierarchy with list, find, search, and path operations.
    /// </summary>
    public static class SceneHierarchyTool
    {
        private static readonly JToken s_inputSchema;

        static SceneHierarchyTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""list"", ""find"", ""search"", ""path""],
                        ""description"": ""Operation to perform.""
                    },
                    ""path"": {
                        ""type"": ""string"",
                        ""description"": ""For 'list': parent path (omit for roots). For 'path': not used. For 'find'/'search': root subtree to limit search.""
                    },
                    ""pattern"": {
                        ""type"": ""string"",
                        ""description"": ""For 'find': name glob pattern (e.g., 'Scout*', '*Door*').""
                    },
                    ""instance_id"": {
                        ""type"": ""integer"",
                        ""description"": ""For 'path': get hierarchy path and metadata for this instance ID.""
                    },
                    ""include_components"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""For 'search': filter by component type.""
                    },
                    ""tag"": {
                        ""type"": ""string"",
                        ""description"": ""For 'search': filter by tag.""
                    },
                    ""layer"": {
                        ""type"": ""string"",
                        ""description"": ""For 'search': filter by layer name.""
                    },
                    ""include_inactive"": {
                        ""type"": ""boolean"",
                        ""default"": false,
                        ""description"": ""Include disabled GameObjects in results.""
                    },
                    ""cursor"": {
                        ""type"": ""string"",
                        ""description"": ""Pagination cursor from a previous response.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "scene_hierarchy",
                description: "Navigate the Transform hierarchy. Operations: "
                    + "'list' children of a path, 'find' by name pattern, "
                    + "'search' by component/tag/layer, 'path' to get info for an instance_id.",
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
            if (arguments == null || arguments.Type != JTokenType.Object)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required parameter 'operation'",
                    "Specify operation: 'list', 'find', 'search', or 'path'");
            }

            var args = (JObject)arguments;
            var opToken = args["operation"];
            if (opToken == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required parameter 'operation'",
                    "Specify operation: 'list', 'find', 'search', or 'path'");
            }

            var operation = opToken.Value<string>();

            return operation switch
            {
                "list" => ExecuteList(args),
                "find" => ExecuteFind(args),
                "search" => ExecuteSearch(args),
                "path" => ExecutePath(args),
                _ => ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    $"Unknown operation '{operation}'",
                    "Valid operations: 'list', 'find', 'search', 'path'")
            };
        }

        private static string ExecuteList(JObject args)
        {
            string parentPath = null;
            bool includeInactive = false;
            int offset = 0;
            const int pageSize = 50;

            if (args["path"] != null)
                parentPath = args["path"].Value<string>();

            if (args["include_inactive"] != null)
                includeInactive = args["include_inactive"].Value<bool>();

            // Handle pagination cursor
            if (args["cursor"] != null)
            {
                var cursorStr = args["cursor"].Value<string>();
                var cursor = PaginationCursor.Decode(
                    cursorStr,
                    SceneManager.GetActiveScene().name);
                if (cursor == null)
                {
                    return ResponseHelpers.ErrorResponse(
                        "invalid_cursor",
                        "Pagination cursor is expired or invalid",
                        "Re-issue the original query without a cursor");
                }
                offset = cursor.Offset;
            }

            // Special case: root level with no parent path
            if (parentPath == null)
            {
                return ExecuteListRoots(includeInactive);
            }

            var (entries, total) = HierarchyWalker.ListChildren(
                parentPath, includeInactive, offset, pageSize);

            if (entries.Count == 0 && offset == 0)
            {
                // Verify parent exists
                var resolved = ObjectResolver.Resolve(path: parentPath);
                if (!resolved.Success)
                {
                    return ResponseHelpers.ErrorResponse(
                        resolved.ErrorCode,
                        resolved.ErrorMessage,
                        resolved.Suggestion);
                }
            }

            return BuildListResponse(entries, total, offset, pageSize,
                "scene_hierarchy", "list");
        }

        private static string ExecuteListRoots(bool includeInactive)
        {
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);
            ResponseHelpers.AddEditingContext(response);

            // List loaded scenes as top-level entries
            var results = new JArray();

            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                var roots = scene.GetRootGameObjects();
                int activeRootCount = 0;
                if (!includeInactive)
                {
                    foreach (var root in roots)
                        if (root.activeInHierarchy) activeRootCount++;
                }
                else
                {
                    activeRootCount = roots.Length;
                }

                var sceneObj = new JObject();
                sceneObj["scene"] = scene.name;
                sceneObj["root_count"] = activeRootCount;
                sceneObj["active"] = scene == SceneManager.GetActiveScene();
                results.Add(sceneObj);
            }

            response["results"] = results;
            return response.ToString(Formatting.None);
        }

        private static string ExecuteFind(JObject args)
        {
            if (args["pattern"] == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required parameter 'pattern' for find operation",
                    "Provide a name pattern like 'Scout*' or '*Door*'");
            }

            string root = null;
            bool includeInactive = false;

            if (args["path"] != null)
                root = args["path"].Value<string>();
            if (args["include_inactive"] != null)
                includeInactive = args["include_inactive"].Value<bool>();

            var entries = HierarchyWalker.Find(
                args["pattern"].Value<string>(), root, includeInactive);

            return BuildResultsResponse(entries);
        }

        private static string ExecuteSearch(JObject args)
        {
            var filter = new HierarchyFilter();

            var compToken = args["include_components"];
            if (compToken != null && compToken.Type == JTokenType.Array)
            {
                var list = new List<string>();
                foreach (var item in (JArray)compToken)
                    list.Add(item.Value<string>());
                filter.RequiredComponents = list.ToArray();
            }

            if (args["tag"] != null)
                filter.Tag = args["tag"].Value<string>();

            if (args["layer"] != null)
                filter.Layer = args["layer"].Value<string>();

            if (args["include_inactive"] != null)
                filter.IncludeInactive = args["include_inactive"].Value<bool>();

            string root = null;
            if (args["path"] != null)
                root = args["path"].Value<string>();

            if (filter.RequiredComponents == null
                && filter.Tag == null
                && filter.Layer == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Search requires at least one filter: include_components, tag, or layer",
                    "Specify include_components, tag, or layer to search by");
            }

            var entries = HierarchyWalker.Search(filter, root);
            return BuildResultsResponse(entries);
        }

        private static string ExecutePath(JObject args)
        {
            if (args["instance_id"] == null)
            {
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required parameter 'instance_id' for path operation",
                    "Provide the instance_id of the object to look up");
            }

            var resolved = ObjectResolver.Resolve(instanceId: args["instance_id"].Value<int>());
            if (!resolved.Success)
            {
                return ResponseHelpers.ErrorResponse(
                    resolved.ErrorCode,
                    resolved.ErrorMessage,
                    resolved.Suggestion);
            }

            var go = resolved.GameObject;
            var t = go.transform;

            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);

            var result = new JObject();
            result["path"] = ResponseHelpers.GetHierarchyPath(t);
            result["instance_id"] = go.GetInstanceID();
            result["name"] = go.name;
            result["scene"] = go.scene.name;
            result["position"] = ResponseHelpers.ToJArray(t.position);
            result["active"] = go.activeInHierarchy;
            result["children_count"] = t.childCount;

            // Parent info
            if (t.parent != null)
            {
                var parent = new JObject();
                parent["path"] = ResponseHelpers.GetHierarchyPath(t.parent);
                parent["instance_id"] = t.parent.gameObject.GetInstanceID();
                result["parent"] = parent;
            }

            response["result"] = result;
            return response.ToString(Formatting.None);
        }

        // --- Response builders ---

        private static string BuildListResponse(
            List<HierarchyEntry> entries,
            int total, int offset, int pageSize,
            string tool, string operation)
        {
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);

            var results = new JArray();
            foreach (var entry in entries)
                results.Add(BuildEntryJObject(entry));
            response["results"] = results;

            // Pagination
            bool hasMore = offset + entries.Count < total;
            string cursor = hasMore
                ? PaginationCursor.Create(tool, operation,
                    offset + entries.Count)
                : null;
            response["pagination"] = PaginationCursor.BuildPaginationJObject(
                cursor, hasMore, entries.Count, total);

            return response.ToString(Formatting.None);
        }

        private static string BuildResultsResponse(
            List<HierarchyEntry> entries)
        {
            var response = new JObject();
            ResponseHelpers.AddFrameContext(response);

            var results = new JArray();
            foreach (var entry in entries)
                results.Add(BuildEntryJObject(entry));
            response["results"] = results;
            response["returned"] = entries.Count;

            return response.ToString(Formatting.None);
        }

        private static JObject BuildEntryJObject(HierarchyEntry entry)
        {
            var obj = new JObject();
            obj["path"] = entry.Path;
            obj["instance_id"] = entry.InstanceId;
            obj["name"] = entry.Name;
            obj["position"] = ResponseHelpers.ToJArray(entry.Position);
            obj["active"] = entry.Active;
            obj["children_count"] = entry.ChildrenCount;

            if (entry.Components != null && entry.Components.Length > 0)
            {
                var comps = new JArray();
                foreach (var comp in entry.Components)
                    comps.Add(comp);
                obj["components"] = comps;
            }

            return obj;
        }
    }
}
