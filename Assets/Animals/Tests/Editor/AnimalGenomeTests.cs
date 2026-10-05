using NUnit.Framework;
using UnityEngine;

public class AnimalGenomeTests
{
    const float Tolerance = 0.0001f;

    Random.State savedRandomState;

    [SetUp]
    public void SetUp()
    {
        savedRandomState = Random.state;
        Random.InitState(1234);
    }

    [TearDown]
    public void TearDown()
    {
        Random.state = savedRandomState;
    }

    [Test]
    public void EveryGeneHasAValidRange()
    {
        for (int index = 0; index < AnimalGenome.GeneCount; index++)
        {
            AnimalGene gene = (AnimalGene)index;
            AnimalGeneDefinition definition = AnimalGenome.GetDefinition(gene);
            Assert.Less(definition.minimum, definition.maximum, gene.ToString());
            Assert.Greater(definition.stepScale, 0f, gene.ToString());
        }
    }

    [Test]
    public void SettingAGeneClampsItToItsBounds()
    {
        AnimalGenome genome = AnimalGenome.Create();

        genome[AnimalGene.DietAffinity] = 5f;
        Assert.AreEqual(1f, genome[AnimalGene.DietAffinity]);

        genome[AnimalGene.MoveSpeed] = -3f;
        Assert.AreEqual(AnimalGenome.GetDefinition(AnimalGene.MoveSpeed).minimum, genome[AnimalGene.MoveSpeed]);
    }

    [Test]
    public void UnsetGenomeIsInvalidAndCreatedGenomeIsValid()
    {
        Assert.IsFalse(new AnimalGenome().IsValid);
        Assert.IsTrue(AnimalGenome.Create().IsValid);
    }

    [Test]
    public void ZeroMutationChanceCopiesTheParentExactly()
    {
        AnimalGenome parent = CreateMidpointGenome();

        AnimalGenome child = parent.CreateMutatedCopy(0f, 0.5f, out bool mutated);

        Assert.IsFalse(mutated);
        AssertGenesEqual(parent, child);
    }

    [Test]
    public void MutatingACopyLeavesTheParentUnchanged()
    {
        AnimalGenome parent = CreateMidpointGenome();
        AnimalGenome snapshot = parent.Clone();

        parent.CreateMutatedCopy(100f, 0.5f, out bool mutated);

        Assert.IsTrue(mutated);
        AssertGenesEqual(snapshot, parent);
    }

    [Test]
    public void RepeatedExtremeMutationNeverLeavesGeneBounds()
    {
        AnimalGenome genome = CreateMidpointGenome();

        for (int generation = 0; generation < 2000; generation++)
        {
            genome = genome.CreateMutatedCopy(100f, 2f, out _);

            for (int index = 0; index < AnimalGenome.GeneCount; index++)
            {
                AnimalGene gene = (AnimalGene)index;
                AnimalGeneDefinition definition = AnimalGenome.GetDefinition(gene);
                Assert.That(genome[gene], Is.InRange(definition.minimum, definition.maximum), gene.ToString());
            }
        }
    }

    [Test]
    public void AdditiveGenesStepByAtMostTheMagnitudeTimesTheirScale()
    {
        AnimalGenome parent = CreateMidpointGenome();
        parent[AnimalGene.DietAffinity] = 0.5f;
        parent[AnimalGene.PreferredTemperature] = 0f;

        for (int sample = 0; sample < 500; sample++)
        {
            AnimalGenome child = parent.CreateMutatedCopy(100f, 0.1f, out _);
            Assert.That(child[AnimalGene.DietAffinity], Is.InRange(0.4f - Tolerance, 0.6f + Tolerance));
            Assert.That(child[AnimalGene.PreferredTemperature], Is.InRange(-1f - Tolerance, 1f + Tolerance));
        }
    }

