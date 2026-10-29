using Boreal.World;
using Boreal.Temperature;
using Boreal.TerrainGeneration;
using NUnit.Framework;
using UnityEngine;

namespace Boreal.Animals.Tests
{
    public class SeasonTests
    {
        const float Tolerance = 0.0001f;

        // Flat, fairly cold, half-wet grassland: the terrain's temperature index 0.3 is about -14 °C.
        static EnvironmentSample Grassland()
        {
            return new EnvironmentSample
            {
                isValid = true,
                isLand = true,
                position = new Vector3(40f, 30f, -25f),
                temperature = 0.3f,
                moisture = 0.5f,
                slope = 0f,
                grassBiomass = 0.6f
            };
        }

        static EnvironmentSample Sea()
        {
            EnvironmentSample sea = Grassland();
            sea.isLand = false;
            sea.isWater = true;
            sea.grassBiomass = 0f;
            return sea;
        }

        [Test]
        public void TheBaseClimateChangesNothing()
        {
            SeasonalGround ground = SeasonalGround.At(Grassland(), SeasonClock.Neutral);

            Assert.AreEqual(1f, ground.growth, Tolerance);
            Assert.AreEqual(1f, ground.uncovered, Tolerance);
            Assert.AreEqual(1f, ground.movementCost, Tolerance);
            Assert.AreEqual(0.3f, ground.temperature, Tolerance);
        }

        [Test]
        public void WinterStopsGrowthBuriesMostGrassAndSlowsMovement()
        {
            SeasonalGround ground = SeasonalGround.At(Grassland(), SeasonState.For(BorealSeason.Winter));

            // The winter preset grows at 5%, a little less again on its drier ground.
            Assert.That(ground.growth, Is.InRange(0.03f, 0.05f));
            // Snow covers 72% to 100% of flat ground this cold, and the rest has withered by a quarter.
            Assert.That(ground.uncovered, Is.InRange(0.07f, 0.27f));
            Assert.That(ground.movementCost, Is.InRange(1.54f, 1.75f));
            // 0.6 colder on the temperature index, stopping at the coldest.
            Assert.AreEqual(0f, ground.temperature, Tolerance);
        }

        [Test]
        public void SummerLeavesGrassUncoveredAndWarmsTheGround()
        {
            SeasonalGround ground = SeasonalGround.At(Grassland(), SeasonState.For(BorealSeason.Summer));

            Assert.AreEqual(1f, ground.uncovered, Tolerance);
            Assert.AreEqual(1f, ground.movementCost, Tolerance);
            Assert.AreEqual(0.38f, ground.temperature, Tolerance);
            // Full growth, on ground a little drier than usual.
            Assert.That(ground.growth, Is.InRange(0.95f, 1f));
        }

        [Test]
        public void TheSeaHasNoSnowButFollowsTheSeasonsGrowth()
        {
            SeasonalGround sea = SeasonalGround.At(Sea(), SeasonState.For(BorealSeason.Winter));

            Assert.AreEqual(0.05f, sea.growth, Tolerance);
            Assert.AreEqual(1f, sea.uncovered, Tolerance);
            Assert.AreEqual(1f, sea.movementCost, Tolerance);
        }

        [Test]
        public void StrengthSoftensASeasonTowardsTheBaseClimate()
        {
            SeasonState winter = SeasonState.For(BorealSeason.Winter);

            SeasonState none = SeasonClock.Scale(winter, 0f);
            Assert.AreEqual(0f, none.temperatureOffset, Tolerance);
            Assert.AreEqual(1f, none.growthMultiplier, Tolerance);
            Assert.AreEqual(0f, none.snowAmount, Tolerance);
            Assert.AreEqual(0f, none.dormancy, Tolerance);

            SeasonState half = SeasonClock.Scale(winter, 0.5f);
            Assert.AreEqual(-0.3f, half.temperatureOffset, Tolerance);
            Assert.AreEqual(0.525f, half.growthMultiplier, Tolerance);
            Assert.AreEqual(0.5f, half.snowAmount, Tolerance);

            SeasonState full = SeasonClock.Scale(winter, 1f);
            Assert.AreEqual(winter.temperatureOffset, full.temperatureOffset, Tolerance);
            Assert.AreEqual(winter.growthMultiplier, full.growthMultiplier, Tolerance);
            Assert.AreEqual(BorealSeason.Winter, full.season);
        }

