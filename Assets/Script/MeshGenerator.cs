using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[ExecuteInEditMode]
public class MeshGenerator : MonoBehaviour
{
    Mesh mesh;
    Vector3[] vertices;
    int[] triangles;
    Color[] colors;

    public int xSize = 80;
    public int zSize = 80;
    public float cellSize = 1.5f;      
    public float noiseScale = 0.03f;   
    public float heightMultiplier = 5f;
    public Vector2 noiseOffset;        
    public Gradient gradient;

    float minTerrainHeight;
    float maxTerrainHeight;

    void Start() => InitializeMesh();
    void OnValidate() => InitializeMesh();

    void InitializeMesh()
    {
        if (mesh == null) mesh = new Mesh();
        GetComponent<MeshFilter>().sharedMesh = mesh;
        CreateShape();
        UpdateMesh();
    }

    void CreateShape()
    {
        minTerrainHeight = float.MaxValue;
        maxTerrainHeight = float.MinValue;

        vertices = new Vector3[(xSize + 1) * (zSize + 1)];

        for (int i = 0, z = 0; z <= zSize; z++)
        {
            for (int x = 0; x <= xSize; x++)
            {
                float y = GetFBMSample(x, z);
                vertices[i] = new Vector3(x * cellSize, y, z * cellSize);

                if (y > maxTerrainHeight) maxTerrainHeight = y;
                if (y < minTerrainHeight) minTerrainHeight = y;
                i++;
            }
        }

        triangles = new int[xSize * zSize * 6];
        int vert = 0, tris = 0;

        for (int z = 0; z < zSize; z++)
        {
            for (int x = 0; x < xSize; x++)
            {
                triangles[tris + 0] = vert + 0;
                triangles[tris + 1] = vert + xSize + 1;
                triangles[tris + 2] = vert + 1;
                triangles[tris + 3] = vert + 1;
                triangles[tris + 4] = vert + xSize + 1;
                triangles[tris + 5] = vert + xSize + 2;
                vert++;
                tris += 6;
            }
            vert++;
        }

        colors = new Color[vertices.Length];
        for (int i = 0, z = 0; z <= zSize; z++)
        {
            for (int x = 0; x <= xSize; x++)
            {
                float height = Mathf.InverseLerp(minTerrainHeight, maxTerrainHeight, vertices[i].y);
                colors[i] = gradient.Evaluate(height);
                i++;
            }
        }
    }

    void UpdateMesh()
    {
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.colors = colors;
        mesh.RecalculateNormals();
    }

    float GetFBMSample(int x, int z)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float total = 0f;
        float maxValue = 0f;

        for (int o = 0; o < 4; o++)
        {
            float sx = (x * noiseScale + noiseOffset.x) * frequency;
            float sz = (z * noiseScale + noiseOffset.y) * frequency;
            total += Mathf.PerlinNoise(sx, sz) * amplitude;
            maxValue += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }

        return (total / maxValue) * heightMultiplier;
    }
}