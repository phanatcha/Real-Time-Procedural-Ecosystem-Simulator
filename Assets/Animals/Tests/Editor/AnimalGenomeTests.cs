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
    public void DistanceIsOneBetweenOppositeEndsOfEveryGene()
    {
        AnimalGenome minimum = AnimalGenome.Create();
        AnimalGenome maximum = AnimalGenome.Create();
        for (int index = 0; index < AnimalGenome.GeneCount; index++)
        {
            AnimalGene gene = (AnimalGene)index;
            maximum[gene] = AnimalGenome.GetDefinition(gene).maximum;
        }

        Assert.That(AnimalGenome.Distance(minimum, maximum), Is.EqualTo(1f).Within(Tolerance));
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
