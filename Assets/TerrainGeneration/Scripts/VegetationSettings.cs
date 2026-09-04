using UnityEngine;

[System.Serializable]
public class VegetationTypeSettings
{
    public bool enabled = true;
    [Tooltip("World units between candidate placement cells - smaller = denser.")]
    public float cellSize = 10f;
    [Range(0, 1)]
    [Tooltip("Chance an eligible cell actually spawns an instance.")]
    public float density = 0.6f;

    [Header("Placement constraints")]
    [Range(0, 1)]
    public float minHeightPercent = 0.2f;
    [Range(0, 1)]
    public float maxHeightPercent = 0.55f;
    [Range(0, 1)]
    [Tooltip("Same 0-1 slope scale the terrain shader uses.")]
    public float maxSlope = 0.35f;
    [Range(0, 1)]
    public float minMoisture = 0f;
    [Range(0, 1)]
    public float maxMoisture = 1f;

    [Header("Appearance")]
    public float minScale = 0.85f;
    public float maxScale = 1.25f;

    [Tooltip("How far the generated mesh is pushed into the terrain, in world units. This hides small height differences when the terrain changes LOD.")]
    [Min(0)]
    public float groundSink = 0.15f;

    [Range(0, 1)]
    [Tooltip("0 keeps the object upright; 1 aligns it completely to the terrain normal.")]
    public float surfaceAlignment = 0f;
}

[CreateAssetMenu()]
public class VegetationSettings : ScriptableObject
{
    public int seed = 0;

    public VegetationTypeSettings trees = new VegetationTypeSettings
    {
        cellSize = 6.5f,
        density = 0.82f,
        minHeightPercent = 0.38f,
        maxHeightPercent = 0.76f,
        maxSlope = 0.22f,
        maxMoisture = 1f,
        minScale = 0.85f,
        maxScale = 1.5f,
        groundSink = 0.5f,
        surfaceAlignment = 0f,
    };

    public VegetationTypeSettings grass = new VegetationTypeSettings
    {
        cellSize = 3.5f,
        density = 0.68f,
        minHeightPercent = 0.35f,
        maxHeightPercent = 0.88f,
        maxSlope = 0.4f,
        minScale = 0.7f,
        maxScale = 1.3f,
        groundSink = 0.04f,
        surfaceAlignment = 0.35f,
    };

    public VegetationTypeSettings rocks = new VegetationTypeSettings
    {
        cellSize = 16f,
        density = 0.3f,
        minHeightPercent = 0.35f,
        maxHeightPercent = 1f,
        maxSlope = 1f,
        minScale = 0.6f,
        maxScale = 2f,
        groundSink = 0.25f,
        surfaceAlignment = 0.85f,
    };

    [Header("Tree colours")]
    public Color trunkColour = new Color(0.28f, 0.19f, 0.13f);
    public Color foliageColourA = new Color(0.09f, 0.20f, 0.13f);
    public Color foliageColourB = new Color(0.16f, 0.30f, 0.18f);

    [Header("Grass colours")]
    public Color grassColourA = new Color(0.20f, 0.34f, 0.14f);
    public Color grassColourB = new Color(0.32f, 0.44f, 0.19f);

    [Header("Rock colours")]
    public Color rockColourA = new Color(0.42f, 0.42f, 0.40f);
    public Color rockColourB = new Color(0.55f, 0.53f, 0.50f);
}
