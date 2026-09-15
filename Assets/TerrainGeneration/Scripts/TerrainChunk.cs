using UnityEngine;

public class TerrainChunk
{
    public event System.Action<TerrainChunk, bool> onVisibilityChanged;


    public event System.Action<TerrainChunk, MeshCollider> onColliderReady;

    public event System.Action<TerrainChunk> onRenderMeshReady;
    public Vector2Int coord;

    // ---------------------------------------------------------
    // PUBLIC ACCESS
    // ---------------------------------------------------------

    public GameObject GameObject
    {
        get { return meshObject; }
    }

    public MeshCollider MeshCollider
    {
        get { return meshCollider; }
    }
    public Mesh CurrentMesh
    {
        get { return meshFilter.sharedMesh; }
    }

    public Transform ChunkTransform
    {
        get { return meshObject.transform; }
    }
    public Bounds WorldBounds
    {
        get
        {
            return meshCollider != null && meshCollider.sharedMesh != null
            ? meshCollider.bounds
            : bounds;
        }
    }


    // ---------------------------------------------------------
    // CHUNK OBJECTS
    // ---------------------------------------------------------

    private GameObject meshObject;

    private MeshRenderer meshRenderer;
    private MeshFilter meshFilter;
    private MeshCollider meshCollider;


    // ---------------------------------------------------------
    // TERRAIN DATA
    // ---------------------------------------------------------

    private Vector2 sampleCentre;
    private Vector2 position;

    private Bounds bounds;

    private LODInfo[] detailLevels;
    private LODMesh[] lodMeshes;

    private int colliderLODIndex;

    private HeightMap heightMap;

    private bool heightMapReceived;
    private bool hasSetCollider;

    private int previousLODIndex = -1;

    private float maxViewDst;


    // ---------------------------------------------------------
    // SETTINGS
    // ---------------------------------------------------------

    private HeightMapSettings heightMapSettings;
    private MeshSettings meshSettings;

    private Transform viewer;


    // ---------------------------------------------------------
    // VEGETATION
    // ---------------------------------------------------------

    private VegetationSettings vegetationSettings;
    private Material vegetationMaterial;

    private EnvironmentDefinitions environmentDefinitions;
    private TerrainEnvironmentSampler environmentSampler;

    private bool vegetationRequested;

    private VegetationPlacementData vegetationData;

    private GameObject vegetationObject;

    private MeshFilter treesMeshFilter;
    private MeshFilter grassMeshFilter;
    private MeshFilter rocksMeshFilter;

    private int vegetationLODIndex = -1;


    // ---------------------------------------------------------
    // CONSTRUCTOR
    // ---------------------------------------------------------

    public TerrainChunk(
        Vector2Int coord,
        HeightMapSettings heightMapSettings,
        MeshSettings meshSettings,
        LODInfo[] detailLevels,
        int colliderLODIndex,
        Transform parent,
        Transform viewer,
        Material material,
        VegetationSettings vegetationSettings,
        Material vegetationMaterial,
        EnvironmentDefinitions environmentDefinitions,
        TerrainEnvironmentSampler environmentSampler)
    {
        this.coord = coord;

        this.detailLevels = detailLevels;

        this.colliderLODIndex =
            colliderLODIndex;

        this.heightMapSettings =
            heightMapSettings;

        this.meshSettings =
            meshSettings;

        this.viewer =
            viewer;

        this.vegetationSettings =
            vegetationSettings;

        this.vegetationMaterial =
            vegetationMaterial;

        this.environmentDefinitions =
            environmentDefinitions;

        this.environmentSampler =
            environmentSampler;


        sampleCentre =
            (Vector2)coord *
            meshSettings.meshWorldSize /
            meshSettings.meshScale;


        position =
            (Vector2)coord *
            meshSettings.meshWorldSize;


        bounds =
            new Bounds(
                position,
                Vector2.one *
                meshSettings.meshWorldSize
            );


        // -----------------------------------------------------
        // CREATE CHUNK GAMEOBJECT
        // -----------------------------------------------------

        meshObject =
            new GameObject(
                $"Terrain Chunk ({coord.x}, {coord.y})"
            );


        meshRenderer =
            meshObject.AddComponent<MeshRenderer>();


        meshFilter =
            meshObject.AddComponent<MeshFilter>();


        meshCollider =
            meshObject.AddComponent<MeshCollider>();


        meshRenderer.sharedMaterial =
            material;


        meshObject.transform.position =
            new Vector3(
                position.x,
                0f,
                position.y
            );


        meshObject.transform.SetParent(
            parent,
            true
        );


        SetVisible(false);


        // -----------------------------------------------------
        // LOD
        // -----------------------------------------------------

        lodMeshes =
            new LODMesh[
                detailLevels.Length
            ];


        for (int i = 0;
             i < detailLevels.Length;
             i++)
        {
            lodMeshes[i] =
                new LODMesh(
                    detailLevels[i].lod
                );


            lodMeshes[i].updateCallback +=
                UpdateTerrainChunk;


            if (i == colliderLODIndex)
            {
                lodMeshes[i].updateCallback +=
                    UpdateCollisionMesh;
            }
        }


        maxViewDst =
            detailLevels[
                detailLevels.Length - 1
            ]
            .visibleDstThreshold;
    }


