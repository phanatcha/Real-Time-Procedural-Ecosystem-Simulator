using NUnit.Framework;

namespace Boreal.Animals.Tests
{
    public class MatingRulesTests
    {
        const float Tolerance = 0.0001f;

        [Test]
        public void LowDriveOnlyClonesAndHighDriveOnlyMates()
        {
            Assert.IsFalse(MatingRules.CanMate(0.1f));
            Assert.IsTrue(MatingRules.CanClone(0.1f));

            Assert.IsTrue(MatingRules.CanMate(0.5f));
            Assert.IsTrue(MatingRules.CanClone(0.5f));

            Assert.IsTrue(MatingRules.CanMate(0.9f));
            Assert.IsFalse(MatingRules.CanClone(0.9f));
        }

        [Test]
        public void MateSearchLastsDriveTimesTwentySecondsBetweenTheExtremes()
        {
            Assert.AreEqual(0f, MatingRules.MateSearchTime(0.1f, 20f));
            Assert.AreEqual(3f, MatingRules.MateSearchTime(0.15f, 20f), Tolerance);
            Assert.AreEqual(10f, MatingRules.MateSearchTime(0.5f, 20f), Tolerance);
            Assert.AreEqual(17f, MatingRules.MateSearchTime(0.85f, 20f), Tolerance);
            Assert.IsTrue(float.IsPositiveInfinity(MatingRules.MateSearchTime(0.9f, 20f)));
        }

        [Test]
        public void FertilityIsFullForCloseRelativesAndFadesToNoneAtTheSpeciesThreshold()
        {
            const float Threshold = 0.03f;

            Assert.AreEqual(1f, MatingRules.Fertility(0f, Threshold, 0.5f));
            Assert.AreEqual(1f, MatingRules.Fertility(0.015f, Threshold, 0.5f));
            Assert.AreEqual(0.5f, MatingRules.Fertility(0.0225f, Threshold, 0.5f), Tolerance);
            Assert.AreEqual(0f, MatingRules.Fertility(0.03f, Threshold, 0.5f));
            Assert.AreEqual(0f, MatingRules.Fertility(0.2f, Threshold, 0.5f));
        }

        [Test]
        public void FertilityFallsSteadilyWithDistance()
        {
            float previous = 1f;
            for (float distance = 0f; distance <= 0.04f; distance += 0.001f)
            {
                float fertility = MatingRules.Fertility(distance, 0.03f, 0.5f);
                Assert.That(fertility, Is.InRange(0f, 1f));
                Assert.LessOrEqual(fertility, previous + Tolerance);
                previous = fertility;
            }
        }

        // The snowball (item 63): 1 - (distance / threshold)^2, so fertility falls ever faster towards the threshold.
        [Test]
        public void SnowballFertilityFallsEverFasterToNoneAtTheThreshold()
        {
            const float Threshold = 0.04f;
            Assert.AreEqual(1f, MatingRules.SnowballFertility(0f, Threshold));
            Assert.AreEqual(0.9375f, MatingRules.SnowballFertility(0.01f, Threshold), Tolerance);
            Assert.AreEqual(0.75f, MatingRules.SnowballFertility(0.02f, Threshold), Tolerance);
            Assert.AreEqual(0.4375f, MatingRules.SnowballFertility(0.03f, Threshold), Tolerance);
            Assert.AreEqual(0f, MatingRules.SnowballFertility(0.04f, Threshold));
            Assert.AreEqual(0f, MatingRules.SnowballFertility(0.5f, Threshold));

            // Each step down is bigger than the one before.
            float previousDrop = 0f;
            for (float distance = 0.002f; distance < Threshold; distance += 0.002f)
            {
                float drop = MatingRules.SnowballFertility(distance - 0.002f, Threshold) -
                             MatingRules.SnowballFertility(distance, Threshold);
                Assert.Greater(drop, previousDrop - Tolerance);
                previousDrop = drop;
            }

            // The curve picks the rule; Linear is the straight fade from before.
            Assert.AreEqual(0.75f, MatingRules.Fertility(0.02f, Threshold, 0.5f, FertilityCurve.Snowball), Tolerance);
            Assert.AreEqual(1f, MatingRules.Fertility(0.02f, Threshold, 0.5f, FertilityCurve.Linear));
        }

        // A child of parents more than half the species threshold apart counts as a hybrid.
        [Test]
        public void HybridsComeFromParentsMoreThanHalfTheThresholdApart()
        {
            Assert.IsFalse(MatingRules.IsHybrid(0.01f, 0.05f));
            Assert.IsFalse(MatingRules.IsHybrid(0.025f, 0.05f));
            Assert.IsTrue(MatingRules.IsHybrid(0.026f, 0.05f));
            Assert.IsFalse(MatingRules.IsHybrid(-1f, 0.05f), "A clone's distance of -1 is no hybrid.");
        }

        [Test]
        public void FertilityWithoutAFadeStopsAtTheThreshold()
        {
            Assert.AreEqual(1f, MatingRules.Fertility(0.0299f, 0.03f, 1f));
            Assert.AreEqual(0f, MatingRules.Fertility(0.03f, 0.03f, 1f));
        }
    }
}
