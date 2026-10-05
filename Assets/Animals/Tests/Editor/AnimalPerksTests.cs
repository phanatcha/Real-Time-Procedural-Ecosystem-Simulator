using NUnit.Framework;

public class AnimalPerksTests
{
    const float Tolerance = 0.0001f;

    [Test]
    public void AnimalsWithoutPerksAreUnchanged()
    {
        PerkEffects effects = AnimalPerks.Evaluate(AnimalGenome.Create());

        Assert.AreEqual(0f, effects.coldToleranceChange);
        Assert.AreEqual(0f, effects.heatToleranceChange);
        Assert.AreEqual(1f, effects.energyUseMultiplier);
        Assert.AreEqual(0f, effects.venomDamageReturned);
        Assert.AreEqual(1f, effects.visibility);
        Assert.AreEqual(0f, effects.regenerationPerSecond);
        Assert.IsFalse(effects.canEatSpoiledFood);
        Assert.AreEqual(1f, effects.plantDigestionMultiplier);
    }

    [Test]
    public void AnUnsetGenomeHasNoPerkEffects()
    {
        Assert.AreEqual(1f, AnimalPerks.Evaluate(null).energyUseMultiplier);
        Assert.AreEqual(1f, AnimalPerks.Evaluate(new AnimalGenome()).visibility);
    }

    [Test]
    public void ThickFurTradesHeatToleranceForColdTolerance()
    {
        PerkEffects effects = AnimalPerks.Evaluate(WithPerks(AnimalPerk.ThickFur));

        Assert.AreEqual(AnimalPerks.ThickFurToleranceShift, effects.coldToleranceChange);
        Assert.AreEqual(-AnimalPerks.ThickFurToleranceShift, effects.heatToleranceChange);
        Assert.AreEqual(1f, effects.energyUseMultiplier);
    }

    [Test]
    public void VenomReturnsPartOfEachBiteAndCostsEnergy()
    {
        PerkEffects effects = AnimalPerks.Evaluate(WithPerks(AnimalPerk.Venom));

        Assert.AreEqual(AnimalPerks.VenomDamageReturned, effects.venomDamageReturned);
        Assert.AreEqual(1f + AnimalPerks.VenomEnergyCost, effects.energyUseMultiplier, Tolerance);
    }

    [Test]
    public void CamouflageHidesTheAnimalAndCostsEnergy()
    {
        PerkEffects effects = AnimalPerks.Evaluate(WithPerks(AnimalPerk.Camouflage));

        Assert.AreEqual(AnimalPerks.CamouflageVisibility, effects.visibility);
        Assert.Less(effects.visibility, 1f);
        Assert.AreEqual(1f + AnimalPerks.CamouflageEnergyCost, effects.energyUseMultiplier, Tolerance);
    }

    [Test]
    public void RegenerationHealsAndCostsEnergy()
    {
        PerkEffects effects = AnimalPerks.Evaluate(WithPerks(AnimalPerk.Regeneration));

        Assert.AreEqual(AnimalPerks.RegenerationPerSecond, effects.regenerationPerSecond);
        Assert.AreEqual(1f + AnimalPerks.RegenerationEnergyCost, effects.energyUseMultiplier, Tolerance);
    }

    [Test]
    public void AScavengerGutEatsSpoiledFoodButDigestsPlantsWorse()
    {
        PerkEffects effects = AnimalPerks.Evaluate(WithPerks(AnimalPerk.ScavengerGut));

        Assert.IsTrue(effects.canEatSpoiledFood);
        Assert.AreEqual(AnimalPerks.ScavengerPlantDigestion, effects.plantDigestionMultiplier);
        Assert.Less(effects.plantDigestionMultiplier, 1f);
    }

    [Test]
    public void TheUpkeepOfSeveralPerksAddsUp()
    {
        PerkEffects effects = AnimalPerks.Evaluate(
            WithPerks(AnimalPerk.Venom, AnimalPerk.Camouflage, AnimalPerk.Regeneration));

        float expected = 1f + AnimalPerks.VenomEnergyCost + AnimalPerks.CamouflageEnergyCost +
                         AnimalPerks.RegenerationEnergyCost;
        Assert.AreEqual(expected, effects.energyUseMultiplier, Tolerance);
    }

    [Test]
    public void DescribeListsThePerksAnAnimalHas()
    {
        Assert.AreEqual("none", AnimalPerks.Describe(AnimalGenome.Create()));
        Assert.AreEqual("venom, scavenger gut",
                        AnimalPerks.Describe(WithPerks(AnimalPerk.ScavengerGut, AnimalPerk.Venom)));
    }

    [Test]
    public void DescribeSharesLeavesOutPerksNobodyHas()
    {
        float[] shares = new float[AnimalGenome.PerkCount];
        Assert.AreEqual("none yet", AnimalPerks.DescribeShares(shares));

        shares[(int)AnimalPerk.ThickFur] = 0.12f;
        shares[(int)AnimalPerk.Regeneration] = 0.5f;
        Assert.AreEqual("thick fur 12%, regeneration 50%", AnimalPerks.DescribeShares(shares));
    }

    static AnimalGenome WithPerks(params AnimalPerk[] perks)
    {
        AnimalGenome genome = AnimalGenome.Create();
        foreach (AnimalPerk perk in perks)
        {
            genome.SetPerk(perk, true);
        }

        return genome;
    }
}
