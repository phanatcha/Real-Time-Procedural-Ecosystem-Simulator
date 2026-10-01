using NUnit.Framework;

public class WaterAccessTests
{
    const float Tolerance = 0.0001f;
    const float WadingDepth = 1f;

    static readonly WaterMovement Water = WaterMovement.Default;

    [Test]
    public void FinsCanSproutByDefault()
    {
        Assert.IsTrue(BodyPlanMutation.Default.allowFins);
    }

    [Test]
    public void PlainBodyCanOnlyWade()
    {
        float ability = AnimalBodyPlan.Evaluate(CreateFounder()).swimmingAbility;

        Assert.AreEqual(0f, ability);
        Assert.IsFalse(Water.CanSwimDeepWater(ability));
    }

    [Test]
    public void FullTailFinReachesDeepWater()
    {
        AnimalGenome genome = CreateFounder();
        genome.SetPart(BodySite.Tail, BodyPartType.Fins, 1f);
        float ability = AnimalBodyPlan.Evaluate(genome).swimmingAbility;

        Assert.That(ability, Is.EqualTo(AnimalBodyPlan.TailFinSwimming).Within(Tolerance));
        Assert.IsTrue(Water.CanSwimDeepWater(ability));
    }

    [Test]
    public void FinsAddUpInProportionToTheirSize()
    {
        AnimalGenome genome = CreateFounder();
        genome.SetPart(BodySite.Back, BodyPartType.Fins, 0.5f);
        genome.SetPart(BodySite.FrontPair, BodyPartType.Fins, 0.4f);
        float expected = 0.5f * AnimalBodyPlan.BackFinSwimming + 0.4f * AnimalBodyPlan.PairedFinSwimming;

        Assert.That(AnimalBodyPlan.Evaluate(genome).swimmingAbility, Is.EqualTo(expected).Within(Tolerance));
    }

    [Test]
    public void LegsAloneHelpWadingButNeverReachDeepWater()
    {
        AnimalGenome genome = CreateFounder();
        genome.SetPart(BodySite.FrontPair, BodyPartType.Legs, 1f);
        genome.SetPart(BodySite.MiddlePair, BodyPartType.Legs, 1f);
        genome.SetPart(BodySite.RearPair, BodyPartType.Legs, 1f);
        float ability = AnimalBodyPlan.Evaluate(genome).swimmingAbility;

        Assert.That(ability, Is.EqualTo(AnimalBodyPlan.LegWadingSwimming).Within(Tolerance));
        Assert.IsFalse(Water.CanSwimDeepWater(ability));
    }

    [Test]
    public void SwimmingAbilityIsCappedAtOne()
    {
        AnimalGenome genome = CreateFounder();
        genome.SetPart(BodySite.Back, BodyPartType.Fins, 1f);
        genome.SetPart(BodySite.Tail, BodyPartType.Fins, 1f);
        genome.SetPart(BodySite.FrontPair, BodyPartType.Fins, 1f);
        genome.SetPart(BodySite.MiddlePair, BodyPartType.Fins, 1f);
        genome.SetPart(BodySite.RearPair, BodyPartType.Fins, 1f);

        Assert.AreEqual(1f, AnimalBodyPlan.Evaluate(genome).swimmingAbility);
    }

    [Test]
    public void SwimmersMoveMuchFasterInWaterThanWaders()
    {
        Assert.That(Water.SpeedMultiplier(0f), Is.EqualTo(Water.wadingSpeed).Within(Tolerance));
        Assert.That(Water.SpeedMultiplier(1f), Is.EqualTo(Water.swimmingSpeed).Within(Tolerance));
        Assert.Greater(Water.SpeedMultiplier(0.5f), Water.SpeedMultiplier(0.2f));
        Assert.Greater(Water.SpeedMultiplier(1f), 3f * Water.SpeedMultiplier(0f));
    }

    [Test]
    public void WadingDrainsEnergyFastAndSwimmersPayNoExtra()
    {
        Assert.That(Water.EnergyMultiplier(0f), Is.EqualTo(Water.wadingEnergyCost).Within(Tolerance));
        Assert.Greater(Water.EnergyMultiplier(0f), Water.EnergyMultiplier(0.25f));
        Assert.That(Water.EnergyMultiplier(Water.deepWaterAbility), Is.EqualTo(1f).Within(Tolerance));
        Assert.That(Water.EnergyMultiplier(1f), Is.EqualTo(1f).Within(Tolerance));
    }

    [Test]
    public void WaterRoutesCostWadersMostAndNeverLessThanLand()
    {
        float wader = Water.PathCost(0f, 1f);
        float leggyWader = Water.PathCost(0f, 2f);
        float smallFins = Water.PathCost(0.25f, 1f);
        float swimmer = Water.PathCost(1f, 0.9f);

        // A finless capsule pays for both being slow (0.3) and burning energy four times as fast.
        Assert.That(wader, Is.EqualTo(Water.wadingEnergyCost / Water.wadingSpeed).Within(Tolerance));
        Assert.Greater(leggyWader, wader, "legs make land relatively quicker, so water looks worse");
        Assert.Greater(wader, smallFins);
        Assert.Greater(smallFins, swimmer);
        Assert.AreEqual(1f, swimmer);
    }

