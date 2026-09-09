using UnityEngine;

public class AutoTerrainBorders : MonoBehaviour
{
    [Header("Terrain Settings")]
    public Terrain activeTerrain;
    public AnimalTerrainWorld proceduralTerrain;

    [Header("Border Dimensions")]
    public float wallHeight = 100f;
    public float wallThickness = 5f;

    void Start()
    {
        if (proceduralTerrain == null) proceduralTerrain = AnimalTerrainWorld.Active;
        if (proceduralTerrain != null && proceduralTerrain.IsConfigured)
        {
            GenerateProceduralBorders();
            return;
        }

        if (activeTerrain == null) activeTerrain = Terrain.activeTerrain;
        if (activeTerrain != null)
        {
            GenerateTerrainBorders();
        }
        else
        {
            Debug.LogWarning("No terrain source was found for animal borders.", this);
        }
    }

    void GenerateProceduralBorders()
    {
        float extent = Mathf.Max(1f, proceduralTerrain.WorldRadius - proceduralTerrain.boundaryInset);
        float size = extent * 2f;
        HeightMapSettings settings = proceduralTerrain.terrainGenerator.heightMapSettings;
        float totalHeight = Mathf.Max(wallHeight, settings.maxHeight - settings.minHeight + wallHeight);
        float centerY = (settings.minHeight + settings.maxHeight) * 0.5f;

        CreateOrUpdateWall("Border_North", new Vector3(0f, centerY, extent),
            new Vector3(size, totalHeight, wallThickness));
        CreateOrUpdateWall("Border_South", new Vector3(0f, centerY, -extent),
            new Vector3(size, totalHeight, wallThickness));
        CreateOrUpdateWall("Border_East", new Vector3(extent, centerY, 0f),
            new Vector3(wallThickness, totalHeight, size));
        CreateOrUpdateWall("Border_West", new Vector3(-extent, centerY, 0f),
            new Vector3(wallThickness, totalHeight, size));
    }

    void GenerateTerrainBorders()
    {
        TerrainData terrainData = activeTerrain.terrainData;
        Vector3 terrainPosition = activeTerrain.transform.position;
        float width = terrainData.size.x;
        float length = terrainData.size.z;
        float halfHeight = wallHeight * 0.5f;
        const float inset = 5f;

        CreateOrUpdateWall("Border_North",
            new Vector3(terrainPosition.x + width * 0.5f, terrainPosition.y + halfHeight,
                terrainPosition.z + length - inset),
            new Vector3(width, wallHeight, wallThickness));
        CreateOrUpdateWall("Border_South",
            new Vector3(terrainPosition.x + width * 0.5f, terrainPosition.y + halfHeight,
                terrainPosition.z + inset),
            new Vector3(width, wallHeight, wallThickness));
        CreateOrUpdateWall("Border_East",
            new Vector3(terrainPosition.x + width - inset, terrainPosition.y + halfHeight,
                terrainPosition.z + length * 0.5f),
            new Vector3(wallThickness, wallHeight, length));
        CreateOrUpdateWall("Border_West",
            new Vector3(terrainPosition.x + inset, terrainPosition.y + halfHeight,
                terrainPosition.z + length * 0.5f),
            new Vector3(wallThickness, wallHeight, length));
    }

    void CreateOrUpdateWall(string wallName, Vector3 position, Vector3 size)
    {
        Transform existing = transform.Find(wallName);
        GameObject wall = existing == null ? new GameObject(wallName) : existing.gameObject;
        wall.transform.SetParent(transform, true);
        wall.transform.position = position;
        wall.transform.rotation = Quaternion.identity;
        wall.transform.localScale = Vector3.one;

        BoxCollider collider = wall.GetComponent<BoxCollider>();
        if (collider == null) collider = wall.AddComponent<BoxCollider>();
        collider.size = size;
        collider.isTrigger = true;
        wall.tag = "Border";
    }
}
