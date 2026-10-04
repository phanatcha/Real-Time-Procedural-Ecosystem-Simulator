using NUnit.Framework;

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

    [Test]
    public void FertilityWithoutAFadeStopsAtTheThreshold()
    {
        Assert.AreEqual(1f, MatingRules.Fertility(0.0299f, 0.03f, 1f));
        Assert.AreEqual(0f, MatingRules.Fertility(0.03f, 0.03f, 1f));
    }
}
