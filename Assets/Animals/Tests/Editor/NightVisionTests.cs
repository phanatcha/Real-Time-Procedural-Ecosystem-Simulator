using Boreal.TerrainGeneration;
using NUnit.Framework;

namespace Boreal.Animals.Tests
{
    // Seeing in the dark (item 75): what share of its vision radius an animal sees by each light, with ordinary eyes
    // and with night eyes.
    public class NightVisionTests
    {
        const float Tolerance = 1e-3f;
        const float Sunrise = 0.3f;

        [Test]
        public void OrdinaryEyesSeeLessTheDarkerItIs()
        {
            Assert.AreEqual(1f, SeekFood.SightShare(1f, 0f), Tolerance);
            Assert.AreEqual(0.8f, SeekFood.SightShare(Sunrise, 0f), Tolerance);
            Assert.AreEqual(0.4f, SeekFood.SightShare(DayCycle.FullMoonLight, 0f), Tolerance);
            Assert.AreEqual(0.15f, SeekFood.SightShare(DayCycle.StarLight, 0f), Tolerance);
        }

        [Test]
        public void NightEyesSeeFurtherInTheDarkButAreDazzledByDay()
        {
            Assert.AreEqual(0.85f, SeekFood.SightShare(1f, 1f), Tolerance);
            Assert.AreEqual(0.95f, SeekFood.SightShare(Sunrise, 1f), Tolerance);
            Assert.AreEqual(0.8f, SeekFood.SightShare(DayCycle.FullMoonLight, 1f), Tolerance);
            Assert.AreEqual(0.55f, SeekFood.SightShare(DayCycle.StarLight, 1f), Tolerance);
        }

        // Founders (0.2) are a fifth of the way from ordinary eyes to night eyes, and nothing darker than starlight
        // counts.
        [Test]
        public void SightIsBetweenTheTwoKindsOfEyes()
        {
            Assert.AreEqual(0.4f + 0.2f * (0.8f - 0.4f), SeekFood.SightShare(DayCycle.FullMoonLight, 0.2f), Tolerance);
            Assert.AreEqual(SeekFood.SightShare(DayCycle.StarLight, 0f), SeekFood.SightShare(0f, 0f), Tolerance);

            float previous = 0f;
            for (float light = DayCycle.StarLight; light <= 1f; light *= 1.2f)
            {
                float share = SeekFood.SightShare(light, 0f);
                Assert.GreaterOrEqual(share, previous, $"ordinary eyes saw less at light {light} than in less light");
                previous = share;
            }
        }

        [Test]
        public void OnlyNightEyesShine()
        {
            Assert.AreEqual(0f, AnimalBodyView.EyeshineStrength(0.2f), Tolerance, "founders' eyes don't shine");
            Assert.AreEqual(0f, AnimalBodyView.EyeshineStrength(0.3f), Tolerance);
            Assert.AreEqual(1f, AnimalBodyView.EyeshineStrength(0.8f), Tolerance);
            Assert.AreEqual(1f, AnimalBodyView.EyeshineStrength(1f), Tolerance);
        }

        [Test]
        public void EyesHaveNames()
        {
            Assert.AreEqual("ordinary eyes", SeekFood.DescribeNightVision(0.2f));
            Assert.AreEqual("fair night eyes", SeekFood.DescribeNightVision(0.5f));
            Assert.AreEqual("night eyes", SeekFood.DescribeNightVision(0.9f));
        }
    }
}
