#if THEATRE_HAS_ENTITIES
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using Unity.Entities;
using UnityEngine;

namespace Theatre.Editor.Tools.ECS
{
    /// <summary>
    /// MCP tool: ecs_inspect
    /// Deep-inspects a single ECS entity: all component data with values.
    /// Supports optional component name filter.
    /// </summary>
    public static class EcsInspectTool
    {
        private static readonly JToken s_inputSchema;

        static EcsInspectTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""entity_index"": {
                        ""type"": ""integer"",
                        ""description"": ""Entity index (from ecs_snapshot or ecs_query).""
                    },
                    ""entity_version"": {
                        ""type"": ""integer"",
                        ""description"": ""Entity version (from ecs_snapshot or ecs_query).""
                    },
                    ""world"": {
                        ""type"": ""string"",
                        ""description"": ""World name. Defaults to the default GameObjectInjection world.""
                    },
                    ""components"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Optional filter: only return these component types by name.""
                    },
                    ""budget"": {
                        ""type"": ""integer"",
                        ""description"": ""Token budget for the response. Default 1500.""
                    }
                },
                ""required"": [""entity_index"", ""entity_version""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "ecs_inspect",
                description: "Deep-inspect a single ECS entity. Returns all component data with values. "
                    + "Requires entity_index and entity_version from ecs_snapshot or ecs_query. "
                    + "Use components filter to read specific types.",
                inputSchema: s_inputSchema,
                group: ToolGroup.ECSEntity,
                handler: Execute,
                annotations: new McpToolAnnotations { ReadOnlyHint = true }
            ));
        }

        private static string Execute(JToken arguments)
        {
            if (arguments == null || arguments.Type != JTokenType.Object)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Arguments must be a JSON object",
                    "Provide {\"entity_index\": N, \"entity_version\": N}");

            var args = (JObject)arguments;

            try
            {
                return Inspect(args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] ecs_inspect failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"ecs_inspect failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }

        /// <summary>Run the ecs_inspect operation.</summary>
        internal static string Inspect(JObject args)
        {
            var indexToken = args["entity_index"];
            var versionToken = args["entity_version"];

            if (indexToken == null || versionToken == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required parameters 'entity_index' and 'entity_version'",
                    "Use ecs_snapshot or ecs_query to get entity identity");

            int entityIndex = indexToken.Value<int>();
            int entityVersion = versionToken.Value<int>();

            var worldName = args["world"]?.Value<string>();
            var world = EcsHelpers.ResolveWorld(worldName, out var resolveError);
            if (world == null)
                return ResponseHelpers.ErrorResponse("world_not_found", resolveError,
                    "Use ecs_world list_worlds to see available worlds");

            var entity = EcsHelpers.ResolveEntity(world, entityIndex, entityVersion, out var entityError);
            if (entity == Unity.Entities.Entity.Null)
                return ResponseHelpers.ErrorResponse("entity_not_found", entityError,
                    "Entity may have been destroyed. Use ecs_snapshot to find current entities.");

            // Optional component filter
            string[] componentFilter = null;
            var componentsToken = args["components"] as JArray;
            if (componentsToken != null && componentsToken.Count > 0)
            {
                componentFilter = new string[componentsToken.Count];
                for (int i = 0; i < componentsToken.Count; i++)
                    componentFilter[i] = componentsToken[i].Value<string>();
            }

            var em = world.EntityManager;
            var components = EcsHelpers.ReadEntityComponents(em, entity, componentFilter);

            var response = new JObject();
            response["result"] = "ok";
            response["world"] = world.Name;
            response["entity"] = new JObject
            {
                ["index"] = entity.Index,
                ["version"] = entity.Version
            };
            response["components"] = components;
            response["component_count"] = components.Count;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }
    }
}
#endif
