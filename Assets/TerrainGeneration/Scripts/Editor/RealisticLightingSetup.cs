using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Tools > Boreal Ecosystem > Apply Realistic Lighting: believable daylight for the stylised island. Sets the sun,
// sky, ambient light, post-processing, shadow range and anti-aliasing in the open scene and the active URP asset,
// and logs every old and new value so before/after screenshots can be reported. Scene changes can be undone; the
// URP asset and the sky material are assets, so revert those through version control.
static class RealisticLightingSetup
{
    const string ProfilePath = "Assets/Settings/BorealLightingProfile.asset";

    // An afternoon sun about 32° up: low enough for long shadows that show the shape of the valleys. Direct sunlight
    // is about 5,300 K (warmer than noon daylight, much cooler than a 3,500 K sunset).
    const float SunTemperature = 5300f;
    const float SunIntensity = 2.3f;
    const float SunPitch = 32f;
    const float SunYaw = -35f;

    // Shadows reach 350 m: past the valley walls in view, short of the fog (550–780 m), which hides the edge.
    // Cascades end at 21, 63, 158 and 350 m, so the near ground keeps sharp shadows.
    const float ShadowDistance = 350f;
    static readonly Vector3 CascadeSplits = new Vector3(0.06f, 0.18f, 0.45f);
    const int ShadowResolution = 4096;

    [MenuItem("Tools/Boreal Ecosystem/Apply Realistic Lighting")]
    static void Apply()
    {
        StringBuilder log = new StringBuilder("Realistic lighting applied (old -> new):\n");
        ApplySun(log);
        ApplySky(log);
        ApplyPostProcessing(log);
        ApplyPipeline(log);
        ApplyCamera(log);

        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }

