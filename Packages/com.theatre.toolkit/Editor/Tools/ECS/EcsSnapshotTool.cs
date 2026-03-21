#if THEATRE_HAS_ENTITIES
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Transport;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Theatre.Editor.Tools.ECS
{
    /// <summary>
    /// MCP tool: ecs_snapshot
    /// Returns a token-budgeted spatial overview of entities in an ECS world.
    /// Supports required_components filter and focus/radius spatial filter.
    /// </summary>
    public static class EcsSnapshotTool
    {
        private static readonly JToken s_inputSchema;

        static EcsSnapshotTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""world"": {
                        ""type"": ""string"",
                        ""description"": ""World name. Defaults to the default GameObjectInjection world.""
                    },
                    ""required_components"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Only include entities that have all of these component types.""
                    },
                    ""focus"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""minItems"": 3,
                        ""maxItems"": 3,
                        ""description"": ""Optional center point [x, y, z] for spatial filtering.""
                    },
                    ""radius"": {
                        ""type"": ""number"",
                        ""description"": ""If focus is set, only include entities within this radius.""
                    },
                    ""budget"": {
                        ""type"": ""integer"",
                        ""description"": ""Token budget for the response. Default 1500.""
                    }
                }
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "ecs_snapshot",
                description: "Token-budgeted spatial overview of ECS entities. "
                    + "Supports filtering by required component types and by proximity to a focus point. "
                    + "Each entity includes identity, archetype summary, and position.",
                inputSchema: s_inputSchema,
                group: ToolGroup.ECSWorld,
                handler: Execute,
                annotations: new McpToolAnnotations { ReadOnlyHint = true }
            ));
        }

        private static string Execute(JToken arguments)
        {
            var args = arguments as JObject ?? new JObject();

            try
            {
                return Snapshot(args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] ecs_snapshot failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"ecs_snapshot failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }

        /// <summary>Run the ecs_snapshot operation.</summary>
        internal static string Snapshot(JObject args)
        {
            var worldName = args["world"]?.Value<string>();
            var world = EcsHelpers.ResolveWorld(worldName, out var resolveError);
            if (world == null)
                return ResponseHelpers.ErrorResponse("world_not_found", resolveError,
                    "Use ecs_world list_worlds to see available worlds");

            var budget = new TokenBudget(
                args["budget"]?.Value<int>() ?? TokenBudget.DefaultBudget);

            var em = world.EntityManager;

            // Parse focus + radius
            bool hasFocus = false;
            float3 focus = float3.zero;
            float radius = float.MaxValue;

            var focusToken = args["focus"] as JArray;
            if (focusToken != null && focusToken.Count >= 3)
            {
                focus = new float3(
                    focusToken[0].Value<float>(),
                    focusToken[1].Value<float>(),
                    focusToken[2].Value<float>());
                hasFocus = true;

                var radiusToken = args["radius"];
                if (radiusToken != null)
                    radius = radiusToken.Value<float>();
            }

            // Build required_components filter
            var requiredComponentsToken = args["required_components"] as JArray;
            string[] requiredComponents = null;
            if (requiredComponentsToken != null && requiredComponentsToken.Count > 0)
            {
                requiredComponents = new string[requiredComponentsToken.Count];
                for (int i = 0; i < requiredComponentsToken.Count; i++)
                    requiredComponents[i] = requiredComponentsToken[i].Value<string>();
            }

            // Build EntityQuery
            EntityQuery query;
            if (requiredComponents != null && requiredComponents.Length > 0)
            {
                var componentTypes = new ComponentType[requiredComponents.Length];
                for (int i = 0; i < requiredComponents.Length; i++)
                {
                    var ct = EcsHelpers.ResolveComponentType(requiredComponents[i], out var ctError);
                    if (ctError != null)
                        return ResponseHelpers.ErrorResponse("invalid_component",
                            ctError, "Check component type name spelling");
                    componentTypes[i] = ct;
                }
                query = em.CreateEntityQuery(componentTypes);
            }
            else
            {
                query = em.UniversalQuery;
            }

            var entitiesArray = new JArray();
            bool truncated = false;
            int totalMatched = 0;

            try
            {
                using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
                totalMatched = entities.Length;

                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];

                    // Spatial filter
                    float distance = -1f;
                    if (hasFocus)
                    {
                        var (pos, found) = EcsHelpers.GetEntityPosition(em, entity);
                        if (!found) continue;
                        distance = math.distance(focus, pos);
                        if (distance > radius) continue;
                    }

                    // Build entity object
                    var entityObj = new JObject();
                    entityObj["entity"] = new JObject
                    {
                        ["index"] = entity.Index,
                        ["version"] = entity.Version
                    };

                    // Position
                    var (entityPos, hasPos) = EcsHelpers.GetEntityPosition(em, entity);
                    if (hasPos)
                    {
                        entityObj["position"] = new JArray(
                            Math.Round(entityPos.x, 3),
                            Math.Round(entityPos.y, 3),
                            Math.Round(entityPos.z, 3));
                    }

                    if (hasFocus && distance >= 0)
                        entityObj["distance"] = Math.Round(distance, 3);

                    // Component type summary (brief — snapshot not deep inspect)
                    using var compTypes = em.GetComponentTypes(entity);
                    var typeNames = new JArray();
                    foreach (var ct in compTypes)
                    {
                        try
                        {
                            var t = TypeManager.GetType(ct.TypeIndex);
                            if (t != null) typeNames.Add(t.Name);
                        }
                        catch { }
                    }
                    entityObj["component_types"] = typeNames;

                    var json = entityObj.ToString(Formatting.None);
                    if (budget.WouldExceed(json.Length)) { truncated = true; break; }

                    entitiesArray.Add(entityObj);
                    budget.Add(json.Length);
                }
            }
            finally
            {
                // Only dispose if we created it (not UniversalQuery)
                if (requiredComponents != null && requiredComponents.Length > 0)
                    query.Dispose();
            }

            var response = new JObject();
            response["result"] = "ok";
            response["world"] = world.Name;
            response["entities"] = entitiesArray;
            response["returned"] = entitiesArray.Count;
            response["total_matched"] = totalMatched;
            response["budget"] = budget.ToBudgetJObject(truncated,
                truncated ? "budget_exceeded" : null,
                truncated ? "Increase budget or narrow with required_components / radius" : null);
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }
    }
}
#endif
