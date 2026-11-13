using System;
using System.Collections.Generic;
using Boreal.TerrainGeneration;
using NUnit.Framework;
using UnityEngine;

namespace Boreal.Animals.Tests
{
    // The wildfire's spread rules and fire weather (items 78 and 79), on stand-in landscapes without a scene.
    public class WildfireTests
    {
        static FireFuel Forest(float height = 0f, float moisture = 0.2f) =>
            new FireFuel { burnable = true, load = 1f, crown = 0.85f, moisture = moisture, height = height };

        static IFireLandscape Landscape(float extent, Func<Vector2, FireFuel?> fuel) =>
            new FireLandscapeFunction(extent, (position, dryness) => fuel(position));

        static FireWeather Weather(float windSpeed = 0f, float dryness = 0.8f) => new FireWeather
        {
            dryness = dryness, windDirection = Vector2.right, windSpeed = windSpeed, daylight = 1f
        };

        // Lights one cell at start and lets the fire run for up to seconds, or until it has burned out.
        static WildfireGrid Burn(IFireLandscape landscape, Vector2 start, FireWeather weather, int seed, float seconds)
        {
            WildfireGrid grid = new WildfireGrid(new WildfireRules(), landscape.Extent);
            int fire = grid.NewFire();
            Assert.IsTrue(grid.IgniteNear(start, 0f, fire, 0f, weather.dryness, landscape), "The first cell did not catch.");
            System.Random random = new System.Random(seed);
            float step = grid.rules.stepSeconds;
            for (float time = step; time <= seconds && grid.BurningCount > 0; time += step)
            {
                grid.Step(step, time, weather, landscape, random);
            }
            return grid;
        }

        static List<Vector2> TouchedCells(WildfireGrid grid)
        {
            List<Vector2> touched = new List<Vector2>();
            for (int index = 0; index < grid.size * grid.size; index++)
            {
                if (grid.StateOf(index) != WildfireGrid.CellState.Unburned) touched.Add(grid.CellCentre(index));
            }
            return touched;
        }

        [Test]
        public void FireSpreadsThroughFuelAndBurnsOut()
        {
            IFireLandscape forest = Landscape(120f, position => Forest());
            WildfireGrid grid = Burn(forest, Vector2.zero, Weather(), 1, 3000f);

            Assert.AreEqual(0, grid.BurningCount, "The fire should have burned out.");
            Assert.Greater(grid.EverBurnedCount, 300, "The fire should have spread well beyond its first cell.");
            Assert.AreEqual(WildfireGrid.CellState.Burned, grid.StateAt(Vector2.zero));
            WildfireGrid.FireTally tally = grid.TallyOf(1);
            Assert.AreEqual(grid.EverBurnedCount, tally.burned);
            Assert.Greater(tally.peakBurning, 10);
        }

        [Test]
        public void WaterAndBareGroundStopTheFire()
        {
            // A river 32 m wide from x 16 to 48, then forest again; bare rock to the south of z -56. Cell centres sit at
            // -116 + 8k, so no centre lies on a boundary.
            IFireLandscape land = Landscape(120f, position =>
            {
                if (position.x > 16f && position.x < 48f) return new FireFuel { burnable = false, height = -1f };
                if (position.y < -56f) return new FireFuel { burnable = true, load = 0.01f, crown = 0f, moisture = 0.1f };
                return Forest();
            });
            WildfireGrid grid = Burn(land, new Vector2(-60f, 0f), Weather(), 2, 3000f);

            List<Vector2> touched = TouchedCells(grid);
            Assert.Greater(touched.Count, 100, "The fire should have burned the forest west of the river.");
            foreach (Vector2 cell in touched)
            {
                Assert.Less(cell.x, 16f, $"The fire crossed the river to {cell}.");
                Assert.Greater(cell.y, -56f, $"The fire burned bare rock at {cell}.");
            }
        }