    static void ApplySun(StringBuilder log)
    {
        Light sun = RenderSettings.sun;
        if (sun == null || sun.type != LightType.Directional)
        {
            sun = null;
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional)
                {
                    sun = light;
                    break;
                }
            }
        }

        if (sun == null)
        {
            log.AppendLine("  Sun: no directional light in the scene, skipped");
            return;
        }

        Undo.RecordObject(sun, "Apply Realistic Lighting");
        Undo.RecordObject(sun.transform, "Apply Realistic Lighting");
        Note(log, "Sun colour temperature (K)", sun.useColorTemperature ? sun.colorTemperature.ToString("0") : "off", SunTemperature);
        Note(log, "Sun intensity", sun.intensity, SunIntensity);
        Note(log, "Sun rotation", sun.transform.eulerAngles, new Vector3(SunPitch, SunYaw, 0f));
        Note(log, "Sun shadows", sun.shadows, LightShadows.Soft);
        sun.useColorTemperature = true;
        sun.colorTemperature = SunTemperature;
        sun.color = Color.white;
        sun.intensity = SunIntensity;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 1f;
        sun.transform.rotation = Quaternion.Euler(SunPitch, SunYaw, 0f);
        RenderSettings.sun = sun;

        if (sun.GetComponent<SkyAmbient>() == null)
        {
            Undo.AddComponent<SkyAmbient>(sun.gameObject);
            log.AppendLine("  Sun: added SkyAmbient so ambient light follows the sky");
        }
    }

    static void ApplySky(StringBuilder log)
    {
        Material sky = RenderSettings.skybox;
        if (sky != null && sky.shader.name == "Skybox/Procedural")
        {
            Undo.RecordObject(sky, "Apply Realistic Lighting");
            SetFloat(log, sky, "_SunDisk", 2f);
            SetFloat(log, sky, "_SunSize", 0.035f);
            SetFloat(log, sky, "_SunSizeConvergence", 6f);
            SetFloat(log, sky, "_AtmosphereThickness", 1f);
            SetFloat(log, sky, "_Exposure", 1.25f);
            EditorUtility.SetDirty(sky);
        }
        else
        {
            log.AppendLine("  Sky: not the procedural skybox, left as it is");
        }

        // Shadows are filled by light from the whole sky, which is what keeps them blue rather than black.
        Note(log, "Ambient source", RenderSettings.ambientMode, AmbientMode.Skybox);
        Note(log, "Ambient intensity", RenderSettings.ambientIntensity, 1f);
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 1f;
    }

    static void ApplyPostProcessing(StringBuilder log)
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            log.AppendLine($"  Created {ProfilePath}");
        }

        // ACES compresses bright sky and sunlit snow the way film does, instead of clipping them to white.
        Get<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);

        // ACES darkens and desaturates a little; this brings the image back without going past natural.
        ColorAdjustments colour = Get<ColorAdjustments>(profile);
        colour.postExposure.Override(0.3f);
        colour.contrast.Override(12f);
        colour.saturation.Override(10f);

        Get<WhiteBalance>(profile).temperature.Override(5f);

        // Slightly cool shadows and warm highlights, as under a blue sky and a yellow sun.
        ShadowsMidtonesHighlights tones = Get<ShadowsMidtonesHighlights>(profile);
        tones.shadows.Override(new Vector4(0.97f, 0.99f, 1.04f, 0f));
        tones.highlights.Override(new Vector4(1.03f, 1f, 0.96f, 0f));

        // Only the brightest things glow: the sun, its glint on water and sunlit snow.
        Bloom bloom = Get<Bloom>(profile);
        bloom.threshold.Override(1.1f);
        bloom.intensity.Override(0.4f);
        bloom.scatter.Override(0.65f);
        bloom.highQualityFiltering.Override(true);

        Vignette vignette = Get<Vignette>(profile);
        vignette.intensity.Override(0.18f);
        vignette.smoothness.Override(0.4f);

        EditorUtility.SetDirty(profile);

        Volume volume = null;
        foreach (Volume candidate in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
        {
            if (candidate.isGlobal)
            {
                volume = candidate;
                break;
            }
        }

        if (volume == null)
        {
            GameObject volumeObject = new GameObject("Global Volume");
            Undo.RegisterCreatedObjectUndo(volumeObject, "Apply Realistic Lighting");
            volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
        }

        Undo.RecordObject(volume, "Apply Realistic Lighting");
        Note(log, "Volume profile", volume.sharedProfile != null ? volume.sharedProfile.name : "none", profile.name);
        volume.sharedProfile = profile;
        log.AppendLine("  Post-processing: ACES tonemapping, colour adjustments, white balance, shadows/highlights, bloom, vignette");
    }

    static void ApplyPipeline(StringBuilder log)
    {
        UniversalRenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline == null)
        {
            log.AppendLine("  URP asset: none active, skipped");
            return;
        }

        SerializedObject settings = new SerializedObject(pipeline);
        SetFloat(log, settings, "m_ShadowDistance", ShadowDistance);
        SetInt(log, settings, "m_ShadowCascadeCount", 4);
        SetVector3(log, settings, "m_Cascade4Split", CascadeSplits);
        SetInt(log, settings, "m_MainLightShadowmapResolution", ShadowResolution);
        // Far cascades have larger shadow texels, so they need a little more bias to avoid speckled self-shadowing.
        SetFloat(log, settings, "m_ShadowDepthBias", 0.4f);
        SetFloat(log, settings, "m_ShadowNormalBias", 0.6f);
        SetInt(log, settings, "m_SoftShadowsSupported", 1);
        SetInt(log, settings, "m_SoftShadowQuality", 3);
        settings.ApplyModifiedProperties();
        EditorUtility.SetDirty(pipeline);

        SerializedProperty renderers = settings.FindProperty("m_RendererDataList");
        int defaultIndex = settings.FindProperty("m_DefaultRendererIndex").intValue;
        ScriptableRendererData renderer = renderers.GetArrayElementAtIndex(defaultIndex).objectReferenceValue as ScriptableRendererData;
        if (renderer == null) return;

        foreach (ScriptableRendererFeature feature in renderer.rendererFeatures)
        {
            if (feature == null || feature.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;

            // A 1 m radius darkens the creases of the ground and the foot of every tree and rock; the shipped 0.3 m
            // only reached small details.
            SerializedObject occlusion = new SerializedObject(feature);
            SetFloat(log, occlusion, "m_Settings.Intensity", 0.7f);
            SetFloat(log, occlusion, "m_Settings.Radius", 1f);
            SetFloat(log, occlusion, "m_Settings.DirectLightingStrength", 0.25f);
            SetFloat(log, occlusion, "m_Settings.Falloff", 300f);
            occlusion.ApplyModifiedProperties();
            feature.SetActive(true);
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(renderer);
        }
    }

    static void ApplyCamera(StringBuilder log)
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            log.AppendLine("  Camera: no main camera, skipped");
            return;
        }

        // SMAA smooths the jagged edges of distant ridges and trees without smearing moving animals, as TAA would.
        UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
        Undo.RecordObject(data, "Apply Realistic Lighting");
        Note(log, "Camera anti-aliasing", data.antialiasing, AntialiasingMode.SubpixelMorphologicalAntiAliasing);
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        data.renderPostProcessing = true;
    }

    static T Get<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet(out T component))
        {
            component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
        }

        component.active = true;
        return component;
    }

    static void Note(StringBuilder log, string what, object before, object after)
    {
        log.AppendLine($"  {what}: {before} -> {after}");
    }

    static void SetFloat(StringBuilder log, Material material, string property, float value)
    {
        if (!material.HasProperty(property)) return;
        Note(log, $"Sky {property}", material.GetFloat(property), value);
        material.SetFloat(property, value);
    }

    static void SetFloat(StringBuilder log, SerializedObject target, string path, float value)
    {
        SerializedProperty property = target.FindProperty(path);
        if (property == null) return;
        Note(log, $"{target.targetObject.name} {path}", property.floatValue, value);
        property.floatValue = value;
    }

    static void SetInt(StringBuilder log, SerializedObject target, string path, int value)
    {
        SerializedProperty property = target.FindProperty(path);
        if (property == null) return;
        int before = property.propertyType == SerializedPropertyType.Boolean ? (property.boolValue ? 1 : 0) : property.intValue;
        Note(log, $"{target.targetObject.name} {path}", before, value);
        if (property.propertyType == SerializedPropertyType.Boolean) property.boolValue = value != 0;
        else property.intValue = value;
    }

    static void SetVector3(StringBuilder log, SerializedObject target, string path, Vector3 value)
    {
        SerializedProperty property = target.FindProperty(path);
        if (property == null) return;
        Note(log, $"{target.targetObject.name} {path}", property.vector3Value, value);
        property.vector3Value = value;
    }
}
