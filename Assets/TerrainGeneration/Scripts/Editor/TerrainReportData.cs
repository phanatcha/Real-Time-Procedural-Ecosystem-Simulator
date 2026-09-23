using System;
using System.Collections.Generic;
using UnityEngine;

public enum TerrainReportStage
{
    BaseNoise,
    WarpedNoise,
    RidgeBlend,
    RiverMask,
    LakeMask,
    Falloff,
    ShapedHeight,
    FinalHeight
}

public sealed class TerrainReportData : IDisposable
{
    public const int MaximumMapSize = 1025;

    public static readonly string[] StageTitles =
    {
        "01  Base noise",
        "02  Domain warped noise",
        "03  Mountain ridge blend",
        "04  River carving mask",
        "05  Lake carving mask",
        "06  World falloff mask",
        "07  Shaped height before curve",
        "08  Final heightmap"
    };

    public static readonly string[] StageDescriptions =
    {
        "Seeded octave noise with domain warp disabled for comparison. Black = 0, white = 1.",
        "The actual base noise supplied to the terrain evaluator, including configured domain warp.",
        "Height after the mountain band and ridged noise are blended into the warped base.",
        "Actual river carving weight. White = strongest carving; black = no carving.",
        "Actual lake carving weight. White = strongest carving; black = no carving.",
        "World-space height subtraction mask. Black when falloff is disabled.",
        "Height after ridge blending, river/lake carving and falloff, before the height curve.",
        "Final world height after the configured height curve and height multiplier. This drives the 3D meshes."
    };

    public sealed class Chunk
    {
        public Vector2Int coordinate;
        public Vector3 offset;
        public Mesh mesh;
    }

    public readonly List<Chunk> Chunks = new List<Chunk>();
    public readonly float[][,] Maps = new float[StageTitles.Length][,];
    public readonly Texture2D[] Textures = new Texture2D[StageTitles.Length];
    public int Size { get; private set; }
    public int ChunkCount { get; private set; }
    public int Lod { get; private set; }
    public Vector2Int CentreChunk { get; private set; }
    public Vector2 WorldCentre { get; private set; }
    public float WorldSize { get; private set; }
    public float MinimumHeight { get; private set; }
    public float MaximumHeight { get; private set; }
    public float HeightScaleMinimum { get; private set; }
    public float HeightScaleMaximum { get; private set; }

