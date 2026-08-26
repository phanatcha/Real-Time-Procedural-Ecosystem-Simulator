using UnityEngine;

[CreateAssetMenu()]
public class TextureData : UpdatableData
{
    const int textureSize = 512;
    const TextureFormat textureFormat = TextureFormat.RGB565;

    public Layer[] layers;

    float savedMinHeight;
    float savedMaxHeight;

    public void ApplyToMaterial(Material material)
    {
        material.SetInt("layerCount", layers.Length);
        material.SetColorArray("baseColours", GetColours());
        material.SetFloatArray("baseStartHeights", GetStartHeights());
        material.SetFloatArray("baseBlends", GetBlends());

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
            heights[i] = layers[i].startHeight;
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
        [Range(0, 1)]
        public float startHeight;
        [Range(0, 1)]
        public float blendStrength;
    }
}
