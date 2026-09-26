using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class SeasonalEnvironmentTests
{
    HeightMapSettings heights;
    MeshSettings meshes;
    TextureData textures;
    VegetationSettings vegetation;
    TerrainEnvironmentSampler sampler;
    const string Folder = "Assets/TerrainGeneration/Settings/";

    [SetUp]
    public void SetUp()
    {
        heights = Object.Instantiate(AssetDatabase.LoadAssetAtPath<HeightMapSettings>(Folder + "HeightMapSettings.asset"));
        meshes = Object.Instantiate(AssetDatabase.LoadAssetAtPath<MeshSettings>(Folder + "MeshSettings.asset"));
        textures = AssetDatabase.LoadAssetAtPath<TextureData>(Folder + "TextureData.asset");
        vegetation = AssetDatabase.LoadAssetAtPath<VegetationSettings>(Folder + "VegetationSettings.asset");
        sampler = new TerrainEnvironmentSampler(heights, meshes, textures.environmentDefinitions, vegetation);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(heights);
        Object.DestroyImmediate(meshes);
    }

    static EnvironmentSample Forest(float temperature = 0.6f) => new EnvironmentSample
    {
        isValid = true, isLand = true, biome = BiomeId.BorealForest,
        position = new Vector3(-127f, 40f, 83f), height = 40f, normalizedHeight = 0.45f,
        surfaceNormal = Vector3.up, slope = 0f, moisture = 0.5f, temperature = temperature,
        grassBiomass = 0.8f, treeCover = 0.7f, waterAvailability = 0.5f
    };

    [TestCase(BorealSeason.Spring)]
    [TestCase(BorealSeason.Summer)]
    [TestCase(BorealSeason.Fall)]
    [TestCase(BorealSeason.Winter)]
    public void SeasonalOverlayPreservesGeometryBiomeWaterAndResourceStock(BorealSeason season)
    {
        EnvironmentSample original = Forest();
        SeasonalEnvironmentSample result = SeasonalEnvironment.Evaluate(original, SeasonState.For(season));
        Assert.AreEqual(original, result.baseEnvironment);
        Assert.That(result.temperature, Is.InRange(0f, 1f));
        Assert.That(result.moisture, Is.InRange(0f, 1f));
        Assert.That(result.snowCover, Is.InRange(0f, 1f));
        Assert.That(result.accessibleGrassBiomass, Is.InRange(0f, original.grassBiomass));
        Assert.That(result.movementCostMultiplier, Is.InRange(1f, 1.75f));
        Assert.AreEqual(1f - result.temperature, result.coldness);
    }

    [Test]
    public void WinterIsColderSnowierAndLessAccessibleWithoutDeletingFood()
    {
        SeasonalEnvironmentSample summer = SeasonalEnvironment.Evaluate(Forest(), SeasonState.For(BorealSeason.Summer));
        SeasonalEnvironmentSample winter = SeasonalEnvironment.Evaluate(Forest(), SeasonState.For(BorealSeason.Winter));
        Assert.Less(winter.temperature, summer.temperature);
        Assert.Greater(winter.snowCover, summer.snowCover);
        Assert.Less(winter.accessibleGrassBiomass, summer.accessibleGrassBiomass);
        Assert.Greater(winter.accessibleGrassBiomass, 0f);
        Assert.AreEqual(summer.baseEnvironment.grassBiomass, winter.baseEnvironment.grassBiomass);
        Assert.Less(winter.grassGrowthMultiplier, summer.grassGrowthMultiplier);
        Assert.Greater(winter.movementCostMultiplier, summer.movementCostMultiplier);
    }

    [Test]
    public void WaterStaysWaterAndDoesNotGainSnowOrTerrestrialFood()
    {
        EnvironmentSample water = Forest();
        water.isLand = false; water.isWater = true; water.biome = BiomeId.DeepWater;
        SeasonalEnvironmentSample sample = SeasonalEnvironment.Evaluate(water, SeasonState.For(BorealSeason.Winter));
        Assert.IsTrue(sample.baseEnvironment.isWater);
        Assert.AreEqual(0f, sample.snowCover);
        Assert.AreEqual(0f, sample.accessibleGrassBiomass);
        Assert.AreEqual(0f, sample.grassGrowthMultiplier);
        Assert.AreEqual(water.waterAvailability, sample.baseEnvironment.waterAvailability);
        Assert.AreEqual(Color.blue, SeasonalReportData.Colorize(Color.blue, sample, SeasonalReportSurface.Ground, 1f));
    }

    [Test]
    public void ColdGentleGroundRetainsMoreSnowThanWarmOrSteepGround()
    {
        SeasonState spring = SeasonState.For(BorealSeason.Spring);
        EnvironmentSample gentle = Forest(0.25f);
        EnvironmentSample steep = gentle; steep.slope = 0.9f;
        Assert.Greater(SeasonalEnvironment.Evaluate(gentle, spring).snowCover, SeasonalEnvironment.Evaluate(Forest(0.8f), spring).snowCover);
        Assert.Greater(SeasonalEnvironment.Evaluate(gentle, spring).snowCover, SeasonalEnvironment.Evaluate(steep, spring).snowCover);
    }

    [TestCase(-125f, -125f)]
    [TestCase(62.5f, 0f)]
    [TestCase(3700f, -2900f)]
    public void OffscreenQueriesAreDeterministicAndSeasonChangesReuseHeightCache(float x, float z)
    {
        Vector2 position = new Vector2(x, z);
        EnvironmentSample baseline = sampler.Sample(position);
        int cached = sampler.CachedChunkCount;
        SeasonalEnvironmentSample first = sampler.SampleSeasonal(position, SeasonState.For(BorealSeason.Winter));
        SeasonalEnvironmentSample second = sampler.SampleSeasonal(position, SeasonState.For(BorealSeason.Winter));
        SeasonalEnvironmentSample summer = sampler.SampleSeasonal(position, SeasonState.For(BorealSeason.Summer));
        Assert.AreEqual(first, second);
        Assert.AreEqual(baseline, first.baseEnvironment);
        Assert.AreEqual(baseline, summer.baseEnvironment);
        Assert.AreEqual(cached, sampler.CachedChunkCount);
        Assert.AreEqual(SeasonalEnvironment.Evaluate(baseline, SeasonState.For(BorealSeason.Winter)), first);
    }

    [Test]
    public void YearInterpolationWrapsContinuouslyAndMatchesSeasonAnchors()
    {
        for (int i = 0; i < 4; i++) Assert.AreEqual(SeasonState.For((BorealSeason)i), SeasonState.AtYearProgress(i * 0.25f));
        Assert.AreEqual(SeasonState.AtYearProgress(0f), SeasonState.AtYearProgress(1f));
        Assert.AreEqual(SeasonState.AtYearProgress(0.75f), SeasonState.AtYearProgress(-0.25f));
        Assert.That(SeasonState.AtYearProgress(0.99999f).temperatureOffset,
            Is.EqualTo(SeasonState.AtYearProgress(0.00001f).temperatureOffset).Within(0.0001f));
        Assert.Throws<ArgumentOutOfRangeException>(() => SeasonState.AtYearProgress(float.NaN));
    }

    [Test]
    public void InvalidQueriesAndNonFiniteModifiersAreHandled()
    {
        Assert.IsFalse(sampler.TrySampleSeasonal(new Vector2(float.NaN, 0f), SeasonState.For(BorealSeason.Winter), out _));
        Assert.IsFalse(SeasonalEnvironment.Evaluate(default, SeasonState.For(BorealSeason.Spring)).IsValid);
        SeasonalEnvironmentSample sample = SeasonalEnvironment.Evaluate(Forest(), new SeasonState
        {
            temperatureOffset = float.NaN, moistureOffset = float.PositiveInfinity,
            snowAmount = 8f, dormancy = -9f, growthMultiplier = float.NaN
        });
        Assert.That(sample.temperature, Is.InRange(0f, 1f));
        Assert.That(sample.snowCover, Is.InRange(0f, 1f));
        Assert.That(sample.grassGrowthMultiplier, Is.InRange(0f, 1f));
    }

    [Test]
    public void FallChangesGrassButKeepsPineFoliageGreen()
    {
        SeasonalEnvironmentSample fall = SeasonalEnvironment.Evaluate(Forest(), SeasonState.For(BorealSeason.Fall));
        Color green = new Color(0.09f, 0.2f, 0.13f);
        Color tree = SeasonalReportData.Colorize(green, fall, SeasonalReportSurface.Tree, 0f);
        Color grass = SeasonalReportData.Colorize(green, fall, SeasonalReportSurface.Grass, 0f);
        Assert.Less(tree.r, tree.g);
        Assert.Greater(grass.r, tree.r);
        Assert.That(tree.r / tree.g, Is.EqualTo(green.r / green.g).Within(0.00001f));
    }

    [Test]
    public void PreviewReusesIdenticalGeometryAndDoesNotMutateSourceSettings()
    {
        string settingsBefore = EditorJsonUtility.ToJson(heights) + EditorJsonUtility.ToJson(meshes)
            + EditorJsonUtility.ToJson(vegetation) + EditorJsonUtility.ToJson(textures);
        using (SeasonalReportData data = SeasonalReportData.Generate(heights, meshes, textures, vegetation, new Vector2Int(4, 0), 1))
        {
            for (int season = 1; season < 4; season++)
            {
                Assert.AreEqual(data.Frames[0].meshes.Count, data.Frames[season].meshes.Count);
                for (int i = 0; i < data.Frames[0].meshes.Count; i++)
                {
                    Mesh first = data.Frames[0].meshes[i].mesh;
                    Mesh current = data.Frames[season].meshes[i].mesh;
                    CollectionAssert.AreEqual(first.vertices, current.vertices);
                    CollectionAssert.AreEqual(first.triangles, current.triangles);
                    CollectionAssert.AreEqual(first.normals, current.normals);
                    Assert.AreEqual(data.Frames[0].meshes[i].offset, data.Frames[season].meshes[i].offset);
                }
            }
        }
        Assert.AreEqual(settingsBefore, EditorJsonUtility.ToJson(heights) + EditorJsonUtility.ToJson(meshes)
            + EditorJsonUtility.ToJson(vegetation) + EditorJsonUtility.ToJson(textures));
    }

    [Test]
    public void PreviewCanBeCancelledWithoutChangingSourceSettings()
    {
        Assert.Throws<OperationCanceledException>(() => SeasonalReportData.Generate(heights, meshes, textures, vegetation, Vector2Int.zero, 1, _ => true));
    }
}
