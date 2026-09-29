using UnityEngine;
using System.Collections.Generic;

public class TerrainGenerator : MonoBehaviour
{
    const float viewerMoveThresholdForChunkUpdate = 25f;
    public event System.Action<MeshCollider> onTerrainColliderReady;
    public event System.Action<TerrainChunk> onRenderMeshReady;

    public event System.Action<TerrainChunk> onTerrainRenderMeshReady;

    // Raised after GenerateTerrain replaces the terrain, so systems built on the old terrain
    // (navigation, food, animals) can rebuild.
    public event System.Action onTerrainGenerated;
    const float sqrViewerMoveThresholdForChunkUpdate =
        viewerMoveThresholdForChunkUpdate *
        viewerMoveThresholdForChunkUpdate;


    [Header("LOD")]
    public int colliderLODIndex;
    public LODInfo[] detailLevels;


    [Header("Terrain Settings")]
    public MeshSettings meshSettings;
    public HeightMapSettings heightMapSettings;
    public TextureData textureSettings;
    public VegetationSettings vegetationSettings;


    [Header("References")]
    public Transform viewer;

    public Material mapMaterial;
    public Material vegetationMaterial;


    [Header("Chunk Settings")]

    [Tooltip("Base name used for generated terrain chunks.")]
    public string chunkName = "Terrain Chunk";

    [Tooltip(
        "Unity layer assigned to generated terrain chunks. " +
        "Create this layer in Tags and Layers first."
    )]
    public string chunkLayerName = "AnimalSpawn";


    Vector2 viewerPosition;
    Vector2 viewerPositionOld;

    float meshWorldSize;

    int chunksVisibleInViewDst;

    int chunkLayer = -1;


    private bool terrainGenerated = false;


    Dictionary<Vector2Int, TerrainChunk>
        terrainChunkDictionary =
            new Dictionary<Vector2Int, TerrainChunk>();


    List<TerrainChunk>
        visibleTerrainChunks =
            new List<TerrainChunk>();


    TerrainEnvironmentSampler
        worldEnvironmentSampler;


    private HeightMapSettings
        originalHeightMapSettings;


    // Transparent water over every chunk that dips below the water level.
    // Null when TextureData has no water material.
    WaterSurface
        waterSurface;


    // The viewer every chunk currently judges its distance from.
    Transform
        chunkViewer;


    // ---------------------------------------------------------
    // PROPERTIES
    // ---------------------------------------------------------

    public EnvironmentDefinitions EnvironmentDefinitions
    {
        get
        {
            return textureSettings == null
                ? null
                : textureSettings.environmentDefinitions;
        }
    }


    public TerrainEnvironmentSampler WorldEnvironmentSampler
    {
        get
        {
            EnsureEnvironmentSampler();

            return worldEnvironmentSampler;
        }
    }


    public bool IsGenerated
    {
        get
        {
            return terrainGenerated;
        }
    }


    // World-space height of the water surface (the canonical water level).
    // Valid once terrain has been generated.
    public float WaterLevelHeight { get; private set; }


    // True while transparent water is drawn over the terrain
    // (TextureData has a water material).
    public bool HasWaterSurface
    {
        get
        {
            return waterSurface != null;
        }
    }


    // ---------------------------------------------------------
    // UNITY
    // ---------------------------------------------------------

    private void Awake()
    {
        originalHeightMapSettings =
            heightMapSettings;

        CacheChunkLayer();
    }


    private void OnDestroy()
    {
        // Also hands the water layers back to the terrain shader,
        // so editor previews show painted water again.
        waterSurface?.Dispose();
        waterSurface = null;
    }


