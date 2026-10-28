using Boreal.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Boreal.TerrainGeneration.Tests
{
    // Wide, flat-floored valleys with a river along the floor and, optionally, raised land on both sides.
    public class ValleyCarvingTests
    {
        const string Folder = "Assets/TerrainGeneration/Settings/";
        const float Ground = 0.8f;

        HeightMapSettings heights;
        float waterLevel;

        [SetUp]
        public void SetUp()
        {
            heights = Object.Instantiate(AssetDatabase.LoadAssetAtPath<HeightMapSettings>(Folder + "HeightMapSettings.asset"));
            EnvironmentDefinitions environment = AssetDatabase.LoadAssetAtPath<EnvironmentDefinitions>(Folder + "EnvironmentDefinitions.asset");
            waterLevel = Mathf.Lerp(heights.minHeight, heights.maxHeight, environment.ShorelineThreshold);
            heights.valleySettings = new ValleySettings
            {
                enabled = true,
                seed = 12345,
                scale = 1200f,
                meander = 80f,
                floorHalfWidth = 30f,
                halfWidth = 110f,
                floorLevel = 0.6f,
                strength = 1f,
                riverHalfWidth = 6f
            };
        }

        [TearDown]
        public void TearDown()
        {
            HydraulicErosionCache.Invalidate(heights);
            Object.DestroyImmediate(heights);
        }

        [Test]
        public void ValleysOnlyLowerTheGround()
        {
            HeightMapSettings plain = Object.Instantiate(heights);
            plain.valleySettings.enabled = false;
            try
            {
                int lowered = 0;
                for (float x = -950f; x <= 950f; x += 10f)
                {
                    for (float y = -950f; y <= 950f; y += 10f)
                    {
                        Vector2 position = new Vector2(x, y);
                        TerrainHeightEvaluation carved = TerrainHeightEvaluator.Evaluate(0.7f, 0f, position, heights, heights.heightCurve);
                        TerrainHeightEvaluation flat = TerrainHeightEvaluator.Evaluate(0.7f, 0f, position, plain, plain.heightCurve);
                        Assert.AreEqual(0f, flat.valleyStrength);
                        Assert.LessOrEqual(carved.height, flat.height + 0.0001f, $"a valley raised the ground at {position}");
                        if (carved.height < flat.height - 0.5f) lowered++;
                    }
                }

                Assert.Greater(lowered, 0, "valleys should cut into the land somewhere");
            }
            finally
            {
                Object.DestroyImmediate(plain);
            }
        }

        // Without the island falloff, rivers or lakes, the ground stays at Ground everywhere, so every change comes from
        // the valleys.
        [Test]
        public void TheFloorIsFlatAndDryAndTheRiverAlongItHoldsWater()
        {
            heights.useFalloff = false;
            heights.riverSettings.enabled = false;
            heights.lakeSettings.enabled = false;
            ValleySettings valley = heights.valleySettings;
            Assert.Greater(Height(valley.floorLevel), waterLevel, "the valley floor must be dry land");

            int floorPoints = 0;
            int riverPoints = 0;
            for (float x = -1000f; x <= 1000f; x += 4f)
            {
                for (float y = -1000f; y <= 1000f; y += 4f)
                {
                    Vector2 position = new Vector2(x, y);
                    float distance = TerrainHeightEvaluator.ValleyDistance(valley, position);
                    TerrainHeightEvaluation result = TerrainHeightEvaluator.Evaluate(Ground, 0f, position, heights, heights.heightCurve);
                    if (distance > valley.riverHalfWidth * 1.5f && distance < valley.floorHalfWidth)
                    {
                        floorPoints++;
                        Assert.AreEqual(valley.floorLevel, result.normalizedHeightInput, 0.0001f, $"the floor isn't flat at {position}");
                        Assert.Greater(result.height, waterLevel, $"the floor is under water at {position}");
                    }
                    else if (distance < valley.riverHalfWidth * 0.15f)
                    {
                        riverPoints++;
                        Assert.GreaterOrEqual(result.riverStrength, 0.9f, $"the valley river is missing at {position}");
                        Assert.Less(result.height, waterLevel, $"the valley river is dry at {position}");
                    }
                    else if (distance > valley.halfWidth)
                    {
                        Assert.AreEqual(Ground, result.normalizedHeightInput, 0.0001f, $"ground outside a valley changed at {position}");
                    }
                }
            }

            Assert.Greater(floorPoints, 100);
            Assert.Greater(riverPoints, 10);
        }

        // The distance to a valley's centre line is measured in world units wherever the noise is, so walking straight
        // away from the line increases it by about one unit per unit walked.
        [Test]
        public void ValleyDistanceIsMeasuredInWorldUnits()
        {
            ValleySettings valley = heights.valleySettings;
            int linesChecked = 0;
            for (float y = -900f; y <= 900f; y += 150f)
            {
                // Points on a centre line are the local minima of the distance along this row.
                float previous = float.MaxValue;
                float current = TerrainHeightEvaluator.ValleyDistance(valley, new Vector2(-900f, y));
                for (float x = -898f; x <= 900f; x += 2f)
                {
                    float next = TerrainHeightEvaluator.ValleyDistance(valley, new Vector2(x, y));
                    Vector2 start = new Vector2(x - 2f, y);
                    if (current < 1.5f && current <= previous && current <= next)
                    {
                        CheckDistanceGrowsLikeWalking(valley, start, current);
                        linesChecked++;
                    }

                    previous = current;
                    current = next;
                }
            }

            Assert.Greater(linesChecked, 5, "the test should cross several valley centre lines");
        }

        static void CheckDistanceGrowsLikeWalking(ValleySettings valley, Vector2 start, float startDistance)
        {
            // The way across the line is the direction in which the distance grows fastest.
            Vector2 across = Vector2.zero;
            float farthest = -1f;
            for (int step = 0; step < 72; step++)
            {
                float angle = step * Mathf.PI / 36f;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float distance = TerrainHeightEvaluator.ValleyDistance(valley, start + direction * 20f);
                if (distance > farthest)
                {
                    farthest = distance;
                    across = direction;
                }
            }

            for (float walked = 10f; walked <= 50f; walked += 10f)
            {
                float distance = TerrainHeightEvaluator.ValleyDistance(valley, start + across * walked);
                Assert.AreEqual(walked, distance, walked * 0.25f + startDistance + 2f, $"distance after walking {walked} from {start}");
            }
        }

        [Test]
        public void ShouldersRaiseOnlyLandBesideTheValley()
        {
            heights.useFalloff = false;
            heights.riverSettings.enabled = false;
            heights.lakeSettings.enabled = false;
            ValleySettings valley = heights.valleySettings;
            valley.shoulderHeight = 0.5f;
            valley.shoulderWidth = 250f;
            float seaFloor = valley.floorLevel - 0.1f;

            int raised = 0;
            for (float x = -1000f; x <= 1000f; x += 6f)
            {
                for (float y = -1000f; y <= 1000f; y += 6f)
                {
                    Vector2 position = new Vector2(x, y);
                    float distance = TerrainHeightEvaluator.ValleyDistance(valley, position);
                    TerrainHeightEvaluation land = TerrainHeightEvaluator.Evaluate(Ground, 0f, position, heights, heights.heightCurve);
                    TerrainHeightEvaluation sea = TerrainHeightEvaluator.Evaluate(seaFloor, 0f, position, heights, heights.heightCurve);
                    Assert.AreEqual(seaFloor, sea.normalizedHeightInput, 0.0001f, $"the sea floor changed at {position}");
                    Assert.LessOrEqual(land.normalizedHeightInput, 1f);
                    if (distance > valley.riverHalfWidth * 1.5f && distance < valley.floorHalfWidth)
                    {
                        Assert.AreEqual(valley.floorLevel, land.normalizedHeightInput, 0.0001f, $"the floor moved at {position}");
                    }
                    else if (distance > valley.halfWidth + valley.shoulderWidth)
                    {
                        Assert.AreEqual(Ground, land.normalizedHeightInput, 0.0001f, $"ground beyond the shoulder changed at {position}");
                    }
                    else if (land.normalizedHeightInput > Ground + 0.01f)
                    {
                        raised++;
                    }
                }
            }

            Assert.Greater(raised, 100, "land beside the valleys should rise");
        }

        [Test]
        public void ValleysFollowTheRiversWorldSeed()
        {
            WorldSeeds seeds = SeedManager.GetWorldSeeds("FOREST-123");
            seeds.ApplyTo(heights);
            Assert.AreEqual(seeds.rivers, heights.valleySettings.seed);

            HeightMapSettings other = Object.Instantiate(heights);
            try
            {
                SeedManager.GetWorldSeeds("TUNDRA-7").ApplyTo(other);
                int differences = 0;
                for (float x = -1000f; x <= 1000f; x += 20f)
                {
                    for (float y = -1000f; y <= 1000f; y += 20f)
                    {
                        Vector2 position = new Vector2(x, y);
                        float first = TerrainHeightEvaluator.Evaluate(0.7f, 0f, position, heights, heights.heightCurve).valleyStrength;
                        float second = TerrainHeightEvaluator.Evaluate(0.7f, 0f, position, other, other.heightCurve).valleyStrength;
                        if (Mathf.Abs(first - second) > 0.5f) differences++;
                    }
                }

                Assert.Greater(differences, 100, "a different world seed should move the valleys");
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        float Height(float input) => heights.heightCurve.Evaluate(input) * heights.heightMultiplier;
    }
}
