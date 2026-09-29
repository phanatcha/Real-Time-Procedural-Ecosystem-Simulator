using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Transparent low-poly water at the water level: one flat grid tile under every terrain chunk that dips below it.
// Tiles are children of their chunk, so they stream in, hide and unload with the terrain. The water shader moves
// the waves and facets from world positions, so neighbouring tiles meet without seams. While the surface exists the
// terrain shader draws lake and river beds as sand and silt (from the water material) instead of painted-on blue.
public class WaterSurface
{
    public const string TileName = "Water Surface";

    const string NoDepthTextureKeyword = "_WATER_NO_DEPTH_TEXTURE";

    static readonly int SurfaceActiveId = Shader.PropertyToID("_TerrainWaterSurface");
    static readonly int SurfaceHeightId = Shader.PropertyToID("_TerrainWaterHeight");
    static readonly int LakebedShallowId = Shader.PropertyToID("_TerrainLakebedShallow");
    static readonly int LakebedDeepId = Shader.PropertyToID("_TerrainLakebedDeep");
    static readonly int LakebedDeepDepthId = Shader.PropertyToID("_TerrainLakebedDeepDepth");
    static readonly int CausticsId = Shader.PropertyToID("_TerrainCaustics");
    static readonly int WetShoreId = Shader.PropertyToID("_TerrainWetShore");

    readonly Material material;
    readonly Mesh tileMesh;
    readonly int tileLayer;
    int lastAppliedFrame = -1;

    public float Height { get; }

    public WaterSurface(Material material, float height, float chunkSize)
    {
        this.material = material;
        Height = height;
        tileLayer = Mathf.Max(0, LayerMask.NameToLayer("Water"));
        tileMesh = BuildTileMesh(chunkSize, GetFloat("_FacetSize", 3f), GetFloat("_WaveHeight", 0.3f));

        ApplyTerrainSettings();
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    // Adds a tile under the chunk when its current terrain mesh reaches below the water. Chunks report again when
    // they change detail level, so a pond a coarse mesh smooths over gets its water once the chunk is seen closer.
    public void AddTileIfNeeded(TerrainChunk chunk)
    {
        GameObject chunkObject = chunk != null ? chunk.GameObject : null;
        Mesh terrainMesh = chunk != null ? chunk.CurrentMesh : null;
        if (chunkObject == null || terrainMesh == null)
        {
            return;
        }

        Transform chunkTransform = chunkObject.transform;
        if (chunkTransform.position.y + terrainMesh.bounds.min.y >= Height || chunkTransform.Find(TileName) != null)
        {
            return;
        }

        GameObject tile = new GameObject(TileName) { layer = tileLayer };
        tile.transform.SetParent(chunkTransform, false);
        tile.transform.localPosition = new Vector3(0f, Height - chunkTransform.position.y, 0f);
        tile.AddComponent<MeshFilter>().sharedMesh = tileMesh;

        MeshRenderer tileRenderer = tile.AddComponent<MeshRenderer>();
        tileRenderer.sharedMaterial = material;
        tileRenderer.shadowCastingMode = ShadowCastingMode.Off;
        tileRenderer.lightProbeUsage = LightProbeUsage.Off;
    }

    public void Dispose()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        Shader.SetGlobalFloat(SurfaceActiveId, 0f);
        Shader.DisableKeyword(NoDepthTextureKeyword);

        if (tileMesh == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Object.Destroy(tileMesh);
        }
        else
        {
            Object.DestroyImmediate(tileMesh);
        }
    }

    void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        // Once a frame, so changes made to the water material while playing reach the terrain straight away.
        if (lastAppliedFrame != Time.frameCount)
        {
            lastAppliedFrame = Time.frameCount;
            ApplyTerrainSettings();
        }

