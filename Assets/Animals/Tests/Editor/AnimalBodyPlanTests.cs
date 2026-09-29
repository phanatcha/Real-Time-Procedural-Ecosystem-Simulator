using NUnit.Framework;

public class AnimalBodyPlanTests
{
    const float Tolerance = 0.0001f;

    [Test]
    public void PlainBodyHasNoEffects()
    {
        BodyPlanEffects effects = AnimalBodyPlan.Evaluate(CreateFounder());

        Assert.AreEqual(1f, effects.speedMultiplier);
        Assert.AreEqual(1f, effects.visionMultiplier);
        Assert.AreEqual(1f, effects.strengthMultiplier);
        Assert.AreEqual(0f, effects.extraFeedingReach);
        Assert.AreEqual(1f, effects.damageTakenMultiplier);
        Assert.AreEqual(0f, effects.partsMass);
        Assert.AreEqual(0f, effects.coldToleranceChange);
        Assert.AreEqual(0f, effects.longestLegs);
    }

    [Test]
    public void TwoFullLegPairsDoubleSpeedAndAThirdPairAddsLess()
    {
        AnimalGenome genome = CreateFounder();
        genome.SetPart(BodySite.FrontPair, BodyPartType.Legs, 1f);
        genome.SetPart(BodySite.RearPair, BodyPartType.Legs, 1f);
        Assert.That(AnimalBodyPlan.Evaluate(genome).speedMultiplier, Is.EqualTo(2f).Within(Tolerance));

        genome.SetPart(BodySite.MiddlePair, BodyPartType.Legs, 1f);
        Assert.That(AnimalBodyPlan.Evaluate(genome).speedMultiplier, Is.EqualTo(2.25f).Within(Tolerance));
    }

    [Test]
    public void PartialPartsGiveProportionalEffects()
    {
        AnimalGenome genome = CreateFounder();
        genome.SetPart(BodySite.FrontPair, BodyPartType.Legs, 0.4f);
        genome.SetPart(BodySite.Head, BodyPartType.Neck, 0.5f);

        BodyPlanEffects effects = AnimalBodyPlan.Evaluate(genome);

        Assert.That(effects.speedMultiplier, Is.EqualTo(1.2f).Within(Tolerance));
        Assert.That(effects.extraFeedingReach, Is.EqualTo(5f).Within(Tolerance));
        Assert.That(effects.visionMultiplier, Is.EqualTo(1.05f).Within(Tolerance));
        Assert.That(effects.longestLegs, Is.EqualTo(0.4f).Within(Tolerance));
    }

    [Test]
    public void HornsDoubleStrengthAndEyeStalksExtendVision()
    {
        AnimalGenome horned = CreateFounder();
        horned.SetPart(BodySite.Head, BodyPartType.Horn, 1f);
        Assert.That(AnimalBodyPlan.Evaluate(horned).strengthMultiplier, Is.EqualTo(2f).Within(Tolerance));

        AnimalGenome eyed = CreateFounder();
        eyed.SetPart(BodySite.Head, BodyPartType.EyeStalks, 1f);
        Assert.That(AnimalBodyPlan.Evaluate(eyed).visionMultiplier, Is.EqualTo(1.6f).Within(Tolerance));
    }

    [Test]
    public void PlatesAbsorbDamageUpToACapAndSlowTheAnimal()
    {
        AnimalGenome genome = CreateFounder();
        genome.SetPart(BodySite.Back, BodyPartType.Plates, 1f);
        BodyPlanEffects onePlate = AnimalBodyPlan.Evaluate(genome);
        Assert.That(onePlate.damageTakenMultiplier, Is.EqualTo(0.8f).Within(Tolerance));
        Assert.That(onePlate.speedMultiplier, Is.EqualTo(0.92f).Within(Tolerance));

        foreach (BodySite site in new[] { BodySite.Tail, BodySite.FrontPair, BodySite.MiddlePair, BodySite.RearPair })
        {
            genome.SetPart(site, BodyPartType.Plates, 1f);
        }

        Assert.That(AnimalBodyPlan.Evaluate(genome).damageTakenMultiplier, Is.EqualTo(0.4f).Within(Tolerance));
    }

    [Test]
    public void PairedPartsWeighTwiceAsMuch()
    {
        AnimalGenome single = CreateFounder();
        single.SetPart(BodySite.Back, BodyPartType.Plates, 1f);
        AnimalGenome paired = CreateFounder();
        paired.SetPart(BodySite.MiddlePair, BodyPartType.Plates, 1f);

        Assert.That(AnimalBodyPlan.Evaluate(paired).partsMass,
                    Is.EqualTo(2f * AnimalBodyPlan.Evaluate(single).partsMass).Within(Tolerance));
    }

    [Test]
    public void LongLimbsShedHeatAndBulkKeepsIt()
    {
        AnimalGenome leggy = CreateFounder();
        leggy.SetPart(BodySite.FrontPair, BodyPartType.Legs, 1f);
        BodyPlanEffects limbs = AnimalBodyPlan.Evaluate(leggy);
        Assert.Less(limbs.coldToleranceChange, 0f);
        Assert.Greater(limbs.heatToleranceChange, 0f);

        AnimalGenome stocky = CreateFounder();
        stocky[AnimalGene.BodyBulk] = 1.5f;
        BodyPlanEffects bulk = AnimalBodyPlan.Evaluate(stocky);
        Assert.Greater(bulk.coldToleranceChange, 0f);
        Assert.Less(bulk.heatToleranceChange, 0f);
    }

    [Test]
    public void SpeedNeverDropsBelowTheMinimum()
    {
        AnimalGenome genome = CreateFounder();
        foreach (BodySite site in new[] { BodySite.Back, BodySite.Tail, BodySite.FrontPair, BodySite.MiddlePair, BodySite.RearPair })
        {
            genome.SetPart(site, BodyPartType.Plates, 1f);
        }

        Assert.GreaterOrEqual(AnimalBodyPlan.Evaluate(genome).speedMultiplier, AnimalBodyPlan.MinimumSpeedMultiplier);
    }

    static AnimalGenome CreateFounder()
    {
        AnimalGenome genome = AnimalGenome.Create();
        genome[AnimalGene.BodyBulk] = 1f;
        genome[AnimalGene.BodyHeight] = 1f;
        return genome;
    }
}