    // ---------------------------------------------------------
    // LOAD
    // ---------------------------------------------------------

    public void Load()
    {
        ThreadedDataRequester.RequestData(
            () =>
                HeightMapGenerator.GenerateHeightMap(
                    meshSettings.numVertsPerLine,
                    meshSettings.numVertsPerLine,
                    heightMapSettings,
                    sampleCentre
                ),

            OnHeightMapReceived
        );
    }


    private void OnHeightMapReceived(
        object heightMapObject)
    {
        heightMap =
            (HeightMap)heightMapObject;


        environmentSampler?.CacheHeightMap(
            coord,
            heightMap
        );


        heightMapReceived =
            true;


        UpdateTerrainChunk();
    }


    // ---------------------------------------------------------
    // VIEWER
    // ---------------------------------------------------------

    private Vector2 viewerPosition
    {
        get
        {
            return new Vector2(
                viewer.position.x,
                viewer.position.z
            );
        }
    }


    // ---------------------------------------------------------
    // TERRAIN LOD
    // ---------------------------------------------------------

    public void UpdateTerrainChunk()
    {
        if (!heightMapReceived)
            return;


        float viewerDstFromNearestEdge =
            Mathf.Sqrt(
                bounds.SqrDistance(
                    viewerPosition
                )
            );


        bool wasVisible =
            IsVisible();


        bool visible =
            viewerDstFromNearestEdge <=
            maxViewDst;


        if (visible)
        {
            int lodIndex =
                0;


            for (int i = 0;
                 i <
                 detailLevels.Length - 1;
                 i++)
            {
                if (viewerDstFromNearestEdge >
                    detailLevels[i]
                        .visibleDstThreshold)
                {
                    lodIndex =
                        i + 1;
                }
                else
                {
                    break;
                }
            }


            if (lodIndex !=
                previousLODIndex)
            {
                LODMesh lodMesh =
                    lodMeshes[
                        lodIndex
                    ];


                if (lodMesh.hasMesh)
                {
                    previousLODIndex =
                        lodIndex;


                    meshFilter.sharedMesh =
                        lodMesh.mesh;

                    onRenderMeshReady?.Invoke(this);


                    UpdateVegetationForLOD(
                        lodIndex
                    );
                }
                else if (
                    !lodMesh.hasRequestedMesh)
                {
                    lodMesh.RequestMesh(
                        heightMap,
                        meshSettings
                    );
                }
            }


            // Important:
            // make sure collider generation is requested
            // whenever the chunk is visible.
            UpdateCollisionMesh();
        }


        if (wasVisible != visible)
        {
            SetVisible(
                visible
            );


            onVisibilityChanged?.Invoke(
                this,
                visible
            );
        }


        if (visible &&
            !vegetationRequested &&
            vegetationSettings != null &&
            vegetationMaterial != null)
        {
            vegetationRequested =
                true;


            RequestVegetation();
        }
    }


    // ---------------------------------------------------------
    // COLLIDER
    // ---------------------------------------------------------

    public void UpdateCollisionMesh()
    {
        if (hasSetCollider)
        {
            return;
        }

        if (!heightMapReceived)
        {
            return;
        }

        float sqrDistance = bounds.SqrDistance(viewerPosition);

        float colliderDistance =
            detailLevels[colliderLODIndex].visibleDstThreshold;

        float colliderDistanceSqr =
            colliderDistance * colliderDistance;

        if (sqrDistance > colliderDistanceSqr)
        {
            return;
        }

        LODMesh colliderLODMesh = lodMeshes[colliderLODIndex];

        if (!colliderLODMesh.hasRequestedMesh)
        {
            colliderLODMesh.RequestMesh(heightMap, meshSettings);
            return;
        }

        if (!colliderLODMesh.hasMesh)
        {
            return;
        }

        meshCollider.sharedMesh = colliderLODMesh.mesh;
        hasSetCollider = true;

        onColliderReady?.Invoke(this, meshCollider);
    }

    // ---------------------------------------------------------
    // VEGETATION
    // ---------------------------------------------------------

    private void RequestVegetation()
    {
        Vector2 halfSize =
            Vector2.one *
            (
                meshSettings.meshWorldSize /
                2f
            );


        Vector2 chunkWorldMin =
            position -
            halfSize;


        Vector2 chunkWorldMax =
            position +
            halfSize;


        float terrainMinHeight =
            heightMapSettings.minHeight;


        float terrainMaxHeight =
            heightMapSettings.maxHeight;


        ThreadedDataRequester.RequestData(
            () =>
                VegetationGenerator.GeneratePlacements(
                    chunkWorldMin,
                    chunkWorldMax,
                    heightMap,
                    terrainMinHeight,
                    terrainMaxHeight,
                    vegetationSettings,
                    environmentDefinitions,
                    meshSettings.numVertsPerLine,
                    meshSettings.meshScale
                ),

            OnVegetationDataReceived
        );
    }