    private void Update()
    {
        if (!terrainGenerated)
            return;

        if (viewer == null)
            return;


        // The viewer changed (GodCamera replaces the scene's player after the
        // first chunks exist). Chunks judge visibility and detail from their
        // own viewer, so hand them the new one and re-check them straight away.
        bool viewerChanged =
            viewer != chunkViewer;

        if (viewerChanged)
        {
            chunkViewer =
                viewer;

            foreach (TerrainChunk chunk
                     in terrainChunkDictionary.Values)
            {
                chunk.SetViewer(
                    viewer
                );
            }
        }


        viewerPosition =
            new Vector2(
                viewer.position.x,
                viewer.position.z
            );


        // Update terrain collision meshes when viewer moves.
        if (viewerChanged ||
            viewerPosition != viewerPositionOld)
        {
            foreach (
                TerrainChunk chunk
                in visibleTerrainChunks)
            {
                chunk.UpdateCollisionMesh();
            }
        }


        // Update visible chunks only after moving far enough.
        if (viewerChanged ||
            (viewerPositionOld - viewerPosition)
            .sqrMagnitude >
            sqrViewerMoveThresholdForChunkUpdate)
        {
            viewerPositionOld =
                viewerPosition;

            UpdateVisibleChunks();
        }
    }


    // ---------------------------------------------------------
    // CHUNK LAYER
    // ---------------------------------------------------------

    private void CacheChunkLayer()
    {
        chunkLayer =
            LayerMask.NameToLayer(
                chunkLayerName
            );

        if (chunkLayer == -1)
        {
            Debug.LogError(
                $"TerrainGenerator: Layer " +
                $"'{chunkLayerName}' does not exist.\n" +
                $"Create it in " +
                $"Edit > Project Settings > Tags and Layers."
            );
        }
    }


    private void ConfigureChunk(
        TerrainChunk chunk,
        Vector2Int coord)
    {
        if (chunk == null)
            return;

        GameObject chunkObject =
            chunk.GameObject;

        if (chunkObject == null)
        {
            Debug.LogError(
                $"TerrainGenerator: " +
                $"TerrainChunk {coord} has no GameObject."
            );

            return;
        }


        // Nice readable hierarchy name.
        chunkObject.name =
            $"{chunkName} ({coord.x}, {coord.y})";


        // Assign layer.
        if (chunkLayer >= 0)
        {
            chunkObject.layer =
                chunkLayer;
        }
    }


    // ---------------------------------------------------------
    // CHUNK GENERATION
    // ---------------------------------------------------------

    private void UpdateVisibleChunks()
    {
        HashSet<Vector2Int>
            alreadyUpdatedChunkCoords =
                new HashSet<Vector2Int>();


        // Update currently visible chunks.
        for (
            int i = visibleTerrainChunks.Count - 1;
            i >= 0;
            i--)
        {
            TerrainChunk chunk =
                visibleTerrainChunks[i];

            alreadyUpdatedChunkCoords.Add(
                chunk.coord
            );

            chunk.UpdateTerrainChunk();
        }


        Vector2Int currentChunkCoordinate =
            TerrainGrid.WorldToChunkCoordinate(
                viewerPosition,
                meshWorldSize
            );


        for (
            int yOffset =
                -chunksVisibleInViewDst;
            yOffset <=
                chunksVisibleInViewDst;
            yOffset++)
        {
            for (
                int xOffset =
                    -chunksVisibleInViewDst;
                xOffset <=
                    chunksVisibleInViewDst;
                xOffset++)
            {
                Vector2Int viewedChunkCoord =
                    new Vector2Int(
                        currentChunkCoordinate.x +
                        xOffset,

                        currentChunkCoordinate.y +
                        yOffset
                    );


                if (alreadyUpdatedChunkCoords
                    .Contains(viewedChunkCoord))
                {
                    continue;
                }


                if (terrainChunkDictionary
                    .TryGetValue(
                        viewedChunkCoord,
                        out TerrainChunk existingChunk))
                {
                    existingChunk
                        .UpdateTerrainChunk();

                    continue;
                }


                // -------------------------------------
                // CREATE NEW CHUNK
                // -------------------------------------

                TerrainChunk newChunk =
                    new TerrainChunk(
                        viewedChunkCoord,
                        heightMapSettings,
                        meshSettings,
                        detailLevels,
                        colliderLODIndex,
                        transform,
                        viewer,
                        mapMaterial,
                        vegetationSettings,
                        vegetationMaterial,
                        EnvironmentDefinitions,
                        WorldEnvironmentSampler
                    );


                // Set name and layer immediately.
                ConfigureChunk(
                    newChunk,
                    viewedChunkCoord
                );


                terrainChunkDictionary.Add(
                    viewedChunkCoord,
                    newChunk
                );


                newChunk.onVisibilityChanged +=
                    OnTerrainChunkVisibilityChanged;


                newChunk.onColliderReady += OnTerrainChunkColliderReady;

                newChunk.onRenderMeshReady += OnTerrainChunkRenderMeshReady;
                newChunk.Load();
            }
        }
    }

