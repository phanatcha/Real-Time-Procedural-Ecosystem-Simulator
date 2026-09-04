using UnityEngine;

[CreateAssetMenu()]
public class TextureData : UpdatableData
{
    const int textureSize = 512;
    const TextureFormat textureFormat = TextureFormat.RGB565;

    public EnvironmentDefinitions environmentDefinitions;
    public Layer[] layers;

    [Header("Moisture / Bog")]
    [Tooltip("Low-frequency noise drives where bog/wetland patches settle - not simulated, just a smooth noise field sampled in the shader.")]
    public bool enableBog = true;
    public Color bogTint = new Color(0.13f, 0.14f, 0.09f);
    [Range(0, 1)]
    [Tooltip("Only the wettest fraction of the moisture range becomes bog.")]
    public float bogMoistureThreshold = 0.6f;
    [Range(0, 1)]
    public float bogMinHeight = 0.2f;
    [Range(0, 1)]
    public float bogMaxHeight = 0.5f;
    [Range(0, 1)]
    [Tooltip("Bogs need flat ground - same 0-1 slope scale the shader already uses for rock/snow.")]
    public float bogMaxSlope = 0.25f;

    [Header("Erosion Streaks")]
    [Tooltip("Not simulated - a stretched noise pattern darkening steep faces to suggest water-carved rilling, since true particle erosion doesn't fit infinite chunk streaming.")]
    public bool enableErosion = true;
    [Range(0, 1)]
    [Tooltip("Slope (same 0-1 scale) above which erosion streaks start appearing.")]
    public float erosionSlopeThreshold = 0.35f;
    [Range(0, 1)]
    public float erosionStrength = 0.6f;
    [Range(0, 1)]
    [Tooltip("How dark the carved grooves get relative to the ridges between them.")]
    public float erosionDarkening = 0.35f;
    [Tooltip("World units per groove - smaller = finer, more frequent streaking.")]
    public float erosionScale = 6f;

    float savedMinHeight;
    float savedMaxHeight;

    public void ApplyToMaterial(Material material)
    {
        if (material == null || environmentDefinitions == null) return;

        material.SetInt("layerCount", layers.Length);
        material.SetColorArray("baseColours", GetColours());
        material.SetFloatArray("baseStartHeights", GetStartHeights());
        material.SetFloatArray("baseBlends", GetBlends());
        material.SetFloat("normalizedWaterLevel", environmentDefinitions.ShorelineThreshold);
        material.SetFloat("landThreshold", environmentDefinitions.LandThreshold);

        material.SetInt("enableBog", enableBog ? 1 : 0);
        material.SetColor("bogTint", bogTint);
        material.SetFloat("moistureScale", Mathf.Max(environmentDefinitions.moistureScale, 0.01f));
        material.SetVector("moistureOffset", environmentDefinitions.moistureOffset);
        material.SetFloat("bogMoistureThreshold", bogMoistureThreshold);
        material.SetFloat("bogMinHeight", bogMinHeight);
        material.SetFloat("bogMaxHeight", bogMaxHeight);
        material.SetFloat("bogMaxSlope", bogMaxSlope);

        material.SetInt("enableErosion", enableErosion ? 1 : 0);
        material.SetFloat("erosionSlopeThreshold", erosionSlopeThreshold);
        material.SetFloat("erosionStrength", erosionStrength);
        material.SetFloat("erosionDarkening", erosionDarkening);
        material.SetFloat("erosionScale", Mathf.Max(erosionScale, 0.01f));

        UpdateMeshHeights(material, savedMinHeight, savedMaxHeight);
    }

    public void UpdateMeshHeights(Material material, float minHeight, float maxHeight)
    {
        savedMinHeight = minHeight;
        savedMaxHeight = maxHeight;

        material.SetFloat("minHeight", minHeight);
        material.SetFloat("maxHeight", maxHeight);
    }

    Color[] GetColours()
    {
        Color[] colours = new Color[layers.Length];
        for (int i = 0; i < layers.Length; i++)
        {
            colours[i] = layers[i].tint;
        }
        return colours;
    }

    float[] GetStartHeights()
    {
        float[] heights = new float[layers.Length];
        for (int i = 0; i < layers.Length; i++)
        {
            heights[i] = environmentDefinitions.GetBiomeStartHeight(layers[i].biome);
        }
        return heights;
    }

    float[] GetBlends()
    {
        float[] blends = new float[layers.Length];
        for (int i = 0; i < layers.Length; i++)
        {
            blends[i] = layers[i].blendStrength;
        }
        return blends;
    }

    [System.Serializable]
    public class Layer
    {
        public string name;
        public Color tint;
        public BiomeId biome;
        [Range(0, 1)]
        public float blendStrength;
    }
}
