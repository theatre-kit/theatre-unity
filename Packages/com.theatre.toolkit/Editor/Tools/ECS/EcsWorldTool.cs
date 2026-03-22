#if THEATRE_HAS_ENTITIES
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Editor.Tools;
using Theatre.Stage;
using Theatre.Transport;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Theatre.Editor.Tools.ECS
{
    /// <summary>
    /// MCP tool: ecs_world
    /// Provides world-level ECS awareness: list worlds, world summary,
    /// list archetypes, and list systems.
    /// Operations: list_worlds, world_summary, list_archetypes, list_systems.
    /// </summary>
    public static class EcsWorldTool
    {
        private static readonly JToken s_inputSchema;

        static EcsWorldTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""list_worlds"", ""world_summary"", ""list_archetypes"", ""list_systems""],
                        ""description"": ""The world operation to perform.""
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
                name: "ecs_world",
                description: "ECS world awareness: list worlds, world summary, archetypes, and systems. "
                    + "Operations: list_worlds, world_summary, list_archetypes, list_systems.",
                inputSchema: s_inputSchema,
                group: ToolGroup.ECSWorld,
                handler: Execute,
                annotations: new McpToolAnnotations { ReadOnlyHint = true }
            ));
        }

        private static string Execute(JToken arguments) =>
            CompoundToolDispatcher.Execute(
                "ecs_world",
                arguments,
                (args, operation) => operation switch
                {
                    "list_worlds"     => ListWorlds(args),
                    "world_summary"   => WorldSummary(args),
                    "list_archetypes" => ListArchetypes(args),
                    "list_systems"    => ListSystems(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: list_worlds, world_summary, list_archetypes, list_systems")
                },
                "list_worlds, world_summary, list_archetypes, list_systems");

        /// <summary>List all active ECS worlds with entity and system counts.</summary>
        internal static string ListWorlds(JObject args)
        {
            var worldsArray = new JArray();

            foreach (var world in World.All)
            {
                if (world == null || !world.IsCreated) continue;

                var worldObj = new JObject();
                worldObj["name"] = world.Name;

                try
                {
                    worldObj["entity_count"] = world.EntityManager.UniversalQuery.CalculateEntityCount();
                }
                catch
                {
                    worldObj["entity_count"] = -1;
                }

                try
                {
                    worldObj["system_count"] = world.Systems.Count;
                }
                catch
                {
                    worldObj["system_count"] = -1;
                }

                worldsArray.Add(worldObj);
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "list_worlds";
            response["worlds"] = worldsArray;
            response["world_count"] = worldsArray.Count;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Return a summary of entity count and top archetypes for a world.</summary>
        internal static string WorldSummary(JObject args)
        {
            var worldName = args["world"]?.Value<string>();
            var world = EcsHelpers.ResolveWorld(worldName, out var resolveError);
            if (world == null)
                return ResponseHelpers.ErrorResponse("world_not_found", resolveError,
                    "Use list_worlds to see available worlds");

            var budget = new Theatre.Stage.TokenBudget(
                args["budget"]?.Value<int>() ?? Theatre.Stage.TokenBudget.DefaultBudget);

            var em = world.EntityManager;
            int totalEntities;
            try
            {
                totalEntities = em.UniversalQuery.CalculateEntityCount();
            }
            catch
            {
                totalEntities = -1;
            }

            int systemCount;
            try
            {
                systemCount = world.Systems.Count;
            }
            catch
            {
                systemCount = -1;
            }

            // Archetype breakdown via GetAllArchetypes
            var archetypeArray = new JArray();
            bool truncated = false;
            try
            {
                using var archetypes = new NativeList<EntityArchetype>(64, Allocator.Temp);
                em.GetAllArchetypes(archetypes);

                for (int i = 0; i < archetypes.Length; i++)
                {
                    var arch = archetypes[i];
                    if (!arch.Valid) continue;

                    var archObj = new JObject();
                    archObj["chunk_count"] = arch.ChunkCount;
                    var json = archObj.ToString(Formatting.None);
                    if (budget.WouldExceed(json.Length)) { truncated = true; break; }

                    // Component types for this archetype
                    using var types = arch.GetComponentTypes();
                    var typesArray = new JArray();
                    foreach (var ct in types)
                    {
                        try
                        {
                            var t = TypeManager.GetType(ct.TypeIndex);
                            if (t != null) typesArray.Add(t.Name);
                        }
                        catch { }
                    }
                    archObj["component_types"] = typesArray;
                    archetypeArray.Add(archObj);
                    budget.Add(json.Length);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Theatre] ecs_world:world_summary archetype enumeration failed: {ex.Message}");
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "world_summary";
            response["world"] = world.Name;
            response["entity_count"] = totalEntities;
            response["system_count"] = systemCount;
            response["archetypes"] = archetypeArray;
            response["budget"] = budget.ToBudgetJObject(truncated,
                truncated ? "budget_exceeded" : null,
                truncated ? "Increase budget or use list_archetypes with pagination" : null);
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>List all archetypes in a world with their component sets and entity counts.</summary>
        internal static string ListArchetypes(JObject args)
        {
            var worldName = args["world"]?.Value<string>();
            var world = EcsHelpers.ResolveWorld(worldName, out var resolveError);
            if (world == null)
                return ResponseHelpers.ErrorResponse("world_not_found", resolveError,
                    "Use list_worlds to see available worlds");

            var budget = new Theatre.Stage.TokenBudget(
                args["budget"]?.Value<int>() ?? Theatre.Stage.TokenBudget.DefaultBudget);

            var em = world.EntityManager;
            var archetypeArray = new JArray();
            bool truncated = false;

            try
            {
                using var archetypes = new NativeList<EntityArchetype>(64, Allocator.Temp);
                em.GetAllArchetypes(archetypes);

                for (int i = 0; i < archetypes.Length; i++)
                {
                    var arch = archetypes[i];
                    if (!arch.Valid) continue;

                    var archObj = new JObject();

                    using var types = arch.GetComponentTypes();
                    var typesArray = new JArray();
                    foreach (var ct in types)
                    {
                        try
                        {
                            var t = TypeManager.GetType(ct.TypeIndex);
                            if (t != null) typesArray.Add(t.Name);
                        }
                        catch { }
                    }

                    archObj["archetype_index"] = i;
                    archObj["component_types"] = typesArray;
                    archObj["chunk_count"] = arch.ChunkCount;

                    var json = archObj.ToString(Formatting.None);
                    if (budget.WouldExceed(json.Length)) { truncated = true; break; }

                    archetypeArray.Add(archObj);
                    budget.Add(json.Length);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Theatre] ecs_world:list_archetypes failed: {ex.Message}");
                return ResponseHelpers.ErrorResponse("internal_error",
                    $"Could not enumerate archetypes: {ex.Message}",
                    "Check the Unity Console for details");
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "list_archetypes";
            response["world"] = world.Name;
            response["archetypes"] = archetypeArray;
            response["returned"] = archetypeArray.Count;
            response["budget"] = budget.ToBudgetJObject(truncated,
                truncated ? "budget_exceeded" : null,
                truncated ? "Increase budget to see more archetypes" : null);
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>List all systems registered in a world.</summary>
        internal static string ListSystems(JObject args)
        {
            var worldName = args["world"]?.Value<string>();
            var world = EcsHelpers.ResolveWorld(worldName, out var resolveError);
            if (world == null)
                return ResponseHelpers.ErrorResponse("world_not_found", resolveError,
                    "Use list_worlds to see available worlds");

            var budget = new Theatre.Stage.TokenBudget(
                args["budget"]?.Value<int>() ?? Theatre.Stage.TokenBudget.DefaultBudget);

            var systemsArray = new JArray();
            bool truncated = false;

            try
            {
                var systems = world.Systems;
                foreach (var system in systems)
                {
                    if (system == null) continue;
                    try
                    {
                        var sysObj = new JObject();
                        sysObj["name"] = system.GetType().Name;
                        sysObj["full_name"] = system.GetType().FullName;
                        sysObj["enabled"] = system.Enabled;

                        var json = sysObj.ToString(Formatting.None);
                        if (budget.WouldExceed(json.Length)) { truncated = true; break; }

                        systemsArray.Add(sysObj);
                        budget.Add(json.Length);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Theatre] ecs_world:list_systems could not read system: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Theatre] ecs_world:list_systems enumeration failed: {ex.Message}");
                return ResponseHelpers.ErrorResponse("internal_error",
                    $"Could not enumerate systems: {ex.Message}",
                    "Check the Unity Console for details");
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "list_systems";
            response["world"] = world.Name;
            response["systems"] = systemsArray;
            response["returned"] = systemsArray.Count;
            response["budget"] = budget.ToBudgetJObject(truncated,
                truncated ? "budget_exceeded" : null,
                truncated ? "Increase budget to see more systems" : null);
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }
    }
}
#endif