    private void OnTerrainChunkRenderMeshReady(TerrainChunk chunk)
    {
        if (chunk == null)
        {
            return;
        }

        waterSurface?.AddTileIfNeeded(chunk);

        onTerrainRenderMeshReady?.Invoke(chunk);
    }
    private void OnTerrainChunkVisibilityChanged(
        TerrainChunk chunk,
        bool isVisible)
    {
        if (isVisible)
        {
            if (!visibleTerrainChunks
                .Contains(chunk))
            {
                visibleTerrainChunks.Add(
                    chunk
                );
            }

            // Animals require terrain physics.
            chunk.UpdateCollisionMesh();
        }
        else
        {
            visibleTerrainChunks.Remove(
                chunk
            );
        }
    }
    private void OnTerrainChunkColliderReady(
    TerrainChunk chunk,
    MeshCollider meshCollider)
    {
        if (meshCollider == null)
        {
            return;
        }

        onTerrainColliderReady?.Invoke(meshCollider);
    }

    // ---------------------------------------------------------
    // ENVIRONMENT
    // ---------------------------------------------------------

    public bool TryGetEnvironmentSample(
        Vector3 worldPosition,
        out EnvironmentSample sample)
    {
        return WorldEnvironmentSampler
            .TrySample(
                worldPosition,
                out sample
            );
    }


    public bool TryGetEnvironmentSample(
        Vector2 worldPosition,
        out EnvironmentSample sample)
    {
        return WorldEnvironmentSampler
            .TrySample(
                worldPosition,
                out sample
            );
    }


    private void EnsureEnvironmentSampler()
    {
        EnvironmentDefinitions definitions =
            EnvironmentDefinitions;


        if (
            worldEnvironmentSampler != null &&
            worldEnvironmentSampler
                .HeightMapSettings ==
                heightMapSettings &&

            worldEnvironmentSampler
                .MeshSettings ==
                meshSettings &&

            worldEnvironmentSampler
                .EnvironmentDefinitions ==
                definitions &&

            worldEnvironmentSampler
                .VegetationSettings ==
                vegetationSettings)
        {
            return;
        }


        // Animals and plants sample terrain well beyond the camera's view, so cache far more
        // chunks than the default (about 34 KB each).
        worldEnvironmentSampler =
            new TerrainEnvironmentSampler(
                heightMapSettings,
                meshSettings,
                definitions,
                vegetationSettings,
                1024
            );
    }


    // ---------------------------------------------------------
    // TERRAIN GENERATION
    // ---------------------------------------------------------