    [Test]
    public void ProportionalGenesStepRelativeToTheirValue()
    {
        AnimalGenome parent = CreateMidpointGenome();
        parent[AnimalGene.MoveSpeed] = 20f;

        for (int sample = 0; sample < 500; sample++)
        {
            AnimalGenome child = parent.CreateMutatedCopy(100f, 0.1f, out _);
            Assert.That(child[AnimalGene.MoveSpeed], Is.InRange(18f - Tolerance, 22f + Tolerance));
        }
    }

    [Test]
    public void ToleranceAtZeroCanStillMutateUpward()
    {
        AnimalGenome parent = CreateMidpointGenome();
        parent[AnimalGene.ColdTolerance] = 0f;

        bool increased = false;
        for (int sample = 0; sample < 200 && !increased; sample++)
        {
            increased = parent.CreateMutatedCopy(100f, 0.5f, out _)[AnimalGene.ColdTolerance] > 0f;
        }

        Assert.IsTrue(increased);
    }

    [Test]
    public void DistanceIsZeroForIdenticalGenomesAndSymmetric()
    {
        AnimalGenome first = CreateMidpointGenome();
        AnimalGenome second = first.CreateMutatedCopy(100f, 0.5f, out _);

        Assert.AreEqual(0f, AnimalGenome.Distance(first, first.Clone()));
        Assert.Greater(AnimalGenome.Distance(first, second), 0f);
        Assert.AreEqual(AnimalGenome.Distance(first, second), AnimalGenome.Distance(second, first));
    }

    [Test]
    public void DistanceIsOneBetweenOppositeEndsOfEveryGeneFullyDifferentBodiesAndOppositePerks()
    {
        AnimalGenome minimum = AnimalGenome.Create();
        AnimalGenome maximum = AnimalGenome.Create();
        for (int index = 0; index < AnimalGenome.GeneCount; index++)
        {
            AnimalGene gene = (AnimalGene)index;
            maximum[gene] = AnimalGenome.GetDefinition(gene).maximum;
        }

        for (int index = 0; index < AnimalGenome.SiteCount; index++)
        {
            BodySite site = (BodySite)index;
            maximum.SetPart(site, AnimalGenome.GetAllowedParts(site)[0], 1f);
        }

        for (int index = 0; index < AnimalGenome.PerkCount; index++)
        {
            maximum.SetPerk((AnimalPerk)index, true);
        }

        Assert.That(AnimalGenome.Distance(minimum, maximum), Is.EqualTo(1f).Within(Tolerance));
    }

    [Test]
    public void NewGenomeIsAPlainBody()
    {
        AnimalGenome genome = AnimalGenome.Create();
        for (int index = 0; index < AnimalGenome.SiteCount; index++)
        {
            Assert.AreEqual(BodyPartType.None, genome.GetPartType((BodySite)index));
            Assert.AreEqual(0f, genome.GetPartSize((BodySite)index));
        }
    }

    [Test]
    public void OnlyAllowedPartsCanBeSet()
    {
        AnimalGenome genome = AnimalGenome.Create();
        genome.SetPart(BodySite.Head, BodyPartType.Neck, 0.4f);
        Assert.AreEqual(BodyPartType.Neck, genome.GetPartType(BodySite.Head));
        Assert.Throws<System.ArgumentException>(() => genome.SetPart(BodySite.Head, BodyPartType.Legs, 0.4f));

        genome.SetPart(BodySite.Head, BodyPartType.Neck, 0f);
        Assert.AreEqual(BodyPartType.None, genome.GetPartType(BodySite.Head));
    }

    [Test]
    public void CloneCopiesTheBodyPlanIndependently()
    {
        AnimalGenome parent = CreateMidpointGenome();
        parent.SetPart(BodySite.FrontPair, BodyPartType.Legs, 0.5f);

        AnimalGenome clone = parent.Clone();
        clone.SetPart(BodySite.FrontPair, BodyPartType.Legs, 0.9f);

        Assert.AreEqual(0.5f, parent.GetPartSize(BodySite.FrontPair));
        Assert.AreEqual(0.9f, clone.GetPartSize(BodySite.FrontPair));
    }

