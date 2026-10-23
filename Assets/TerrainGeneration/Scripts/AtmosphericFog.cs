using UnityEngine;

// Settings for the full-screen atmosphere (Hidden/Boreal/Atmospheric Fog), sent to the shader every frame. It
// replaces Unity's built-in fog, which Tools > Boreal Ecosystem > Apply Atmospheric Fog turns off. Sits on the main
// camera; turning it off turns the atmosphere off.
[ExecuteAlways]
public class AtmosphericFog : MonoBehaviour
{
    [Header("Haze: far mountains turn blue")]
    [ColorUsage(false, true)]
    public Color hazeColour = new Color(0.64f, 0.75f, 0.9f);
    [Tooltip("Haze added by each metre of air. At 0.0009, mountains 400 m away are about a third haze and the haze " +
             "is half by about 770 m.")]
    [Min(0f)] public float hazeDensity = 0.0009f;

    [Header("Mist: gathers low, in the valleys")]
    [ColorUsage(false, true)]
    public Color mistColour = new Color(0.8f, 0.84f, 0.88f);
    [Tooltip("Mist added by each metre of air at the mist's base, just above the water.")]
    [Min(0f)] public float mistDensity = 0.0012f;
    [Tooltip("Height in metres over which the mist thins to about a third, so smaller keeps it lower in the valleys.")]
    [Min(0.1f)] public float mistFalloff = 25f;
    [Tooltip("Height of the densest mist above the water, in metres.")]
    public float mistBaseHeight = 2f;

    [Header("Sun glow")]
    [Tooltip("The light whose direction and colour warm the haze. Empty uses the scene's sun.")]
    public Light sun;
    [Tooltip("How strongly the haze glows when looking towards the sun.")]
    [Min(0f)] public float sunGlow = 1.2f;

    [Header("Edges")]
    [Tooltip("Distance in metres where the drawn world starts fading into the haze. Keep the fade inside the terrain's view distance.")]
    [Min(0f)] public float edgeStart = 560f;
    [Tooltip("Distance in metres where the drawn world is fully hidden in haze, just inside the terrain's view distance.")]
    [Min(0f)] public float edgeEnd = 790f;
    [Tooltip("How far above the horizon, in degrees, the sky fades to the haze colour, so the world's edge meets the sky without a seam.")]
    [Range(0.5f, 45f)] public float horizonBand = 10f;

    [Range(0f, 1f)] public float strength = 1f;

    static readonly int StrengthId = Shader.PropertyToID("_BorealFogStrength");
    static readonly int HazeColourId = Shader.PropertyToID("_BorealFogHazeColour");
    static readonly int MistColourId = Shader.PropertyToID("_BorealFogMistColour");
    static readonly int SunColourId = Shader.PropertyToID("_BorealFogSunColour");
    static readonly int SunDirectionId = Shader.PropertyToID("_BorealFogSunDirection");
    static readonly int DensityId = Shader.PropertyToID("_BorealFogDensity");
    static readonly int EdgeId = Shader.PropertyToID("_BorealFogEdge");

    void OnEnable() => Apply();

    void OnDisable() => Shader.SetGlobalFloat(StrengthId, 0f);

    void OnValidate()
    {
        if (isActiveAndEnabled) Apply();
    }

    // The sun moves and changes colour at run time, so the values are sent every frame.
    void LateUpdate() => Apply();

    void Apply()
    {
        Light light = sun != null ? sun : RenderSettings.sun;
        Vector3 towardsSun = Vector3.up;
        Color glow = Color.black;
        if (light != null && light.isActiveAndEnabled)
        {
            towardsSun = -light.transform.forward;
            // The same linear colour URP lights the scene with, scaled down so the glow matches the haze's brightness.
            Color sunColour = light.color.linear * light.intensity;
            if (light.useColorTemperature) sunColour *= Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
            glow = sunColour * (0.4f * sunGlow);
        }

        Shader.SetGlobalFloat(StrengthId, strength);
        Shader.SetGlobalColor(HazeColourId, hazeColour);
        Shader.SetGlobalColor(MistColourId, mistColour);
        Shader.SetGlobalVector(SunColourId, new Vector4(glow.r, glow.g, glow.b, 0f));
        Shader.SetGlobalVector(SunDirectionId, towardsSun);
        Shader.SetGlobalVector(DensityId, new Vector4(hazeDensity, mistDensity, mistFalloff, mistBaseHeight));
        Shader.SetGlobalVector(EdgeId, new Vector4(edgeStart, Mathf.Max(edgeEnd, edgeStart + 1f),
            Mathf.Sin(horizonBand * Mathf.Deg2Rad), 0f));
    }
}