    private void OnVegetationDataReceived(
        object dataObject)
    {
        vegetationData =
            (VegetationPlacementData)
            dataObject;


        VegetationTemplateCache
            .EnsureBuilt(
                vegetationSettings
            );


        vegetationObject =
            new GameObject(
                "Vegetation"
            );


        vegetationObject.transform
            .SetParent(
                meshObject.transform,
                false
            );


        treesMeshFilter =
            AddVegetationLayer(
                vegetationObject.transform,
                "Trees"
            );


        grassMeshFilter =
            AddVegetationLayer(
                vegetationObject.transform,
                "Grass"
            );


        rocksMeshFilter =
            AddVegetationLayer(
                vegetationObject.transform,
                "Rocks"
            );


        if (previousLODIndex >= 0)
        {
            UpdateVegetationForLOD(
                previousLODIndex
            );
        }
    }


    private MeshFilter AddVegetationLayer(
        Transform parent,
        string layerName)
    {
        GameObject layer =
            new GameObject(
                layerName
            );


        layer.transform.SetParent(
            parent,
            false
        );


        MeshFilter filter =
            layer.AddComponent<
                MeshFilter
            >();


        layer.AddComponent<
            MeshRenderer
        >()
        .sharedMaterial =
            vegetationMaterial;


        return filter;
    }


    private void UpdateVegetationForLOD(
        int lodIndex)
    {
        if (vegetationData == null ||
            vegetationObject == null ||
            vegetationLODIndex ==
            lodIndex)
        {
            return;
        }


        VegetationPlacementData projected =
            VegetationGenerator
                .ProjectPlacementsToLOD(
                    vegetationData,
                    heightMap,
                    heightMapSettings.minHeight,
                    heightMapSettings.maxHeight,
                    vegetationSettings,
                    environmentDefinitions,
                    position,
                    meshSettings.numVertsPerLine,
                    meshSettings.meshScale,
                    detailLevels[
                        lodIndex
                    ].lod
                );


        VegetationGenerator
            .BuildCombinedMeshes(
                projected,
                meshObject.transform.position,
                out Mesh treesMesh,
                out Mesh grassMesh,
                out Mesh rocksMesh
            );


        SetVegetationLayer(
            treesMeshFilter,
            treesMesh,
            "Trees (" +
            projected.trees.Count +
            ")"
        );


        SetVegetationLayer(
            grassMeshFilter,
            grassMesh,
            "Grass (" +
            projected.grass.Count +
            ")"
        );


        SetVegetationLayer(
            rocksMeshFilter,
            rocksMesh,
            "Rocks (" +
            projected.rocks.Count +
            ")"
        );


        vegetationLODIndex =
            lodIndex;
    }


    private void SetVegetationLayer(
        MeshFilter filter,
        Mesh mesh,
        string layerName)
    {
        Mesh previousMesh =
            filter.sharedMesh;


        filter.sharedMesh =
            mesh;


        filter.gameObject.name =
            layerName;


        filter.gameObject.SetActive(
            mesh != null
        );


        if (previousMesh != null)
        {
            Object.Destroy(
                previousMesh
            );
        }
    }


    // ---------------------------------------------------------
    // VISIBILITY
    // ---------------------------------------------------------

    public void SetVisible(
        bool visible)
    {
        meshObject.SetActive(
            visible
        );
    }


    public bool IsVisible()
    {
        return
            meshObject.activeSelf;
    }


    // ---------------------------------------------------------
    // ENVIRONMENT
    // ---------------------------------------------------------

    public bool TryGetEnvironmentSample(
        Vector2 worldPosition,
        out EnvironmentSample sample)
    {
        if (!heightMapReceived ||
            environmentDefinitions == null)
        {
            sample =
                default;

            return false;
        }


        return EnvironmentSampler.TrySample(
            worldPosition,
            heightMap,
            heightMapSettings.minHeight,
            heightMapSettings.maxHeight,
            environmentDefinitions,
            vegetationSettings,
            position,
            meshSettings.numVertsPerLine,
            meshSettings.meshScale,
            out sample
        );
    }
}


// =============================================================
// LOD MESH
// =============================================================

class LODMesh
{
    public Mesh mesh;

    public bool hasRequestedMesh;

    public bool hasMesh;


    private int lod;


    public event System.Action
        updateCallback;


    public LODMesh(
        int lod)
    {
        this.lod =
            lod;
    }


    private void OnMeshDataReceived(
        object meshDataObject)
    {
        mesh =
            ((MeshData)meshDataObject)
            .CreateMesh();


        hasMesh =
            true;


        updateCallback?.Invoke();
    }


    public void RequestMesh(
        HeightMap heightMap,
        MeshSettings meshSettings)
    {
        if (hasRequestedMesh)
            return;


        hasRequestedMesh =
            true;


        ThreadedDataRequester.RequestData(
            () =>
                MeshGenerator.GenerateTerrainMesh(
                    heightMap.values,
                    meshSettings,
                    lod
                ),

            OnMeshDataReceived
        );
    }
}