        [Test]
        public void SnowAndWetGroundDoNotCarryFire()
        {
            FireGround ground = new FireGround { isLand = true, treeCover = 0.9f, grassBiomass = 0.5f, moisture = 0.4f };
            WildfireRules rules = new WildfireRules();
            float dry = rules.Flammability(WildfireRules.Fuel(ground, 0.8f));
            Assert.Greater(dry, 0.3f, "Dry forest should catch.");

            FireGround snowy = ground;
            snowy.snowCover = 0.6f;
            Assert.AreEqual(0f, rules.Flammability(WildfireRules.Fuel(snowy, 0.8f)), "Snow-covered ground should not burn.");
            FireGround riverbank = ground;
            riverbank.wetness = 0.8f;
            Assert.AreEqual(0f, rules.Flammability(WildfireRules.Fuel(riverbank, 0.8f)), "Wet ground should not burn.");
            Assert.Less(rules.Flammability(WildfireRules.Fuel(ground, 0.3f)), dry, "Damp weather should damp the fire.");

            // East of x -4 the snow lies (cell centres sit at -96 + 8k).
            IFireLandscape land = Landscape(100f, position => WildfireRules.Fuel(position.x > -4f ? snowy : ground, 0.8f));
            WildfireGrid grid = Burn(land, new Vector2(-50f, 0f), Weather(), 3, 3000f);
            Assert.Greater(grid.EverBurnedCount, 50);
            foreach (Vector2 cell in TouchedCells(grid)) Assert.Less(cell.x, -4f, $"Snowy ground burned at {cell}.");
        }

        [Test]
        public void FuelFollowsTreesGrassAndRock()
        {
            FireGround forest = new FireGround { isLand = true, treeCover = 1f, grassBiomass = 0.4f, moisture = 0.3f };
            FireGround meadow = new FireGround { isLand = true, treeCover = 0f, grassBiomass = 1f, moisture = 0.3f };
            FireGround scree = forest;
            scree.rockDensity = 0.9f;
            FireGround lake = forest;
            lake.isLand = false;

            FireFuel forestFuel = WildfireRules.Fuel(forest, 0.7f);
            FireFuel meadowFuel = WildfireRules.Fuel(meadow, 0.7f);
            Assert.Greater(forestFuel.load, meadowFuel.load);
            Assert.Greater(forestFuel.crown, 0.8f, "Most of a forest's fuel is up in the trees.");
            Assert.AreEqual(0f, meadowFuel.crown);
            Assert.Less(WildfireRules.Fuel(scree, 0.7f).load, forestFuel.load * 0.5f);
            Assert.IsFalse(WildfireRules.Fuel(lake, 0.7f).burnable);

            // A meadow burns quickly; a forest burns long.
            WildfireRules rules = new WildfireRules();
            Assert.Less(rules.BurnSeconds(meadowFuel), rules.BurnSeconds(forestFuel) * 0.5f);
            // Cured grass in a dormant season is drier.
            FireGround cured = meadow;
            cured.dormancy = 1f;
            Assert.Less(WildfireRules.Fuel(cured, 0.7f).moisture, meadowFuel.moisture);
        }

        [Test]
        public void FireRunsFurtherDownwindThanUpwind()
        {
            // A damp forest, so in 40 s neither front reaches the edge.
            IFireLandscape forest = Landscape(300f, position => Forest(moisture: 0.35f));
            float downwind = 0f, upwind = 0f;
            for (int seed = 10; seed < 15; seed++)
            {
                WildfireGrid grid = Burn(forest, Vector2.zero, Weather(windSpeed: 6f), seed, 40f);
                foreach (Vector2 cell in TouchedCells(grid))
                {
                    downwind = Mathf.Max(downwind, cell.x);
                    upwind = Mathf.Max(upwind, -cell.x);
                }
            }
            Assert.Greater(downwind, 2f * upwind, $"Downwind reach {downwind:0} m, upwind {upwind:0} m.");
        }