    [Test]
    public void GeneOnlyMutationLeavesTheBodyPlanAlone()
    {
        AnimalGenome parent = CreateMidpointGenome();
        parent.SetPart(BodySite.Head, BodyPartType.Horn, 0.3f);

        for (int sample = 0; sample < 200; sample++)
        {
            AnimalGenome child = parent.CreateMutatedCopy(100f, 0.5f, out _);
            Assert.AreEqual(BodyPartType.Horn, child.GetPartType(BodySite.Head));
            Assert.AreEqual(0.3f, child.GetPartSize(BodySite.Head));
            Assert.AreEqual(BodyPartType.None, child.GetPartType(BodySite.Back));
        }
    }

    [Test]
    public void CertainSproutingGrowsAnAllowedStubAtEveryEmptySite()
    {
        BodyPlanMutation rules = new BodyPlanMutation { sproutChance = 100f, sproutSize = 0.05f };

        for (int sample = 0; sample < 100; sample++)
        {
            AnimalGenome child = CreateMidpointGenome().CreateMutatedCopy(0f, 0f, rules, out bool mutated);
            Assert.IsTrue(mutated);
            for (int index = 0; index < AnimalGenome.SiteCount; index++)
            {
                BodySite site = (BodySite)index;
                BodyPartType type = child.GetPartType(site);
                Assert.IsTrue(AnimalGenome.IsAllowed(site, type), $"{type} at {site}");
                Assert.AreNotEqual(BodyPartType.Fins, type, "these rules don't allow fins");
                Assert.AreEqual(0.05f, child.GetPartSize(site), Tolerance);
            }
        }
    }

    [Test]
    public void FinsSproutOnlyWhenAllowed()
    {
        BodyPlanMutation rules = new BodyPlanMutation { sproutChance = 100f, sproutSize = 0.05f, allowFins = true };
        bool sawFins = false;
        for (int sample = 0; sample < 200 && !sawFins; sample++)
        {
            AnimalGenome child = CreateMidpointGenome().CreateMutatedCopy(0f, 0f, rules, out _);
            sawFins = child.GetPartType(BodySite.Tail) == BodyPartType.Fins;
        }

        Assert.IsTrue(sawFins);
    }

    [Test]
    public void PartSizesStayBetweenZeroAndOneAndShrunkPartsDisappear()
    {
        BodyPlanMutation rules = new BodyPlanMutation { growthChance = 100f, growthStep = 0.3f, lossSize = 0.1f };
        AnimalGenome genome = CreateMidpointGenome();
        genome.SetPart(BodySite.MiddlePair, BodyPartType.Legs, 0.5f);

        bool disappeared = false;
        for (int generation = 0; generation < 500 && !disappeared; generation++)
        {
            genome = genome.CreateMutatedCopy(0f, 0f, rules, out _);
            float size = genome.GetPartSize(BodySite.MiddlePair);
            Assert.That(size, Is.InRange(0f, 1f));
            disappeared = genome.GetPartType(BodySite.MiddlePair) == BodyPartType.None;
            if (!disappeared) Assert.GreaterOrEqual(size, 0.1f);
        }

        Assert.IsTrue(disappeared, "a random walk with loss should eventually lose the part");
    }

    [Test]
    public void RepurposingChangesThePartButKeepsItsSize()
    {
        BodyPlanMutation rules = new BodyPlanMutation { repurposeChance = 100f };
        AnimalGenome parent = CreateMidpointGenome();
        parent.SetPart(BodySite.Head, BodyPartType.Neck, 0.6f);

        AnimalGenome child = parent.CreateMutatedCopy(0f, 0f, rules, out bool mutated);

        Assert.IsTrue(mutated);
        Assert.AreNotEqual(BodyPartType.Neck, child.GetPartType(BodySite.Head));
        Assert.IsTrue(AnimalGenome.IsAllowed(BodySite.Head, child.GetPartType(BodySite.Head)));
        Assert.AreEqual(0.6f, child.GetPartSize(BodySite.Head));
    }