    public static TerrainReportData Generate(HeightMapSettings heights, MeshSettings meshSettings,
        TextureData textureSettings, Vector2Int centre, int chunkCount, int lod,
        Func<float, bool> cancel = null)
    {
        if (heights == null || heights.noiseSettings == null || heights.heightCurve == null)
            throw new ArgumentException("Assign valid height settings with noise and a height curve.");
        if (meshSettings == null || meshSettings.meshScale <= 0f)
            throw new ArgumentException("Assign mesh settings with a positive mesh scale.");
        if (chunkCount < 1 || chunkCount > 15 || chunkCount % 2 == 0)
            throw new ArgumentException("Use an odd region width from 1 to 15 chunks.");
        if (lod < 0 || lod >= MeshSettings.numSupportedLODs)
            throw new ArgumentOutOfRangeException(nameof(lod));

        int n = meshSettings.numVertsPerLine;
        int stride = n - 3;
        int size = chunkCount * stride + 1;
        if (size > MaximumMapSize)
            throw new ArgumentException($"This region produces a {size} x {size} map. Choose fewer chunks (limit {MaximumMapSize}).");

        TerrainReportData data = new TerrainReportData
        {
            Size = size,
            ChunkCount = chunkCount,
            Lod = lod,
            CentreChunk = centre,
            WorldCentre = TerrainGrid.ChunkCoordinateToWorldPosition(centre, meshSettings),
            WorldSize = chunkCount * meshSettings.meshWorldSize,
            MinimumHeight = float.PositiveInfinity,
            MaximumHeight = float.NegativeInfinity,
            HeightScaleMinimum = heights.minHeight,
            HeightScaleMaximum = heights.maxHeight
        };

        try
        {
            for (int stage = 0; stage < data.Maps.Length; stage++)
                data.Maps[stage] = new float[size, size];

            AnimationCurve curve = new AnimationCurve(heights.heightCurve.keys);
            NoiseSettings noise = heights.noiseSettings;
            int radius = chunkCount / 2;
            for (int row = 0; row < chunkCount; row++)
            {
                for (int column = 0; column < chunkCount; column++)
                {
                    float progress = (row * chunkCount + column) / (float)(chunkCount * chunkCount);
                    if (cancel != null && cancel(progress)) throw new OperationCanceledException();
                    Vector2Int coordinate = centre + new Vector2Int(column - radius, radius - row);
                    Vector2 world = TerrainGrid.ChunkCoordinateToWorldPosition(coordinate, meshSettings);
                    Vector2 sampleCentre = world / meshSettings.meshScale;
                    float[,] warped = Noise.GenerateNoiseMap(n, n, noise, sampleCentre);
                    float[,] unwarped = noise.domainWarpStrength <= 0f ? warped : Noise.GenerateNoiseMap(
                        n, n, noise.seed, noise.scale, noise.octaves, noise.persistance, noise.lacunarity,
                        sampleCentre + noise.offset, noise.normalizeMode, 0f, noise.domainWarpScale,
                        noise.globalHeightBias, noise.globalContrast);
                    bool useRidges = heights.ridgeSettings != null && heights.ridgeSettings.strength > 0f;
                    float[,] ridges = useRidges
                        ? Noise.GenerateRidgedNoiseMap(n, n, noise, heights.ridgeSettings, sampleCentre)
                        : null;
                    float[,] final = new float[n, n];
                    for (int y = 0; y < n; y++)
                    {
                        for (int x = 0; x < n; x++)
                        {
                            Vector2 terrainPosition = new Vector2(sampleCentre.x + x - n / 2f,
                                sampleCentre.y - (y - n / 2f));
                            TerrainHeightEvaluation result = TerrainHeightEvaluator.Evaluate(
                                warped[x, y], useRidges ? ridges[x, y] : 0f,
                                terrainPosition, heights, curve);
                            final[x, y] = result.height;
                            if (x == 0 || y == 0 || x == n - 1 || y == n - 1) continue;
                            int atlasX = column * stride + x - 1;
                            int atlasY = row * stride + y - 1;
                            data.Maps[(int)TerrainReportStage.BaseNoise][atlasX, atlasY] = unwarped[x, y];
                            data.Maps[(int)TerrainReportStage.WarpedNoise][atlasX, atlasY] = warped[x, y];
                            data.Maps[(int)TerrainReportStage.RidgeBlend][atlasX, atlasY] = result.heightAfterRidges;
                            data.Maps[(int)TerrainReportStage.RiverMask][atlasX, atlasY] = result.riverStrength;
                            data.Maps[(int)TerrainReportStage.LakeMask][atlasX, atlasY] = result.lakeStrength;
                            data.Maps[(int)TerrainReportStage.Falloff][atlasX, atlasY] = result.falloff;
                            data.Maps[(int)TerrainReportStage.ShapedHeight][atlasX, atlasY] = result.normalizedHeightInput;
                            data.Maps[(int)TerrainReportStage.FinalHeight][atlasX, atlasY] = result.height;
                            data.MinimumHeight = Mathf.Min(data.MinimumHeight, result.height);
                            data.MaximumHeight = Mathf.Max(data.MaximumHeight, result.height);
                        }
                    }

                    Mesh mesh = MeshGenerator.GenerateTerrainMesh(final, meshSettings, lod).CreateMesh();
                    mesh.name = $"Report terrain {coordinate}";
                    mesh.hideFlags = HideFlags.HideAndDontSave;
                    data.Chunks.Add(new Chunk
                    {
                        coordinate = coordinate,
                        offset = new Vector3(world.x - data.WorldCentre.x, 0f, world.y - data.WorldCentre.y),
                        mesh = mesh
                    });
                    ApplyReportColors(mesh, textureSettings, heights.minHeight, heights.maxHeight);
                }
            }

            for (int stage = 0; stage < data.Maps.Length; stage++)
            {
                bool isHeight = stage == (int)TerrainReportStage.FinalHeight;
                data.Textures[stage] = CreateTexture(data.Maps[stage],
                    isHeight ? heights.minHeight : 0f, isHeight ? heights.maxHeight : 1f);
                data.Textures[stage].name = StageTitles[stage];
            }
            return data;
        }
        catch
        {
            data.Dispose();
            throw;
        }
    }

    public static Texture2D CreateTexture(float[,] values, float minimum, float maximum)
    {
        int width = values.GetLength(0);
        int height = values.GetLength(1);
        Color32[] pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte shade = (byte)Mathf.RoundToInt(Mathf.InverseLerp(minimum, maximum, values[x, y]) * 255f);
                pixels[(height - 1 - y) * width + x] = new Color32(shade, shade, shade, 255);
            }
        }
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    static void ApplyReportColors(Mesh mesh, TextureData settings, float minimum, float maximum)
    {
        Vector3[] vertices = mesh.vertices;
        Color[] colors = new Color[vertices.Length];
        for (int index = 0; index < vertices.Length; index++)
        {
            float normalized = Mathf.InverseLerp(minimum, maximum, vertices[index].y);
            Color color = Color.Lerp(new Color(0.15f, 0.3f, 0.22f), new Color(0.87f, 0.91f, 0.93f), normalized);
            if (settings != null && settings.environmentDefinitions != null && settings.layers != null && settings.layers.Length > 0)
            {
                color = settings.layers[0].tint;
                for (int layerIndex = 1; layerIndex < settings.layers.Length; layerIndex++)
                {
                    TextureData.Layer layer = settings.layers[layerIndex];
                    float start = settings.environmentDefinitions.GetBiomeStartHeight(layer.biome);
                    float blend = Mathf.Max(0.0001f, layer.blendStrength);
                    color = Color.Lerp(color, layer.tint, Mathf.InverseLerp(start - blend * 0.5f, start + blend * 0.5f, normalized));
                }
            }
            color.a = 1f;
            colors[index] = color;
        }
        mesh.colors = colors;
    }

    public void Dispose()
    {
        foreach (Chunk chunk in Chunks)
            if (chunk.mesh != null) UnityEngine.Object.DestroyImmediate(chunk.mesh);
        Chunks.Clear();
        for (int index = 0; index < Textures.Length; index++)
        {
            if (Textures[index] != null) UnityEngine.Object.DestroyImmediate(Textures[index]);
            Textures[index] = null;
        }
    }
}
