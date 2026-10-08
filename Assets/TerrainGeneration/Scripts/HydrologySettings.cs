using UnityEngine;

[System.Serializable]
public class RiverSettings
{
    public bool enabled = true;
    [Tooltip("Replaced by a seed from the world seed when SeedManager generates the world.")]
    public int seed;
    [Tooltip("World units per meander wavelength.")]
    public float scale = 250f;
    [Range(0.001f, 0.2f)]
    [Tooltip("How wide the carved channel is, in noise-threshold units.")]
    public float width = 0.025f;
    [Range(0, 1)]
    [Tooltip("Rivers only carve where the ground, after the island falloff and before the height curve, falls in this range. " +
             "Start it about 0.05 below the shoreline (about 0.55 with the current height curve and water level) so rivers reach the sea.")]
    public float minHeightPercent = 0.5f;
    [Range(0, 1)]
    public float maxHeightPercent = 0.8f;
    [Range(0, 1)]
    [Tooltip("Height the channel bed is pulled down to, after the island falloff and before the height curve. " +
             "Keep it below the shoreline (about 0.55) so the channel fills with water, and no lower than Min Height Percent, " +
             "or the carving raises the ground at the bottom of the range.")]
    public float bedLevel = 0.5f;

    public void ValidateValues()
    {
        scale = Mathf.Max(scale, 0.01f);
        width = Mathf.Max(width, 0.001f);
        maxHeightPercent = Mathf.Max(maxHeightPercent, minHeightPercent + 0.05f);
    }
}

[System.Serializable]
public class LakeSettings
{
    public bool enabled = true;
    [Tooltip("Replaced by a seed from the world seed when SeedManager generates the world.")]
    public int seed;
    [Tooltip("World units per lake-noise feature - bigger = fewer, larger lakes.")]
    public float scale = 400f;
    [Range(0, 1)]
    [Tooltip("Only the noise values above this become a lake.")]
    public float threshold = 0.62f;
    [Range(0, 1)]
    [Tooltip("Lakes only settle where the ground, after the island falloff and before the height curve, falls in this range. " +
             "Start it above the shoreline (about 0.55 with the current height curve and water level) so lakes stay inland.")]
    public float minHeightPercent = 0.6f;
    [Range(0, 1)]
    public float maxHeightPercent = 0.82f;
    [Range(0, 1)]
    [Tooltip("Height the lake bed is pulled down to, after the island falloff and before the height curve. " +
             "Keep it just below the shoreline (about 0.55) so the lake fills with water.")]
    public float bedLevel = 0.5f;

    public void ValidateValues()
    {
        scale = Mathf.Max(scale, 0.01f);
        maxHeightPercent = Mathf.Max(maxHeightPercent, minHeightPercent + 0.05f);
    }
}