    [Test]
    public void BodyDifferencesCountBySizeAndMoreForDifferentParts()
    {
        AnimalGenome plain = CreateMidpointGenome();
        AnimalGenome stub = plain.Clone();
        stub.SetPart(BodySite.Head, BodyPartType.Neck, 0.05f);
        AnimalGenome fullNeck = plain.Clone();
        fullNeck.SetPart(BodySite.Head, BodyPartType.Neck, 1f);
        AnimalGenome fullHorn = plain.Clone();
        fullHorn.SetPart(BodySite.Head, BodyPartType.Horn, 1f);
        AnimalGenome halfNeck = plain.Clone();
        halfNeck.SetPart(BodySite.Head, BodyPartType.Neck, 0.5f);
        AnimalGenome halfHorn = plain.Clone();
        halfHorn.SetPart(BodySite.Head, BodyPartType.Horn, 0.5f);

        float wholePart = AnimalGenome.Distance(plain, fullNeck);
        Assert.That(AnimalGenome.Distance(plain, stub), Is.EqualTo(0.05f * wholePart).Within(Tolerance));
        Assert.That(AnimalGenome.Distance(fullNeck, halfNeck), Is.EqualTo(0.5f * wholePart).Within(Tolerance));
        Assert.That(AnimalGenome.Distance(halfNeck, halfHorn), Is.EqualTo(wholePart).Within(Tolerance));
        Assert.That(AnimalGenome.Distance(fullNeck, fullHorn), Is.EqualTo(wholePart).Within(Tolerance));
    }

    [Test]
    public void DietDifferenceCountsTripleAnEqualShareOfAnotherGenesRange()
    {
        AnimalGenome parent = CreateMidpointGenome();
        AnimalGenome dietChanged = parent.Clone();
        AnimalGenome speedChanged = parent.Clone();
        AnimalGeneDefinition diet = AnimalGenome.GetDefinition(AnimalGene.DietAffinity);
        AnimalGeneDefinition speed = AnimalGenome.GetDefinition(AnimalGene.MoveSpeed);
        dietChanged[AnimalGene.DietAffinity] += 0.1f * (diet.maximum - diet.minimum);
        speedChanged[AnimalGene.MoveSpeed] += 0.1f * (speed.maximum - speed.minimum);

        float ratio = AnimalGenome.Distance(parent, dietChanged) / AnimalGenome.Distance(parent, speedChanged);

        Assert.That(ratio, Is.EqualTo(3f).Within(0.001f));
    }

    [Test]
    public void RecombiningIdenticalParentsCopiesThem()
    {
        AnimalGenome parent = CreateMidpointGenome();
        parent.SetPart(BodySite.Head, BodyPartType.Neck, 0.4f);

        AnimalGenome child = AnimalGenome.Recombine(parent, parent.Clone());

        Assert.AreEqual(0f, AnimalGenome.Distance(parent, child));
    }

    [Test]
    public void RecombinationTakesEveryGeneAndSiteWholeFromOneParent()
    {
        AnimalGenome first = AnimalGenome.Create();
        AnimalGenome second = AnimalGenome.Create();
        for (int index = 0; index < AnimalGenome.GeneCount; index++)
        {
            AnimalGene gene = (AnimalGene)index;
            second[gene] = AnimalGenome.GetDefinition(gene).maximum;
        }

        first.SetPart(BodySite.Head, BodyPartType.Neck, 0.3f);
        second.SetPart(BodySite.Head, BodyPartType.Horn, 0.9f);
        second.SetPart(BodySite.Tail, BodyPartType.Fins, 0.6f);

        int[] genesFromSecond = new int[AnimalGenome.GeneCount];
        int headsFromSecond = 0;
        const int Samples = 400;
        for (int sample = 0; sample < Samples; sample++)
        {
            AnimalGenome child = AnimalGenome.Recombine(first, second);
            for (int index = 0; index < AnimalGenome.GeneCount; index++)
            {
                AnimalGene gene = (AnimalGene)index;
                Assert.That(child[gene], Is.EqualTo(first[gene]).Or.EqualTo(second[gene]), gene.ToString());
                if (child[gene] == second[gene]) genesFromSecond[index]++;
            }

            // A part never comes with the other parent's size.
            BodyPartType head = child.GetPartType(BodySite.Head);
            Assert.AreEqual(head == BodyPartType.Neck ? 0.3f : 0.9f, child.GetPartSize(BodySite.Head));
            if (head == BodyPartType.Horn) headsFromSecond++;

            BodyPartType tail = child.GetPartType(BodySite.Tail);
            Assert.AreEqual(tail == BodyPartType.Fins ? 0.6f : 0f, child.GetPartSize(BodySite.Tail));
        }

        // Each parent gives about half of everything (400 samples: 200 expected, 6 standard deviations either way).
        for (int index = 0; index < AnimalGenome.GeneCount; index++)
        {
            Assert.That(genesFromSecond[index], Is.InRange(140, 260), ((AnimalGene)index).ToString());
        }

        Assert.That(headsFromSecond, Is.InRange(140, 260));
    }

