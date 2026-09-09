using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AnimalTerrainDemoInstaller
{
    const string RootName = "Animal Terrain Integration";

    [MenuItem("Tools/Boreal Ecosystem/Add Placeholder Animal Demo")]
    public static void AddPlaceholderAnimalDemo()
    {
        TerrainGenerator terrainGenerator = Object.FindAnyObjectByType<TerrainGenerator>();
        if (terrainGenerator == null)
        {
            EditorUtility.DisplayDialog(
                "Boreal Ecosystem",
                "Open the procedural terrain scene before adding the placeholder animal demo.",
                "OK");
            return;
        }

        AnimalTerrainDemoBootstrap bootstrap = Object.FindAnyObjectByType<AnimalTerrainDemoBootstrap>();
        if (bootstrap == null)
        {
            GameObject root = GameObject.Find(RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "Add Placeholder Animal Demo");
            }
            bootstrap = Undo.AddComponent<AnimalTerrainDemoBootstrap>(root);
        }

        Undo.RecordObject(bootstrap, "Configure Placeholder Animal Demo");
        bootstrap.terrainGenerator = terrainGenerator;
        bootstrap.simulationFocus = terrainGenerator.viewer;
        EditorUtility.SetDirty(bootstrap);
        EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);
        Selection.activeGameObject = bootstrap.gameObject;
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log("Placeholder animal integration added. Enter Play mode to generate the cylinders, food cubes, temperature, borders, and land-only navigation.", bootstrap);
    }
}
