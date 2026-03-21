using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Theatre.Editor
{
    public static class TestSceneCreator
    {
        private const string ScenePath = "Assets/Scenes/TestScene_Hierarchy.unity";

        [InitializeOnLoadMethod]
        private static void AutoCreateIfMissing()
        {
            if (!System.IO.File.Exists(ScenePath))
            {
                EditorApplication.delayCall += CreateTestScene;
            }
        }

        [MenuItem("Theatre/Create Test Scene")]
        public static void CreateTestScene()
        {
            // Create directory if needed
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            // Create new empty scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Player hierarchy
            var player = CreateGO("Player", null, Vector3.zero);
            player.tag = "Player";

            var cam = CreateGO("Camera", player, Vector3.zero);
            cam.AddComponent<Camera>();

            var model = CreateGO("Model", player, Vector3.zero);
            CreateGO("Body", model, Vector3.zero);
            CreateGO("Head", model, Vector3.zero);
            CreateGO("Weapon", model, Vector3.zero);

            var ui = CreateGO("UI", player, Vector3.zero);
            CreateGO("HealthBar", ui, Vector3.zero);

            // Environment
            var env = CreateGO("Environment", null, Vector3.zero);

            var floor = CreateGO("Floor", env, new Vector3(0, -0.5f, 0));
            floor.AddComponent<BoxCollider>();
            floor.transform.localScale = new Vector3(100, 1, 100);

            CreateGO("Wall_01", env, new Vector3(5, 1, 0));
            CreateGO("Wall_02", env, new Vector3(-5, 1, 0));
            CreateGO("Pillar", env, new Vector3(0, 1, 3));

            // Enemies
            var enemies = CreateGO("Enemies", null, Vector3.zero);
            CreateGO("Scout_01", enemies, new Vector3(10, 0, 5));
            CreateGO("Scout_02", enemies, new Vector3(12, 0, 6));
            CreateGO("Scout_03", enemies, new Vector3(11, 0, 4));
            CreateGO("Heavy_01", enemies, new Vector3(-8, 0, -3));
            CreateGO("Heavy_02", enemies, new Vector3(-9, 0, -4));

            // Collectibles
            var collectibles = CreateGO("Collectibles", null, Vector3.zero);
            CreateGO("Coin_01", collectibles, new Vector3(3, 0.5f, 2));
            CreateGO("Coin_02", collectibles, new Vector3(-3, 0.5f, -2));

            // Save
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Theatre] Test scene created at {ScenePath}");
        }

        private static GameObject CreateGO(string name, GameObject parent, Vector3 position)
        {
            var go = new GameObject(name);
            if (parent != null)
                go.transform.SetParent(parent.transform, false);
            go.transform.position = position;
            return go;
        }
    }
}