    [Test]
    public void RecombiningLeavesTheParentsUnchanged()
    {
        AnimalGenome first = CreateMidpointGenome();
        AnimalGenome second = first.CreateMutatedCopy(100f, 0.5f, BodyPlanMutation.Default, out _);
        second.SetPart(BodySite.FrontPair, BodyPartType.Legs, 0.7f);
        AnimalGenome firstSnapshot = first.Clone();
        AnimalGenome secondSnapshot = second.Clone();

        AnimalGenome.Recombine(first, second);

        Assert.AreEqual(0f, AnimalGenome.Distance(first, firstSnapshot));
        Assert.AreEqual(0f, AnimalGenome.Distance(second, secondSnapshot));
    }

    [Test]
    public void ANewGenomeCarriesNoHarmfulMutations()
    {
        AnimalGenome genome = AnimalGenome.Create();

        Assert.AreEqual(0, genome.HarmfulMutations);
        Assert.AreEqual(1f, genome.EnergyUseMultiplier(0.02f));
    }

    [Test]
    public void HarmfulMutationChanceIsAPercentagePerBirth()
    {
        AnimalGenome genome = AnimalGenome.Create();

        Assert.IsFalse(genome.TryAddHarmfulMutation(0f));
        Assert.IsTrue(genome.TryAddHarmfulMutation(100f));
        Assert.AreEqual(1, genome.HarmfulMutations);

        int added = 0;
        for (int birth = 0; birth < 1000; birth++)
        {
            if (AnimalGenome.Create().TryAddHarmfulMutation(10f)) added++;
        }

        Assert.That(added, Is.InRange(70, 130));
    }

    [Test]
    public void EachHarmfulMutationAddsItsCostToEnergyUse()
    {
        AnimalGenome genome = CreateGenomeWithHarmfulMutations(5);

        Assert.AreEqual(1.1f, genome.EnergyUseMultiplier(0.02f), Tolerance);
        Assert.AreEqual(1f, genome.EnergyUseMultiplier(-1f));
    }

    [Test]
    public void ClonesKeepEveryHarmfulMutation()
    {
        AnimalGenome parent = CreateGenomeWithHarmfulMutations(4);

        Assert.AreEqual(4, parent.Clone().HarmfulMutations);
        Assert.AreEqual(4, parent.CreateMutatedCopy(100f, 0.5f, BodyPlanMutation.Default, out _).HarmfulMutations);
    }

    [Test]
    public void AChildOfTwoParentsInheritsEachHarmfulMutationHalfTheTime()
    {
        AnimalGenome first = CreateGenomeWithHarmfulMutations(10);
        AnimalGenome second = CreateGenomeWithHarmfulMutations(6);
        const int Children = 400;

        int total = 0;
        int fewerThanEitherParent = 0;
        for (int child = 0; child < Children; child++)
        {
            int inherited = AnimalGenome.Recombine(first, second).HarmfulMutations;
            Assert.That(inherited, Is.InRange(0, 16));
            total += inherited;
            if (inherited < 6) fewerThanEitherParent++;
        }

        // 8 on average. Some children carry fewer than either parent, which a clone never can.
        Assert.That((float)total / Children, Is.InRange(7.5f, 8.5f));
        Assert.Greater(fewerThanEitherParent, 0);
        Assert.AreEqual(10, first.HarmfulMutations);
        Assert.AreEqual(6, second.HarmfulMutations);
    }