        [Test]
        public void FireRunsFasterUphill()
        {
            // The ground rises 0.3 m per metre towards +x.
            IFireLandscape slope = Landscape(300f, position => Forest(position.x * 0.3f, moisture: 0.35f));
            float uphill = 0f, downhill = 0f;
            for (int seed = 20; seed < 25; seed++)
            {
                WildfireGrid grid = Burn(slope, Vector2.zero, Weather(), seed, 40f);
                foreach (Vector2 cell in TouchedCells(grid))
                {
                    uphill = Mathf.Max(uphill, cell.x);
                    downhill = Mathf.Max(downhill, -cell.x);
                }
            }
            Assert.Greater(uphill, 1.5f * downhill, $"Uphill reach {uphill:0} m, downhill {downhill:0} m.");
        }

        [Test]
        public void TheSameSeedBurnsTheSameCells()
        {
            IFireLandscape forest = Landscape(120f, position => Forest());
            WildfireGrid first = Burn(forest, Vector2.zero, Weather(windSpeed: 4f), 7, 90f);
            WildfireGrid second = Burn(forest, Vector2.zero, Weather(windSpeed: 4f), 7, 90f);
            Assert.AreEqual(first.EverBurnedCount, second.EverBurnedCount);
            for (int index = 0; index < first.size * first.size; index++)
            {
                Assert.AreEqual(first.StateOf(index), second.StateOf(index), $"Cell {index} differs.");
            }
        }

        [Test]
        public void HeatIsFeltInAndBesideTheFlamesAndSmokeDriftsDownwind()
        {
            IFireLandscape forest = Landscape(200f, position => Forest());
            WildfireGrid grid = Burn(forest, Vector2.zero, Weather(windSpeed: 5f), 30, 20f);
            Assert.Greater(grid.BurningCount, 0, "The fire should still be burning after 20 s.");

            // The fiercest burning cell.
            int cell = -1;
            Vector2 flames = Vector2.zero;
            float fiercest = 0f;
            for (int burning = 0; burning < grid.BurningCount; burning++)
            {
                int index = grid.BurningCellAt(burning, out Vector2 centre, out float intensity, out _);
                if (intensity <= fiercest) continue;
                fiercest = intensity;
                flames = centre;
                cell = index;
            }
            Assert.AreEqual(WildfireGrid.CellState.Burning, grid.StateOf(cell));
            Assert.Greater(grid.HeatAt(flames), 0.2f, "Standing in flames should be hot.");
            Assert.AreEqual(0f, grid.HeatAt(flames + new Vector2(0f, 150f)), "150 m away there is no heat.");

            Vector2 ahead = new Vector2(100f, 0f), behind = new Vector2(-100f, 0f);
            Assert.Greater(grid.SmokeAt(ahead), grid.SmokeAt(behind) + 0.001f, "Smoke should drift downwind.");

            Assert.IsTrue(grid.Sense(new Vector2(-90f, 0f), 150f, out float distance, out Vector2 away, out float heat));
            Assert.Less(distance, 120f);
            Assert.Greater(heat, 0f);
            Assert.Less(away.x, -0.8f, "Away from a fire to the east is west.");
            Assert.IsFalse(grid.Sense(new Vector2(-190f, 190f), 30f, out _, out _, out _));
        }

        [Test]
        public void PuttingAFireOutLeavesNothingBurning()
        {
            IFireLandscape forest = Landscape(120f, position => Forest());
            WildfireGrid grid = Burn(forest, Vector2.zero, Weather(), 40, 15f);
            Assert.Greater(grid.BurningCount, 0);
            grid.Extinguish(1, 15f);
            Assert.AreEqual(0, grid.BurningCount);
            Assert.AreEqual(grid.EverBurnedCount, grid.TallyOf(1).burned);
        }

