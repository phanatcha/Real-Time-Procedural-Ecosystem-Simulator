using UnityEngine;

public class AutoTerrainBorders : MonoBehaviour
{
    [Header("Terrain Settings")]
    public Terrain activeTerrain;
    
    [Header("Border Dimensions")]
    public float wallHeight = 100f;
    public float wallThickness = 5f;

    void Start()
    {
        if (activeTerrain == null)
        {
            activeTerrain = Terrain.activeTerrain;
        }

        if (activeTerrain != null)
        {
            GenerateBorders();
        }
        else
        {
            Debug.LogError("No terrain found to build borders around!");
        }
    }

    void GenerateBorders()
    {
        TerrainData tData = activeTerrain.terrainData;
        Vector3 tPos = activeTerrain.transform.position;

        float width = tData.size.x;
        float length = tData.size.z;
        float halfHeight = wallHeight / 2f;
        
        float inset = 5f; 

        CreateWall("Border_North", new Vector3(tPos.x + width / 2, tPos.y + halfHeight, tPos.z + length - inset), new Vector3(width, wallHeight, wallThickness));
        CreateWall("Border_South", new Vector3(tPos.x + width / 2, tPos.y + halfHeight, tPos.z + inset), new Vector3(width, wallHeight, wallThickness));
        CreateWall("Border_East", new Vector3(tPos.x + width - inset, tPos.y + halfHeight, tPos.z + length / 2), new Vector3(wallThickness, wallHeight, length));
        CreateWall("Border_West", new Vector3(tPos.x + inset, tPos.y + halfHeight, tPos.z + length / 2), new Vector3(wallThickness, wallHeight, length));
    }

    void CreateWall(string wallName, Vector3 position, Vector3 scale)
    {
        GameObject wall = new GameObject(wallName);
        wall.transform.position = position;
        
        BoxCollider col = wall.AddComponent<BoxCollider>();
        col.size = scale;
        col.isTrigger = true;

        wall.tag = "Border";

        wall.transform.parent = this.transform;
    }
}