    [Test]
    public void HarmfulMutationsDoNotCountTowardsGeneticDistance()
    {
        Assert.AreEqual(0f, AnimalGenome.Distance(CreateMidpointGenome(), CreateGenomeWithHarmfulMutations(20)));
    }

    [Test]
    public void ANewGenomeHasNoPerks()
    {
        AnimalGenome genome = AnimalGenome.Create();

        for (int index = 0; index < AnimalGenome.PerkCount; index++)
        {
            Assert.IsFalse(genome.HasPerk((AnimalPerk)index), ((AnimalPerk)index).ToString());
        }
    }

    [Test]
    public void SettingAPerkTurnsOnlyThatPerkOnAndOff()
    {
        AnimalGenome genome = AnimalGenome.Create();

        genome.SetPerk(AnimalPerk.Venom, true);
        Assert.IsTrue(genome.HasPerk(AnimalPerk.Venom));
        Assert.IsFalse(genome.HasPerk(AnimalPerk.Camouflage));

        genome.SetPerk(AnimalPerk.Venom, false);
        Assert.IsFalse(genome.HasPerk(AnimalPerk.Venom));
    }

    [Test]
    public void PerkFlipChanceIsAPercentagePerPerkPerBirth()
    {
        AnimalGenome genome = AnimalGenome.Create();
        Assert.IsFalse(genome.MutatePerks(0f));

        Assert.IsTrue(genome.MutatePerks(100f));
        Assert.IsTrue(genome.HasPerk(AnimalPerk.ThickFur) && genome.HasPerk(AnimalPerk.ScavengerGut));
        Assert.IsTrue(genome.MutatePerks(100f));
        Assert.IsFalse(genome.HasPerk(AnimalPerk.ThickFur) || genome.HasPerk(AnimalPerk.ScavengerGut));

        // At 1% per perk, about one perk flips in every 100 / PerkCount = 20 births.
        int flips = 0;
        for (int birth = 0; birth < 2000; birth++)
        {
            AnimalGenome child = AnimalGenome.Create();
            child.MutatePerks(1f);
            for (int index = 0; index < AnimalGenome.PerkCount; index++)
            {
                if (child.HasPerk((AnimalPerk)index)) flips++;
            }
        }

        Assert.That(flips, Is.InRange(60, 140));
    }

    [Test]
    public void ClonesKeepTheirPerks()
    {
        AnimalGenome parent = CreateMidpointGenome();
        parent.SetPerk(AnimalPerk.Camouflage, true);

        Assert.IsTrue(parent.Clone().HasPerk(AnimalPerk.Camouflage));
        Assert.IsTrue(parent.CreateMutatedCopy(100f, 0.5f, BodyPlanMutation.Default, out _)
                            .HasPerk(AnimalPerk.Camouflage));
    }

    [Test]
    public void AChildOfTwoParentsTakesEachPerkFromOneParent()
    {
        AnimalGenome first = CreateMidpointGenome();
        AnimalGenome second = CreateMidpointGenome();
        for (int index = 0; index < AnimalGenome.PerkCount; index++)
        {
            first.SetPerk((AnimalPerk)index, true);
        }

        int[] fromFirst = new int[AnimalGenome.PerkCount];
        for (int child = 0; child < 400; child++)
        {
            AnimalGenome genome = AnimalGenome.Recombine(first, second);
            for (int index = 0; index < AnimalGenome.PerkCount; index++)
            {
                if (genome.HasPerk((AnimalPerk)index)) fromFirst[index]++;
            }
        }

        foreach (int count in fromFirst)
        {
            Assert.That(count, Is.InRange(140, 260));
        }
    }

