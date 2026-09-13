using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class AnimalSpawnHeatmap : MonoBehaviour
{
    [Header("References")]
    public TerrainGenerator terrainGenerator;
    public AnimalSpawner animalSpawner;
    public Material heatmapMaterial;

    [Header("Heatmap Settings")]
    public string animalSpawnLayerName = "AnimalSpawn";
    public float heightOffset = 0.2f;

    private readonly Dictionary<TerrainChunk, GameObject> heatmapObjects =
        new Dictionary<TerrainChunk, GameObject>();

    private int animalSpawnLayer = -1;

    private void OnEnable()
    {
        if (terrainGenerator != null)
        {
            terrainGenerator.onTerrainRenderMeshReady += HandleTerrainMeshReady;
        }
    }

    private void OnDisable()
    {
        if (terrainGenerator != null)
        {
            terrainGenerator.onTerrainRenderMeshReady -= HandleTerrainMeshReady;
        }
    }

    public void GenerateHeatmap()
    {
        if (terrainGenerator == null)
        {
            Debug.LogError("AnimalSpawnHeatmap: TerrainGenerator is not assigned!");
            return;
        }

        if (animalSpawner == null)
        {
            Debug.LogError("AnimalSpawnHeatmap: AnimalSpawner is not assigned!");
            return;
        }

        if (heatmapMaterial == null)
        {
            Debug.LogError("AnimalSpawnHeatmap: Heatmap material is not assigned!");
            return;
        }

        animalSpawnLayer = LayerMask.NameToLayer(animalSpawnLayerName);

        if (animalSpawnLayer == -1)
        {
            Debug.LogError($"AnimalSpawnHeatmap: Layer '{animalSpawnLayerName}' does not exist.");
            return;
        }

        ClearHeatmap();

        List<TerrainChunk> chunks = terrainGenerator.GetVisibleTerrainChunks();

        foreach (TerrainChunk chunk in chunks)
        {
            if (chunk == null)
            {
                continue;
            }

            if (chunk.CurrentMesh == null)
            {
                continue;
            }

            GenerateHeatmapForChunk(chunk);
        }

        Debug.Log($"AnimalSpawnHeatmap: Generated heatmap for {heatmapObjects.Count} chunks.");
    }

    private void HandleTerrainMeshReady(TerrainChunk chunk)
    {
        if (chunk == null)
        {
            return;
        }

        if (!chunk.IsVisible())
        {
            return;
        }

        if (chunk.CurrentMesh == null)
        {
            return;
        }

        if (animalSpawnLayer == -1)
        {
            animalSpawnLayer = LayerMask.NameToLayer(animalSpawnLayerName);
        }

        GenerateHeatmapForChunk(chunk);
    }

    private void GenerateHeatmapForChunk(TerrainChunk chunk)
    {
        Mesh sourceMesh = chunk.CurrentMesh;

        if (sourceMesh == null)
        {
            return;
        }

        Vector3[] vertices = sourceMesh.vertices;
        Vector3[] normals = sourceMesh.normals;
        int[] triangles = sourceMesh.triangles;

        if (vertices == null || vertices.Length == 0)
        {
            return;
        }

        if (normals == null || normals.Length != vertices.Length)
        {
            Debug.LogWarning($"AnimalSpawnHeatmap: {chunk.GameObject.name} has invalid normals.");
            return;
        }

        Vector3[] overlayVertices = new Vector3[vertices.Length];
        Color[] colors = new Color[vertices.Length];

        bool isAnimalSpawnChunk = chunk.GameObject.layer == animalSpawnLayer;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 localNormal = normals[i].normalized;

            // Push the overlay slightly above the terrain.
            overlayVertices[i] = vertices[i] + localNormal * heightOffset;

            Vector3 worldPosition = chunk.ChunkTransform.TransformPoint(vertices[i]);
            Vector3 worldNormal = chunk.ChunkTransform.TransformDirection(localNormal).normalized;

            float suitability = 0f;

            if (isAnimalSpawnChunk)
            {
                suitability = animalSpawner.GetSpawnSuitability(
                    worldPosition.y,
                    worldNormal
                );
            }

            colors[i] = GetHeatColor(suitability);
        }

        Mesh heatMesh = new Mesh();

        heatMesh.name = $"Heatmap Mesh {chunk.coord}";
        heatMesh.indexFormat = sourceMesh.indexFormat;

        heatMesh.vertices = overlayVertices;
        heatMesh.triangles = triangles;
        heatMesh.normals = normals;
        heatMesh.colors = colors;

        heatMesh.RecalculateBounds();

        GameObject heatmapObject;

        if (heatmapObjects.TryGetValue(chunk, out GameObject existingObject))
        {
            heatmapObject = existingObject;

            MeshFilter existingFilter = heatmapObject.GetComponent<MeshFilter>();

            if (existingFilter.sharedMesh != null)
            {
                Destroy(existingFilter.sharedMesh);
            }
        }
        else
        {
            heatmapObject = new GameObject($"Heatmap {chunk.coord}");

            heatmapObject.transform.SetParent(chunk.ChunkTransform, false);
            heatmapObject.transform.localPosition = Vector3.zero;
            heatmapObject.transform.localRotation = Quaternion.identity;
            heatmapObject.transform.localScale = Vector3.one;

            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");

            if (ignoreRaycastLayer != -1)
            {
                heatmapObject.layer = ignoreRaycastLayer;
            }

            heatmapObject.AddComponent<MeshFilter>();

            MeshRenderer renderer = heatmapObject.AddComponent<MeshRenderer>();

            renderer.sharedMaterial = heatmapMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            heatmapObjects.Add(chunk, heatmapObject);
        }

        MeshFilter meshFilter = heatmapObject.GetComponent<MeshFilter>();
        meshFilter.sharedMesh = heatMesh;
    }

    private Color GetHeatColor(float suitability)
    {
        suitability = Mathf.Clamp01(suitability);

        if (suitability <= 0.5f)
        {
            return Color.Lerp(
                Color.red,
                Color.yellow,
                suitability * 2f
            );
        }

        return Color.Lerp(
            Color.yellow,
            Color.green,
            (suitability - 0.5f) * 2f
        );
    }

    public void ClearHeatmap()
    {
        foreach (KeyValuePair<TerrainChunk, GameObject> pair in heatmapObjects)
        {
            GameObject heatmapObject = pair.Value;

            if (heatmapObject == null)
            {
                continue;
            }

            MeshFilter filter = heatmapObject.GetComponent<MeshFilter>();

            if (filter != null && filter.sharedMesh != null)
            {
                Destroy(filter.sharedMesh);
            }

            Destroy(heatmapObject);
        }

        heatmapObjects.Clear();
    }
}