# Design: Phase 11 — ECS (DOTS/Entities)

## Overview

Full ECS support — parallel tool set to the GameObject Stage tools.
Five tools for world awareness, entity inspection, spatial queries, and
entity mutation. All guarded by `#if THEATRE_HAS_ENTITIES` since the
`com.unity.entities` package is optional.

**Tools**:
- `ecs_world` (4 ops) — list_worlds, world_summary, list_archetypes, list_systems
- `ecs_snapshot` (1 op) — spatial overview of entities, budgeted
- `ecs_inspect` (1 op) — deep inspection of a single entity's components
- `ecs_query` (3 ops) — nearest, radius, overlap (AABB) on entities with LocalTransform
- `ecs_action` (5 ops) — set_component, add_component, remove_component, destroy_entity, create_entity

All tools live in `Editor/Tools/ECS/` (new subdirectory) under their
respective `ToolGroup.ECS*` groups.

---

## Architecture

```
Editor/Tools/ECS/
  EcsWorldTool.cs      — MCP tool: ecs_world (4 operations)
  EcsSnapshotTool.cs   — MCP tool: ecs_snapshot (1 operation)
  EcsInspectTool.cs    — MCP tool: ecs_inspect (1 operation)
  EcsQueryTool.cs      — MCP tool: ecs_query (3 operations)
  EcsActionTool.cs     — MCP tool: ecs_action (5 operations)
  EcsHelpers.cs        — Shared: world resolution, entity resolution, component type lookup
```

**All files wrapped in `#if THEATRE_HAS_ENTITIES`**.

**Namespace**: `Theatre.Editor.Tools.ECS`

---

## Implementation Units

### Unit 1: Asmdef — Entities Version Define

**Editor asmdef**: Add `versionDefines` for `com.unity.entities`:
```json
{
    "name": "com.unity.entities",
    "expression": "",
    "define": "THEATRE_HAS_ENTITIES"
}
```
Add to `references`: `"Unity.Entities"`, `"Unity.Transforms"`, `"Unity.Collections"`

**Test asmdef**: same additions.

---

### Unit 2: EcsHelpers — Shared Utilities

**File**: `Packages/com.theatre.toolkit/Editor/Tools/ECS/EcsHelpers.cs`

```csharp
#if THEATRE_HAS_ENTITIES
using Unity.Entities;
using Unity.Transforms;

namespace Theatre.Editor.Tools.ECS
{
    internal static class EcsHelpers
    {
        /// <summary>Find a World by name. Default: "Default World".</summary>
        public static World ResolveWorld(string worldName, out string error);

        /// <summary>Find an entity by index + version in a world.</summary>
        public static (Entity entity, bool found) ResolveEntity(
            World world, int index, int version, out string error);

        /// <summary>Resolve a component type by name from TypeManager.</summary>
        public static (ComponentType type, bool found) ResolveComponentType(
            string typeName, out string error);

        /// <summary>Read all component data from an entity as JObject.</summary>
        public static JObject ReadEntityComponents(
            EntityManager em, Entity entity, string[] filter = null);

        /// <summary>Get position from LocalTransform or LocalToWorld.</summary>
        public static (float3 position, bool found) GetEntityPosition(
            EntityManager em, Entity entity);
    }
}
#endif
```

**Implementation Notes**:
- `ResolveWorld`: Iterate `World.All`, find by name. Default to `World.DefaultGameObjectInjectionWorld`.
- `ResolveEntity`: `new Entity { Index = index, Version = version }`, verify exists via `em.Exists(entity)`.
- `ResolveComponentType`: Use `TypeManager.GetTypeIndex(componentType)` or search all types for name match. Types are unmanaged structs — read via `EntityManager.GetComponentDataRaw` for unknown types.
- `ReadEntityComponents`: `em.GetComponentTypes(entity)` returns `NativeArray<ComponentType>`. For each, read data and convert to JObject. Known types (LocalTransform, LocalToWorld) get fast-path serialization. Unknown types use `TypeManager.GetTypeInfo` + unsafe raw data reading.
- `GetEntityPosition`: Check `LocalTransform` first (newer API), fallback to `LocalToWorld.Position`.

