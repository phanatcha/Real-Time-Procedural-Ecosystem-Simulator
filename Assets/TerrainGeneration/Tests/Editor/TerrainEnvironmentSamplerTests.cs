using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class TerrainEnvironmentSamplerTests
{
    const string HeightSettingsPath = "Assets/TerrainGeneration/Settings/HeightMapSettings.asset";
    const string MeshSettingsPath = "Assets/TerrainGeneration/Settings/MeshSettings.asset";
    const string EnvironmentDefinitionsPath = "Assets/TerrainGeneration/Settings/EnvironmentDefinitions.asset";
    const string VegetationSettingsPath = "Assets/TerrainGeneration/Settings/VegetationSettings.asset";

    HeightMapSettings heightSettings;
    MeshSettings meshSettings;
    EnvironmentDefinitions definitions;
    VegetationSettings vegetationSettings;

    [SetUp]
    public void SetUp()
    {
        heightSettings = AssetDatabase.LoadAssetAtPath<HeightMapSettings>(HeightSettingsPath);
        meshSettings = AssetDatabase.LoadAssetAtPath<MeshSettings>(MeshSettingsPath);
        definitions = AssetDatabase.LoadAssetAtPath<EnvironmentDefinitions>(EnvironmentDefinitionsPath);
        vegetationSettings = AssetDatabase.LoadAssetAtPath<VegetationSettings>(VegetationSettingsPath);

        Assert.IsNotNull(heightSettings);
        Assert.IsNotNull(meshSettings);
        Assert.IsNotNull(definitions);
        Assert.IsNotNull(vegetationSettings);
    }

    [Test]
    public void SamplerWorksWithoutTerrainGameObjectOrMesh()
    {
        Assert.IsFalse(typeof(Object).IsAssignableFrom(typeof(TerrainEnvironmentSampler)));

        TerrainEnvironmentSampler sampler = CreateSampler();
        bool succeeded = sampler.TrySample(new Vector2(918.25f, -743.75f), out EnvironmentSample sample);

        Assert.IsTrue(succeeded);
        Assert.IsTrue(sample.isValid);
        Assert.AreEqual(1, sampler.CachedChunkCount);
    }

    [Test]
    public void ResultsAreDeterministicAcrossFreshSamplersAndRepeatedQueries()
    {
        TerrainEnvironmentSampler firstSampler = CreateSampler();
        TerrainEnvironmentSampler secondSampler = CreateSampler();
        float halfChunk = meshSettings.meshWorldSize * 0.5f;
        Vector2[] positions =
        {
            Vector2.zero,
            new Vector2(37.125f, -91.875f),
            new Vector2(-412.5f, 238.25f),
            new Vector2(halfChunk, 17.25f),
            new Vector2(-halfChunk, -22.75f),
            new Vector2(2875f, -2875f)
        };

        foreach (Vector2 position in positions)
        {
            Assert.IsTrue(firstSampler.TrySample(position, out EnvironmentSample first));
            Assert.IsTrue(firstSampler.TrySample(position, out EnvironmentSample repeated));
            Assert.IsTrue(secondSampler.TrySample(position, out EnvironmentSample second));
            AssertSamplesEqual(first, repeated);
            AssertSamplesEqual(first, second);
        }
    }

    [Test]
    public void SharedChunkBordersProduceMatchingSamples()
    {
        float halfChunk = meshSettings.meshWorldSize * 0.5f;

        AssertBorderMatches(
            new Vector2(halfChunk, 17.25f),
            Vector2Int.zero,
            new Vector2Int(1, 0));
        AssertBorderMatches(
            new Vector2(-halfChunk, -22.75f),
            Vector2Int.zero,
            new Vector2Int(-1, 0));
        AssertBorderMatches(
            new Vector2(31.5f, halfChunk),
            Vector2Int.zero,
            new Vector2Int(0, 1));
        AssertBorderMatches(
            new Vector2(-14.25f, -halfChunk),
            Vector2Int.zero,
            new Vector2Int(0, -1));
    }

    [Test]
    public void CoordinateHelpersHandleNegativePositionsAndBoundaryOwnership()
    {
        Assert.AreEqual(new Vector2Int(-1, -2), TerrainGrid.WorldToCell(new Vector2(-0.001f, -5.001f), 5f));
        Assert.AreEqual(new Vector2(-2.5f, -7.5f), TerrainGrid.CellToWorldPosition(new Vector2Int(-1, -2), 5f));

        float halfChunk = meshSettings.meshWorldSize * 0.5f;
        Assert.AreEqual(Vector2Int.zero, TerrainGrid.WorldToChunkCoordinate(new Vector2(-halfChunk, 0f), meshSettings));
        Assert.AreEqual(new Vector2Int(-1, 0), TerrainGrid.WorldToChunkCoordinate(new Vector2(-halfChunk - 0.001f, 0f), meshSettings));
        Assert.AreEqual(new Vector2Int(1, 0), TerrainGrid.WorldToChunkCoordinate(new Vector2(halfChunk, 0f), meshSettings));

        TerrainEnvironmentSampler sampler = CreateSampler();
        Vector2 negativePosition = new Vector2(-meshSettings.meshWorldSize * 3.2f, -meshSettings.meshWorldSize * 2.7f);
        Assert.IsTrue(sampler.TrySample(negativePosition, out EnvironmentSample sample));
        Assert.That(sample.position.x, Is.EqualTo(negativePosition.x).Within(0.0001f));
        Assert.That(sample.position.z, Is.EqualTo(negativePosition.y).Within(0.0001f));
    }

    [Test]
    public void WaterClassificationUsesCanonicalThresholds()
    {
        float farTerrainDistance = (heightSettings.worldRadius + meshSettings.numVertsPerLine) * meshSettings.meshScale;
        TerrainEnvironmentSampler sampler = CreateSampler();

        Assert.IsTrue(sampler.TrySample(new Vector2(farTerrainDistance, farTerrainDistance), out EnvironmentSample water));
        Assert.IsTrue(water.isWater);
        Assert.IsFalse(water.isLand);
        Assert.AreEqual(BiomeId.DeepWater, water.biome);
        Assert.That(water.waterAvailability, Is.EqualTo(1f).Within(0.0001f));

        Assert.IsTrue(definitions.IsWater(definitions.ShorelineThreshold - 0.0001f));
        Assert.IsFalse(definitions.IsWater(definitions.ShorelineThreshold));
        Assert.IsTrue(definitions.IsShore(definitions.ShorelineThreshold));
    }

    [Test]
    public void HydrologyValuesAreIncludedInTheEnvironmentSample()
    {
        TerrainEnvironmentSampler sampler = CreateSampler();
        Assert.IsTrue(sampler.TrySample(new Vector2(126.75f, -88.5f), out EnvironmentSample sample));

        Assert.That(sample.riverStrength, Is.InRange(0f, 1f));
        Assert.That(sample.lakeStrength, Is.InRange(0f, 1f));
        Assert.That(sample.hydrology, Is.EqualTo(Mathf.Max(sample.riverStrength, sample.lakeStrength)).Within(0.0001f));
        Assert.That(sample.waterAvailability, Is.InRange(0f, 1f));
    }

    [Test]
    public void CachedOnlyQueriesDoNotGenerateTerrainData()
    {
        TerrainEnvironmentSampler sampler = CreateSampler();
        Vector2 position = new Vector2(83.25f, -117.75f);

        Assert.IsFalse(sampler.TrySampleCached(position, out _));
        Assert.AreEqual(0, sampler.CachedChunkCount);
        Assert.IsTrue(sampler.TrySample(position, out EnvironmentSample generated));
        Assert.AreEqual(1, sampler.CachedChunkCount);
        Assert.IsTrue(sampler.TrySampleCached(position, out EnvironmentSample cached));
        AssertSamplesEqual(generated, cached);
    }

    TerrainEnvironmentSampler CreateSampler()
    {
        return new TerrainEnvironmentSampler(heightSettings, meshSettings, definitions, vegetationSettings);
    }

    void AssertBorderMatches(Vector2 worldPosition, Vector2Int firstChunk, Vector2Int secondChunk)
    {
        EnvironmentSample first = SampleDirectlyFromChunk(worldPosition, firstChunk);
        EnvironmentSample second = SampleDirectlyFromChunk(worldPosition, secondChunk);
        TerrainEnvironmentSampler sampler = CreateSampler();

        Assert.IsTrue(sampler.TrySample(worldPosition, out EnvironmentSample queried));
        AssertSamplesEqual(first, second);
        AssertSamplesEqual(first, queried);
    }

    EnvironmentSample SampleDirectlyFromChunk(Vector2 worldPosition, Vector2Int chunkCoordinate)
    {
        Vector2 chunkWorldCentre = TerrainGrid.ChunkCoordinateToWorldPosition(chunkCoordinate, meshSettings);
        Vector2 sampleCentre = chunkWorldCentre / meshSettings.meshScale;
        HeightMap heightMap = HeightMapGenerator.GenerateHeightMap(
            meshSettings.numVertsPerLine,
            meshSettings.numVertsPerLine,
            heightSettings,
            sampleCentre);

        Assert.IsTrue(EnvironmentSampler.TrySample(
            worldPosition,
            heightMap,
            heightSettings.minHeight,
            heightSettings.maxHeight,
            definitions,
            vegetationSettings,
            chunkWorldCentre,
            meshSettings.numVertsPerLine,
            meshSettings.meshScale,
            out EnvironmentSample sample));

        return sample;
    }

    static void AssertSamplesEqual(EnvironmentSample expected, EnvironmentSample actual)
    {
        Assert.AreEqual(expected.isValid, actual.isValid);
        Assert.That(actual.position.x, Is.EqualTo(expected.position.x).Within(0.0001f));
        Assert.That(actual.position.y, Is.EqualTo(expected.position.y).Within(0.0001f));
        Assert.That(actual.position.z, Is.EqualTo(expected.position.z).Within(0.0001f));
        Assert.That(actual.normalizedHeight, Is.EqualTo(expected.normalizedHeight).Within(0.0001f));
        Assert.That(actual.surfaceNormal.x, Is.EqualTo(expected.surfaceNormal.x).Within(0.0001f));
        Assert.That(actual.surfaceNormal.y, Is.EqualTo(expected.surfaceNormal.y).Within(0.0001f));
        Assert.That(actual.surfaceNormal.z, Is.EqualTo(expected.surfaceNormal.z).Within(0.0001f));
        Assert.That(actual.slope, Is.EqualTo(expected.slope).Within(0.0001f));
        Assert.That(actual.slopeDegrees, Is.EqualTo(expected.slopeDegrees).Within(0.0001f));
        Assert.AreEqual(expected.isLand, actual.isLand);
        Assert.AreEqual(expected.isWater, actual.isWater);
        Assert.AreEqual(expected.isShore, actual.isShore);
        Assert.AreEqual(expected.biome, actual.biome);
        Assert.That(actual.moisture, Is.EqualTo(expected.moisture).Within(0.0001f));
        Assert.That(actual.temperature, Is.EqualTo(expected.temperature).Within(0.0001f));
        Assert.That(actual.coldness, Is.EqualTo(expected.coldness).Within(0.0001f));
        Assert.That(actual.treeCover, Is.EqualTo(expected.treeCover).Within(0.0001f));
        Assert.That(actual.grassBiomass, Is.EqualTo(expected.grassBiomass).Within(0.0001f));
        Assert.That(actual.rockDensity, Is.EqualTo(expected.rockDensity).Within(0.0001f));
        Assert.That(actual.riverStrength, Is.EqualTo(expected.riverStrength).Within(0.0001f));
        Assert.That(actual.lakeStrength, Is.EqualTo(expected.lakeStrength).Within(0.0001f));
        Assert.That(actual.hydrology, Is.EqualTo(expected.hydrology).Within(0.0001f));
        Assert.That(actual.waterAvailability, Is.EqualTo(expected.waterAvailability).Within(0.0001f));
    }
}
