using UnityEngine;

[CreateAssetMenu()]
public class HeightMapSettings : UpdatableData
{
    public NoiseSettings noiseSettings;
    public RidgeSettings ridgeSettings = new RidgeSettings();

    public bool useFalloff;
    [Tooltip("Distance from world origin (0,0) where the island fully becomes ocean.")]
    public float worldRadius = 1000f;

    public float heightMultiplier;
    public AnimationCurve heightCurve;

    public float minHeight
    {
        get
        {
            return heightMultiplier * heightCurve.Evaluate(0);
        }
    }

    public float maxHeight
    {
        get
        {
            return heightMultiplier * heightCurve.Evaluate(1);
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        if (noiseSettings != null)
        {
            noiseSettings.ValidateValues();
        }
        if (ridgeSettings != null)
        {
            ridgeSettings.ValidateValues();
        }
        base.OnValidate();
    }
#endif
}