### Unknown Component Data Reading (EcsHelpers.ReadEntityComponents)

For components whose types are known at compile time (LocalTransform,
LocalToWorld, etc.), use direct typed access:

```csharp
if (em.HasComponent<LocalTransform>(entity))
{
    var lt = em.GetComponentData<LocalTransform>(entity);
    componentObj["position"] = ResponseHelpers.ToJArray(
        new Vector3(lt.Position.x, lt.Position.y, lt.Position.z));
    componentObj["rotation"] = ResponseHelpers.QuaternionToJArray(lt.Rotation);
    componentObj["scale"] = lt.Scale;
}
```

For unknown component types, use TypeManager + unsafe reads:

```csharp
// 1. Get all component types on the entity
var componentTypes = em.GetComponentTypes(entity);

// 2. For each type, get type info from TypeManager
foreach (var ct in componentTypes)
{
    var typeIndex = ct.TypeIndex;
    var typeInfo = TypeManager.GetTypeInfo(typeIndex);
    var typeName = typeInfo.DebugTypeName.ToString();

    // Skip zero-size (tag) components — they have no data
    if (typeInfo.IsZeroSized)
    {
        result[typeName] = new JObject { ["_tag"] = true };
        continue;
    }

    // Skip buffer, shared, managed components — too complex for MVP
    if (typeInfo.Category != TypeManager.TypeCategory.ComponentData)
    {
        result[typeName] = new JObject { ["_category"] = typeInfo.Category.ToString() };
        continue;
    }

    // 3. Read raw bytes via EntityManager
    // GetComponentDataRawRO returns a void* to the component data
    unsafe
    {
        var ptr = em.GetComponentDataRawRO(entity, typeIndex);
        var size = typeInfo.TypeSize;

        // 4. Use reflection on the managed Type to read fields
        var managedType = typeInfo.Type;
        if (managedType != null)
        {
            var fields = managedType.GetFields(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance);
            var obj = new JObject();
            foreach (var field in fields)
            {
                // Marshal field value from raw pointer
                var offset = System.Runtime.InteropServices.Marshal
                    .OffsetOf(managedType, field.Name).ToInt32();
                obj[ToSnakeCase(field.Name)] = ReadFieldValue(
                    (byte*)ptr + offset, field.FieldType);
            }
            result[typeName] = obj;
        }
        else
        {
            result[typeName] = new JObject { ["_raw_size"] = size };
        }
    }
}
```

**ReadFieldValue helper** handles: `int`, `float`, `bool`, `float3`,
`float4`, `quaternion`, `Entity` (serialize as `{index, version}`).
Unknown field types are reported as `"<TypeName>"` string.

**Safety**: This requires `allowUnsafeCode: true` in the editor asmdef.
Currently `false` — needs to be set to `true` for Phase 11.

**Limitations**:
- Managed components (class-based) are not readable via raw pointer
- Buffer components (DynamicBuffer) return count only, not contents
- Shared components return the shared value index, not the value
- Field offset via `Marshal.OffsetOf` may not match actual layout
  for all blittable structs — verify on key ECS types and add known-
  type fast paths for common Unity.Transforms and Unity.Physics types

---

### Unit 3: EcsWorldTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/ECS/EcsWorldTool.cs`

**Registration**: name `"ecs_world"`, group `ToolGroup.ECSWorld`.

4 operations:

#### `list_worlds`
- Iterate `World.All`, return name + entity count + system count
- Response: `{ "result": "ok", "worlds": [{ "name": "...", "entity_count": N, "system_count": N }] }`

