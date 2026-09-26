using System;
using UnityEngine;

[Serializable]
public sealed class HydraulicErosionSettings
{
    public bool enabled;
    [Range(33, 513)] public int resolution = 257;
    [Range(0, 200000)] public int dropletCount = 6000;
    public int seed = 1729;
    [Range(8, 128)] public int maxLifetime = 60;
    [Range(1, 6)] public int brushRadius = 3;
    [Range(0f, 0.95f)] public float inertia = 0.1f;
    [Range(0f, 1f)] public float erosionRate = 0.3f;
    [Range(0f, 1f)] public float depositionRate = 0.3f;
    [Range(0.001f, 0.5f)] public float evaporationRate = 0.02f;
    [Range(0.1f, 20f)] public float sedimentCapacity = 4f;
    [Range(0f, 0.1f)] public float minimumCapacity = 0.001f;
    [Range(0.1f, 20f)] public float gravity = 4f;
    [Tooltip("Maximum absolute erosion or deposition at a bake node, in terrain height units.")]
    [Range(0f, 20f)] public float maxHeightChange = 6f;
    [Tooltip("Tapers changes to zero at the finite world's boundary, avoiding a seam with the unmodified exterior.")]
    [Range(2, 32)] public int edgeFadeCells = 8;

    public HydraulicErosionSettings ValidatedCopy()
    {
        HydraulicErosionSettings copy = (HydraulicErosionSettings)MemberwiseClone();
        copy.ValidateValues();
        return copy;
    }

    public void ValidateValues()
    {
        resolution = Mathf.Clamp(resolution, 33, 513);
        dropletCount = Mathf.Clamp(dropletCount, 0, 200000);
        maxLifetime = Mathf.Clamp(maxLifetime, 8, 128);
        brushRadius = Mathf.Clamp(brushRadius, 1, 6);
        inertia = Clamp(inertia, 0f, 0.95f, 0.1f);
        erosionRate = Clamp(erosionRate, 0f, 1f, 0.3f);
        depositionRate = Clamp(depositionRate, 0f, 1f, 0.3f);
        evaporationRate = Clamp(evaporationRate, 0.001f, 0.5f, 0.02f);
        sedimentCapacity = Clamp(sedimentCapacity, 0.1f, 20f, 4f);
        minimumCapacity = Clamp(minimumCapacity, 0f, 0.1f, 0.001f);
        gravity = Clamp(gravity, 0.1f, 20f, 4f);
        maxHeightChange = Clamp(maxHeightChange, 0f, 20f, 6f);
        edgeFadeCells = Mathf.Clamp(edgeFadeCells, 2, Mathf.Min(32, (resolution - 1) / 4));
    }

    static float Clamp(float value, float min, float max, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }
}
