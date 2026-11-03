using NUnit.Framework;
using UnityEngine;

namespace Boreal.TerrainGeneration.Tests
{
    // The sun and moon over the island (item 71): day length by latitude and season, the sun's path through the day,
    // the moon's phases, and the light animals see by.
    public class DayCycleTests
    {
        const float Latitude = 62f;
        const float Month = 4f;

        [Test]
        public void DaysAreLongInSummerAndShortInWinterAtTrondheimsLatitude()
        {
            float summer = 24f * DayCycle.DayLength(Latitude, DayCycle.SunDeclination(0.25f));
            float winter = 24f * DayCycle.DayLength(Latitude, DayCycle.SunDeclination(0.75f));
            float equinox = 24f * DayCycle.DayLength(Latitude, DayCycle.SunDeclination(0f));

            Assert.AreEqual(19.3f, summer, 0.3f);
            Assert.AreEqual(4.7f, winter, 0.3f);
            Assert.AreEqual(12f, equinox, 0.01f);
        }

        [Test]
        public void AboveTheArcticCircleThereIsMidnightSunAndPolarNight()
        {
            Assert.AreEqual(1f, DayCycle.DayLength(70f, DayCycle.SunDeclination(0.25f)), 1e-4f);
            Assert.AreEqual(0f, DayCycle.DayLength(70f, DayCycle.SunDeclination(0.75f)), 1e-4f);
            DayState midnight = DayCycle.Evaluate(0f, 70f, 0.25f, Month, 0f);
            Assert.Greater(midnight.sunElevation, 0f, "the midnight sun stays above the horizon");
        }

        // At the equinox the sun rises due east, stands due south at noon, 90° - 62° = 28° up, and sets due west.
        [Test]
        public void TheSunRisesInTheEastAndStandsSouthAtNoon()
        {
            DayState morning = DayCycle.Evaluate(0.3f, Latitude, 0f, Month, 0f);
            DayState noon = DayCycle.Evaluate(0.5f, Latitude, 0f, Month, 0f);
            DayState evening = DayCycle.Evaluate(0.7f, Latitude, 0f, Month, 0f);
            DayState midnight = DayCycle.Evaluate(1f, Latitude, 0f, Month, 0f);

            Assert.AreEqual(28f, noon.sunElevation, 0.1f);
            Assert.Greater(noon.sunElevation, morning.sunElevation);
            Assert.Greater(noon.sunElevation, evening.sunElevation);
            Assert.Greater(morning.sunDirection.x, 0f, "east in the morning");
            Assert.Less(evening.sunDirection.x, 0f, "west in the evening");
            Assert.Less(noon.sunDirection.z, 0f, "south at noon");
            Assert.Less(midnight.sunElevation, -6f);
            Assert.AreEqual(1f, noon.light, 1e-4f);
            Assert.AreEqual(0f, midnight.daylight, 1e-4f);
            Assert.AreEqual(2, midnight.day, "midnight starts the next day");
        }

        // Light for seeing: about a third at sunrise, a full moon's 0.08 at midnight, starlight's 0.01 at new moon.
        [Test]
        public void NightsAreAsLightAsTheMoon()
        {
            Assert.AreEqual(0.32f, DayCycle.Daylight(0f), 0.01f);

            DayState fullMoon = DayCycle.Evaluate(0f, Latitude, 0f, Month, 0.5f);
            Assert.AreEqual(0.5f, fullMoon.moonPhase, 1e-4f);
            Assert.AreEqual(1f, fullMoon.moonBrightness, 1e-4f);
            Assert.AreEqual(28f, fullMoon.moonElevation, 0.5f, "a full moon stands south at midnight");
            Assert.AreEqual(DayCycle.FullMoonLight, fullMoon.light, 0.002f);
            Assert.IsTrue(fullMoon.IsNight);

            DayState newMoon = DayCycle.Evaluate(2f, Latitude, 0f, Month, 0.5f);
            Assert.AreEqual(0f, newMoon.moonPhase, 1e-4f);
            Assert.AreEqual(DayCycle.StarLight, newMoon.light, 0.001f);
        }

        // With a four-day month each night has its own phase.
        [Test]
        public void TheMoonGoesThroughItsPhasesNightByNight()
        {
            float[] phases = new float[4];
            for (int night = 0; night < 4; night++)
            {
                phases[night] = DayCycle.Evaluate(night, Latitude, 0f, Month, 0f).moonPhase;
            }

            Assert.AreEqual(new[] { 0f, 0.25f, 0.5f, 0.75f }, phases);
        }

