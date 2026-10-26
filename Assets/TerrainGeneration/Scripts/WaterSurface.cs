using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Transparent water at the water level: one flat grid tile under every terrain chunk that dips below it. Tiles are
// children of their chunk, so they stream in, hide and unload with the terrain. The water shader moves the waves from
// world positions, so neighbouring tiles meet without seams: long Gerstner swells from WaveSpectrum, and, where the GPU
// runs compute shaders, the shorter waves and their foam from OceanFFT. Each tile's vertex colour says how much the sea's waves
// move each point: none on rivers and lakes, less in the shallows. While the surface exists the terrain shader draws
// lake and river beds as sand and silt (from the water material) instead of painted-on blue.
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
    static readonly int WavesId = Shader.PropertyToID("_WaterWaves");
    static readonly int WavePhasesId = Shader.PropertyToID("_WaterWavePhases");
    static readonly int WaveCountId = Shader.PropertyToID("_WaterWaveCount");
    static readonly int WaveChopId = Shader.PropertyToID("_WaterWaveChop");
    static readonly int WaveHeightId = Shader.PropertyToID("_WaterWaveHeight");
    static readonly int DetailId = Shader.PropertyToID("_WaterDetail");
    static readonly int DetailDisplacement0Id = Shader.PropertyToID("_WaterDetailDisplacement0");
    static readonly int DetailDisplacement1Id = Shader.PropertyToID("_WaterDetailDisplacement1");
    static readonly int DetailSlopes0Id = Shader.PropertyToID("_WaterDetailSlopes0");
    static readonly int DetailSlopes1Id = Shader.PropertyToID("_WaterDetailSlopes1");

    readonly Material material;
    readonly OceanFFT ocean;
    readonly MeshSettings meshSettings;
    readonly Vector3[] tileVertices;
    readonly int[] tileTriangles;
    readonly float tileSpacing;
    readonly List<Mesh> tileMeshes = new List<Mesh>();
    readonly Vector4[] waves = new Vector4[WaveSpectrum.MaxWaves];
    readonly float[] wavePhases = new float[WaveSpectrum.MaxWaves];
    bool spectrumBuilt;
    int spectrumMaterialHash;
    Bounds tileBounds;
    readonly int tileLayer;
    int lastAppliedFrame = -1;

    public float Height { get; }

    public WaterSurface(Material material, float height, MeshSettings meshSettings)
    {
        this.material = material;
        this.meshSettings = meshSettings;
        Height = height;
        tileLayer = Mathf.Max(0, LayerMask.NameToLayer("Water"));
        tileSpacing = BuildTileGrid(meshSettings.meshWorldSize, GetFloat("_MeshSpacing", 2f),
            out tileVertices, out tileTriangles);

        ComputeShader oceanShader = Resources.Load<ComputeShader>("OceanFFT");
        if (oceanShader != null && SystemInfo.supportsComputeShaders)
        {
            ocean = new OceanFFT(oceanShader);
            Shader.SetGlobalTexture(DetailDisplacement0Id, ocean.Displacement(0));
            Shader.SetGlobalTexture(DetailDisplacement1Id, ocean.Displacement(1));
            Shader.SetGlobalTexture(DetailSlopes0Id, ocean.Slopes(0));
            Shader.SetGlobalTexture(DetailSlopes1Id, ocean.Slopes(1));
        }

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
        tile.AddComponent<MeshFilter>().sharedMesh = BuildTileMesh(chunk.HeightMap,
            new Vector2(chunkTransform.position.x, chunkTransform.position.z));

        MeshRenderer tileRenderer = tile.AddComponent<MeshRenderer>();
        tileRenderer.sharedMaterial = material;
        tileRenderer.shadowCastingMode = ShadowCastingMode.Off;
        tileRenderer.lightProbeUsage = LightProbeUsage.Off;
    }

    public void Dispose()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        Shader.SetGlobalFloat(SurfaceActiveId, 0f);
        Shader.SetGlobalVector(DetailId, Vector4.zero);
        Shader.DisableKeyword(NoDepthTextureKeyword);
        ocean?.Dispose();

        foreach (Mesh tileMesh in tileMeshes)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(tileMesh);
            }
            else
            {
                Object.DestroyImmediate(tileMesh);
            }
        }

        tileMeshes.Clear();
    }

    void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        // Once a frame, so changes made to the water material while playing reach the terrain straight away.
        if (lastAppliedFrame != Time.frameCount)
        {
            lastAppliedFrame = Time.frameCount;
            ApplyTerrainSettings();
            // The more crest foam the material asks for, the less the FFT's surface must squeeze before it foams.
            float time = (Application.isPlaying ? Time.time : Time.realtimeSinceStartup) * GetFloat("_WaveSpeed", 1f);
            ocean?.Simulate(time, Mathf.Lerp(0.7f, 1f, GetFloat("_CrestFoam", 0.5f)), GetFloat("_FoamLife", 3f));
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
        ApplyWaveSpectrum();
        Shader.SetGlobalFloat(SurfaceActiveId, 1f);
        Shader.SetGlobalFloat(SurfaceHeightId, Height);
        Shader.SetGlobalColor(LakebedShallowId, GetColor("_LakebedShallowColor", new Color(0.66f, 0.6f, 0.44f)));
        Shader.SetGlobalColor(LakebedDeepId, GetColor("_LakebedDeepColor", new Color(0.22f, 0.25f, 0.19f)));
        Shader.SetGlobalFloat(LakebedDeepDepthId, GetFloat("_LakebedDeepDepth", 5f));
        Shader.SetGlobalFloat(CausticsId, GetFloat("_CausticsStrength", 0.6f));
        Shader.SetGlobalFloat(WetShoreId, GetFloat("_WetShoreHeight", 0.7f));
    }

    // Rebuilds the waves from the material's spectrum settings when the material changes, and hands them to the water
    // shader. With the FFT ocean the Gerstner waves only carry the swells longer than _SwellLength, and the FFT carries
    // on from there.
    void ApplyWaveSpectrum()
    {
        int materialHash = material != null ? material.ComputeCRC() : 0;
        if (spectrumBuilt && materialHash == spectrumMaterialHash)
        {
            return;
        }

        spectrumBuilt = true;
        spectrumMaterialHash = materialHash;
        WaveSpectrum.Settings settings = new WaveSpectrum.Settings
        {
            windDirection = GetFloat("_WindDirection", 35f),
            windSpeed = GetFloat("_WindSpeed", 7f),
            fetch = GetFloat("_Fetch", 40f),
            peakSharpness = GetFloat("_PeakSharpness", 3.3f),
            spread = GetFloat("_Spread", 1f),
            waveCount = Mathf.RoundToInt(GetFloat("_WaveCount", 24f)),
            shortestWave = GetFloat("_ShortestWave", 1f),
            heightScale = GetFloat("_WaveHeightScale", 1f),
            choppiness = GetFloat("_Choppiness", 0.8f),
            seed = Mathf.RoundToInt(GetFloat("_SpectrumSeed", 1f))
        };
        // The mesh can move with waves at least three of its points long.
        float meshWaveLength = 3f * tileSpacing;
        WaveSpectrum.Settings swells = settings;
        if (ocean != null)
        {
            swells.shortestWave = Mathf.Max(GetFloat("_SwellLength", 20f), 1.5f * meshWaveLength);
        }

        int count = WaveSpectrum.Build(swells, waves, wavePhases, out float chop, out float significantHeight,
            out float reach);
        Shader.SetGlobalVectorArray(WavesId, waves);
        Shader.SetGlobalFloatArray(WavePhasesId, wavePhases);
        Shader.SetGlobalFloat(WaveCountId, count);
        Shader.SetGlobalFloat(WaveChopId, chop);
        Shader.SetGlobalFloat(WaveHeightId, significantHeight);

        if (ocean != null)
        {
            ocean.SetSpectrum(settings, swells, meshWaveLength);
            reach += ocean.Reach;
            Shader.SetGlobalVector(DetailId, new Vector4(1f / ocean.TileSize(0), 1f / ocean.TileSize(1), 1f, 0f));
        }
        else
        {
            Shader.SetGlobalVector(DetailId, Vector4.zero);
        }

        // Waves move points up and down, and sideways, by at most the sum of their amplitudes.
        float size = meshSettings.meshWorldSize + (tileSpacing + reach) * 2f;
        tileBounds = new Bounds(Vector3.zero, new Vector3(size, reach * 2f + 2f, size));
        foreach (Mesh tileMesh in tileMeshes)
        {
            tileMesh.bounds = tileBounds;
        }
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

    // The chunk's tile: the shared grid, with each point's wave strength in its vertex colour (red), read from the
    // chunk's own height map.
    Mesh BuildTileMesh(HeightMap heightMap, Vector2 chunkCentre)
    {
        // Samples stay a hair inside the chunk, where its height map is defined; both tiles of a shared edge then read
        // the same ground.
        float half = meshSettings.meshWorldSize * 0.5f - 0.01f;
        Color[] colours = new Color[tileVertices.Length];
        for (int i = 0; i < tileVertices.Length; i++)
        {
            Vector2 position = chunkCentre + new Vector2(Mathf.Clamp(tileVertices[i].x, -half, half),
                                                         Mathf.Clamp(tileVertices[i].z, -half, half));
            float strength = TerrainSurfaceSampler.TrySample(position, heightMap, chunkCentre,
                meshSettings.numVertsPerLine, meshSettings.meshScale, out TerrainSurfaceSample ground)
                ? WaveStrength(ground, Height)
                : 1f;
            colours[i] = new Color(strength, 0f, 0f, 1f);
        }

        Mesh mesh = new Mesh
        {
            name = TileName,
            indexFormat = tileVertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.vertices = tileVertices;
        mesh.colors = colours;
        mesh.triangles = tileTriangles;
        mesh.bounds = tileBounds;
        tileMeshes.Add(mesh);
        return mesh;
    }

    // How much the sea's waves move the water over this ground: not at all on rivers and lakes, and less where it is
    // shallow, so waves don't lift clear of the beach.
    public static float WaveStrength(TerrainSurfaceSample ground, float waterHeight)
    {
        float inland = Mathf.Clamp01(Mathf.Max(ground.riverStrength, ground.lakeStrength));
        float depth = waterHeight - ground.height;
        return (1f - inland) * Mathf.Lerp(0.3f, 1f, Mathf.InverseLerp(0.5f, 6f, depth));
    }

    // A flat square grid the size of a chunk, centred on the chunk like its terrain mesh. Edge points sit exactly on
    // the chunk border, so two tiles compute the same world position (and the same waves) for their shared edge.
    // Returns the spacing between points.
    static float BuildTileGrid(float chunkSize, float pointSpacing, out Vector3[] vertices, out int[] triangles)
    {
        int cells = Mathf.Max(2, Mathf.RoundToInt(chunkSize / Mathf.Max(0.5f, pointSpacing) * 0.5f) * 2);
        int pointsPerLine = cells + 1;
        float spacing = chunkSize / cells;
        float half = chunkSize * 0.5f;

        vertices = new Vector3[pointsPerLine * pointsPerLine];
        for (int z = 0; z < pointsPerLine; z++)
        {
            float localZ = z == cells ? half : z * spacing - half;
            for (int x = 0; x < pointsPerLine; x++)
            {
                float localX = x == cells ? half : x * spacing - half;
                vertices[z * pointsPerLine + x] = new Vector3(localX, 0f, localZ);
            }
        }

        triangles = new int[cells * cells * 6];
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

        return spacing;
    }
}
