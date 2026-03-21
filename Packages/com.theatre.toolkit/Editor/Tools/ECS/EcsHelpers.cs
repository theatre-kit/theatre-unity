#if THEATRE_HAS_ENTITIES
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Theatre.Editor.Tools.ECS
{
    /// <summary>
    /// Shared utilities for ECS tool implementations.
    /// All methods must be called from the main thread.
    /// </summary>
    internal static class EcsHelpers
    {
        /// <summary>
        /// Find a World by name. If null/empty, returns
        /// World.DefaultGameObjectInjectionWorld. Returns null + error on failure.
        /// </summary>
        public static World ResolveWorld(string worldName, out string error)
        {
            if (string.IsNullOrEmpty(worldName))
            {
                var defaultWorld = World.DefaultGameObjectInjectionWorld;
                if (defaultWorld == null || !defaultWorld.IsCreated)
                {
                    error = "No default World exists. Are you in Play Mode or is a SubScene open?";
                    return null;
                }
                error = null;
                return defaultWorld;
            }

            foreach (var world in World.All)
            {
                if (world != null && world.IsCreated && world.Name == worldName)
                {
                    error = null;
                    return world;
                }
            }

            error = $"World '{worldName}' not found or has been disposed";
            return null;
        }

        /// <summary>
        /// Find an entity by index + version inside the given world.
        /// Returns null + error if the entity does not exist.
        /// </summary>
        public static Entity ResolveEntity(World world, int index, int version, out string error)
        {
            var entity = new Entity { Index = index, Version = version };
            if (!world.EntityManager.Exists(entity))
            {
                error = $"Entity (index={index}, version={version}) does not exist in world '{world.Name}'";
                return Entity.Null;
            }
            error = null;
            return entity;
        }

        /// <summary>
        /// Resolve a component type by its short type name from the TypeManager.
        /// Returns ComponentType.Null (TypeIndex 0) on failure.
        /// </summary>
        public static ComponentType ResolveComponentType(string typeName, out string error)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                error = "Component type name must not be null or empty";
                return ComponentType.ReadOnly(TypeIndex.Null);
            }

            var allTypes = TypeManager.GetAllTypes();
            foreach (var typeInfo in allTypes)
            {
                try
                {
                    var managedType = TypeManager.GetType(typeInfo.TypeIndex);
                    if (managedType != null && managedType.Name == typeName)
                    {
                        error = null;
                        return ComponentType.ReadWrite(typeInfo.TypeIndex);
                    }
                }
                catch
                {
                    // TypeManager.GetType can throw for some internal types — skip
                }
            }

            error = $"Component type '{typeName}' not found in TypeManager. "
                + "Ensure it implements IComponentData and the assembly is loaded.";
            return ComponentType.ReadOnly(TypeIndex.Null);
        }

        /// <summary>
        /// Read all (or a filtered subset of) component data from an entity as a JArray.
        /// Known types (LocalTransform, LocalToWorld) get fast-path serialization.
        /// Unknown IComponentData types are serialized via reflection.
        /// The caller must ensure this runs on the main thread.
        /// </summary>
        public static JArray ReadEntityComponents(EntityManager em, Entity entity, string[] filter = null)
        {
            var result = new JArray();

            using var componentTypes = em.GetComponentTypes(entity);
            foreach (var componentType in componentTypes)
            {
                try
                {
                    var managedType = TypeManager.GetType(componentType.TypeIndex);
                    if (managedType == null) continue;

                    var typeName = managedType.Name;

                    // Apply filter if requested
                    if (filter != null && filter.Length > 0)
                    {
                        bool found = false;
                        foreach (var f in filter)
                        {
                            if (string.Equals(f, typeName, StringComparison.OrdinalIgnoreCase))
                            {
                                found = true;
                                break;
                            }
                        }
                        if (!found) continue;
                    }

                    var compObj = new JObject();
                    compObj["type"] = typeName;

                    // Fast-path: LocalTransform
                    if (managedType == typeof(LocalTransform) && em.HasComponent<LocalTransform>(entity))
                    {
                        var lt = em.GetComponentData<LocalTransform>(entity);
                        compObj["position"] = new JArray(
                            Math.Round(lt.Position.x, 3),
                            Math.Round(lt.Position.y, 3),
                            Math.Round(lt.Position.z, 3));
                        compObj["rotation"] = new JArray(
                            Math.Round(lt.Rotation.value.x, 4),
                            Math.Round(lt.Rotation.value.y, 4),
                            Math.Round(lt.Rotation.value.z, 4),
                            Math.Round(lt.Rotation.value.w, 4));
                        compObj["scale"] = Math.Round(lt.Scale, 4);
                    }
                    // Fast-path: LocalToWorld
                    else if (managedType == typeof(LocalToWorld) && em.HasComponent<LocalToWorld>(entity))
                    {
                        var ltw = em.GetComponentData<LocalToWorld>(entity);
                        var pos = ltw.Position;
                        compObj["position"] = new JArray(
                            Math.Round(pos.x, 3),
                            Math.Round(pos.y, 3),
                            Math.Round(pos.z, 3));
                        var matrix = ltw.Value;
                        compObj["matrix"] = new JArray(
                            matrix.c0.x, matrix.c0.y, matrix.c0.z, matrix.c0.w,
                            matrix.c1.x, matrix.c1.y, matrix.c1.z, matrix.c1.w,
                            matrix.c2.x, matrix.c2.y, matrix.c2.z, matrix.c2.w,
                            matrix.c3.x, matrix.c3.y, matrix.c3.z, matrix.c3.w);
                    }
                    else
                    {
                        // Reflection-based fallback for public fields on blittable components
                        SerializeComponentViaReflection(em, entity, managedType, componentType, compObj);
                    }

                    result.Add(compObj);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Theatre] EcsHelpers: Could not read component {componentType}: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// Get the world-space position of an entity using LocalTransform (preferred)
        /// or LocalToWorld as fallback. Returns (float3.zero, false) if neither exists.
        /// </summary>
        public static (float3 position, bool found) GetEntityPosition(EntityManager em, Entity entity)
        {
            if (em.HasComponent<LocalTransform>(entity))
            {
                var lt = em.GetComponentData<LocalTransform>(entity);
                return (lt.Position, true);
            }

            if (em.HasComponent<LocalToWorld>(entity))
            {
                var ltw = em.GetComponentData<LocalToWorld>(entity);
                return (ltw.Position, true);
            }

            return (float3.zero, false);
        }

        // --- Private Helpers ---

        private static void SerializeComponentViaReflection(
            EntityManager em, Entity entity, Type managedType,
            ComponentType componentType, JObject target)
        {
            try
            {
                // Only attempt reflection on value types (struct IComponentData)
                if (!managedType.IsValueType) return;

                var fields = managedType.GetFields(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (fields.Length == 0) return;

                // We can't generically call GetComponentData<T> without knowing T at compile time.
                // Use GetDynamicComponentDataArrayReinterpret / GetComponentDataRawRO for raw access.
                // For now, list field names and types as best-effort schema info.
                var fieldsArray = new JArray();
                foreach (var field in fields)
                {
                    var fieldObj = new JObject();
                    fieldObj["name"] = field.Name;
                    fieldObj["field_type"] = field.FieldType.Name;
                    fieldsArray.Add(fieldObj);
                }
                target["fields"] = fieldsArray;
                target["note"] = "field values require generic access — use ecs_inspect with a known type";
            }
            catch (Exception ex)
            {
                target["reflection_error"] = ex.Message;
            }
        }
    }
}
#endif
