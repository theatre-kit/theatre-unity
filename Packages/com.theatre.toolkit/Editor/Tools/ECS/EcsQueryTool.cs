#if THEATRE_HAS_ENTITIES
using System;
using System.Collections.Generic;
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
    /// MCP tool: ecs_query
    /// Position-based spatial queries over ECS entities with LocalTransform.
    /// No physics dependency — pure position-based AABB and distance checks.
    /// Operations: nearest, radius, overlap.
    /// </summary>
    public static class EcsQueryTool
    {
        private static readonly JToken s_inputSchema;

        static EcsQueryTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""nearest"", ""radius"", ""overlap""],
                        ""description"": ""The spatial query operation: nearest (N closest), radius (within distance), overlap (AABB).""
                    },
                    ""origin"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""minItems"": 3,
                        ""maxItems"": 3,
                        ""description"": ""Query origin [x, y, z]. Required for nearest and radius.""
                    },
                    ""count"": {
                        ""type"": ""integer"",
                        ""description"": ""Maximum number of results for nearest query.""
                    },
                    ""radius"": {
                        ""type"": ""number"",
                        ""description"": ""Search radius for radius query.""
                    },
                    ""max_distance"": {
                        ""type"": ""number"",
                        ""description"": ""Optional max distance cap for nearest query.""
                    },
                    ""min"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""minItems"": 3,
                        ""maxItems"": 3,
                        ""description"": ""AABB minimum corner [x, y, z] for overlap query.""
                    },
                    ""max"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""number"" },
                        ""minItems"": 3,
                        ""maxItems"": 3,
                        ""description"": ""AABB maximum corner [x, y, z] for overlap query.""
                    },
                    ""required_components"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Only include entities that have all of these component types.""
                    },
                    ""world"": {
                        ""type"": ""string"",
                        ""description"": ""World name. Defaults to the default GameObjectInjection world.""
                    },
                    ""budget"": {
                        ""type"": ""integer"",
                        ""description"": ""Token budget for the response. Default 1500.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "ecs_query",
                description: "Position-based spatial queries over ECS entities. "
                    + "Operations: nearest (N closest entities), radius (within distance), overlap (AABB bounds). "
                    + "Queries entities with LocalTransform — no physics dependency.",
                inputSchema: s_inputSchema,
                group: ToolGroup.ECSQuery,
                handler: Execute,
                annotations: new McpToolAnnotations { ReadOnlyHint = true }
            ));
        }

        private static string Execute(JToken arguments)
        {
            if (arguments == null || arguments.Type != JTokenType.Object)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Arguments must be a JSON object with an 'operation' field",
                    "Provide {\"operation\": \"nearest\", \"origin\": [0,0,0], \"count\": 10}");

            var args = (JObject)arguments;
            var operation = args["operation"]?.Value<string>();

            if (string.IsNullOrEmpty(operation))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'operation' parameter",
                    "Valid operations: nearest, radius, overlap");

            try
            {
                return operation switch
                {
                    "nearest" => Nearest(args),
                    "radius"  => Radius(args),
                    "overlap" => Overlap(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: nearest, radius, overlap")
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Theatre] ecs_query:{operation} failed: {ex}");
                return ResponseHelpers.ErrorResponse(
                    "internal_error",
                    $"ecs_query:{operation} failed: {ex.Message}",
                    "Check the Unity Console for details");
            }
        }

        /// <summary>Find the N nearest entities to an origin point.</summary>
        internal static string Nearest(JObject args)
        {
            var originToken = args["origin"] as JArray;
            if (originToken == null || originToken.Count < 3)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'origin' parameter [x, y, z]",
                    "Provide an origin array like [0, 0, 0]");

            int count = args["count"]?.Value<int>() ?? 10;
            float maxDistance = args["max_distance"]?.Value<float>() ?? float.MaxValue;

            var origin = new float3(
                originToken[0].Value<float>(),
                originToken[1].Value<float>(),
                originToken[2].Value<float>());

            var world = EcsHelpers.ResolveWorld(args["world"]?.Value<string>(), out var resolveError);
            if (world == null)
                return ResponseHelpers.ErrorResponse("world_not_found", resolveError,
                    "Use ecs_world list_worlds to see available worlds");

            var budget = new TokenBudget(args["budget"]?.Value<int>() ?? TokenBudget.DefaultBudget);
            var em = world.EntityManager;

            using var query = BuildQuery(em, args);
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);

            // Collect (entity, distance) pairs
            var candidates = new List<(Entity entity, float distance)>(entities.Length);
            for (int i = 0; i < entities.Length; i++)
            {
                var (pos, found) = EcsHelpers.GetEntityPosition(em, entities[i]);
                if (!found) continue;
                float dist = math.distance(origin, pos);
                if (dist <= maxDistance)
                    candidates.Add((entities[i], dist));
            }

            candidates.Sort((a, b) => a.distance.CompareTo(b.distance));
            count = Math.Min(count, candidates.Count);

            var resultsArray = new JArray();
            bool truncated = false;
            for (int i = 0; i < count; i++)
            {
                var (entity, dist) = candidates[i];
                var obj = BuildEntityResult(em, entity, dist);
                var json = obj.ToString(Formatting.None);
                if (budget.WouldExceed(json.Length)) { truncated = true; break; }
                resultsArray.Add(obj);
                budget.Add(json.Length);
            }

            return BuildQueryResponse("nearest", world.Name, resultsArray, candidates.Count, budget, truncated);
        }

        /// <summary>Find all entities within a radius of an origin point.</summary>
        internal static string Radius(JObject args)
        {
            var originToken = args["origin"] as JArray;
            if (originToken == null || originToken.Count < 3)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'origin' parameter [x, y, z]",
                    "Provide an origin array like [0, 0, 0]");

            var radiusToken = args["radius"];
            if (radiusToken == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'radius' parameter",
                    "Provide a radius like 10.0");

            var origin = new float3(
                originToken[0].Value<float>(),
                originToken[1].Value<float>(),
                originToken[2].Value<float>());
            float radius = radiusToken.Value<float>();

            var world = EcsHelpers.ResolveWorld(args["world"]?.Value<string>(), out var resolveError);
            if (world == null)
                return ResponseHelpers.ErrorResponse("world_not_found", resolveError,
                    "Use ecs_world list_worlds to see available worlds");

            var budget = new TokenBudget(args["budget"]?.Value<int>() ?? TokenBudget.DefaultBudget);
            var em = world.EntityManager;

            using var query = BuildQuery(em, args);
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);

            var resultsArray = new JArray();
            bool truncated = false;
            int totalMatched = 0;

            for (int i = 0; i < entities.Length; i++)
            {
                var (pos, found) = EcsHelpers.GetEntityPosition(em, entities[i]);
                if (!found) continue;
                float dist = math.distance(origin, pos);
                if (dist > radius) continue;
                totalMatched++;

                var obj = BuildEntityResult(em, entities[i], dist);
                var json = obj.ToString(Formatting.None);
                if (budget.WouldExceed(json.Length)) { truncated = true; break; }
                resultsArray.Add(obj);
                budget.Add(json.Length);
            }

            return BuildQueryResponse("radius", world.Name, resultsArray, totalMatched, budget, truncated);
        }

        /// <summary>Find all entities whose position falls within an AABB.</summary>
        internal static string Overlap(JObject args)
        {
            var minToken = args["min"] as JArray;
            var maxToken = args["max"] as JArray;

            if (minToken == null || minToken.Count < 3)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'min' parameter [x, y, z]",
                    "Provide a min corner like [-5, -5, -5]");

            if (maxToken == null || maxToken.Count < 3)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'max' parameter [x, y, z]",
                    "Provide a max corner like [5, 5, 5]");

            var boundsMin = new float3(
                minToken[0].Value<float>(),
                minToken[1].Value<float>(),
                minToken[2].Value<float>());
            var boundsMax = new float3(
                maxToken[0].Value<float>(),
                maxToken[1].Value<float>(),
                maxToken[2].Value<float>());

            var world = EcsHelpers.ResolveWorld(args["world"]?.Value<string>(), out var resolveError);
            if (world == null)
                return ResponseHelpers.ErrorResponse("world_not_found", resolveError,
                    "Use ecs_world list_worlds to see available worlds");

            var budget = new TokenBudget(args["budget"]?.Value<int>() ?? TokenBudget.DefaultBudget);
            var em = world.EntityManager;

            using var query = BuildQuery(em, args);
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);

            var resultsArray = new JArray();
            bool truncated = false;
            int totalMatched = 0;

            for (int i = 0; i < entities.Length; i++)
            {
                var (pos, found) = EcsHelpers.GetEntityPosition(em, entities[i]);
                if (!found) continue;

                // AABB containment check
                if (pos.x < boundsMin.x || pos.x > boundsMax.x) continue;
                if (pos.y < boundsMin.y || pos.y > boundsMax.y) continue;
                if (pos.z < boundsMin.z || pos.z > boundsMax.z) continue;

                totalMatched++;
                var obj = BuildEntityResult(em, entities[i], -1f);
                var json = obj.ToString(Formatting.None);
                if (budget.WouldExceed(json.Length)) { truncated = true; break; }
                resultsArray.Add(obj);
                budget.Add(json.Length);
            }

            return BuildQueryResponse("overlap", world.Name, resultsArray, totalMatched, budget, truncated);
        }

        // --- Helpers ---

        private static EntityQuery BuildQuery(EntityManager em, JObject args)
        {
            var requiredToken = args["required_components"] as JArray;

            // Always require LocalTransform so we can get positions
            if (requiredToken == null || requiredToken.Count == 0)
            {
                return em.CreateEntityQuery(
                    ComponentType.ReadOnly<LocalTransform>());
            }

            var componentTypes = new ComponentType[requiredToken.Count + 1];
            componentTypes[0] = ComponentType.ReadOnly<LocalTransform>();
            for (int i = 0; i < requiredToken.Count; i++)
            {
                var ct = EcsHelpers.ResolveComponentType(requiredToken[i].Value<string>(), out _);
                componentTypes[i + 1] = ct;
            }
            return em.CreateEntityQuery(componentTypes);
        }

        private static JObject BuildEntityResult(EntityManager em, Entity entity, float distance)
        {
            var obj = new JObject();
            obj["entity"] = new JObject
            {
                ["index"] = entity.Index,
                ["version"] = entity.Version
            };

            var (pos, _) = EcsHelpers.GetEntityPosition(em, entity);
            obj["position"] = new JArray(
                Math.Round(pos.x, 3),
                Math.Round(pos.y, 3),
                Math.Round(pos.z, 3));

            if (distance >= 0)
                obj["distance"] = Math.Round(distance, 3);

            return obj;
        }

        private static string BuildQueryResponse(
            string operation, string worldName,
            JArray results, int totalMatched,
            TokenBudget budget, bool truncated)
        {
            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = operation;
            response["world"] = worldName;
            response["results"] = results;
            response["returned"] = results.Count;
            response["total_matched"] = totalMatched;
            response["budget"] = budget.ToBudgetJObject(truncated,
                truncated ? "budget_exceeded" : null,
                truncated ? "Increase budget or narrow query with required_components" : null);
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }
    }
}
#endif
