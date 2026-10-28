using Boreal.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Boreal.TerrainGeneration.Tests
{
    // Rivers and lakes are carved into the land and fill with water at the shared water level; the sea floor beyond
    // the shore is left alone (item 45).
    public class RiverAndLakeCarvingTests
    {
        const string Folder = "Assets/TerrainGeneration/Settings/";

        HeightMapSettings heights;
        float waterLevel;

        [SetUp]
        public void SetUp()
        {
            heights = Object.Instantiate(AssetDatabase.LoadAssetAtPath<HeightMapSettings>(Folder + "HeightMapSettings.asset"));
            EnvironmentDefinitions environment = AssetDatabase.LoadAssetAtPath<EnvironmentDefinitions>(Folder + "EnvironmentDefinitions.asset");
            // The same level TerrainGenerator draws the water surface at.
            waterLevel = Mathf.Lerp(heights.minHeight, heights.maxHeight, environment.ShorelineThreshold);
        }

        [TearDown]
        public void TearDown()
        {
            HydraulicErosionCache.Invalidate(heights);
            Object.DestroyImmediate(heights);
        }

        [Test]
        public void TheShippedRangesStartBelowTheShorelineAndBedsSitUnderTheWater()
        {
            Assert.Less(heights.riverSettings.minHeightPercent, ShorelineInput(), "rivers must reach the sea");
            Assert.Greater(heights.lakeSettings.minHeightPercent, ShorelineInput(), "lakes must stay inland");
            Assert.Less(Height(heights.riverSettings.bedLevel), waterLevel);
            Assert.Less(Height(heights.lakeSettings.bedLevel), waterLevel);
            Assert.GreaterOrEqual(heights.riverSettings.bedLevel, heights.riverSettings.minHeightPercent - 0.0001f,
                "a bed below the bottom of its range would raise the ground it starts on");
        }

        // Ground at 0.7 before the island falloff is land in the middle of the island and sea floor towards its edge.
        // The ranges used to be tested before the falloff, so rivers picked ground the falloff then sank into the sea.
        [Test]
        public void RiversAndLakesLeaveTheSeaFloorAlone()
        {
            heights.riverSettings.width = 0.2f;
            HeightMapSettings dry = Object.Instantiate(heights);
            dry.riverSettings.enabled = false;
            dry.lakeSettings.enabled = false;
            float lowestRange = Mathf.Min(heights.riverSettings.minHeightPercent, heights.lakeSettings.minHeightPercent);
            try
            {
                int seaFloorPoints = 0;
                int carvedLandPoints = 0;
                for (float x = -950f; x <= 950f; x += 10f)
                {
                    for (float y = -950f; y <= 950f; y += 10f)
                    {
                        Vector2 position = new Vector2(x, y);
                        TerrainHeightEvaluation wet = TerrainHeightEvaluator.Evaluate(0.7f, 0f, position, heights, heights.heightCurve);
                        TerrainHeightEvaluation plain = TerrainHeightEvaluator.Evaluate(0.7f, 0f, position, dry, dry.heightCurve);
                        Assert.LessOrEqual(wet.height, plain.height + 0.0001f, $"carving raised the ground at {position}");
                        if (plain.normalizedHeightInput < lowestRange)
                        {
                            seaFloorPoints++;
                            Assert.AreEqual(plain.height, wet.height, 0.0001f, $"the sea floor changed at {position}");
                        }
                        else if (plain.height >= waterLevel && wet.riverStrength > 0.5f)
                        {
                            carvedLandPoints++;
                        }
                    }
                }

                Assert.Greater(seaFloorPoints, 1000);
                Assert.Greater(carvedLandPoints, 0, "rivers should still carve the land in this area");
            }
            finally
            {
                Object.DestroyImmediate(dry);
            }
        }

        // With no falloff the ground stays at 0.7 (about 41 m), well above the water (about 28 m), so any water here
        // comes from the carving.
        [Test]
        public void RiversAndLakesCrossingLandFillWithWater()
        {
            heights.useFalloff = false;
            int riverPoints = 0;
            int lakePoints = 0;
            Assert.Greater(Height(0.7f), waterLevel);
            for (float x = -1000f; x <= 1000f; x += 5f)
            {
                for (float y = -1000f; y <= 1000f; y += 5f)
                {
                    Vector2 position = new Vector2(x, y);
                    TerrainHeightEvaluation result = TerrainHeightEvaluator.Evaluate(0.7f, 0f, position, heights, heights.heightCurve);
                    if (result.riverStrength >= 0.9f)
                    {
                        riverPoints++;
                        Assert.Less(result.height, waterLevel, $"a river bed is dry at {position}");
                    }

                    if (result.lakeStrength >= 0.9f)
                    {
                        lakePoints++;
                        Assert.Less(result.height, waterLevel, $"a lake bed is dry at {position}");
                    }
                }
            }

            Assert.Greater(riverPoints, 0);
            Assert.Greater(lakePoints, 0);
        }

        // Two stretches of FOREST-001's coast where a river crosses a lake patch beside a valley. Lakes used to test
        // their height range after the river had cut the ground, so along its banks one point was sunk to the lake
        // bed and the next kept; the valley's shoulder then raised the kept ones into rows of spikes about 20 m tall
        // standing out of the sea. Fixed, the tallest point above all four neighbours here is under 3 m.
        [Test]
        public void RiverBanksInsideLakesLeaveNoSpikes()
        {
            SeedManager.GetWorldSeeds("FOREST-001").ApplyTo(heights);
            heights.erosionSettings.enabled = false;
            foreach (Vector2 centre in new[] { new Vector2(-200f, 380f), new Vector2(0f, -410f) })
            {
                HeightMap map = HeightMapGenerator.GenerateHeightMap(121, 121, heights, centre);
                for (int x = 1; x < 120; x++)
                {
                    for (int y = 1; y < 120; y++)
                    {
                        float here = map.values[x, y];
                        float around = Mathf.Max(Mathf.Max(map.values[x - 1, y], map.values[x + 1, y]),
                                                 Mathf.Max(map.values[x, y - 1], map.values[x, y + 1]));
                        if (here > waterLevel)
                        {
                            Vector2 position = centre + new Vector2(x - 60.5f, 60.5f - y);
                            Assert.Less(here - around, 8f, $"a spike stands out of the ground at {position}");
                        }
                    }
                }
            }
        }

        float Height(float input) => heights.heightCurve.Evaluate(input) * heights.heightMultiplier;

        // The value before the height curve at which the ground meets the water.
        float ShorelineInput()
        {
            float low = 0f;
            float high = 1f;
            for (int step = 0; step < 30; step++)
            {
                float middle = 0.5f * (low + high);
                if (Height(middle) < waterLevel) low = middle;
                else high = middle;
            }

            return 0.5f * (low + high);
        }
    }
}
