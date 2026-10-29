using Boreal.World;
using Boreal.Temperature;
using Boreal.TerrainGeneration;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Boreal.Animals.Tests
{
    // The neutral-drift calibration behind SpeciesManager.DefaultSpeciationThreshold. 250 copies of the founder
    // breed in non-overlapping generations with no selection: every child clones a random parent with the animals'
    // default mutation chances, at the mutation rate it inherits, so lineages whose rate drifts up diverge faster.
    // After each generation the animals are grouped as the species census groups them, and the first split is the
    // first generation with a separate group big enough to become a species. Adding a gene, body site or perk changes
    // the distance scale; if this test then fails, re-tune the threshold, or the distance weights in AnimalGenome,
    // until the median is back near 50 to 55 generations.
    public class SpeciesSplitCalibrationTests
    {
        const int Population = 250;
        const int Runs = 20;
        const int GenerationLimit = 150;
        // SeekFood's defaults.
        const float MutationChance = 5f;
        const float MutationMagnitude = 0.1f;
        const float PerkFlipChance = 1f;
        const float HarmfulMutationChance = 10f;
        // SpeciesManager's default minimumNewSpeciesSize.
        const int MinimumNewSpeciesSize = 3;

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
        public void TheDefaultThresholdSplitsTheFirstSpeciesAfterAboutFiftyToSixtyGenerationsOfDrift()
        {
            float threshold = SpeciesManager.DefaultSpeciationThreshold;
            List<int> firstSplits = new List<int>();
            for (int run = 0; run < Runs; run++)
            {
                firstSplits.Add(GenerationOfFirstSplit(threshold));
            }

            firstSplits.Sort();
            float median = (firstSplits[Runs / 2 - 1] + firstSplits[Runs / 2]) / 2f;
            string summary = $"median {median} generations (runs: {string.Join(", ", firstSplits)})";
            Debug.Log($"Neutral drift at species threshold {threshold}: first split after a {summary}.");
            // Single runs vary from about 20 to 100 generations, so the band is wide: with the target met, the median
            // of 20 runs stays between about 45 and 60.
            Assert.That(median, Is.InRange(40f, 75f), summary);
        }

        // The first generation with a separate group large enough to become a species, or GenerationLimit + 1.
        static int GenerationOfFirstSplit(float threshold)
        {
            AnimalGenome founder = CreateFounder();
            List<AnimalGenome> population = new List<AnimalGenome>(Population);
            for (int index = 0; index < Population; index++)
            {
                population.Add(founder.Clone());
            }

            List<AnimalGenome> children = new List<AnimalGenome>(Population);
            for (int generation = 1; generation <= GenerationLimit; generation++)
            {
                children.Clear();
                for (int index = 0; index < Population; index++)
                {
                    AnimalGenome parent = population[Random.Range(0, Population)];
                    children.Add(parent.CreateChild(MutationChance, MutationMagnitude, BodyPlanMutation.Default,
                                                    PerkFlipChance, HarmfulMutationChance, out _));
                }

                (population, children) = (children, population);
                List<List<int>> groups = AnimalGenome.GroupByDistance(population, threshold);
                if (groups.Count > 1 && groups[1].Count >= MinimumNewSpeciesSize)
                {
                    return generation;
                }
            }

            return GenerationLimit + 1;
        }

        // The founder AnimalTerrainDemoBootstrap places: a legless capsule with no perks.
        static AnimalGenome CreateFounder()
        {
            AnimalGenome founder = AnimalGenome.Create();
            founder[AnimalGene.MoveSpeed] = 6f;
            founder[AnimalGene.Strength] = 25f;
            founder[AnimalGene.BodyBulk] = 1f;
            founder[AnimalGene.BodyHeight] = 1f;
            founder[AnimalGene.VisionRadius] = 110f;
            founder[AnimalGene.MaxEnergy] = 150f;
            founder[AnimalGene.MaxStamina] = 100f;
            founder[AnimalGene.MaxHealth] = 100f;
            founder[AnimalGene.MaturityTime] = 45f;
            founder[AnimalGene.MaxLifespan] = 300f;
            founder[AnimalGene.DietAffinity] = 0.15f;
            founder[AnimalGene.PreferredTemperature] = 2f;
            founder[AnimalGene.ColdTolerance] = 18f;
            founder[AnimalGene.HeatTolerance] = 16f;
            founder[AnimalGene.SexualDrive] = 0.5f;
            founder[AnimalGene.BreedingThreshold] = 0.8f;
            founder[AnimalGene.MutationRate] = 1f;
            return founder;
        }
    }
}
