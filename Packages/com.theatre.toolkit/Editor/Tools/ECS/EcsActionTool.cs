#if THEATRE_HAS_ENTITIES
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Theatre.Editor.Tools;
using Theatre.Stage;
using Theatre.Transport;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Theatre.Editor.Tools.ECS
{
    /// <summary>
    /// MCP tool: ecs_action
    /// Entity mutation: create, destroy, add/remove/set components.
    /// Operations: create_entity, destroy_entity, add_component, remove_component, set_component.
    /// </summary>
    public static class EcsActionTool
    {
        private static readonly JToken s_inputSchema;

        static EcsActionTool()
        {
            s_inputSchema = JToken.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": {
                        ""type"": ""string"",
                        ""enum"": [""create_entity"", ""destroy_entity"", ""add_component"", ""remove_component"", ""set_component""],
                        ""description"": ""The entity action to perform.""
                    },
                    ""entity_index"": {
                        ""type"": ""integer"",
                        ""description"": ""Entity index (required for destroy/add/remove/set).""
                    },
                    ""entity_version"": {
                        ""type"": ""integer"",
                        ""description"": ""Entity version (required for destroy/add/remove/set).""
                    },
                    ""component"": {
                        ""type"": ""string"",
                        ""description"": ""Component type name for add/remove/set operations.""
                    },
                    ""components"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Component type names for create_entity archetype.""
                    },
                    ""values"": {
                        ""type"": ""object"",
                        ""description"": ""Component field values for set_component. LocalTransform supports: position [x,y,z], rotation [x,y,z,w], scale float.""
                    },
                    ""world"": {
                        ""type"": ""string"",
                        ""description"": ""World name. Defaults to the default GameObjectInjection world.""
                    }
                },
                ""required"": [""operation""]
            }");
        }

        /// <summary>Register this tool with the given registry.</summary>
        public static void Register(ToolRegistry registry)
        {
            registry.Register(new ToolRegistration(
                name: "ecs_action",
                description: "ECS entity mutation: create entities, destroy entities, add/remove/set components. "
                    + "Operations: create_entity, destroy_entity, add_component, remove_component, set_component. "
                    + "Works in Edit and Play mode whenever a World exists.",
                inputSchema: s_inputSchema,
                group: ToolGroup.ECSAction,
                handler: Execute,
                annotations: new McpToolAnnotations { ReadOnlyHint = false }
            ));
        }

        private static string Execute(JToken arguments) =>
            CompoundToolDispatcher.Execute(
                "ecs_action",
                arguments,
                (args, operation) => operation switch
                {
                    "create_entity"    => CreateEntity(args),
                    "destroy_entity"   => DestroyEntity(args),
                    "add_component"    => AddComponent(args),
                    "remove_component" => RemoveComponent(args),
                    "set_component"    => SetComponent(args),
                    _ => ResponseHelpers.ErrorResponse(
                        "invalid_parameter",
                        $"Unknown operation '{operation}'",
                        "Valid operations: create_entity, destroy_entity, add_component, remove_component, set_component")
                },
                "create_entity, destroy_entity, add_component, remove_component, set_component");

        /// <summary>Create a new entity, optionally with an archetype defined by component types.</summary>
        internal static string CreateEntity(JObject args)
        {
            var world = EcsHelpers.ResolveWorld(args["world"]?.Value<string>(), out var resolveError);
            if (world == null)
                return ResponseHelpers.ErrorResponse("world_not_found", resolveError,
                    "Use ecs_world list_worlds to see available worlds");

            var em = world.EntityManager;
            Entity entity;

            var componentsToken = args["components"] as JArray;
            if (componentsToken != null && componentsToken.Count > 0)
            {
                var componentTypes = new ComponentType[componentsToken.Count];
                for (int i = 0; i < componentsToken.Count; i++)
                {
                    var typeName = componentsToken[i].Value<string>();
                    var ct = EcsHelpers.ResolveComponentType(typeName, out var ctError);
                    if (ctError != null)
                        return ResponseHelpers.ErrorResponse("invalid_component", ctError,
                            "Check component type name spelling");
                    componentTypes[i] = ct;
                }
                var archetype = em.CreateArchetype(componentTypes);
                entity = em.CreateEntity(archetype);
            }
            else
            {
                entity = em.CreateEntity();
            }

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "create_entity";
            response["world"] = world.Name;
            response["entity"] = new JObject
            {
                ["index"] = entity.Index,
                ["version"] = entity.Version
            };
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Destroy an entity by index and version.</summary>
        internal static string DestroyEntity(JObject args)
        {
            var indexToken = args["entity_index"];
            var versionToken = args["entity_version"];

            if (indexToken == null || versionToken == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'entity_index' and 'entity_version' parameters",
                    "Use ecs_snapshot or ecs_query to get entity identity");

            var world = EcsHelpers.ResolveWorld(args["world"]?.Value<string>(), out var resolveError);
            if (world == null)
                return ResponseHelpers.ErrorResponse("world_not_found", resolveError,
                    "Use ecs_world list_worlds to see available worlds");

            var entity = EcsHelpers.ResolveEntity(world,
                indexToken.Value<int>(), versionToken.Value<int>(), out var entityError);
            if (entity == Entity.Null)
                return ResponseHelpers.ErrorResponse("entity_not_found", entityError,
                    "Entity may have already been destroyed");

            world.EntityManager.DestroyEntity(entity);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "destroy_entity";
            response["world"] = world.Name;
            response["entity"] = new JObject
            {
                ["index"] = entity.Index,
                ["version"] = entity.Version
            };
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Add a component to an existing entity.</summary>
        internal static string AddComponent(JObject args)
        {
            var indexToken = args["entity_index"];
            var versionToken = args["entity_version"];

            if (indexToken == null || versionToken == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'entity_index' and 'entity_version'",
                    "Use ecs_snapshot to find entity identity");

            var componentName = args["component"]?.Value<string>();
            if (string.IsNullOrEmpty(componentName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'component' parameter",
                    "Provide a component type name like 'LocalTransform'");

            var world = EcsHelpers.ResolveWorld(args["world"]?.Value<string>(), out var resolveError);
            if (world == null)
                return ResponseHelpers.ErrorResponse("world_not_found", resolveError,
                    "Use ecs_world list_worlds to see available worlds");

            var entity = EcsHelpers.ResolveEntity(world,
                indexToken.Value<int>(), versionToken.Value<int>(), out var entityError);
            if (entity == Entity.Null)
                return ResponseHelpers.ErrorResponse("entity_not_found", entityError,
                    "Entity may have been destroyed");

            var componentType = EcsHelpers.ResolveComponentType(componentName, out var ctError);
            if (ctError != null)
                return ResponseHelpers.ErrorResponse("invalid_component", ctError,
                    "Check component type name spelling");

            world.EntityManager.AddComponent(entity, componentType);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "add_component";
            response["world"] = world.Name;
            response["entity"] = new JObject
            {
                ["index"] = entity.Index,
                ["version"] = entity.Version
            };
            response["component"] = componentName;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>Remove a component from an existing entity.</summary>
        internal static string RemoveComponent(JObject args)
        {
            var indexToken = args["entity_index"];
            var versionToken = args["entity_version"];

            if (indexToken == null || versionToken == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'entity_index' and 'entity_version'",
                    "Use ecs_snapshot to find entity identity");

            var componentName = args["component"]?.Value<string>();
            if (string.IsNullOrEmpty(componentName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'component' parameter",
                    "Provide a component type name like 'LocalTransform'");

            var world = EcsHelpers.ResolveWorld(args["world"]?.Value<string>(), out var resolveError);
            if (world == null)
                return ResponseHelpers.ErrorResponse("world_not_found", resolveError,
                    "Use ecs_world list_worlds to see available worlds");

            var entity = EcsHelpers.ResolveEntity(world,
                indexToken.Value<int>(), versionToken.Value<int>(), out var entityError);
            if (entity == Entity.Null)
                return ResponseHelpers.ErrorResponse("entity_not_found", entityError,
                    "Entity may have been destroyed");

            var componentType = EcsHelpers.ResolveComponentType(componentName, out var ctError);
            if (ctError != null)
                return ResponseHelpers.ErrorResponse("invalid_component", ctError,
                    "Check component type name spelling");

            world.EntityManager.RemoveComponent(entity, componentType);

            var response = new JObject();
            response["result"] = "ok";
            response["operation"] = "remove_component";
            response["world"] = world.Name;
            response["entity"] = new JObject
            {
                ["index"] = entity.Index,
                ["version"] = entity.Version
            };
            response["component"] = componentName;
            ResponseHelpers.AddFrameContext(response);
            return response.ToString(Formatting.None);
        }

        /// <summary>
        /// Set component data on an entity.
        /// Supports LocalTransform (position, rotation, scale) with direct field assignment.
        /// Other types return a best-effort error explaining limitations.
        /// </summary>
        internal static string SetComponent(JObject args)
        {
            var indexToken = args["entity_index"];
            var versionToken = args["entity_version"];

            if (indexToken == null || versionToken == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'entity_index' and 'entity_version'",
                    "Use ecs_snapshot to find entity identity");

            var componentName = args["component"]?.Value<string>();
            if (string.IsNullOrEmpty(componentName))
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'component' parameter",
                    "Provide a component type name like 'LocalTransform'");

            var values = args["values"] as JObject;
            if (values == null)
                return ResponseHelpers.ErrorResponse(
                    "invalid_parameter",
                    "Missing required 'values' parameter",
                    "Provide a values object, e.g. {\"position\": [1,2,3]}");

            var world = EcsHelpers.ResolveWorld(args["world"]?.Value<string>(), out var resolveError);
            if (world == null)
                return ResponseHelpers.ErrorResponse("world_not_found", resolveError,
                    "Use ecs_world list_worlds to see available worlds");

            var entity = EcsHelpers.ResolveEntity(world,
                indexToken.Value<int>(), versionToken.Value<int>(), out var entityError);
            if (entity == Entity.Null)
                return ResponseHelpers.ErrorResponse("entity_not_found", entityError,
                    "Entity may have been destroyed");

            var em = world.EntityManager;

            // Fast-path: LocalTransform
            if (componentName == "LocalTransform")
            {
                if (!em.HasComponent<LocalTransform>(entity))
                    return ResponseHelpers.ErrorResponse("component_not_found",
                        "Entity does not have LocalTransform",
                        "Use ecs_action add_component to add it first");

                var lt = em.GetComponentData<LocalTransform>(entity);

                var posToken = values["position"] as JArray;
                if (posToken != null && posToken.Count >= 3)
                    lt.Position = new float3(
                        posToken[0].Value<float>(),
                        posToken[1].Value<float>(),
                        posToken[2].Value<float>());

                var rotToken = values["rotation"] as JArray;
                if (rotToken != null && rotToken.Count >= 4)
                    lt.Rotation = new quaternion(
                        rotToken[0].Value<float>(),
                        rotToken[1].Value<float>(),
                        rotToken[2].Value<float>(),
                        rotToken[3].Value<float>());

                var scaleToken = values["scale"];
                if (scaleToken != null)
                    lt.Scale = scaleToken.Value<float>();

                em.SetComponentData(entity, lt);

                var response = new JObject();
                response["result"] = "ok";
                response["operation"] = "set_component";
                response["world"] = world.Name;
                response["entity"] = new JObject
                {
                    ["index"] = entity.Index,
                    ["version"] = entity.Version
                };
                response["component"] = componentName;
                ResponseHelpers.AddFrameContext(response);
                return response.ToString(Formatting.None);
            }

            // For other component types, we cannot generically set values without compile-time types.
            return ResponseHelpers.ErrorResponse(
                "unsupported_component",
                $"set_component does not support '{componentName}' — only 'LocalTransform' has a fast-path",
                "Use LocalTransform for position/rotation/scale. For other types, access them in C# directly.");
        }
    }
}
#endif
