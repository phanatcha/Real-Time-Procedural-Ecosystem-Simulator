using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class WorldSeedsTests
{
    const string Folder = "Assets/TerrainGeneration/Settings/";
    static readonly string[] SeedStrings = { "FOREST-001", "MOSSY-FJORD", "PALE-CAIRN", "WILD-ISLE", "QUIET-LAKE" };

    HeightMapSettings heights;
    VegetationSettings vegetation;
    EnvironmentDefinitions environment;

    [SetUp]
    public void SetUp()
    {
        heights = Object.Instantiate(AssetDatabase.LoadAssetAtPath<HeightMapSettings>(Folder + "HeightMapSettings.asset"));
        vegetation = Object.Instantiate(AssetDatabase.LoadAssetAtPath<VegetationSettings>(Folder + "VegetationSettings.asset"));
        environment = Object.Instantiate(AssetDatabase.LoadAssetAtPath<EnvironmentDefinitions>(Folder + "EnvironmentDefinitions.asset"));
    }

    [TearDown]
    public void TearDown()
    {
        HydraulicErosionCache.Invalidate(heights);
        Object.DestroyImmediate(heights);
        Object.DestroyImmediate(vegetation);
        Object.DestroyImmediate(environment);
    }

    [Test]
    public void EveryPartOfTheWorldGetsItsOwnSeed()
    {
        foreach (string seedString in SeedStrings)
        {
            WorldSeeds seeds = SeedManager.GetWorldSeeds(seedString);
            CollectionAssert.AllItemsAreUnique(new[]
            {
                seeds.terrain, seeds.ridges, seeds.rivers, seeds.lakes,
                seeds.vegetation, seeds.moisture, seeds.temperature, seeds.resourcePatches
            }, seedString);
            // The terrain keeps the seed it had before the other systems followed the world seed.
            Assert.AreEqual(SeedManager.GetSeed(seedString, "Terrain"), seeds.terrain, seedString);
        }
    }

    [Test]
    public void TheSameWorldSeedAlwaysGivesTheSameWorld()
    {
        Assert.AreEqual(SeedManager.GetWorldSeeds("FOREST-001"), SeedManager.GetWorldSeeds("FOREST-001"));

        EnvironmentDefinitions again = Object.Instantiate(environment);
        try
        {
            SeedManager.GetWorldSeeds("FOREST-001").ApplyTo(environment);
            SeedManager.GetWorldSeeds("FOREST-001").ApplyTo(again);
            Assert.AreEqual(environment.moistureOffset, again.moistureOffset);
            Assert.AreEqual(environment.temperatureOffset, again.temperatureOffset);
            Assert.AreEqual(environment.resourcePatchOffset, again.resourcePatchOffset);
        }
        finally
        {
            Object.DestroyImmediate(again);
        }
    }

    [Test]
    public void ApplyingWorldSeedsReplacesOnlyTheSeeds()
    {
        float riverScale = heights.riverSettings.scale;
        float lakeThreshold = heights.lakeSettings.threshold;
        float ridgeStrength = heights.ridgeSettings.strength;
        float treeDensity = vegetation.trees.density;
        float moistureScale = environment.moistureScale;
        float waterLevel = environment.normalizedWaterLevel;

        WorldSeeds seeds = SeedManager.GetWorldSeeds("MOSSY-FJORD");
        seeds.ApplyTo(heights);
        seeds.ApplyTo(vegetation);
        seeds.ApplyTo(environment);

        Assert.AreEqual(seeds.terrain, heights.noiseSettings.seed);
        Assert.AreEqual(seeds.ridges, heights.ridgeSettings.seed);
        Assert.AreEqual(seeds.rivers, heights.riverSettings.seed);
        Assert.AreEqual(seeds.lakes, heights.lakeSettings.seed);
        Assert.AreEqual(seeds.vegetation, vegetation.seed);

        Assert.AreEqual(riverScale, heights.riverSettings.scale);
        Assert.AreEqual(lakeThreshold, heights.lakeSettings.threshold);
        Assert.AreEqual(ridgeStrength, heights.ridgeSettings.strength);
        Assert.AreEqual(treeDensity, vegetation.trees.density);
        Assert.AreEqual(moistureScale, environment.moistureScale);
        Assert.AreEqual(waterLevel, environment.normalizedWaterLevel);
    }

    [Test]
    public void NoiseOffsetsStayWithinPrecisionRange()
    {
        foreach (string seedString in SeedStrings)
        {
            SeedManager.GetWorldSeeds(seedString).ApplyTo(environment);
            foreach (Vector2 offset in new[] { environment.moistureOffset, environment.temperatureOffset, environment.resourcePatchOffset })
            {
                Assert.LessOrEqual(Mathf.Abs(offset.x), 8192f, seedString);
                Assert.LessOrEqual(Mathf.Abs(offset.y), 8192f, seedString);
            }
        }
    }

    [Test]
    public void DifferentWorldSeedsMoveRiversAndLakes()
    {
        HeightMapSettings other = Object.Instantiate(heights);
        try
        {
            SeedManager.GetWorldSeeds("FOREST-001").ApplyTo(heights);
            SeedManager.GetWorldSeeds("MOSSY-FJORD").ApplyTo(other);
            heights.useFalloff = false;
            other.useFalloff = false;

            // With no island falloff and no ridge, the ground stays at 0.72 everywhere, inside both the river and
            // the lake height ranges, so only the river and lake noise decide where water is carved.
            int riverDifferences = 0;
            int lakeDifferences = 0;
            for (int x = -1000; x <= 1000; x += 20)
            {
                for (int y = -1000; y <= 1000; y += 20)
                {
                    Vector2 position = new Vector2(x, y);
                    TerrainHeightEvaluation first = TerrainHeightEvaluator.Evaluate(0.72f, 0f, position, heights, heights.heightCurve);
                    TerrainHeightEvaluation second = TerrainHeightEvaluator.Evaluate(0.72f, 0f, position, other, other.heightCurve);
                    if (Mathf.Abs(first.riverStrength - second.riverStrength) > 0.5f) riverDifferences++;
                    if (Mathf.Abs(first.lakeStrength - second.lakeStrength) > 0.5f) lakeDifferences++;
                }
            }

            Assert.Greater(riverDifferences, 0);
            Assert.Greater(lakeDifferences, 0);
        }
        finally
        {
            HydraulicErosionCache.Invalidate(other);
            Object.DestroyImmediate(other);
        }
    }

    [Test]
    public void DifferentWorldSeedsMoveMoistureTemperatureAndPlantPatches()
    {
        EnvironmentDefinitions other = Object.Instantiate(environment);
        try
        {
            SeedManager.GetWorldSeeds("FOREST-001").ApplyTo(environment);
            SeedManager.GetWorldSeeds("MOSSY-FJORD").ApplyTo(other);

            float moisture = 0f;
            float temperature = 0f;
            float patches = 0f;
            for (int x = -2500; x <= 2500; x += 50)
            {
                for (int y = -2500; y <= 2500; y += 50)
                {
                    Vector2 position = new Vector2(x, y);
                    moisture = Mathf.Max(moisture, Mathf.Abs(environment.SampleMoisture(position) - other.SampleMoisture(position)));
                    temperature = Mathf.Max(temperature,
                        Mathf.Abs(environment.SampleTemperature(position, 0.5f) - other.SampleTemperature(position, 0.5f)));
                    patches = Mathf.Max(patches, Mathf.Abs(environment.SampleResourcePatch(position, EnvironmentResourceType.Grass) -
                                                           other.SampleResourcePatch(position, EnvironmentResourceType.Grass)));
                }
            }

            Assert.Greater(moisture, 0.2f);
            Assert.Greater(temperature, 0.02f);
            Assert.Greater(patches, 0.05f);
        }
        finally
        {
            Object.DestroyImmediate(other);
        }
    }

    // World seeds use the whole int range. The vegetation hash used to add seed * 13.7 to the cell position it
    // hashes, which lost the position to float rounding and gave every cell in a row the same plant offset.
    [Test]
    public void PlantsDoNotLineUpWithAFullRangeWorldSeed()
    {
        MeshSettings meshes = Object.Instantiate(AssetDatabase.LoadAssetAtPath<MeshSettings>(Folder + "MeshSettings.asset"));
        try
        {
            WorldSeeds seeds = SeedManager.GetWorldSeeds("FOREST-001");
            seeds.ApplyTo(heights);
            seeds.ApplyTo(vegetation);
            seeds.ApplyTo(environment);
            heights.erosionSettings.enabled = false;

            float cellSize = vegetation.grass.cellSize;
            Vector2 half = Vector2.one * meshes.meshWorldSize * 0.5f;
            Dictionary<Vector2Int, float> offsetInCell = new Dictionary<Vector2Int, float>();
            for (int chunkY = -2; chunkY <= 2; chunkY++)
            {
                for (int chunkX = -2; chunkX <= 2; chunkX++)
                {
                    Vector2 centre = TerrainGrid.ChunkCoordinateToWorldPosition(new Vector2Int(chunkX, chunkY), meshes);
                    HeightMap map = HeightMapGenerator.GenerateHeightMap(meshes.numVertsPerLine, meshes.numVertsPerLine,
                        heights, centre / meshes.meshScale);
                    VegetationPlacementData placements = VegetationGenerator.GeneratePlacements(centre - half, centre + half,
                        map, heights.minHeight, heights.maxHeight, vegetation, environment, meshes.numVertsPerLine, meshes.meshScale);
                    foreach (VegetationInstance grass in placements.grass)
                    {
                        float cellX = grass.position.x / cellSize;
                        Vector2Int cell = new Vector2Int(Mathf.FloorToInt(cellX), Mathf.FloorToInt(grass.position.z / cellSize));
                        offsetInCell[cell] = cellX - cell.x;
                    }
                }
            }

            int neighbours = 0;
            int sameOffset = 0;
            foreach (KeyValuePair<Vector2Int, float> entry in offsetInCell)
            {
                if (!offsetInCell.TryGetValue(entry.Key + Vector2Int.right, out float next)) continue;
                neighbours++;
                if (Mathf.Abs(entry.Value - next) < 0.001f) sameOffset++;
            }

            Assert.Greater(neighbours, 20, "Too little grass near the start to judge.");
            Assert.Less(sameOffset, neighbours / 10);
        }
        finally
        {
            Object.DestroyImmediate(meshes);
        }
    }
}