        // The swing (item 73) averages to nothing, so the seasons keep their balance; it is coldest at sunrise and
        // warmest in the afternoon, on long days and short ones.
        [Test]
        public void TheDaysSwingAveragesToNothingColdestAtDawnWarmestInTheAfternoon()
        {
            foreach (float dayLength in new[] { 0.2f, 0.5f, 0.8f })
            {
                float total = 0f;
                float coldest = float.MaxValue, warmest = float.MinValue, coldestAt = 0f, warmestAt = 0f;
                const int Steps = 2000;
                for (int step = 0; step < Steps; step++)
                {
                    float time = (step + 0.5f) / Steps;
                    float shape = DailyTemperature.Shape(time, dayLength);
                    total += shape;
                    if (shape < coldest) { coldest = shape; coldestAt = time; }
                    if (shape > warmest) { warmest = shape; warmestAt = time; }
                }

                Assert.AreEqual(0f, total / Steps, 0.002f, $"day length {dayLength}");
                Assert.AreEqual(0.5f - dayLength * 0.5f, coldestAt, 0.002f, "coldest at sunrise");
                Assert.Greater(warmestAt, 0.5f, "warmest after noon");
                Assert.Less(warmestAt, 0.5f + dayLength * 0.5f, "and before sunset");
                Assert.AreEqual(-0.5f, coldest, 0.001f);
                Assert.AreEqual(0.5f, warmest, 0.001f);
            }
        }

        // Largest on dry, high, open ground; much smaller under trees, on the shore and over water; deeper on snowy nights.
        [Test]
        public void TheSwingDependsOnThePlace()
        {
            EnvironmentSample open = new EnvironmentSample { isValid = true, isLand = true, moisture = 0.5f, normalizedHeight = 0.5f };
            EnvironmentSample dryHigh = open;
            dryHigh.moisture = 0f;
            dryHigh.normalizedHeight = 0.9f;
            EnvironmentSample forest = open;
            forest.treeCover = 1f;
            EnvironmentSample shore = open;
            shore.isShore = true;
            EnvironmentSample water = new EnvironmentSample { isValid = true, isWater = true };

            float swing = DailyTemperature.Swing(open, 0.5f);
            Assert.AreEqual(DailyTemperature.BaseSwing * 0.95f * 1.0846f, swing, 0.2f);
            Assert.Greater(DailyTemperature.Swing(dryHigh, 0.8f), 12f, "a dry high meadow in summer");
            Assert.Less(DailyTemperature.Swing(forest, 0.5f), swing * 0.7f);
            Assert.AreEqual(swing * 0.5f, DailyTemperature.Swing(shore, 0.5f), 0.001f);
            Assert.AreEqual(DailyTemperature.WaterSwing, DailyTemperature.Swing(water, 0.5f), 0.001f);
            Assert.Greater(DailyTemperature.Swing(open, 0.8f), DailyTemperature.Swing(open, 0.2f), "longer days swing more");

            float sunrise = 0.25f;
            Assert.AreEqual(-0.5f * swing * 1.3f, DailyTemperature.Offset(swing, sunrise, 0.5f, 1f), 0.01f, "snowy nights are colder");
            Assert.AreEqual(0.5f * swing, DailyTemperature.Offset(swing, sunrise + 1f / 3f, 0.5f, 1f), 0.01f, "snow leaves the afternoon");
        }

        // The scene's one light hands over from the sun to the moon as the sun sinks. With the moon already up it used to
        // switch straight to full moonlight 4° below the horizon, a jump in the light and the sky; now the light dims to
        // nothing and the moon fades in, so no step of a tenth of a degree changes it by more than a little.
        [Test]
        public void TheLightCrossesFromSunToMoonWithoutAJump()
        {
            const float Sun = 2.3f;
            const float Moon = 0.45f;
            float previous = float.NaN;
            float largestStep = 0f;
            bool sawMoon = false;
            for (float elevation = 20f; elevation >= -30f; elevation -= 0.1f)
            {
                DayState moment = new DayState
                {
                    sunElevation = elevation, sunDirection = Vector3.up, moonDirection = Vector3.up, moonlight = 1f
                };
                DayNightLighting.MainLight light = DayNightLighting.MainLightAt(moment, Sun, Moon);
                sawMoon |= light.isMoon;
                if (!float.IsNaN(previous)) largestStep = Mathf.Max(largestStep, Mathf.Abs(light.intensity - previous));
                previous = light.intensity;
            }

            Assert.IsTrue(sawMoon);
            Assert.AreEqual(Moon, previous, 1e-4f, "a full moon high up shines at its full light deep in the night");
            Assert.Less(largestStep, 0.04f);
        }

        [Test]
        public void WithoutSeasonsEveryDayIsAnEquinox()
        {
            Assert.AreEqual(0.5f, DayCycle.Evaluate(0.5f, Latitude, float.NaN, Month, 0f).dayLength, 1e-4f);
        }
    }
}