    [Test]
    public void OnlySwimmersMayUseDeepWater()
    {
        int wader = WaterAccess.AreaMask(false);
        int swimmer = WaterAccess.AreaMask(true);

        Assert.AreNotEqual(0, wader & (1 << WaterAccess.ShallowWaterArea));
        Assert.AreEqual(0, wader & (1 << WaterAccess.DeepWaterArea));
        Assert.AreEqual(0, wader & (1 << WaterAccess.DeadZoneArea), "dead zones are deep water");
        Assert.AreNotEqual(0, wader & 1, "land stays open");
        Assert.AreNotEqual(0, swimmer & (1 << WaterAccess.DeepWaterArea));
        Assert.AreNotEqual(0, swimmer & (1 << WaterAccess.DeadZoneArea));
        Assert.AreEqual(0, WaterAccess.LandAreas & WaterAccess.WaterAreas);
        Assert.AreNotEqual(0, WaterAccess.WaterAreas & (1 << WaterAccess.DeadZoneArea));
    }

    [Test]
    public void DeepWaterTouchingADeadZoneIsDeadZone()
    {
        WaterAccess.Corner deep = WaterAccess.Corner.Water(15f);
        WaterAccess.Corner dead = WaterAccess.Corner.Water(15f, deadZone: true);

        Assert.AreEqual(WaterAccess.Surface.DeadZone, WaterAccess.ClassifyTriangle(deep, dead, deep, WadingDepth));
        Assert.AreEqual(WaterAccess.Surface.DeepWater, WaterAccess.ClassifyTriangle(deep, deep, deep, WadingDepth));
        Assert.AreEqual(WaterAccess.DeadZoneArea, WaterAccess.AreaOf(WaterAccess.Surface.DeadZone));
    }

    [Test]
    public void ShallowWaterIsNeverADeadZone()
    {
        WaterAccess.Corner shallowButMarked = WaterAccess.Corner.Water(0.5f, deadZone: true);

        Assert.AreEqual(WaterAccess.Surface.ShallowWater,
                        WaterAccess.ClassifyTriangle(shallowButMarked, shallowButMarked, shallowButMarked, WadingDepth));
    }

    [Test]
    public void TrianglesWithoutWaterAreLand()
    {
        WaterAccess.Corner land = WaterAccess.Corner.Land;

        Assert.AreEqual(WaterAccess.Surface.Land, WaterAccess.ClassifyTriangle(land, land, land, WadingDepth));
    }

    [Test]
    public void AnyUnusableCornerLeavesAHole()
    {
        WaterAccess.Corner land = WaterAccess.Corner.Land;
        WaterAccess.Corner water = WaterAccess.Corner.Water(0.5f);
        WaterAccess.Corner cliff = WaterAccess.Corner.Unusable;

        Assert.AreEqual(WaterAccess.Surface.None, WaterAccess.ClassifyTriangle(land, land, cliff, WadingDepth));
        Assert.AreEqual(WaterAccess.Surface.None, WaterAccess.ClassifyTriangle(water, cliff, water, WadingDepth));
    }

    [Test]
    public void WaterNoDeeperThanTheWadingDepthIsShallow()
    {
        WaterAccess.Corner shallow = WaterAccess.Corner.Water(0.4f);
        WaterAccess.Corner edge = WaterAccess.Corner.Water(WadingDepth);

        Assert.AreEqual(WaterAccess.Surface.ShallowWater,
                        WaterAccess.ClassifyTriangle(shallow, edge, shallow, WadingDepth));
    }

    [Test]
    public void AnyDeepCornerMakesDeepWater()
    {
        WaterAccess.Corner shallow = WaterAccess.Corner.Water(0.4f);
        WaterAccess.Corner deep = WaterAccess.Corner.Water(WadingDepth + 0.1f);

        Assert.AreEqual(WaterAccess.Surface.DeepWater,
                        WaterAccess.ClassifyTriangle(shallow, shallow, deep, WadingDepth));
    }

    [Test]
    public void ShoreRampsAreWaterOfTheirDeepestCorner()
    {
        WaterAccess.Corner land = WaterAccess.Corner.Land;

        Assert.AreEqual(WaterAccess.Surface.ShallowWater,
                        WaterAccess.ClassifyTriangle(land, land, WaterAccess.Corner.Water(0.3f), WadingDepth));
        Assert.AreEqual(WaterAccess.Surface.DeepWater,
                        WaterAccess.ClassifyTriangle(land, WaterAccess.Corner.Water(4f), land, WadingDepth),
                        "a shore that drops straight into deep water is only open to swimmers");
    }

    [Test]
    public void NothingIsInWaterWithoutAWaterSurface()
    {
        float previous = WaterAccess.SurfaceHeight;
        try
        {
            WaterAccess.SurfaceHeight = float.NegativeInfinity;
            Assert.IsFalse(WaterAccess.HasWater);
            Assert.IsFalse(WaterAccess.IsInWater(-1000f));

            WaterAccess.SurfaceHeight = 27.9f;
            Assert.IsTrue(WaterAccess.IsInWater(27.9f));
            Assert.IsTrue(WaterAccess.IsInWater(27.9f + WaterAccess.SurfaceTolerance * 0.5f));
            Assert.IsFalse(WaterAccess.IsInWater(29f));
        }
        finally
        {
            WaterAccess.SurfaceHeight = previous;
        }
    }

    static AnimalGenome CreateFounder()
    {
        AnimalGenome genome = AnimalGenome.Create();
        genome[AnimalGene.BodyBulk] = 1f;
        genome[AnimalGene.BodyHeight] = 1f;
        return genome;
    }
}