#### `world_summary`
- Required: `world` (name, default "Default World")
- Get EntityManager, count entities, list archetype breakdown
- `em.GetAllArchetypes(archetypes)` — NativeList of archetype info
- For each: component type list + entity count
- Budget-limited response

#### `list_archetypes`
- Required: `world`
- Get all archetypes with component sets and entity counts
- More detailed than world_summary — includes all component names per archetype

#### `list_systems`
- Required: `world`
- Iterate `world.Systems` — name, type, enabled state, group
- Response with system execution order

---

### Unit 4: EcsSnapshotTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/ECS/EcsSnapshotTool.cs`

**Registration**: name `"ecs_snapshot"`, group `ToolGroup.ECSWorld`.

Single operation — spatial overview of entities.

Parameters: `world`, `required_components` (string[]), `focus` (Vector3), `radius` (float), `budget`

- Build an `EntityQuery` from `required_components`
- Iterate matching entities
- If `focus`/`radius`: filter by distance from focus using `GetEntityPosition`
- Budget-limited: use `TokenBudget` pattern
- Response with entity array: `{ entity: {index, version}, archetype: [...], position: [x,y,z], distance: N }`

---

### Unit 5: EcsInspectTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/ECS/EcsInspectTool.cs`

**Registration**: name `"ecs_inspect"`, group `ToolGroup.ECSEntity`.

Single operation — deep inspect one entity.

Parameters: `entity_index`, `entity_version`, `world`, `components` (filter)

- Resolve entity via `EcsHelpers.ResolveEntity`
- Read all component data via `EcsHelpers.ReadEntityComponents`
- Budget-limited
- Response with entity identity + component data array

---

### Unit 6: EcsQueryTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/ECS/EcsQueryTool.cs`

**Registration**: name `"ecs_query"`, group `ToolGroup.ECSQuery`.

3 operations: `nearest`, `radius`, `overlap`

#### `nearest`
- Required: `origin` ([x,y,z]), `count` (int)
- Optional: `world`, `required_components`, `max_distance`, `budget`
- Query all entities with `LocalTransform`, compute distances, sort, take top N

#### `radius`
- Required: `origin`, `radius`
- Similar to nearest but filter by distance <= radius

#### `overlap` (AABB)
- Required: `min` ([x,y,z]), `max` ([x,y,z])
- Filter entities whose position is within the AABB
- No physics — pure position-based AABB check

**Note**: Physics-based queries (raycast, overlap sphere) require Unity Physics
package which may not be installed. These 3 operations work with plain
`LocalTransform` positions — no physics dependency.

### Raycast Support (Deferred)

ROADMAP Phase 11 lists `ecs_query` with raycast support. This is
**deferred to a future phase** because:

1. ECS raycasting requires `Unity.Physics` or `Havok.Physics` — these
   are separate packages from `com.unity.entities`
2. Most ECS projects don't have Unity Physics installed (many use
   custom physics or DOTS physics alternatives)
3. The three index-based queries (nearest, radius, overlap) cover the
   majority of spatial debugging use cases

**When Unity Physics is installed**: A future phase will add:
- `ecs_query:raycast` — single/multi-hit against physics world
- `ecs_query:linecast` — line-of-sight check

These will be guarded by `#if THEATRE_HAS_UNITY_PHYSICS` with a
separate `versionDefines` entry for `com.unity.physics`.

**When Unity Physics is NOT installed**: Calling raycast/linecast
returns:
```json
{
    "error": {
        "code": "package_not_installed",
        "message": "ECS raycast requires com.unity.physics package",
        "suggestion": "Install com.unity.physics via Package Manager, or use ecs_query:nearest as an alternative"
    }
}
```

---

### Unit 7: EcsActionTool

**File**: `Packages/com.theatre.toolkit/Editor/Tools/ECS/EcsActionTool.cs`

**Registration**: name `"ecs_action"`, group `ToolGroup.ECSAction`.

5 operations:

