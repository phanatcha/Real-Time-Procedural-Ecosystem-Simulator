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

        [Test]
        public void WithoutSeasonsEveryDayIsAnEquinox()
        {
            Assert.AreEqual(0.5f, DayCycle.Evaluate(0.5f, Latitude, float.NaN, Month, 0f).dayLength, 1e-4f);
        }
    }
}
