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
    public class BatchToolTests
    {
        private ToolGroup _originalGroups;

        [SetUp]
        public void SetUp()
        {
            _originalGroups = TheatreConfig.EnabledGroups;
            TheatreConfig.EnabledGroups = ToolGroup.Everything;
        }

        [TearDown]
        public void TearDown()
        {
            TheatreConfig.EnabledGroups = _originalGroups;
        }

        [Test]
        public void Batch_MultipleOps_ExecutesAll()
        {
            // Create 2 GameObjects via batch
            var ops = new JArray {
                new JObject { ["tool"] = "scene_op", ["params"] = new JObject
                    { ["operation"] = "create_gameobject", ["name"] = "BatchObj1" } },
                new JObject { ["tool"] = "scene_op", ["params"] = new JObject
                    { ["operation"] = "create_gameobject", ["name"] = "BatchObj2" } }
            };
            var result = BatchTool.Execute(new JObject { ["operations"] = ops });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("\"operation_count\":2"));
            // Verify objects exist
            Assert.IsNotNull(GameObject.Find("BatchObj1"));
            Assert.IsNotNull(GameObject.Find("BatchObj2"));
            // Cleanup
            var go1 = GameObject.Find("BatchObj1");
            var go2 = GameObject.Find("BatchObj2");
            if (go1) Object.DestroyImmediate(go1);
            if (go2) Object.DestroyImmediate(go2);
        }

        [Test]
        public void Batch_FailedOp_RollsBackPreceding()
        {
            var ops = new JArray {
                new JObject { ["tool"] = "scene_op", ["params"] = new JObject
                    { ["operation"] = "create_gameobject", ["name"] = "RollbackTest_Batch" } },
                new JObject { ["tool"] = "scene_op", ["params"] = new JObject
                    { ["operation"] = "delete_gameobject", ["path"] = "/NonExistent_BatchRollback" } }
            };
            var result = BatchTool.Execute(new JObject { ["operations"] = ops });
            Assert.That(result, Does.Contain("\"result\":\"error\""));
            Assert.That(result, Does.Contain("\"failed_index\":1"));
            // First op should have been rolled back
            Assert.IsNull(GameObject.Find("RollbackTest_Batch"));
        }

        [Test]
        public void Batch_DryRun_DoesNotMutate()
        {
            var ops = new JArray {
                new JObject { ["tool"] = "scene_op", ["params"] = new JObject
                    { ["operation"] = "create_gameobject", ["name"] = "DryRunBatchGhost" } }
            };
            var result = BatchTool.Execute(new JObject { ["operations"] = ops, ["dry_run"] = true });
            Assert.That(result, Does.Contain("\"dry_run\":true"));
            Assert.IsNull(GameObject.Find("DryRunBatchGhost"));
        }

        [Test]
        public void Batch_EmptyOps_ReturnsError()
        {
            var result = BatchTool.Execute(new JObject { ["operations"] = new JArray() });
            Assert.That(result, Does.Contain("error"));
        }

        [Test]
        public void Batch_InvalidToolName_ReturnsErrorAtIndex()
        {
            var ops = new JArray {
                new JObject { ["tool"] = "nonexistent_tool_xyz",
                    ["params"] = new JObject { ["foo"] = "bar" } }
            };
            var result = BatchTool.Execute(new JObject { ["operations"] = ops });
            Assert.That(result, Does.Contain("\"failed_index\":0"));
        }
    }

    [TestFixture]
    public class PrefabOpTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = "Assets/_TheatreTest_Prefab";
            if (!AssetDatabase.IsValidFolder(_tempDir))
                AssetDatabase.CreateFolder("Assets", "_TheatreTest_Prefab");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_tempDir);
        }

        [Test]
        public void CreatePrefab_SavesAsset()
        {
            var go = new GameObject("PrefabSource_Test");
            try
            {
                var prefabPath = _tempDir + "/TestPrefab.prefab";
                var result = PrefabOpHandlers.CreatePrefab(new JObject
                {
                    ["source_path"] = "/PrefabSource_Test",
                    ["asset_path"] = prefabPath
                });
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Instantiate_PlacesInScene()
        {
            // First create a prefab asset
            var src = new GameObject("InstPrefabSrc");
            var prefabPath = _tempDir + "/InstTestPrefab.prefab";
            PrefabUtility.SaveAsPrefabAsset(src, prefabPath, out _);
            Object.DestroyImmediate(src);

            try
            {
                var result = PrefabOpHandlers.Instantiate(new JObject
                {
                    ["prefab_path"] = prefabPath
                });
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
                Assert.That(result, Does.Contain("\"path\":"));
                Assert.That(result, Does.Contain("\"instance_id\":"));
                // Cleanup the instantiated object
                var instance = GameObject.Find("InstPrefabSrc");
                if (instance != null) Object.DestroyImmediate(instance);
            }
            finally { }
        }

        [Test]
        public void Unpack_DisconnectsFromPrefab()
        {
            var src = new GameObject("UnpackPrefabSrc");
            var prefabPath = _tempDir + "/UnpackTestPrefab.prefab";
            PrefabUtility.SaveAsPrefabAsset(src, prefabPath, out _);
            Object.DestroyImmediate(src);

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var instance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
            Undo.RegisterCreatedObjectUndo(instance, "test");
            try
            {
                Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(instance),
                    "Precondition: instance should be a prefab instance");

                var result = PrefabOpHandlers.Unpack(new JObject
                {
                    ["instance_path"] = "/" + instance.name
                });
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
                Assert.IsFalse(PrefabUtility.IsPartOfPrefabInstance(instance),
                    "After unpack, object should no longer be a prefab instance");
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void ListOverrides_OnModifiedInstance_ReturnsChanges()
        {
            var src = new GameObject("OverridePrefabSrc");
            var prefabPath = _tempDir + "/OverrideTestPrefab.prefab";
            PrefabUtility.SaveAsPrefabAsset(src, prefabPath, out _);
            Object.DestroyImmediate(src);

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var instance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
            Undo.RegisterCreatedObjectUndo(instance, "test");
            try
            {
                // Modify the instance — move it so there are position overrides
                Undo.RecordObject(instance.transform, "test move");
                instance.transform.position = new Vector3(5, 5, 5);

                var result = PrefabOpHandlers.ListOverrides(new JObject
                {
                    ["instance_path"] = "/" + instance.name
                });
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
                Assert.That(result, Does.Contain("\"property_modifications\":"));
            }
            finally { Object.DestroyImmediate(instance); }
        }

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

    [TestFixture]
    public class WireFormatContractTests
    {
        [Test]
        public void SceneOp_CreateGameObject_ResponseHasFrameContext()
        {
            var result = SceneOpHandlers.CreateGameObject(new JObject { ["name"] = "FrameCtxTest" });
            try
            {
                Assert.That(result, Does.Contain("\"frame\":"));
                Assert.That(result, Does.Contain("\"time\":"));
                Assert.That(result, Does.Contain("\"play_mode\":"));
            }
            finally
            {
                var go = GameObject.Find("FrameCtxTest");
                if (go) Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SceneOp_CreateGameObject_ResponseHasIdentity()
        {
            var result = SceneOpHandlers.CreateGameObject(new JObject { ["name"] = "IdentityTest" });
            try
            {
                Assert.That(result, Does.Contain("\"path\":"));
                Assert.That(result, Does.Contain("\"instance_id\":"));
            }
            finally
            {
                var go = GameObject.Find("IdentityTest");
                if (go) Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ErrorResponse_HasCodeMessageSuggestion()
        {
            // Call with invalid args to trigger error
            var result = SceneOpHandlers.DeleteGameObject(new JObject
            {
                ["path"] = "/NonExistent_WireFormatTest"
            });
            var json = JObject.Parse(result);
            var error = json["error"];
            Assert.IsNotNull(error, "Error responses must have 'error' object");
            Assert.IsNotNull(error["code"], "Error must have 'code'");
            Assert.IsNotNull(error["message"], "Error must have 'message'");
            Assert.IsNotNull(error["suggestion"], "Error must have 'suggestion'");
        }

        // --- Unit 2: ObjectReference writes ---

        [Test]
        public void SetComponent_ObjectReference_AssignsAssetByPath()
        {
            // Create a test material and assign it to MeshRenderer via m_Materials
            // MeshRenderer serializes materials as m_Materials array; use a cube primitive
            // so the renderer already has a slot, then assign via DirectorHelpers directly
            var mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, "Assets/TheatreTestMaterialObjRef.mat");
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ObjRefTest";
            try
            {
                // Use DirectorHelpers.SetPropertyValue directly to test ObjectReference path
                var renderer = go.GetComponent<MeshRenderer>();
                var so = new SerializedObject(renderer);
                // m_Materials is an array; get element 0
                var matsProp = so.FindProperty("m_Materials");
                Assert.IsNotNull(matsProp, "m_Materials property not found");
                var elem = matsProp.GetArrayElementAtIndex(0);
                bool success = DirectorHelpers.SetPropertyValue(
                    elem,
                    "Assets/TheatreTestMaterialObjRef.mat",
                    out var err);
                so.ApplyModifiedProperties();
                Assert.IsTrue(success, $"SetPropertyValue failed: {err}");
                Assert.IsNotNull(renderer.sharedMaterial);
            }
            finally
            {
                Object.DestroyImmediate(go);
                AssetDatabase.DeleteAsset("Assets/TheatreTestMaterialObjRef.mat");
            }
        }

        [Test]
        public void SetComponent_ObjectReference_NullClearsRef()
        {
            // Create a cube, clear its mesh to null via DirectorHelpers
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ClearRefTest";
            try
            {
                var filter = go.GetComponent<MeshFilter>();
                Assert.IsNotNull(filter.sharedMesh, "Cube should start with a mesh");
                var so = new SerializedObject(filter);
                var meshProp = so.FindProperty("m_Mesh");
                Assert.IsNotNull(meshProp, "m_Mesh property not found");
                bool success = DirectorHelpers.SetPropertyValue(meshProp, null, out var err);
                so.ApplyModifiedProperties();
                Assert.IsTrue(success, $"SetPropertyValue failed: {err}");
                Assert.IsNull(filter.sharedMesh);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SetComponent_ObjectReference_BadPath_ReturnsError()
        {
            // Verify that a bad asset path returns an error
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "BadPathTest";
            try
            {
                var filter = go.GetComponent<MeshFilter>();
                var so = new SerializedObject(filter);
                var meshProp = so.FindProperty("m_Mesh");
                bool success = DirectorHelpers.SetPropertyValue(
                    meshProp,
                    "Assets/NoSuchMesh.mesh",
                    out var err);
                Assert.IsFalse(success);
                Assert.That(err, Does.Contain("No asset found"));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // --- Unit 3: Primitive GameObjects ---

        [Test]
        public void CreateGameObject_PrimitiveType_Cube_HasMeshRenderer()
        {
            var result = SceneOpHandlers.CreateGameObject(new JObject
            {
                ["name"] = "TestCube",
                ["primitive_type"] = "cube"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Contain("\"primitive_type\":\"cube\""));
            var go = GameObject.Find("TestCube");
            Assert.IsNotNull(go);
            Assert.IsNotNull(go.GetComponent<MeshFilter>());
            Assert.IsNotNull(go.GetComponent<MeshRenderer>());
            Assert.IsNotNull(go.GetComponent<BoxCollider>());
            Object.DestroyImmediate(go);
        }

        [Test]
        public void CreateGameObject_InvalidPrimitiveType_ReturnsError()
        {
            var result = SceneOpHandlers.CreateGameObject(new JObject
            {
                ["name"] = "BadPrimitive",
                ["primitive_type"] = "triangle"
            });
            Assert.That(result, Does.Contain("\"error\""));
            Assert.That(result, Does.Contain("Unknown primitive_type"));
        }

        [Test]
        public void CreateGameObject_PrimitiveType_WithParentAndPosition()
        {
            var parent = new GameObject("PrimitiveParent");
            try
            {
                var result = SceneOpHandlers.CreateGameObject(new JObject
                {
                    ["name"] = "ChildSphere",
                    ["primitive_type"] = "sphere",
                    ["parent"] = "/" + parent.name,
                    ["position"] = new JArray(1f, 2f, 3f)
                });
                Assert.That(result, Does.Contain("\"result\":\"ok\""));
                var child = parent.transform.Find("ChildSphere");
                Assert.IsNotNull(child);
                Assert.AreEqual(1f, child.localPosition.x, 0.01f);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void CreateGameObject_NoPrimitiveType_CreatesEmptyGO()
        {
            var result = SceneOpHandlers.CreateGameObject(new JObject
            {
                ["name"] = "EmptyObj"
            });
            Assert.That(result, Does.Contain("\"result\":\"ok\""));
            Assert.That(result, Does.Not.Contain("\"primitive_type\""));
            var go = GameObject.Find("EmptyObj");
            Assert.IsNotNull(go);
            Assert.IsNull(go.GetComponent<MeshFilter>());
            Object.DestroyImmediate(go);
        }
    }
}
