using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Boreal.TerrainGeneration.Editor
{
    // Tools > Boreal Ecosystem > Apply Dense Grass: puts GrassField in the scene on the terrain generator, with its
    // compute shader and a Custom/Grass Field material asset to tune. Without it, GrassField still adds itself when the
    // game starts, with a plain material. Running it again only fills in what is missing.
    static class GrassFieldSetup
    {
        const string MaterialPath = "Assets/TerrainGeneration/Materials/Grass Field Mat.mat";
        const string ComputePath = "Assets/TerrainGeneration/Resources/GrassField.compute";
        const string ShaderName = "Custom/Grass Field";

        [MenuItem("Tools/Boreal Ecosystem/Apply Dense Grass")]
        static void Apply()
        {
            TerrainGenerator terrain = Object.FindAnyObjectByType<TerrainGenerator>();
            if (terrain == null)
            {
                Debug.LogError("Dense grass: no TerrainGenerator in the open scene.");
                return;
            }

            ComputeShader placement = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find(ShaderName);
                if (shader == null || placement == null)
                {
                    Debug.LogError("Dense grass: the grass shaders are not imported yet. Let Unity finish importing, then try again.");
                    return;
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            GrassField field = terrain.GetComponent<GrassField>();
            if (field == null) field = Undo.AddComponent<GrassField>(terrain.gameObject);
            Undo.RecordObject(field, "Apply Dense Grass");
            field.terrain = terrain;
            field.placement = placement;
            field.material = material;

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log($"Dense grass applied: GrassField on {terrain.name} (radius {field.radius:0} m, spacing {field.spacing:0.##} m), " +
                      $"material {MaterialPath}. Press Play to see it; save the scene to keep it.");
        }
    }
}