        [Test]
        public void BalancedGrowthAveragesTheBaseClimateOverAYear()
        {
            foreach (float strength in new[] { 0.25f, 0.5f, 1f })
            {
                float balance = SeasonClock.YearlyGrowthBalance(strength);
                float total = 0f;
                for (int day = 0; day < 365; day++)
                {
                    total += SeasonClock.Scale(SeasonState.AtYearProgress(day / 365f), strength).growthMultiplier * balance;
                }

                Assert.AreEqual(1f, total / 365f, 0.01f, $"At strength {strength}.");
            }

            // The presets average 0.525 over a year: spring 0.7, summer 1, fall 0.35 and winter 0.05.
            Assert.AreEqual(1f / 0.525f, SeasonClock.YearlyGrowthBalance(1f), 0.01f);
            Assert.AreEqual(1f, SeasonClock.YearlyGrowthBalance(0f), Tolerance);
        }

        [Test]
        public void SeasonsEaseInOverTheFirstYears()
        {
            Assert.AreEqual(0f, SeasonClock.EasedStrength(1f, 0f, 3f), Tolerance);
            Assert.AreEqual(0.5f, SeasonClock.EasedStrength(1f, 1.5f, 3f), Tolerance);
            Assert.AreEqual(1f, SeasonClock.EasedStrength(1f, 3f, 3f), Tolerance);
            Assert.AreEqual(0.8f, SeasonClock.EasedStrength(0.8f, 10f, 3f), Tolerance);
            Assert.AreEqual(0.8f, SeasonClock.EasedStrength(0.8f, 0f, 0f), Tolerance);
        }

        [Test]
        public void TheClocksConditionsBalanceGrowthAndLimitBurial()
        {
            SeasonConditions winter = new SeasonConditions
            {
                state = SeasonState.For(BorealSeason.Winter), growthBalance = 1.9f, leastUncovered = 0.5f
            };
            SeasonalGround raw = SeasonalGround.At(Grassland(), winter.state);
            SeasonalGround adjusted = SeasonalGround.At(Grassland(), winter);

            Assert.AreEqual(raw.growth * 1.9f, adjusted.growth, Tolerance);
            Assert.Less(raw.uncovered, 0.5f);
            Assert.AreEqual(0.5f, adjusted.uncovered, Tolerance);
            Assert.AreEqual(raw.movementCost, adjusted.movementCost, Tolerance);
        }

        [Test]
        public void SeasonsAreNamedThroughTheYear()
        {
            Assert.AreEqual("Spring", SeasonClock.Describe(0f));
            Assert.AreEqual("Early summer", SeasonClock.Describe(0.125f));
            Assert.AreEqual("Summer", SeasonClock.Describe(0.25f));
            Assert.AreEqual("Late fall", SeasonClock.Describe(0.6f));
            Assert.AreEqual("Early winter", SeasonClock.Describe(0.7f));
            Assert.AreEqual("Winter", SeasonClock.Describe(0.75f));
            Assert.AreEqual("Early spring", SeasonClock.Describe(0.95f));
            Assert.AreEqual("Summer", SeasonClock.Describe(1.25f));
        }