        // Without a depth texture the water can't find the bed or the shore, so it falls back to a simpler look.
        if (CameraHasDepthTexture(camera))
        {
            Shader.DisableKeyword(NoDepthTextureKeyword);
        }
        else
        {
            Shader.EnableKeyword(NoDepthTextureKeyword);
        }
    }

    void ApplyTerrainSettings()
    {
        Shader.SetGlobalFloat(SurfaceActiveId, 1f);
        Shader.SetGlobalFloat(SurfaceHeightId, Height);
        Shader.SetGlobalColor(LakebedShallowId, GetColor("_LakebedShallowColor", new Color(0.66f, 0.6f, 0.44f)));
        Shader.SetGlobalColor(LakebedDeepId, GetColor("_LakebedDeepColor", new Color(0.22f, 0.25f, 0.19f)));
        Shader.SetGlobalFloat(LakebedDeepDepthId, GetFloat("_LakebedDeepDepth", 5f));
        Shader.SetGlobalFloat(CausticsId, GetFloat("_CausticsStrength", 0.6f));
        Shader.SetGlobalFloat(WetShoreId, GetFloat("_WetShoreHeight", 0.7f));
    }

    static bool CameraHasDepthTexture(Camera camera)
    {
        UniversalRenderPipelineAsset pipeline = UniversalRenderPipeline.asset;
        if (pipeline == null)
        {
            return false;
        }

        return camera.TryGetComponent(out UniversalAdditionalCameraData cameraData)
            ? cameraData.requiresDepthTexture
            : pipeline.supportsCameraDepthTexture;
    }

    float GetFloat(string property, float fallback)
    {
        return material != null && material.HasProperty(property) ? material.GetFloat(property) : fallback;
    }

    Color GetColor(string property, Color fallback)
    {
        return material != null && material.HasProperty(property) ? material.GetColor(property) : fallback;
    }

    // A flat square grid the size of a chunk, centred on the chunk like its terrain mesh. Edge points sit exactly on
    // the chunk border, so two tiles compute the same world position (and the same waves) for their shared edge.
    static Mesh BuildTileMesh(float chunkSize, float facetSize, float waveHeight)
    {
        int cells = Mathf.Max(2, Mathf.RoundToInt(chunkSize / Mathf.Max(0.5f, facetSize) * 0.5f) * 2);
        int pointsPerLine = cells + 1;
        float spacing = chunkSize / cells;
        float half = chunkSize * 0.5f;

        Vector3[] vertices = new Vector3[pointsPerLine * pointsPerLine];
        for (int z = 0; z < pointsPerLine; z++)
        {
            float localZ = z == cells ? half : z * spacing - half;
            for (int x = 0; x < pointsPerLine; x++)
            {
                float localX = x == cells ? half : x * spacing - half;
                vertices[z * pointsPerLine + x] = new Vector3(localX, 0f, localZ);
            }
        }

        int[] triangles = new int[cells * cells * 6];
        int index = 0;
        for (int z = 0; z < cells; z++)
        {
            for (int x = 0; x < cells; x++)
            {
                int bottomLeft = z * pointsPerLine + x;
                int bottomRight = bottomLeft + 1;
                int topLeft = bottomLeft + pointsPerLine;
                int topRight = topLeft + 1;

                // Alternating diagonals, so the facets don't all lean the same way.
                if ((x + z) % 2 == 0)
                {
                    triangles[index++] = bottomLeft;
                    triangles[index++] = topLeft;
                    triangles[index++] = topRight;
                    triangles[index++] = bottomLeft;
                    triangles[index++] = topRight;
                    triangles[index++] = bottomRight;
                }
                else
                {
                    triangles[index++] = bottomLeft;
                    triangles[index++] = topLeft;
                    triangles[index++] = bottomRight;
                    triangles[index++] = bottomRight;
                    triangles[index++] = topLeft;
                    triangles[index++] = topRight;
                }
            }
        }

        Mesh mesh = new Mesh
        {
            name = TileName,
            indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        // The shader moves points sideways by under half a facet and up and down with the waves.
        mesh.bounds = new Bounds(Vector3.zero,
            new Vector3(chunkSize + spacing * 2f, waveHeight * 2f + 2f, chunkSize + spacing * 2f));
        mesh.UploadMeshData(true);
        return mesh;
    }
}
