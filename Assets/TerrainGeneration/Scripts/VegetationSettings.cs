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

    [Tooltip("Only drawn on chunks at this terrain detail level or finer (the LOD numbers in TerrainGenerator's " +
             "detail levels; 0 is the nearest). Small, numerous plants can stop at 1, about 200 m away.")]
    [Min(0)]
    public int maxLevelOfDetail = 4;
}

[CreateAssetMenu()]
public class VegetationSettings : ScriptableObject
{
    [Tooltip("Replaced by a seed from the world seed when SeedManager generates the world.")]
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

    // Berry bushes scattered over the forest floor and clearings, as in Valheim: bilberry on moist ground, raspberry
    // on drier, sunnier ground. Scenery; the edible ones are FoodSpawner's ground plants, drawn as the same bushes.
    public VegetationTypeSettings bushes = new VegetationTypeSettings
    {
        cellSize = 6f,
        density = 0.55f,
        minHeightPercent = 0.38f,
        maxHeightPercent = 0.8f,
        maxSlope = 0.3f,
        minScale = 0.8f,
        maxScale = 1.25f,
        groundSink = 0.1f,
        surfaceAlignment = 0.3f,
        maxLevelOfDetail = 1,
    };

    [Tooltip("Chunks at this terrain detail level or coarser draw simpler spruces (the LOD numbers in " +
             "TerrainGenerator's detail levels; 2 is about 200 m away).")]
    [Min(0)]
    public int farTreeLevelOfDetail = 2;

    [Header("Tree colours")]
    public Color trunkColour = new Color(0.28f, 0.19f, 0.13f);
    public Color foliageColourA = new Color(0.09f, 0.20f, 0.13f);
    public Color foliageColourB = new Color(0.16f, 0.30f, 0.18f);
    [Tooltip("Bright new growth on the spruces animals feed from (FoodSpawner's tall food).")]
    public Color freshShootColour = new Color(0.56f, 0.8f, 0.26f);

    [Header("Grass colours")]
    public Color grassColourA = new Color(0.20f, 0.34f, 0.14f);
    public Color grassColourB = new Color(0.32f, 0.44f, 0.19f);

    [Header("Rock colours")]
    public Color rockColourA = new Color(0.42f, 0.42f, 0.40f);
    public Color rockColourB = new Color(0.55f, 0.53f, 0.50f);
    [Header("Berry bush colours")]
    [Tooltip("Darker and richer than the grass, so bushes stand out from the meadow around them.")]
    public Color bushLeafColour = new Color(0.13f, 0.31f, 0.15f);
    public Color bilberryColour = new Color(0.3f, 0.36f, 0.85f);
    public Color raspberryColour = new Color(0.93f, 0.16f, 0.24f);

    [Tooltip("Moss on the upper faces of rocks.")]
    public Color rockMossColour = new Color(0.3f, 0.42f, 0.18f);
}
