using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class HabitatAnalysisTests
{
    // Synthetic worlds span -100 to 100 m, sampled every 10 m: 21 × 21 samples with columns at x = -100, -90 … 100.
    const float Extent = 100f;
    const float Spacing = 10f;
    const int Size = 21;
    const float CellSize = 50f;

    // Temperature indices for 0 °C (comfortable, plants grow) and -28 °C (too cold for plants and animals).
    static readonly float Mild = Mathf.InverseLerp(-28f, 18f, 0f);
    const float Freezing = 0f;

    static readonly HabitatRequirements Founders = HabitatRequirements.Founders;

    static EnvironmentSample Habitat()
    {
        return new EnvironmentSample
        {
            isValid = true,
            isLand = true,
            slopeDegrees = 5f,
            grassBiomass = 0.6f,
            temperature = Mild
        };
    }

    static EnvironmentSample Water()
    {
        return new EnvironmentSample { isValid = true, isWater = true, temperature = Mild };
    }

    static HabitatAnalysis Analyse(Func<Vector2, EnvironmentSample> world, Vector2 start)
    {
        return HabitatAnalysis.Run((Vector2 position, out EnvironmentSample sample) =>
        {
            sample = world(position);
            return true;
        }, Extent, Spacing, Founders, start, CellSize);
    }

    [Test]
    public void RiverSplitsHabitatIntoTwoRegions()
    {
        // Water covers the three middle columns (x = -10, 0, 10), leaving nine columns of habitat each side.
        HabitatAnalysis analysis = Analyse(position => Mathf.Abs(position.x) < 15f ? Water() : Habitat(), new Vector2(-50f, 0f));

        Assert.AreEqual(Size, analysis.Size);
        Assert.AreEqual(18 * Size, analysis.LandSamples);
        Assert.AreEqual(18 * Size, analysis.SuitableSamples);
        Assert.AreEqual(9 * Size, analysis.AccessibleSamples);
        Assert.AreEqual(1f, analysis.Availability, 1e-6f);
        Assert.AreEqual(0.5f, analysis.Accessibility, 1e-6f);
        Assert.AreEqual(2, analysis.HabitatRegionCount);
        Assert.AreEqual(HabitatClass.AccessibleHabitat, analysis.GetClass(0, 10));
        Assert.AreEqual(HabitatClass.Water, analysis.GetClass(10, 10));
        Assert.AreEqual(HabitatClass.IsolatedHabitat, analysis.GetClass(20, 10));
    }

    [Test]
    public void SteepGroundIsABarrierThatCountsAsLand()
    {
        HabitatAnalysis analysis = Analyse(position =>
        {
            EnvironmentSample sample = Habitat();
            if (Mathf.Abs(position.x) < 15f) sample.slopeDegrees = Founders.maximumSlopeDegrees + 5f;
            return sample;
        }, new Vector2(-50f, 0f));

        Assert.AreEqual(Size * Size, analysis.LandSamples);
        Assert.AreEqual(18 * Size, analysis.WalkableSamples);
        Assert.AreEqual(18f / 21f, analysis.Availability, 1e-6f);
        Assert.AreEqual(0.5f, analysis.Accessibility, 1e-6f);
        Assert.AreEqual(HabitatClass.Barrier, analysis.GetClass(10, 10));
    }

    [Test]
    public void ShoreIsABarrierOnlyWhenExcluded()
    {
        Func<Vector2, EnvironmentSample> world = position =>
        {
            EnvironmentSample sample = Habitat();
            sample.isShore = Mathf.Abs(position.x) < 15f;
            return sample;
        };
        HabitatRequirements allowShore = Founders;
        allowShore.excludeShore = false;

        HabitatAnalysis excluded = Analyse(world, new Vector2(-50f, 0f));
        HabitatAnalysis allowed = HabitatAnalysis.Run((Vector2 position, out EnvironmentSample sample) =>
        {
            sample = world(position);
            return true;
        }, Extent, Spacing, allowShore, new Vector2(-50f, 0f), CellSize);

        Assert.AreEqual(0.5f, excluded.Accessibility, 1e-6f);
        Assert.AreEqual(1f, allowed.Accessibility, 1e-6f);
        Assert.AreEqual(1, allowed.HabitatRegionCount);
    }

    [Test]
    public void ColdGroundIsPassableButNotHabitat()
    {
        HabitatAnalysis analysis = Analyse(position =>
        {
            EnvironmentSample sample = Habitat();
            if (Mathf.Abs(position.x) < 15f) sample.temperature = Freezing;
            return sample;
        }, new Vector2(-50f, 0f));

        Assert.AreEqual(Size * Size, analysis.WalkableSamples);
        Assert.AreEqual(18 * Size, analysis.SuitableSamples);
        Assert.AreEqual(1f, analysis.Accessibility, 1e-6f);
        Assert.AreEqual(1, analysis.HabitatRegionCount);
        Assert.AreEqual(HabitatClass.Passable, analysis.GetClass(10, 10));
    }

    [Test]
    public void BareGroundIsPassableButNotHabitat()
    {
        HabitatAnalysis analysis = Analyse(position =>
        {
            EnvironmentSample sample = Habitat();
            if (position.y > 45f) sample.grassBiomass = Founders.minimumGrassBiomass * 0.5f;
            return sample;
        }, Vector2.zero);

        // Rows at z = 50 … 100 have too little grass for plants.
        Assert.AreEqual(15f / 21f, analysis.Availability, 1e-6f);
        Assert.AreEqual(1f, analysis.Accessibility, 1e-6f);
    }

    [Test]
    public void StartMovesToTheNearestWalkableGround()
    {
        HabitatAnalysis analysis = Analyse(position => Mathf.Abs(position.x) < 15f ? Water() : Habitat(), new Vector2(5f, 0f));

        Assert.IsTrue(analysis.HasStart);
        Assert.AreEqual(new Vector2(20f, 0f), analysis.Start);
        Assert.AreEqual(15f, analysis.StartOffset, 1e-4f);
        Assert.AreEqual(HabitatClass.AccessibleHabitat, analysis.GetClass(20, 10));
        Assert.AreEqual(HabitatClass.IsolatedHabitat, analysis.GetClass(0, 10));
    }

    [Test]
    public void AllWaterHasNoStartAndNoHabitat()
    {
        HabitatAnalysis analysis = Analyse(position => Water(), Vector2.zero);

        Assert.IsFalse(analysis.HasStart);
        Assert.AreEqual(0, analysis.LandSamples);
        Assert.AreEqual(0f, analysis.Availability);
        Assert.AreEqual(0f, analysis.Accessibility);
        Assert.AreEqual(0, analysis.HabitatRegionCount);
    }

    [Test]
    public void CellSummariesAddUpToTheTotals()
    {
        HabitatAnalysis analysis = Analyse(position => Mathf.Abs(position.x) < 15f ? Water() : Habitat(), new Vector2(-50f, 0f));

        int samples = 0, land = 0, walkable = 0, suitable = 0, accessible = 0;
        foreach (HabitatCellSummary cell in analysis.Cells)
        {
            samples += cell.samples;
            land += cell.land;
            walkable += cell.walkable;
            suitable += cell.suitable;
            accessible += cell.accessible;
            Assert.AreEqual(cell.coordinate, TerrainGrid.WorldToCell(TerrainGrid.CellToWorldPosition(cell.coordinate, CellSize), CellSize));
        }

        Assert.AreEqual(Size * Size, samples);
        Assert.AreEqual(analysis.LandSamples, land);
        Assert.AreEqual(analysis.WalkableSamples, walkable);
        Assert.AreEqual(analysis.SuitableSamples, suitable);
        Assert.AreEqual(analysis.AccessibleSamples, accessible);
    }

    [Test]
    public void FailedSamplesAreNoDataAndNeverLand()
    {
        HabitatAnalysis analysis = HabitatAnalysis.Run((Vector2 position, out EnvironmentSample sample) =>
        {
            sample = Habitat();
            return position.x < 0f;
        }, Extent, Spacing, Founders, new Vector2(-50f, 0f), CellSize);

        Assert.AreEqual(10 * Size, analysis.LandSamples);
        Assert.AreEqual(HabitatClass.NoData, analysis.GetClass(20, 10));
        Assert.AreEqual(1f, analysis.Accessibility, 1e-6f);
    }

    [Test]
    public void InvalidSpacingIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HabitatAnalysis.Run(
            (Vector2 position, out EnvironmentSample sample) => { sample = Habitat(); return true; },
            Extent, 0f, Founders, Vector2.zero, CellSize));
        // 20,001 samples per axis is more than the limit.
        Assert.Throws<ArgumentOutOfRangeException>(() => HabitatAnalysis.Run(
            (Vector2 position, out EnvironmentSample sample) => { sample = Habitat(); return true; },
            Extent, 0.01f, Founders, Vector2.zero, CellSize));
    }

    [Test]
    public void CancellingStopsTheAnalysis()
    {
        Assert.Throws<OperationCanceledException>(() => HabitatAnalysis.Run(
            (Vector2 position, out EnvironmentSample sample) => { sample = Habitat(); return true; },
            Extent, Spacing, Founders, Vector2.zero, CellSize, progress => progress > 0.5f));
    }

    [Test]
    public void PlantGrowthMatchesThePlantFoodTheBootstrapCreates()
    {
        GameObject food = new GameObject("Plant growth reference");
        try
        {
            FoodItem item = food.AddComponent<FoodItem>();
            item.foodType = FoodType.Plant;
            item.temperatureAffectsGrowth = true;
            item.minimumGrowthTemperature = Founders.plantMinimumCelsius;
            item.optimalGrowthTemperatureMin = Founders.plantOptimalMinimumCelsius;
            item.optimalGrowthTemperatureMax = Founders.plantOptimalMaximumCelsius;
            item.maximumGrowthTemperature = Founders.plantMaximumCelsius;

            for (float celsius = -30f; celsius <= 30f; celsius += 0.5f)
            {
                Assert.AreEqual(item.EvaluateGrowthMultiplier(celsius), Founders.PlantGrowthRate(celsius), 1e-5f,
                    $"Growth rate differs at {celsius} °C.");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(food);
        }
    }

    [Test]
    public void RealTerrainIsDeterministicAndConsistent()
    {
        const string settingsFolder = "Assets/TerrainGeneration/Settings/";
        HeightMapSettings heightSettings = AssetDatabase.LoadAssetAtPath<HeightMapSettings>(settingsFolder + "HeightMapSettings.asset");
        MeshSettings meshSettings = AssetDatabase.LoadAssetAtPath<MeshSettings>(settingsFolder + "MeshSettings.asset");
        EnvironmentDefinitions definitions = AssetDatabase.LoadAssetAtPath<EnvironmentDefinitions>(settingsFolder + "EnvironmentDefinitions.asset");
        VegetationSettings vegetation = AssetDatabase.LoadAssetAtPath<VegetationSettings>(settingsFolder + "VegetationSettings.asset");
        Assert.IsNotNull(heightSettings);
        Assert.IsNotNull(meshSettings);
        Assert.IsNotNull(definitions);

        // A coarse grid keeps the test quick; the seed is the one the seed screen gives "FOREST-001".
        HeightMapSettings heights = UnityEngine.Object.Instantiate(heightSettings);
        try
        {
            heights.noiseSettings.seed = SeedManager.GetSeed("FOREST-001", "Terrain");
            HabitatAnalysis first = HabitatAnalysis.Run(new TerrainEnvironmentSampler(heights, meshSettings, definitions, vegetation),
                Founders, 160f, Vector2.zero, 250f);
            HabitatAnalysis second = HabitatAnalysis.Run(new TerrainEnvironmentSampler(heights, meshSettings, definitions, vegetation),
                Founders, 160f, Vector2.zero, 250f);

            Assert.Greater(first.LandSamples, 0);
            Assert.IsTrue(first.HasStart);
            Assert.AreEqual(first.LandSamples, second.LandSamples);
            Assert.AreEqual(first.WalkableSamples, second.WalkableSamples);
            Assert.AreEqual(first.SuitableSamples, second.SuitableSamples);
            Assert.AreEqual(first.AccessibleSamples, second.AccessibleSamples);
            Assert.AreEqual(first.Start, second.Start);
            Assert.LessOrEqual(first.WalkableSamples, first.LandSamples);
            Assert.LessOrEqual(first.SuitableSamples, first.WalkableSamples);
            Assert.LessOrEqual(first.AccessibleSamples, first.SuitableSamples);
            Assert.That(first.Availability, Is.InRange(0f, 1f));
            Assert.That(first.Accessibility, Is.InRange(0f, 1f));
        }
        finally
        {
            HydraulicErosionCache.Invalidate(heights);
            UnityEngine.Object.DestroyImmediate(heights);
        }
    }
}