    [Test]
    public void APerkOfDifferenceCountsHalfAsMuchAsAWholeBodyPart()
    {
        AnimalGenome plain = CreateMidpointGenome();
        AnimalGenome venomous = CreateMidpointGenome();
        venomous.SetPerk(AnimalPerk.Venom, true);
        AnimalGenome withNeck = CreateMidpointGenome();
        withNeck.SetPart(BodySite.Head, BodyPartType.Neck, 1f);

        float perkDistance = AnimalGenome.Distance(plain, venomous);

        float expectedRatio = AnimalGenome.PerkDistanceWeight / AnimalGenome.BodySiteDistanceWeight;
        Assert.That(perkDistance / AnimalGenome.Distance(plain, withNeck), Is.EqualTo(expectedRatio).Within(Tolerance));
        // Under half the default species threshold (0.03), so one perk doesn't lower fertility.
        Assert.Less(perkDistance, 0.015f);
    }

    [Test]
    public void GroupingSeparatesGenomesFurtherApartThanTheThreshold()
    {
        AnimalGenome[] genomes =
        {
            CreateGenomeWithDiet(0f), CreateGenomeWithDiet(0.02f), CreateGenomeWithDiet(0.04f),
            CreateGenomeWithDiet(0.9f), CreateGenomeWithDiet(0.92f)
        };
        float threshold = AnimalGenome.Distance(CreateGenomeWithDiet(0f), CreateGenomeWithDiet(0.1f));

        var groups = AnimalGenome.GroupByDistance(genomes, threshold);

        Assert.AreEqual(2, groups.Count);
        CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, groups[0]);
        CollectionAssert.AreEquivalent(new[] { 3, 4 }, groups[1]);
    }

    [Test]
    public void GroupingChainsCloseRelativesIntoOneGroup()
    {
        // Each neighbour is within the threshold even though the two ends are far apart.
        AnimalGenome[] genomes =
        {
            CreateGenomeWithDiet(0f), CreateGenomeWithDiet(0.2f), CreateGenomeWithDiet(0.4f),
            CreateGenomeWithDiet(0.6f), CreateGenomeWithDiet(0.8f)
        };
        float threshold = 1.5f * AnimalGenome.Distance(genomes[0], genomes[1]);

        var groups = AnimalGenome.GroupByDistance(genomes, threshold);

        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(genomes.Length, groups[0].Count);
    }

    [Test]
    public void GroupingOfNoGenomesIsEmpty()
    {
        Assert.AreEqual(0, AnimalGenome.GroupByDistance(new AnimalGenome[0], 0.1f).Count);
    }

    static AnimalGenome CreateGenomeWithDiet(float dietAffinity)
    {
        AnimalGenome genome = CreateMidpointGenome();
        genome[AnimalGene.DietAffinity] = dietAffinity;
        return genome;
    }

    static AnimalGenome CreateGenomeWithHarmfulMutations(int count)
    {
        AnimalGenome genome = CreateMidpointGenome();
        for (int mutation = 0; mutation < count; mutation++)
        {
            genome.TryAddHarmfulMutation(100f);
        }

        return genome;
    }

    static AnimalGenome CreateMidpointGenome()
    {
        AnimalGenome genome = AnimalGenome.Create();
        for (int index = 0; index < AnimalGenome.GeneCount; index++)
        {
            AnimalGene gene = (AnimalGene)index;
            AnimalGeneDefinition definition = AnimalGenome.GetDefinition(gene);
            genome[gene] = (definition.minimum + definition.maximum) * 0.5f;
        }

        return genome;
    }

    static void AssertGenesEqual(AnimalGenome expected, AnimalGenome actual)
    {
        for (int index = 0; index < AnimalGenome.GeneCount; index++)
        {
            AnimalGene gene = (AnimalGene)index;
            Assert.AreEqual(expected[gene], actual[gene], gene.ToString());
        }
    }
}