        [Test]
        public void ATallyAddsUpEverySpeciesAndCountsOtherDeaths()
        {
            SpeciesTelemetryRecord first = new SpeciesTelemetryRecord
            {
                reproductionEvents = 4, sexualOffspring = 1, deaths = 5, starvationDeaths = 2, coldExposureDeaths = 1,
                plantMeals = 7
            };
            SpeciesTelemetryRecord second = new SpeciesTelemetryRecord
            {
                reproductionEvents = 3, deaths = 2, predationDeaths = 1, meatMeals = 2
            };

            EcosystemTally tally = EcosystemTally.Of(new[] { first, second });

            Assert.AreEqual(7, tally.births);
            Assert.AreEqual(1, tally.sexualBirths);
            Assert.AreEqual(7, tally.deaths);
            Assert.AreEqual(3, tally.OtherDeaths);
            Assert.AreEqual(7, tally.plantMeals);
            Assert.AreEqual(2, tally.meatMeals);
            Assert.AreEqual(2, tally.speciesEver);
        }

        [Test]
        public void ASeasonSummaryReportsWhatHappenedDuringTheSeason()
        {
            EcosystemTally start = new EcosystemTally
            {
                births = 5, deaths = 2, starvationDeaths = 2, plantMeals = 10, speciesEver = 1
            };
            EcosystemTally end = new EcosystemTally
            {
                births = 12, sexualBirths = 2, deaths = 6, starvationDeaths = 3, oldAgeDeaths = 1, coldDeaths = 1,
                plantMeals = 40, meatMeals = 1, speciesEver = 2
            };
            SeasonSummary summary = new SeasonSummary(2, BorealSeason.Winter, 300f, 3, start);
            summary.Sample(10f, 3, 100, 20, 0, 1f, 0f, 4f);
            summary.Sample(30f, 9, 200, 60, 40, 0.05f, 1f, -28f);

            WinterTraits traits = new WinterTraits
            {
                animals = 8, breedingThreshold = 0.85f, torpor = 0.12f, maxEnergy = 162f, coldestComfort = -19f,
                perkShares = new[] { 0.25f, 0f, 0.1f, 0f, 0f }
            };
            string line = summary.Describe(400f, 8, 0, 0.58f, 2, end, traits, "seasons on", "");

            StringAssert.StartsWith("Season summary: year 2 winter, 300-400 s (seasons on): ", line);
            StringAssert.Contains("animals 3 -> 8, lowest 3, highest 9, off-screen 0", line);
            StringAssert.Contains("births 7 (5 clones, 2 from two parents)", line);
            StringAssert.Contains("deaths 4: starvation 1, predation 0, old age 1, cold 1, heat 0, venom 0, other 1", line);
            StringAssert.Contains("meals: 30 plant, 1 meat", line);
            StringAssert.Contains("energy at end 58% on average", line);
            StringAssert.Contains("plants on average: 175 shown (50 in the sea), 30 under snow", line);
            StringAssert.Contains("species: 2 alive, 1 new", line);
            StringAssert.Contains("plants regrow at 29%, snow 75%, 20 °C colder", line);
            StringAssert.Contains("traits on average: breeds at 85% energy, torpor 0.12, energy store 162, " +
                                  "comfortable down to -19 °C", line);
            StringAssert.Contains("perks: thick fur 25%, venom 0%, camouflage 10%, regeneration 0%, scavenger gut 0%",
                                  line);
        }

        [Test]
        public void ASeasonCutShortSaysWhy()
        {
            SeasonSummary summary = new SeasonSummary(1, BorealSeason.Fall, 180f, 2, default);
            summary.Sample(5f, 0, -1, -1, -1, 1f, 0f, float.NaN);

            string line = summary.Describe(185f, 0, 0, float.NaN, 0, default, default, "seasons off",
                                           "ended early: no animals left");

            StringAssert.StartsWith("Season summary: year 1 fall, 180-185 s (seasons off, ended early: no animals left): ",
                                    line);
            StringAssert.Contains("energy at end: no live animals", line);
            StringAssert.Contains("plants: unknown", line);
            StringAssert.DoesNotContain("°C", line);
            StringAssert.DoesNotContain("traits", line);
        }
    }
}