    public void GenerateTerrain(
        int terrainSeed)
    {
        Debug.Log(
            $"Generating terrain with seed: " +
            $"{terrainSeed}"
        );


        ClearTerrain();


        // Refresh in case you changed the layer
        // while testing in the editor.
        CacheChunkLayer();


        // Runtime copy.
        heightMapSettings =
            Instantiate(
                originalHeightMapSettings
            );


        // Apply deterministic terrain seed.
        heightMapSettings
            .noiseSettings
            .seed =
                terrainSeed;


        EnsureEnvironmentSampler();


        textureSettings.ApplyToMaterial(
            mapMaterial
        );


        textureSettings.UpdateMeshHeights(
            mapMaterial,
            heightMapSettings.minHeight,
            heightMapSettings.maxHeight
        );


        // Before any chunk exists, so every chunk gets its water.
        CreateWaterSurface();


        float maxViewDst =
            detailLevels[
                detailLevels.Length - 1
            ]
            .visibleDstThreshold;


        meshWorldSize =
            meshSettings.meshWorldSize;


        chunksVisibleInViewDst =
            Mathf.RoundToInt(
                maxViewDst /
                meshWorldSize
            );


        viewerPosition =
            new Vector2(
                viewer.position.x,
                viewer.position.z
            );


        viewerPositionOld =
            viewerPosition;


        // New chunks are created with the current viewer.
        chunkViewer =
            viewer;


        terrainGenerated =
            true;


        UpdateVisibleChunks();

        onTerrainGenerated?.Invoke();
    }


    // ---------------------------------------------------------
    // WATER
    // ---------------------------------------------------------

    private void CreateWaterSurface()
    {
        waterSurface?.Dispose();
        waterSurface = null;

        if (EnvironmentDefinitions == null)
            return;


        // Same level the environment classifies as water
        // (the terrain shader's normalizedWaterLevel).
        WaterLevelHeight =
            Mathf.Lerp(
                heightMapSettings.minHeight,
                heightMapSettings.maxHeight,
                EnvironmentDefinitions.ShorelineThreshold
            );


        if (textureSettings.waterMaterial != null)
        {
            waterSurface =
                new WaterSurface(
                    textureSettings.waterMaterial,
                    WaterLevelHeight,
                    meshSettings.meshWorldSize
                );
        }
    }


    // ---------------------------------------------------------
    // CLEAR TERRAIN
    // ---------------------------------------------------------

    private void ClearTerrain()
    {
        foreach (Transform child
                 in transform)
        {
            Destroy(
                child.gameObject
            );
        }


        terrainChunkDictionary.Clear();

        visibleTerrainChunks.Clear();


        worldEnvironmentSampler =
            null;


        terrainGenerated =
            false;
    }


    // ---------------------------------------------------------
    // ANIMAL SPAWNING
    // ---------------------------------------------------------


    public List<TerrainChunk> GetVisibleTerrainChunks()
    {
        return new List<TerrainChunk>(visibleTerrainChunks);
    }

    public List<MeshCollider>
        GetSpawnableTerrainColliders()
    {
        List<MeshCollider> result =
            new List<MeshCollider>();


        if (chunkLayer < 0)
        {
            CacheChunkLayer();

            if (chunkLayer < 0)
            {
                return result;
            }
        }


        MeshCollider[] colliders =
            GetComponentsInChildren<
                MeshCollider
            >(true);


        foreach (
            MeshCollider col
            in colliders)
        {
            if (col == null)
                continue;


            if (!col.gameObject
                .activeInHierarchy)
            {
                continue;
            }


            if (!col.enabled)
                continue;


            if (col.sharedMesh == null)
                continue;


            // --------------------------------
            // IMPORTANT:
            // Only AnimalSpawn terrain chunks
            // --------------------------------

            if (col.gameObject.layer !=
                chunkLayer)
            {
                continue;
            }


            // Ignore small mesh colliders,
            // e.g. vegetation.
            if (meshWorldSize > 0f)
            {
                if (
                    col.bounds.size.x <
                    meshWorldSize * 0.5f ||

                    col.bounds.size.z <
                    meshWorldSize * 0.5f)
                {
                    continue;
                }
            }


            result.Add(col);
        }


        return result;
    }


    public bool HasSpawnableTerrain()
    {
        return
            GetSpawnableTerrainColliders()
                .Count > 0;
    }
}


[System.Serializable]
public struct LODInfo
{
    [Range(
        0,
        MeshSettings.numSupportedLODs - 1
    )]
    public int lod;


    public float
        visibleDstThreshold;


    public float sqrVisibleDstThreshold
    {
        get
        {
            return
                visibleDstThreshold *
                visibleDstThreshold;
        }
    }
}