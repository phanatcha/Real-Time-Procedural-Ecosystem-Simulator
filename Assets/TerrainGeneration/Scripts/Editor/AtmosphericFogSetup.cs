using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Tools > Boreal Ecosystem > Apply Atmospheric Fog: replaces Unity's built-in fog with the full-screen atmosphere
// (haze, valley mist and sun glow). Adds a Full Screen Pass Renderer Feature with the fog material to the active
// renderer, puts AtmosphericFog on the main camera with its edge matched to the terrain's view distance, and turns
// the built-in fog off. Logs every change. Running it again only updates what is already there.
static class AtmosphericFogSetup
{
    const string MaterialPath = "Assets/TerrainGeneration/Materials/Atmospheric Fog.mat";
    const string ShaderName = "Hidden/Boreal/Atmospheric Fog";

    [MenuItem("Tools/Boreal Ecosystem/Apply Atmospheric Fog")]
    static void Apply()
    {
        StringBuilder log = new StringBuilder("Atmospheric fog applied:\n");
        Material material = FogMaterial(log);
        if (material == null)
        {
            Debug.LogError($"Atmospheric fog: shader '{ShaderName}' not found. Let Unity finish importing, then try again.");
            return;
        }

        AddRendererFeature(material, log);
        AddCameraSettings(log);

        log.AppendLine($"  Built-in fog: {(RenderSettings.fog ? "on" : "off")} -> off (the atmosphere replaces it)");
        RenderSettings.fog = false;

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }

    static Material FogMaterial(StringBuilder log)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null) return material;

        Shader shader = Shader.Find(ShaderName);
        if (shader == null) return null;

        material = new Material(shader);
        AssetDatabase.CreateAsset(material, MaterialPath);
        log.AppendLine($"  Created {MaterialPath}");
        return material;
    }

    static void AddRendererFeature(Material material, StringBuilder log)
    {
        UniversalRenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline == null)
        {
            log.AppendLine("  Renderer: no URP asset active, skipped");
            return;
        }

        SerializedObject pipelineSettings = new SerializedObject(pipeline);
        int defaultIndex = pipelineSettings.FindProperty("m_DefaultRendererIndex").intValue;
        ScriptableRendererData renderer = pipelineSettings.FindProperty("m_RendererDataList")
            .GetArrayElementAtIndex(defaultIndex).objectReferenceValue as ScriptableRendererData;
        if (renderer == null)
        {
            log.AppendLine("  Renderer: none found, skipped");
            return;
        }

        FullScreenPassRendererFeature feature = null;
        foreach (ScriptableRendererFeature existing in renderer.rendererFeatures)
        {
            if (existing is FullScreenPassRendererFeature fullScreen && fullScreen.passMaterial == material)
            {
                feature = fullScreen;
            }
        }

        if (feature == null)
        {
            // The same steps as the renderer's own "Add Renderer Feature" button: the feature lives inside the
            // renderer asset, and the renderer keeps a map of its features' file IDs.
            feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = "Atmospheric Fog";
            AssetDatabase.AddObjectToAsset(feature, renderer);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

            SerializedObject rendererSettings = new SerializedObject(renderer);
            SerializedProperty features = rendererSettings.FindProperty("m_RendererFeatures");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            SerializedProperty featureMap = rendererSettings.FindProperty("m_RendererFeatureMap");
            featureMap.arraySize++;
            featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue = localId;
            rendererSettings.ApplyModifiedProperties();
            log.AppendLine($"  {renderer.name}: added the Atmospheric Fog full screen pass");
        }

        // Before post-processing, so the fog is drawn in HDR and bloom and tonemapping treat it like the rest of
        // the scene. It needs the depth texture to know how far away each pixel is.
        feature.passMaterial = material;
        feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
        feature.fetchColorBuffer = true;
        feature.requirements = ScriptableRenderPassInput.Depth;
        feature.passIndex = 0;
        feature.SetActive(true);
        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(renderer);
        renderer.SetDirty();
    }

    static void AddCameraSettings(StringBuilder log)
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            log.AppendLine("  Camera: no main camera, skipped");
            return;
        }

        AtmosphericFog fog = camera.GetComponent<AtmosphericFog>();
        if (fog == null)
        {
            fog = Undo.AddComponent<AtmosphericFog>(camera.gameObject);
            log.AppendLine($"  {camera.name}: added AtmosphericFog");
        }

        // The world ends where the farthest terrain detail level stops drawing, so the haze must hide it by then.
        TerrainGenerator terrain = Object.FindFirstObjectByType<TerrainGenerator>();
        if (terrain != null && terrain.detailLevels != null && terrain.detailLevels.Length > 0)
        {
            float viewDistance = terrain.detailLevels[terrain.detailLevels.Length - 1].visibleDstThreshold;
            Undo.RecordObject(fog, "Apply Atmospheric Fog");
            fog.edgeStart = viewDistance * 0.7f;
            fog.edgeEnd = viewDistance - 10f;
            log.AppendLine($"  Edge fade: {fog.edgeStart:0}–{fog.edgeEnd:0} m (terrain view distance {viewDistance:0} m)");
        }
    }
}