#### `set_component`
- Required: `entity_index`, `entity_version`, `component` (type name), `values` (JObject)
- Resolve entity and component type
- For known types (LocalTransform): set fields directly
- For unknown types: use `EntityManager.SetComponentData` with raw byte manipulation via TypeManager

#### `add_component`
- Required: `entity_index`, `entity_version`, `component`
- `em.AddComponent(entity, componentType)`

#### `remove_component`
- Required: `entity_index`, `entity_version`, `component`
- `em.RemoveComponent(entity, componentType)`

#### `destroy_entity`
- Required: `entity_index`, `entity_version`
- `em.DestroyEntity(entity)`

#### `create_entity`
- Optional: `components` (string array of component type names), `world`
- Build archetype from component types
- `em.CreateEntity(archetype)`
- Response with new entity index + version

**Play Mode Note**: ECS mutations work in both Edit and Play mode
(unlike physics queries which need Play). The EntityManager is available
whenever a World exists.

---

### Unit 8: Server Integration

```csharp
#if THEATRE_HAS_ENTITIES
using Theatre.Editor.Tools.ECS;
#endif

// In RegisterBuiltInTools:
#if THEATRE_HAS_ENTITIES
            EcsWorldTool.Register(registry);        // Phase 11
            EcsSnapshotTool.Register(registry);     // Phase 11
            EcsInspectTool.Register(registry);      // Phase 11
            EcsQueryTool.Register(registry);        // Phase 11
            EcsActionTool.Register(registry);       // Phase 11
#endif
```

---

## Implementation Order

```
Unit 1: Asmdef updates
Unit 2: EcsHelpers (shared utilities — everything depends on this)
  └─ Unit 3: EcsWorldTool
  └─ Unit 4: EcsSnapshotTool
  └─ Unit 5: EcsInspectTool
  └─ Unit 6: EcsQueryTool
  └─ Unit 7: EcsActionTool
     └─ Unit 8: Server Integration
```

---

## Testing

### Tests: `Tests/Editor/EcsToolTests.cs`

All tests wrapped in `#if THEATRE_HAS_ENTITIES`.

```csharp
#if THEATRE_HAS_ENTITIES
using Unity.Entities;
using Unity.Transforms;

[TestFixture]
public class EcsHelperTests
{
    [Test] public void ResolveWorld_Default_ReturnsWorld() { }
    [Test] public void ResolveWorld_Unknown_ReturnsError() { }
    [Test] public void ResolveEntity_Invalid_ReturnsError() { }
}

[TestFixture]
public class EcsWorldToolTests
{
    [Test] public void ListWorlds_ReturnsAtLeastOne() { }
    [Test] public void WorldSummary_ReturnsEntityCount() { }
    [Test] public void ListSystems_ReturnsSystems() { }
}

[TestFixture]
public class EcsActionToolTests
{
    [Test] public void CreateEntity_CreatesWithArchetype() { }
    [Test] public void DestroyEntity_RemovesEntity() { }
    [Test] public void CreateEntity_MissingWorld_ReturnsError() { }
}
#endif
```

**Test Note**: ECS tests require a World to exist. In EditMode tests,
`World.DefaultGameObjectInjectionWorld` may be null. Tests should create
a temporary World in `[SetUp]` and dispose it in `[TearDown]`:
```csharp
private World _testWorld;
[SetUp] public void SetUp()
{
    _testWorld = new World("TestWorld");
}
[TearDown] public void TearDown()
{
    _testWorld?.Dispose();
}
```

---

## Verification Checklist

1. `unity_console {"operation": "refresh"}` — recompile
2. `unity_console {"filter": "error"}` — no compile errors
3. `unity_tests {"operation": "run"}` — all tests pass
4. Tool hidden when com.unity.entities not installed
5. Manual: `ecs_world` list_worlds → verify worlds exist
6. Manual: `ecs_action` create_entity → `ecs_inspect` → verify
