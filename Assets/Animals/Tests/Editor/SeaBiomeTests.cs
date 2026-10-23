using NUnit.Framework;

public class SeaBiomeTests
{
    static readonly SeaBiomeRules Rules = SeaBiomeRules.Default;

    // Noise values that leave the rules alone: substrate 0.5 means no blurring, reef 0 means "in a reef patch".
    const float NoBlur = 0.5f;
    const float InReefPatch = 0f;
    const float OutsideReefPatch = 1f;
    const float Flat = 0f;
    const float Steep = 20f;
    const float NoBasin = 0f;
    const float Cold = -3f;
    const float Warm = 4f;

    static SeaBiome Classify(float depth, float slope, float basin = NoBasin, float celsius = Cold,
                             float reefPatch = OutsideReefPatch, float substrate = NoBlur)
    {
        return Rules.Classify(depth, slope, basin, celsius, reefPatch, substrate);
    }

    [Test]
    public void ShallowSandIsSeagrass()
    {
        Assert.AreEqual(SeaBiome.SeagrassMeadow, Classify(0.5f, Flat));
        Assert.AreEqual(SeaBiome.SeagrassMeadow, Classify(Rules.seagrassMaxDepth, Flat));
    }

    [Test]
    public void SandBeyondTheSeagrassDepthIsOpenSea()
    {
        Assert.AreEqual(SeaBiome.OpenSea, Classify(Rules.seagrassMaxDepth + 0.5f, Flat));
    }

    [Test]
    public void RockIsKelpForestDownToItsDepth()
    {
        Assert.AreEqual(SeaBiome.KelpForest, Classify(0.5f, Steep));
        Assert.AreEqual(SeaBiome.KelpForest, Classify(Rules.kelpMaxDepth, Steep));
        Assert.AreEqual(SeaBiome.OpenSea, Classify(Rules.kelpMaxDepth + 0.5f, Steep));
    }

    [Test]
    public void ReefsGrowOnWarmRockInsideTheirDepthRangeAndPatches()
    {
        float depth = (Rules.reefMinDepth + Rules.reefMaxDepth) * 0.5f;

        Assert.AreEqual(SeaBiome.ColdWaterReef, Classify(depth, Steep, celsius: Warm, reefPatch: InReefPatch));
        Assert.AreNotEqual(SeaBiome.ColdWaterReef, Classify(depth, Steep, celsius: Cold, reefPatch: InReefPatch),
                           "too cold");
        Assert.AreNotEqual(SeaBiome.ColdWaterReef, Classify(depth, Steep, celsius: Warm, reefPatch: OutsideReefPatch),
                           "outside a reef patch");
        Assert.AreNotEqual(SeaBiome.ColdWaterReef, Classify(depth, Flat, celsius: Warm, reefPatch: InReefPatch),
                           "reefs need rock");
        Assert.AreNotEqual(SeaBiome.ColdWaterReef,
                           Classify(Rules.reefMinDepth - 0.5f, Steep, celsius: Warm, reefPatch: InReefPatch),
                           "too shallow");
        Assert.AreNotEqual(SeaBiome.ColdWaterReef,
                           Classify(Rules.reefMaxDepth + 0.5f, Steep, celsius: Warm, reefPatch: InReefPatch),
                           "too deep");
    }

    [Test]
    public void ReefCoverageSetsWhichPatchesBecomeReef()
    {
        SeaBiomeRules none = Rules;
        none.reefCoverage = 0f;
        SeaBiomeRules all = Rules;
        all.reefCoverage = 1f;
        float depth = (Rules.reefMinDepth + Rules.reefMaxDepth) * 0.5f;

        Assert.AreNotEqual(SeaBiome.ColdWaterReef, none.Classify(depth, Steep, NoBasin, Warm, 0.3f, NoBlur));
        Assert.AreEqual(SeaBiome.ColdWaterReef, all.Classify(depth, Steep, NoBasin, Warm, 0.9f, NoBlur));
    }

    [Test]
    public void DeepBasinsAreDeadZones()
    {
        Assert.AreEqual(SeaBiome.DeadZone, Classify(Rules.deadZoneMinDepth, Flat, basin: 1f));
        Assert.AreEqual(SeaBiome.DeadZone, Classify(Rules.deadZoneMinDepth + 5f, Steep, basin: Rules.deadZoneBasin,
                                                    celsius: Warm, reefPatch: InReefPatch));
    }

    [Test]
    public void ShallowBasinsAndOpenWaterAreNotDeadZones()
    {
        Assert.AreEqual(SeaBiome.SeagrassMeadow, Classify(2f, Flat, basin: 1f), "the water still mixes when shallow");
        Assert.AreNotEqual(SeaBiome.DeadZone, Classify(Rules.deadZoneMinDepth + 5f, Flat,
                                                       basin: Rules.deadZoneBasin - 0.1f));
    }

    [Test]
    public void SubstratePatchesBlurTheLineBetweenRockAndSand()
    {
        float nearlySteep = Rules.rockySlope - Rules.rockySlopeJitter * 0.5f;

        Assert.AreEqual(SeaBiome.SeagrassMeadow, Classify(2f, nearlySteep, substrate: NoBlur));
        Assert.AreEqual(SeaBiome.KelpForest, Classify(2f, nearlySteep, substrate: 1f), "a rocky patch");
        Assert.AreEqual(SeaBiome.SeagrassMeadow, Classify(2f, Rules.rockySlope, substrate: 0f), "a sandy patch");
    }

    [Test]
    public void EveryBiomeHasAName()
    {
        foreach (SeaBiome biome in System.Enum.GetValues(typeof(SeaBiome)))
        {
            Assert.IsFalse(string.IsNullOrEmpty(SeaBiomeRules.Name(biome)));
        }
    }
}
