using UnityEngine;

[System.Serializable]
public class RiverSettings
{
    public bool enabled = true;
    public int seed;
    [Tooltip("World units per meander wavelength.")]
    public float scale = 250f;
    [Range(0.001f, 0.2f)]
    [Tooltip("How wide the carved channel is, in noise-threshold units.")]
    public float width = 0.025f;
    [Range(0, 1)]
    [Tooltip("Rivers only carve where the natural (pre-carve) terrain falls in this height range.")]
    public float minHeightPercent = 0.15f;
    [Range(0, 1)]
    public float maxHeightPercent = 0.65f;
    [Range(0, 1)]
    [Tooltip("Pre-curve value the channel bed is pulled toward - keep below Water Shallow's start height.")]
    public float bedLevel = 0.12f;

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
    public int seed;
    [Tooltip("World units per lake-noise feature - bigger = fewer, larger lakes.")]
    public float scale = 400f;
    [Range(0, 1)]
    [Tooltip("Only the noise values above this become a lake.")]
    public float threshold = 0.62f;
    [Range(0, 1)]
    [Tooltip("Lakes only settle where the natural (pre-carve) terrain falls in this height range.")]
    public float minHeightPercent = 0.1f;
    [Range(0, 1)]
    public float maxHeightPercent = 0.4f;
    [Range(0, 1)]
    [Tooltip("Pre-curve value the lake bed is pulled toward - keep below Water Deep's start height for a proper deep look.")]
    public float bedLevel = 0.08f;

    public void ValidateValues()
    {
        scale = Mathf.Max(scale, 0.01f);
        maxHeightPercent = Mathf.Max(maxHeightPercent, minHeightPercent + 0.05f);
    }
}
