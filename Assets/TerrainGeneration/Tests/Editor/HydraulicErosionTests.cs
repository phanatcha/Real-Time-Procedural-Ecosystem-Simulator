using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class HydraulicErosionTests
{
    HeightMapSettings heights;
    MeshSettings meshes;
    EnvironmentDefinitions environment;

    [SetUp]
    public void SetUp()
    {
        heights = Object.Instantiate(AssetDatabase.LoadAssetAtPath<HeightMapSettings>("Assets/TerrainGeneration/Settings/HeightMapSettings.asset"));
        meshes = Object.Instantiate(AssetDatabase.LoadAssetAtPath<MeshSettings>("Assets/TerrainGeneration/Settings/MeshSettings.asset"));
        environment = AssetDatabase.LoadAssetAtPath<EnvironmentDefinitions>("Assets/TerrainGeneration/Settings/EnvironmentDefinitions.asset");
        heights.erosionSettings = Settings();
        heights.erosionSettings.enabled = true;
        heights.worldRadius = 200f;
        heights.noiseSettings.normalizeMode = Noise.NormalizeMode.Global;
    }

    [TearDown]
    public void TearDown()
    {
        HydraulicErosionCache.Invalidate(heights);
        Object.DestroyImmediate(heights);
        Object.DestroyImmediate(meshes);
    }

    static HydraulicErosionSettings Settings() => new HydraulicErosionSettings
    {
        enabled = true, resolution = 65, dropletCount = 4000, maxLifetime = 40,
        maxHeightChange = 8f, edgeFadeCells = 4
    };

    static float[,] Ramp(int size = 65)
    {
        float[,] map = new float[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                map[x, y] = 70f - 40f * y / (size - 1f) + 4f * Mathf.Sin(x * 0.2f);
        return map;
    }

    static Vector2 Position(int x, int y, int size = 65) => new Vector2(-100f + x * 200f / (size - 1), 100f - y * 200f / (size - 1));

    [Test]
    public void FlatTerrainRemainsFlatAndFinite()
    {
        float[,] original = new float[65, 65];
        for (int y = 0; y < 65; y++) for (int x = 0; x < 65; x++) original[x, y] = 30f;
        HydraulicErosionMap map = HydraulicErosion.Simulate(original, Settings(), 123, 100f, 0f, 100f);
        for (int y = 0; y < 65; y++) for (int x = 0; x < 65; x++) Assert.AreEqual(0f, map.SampleDelta(Position(x, y)));
        Assert.AreEqual(0d, map.RemovedHeightSum);
        Assert.AreEqual(0d, map.DepositedHeightSum);
    }

    [TestCase(0, 8f)]
    [TestCase(4000, 0f)]
    public void ZeroDropletsOrStrengthLeavesOriginalHeightsUntouched(int count, float strength)
    {
        HydraulicErosionSettings settings = Settings();
        settings.dropletCount = count;
        settings.maxHeightChange = strength;
        HydraulicErosionMap map = HydraulicErosion.Simulate(Ramp(), settings, 2, 100f, 0f, 100f);
        for (int y = 0; y < 65; y++) for (int x = 0; x < 65; x++) Assert.AreEqual(0f, map.SampleDelta(Position(x, y)));
    }

    [Test]
    public void SlopedTerrainErodesDepositsAndAccountsForSedimentWithinBounds()
    {
        float[,] input = Ramp();
        float[,] untouched = (float[,])input.Clone();
        HydraulicErosionMap map = HydraulicErosion.Simulate(input, Settings(), 123, 100f, 0f, 100f);
        double netChange = 0d;
        float minimum = 0f;
        float maximum = 0f;
        for (int y = 0; y < 65; y++)
            for (int x = 0; x < 65; x++)
            {
                float change = map.SampleDelta(Position(x, y));
                Assert.IsFalse(float.IsNaN(change) || float.IsInfinity(change));
                Assert.That(Mathf.Abs(change), Is.LessThanOrEqualTo(8.0001f));
                minimum = Mathf.Min(minimum, change);
                maximum = Mathf.Max(maximum, change);
                netChange += change;
                if (x == 0 || y == 0 || x == 64 || y == 64) Assert.AreEqual(0f, change);
            }
        Assert.Less(minimum, -0.01f);
        Assert.Greater(maximum, 0.01f);
        Assert.Greater(map.MaximumFlow, 0f);
        Assert.That(netChange, Is.EqualTo(map.DepositedHeightSum - map.RemovedHeightSum).Within(0.01d));
        Assert.That(map.RemovedHeightSum, Is.EqualTo(map.DepositedHeightSum + map.UnsettledHeightSum).Within(0.05d));
        CollectionAssert.AreEqual(untouched, input);
        Assert.AreEqual(0f, map.SampleDelta(new Vector2(101f, 0f)));
        Assert.AreEqual(0f, map.SampleDelta(new Vector2(0f, -101f)));
    }

    [Test]
    public void SameSeedIsRepeatableAndDifferentSeedChangesChannels()
    {
        HydraulicErosionMap first = HydraulicErosion.Simulate(Ramp(), Settings(), 7, 100f, 0f, 100f);
        HydraulicErosionMap second = HydraulicErosion.Simulate(Ramp(), Settings(), 7, 100f, 0f, 100f);
        HydraulicErosionMap different = HydraulicErosion.Simulate(Ramp(), Settings(), 8, 100f, 0f, 100f);
        bool changed = false;
        for (int y = 1; y < 64; y++) for (int x = 1; x < 64; x++)
        {
            Vector2 position = Position(x, y);
            Assert.AreEqual(first.SampleDelta(position), second.SampleDelta(position));
            if (first.SampleDelta(position) != different.SampleDelta(position)) changed = true;
        }
        Assert.IsTrue(changed);
    }

    [Test]
    public void DisabledErosionPreservesTheUnmodifiedEvaluatorExactly()
    {
        heights.erosionSettings.enabled = false;
        Assert.IsNull(HydraulicErosionCache.Get(heights));
        using (TerrainReportData data = TerrainReportData.Generate(heights, meshes, null, Vector2Int.zero, 1, 0))
            CollectionAssert.AreEqual(data.Maps[(int)TerrainReportStage.BeforeErosion], data.Maps[(int)TerrainReportStage.FinalHeight]);
    }

    [Test]
    public void OneBakeIsSharedAcrossConcurrentChunksAndInvalidatesOnConfigurationChanges()
    {
        HydraulicErosionMap[] maps = new HydraulicErosionMap[6];
        Parallel.For(0, maps.Length, index => maps[index] = HydraulicErosionCache.Get(heights));
        foreach (HydraulicErosionMap map in maps) Assert.AreSame(maps[0], map);
        heights.noiseSettings.seed++;
        HydraulicErosionMap reseeded = HydraulicErosionCache.Get(heights);
        Assert.AreNotSame(maps[0], reseeded);
        heights.erosionSettings.dropletCount++;
        HydraulicErosionMap changed = HydraulicErosionCache.Get(heights);
        Assert.AreNotSame(reseeded, changed);
        heights.heightCurve = AnimationCurve.Linear(0f, 0f, 1f, 0.5f);
        Assert.AreNotSame(changed, HydraulicErosionCache.Get(heights));
    }

    [TestCase(-2, -1)]
    [TestCase(0, 0)]
    public void ChunkBordersAndReportMeshesUseTheSameErodedHeightField(int cx, int cz)
    {
        Vector2Int centre = new Vector2Int(cx, cz);
        int size = meshes.numVertsPerLine;
        int stride = size - 3;
        using (TerrainReportData report = TerrainReportData.Generate(heights, meshes, null, centre, 3, 0))
        {
            Assert.Greater(report.MeanAbsoluteErosionChange, 0.0001d);
            foreach (TerrainReportData.Chunk chunk in report.Chunks)
            {
                Vector2 world = TerrainGrid.ChunkCoordinateToWorldPosition(chunk.coordinate, meshes);
                HeightMap expected = HeightMapGenerator.GenerateHeightMap(size, size, heights, world / meshes.meshScale);
                int column = chunk.coordinate.x - centre.x + 1;
                int row = centre.y + 1 - chunk.coordinate.y;
                for (int y = 1; y < size - 1; y++) for (int x = 1; x < size - 1; x++)
                    Assert.That(report.Maps[(int)TerrainReportStage.FinalHeight][column * stride + x - 1, row * stride + y - 1],
                        Is.EqualTo(expected.values[x, y]).Within(0.0001f));
                Mesh mesh = MeshGenerator.GenerateTerrainMesh(expected.values, meshes, 0).CreateMesh();
                try { CollectionAssert.AreEqual(mesh.vertices, chunk.mesh.vertices); }
                finally { Object.DestroyImmediate(mesh); }
            }
        }
    }

    [Test]
    public void OffscreenEnvironmentSamplesUseErodedHeightNormalsAndWaterClassification()
    {
        TerrainEnvironmentSampler sampler = new TerrainEnvironmentSampler(heights, meshes, environment);
        Vector2[] positions = { Vector2.zero, new Vector2(-43f, 17f), new Vector2(meshes.meshWorldSize * 0.5f, 0f) };
        foreach (Vector2 position in positions)
        {
            EnvironmentSample sample = sampler.Sample(position);
            Vector2Int coordinate = TerrainGrid.WorldToChunkCoordinate(position, meshes);
            Vector2 world = TerrainGrid.ChunkCoordinateToWorldPosition(coordinate, meshes);
            HeightMap map = HeightMapGenerator.GenerateHeightMap(meshes.numVertsPerLine, meshes.numVertsPerLine, heights, world / meshes.meshScale);
            Assert.IsTrue(EnvironmentSampler.TrySample(position, map, heights.minHeight, heights.maxHeight, environment, null,
                world, meshes.numVertsPerLine, meshes.meshScale, out EnvironmentSample expected));
            Assert.AreEqual(expected.position, sample.position);
            Assert.AreEqual(expected.surfaceNormal, sample.surfaceNormal);
            Assert.AreEqual(expected.isWater, sample.isWater);
            Assert.AreEqual(expected.biome, sample.biome);
        }
    }

    [Test]
    public void LocalNormalizationIsRejectedWithActionableMessage()
    {
        heights.noiseSettings.normalizeMode = Noise.NormalizeMode.Local;
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => HydraulicErosionCache.Get(heights));
        StringAssert.Contains("Global", error.Message);
    }

    [Test]
    public void CancelledBakeDoesNotLeavePartialCachedResults()
    {
        Assert.Throws<OperationCanceledException>(() => HydraulicErosionCache.Get(heights, progress => progress > 0.2f));
        HydraulicErosionMap map = HydraulicErosionCache.Get(heights);
        Assert.IsNotNull(map);
        Assert.AreSame(map, HydraulicErosionCache.Get(heights));
    }

    [Test]
    public void ChangeTextureUsesBlueForErosionOrangeForDepositionAndWhiteForZero()
    {
        Texture2D texture = TerrainReportData.CreateChangeTexture(new float[,] { { -2f }, { 0f }, { 2f } }, 2f);
        try
        {
            Assert.Greater(texture.GetPixel(0, 0).b, texture.GetPixel(0, 0).r);
            Assert.AreEqual(Color.white, texture.GetPixel(1, 0));
            Assert.Greater(texture.GetPixel(2, 0).r, texture.GetPixel(2, 0).b);
        }
        finally { Object.DestroyImmediate(texture); }
    }
}
