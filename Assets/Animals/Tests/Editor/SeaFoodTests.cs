using NUnit.Framework;
using UnityEngine;

namespace Boreal.Animals.Tests
{
    public class SeaFoodTests
    {
        static readonly SeaFoodRules Rules = SeaFoodRules.Default;

        // Plankton (item 76) rise to the surface at night and sink by day, less far under a bright moon.
        [Test]
        public void PlanktonAreRichAtNightAndPoorByDay()
        {
            Assert.AreEqual(0.5f, SeaFoodRules.PlanktonAtSurface(1f, 0f), 1e-5f, "by day");
            Assert.AreEqual(0.5f, SeaFoodRules.PlanktonAtSurface(1f, 1f), 1e-5f, "by day the moon doesn't matter");
            Assert.AreEqual(1.5f, SeaFoodRules.PlanktonAtSurface(0f, 0f), 1e-5f, "on a moonless night");
            Assert.AreEqual(1.1f, SeaFoodRules.PlanktonAtSurface(0f, 1f), 1e-5f, "under a full moon high up");
            Assert.AreEqual(1.0f, SeaFoodRules.PlanktonAtSurface(0.5f, 0f), 1e-5f, "halfway through twilight");
        }

        // A plankton food item is worth its nutrition times the time of day's plankton; other sea food and land plants
        // are worth their nutrition whatever the time, and so is plankton with day and night off.
        [Test]
        public void OnlyPlanktonFoodFollowsTheTimeOfDay()
        {
            GameObject plankton = new GameObject("Plankton");
            GameObject kelp = new GameObject("Kelp");
            try
            {
                FoodItem planktonFood = plankton.AddComponent<FoodItem>();
                planktonFood.nutritionValue = 60f;
                planktonFood.seaBiome = SeaBiome.OpenSea;
                FoodItem kelpFood = kelp.AddComponent<FoodItem>();
                kelpFood.nutritionValue = 60f;
                kelpFood.seaBiome = SeaBiome.KelpForest;

                Boreal.World.WorldDay.Reset();
                Assert.AreEqual(60f, planktonFood.CurrentNutrition, 1e-4f, "Without day and night nothing changes.");

                Boreal.World.WorldDay.IsRunning = true;
                // Midnight on the world's first day: the moon is new, so the night is dark.
                Boreal.World.WorldDay.Current = Boreal.TerrainGeneration.DayCycle.Evaluate(0f, 62f, 0f, 4f, 0f);
                float night = Boreal.World.WorldDay.Current.daylight;
                Assert.AreEqual(0f, night, 1e-4f);
                Assert.AreEqual(60f * SeaFoodRules.PlanktonAtSurface(night, Boreal.World.WorldDay.Current.moonlight),
                                planktonFood.CurrentNutrition, 1e-3f);
                Assert.Greater(planktonFood.CurrentNutrition, 60f);
                Assert.AreEqual(60f, kelpFood.CurrentNutrition, 1e-4f);

                Boreal.World.WorldDay.Current = Boreal.TerrainGeneration.DayCycle.Evaluate(0.5f, 62f, 0f, 4f, 0f);
                Assert.AreEqual(30f, planktonFood.CurrentNutrition, 1e-3f, "At noon plankton is worth half.");
            }
            finally
            {
                Boreal.World.WorldDay.Reset();
                Object.DestroyImmediate(plankton);
                Object.DestroyImmediate(kelp);
            }
        }

        // Production per plant cell, relative to a land plant: how often a cell holds food, times its nutrition,
        // divided by how long it takes to regrow.
        static float Production(SeaFoodYield yield) => yield.chance * yield.nutrition / yield.regrowth;

        [Test]
        public void DeadZonesAndLandGrowNoSeaFood()
        {
            Assert.AreEqual(0f, Rules.Yield(SeaBiome.DeadZone).chance);
            Assert.AreEqual(0f, Rules.Yield(SeaBiome.None).chance);
        }

        [Test]
        public void EveryLivingBiomeGrowsSomething()
        {
            foreach (SeaBiome biome in new[]
                     {
                         SeaBiome.SeagrassMeadow, SeaBiome.KelpForest, SeaBiome.ColdWaterReef, SeaBiome.OpenSea
                     })
            {
                SeaFoodYield yield = Rules.Yield(biome);
                Assert.Greater(yield.chance, 0f, SeaBiomeRules.Name(biome));
                Assert.Greater(yield.nutrition, 0f, SeaBiomeRules.Name(biome));
            }
        }

        [Test]
        public void KelpIsTheMostProductiveAndOpenSeaTheLeast()
        {
            float kelp = Production(Rules.kelp);
            Assert.Greater(kelp, Production(Rules.seagrass));
            Assert.Greater(kelp, Production(Rules.reef));
            Assert.Less(Production(Rules.plankton), Production(Rules.seagrass));
            Assert.Less(Rules.plankton.chance, 0.1f, "open sea food should be sparse");
        }

        [Test]
        public void ReefPlantsAreTheRichestButSlowestToRegrow()
        {
            Assert.Greater(Rules.reef.nutrition, Rules.kelp.nutrition);
            Assert.Greater(Rules.reef.nutrition, Rules.seagrass.nutrition);
            Assert.Greater(Rules.reef.regrowth, Rules.kelp.regrowth);
        }

        [Test]
        public void GrowthFollowsTheTemperatureRange()
        {
            Assert.AreEqual(0f, Rules.GrowthRate(Rules.minimumGrowthTemperature), "too cold");
            Assert.AreEqual(0f, Rules.GrowthRate(Rules.maximumGrowthTemperature), "too warm");
            Assert.AreEqual(1f, Rules.GrowthRate(Rules.optimalGrowthTemperatureMin));
            Assert.AreEqual(1f, Rules.GrowthRate(Rules.optimalGrowthTemperatureMax));
            float halfwayUp = (Rules.minimumGrowthTemperature + Rules.optimalGrowthTemperatureMin) * 0.5f;
            Assert.AreEqual(0.5f, Rules.GrowthRate(halfwayUp), 1e-5f);
            float halfwayDown = (Rules.optimalGrowthTemperatureMax + Rules.maximumGrowthTemperature) * 0.5f;
            Assert.AreEqual(0.5f, Rules.GrowthRate(halfwayDown), 1e-5f);
            Assert.AreEqual(0f, Rules.GrowthRate(float.NaN));
        }

        [Test]
        public void TheWholeMeasuredSeaGrowsFood()
        {
            // Seed FOREST-001's sea surface runs from about -6.5 to +4.3 C. The Food Spawner drops sites growing
            // slower than 5% of the best rate, so all of that range must stay above it.
            for (float celsius = -6.5f; celsius <= 4.3f; celsius += 0.1f)
            {
                Assert.Greater(Rules.GrowthRate(celsius), 0.05f, $"{celsius:0.0} C");
            }

            Assert.AreEqual(1f, Rules.GrowthRate(4.3f), "the warmest water grows at the best rate");
        }

        [Test]
        public void EveryBiomeHasAPlantName()
        {
            foreach (SeaBiome biome in System.Enum.GetValues(typeof(SeaBiome)))
            {
                Assert.IsFalse(string.IsNullOrEmpty(SeaFoodRules.PlantName(biome)));
            }
        }
    }
}
