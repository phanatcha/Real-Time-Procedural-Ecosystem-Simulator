using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public enum SeasonalReportSurface { Ground, Tree, Grass, Rock }

public sealed class SeasonalReportData : IDisposable
{
    public sealed class Frame
    {
        public SeasonState state;
        public readonly List<TerrainReportData.Chunk> meshes = new List<TerrainReportData.Chunk>();
        public double temperature;
        public double moisture;
        public double snow;
        public double accessibleGrass;
        public double growth;
    }

    public TerrainReportData Terrain { get; private set; }
    public readonly Frame[] Frames = new Frame[4];
    public int TreeCount { get; private set; }
    public int GrassCount { get; private set; }
    public int RockCount { get; private set; }
    public int LandSampleCount { get; private set; }
    public Vector2 HeightRange { get; private set; }
    int vertexCount;

    public static SeasonalReportData Generate(HeightMapSettings heights, MeshSettings meshes, TextureData textures,
        VegetationSettings vegetation, Vector2Int centre, int width, Func<float, bool> cancel = null)
    {
        if (textures == null || textures.environmentDefinitions == null || vegetation == null)
            throw new ArgumentException("Assign terrain colors with environmental definitions and vegetation settings.");
        if (width != 1 && width != 3 && width != 5) throw new ArgumentOutOfRangeException(nameof(width));
        SeasonalReportData data = new SeasonalReportData();
        List<Mesh> templates = new List<Mesh>();
        try
        {
            for (int i = 0; i < 4; i++) data.Frames[i] = new Frame { state = SeasonState.For((BorealSeason)i) };
            data.Terrain = TerrainReportData.Generate(heights, meshes, textures, centre, width, 0,
                cancel == null ? null : new Func<float, bool>(p => cancel(p * 0.35f)));
            data.HeightRange = new Vector2(data.Terrain.MinimumHeight, data.Terrain.MaximumHeight);
            TerrainEnvironmentSampler sampler = new TerrainEnvironmentSampler(heights, meshes, textures.environmentDefinitions, vegetation);
            System.Random random = new System.Random(vegetation.seed);
            Mesh[][] variants = new Mesh[3][];
            for (int kind = 0; kind < 3; kind++)
            {
                variants[kind] = new Mesh[3];
                for (int i = 0; i < 3; i++)
                {
                    Mesh mesh = kind == 0 ? VegetationMeshBuilder.BuildTree(vegetation.trunkColour,
                        i % 2 == 0 ? vegetation.foliageColourA : vegetation.foliageColourB, random)
                        : kind == 1 ? VegetationMeshBuilder.BuildGrassClump(vegetation.grassColourA, vegetation.grassColourB, random)
                        : VegetationMeshBuilder.BuildRock(vegetation.rockColourA, vegetation.rockColourB, random);
                    variants[kind][i] = mesh;
                    templates.Add(mesh);
                }
            }
            for (int index = 0; index < data.Terrain.Chunks.Count; index++)
            {
                if (cancel != null && cancel(0.35f + 0.55f * index / data.Terrain.Chunks.Count)) throw new OperationCanceledException();
                TerrainReportData.Chunk chunk = data.Terrain.Chunks[index];
                Vector2 world = TerrainGrid.ChunkCoordinateToWorldPosition(chunk.coordinate, meshes);
                HeightMap map = HeightMapGenerator.GenerateHeightMap(meshes.numVertsPerLine, meshes.numVertsPerLine, heights, world / meshes.meshScale);
                sampler.CacheHeightMap(chunk.coordinate, map);
                data.AddSurface(chunk.mesh, chunk.offset, chunk.coordinate, SeasonalReportSurface.Ground, sampler);
                Vector2 half = Vector2.one * meshes.meshWorldSize * 0.5f;
                VegetationPlacementData placements = VegetationGenerator.GeneratePlacements(world - half, world + half, map,
                    heights.minHeight, heights.maxHeight, vegetation, textures.environmentDefinitions, meshes.numVertsPerLine, meshes.meshScale);
                placements = VegetationGenerator.ProjectPlacementsToLOD(placements, map, heights.minHeight, heights.maxHeight,
                    vegetation, textures.environmentDefinitions, world, meshes.numVertsPerLine, meshes.meshScale, 0);
                data.TreeCount += placements.trees.Count;
                data.GrassCount += placements.grass.Count;
                data.RockCount += placements.rocks.Count;
                List<VegetationInstance>[] groups = { placements.trees, placements.grass, placements.rocks };
                for (int kind = 0; kind < groups.Length; kind++)
                {
                    if (groups[kind].Count == 0) continue;
                    int count = 0;
                    foreach (VegetationInstance instance in groups[kind]) count += variants[kind][instance.variantIndex].vertexCount;
                    if (data.vertexCount + count > 500000) throw new InvalidOperationException("Preview exceeds 500,000 vertices. Choose a smaller region.");
                    CombineInstance[] combines = new CombineInstance[groups[kind].Count];
                    Vector3 origin = new Vector3(world.x, 0f, world.y);
                    for (int i = 0; i < combines.Length; i++)
                    {
                        VegetationInstance instance = groups[kind][i];
                        combines[i] = new CombineInstance { mesh = variants[kind][instance.variantIndex],
                            transform = Matrix4x4.TRS(instance.position - origin, instance.rotation, Vector3.one * instance.scale) };
                    }
                    Mesh combined = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    try
                    {
                        combined.CombineMeshes(combines, true, true);
                        data.AddSurface(combined, chunk.offset, chunk.coordinate, (SeasonalReportSurface)(kind + 1), sampler);
                    }
                    finally { Object.DestroyImmediate(combined); }
                }
            }
            const int samples = 33;
            for (int y = 0; y < samples; y++)
            {
                if (cancel != null && cancel(0.9f + 0.1f * y / samples)) throw new OperationCanceledException();
                for (int x = 0; x < samples; x++)
                {
                    Vector2 world = data.Terrain.WorldCentre + new Vector2(x / 32f - 0.5f, y / 32f - 0.5f) * data.Terrain.WorldSize;
                    EnvironmentSample sample = sampler.Sample(world);
                    if (!sample.isLand) continue;
                    data.LandSampleCount++;
                    foreach (Frame frame in data.Frames)
                    {
                        SeasonalEnvironmentSample seasonal = SeasonalEnvironment.Evaluate(sample, frame.state);
                        frame.temperature += seasonal.temperature;
                        frame.moisture += seasonal.moisture;
                        frame.snow += seasonal.snowCover;
                        frame.accessibleGrass += seasonal.accessibleGrassBiomass;
                        frame.growth += seasonal.grassGrowthMultiplier;
                    }
                }
            }
            foreach (Frame frame in data.Frames)
            {
                int n = Mathf.Max(1, data.LandSampleCount);
                frame.temperature /= n; frame.moisture /= n; frame.snow /= n; frame.accessibleGrass /= n; frame.growth /= n;
            }
            return data;
        }
        catch { data.Dispose(); throw; }
        finally { foreach (Mesh mesh in templates) Object.DestroyImmediate(mesh); }
    }