        // Dryness lags the warm, dry summer, so it peaks in late summer and stays up into early fall, while snowmelt
        // keeps spring wet and winter has none.
        [Test]
        public void DrynessPeaksInLateSummerAndStaysUpIntoFall()
        {
            NaturalWeather weather = new NaturalWeather { spellStrength = 0f };
            const float Year = 400f;
            weather.Reset(1, 0f);
            float[] sums = new float[8];
            int[] counts = new int[8];
            float peak = -1f, peakAt = 0f;
            for (float time = 0f; time < 3f * Year; time += 1f)
            {
                float progress = time / Year;
                SeasonState season = SeasonClock.Scale(SeasonState.AtYearProgress(progress), 0.7f);
                weather.Step(1f, true, season, Year);
                if (time < 2f * Year) continue;
                // Eighths of the year from the height of spring: 0 spring, 1 early summer, 2 summer... 7 late winter.
                float yearProgress = Mathf.Repeat(progress, 1f);
                int eighth = Mathf.FloorToInt(Mathf.Repeat(yearProgress + 1f / 16f, 1f) * 8f) % 8;
                sums[eighth] += weather.Dryness;
                counts[eighth]++;
                if (weather.Dryness > peak)
                {
                    peak = weather.Dryness;
                    peakAt = yearProgress;
                }
            }
            float Mean(int eighth) => sums[eighth] / Mathf.Max(1, counts[eighth]);
            float spring = Mean(0), earlySummer = Mean(1), summer = Mean(2), lateSummer = Mean(3), fall = Mean(4);
            float winter = Mean(6);
            Assert.That(peakAt, Is.InRange(0.28f, 0.42f), $"Dryness peaked at year progress {peakAt:0.00}.");
            Assert.Greater(lateSummer, earlySummer, "Late summer should be drier than early summer.");
            Assert.Greater(lateSummer, summer, "Late summer should be drier than the height of summer.");
            Assert.Greater(fall, spring + 0.15f, $"Fall ({fall:0.00}) should be drier than spring ({spring:0.00}).");
            Assert.Greater(fall, winter + 0.15f, $"Fall ({fall:0.00}) should be drier than winter ({winter:0.00}).");
            Assert.Less(spring, 0.15f);
            Assert.Less(winter, 0.1f);
            Assert.Greater(lateSummer, 0.4f);
        }

        [Test]
        public void FireDangerIsHighestWhenDryAndWarm()
        {
            WildfireSettings settings = new WildfireSettings();
            NaturalConditions dryWarm = new NaturalConditions { dryness = 0.8f, celsius = 18f };
            NaturalConditions dryCold = new NaturalConditions { dryness = 0.8f, celsius = -5f };
            NaturalConditions wetWarm = new NaturalConditions { dryness = 0.2f, celsius = 18f };
            Assert.Greater(settings.Danger(dryWarm), settings.Danger(dryCold));
            Assert.Greater(settings.Danger(dryWarm), 3f * settings.Danger(wetWarm));
            Assert.AreEqual(0f, settings.Danger(new NaturalConditions { dryness = 0f, celsius = 18f }));
        }

        [Test]
        public void GodToolsScheduleWildfires()
        {
            Assert.IsTrue(GodTools.TryParse("fire@900", out GodTools.Intervention fire));
            Assert.AreEqual(GodTools.Kind.Fire, fire.kind);
            Assert.AreEqual(900f, fire.time);
            Assert.AreEqual(1f, fire.amount);
            Assert.AreEqual(0f, fire.duration);

            Assert.IsTrue(GodTools.TryParse("wildfire@900:120x2", out GodTools.Intervention timed));
            Assert.AreEqual(GodTools.Kind.Fire, timed.kind);
            Assert.AreEqual(120f, timed.duration);
            Assert.AreEqual(2f, timed.amount);

            Assert.IsTrue(GodTools.TryParse("fire@10x0", out GodTools.Intervention atLeastOne));
            Assert.AreEqual(1f, atLeastOne.amount);
            Assert.IsFalse(GodTools.TryParse("fire@-5", out _));
            Assert.IsFalse(GodTools.TryParse("4@100", out _), "A number is not a kind of god tool.");
            Assert.IsFalse(GodTools.TryParse("blaze@100", out _));
        }

        [Test]
        public void CompassPointsNameWhereTheWindBlows()
        {
            Assert.AreEqual("N", NaturalConditions.CompassPoint(new Vector2(0f, 1f)));
            Assert.AreEqual("E", NaturalConditions.CompassPoint(new Vector2(1f, 0f)));
            Assert.AreEqual("SW", NaturalConditions.CompassPoint(new Vector2(-1f, -1f)));
        }
    }
}
