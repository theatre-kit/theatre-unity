using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Theatre.Stage;
using Theatre.Editor.Tools.Director;
using UnityEngine;
using UnityEditor;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class DirectorHelpersTests
    {
        [Test]
        public void ResolveComponentType_BoxCollider_ReturnsType()
        {
            var type = DirectorHelpers.ResolveComponentType("BoxCollider", out var error);
            Assert.IsNotNull(type);
            Assert.IsNull(error);
            Assert.AreEqual(typeof(BoxCollider), type);
        }

        [Test]
        public void ResolveComponentType_Unknown_ReturnsNull()
        {
            var type = DirectorHelpers.ResolveComponentType("FakeComponent99", out var error);
            Assert.IsNull(type);
            Assert.IsNotNull(error);
            Assert.That(error, Does.Contain("not found").Or.Contain("type_not_found"));
        }

        [Test]
        public void ValidateAssetPath_Valid_ReturnsNull()
        {
            var error = DirectorHelpers.ValidateAssetPath("Assets/Scenes/Test.unity", ".unity");
            Assert.IsNull(error);
        }

        [Test]
        public void ValidateAssetPath_NoPrefix_ReturnsError()
        {
            var error = DirectorHelpers.ValidateAssetPath("foo/bar.unity", ".unity");
            Assert.IsNotNull(error);
            Assert.That(error, Does.Contain("invalid_parameter").Or.Contain("must start with"));
        }

        [Test]
        public void ValidateAssetPath_WrongExtension_ReturnsError()
        {
            var error = DirectorHelpers.ValidateAssetPath("Assets/foo/bar.asset", ".prefab");
            Assert.IsNotNull(error);
            Assert.That(error, Does.Contain("invalid_parameter").Or.Contain(".prefab"));
        }

        [Test]
        public void ValidateAssetPath_PackagesPrefix_ReturnsNull()
        {
            var error = DirectorHelpers.ValidateAssetPath("Packages/com.foo/Resources/Prefabs/Thing.prefab", ".prefab");
            Assert.IsNull(error);
        }

        [Test]
        public void SetProperties_SetsMultipleValues()
        {
            var go = new GameObject("PropTest");
            var rb = go.AddComponent<Rigidbody>();
            try
            {
                var props = new JObject { ["mass"] = 5.0f, ["is_kinematic"] = true };
                var (count, errors) = DirectorHelpers.SetProperties(rb, props);
                Assert.AreEqual(2, count, $"Expected 2 set but got {count}. Errors: {string.Join(", ", errors)}");
                Assert.AreEqual(0, errors.Count);
                Assert.AreEqual(5.0f, rb.mass, 0.01f);
                Assert.IsTrue(rb.isKinematic);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SetProperties_UnknownProperty_ReturnsError()
        {
            var go = new GameObject("PropErrorTest");
            var rb = go.AddComponent<Rigidbody>();
            try
            {
                var props = new JObject { ["nonexistent_property_xyz"] = 42 };
                var (count, errors) = DirectorHelpers.SetProperties(rb, props);
                Assert.AreEqual(0, count);
                Assert.AreEqual(1, errors.Count);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ToPascalCase_ConvertsCorrectly()
        {
            Assert.AreEqual("IsKinematic", DirectorHelpers.ToPascalCase("is_kinematic"));
            Assert.AreEqual("Mass", DirectorHelpers.ToPascalCase("mass"));
            Assert.AreEqual("MyPropName", DirectorHelpers.ToPascalCase("my_prop_name"));
        }

        [Test]
        public void CheckDryRun_NotSet_ReturnsNull()
        {
            var args = new JObject { ["name"] = "Test" };
            var result = DirectorHelpers.CheckDryRun(args, () => (true, new System.Collections.Generic.List<string>()));
            Assert.IsNull(result);
        }

        [Test]
        public void CheckDryRun_Set_ReturnsResponse()
        {
            var args = new JObject { ["dry_run"] = true };
            var result = DirectorHelpers.CheckDryRun(args, () => (true, new System.Collections.Generic.List<string>()));
            Assert.IsNotNull(result);
            var parsed = JObject.Parse(result);
            Assert.AreEqual(true, (bool)parsed["dry_run"]);
            Assert.AreEqual(true, (bool)parsed["would_succeed"]);
        }
    }

    [TestFixture]
    public class SceneOpTests
    {
        [Test]
        public void CreateGameObject_CreatesAtRoot()
        {
            var result = SceneOpHandlers.CreateGameObject(new JObject { ["name"] = "TestObj_Director" });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("TestObj_Director"));
            // Cleanup
            var go = GameObject.Find("TestObj_Director");
            if (go != null) Object.DestroyImmediate(go);
        }

        [Test]
        public void CreateGameObject_DryRun_DoesNotCreate()
        {
            var result = SceneOpHandlers.CreateGameObject(new JObject
            {
                ["name"] = "DryRunGhost",
                ["dry_run"] = true
            });
            Assert.That(result, Does.Contain("\"dry_run\":true"));
            Assert.IsNull(GameObject.Find("DryRunGhost"));
        }

        [Test]
        public void CreateGameObject_WithPosition_SetsTransform()
        {
            var result = SceneOpHandlers.CreateGameObject(new JObject
            {
                ["name"] = "PosTest_Director",
                ["position"] = new JArray(1.0f, 2.0f, 3.0f)
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            var go = GameObject.Find("PosTest_Director");
            Assert.IsNotNull(go);
            try
            {
                Assert.AreEqual(1.0f, go.transform.localPosition.x, 0.01f);
                Assert.AreEqual(2.0f, go.transform.localPosition.y, 0.01f);
                Assert.AreEqual(3.0f, go.transform.localPosition.z, 0.01f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void CreateGameObject_WithComponent_AddsIt()
        {
            var result = SceneOpHandlers.CreateGameObject(new JObject
            {
                ["name"] = "CompCreate_Director",
                ["components"] = new JArray(
                    new JObject { ["type"] = "BoxCollider" }
                )
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            var go = GameObject.Find("CompCreate_Director");
            Assert.IsNotNull(go);
            try
            {
                Assert.IsNotNull(go.GetComponent<BoxCollider>());
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void DeleteGameObject_RemovesObject()
        {
            var go = new GameObject("DeleteMe_Director");
            Undo.RegisterCreatedObjectUndo(go, "test");
            var result = SceneOpHandlers.DeleteGameObject(
                new JObject { ["path"] = "/DeleteMe_Director" });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.IsNull(GameObject.Find("DeleteMe_Director"));
        }

        [Test]
        public void DeleteGameObject_NotFound_ReturnsError()
        {
            var result = SceneOpHandlers.DeleteGameObject(
                new JObject { ["path"] = "/NonExistentObj_Director_XYZ123" });
            Assert.That(result, Does.Contain("\"error\""));
        }

        [Test]
        public void Reparent_MovesUnderNewParent()
        {
            var parent = new GameObject("ReparentParent_Director");
            var child = new GameObject("ReparentChild_Director");
            try
            {
                var result = SceneOpHandlers.Reparent(new JObject
                {
                    ["path"] = "/ReparentChild_Director",
                    ["new_parent"] = "/ReparentParent_Director"
                });
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
                Assert.AreEqual(parent.transform, child.transform.parent);
            }
            finally
            {
                Object.DestroyImmediate(child);
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Reparent_ToRoot_DetachesParent()
        {
            var parent = new GameObject("ReparentRoot_Parent");
            var child = new GameObject("ReparentRoot_Child");
            child.transform.SetParent(parent.transform);
            try
            {
                var result = SceneOpHandlers.Reparent(new JObject
                {
                    ["path"] = "/ReparentRoot_Parent/ReparentRoot_Child"
                    // no new_parent => move to root
                });
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
                Assert.IsNull(child.transform.parent);
            }
            finally
            {
                Object.DestroyImmediate(child);
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Duplicate_CreatesACopy()
        {
            var original = new GameObject("DupSource_Director");
            try
            {
                var result = SceneOpHandlers.Duplicate(new JObject
                {
                    ["path"] = "/DupSource_Director"
                });
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
                Assert.That(result, Does.Contain("\"results\""));

                // Cleanup copy
                var copy = GameObject.Find("DupSource_Director");
                // There should be 2 objects with this name now (original + copy)
                var all = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                int found = 0;
                GameObject copyGo = null;
                foreach (var g in all)
                {
                    if (g.name == "DupSource_Director") { found++; copyGo = g; }
                }
                Assert.GreaterOrEqual(found, 2);
                if (copyGo != null && copyGo != original)
                    Object.DestroyImmediate(copyGo);
            }
            finally { Object.DestroyImmediate(original); }
        }

        [Test]
        public void SetComponent_AddsAndSetsProperties()
        {
            var go = new GameObject("CompTest_Director");
            try
            {
                var result = SceneOpHandlers.SetComponent(new JObject
                {
                    ["path"] = "/CompTest_Director",
                    ["component"] = "BoxCollider",
                    ["properties"] = new JObject { ["is_trigger"] = true },
                    ["add_if_missing"] = true
                });
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
                var collider = go.GetComponent<BoxCollider>();
                Assert.IsNotNull(collider);
                Assert.IsTrue(collider.isTrigger);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void SetComponent_NotFound_NoAdd_ReturnsError()
        {
            var go = new GameObject("NoAddTest_Director");
            try
            {
                var result = SceneOpHandlers.SetComponent(new JObject
                {
                    ["path"] = "/NoAddTest_Director",
                    ["component"] = "BoxCollider",
                    ["properties"] = new JObject(),
                    ["add_if_missing"] = false
                });
                Assert.That(result, Does.Contain("\"error\""));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void RemoveComponent_Transform_ReturnsError()
        {
            var go = new GameObject("TransformRemoveTest");
            try
            {
                var result = SceneOpHandlers.RemoveComponent(new JObject
                {
                    ["path"] = "/TransformRemoveTest",
                    ["component"] = "Transform"
                });
                Assert.That(result, Does.Contain("\"error\""));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void RemoveComponent_BoxCollider_RemovesIt()
        {
            var go = new GameObject("RemoveComp_Director");
            go.AddComponent<BoxCollider>();
            try
            {
                var result = SceneOpHandlers.RemoveComponent(new JObject
                {
                    ["path"] = "/RemoveComp_Director",
                    ["component"] = "BoxCollider"
                });
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
                Assert.IsNull(go.GetComponent<BoxCollider>());
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void MoveToScene_NotRootObject_ReturnsError()
        {
            var parent = new GameObject("MoveSceneParent_Director");
            var child = new GameObject("MoveSceneChild_Director");
            child.transform.SetParent(parent.transform);
            try
            {
                // Move to active scene by name — this tests the "not root" guard
                var activeSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                var result = SceneOpHandlers.MoveToScene(new JObject
                {
                    ["paths"] = new JArray("/MoveSceneParent_Director/MoveSceneChild_Director"),
                    ["target_scene"] = activeSceneName
                });
                Assert.That(result, Does.Contain("not a root object")
                    .Or.Contain("errors")
                    .Or.Contain("error"));
            }
            finally
            {
                Object.DestroyImmediate(child);
                Object.DestroyImmediate(parent);
            }
        }
    }

    [TestFixture]
    public class PrefabOpTests
    {
        [Test]
        public void ListOverrides_NotPrefab_ReturnsError()
        {
            var go = new GameObject("NotAPrefab_Director");
            try
            {
                var result = PrefabOpHandlers.ListOverrides(new JObject
                {
                    ["instance_path"] = "/NotAPrefab_Director"
                });
                Assert.That(result, Does.Contain("not_prefab_instance"));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void CreatePrefab_MissingSourcePath_ReturnsError()
        {
            var result = PrefabOpHandlers.CreatePrefab(new JObject
            {
                ["asset_path"] = "Assets/Tests/TestPrefab.prefab"
            });
            Assert.That(result, Does.Contain("\"error\""));
        }

        [Test]
        public void CreatePrefab_InvalidAssetPath_ReturnsError()
        {
            var go = new GameObject("PrefabSource_Director");
            try
            {
                var result = PrefabOpHandlers.CreatePrefab(new JObject
                {
                    ["source_path"] = "/PrefabSource_Director",
                    ["asset_path"] = "NotAssets/bad/path.prefab"
                });
                Assert.That(result, Does.Contain("\"error\""));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Instantiate_MissingPrefabPath_ReturnsError()
        {
            var result = PrefabOpHandlers.Instantiate(new JObject());
            Assert.That(result, Does.Contain("\"error\""));
        }

        [Test]
        public void Instantiate_NonExistentPrefab_ReturnsError()
        {
            var result = PrefabOpHandlers.Instantiate(new JObject
            {
                ["prefab_path"] = "Assets/DoesNotExist_XYZABC123.prefab"
            });
            Assert.That(result, Does.Contain("prefab_not_found"));
        }

        [Test]
        public void ApplyOverrides_NotPrefabInstance_ReturnsError()
        {
            var go = new GameObject("NotPrefab_Apply_Director");
            try
            {
                var result = PrefabOpHandlers.ApplyOverrides(new JObject
                {
                    ["instance_path"] = "/NotPrefab_Apply_Director"
                });
                Assert.That(result, Does.Contain("not_prefab_instance"));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void RevertOverrides_NotPrefabInstance_ReturnsError()
        {
            var go = new GameObject("NotPrefab_Revert_Director");
            try
            {
                var result = PrefabOpHandlers.RevertOverrides(new JObject
                {
                    ["instance_path"] = "/NotPrefab_Revert_Director"
                });
                Assert.That(result, Does.Contain("not_prefab_instance"));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Unpack_NotPrefabInstance_ReturnsError()
        {
            var go = new GameObject("NotPrefab_Unpack_Director");
            try
            {
                var result = PrefabOpHandlers.Unpack(new JObject
                {
                    ["instance_path"] = "/NotPrefab_Unpack_Director"
                });
                Assert.That(result, Does.Contain("not_prefab_instance"));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void CreateVariant_MissingBasePrefab_ReturnsError()
        {
            var result = PrefabOpHandlers.CreateVariant(new JObject
            {
                ["asset_path"] = "Assets/Tests/Variant.prefab"
            });
            Assert.That(result, Does.Contain("\"error\""));
        }

        [Test]
        public void CreateVariant_NonExistentBase_ReturnsError()
        {
            var result = PrefabOpHandlers.CreateVariant(new JObject
            {
                ["base_prefab"] = "Assets/DoesNotExist_XYZABC123.prefab",
                ["asset_path"] = "Assets/Tests/Variant.prefab"
            });
            Assert.That(result, Does.Contain("prefab_not_found"));
        }
    }
}