    void AddSurface(Mesh source, Vector3 offset, Vector2Int coordinate, SeasonalReportSurface kind, TerrainEnvironmentSampler sampler)
    {
        Vector3[] vertices = source.vertices;
        Vector3[] normals = source.normals;
        Color[] colors = source.colors;
        vertexCount += vertices.Length;
        if (vertexCount > 500000) throw new InvalidOperationException("Choose a smaller seasonal preview region.");
        EnvironmentSample[] samples = new EnvironmentSample[vertices.Length];
        Vector3 origin = offset + new Vector3(Terrain.WorldCentre.x, 0f, Terrain.WorldCentre.y);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 world = vertices[i] + origin;
            samples[i] = sampler.Sample(new Vector2(world.x, world.z));
        }
        foreach (Frame frame in Frames)
        {
            Mesh mesh = Object.Instantiate(source);
            mesh.hideFlags = HideFlags.HideAndDontSave;
            frame.meshes.Add(new TerrainReportData.Chunk { mesh = mesh, offset = offset, coordinate = coordinate });
            mesh.name = $"{frame.state.season} {kind} {coordinate}";
            Color[] seasonalColors = new Color[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                seasonalColors[i] = Colorize(colors[i], SeasonalEnvironment.Evaluate(samples[i], frame.state), kind, normals[i].y);
            mesh.colors = seasonalColors;
        }
        HeightRange = new Vector2(Mathf.Min(HeightRange.x, source.bounds.min.y), Mathf.Max(HeightRange.y, source.bounds.max.y));
    }

    public static Color Colorize(Color original, SeasonalEnvironmentSample sample, SeasonalReportSurface kind, float normalY)
    {
        if (!sample.IsValid || sample.baseEnvironment.isWater) return original;
        Color color = original;
        bool groundVegetation = kind == SeasonalReportSurface.Ground
            && (sample.baseEnvironment.biome == BiomeId.BorealForest || sample.baseEnvironment.biome == BiomeId.HighlandTaiga);
        if (kind == SeasonalReportSurface.Grass || groundVegetation)
            color = Color.Lerp(color, new Color(0.47f, 0.36f, 0.18f), sample.season.dormancy * (groundVegetation ? 0.65f : 0.85f));
        color *= 1f - sample.moisture * 0.12f;
        float upward = kind == SeasonalReportSurface.Ground ? 1f
            : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.65f, normalY));
        color = Color.Lerp(color, new Color(0.93f, 0.96f, 0.99f), sample.snowCover * upward);
        color.a = 1f;
        return color;
    }

    public static Color Lighting(BorealSeason season)
    {
        switch (season)
        {
            case BorealSeason.Spring: return new Color(0.96f, 1f, 0.98f);
            case BorealSeason.Summer: return new Color(1f, 1f, 0.96f);
            case BorealSeason.Fall: return new Color(1f, 0.94f, 0.84f);
            default: return new Color(0.84f, 0.92f, 1f);
        }
    }

    public void Dispose()
    {
        foreach (Frame frame in Frames)
        {
            if (frame == null) continue;
            foreach (TerrainReportData.Chunk chunk in frame.meshes) if (chunk.mesh != null) Object.DestroyImmediate(chunk.mesh);
            frame.meshes.Clear();
        }
        Terrain?.Dispose();
        Terrain = null;
    }
}
