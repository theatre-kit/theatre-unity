#if THEATRE_HAS_ENTITIES
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Theatre.Editor.Tools.ECS;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class EcsHelperTests
    {
        private World _testWorld;

        [SetUp]
        public void SetUp()
        {
            _testWorld = new World("TheatreTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            _testWorld?.Dispose();
            _testWorld = null;
        }

        [Test]
        public void ResolveWorld_ByName_FindsWorld()
        {
            var world = EcsHelpers.ResolveWorld("TheatreTestWorld", out var error);
            Assert.IsNotNull(world);
            Assert.IsNull(error);
            Assert.AreEqual("TheatreTestWorld", world.Name);
        }

        [Test]
        public void ResolveWorld_Unknown_ReturnsError()
        {
            var world = EcsHelpers.ResolveWorld("NonExistentWorld999", out var error);
            Assert.IsNull(world);
            Assert.IsNotNull(error);
            StringAssert.Contains("NonExistentWorld999", error);
        }

        [Test]
        public void ResolveEntity_ValidEntity_Resolves()
        {
            var em = _testWorld.EntityManager;
            var created = em.CreateEntity();
            var resolved = EcsHelpers.ResolveEntity(_testWorld, created.Index, created.Version, out var error);
            Assert.AreEqual(Entity.Null, error == null ? Entity.Null : Entity.Null); // null error expected
            Assert.IsNull(error);
            Assert.AreEqual(created.Index, resolved.Index);
            Assert.AreEqual(created.Version, resolved.Version);
        }

        [Test]
        public void ResolveEntity_InvalidEntity_ReturnsError()
        {
            var resolved = EcsHelpers.ResolveEntity(_testWorld, 99999, 0, out var error);
            Assert.AreEqual(Entity.Null, resolved);
            Assert.IsNotNull(error);
        }

        [Test]
        public void GetEntityPosition_WithLocalTransform_ReturnsPosition()
        {
            var em = _testWorld.EntityManager;
            var entity = em.CreateEntity(typeof(LocalTransform));
            var lt = em.GetComponentData<LocalTransform>(entity);
            lt.Position = new float3(1f, 2f, 3f);
            em.SetComponentData(entity, lt);

            var (pos, found) = EcsHelpers.GetEntityPosition(em, entity);
            Assert.IsTrue(found);
            Assert.AreEqual(1f, pos.x, 0.001f);
            Assert.AreEqual(2f, pos.y, 0.001f);
            Assert.AreEqual(3f, pos.z, 0.001f);
        }

        [Test]
        public void GetEntityPosition_NoTransform_ReturnsFalse()
        {
            var em = _testWorld.EntityManager;
            var entity = em.CreateEntity(); // no LocalTransform

            var (_, found) = EcsHelpers.GetEntityPosition(em, entity);
            Assert.IsFalse(found);
        }

        [Test]
        public void ReadEntityComponents_WithLocalTransform_ReturnsData()
        {
            var em = _testWorld.EntityManager;
            var entity = em.CreateEntity(typeof(LocalTransform));
            var lt = em.GetComponentData<LocalTransform>(entity);
            lt.Position = new float3(5f, 6f, 7f);
            em.SetComponentData(entity, lt);

            var components = EcsHelpers.ReadEntityComponents(em, entity);
            Assert.IsNotNull(components);
            Assert.Greater(components.Count, 0);

            // Find LocalTransform component
            JObject ltComp = null;
            foreach (var c in components)
            {
                if (c["type"]?.Value<string>() == "LocalTransform")
                {
                    ltComp = c as JObject;
                    break;
                }
            }
            Assert.IsNotNull(ltComp, "LocalTransform component not found in results");
            var pos = ltComp["position"] as JArray;
            Assert.IsNotNull(pos);
            Assert.AreEqual(5f, pos[0].Value<float>(), 0.01f);
            Assert.AreEqual(6f, pos[1].Value<float>(), 0.01f);
            Assert.AreEqual(7f, pos[2].Value<float>(), 0.01f);
        }
    }

    [TestFixture]
    public class EcsWorldToolTests
    {
        private World _testWorld;

        [SetUp]
        public void SetUp()
        {
            _testWorld = new World("TheatreEcsTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            _testWorld?.Dispose();
            _testWorld = null;
        }

        [Test]
        public void ListWorlds_ReturnsAtLeastTestWorld()
        {
            var result = EcsWorldTool.ListWorlds(new JObject());
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("TheatreEcsTestWorld"));
        }

        [Test]
        public void ListWorlds_IncludesEntityCount()
        {
            var em = _testWorld.EntityManager;
            em.CreateEntity();
            em.CreateEntity();

            var result = EcsWorldTool.ListWorlds(new JObject());
            var json = JObject.Parse(result);
            Assert.AreEqual("ok", json["result"]?.Value<string>());
            Assert.IsNotNull(json["worlds"]);
        }

        [Test]
        public void WorldSummary_ValidWorld_ReturnsData()
        {
            var result = EcsWorldTool.WorldSummary(new JObject
            {
                ["world"] = "TheatreEcsTestWorld"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("TheatreEcsTestWorld"));
            Assert.That(result, Does.Contain("entity_count"));
        }

        [Test]
        public void WorldSummary_UnknownWorld_ReturnsError()
        {
            var result = EcsWorldTool.WorldSummary(new JObject
            {
                ["world"] = "NoSuchWorld999"
            });
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void ListSystems_ValidWorld_ReturnsResponse()
        {
            var result = EcsWorldTool.ListSystems(new JObject
            {
                ["world"] = "TheatreEcsTestWorld"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("systems"));
        }

        [Test]
        public void ListArchetypes_ValidWorld_ReturnsResponse()
        {
            var em = _testWorld.EntityManager;
            em.CreateEntity(typeof(LocalTransform));

            var result = EcsWorldTool.ListArchetypes(new JObject
            {
                ["world"] = "TheatreEcsTestWorld"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("archetypes"));
        }
    }

    [TestFixture]
    public class EcsActionToolTests
    {
        private World _testWorld;

        [SetUp]
        public void SetUp()
        {
            _testWorld = new World("TheatreActionTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            _testWorld?.Dispose();
            _testWorld = null;
        }

        [Test]
        public void CreateEntity_CreatesInWorld()
        {
            var result = EcsActionTool.CreateEntity(new JObject
            {
                ["world"] = "TheatreActionTestWorld"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("\"index\""));
            Assert.That(result, Does.Contain("\"version\""));
        }

        [Test]
        public void CreateEntity_WithComponents_CreatesArchetype()
        {
            var result = EcsActionTool.CreateEntity(new JObject
            {
                ["world"] = "TheatreActionTestWorld",
                ["components"] = new JArray("LocalTransform")
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));

            var json = JObject.Parse(result);
            var entityObj = json["entity"] as JObject;
            Assert.IsNotNull(entityObj);
            Assert.IsNotNull(entityObj["index"]);

            int index = entityObj["index"].Value<int>();
            int version = entityObj["version"].Value<int>();
            var em = _testWorld.EntityManager;
            var entity = new Entity { Index = index, Version = version };
            Assert.IsTrue(em.Exists(entity));
            Assert.IsTrue(em.HasComponent<LocalTransform>(entity));
        }

        [Test]
        public void DestroyEntity_RemovesEntity()
        {
            var em = _testWorld.EntityManager;
            var entity = em.CreateEntity();

            var result = EcsActionTool.DestroyEntity(new JObject
            {
                ["entity_index"] = entity.Index,
                ["entity_version"] = entity.Version,
                ["world"] = "TheatreActionTestWorld"
            });

            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.IsFalse(em.Exists(entity));
        }

        [Test]
        public void DestroyEntity_InvalidEntity_ReturnsError()
        {
            var result = EcsActionTool.DestroyEntity(new JObject
            {
                ["entity_index"] = 99999,
                ["entity_version"] = 0,
                ["world"] = "TheatreActionTestWorld"
            });
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void AddComponent_AddsToEntity()
        {
            var em = _testWorld.EntityManager;
            var entity = em.CreateEntity();

            var result = EcsActionTool.AddComponent(new JObject
            {
                ["entity_index"] = entity.Index,
                ["entity_version"] = entity.Version,
                ["component"] = "LocalTransform",
                ["world"] = "TheatreActionTestWorld"
            });

            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.IsTrue(em.HasComponent<LocalTransform>(entity));
        }

        [Test]
        public void RemoveComponent_RemovesFromEntity()
        {
            var em = _testWorld.EntityManager;
            var entity = em.CreateEntity(typeof(LocalTransform));
            Assert.IsTrue(em.HasComponent<LocalTransform>(entity));

            var result = EcsActionTool.RemoveComponent(new JObject
            {
                ["entity_index"] = entity.Index,
                ["entity_version"] = entity.Version,
                ["component"] = "LocalTransform",
                ["world"] = "TheatreActionTestWorld"
            });

            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.IsFalse(em.HasComponent<LocalTransform>(entity));
        }

        [Test]
        public void SetComponent_LocalTransform_UpdatesPosition()
        {
            var em = _testWorld.EntityManager;
            var entity = em.CreateEntity(typeof(LocalTransform));

            var result = EcsActionTool.SetComponent(new JObject
            {
                ["entity_index"] = entity.Index,
                ["entity_version"] = entity.Version,
                ["component"] = "LocalTransform",
                ["values"] = new JObject
                {
                    ["position"] = new JArray(10f, 20f, 30f)
                },
                ["world"] = "TheatreActionTestWorld"
            });

            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            var lt = em.GetComponentData<LocalTransform>(entity);
            Assert.AreEqual(10f, lt.Position.x, 0.001f);
            Assert.AreEqual(20f, lt.Position.y, 0.001f);
            Assert.AreEqual(30f, lt.Position.z, 0.001f);
        }
    }

    [TestFixture]
    public class EcsSnapshotToolTests
    {
        private World _testWorld;

        [SetUp]
        public void SetUp()
        {
            _testWorld = new World("TheatreSnapshotTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            _testWorld?.Dispose();
            _testWorld = null;
        }

        [Test]
        public void Snapshot_EmptyWorld_ReturnsOk()
        {
            var result = EcsSnapshotTool.Snapshot(new JObject
            {
                ["world"] = "TheatreSnapshotTestWorld"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("entities"));
        }

        [Test]
        public void Snapshot_WithEntities_ReturnsEntities()
        {
            var em = _testWorld.EntityManager;
            em.CreateEntity(typeof(LocalTransform));
            em.CreateEntity(typeof(LocalTransform));

            var result = EcsSnapshotTool.Snapshot(new JObject
            {
                ["world"] = "TheatreSnapshotTestWorld"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));

            var json = JObject.Parse(result);
            var entities = json["entities"] as JArray;
            Assert.IsNotNull(entities);
            Assert.GreaterOrEqual(entities.Count, 2);
        }

        [Test]
        public void Snapshot_WithRadius_FiltersEntities()
        {
            var em = _testWorld.EntityManager;

            // Near entity
            var near = em.CreateEntity(typeof(LocalTransform));
            var lt = em.GetComponentData<LocalTransform>(near);
            lt.Position = new float3(1f, 0f, 0f);
            em.SetComponentData(near, lt);

            // Far entity
            var far = em.CreateEntity(typeof(LocalTransform));
            lt = em.GetComponentData<LocalTransform>(far);
            lt.Position = new float3(1000f, 0f, 0f);
            em.SetComponentData(far, lt);

            var result = EcsSnapshotTool.Snapshot(new JObject
            {
                ["world"] = "TheatreSnapshotTestWorld",
                ["focus"] = new JArray(0f, 0f, 0f),
                ["radius"] = 10f
            });

            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            var json = JObject.Parse(result);
            var entities = json["entities"] as JArray;
            Assert.IsNotNull(entities);
            // Should only contain the near entity
            Assert.AreEqual(1, entities.Count);
        }
    }

    [TestFixture]
    public class EcsInspectToolTests
    {
        private World _testWorld;

        [SetUp]
        public void SetUp()
        {
            _testWorld = new World("TheatreInspectTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            _testWorld?.Dispose();
            _testWorld = null;
        }

        [Test]
        public void Inspect_ValidEntity_ReturnsComponents()
        {
            var em = _testWorld.EntityManager;
            var entity = em.CreateEntity(typeof(LocalTransform));

            var result = EcsInspectTool.Inspect(new JObject
            {
                ["entity_index"] = entity.Index,
                ["entity_version"] = entity.Version,
                ["world"] = "TheatreInspectTestWorld"
            });

            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("components"));
        }

        [Test]
        public void Inspect_InvalidEntity_ReturnsError()
        {
            var result = EcsInspectTool.Inspect(new JObject
            {
                ["entity_index"] = 99999,
                ["entity_version"] = 0,
                ["world"] = "TheatreInspectTestWorld"
            });
            Assert.That(result, Does.Contain("error"));
        }
    }

    [TestFixture]
    public class EcsQueryToolTests
    {
        private World _testWorld;

        [SetUp]
        public void SetUp()
        {
            _testWorld = new World("TheatreQueryTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            _testWorld?.Dispose();
            _testWorld = null;
        }

        private void CreateEntityAt(float x, float y, float z)
        {
            var em = _testWorld.EntityManager;
            var entity = em.CreateEntity(typeof(LocalTransform));
            var lt = em.GetComponentData<LocalTransform>(entity);
            lt.Position = new float3(x, y, z);
            em.SetComponentData(entity, lt);
        }

        [Test]
        public void Nearest_FindsClosestEntities()
        {
            CreateEntityAt(1f, 0f, 0f);
            CreateEntityAt(5f, 0f, 0f);
            CreateEntityAt(10f, 0f, 0f);

            var result = EcsQueryTool.Nearest(new JObject
            {
                ["origin"] = new JArray(0f, 0f, 0f),
                ["count"] = 2,
                ["world"] = "TheatreQueryTestWorld"
            });

            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            var json = JObject.Parse(result);
            var results = json["results"] as JArray;
            Assert.IsNotNull(results);
            Assert.AreEqual(2, results.Count);

            // Closest entity should be at distance ~1
            float d0 = results[0]["distance"].Value<float>();
            float d1 = results[1]["distance"].Value<float>();
            Assert.Less(d0, d1, "Results should be sorted by distance");
        }

        [Test]
        public void Radius_ReturnsEntitiesWithinRadius()
        {
            CreateEntityAt(2f, 0f, 0f);
            CreateEntityAt(20f, 0f, 0f);

            var result = EcsQueryTool.Radius(new JObject
            {
                ["origin"] = new JArray(0f, 0f, 0f),
                ["radius"] = 5f,
                ["world"] = "TheatreQueryTestWorld"
            });

            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            var json = JObject.Parse(result);
            var results = json["results"] as JArray;
            Assert.IsNotNull(results);
            Assert.AreEqual(1, results.Count, "Only the entity at distance 2 should be included");
        }

        [Test]
        public void Overlap_ReturnsEntitiesInsideAABB()
        {
            CreateEntityAt(0f, 0f, 0f);
            CreateEntityAt(100f, 100f, 100f);

            var result = EcsQueryTool.Overlap(new JObject
            {
                ["min"] = new JArray(-5f, -5f, -5f),
                ["max"] = new JArray(5f, 5f, 5f),
                ["world"] = "TheatreQueryTestWorld"
            });

            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            var json = JObject.Parse(result);
            var results = json["results"] as JArray;
            Assert.IsNotNull(results);
            Assert.AreEqual(1, results.Count, "Only origin entity should be inside AABB");
        }

        [Test]
        public void Nearest_MissingOrigin_ReturnsError()
        {
            var result = EcsQueryTool.Nearest(new JObject
            {
                ["count"] = 5,
                ["world"] = "TheatreQueryTestWorld"
            });
            Assert.That(result, Does.Contain("error"));
        }
    }
}
#endif
