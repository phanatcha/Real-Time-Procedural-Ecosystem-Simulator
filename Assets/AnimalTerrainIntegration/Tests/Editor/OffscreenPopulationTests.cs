using Boreal.TerrainGeneration;
using Boreal.Animals;
using NUnit.Framework;
using UnityEngine;

namespace Boreal.AnimalTerrainIntegration.Tests
{
    public class OffscreenPopulationTests
    {
        const float CellSize = 250f;
        const float PlantEnergy = 60f;
        const float Mild = 2f;

        static readonly OffscreenEcologyRules Rules = OffscreenEcologyRules.Defaults;

        // The founders' genes, set by AnimalTerrainDemoBootstrap.
        static AnimalGenome FounderGenome(float diet = 0.15f)
        {
            AnimalGenome genome = AnimalGenome.Create();
            genome[AnimalGene.MoveSpeed] = 6f;
            genome[AnimalGene.Strength] = 25f;
            genome[AnimalGene.BodyBulk] = 1f;
            genome[AnimalGene.BodyHeight] = 1f;
            genome[AnimalGene.VisionRadius] = 110f;
            genome[AnimalGene.MaxEnergy] = 150f;
            genome[AnimalGene.MaxStamina] = 100f;
            genome[AnimalGene.MaxHealth] = 100f;
            genome[AnimalGene.MaturityTime] = 45f;
            genome[AnimalGene.MaxLifespan] = 300f;
            genome[AnimalGene.DietAffinity] = diet;
            genome[AnimalGene.PreferredTemperature] = 2f;
            genome[AnimalGene.ColdTolerance] = 18f;
            genome[AnimalGene.HeatTolerance] = 16f;
            return genome;
        }

        static OffscreenSample Founder(float energyUse = 1f, float diet = 0.15f)
        {
            return new OffscreenSample(FounderGenome(diet), energyUse, 0);
        }

        static OffscreenPopulationStore CreateStore(float plantEnergy = PlantEnergy)
        {
            OffscreenPopulationStore store = new OffscreenPopulationStore(CellSize, Rules, new System.Random(7));
            store.AddCell(Vector2Int.zero, plantEnergy, Mild);
            store.AddCell(Vector2Int.right, plantEnergy, Mild);
            return store;
        }

        static OffscreenCell Cell(OffscreenPopulationStore store, Vector2Int coordinate)
        {
            Assert.IsTrue(store.TryGetCell(coordinate, out OffscreenCell cell));
            return cell;
        }

        static OffscreenPopulation AddAnimals(OffscreenPopulationStore store, OffscreenCell cell, string species, int count,
            float energyUse = 1f, float diet = 0.15f)
        {
            for (int i = 0; i < count; i++) store.AddAnimal(cell, species, Color.white, Founder(energyUse, diet));
            return cell.Find(species);
        }

        [Test]
        public void PlantEnergyFollowsTheFoodSpawnerRules()
        {
            // One 16 m site, half the time holding a plant worth 75 × 1.0 at medium moisture, regrowing in 90 s.
            Assert.That(Rules.PlantEnergyPerSecond(256f, 0.5f, 0.5f, 1f), Is.EqualTo(0.5f * 75f / 90f).Within(1e-5f));
            Assert.That(Rules.PlantEnergyPerSecond(256f, 0.5f, 0.5f, 0.5f), Is.EqualTo(0.5f * 75f * 0.5f / 90f).Within(1e-5f));
            Assert.AreEqual(0f, Rules.PlantEnergyPerSecond(256f, Rules.minimumGrassBiomass * 0.5f, 0.5f, 1f));
            Assert.AreEqual(0f, Rules.PlantEnergyPerSecond(256f, 0.5f, 0.5f, Rules.minimumPlantGrowthRate * 0.5f));
        }

        [Test]
        public void DigestionAndTemperatureMatchTheLiveAnimals()
        {
            // Inactive, so the NavMeshAgent SeekFood requires never tries to join a NavMesh.
            GameObject animal = new GameObject("Offscreen rules reference");
            animal.SetActive(false);
            try
            {
                SeekFood behaviour = animal.AddComponent<SeekFood>();
                AnimalTemperature thermal = animal.AddComponent<AnimalTemperature>();
                thermal.preferredTemperature = 2f;
                thermal.coldTolerance = 18f;
                thermal.heatTolerance = 16f;

                for (float diet = 0f; diet <= 1f; diet += 0.1f)
                {
                    behaviour.dietAffinity = diet;
                    Assert.That(Rules.PlantDigestion(diet), Is.EqualTo(behaviour.GetDigestionEfficiency(FoodType.Plant)).Within(1e-6f));
                }

                for (float celsius = -80f; celsius <= 80f; celsius += 2.5f)
                {
                    Assert.That(Rules.ThermalEnergyMultiplier(-16f, 18f, celsius),
                        Is.EqualTo(thermal.EvaluateAtTemperature(celsius).energyMultiplier).Within(1e-5f), $"At {celsius} °C.");
                }

                thermal.torpor = 0.7f;
                for (float celsius = -80f; celsius <= 80f; celsius += 2.5f)
                {
                    Assert.That(Rules.ThermalEnergyMultiplier(-16f, 18f, celsius, 0.7f),
                        Is.EqualTo(thermal.EvaluateAtTemperature(celsius).energyMultiplier).Within(1e-5f),
                        $"With torpor at {celsius} °C.");
                }
            }
            finally
            {
                Object.DestroyImmediate(animal);
            }
        }

        [Test]
        public void CapacityIsDigestedFoodOverEnergyUse()
        {
            OffscreenSample founder = Founder();
            float expected = Rules.harvestEfficiency * PlantEnergy * Rules.PlantDigestion(0.15f) / 1f;

            Assert.That(Rules.CarryingCapacity(founder, PlantEnergy, Mild), Is.EqualTo(expected).Within(1e-4f));
            Assert.That(Rules.CarryingCapacity(Founder(energyUse: 2f), PlantEnergy, Mild), Is.EqualTo(expected / 2f).Within(1e-4f));
            Assert.That(Rules.CarryingCapacity(founder, PlantEnergy * 2f, Mild), Is.EqualTo(expected * 2f).Within(1e-4f));
            Assert.Less(Rules.CarryingCapacity(Founder(diet: 0.9f), PlantEnergy, Mild), expected);
            // 40 °C below the comfort range: two stress units, so double energy use.
            Assert.That(Rules.CarryingCapacity(founder, PlantEnergy, -56f), Is.EqualTo(expected / 2f).Within(1e-4f));
        }

        [Test]
        public void TorporSavesEnergyOnlyInTheCold()
        {
            // Comfortable: torpor costs and saves nothing.
            Assert.AreEqual(1f, Rules.ThermalEnergyMultiplier(-16f, 18f, 0f, 1f), 1e-5f);
            // 12 °C below the comfort range: 0.6 stress units, so 1.3 times the energy without torpor and half that with it.
            Assert.AreEqual(1.3f, Rules.ThermalEnergyMultiplier(-16f, 18f, -28f), 1e-5f);
            Assert.AreEqual(0.65f, Rules.ThermalEnergyMultiplier(-16f, 18f, -28f, 1f), 1e-5f);
            // 5 °C below it, torpor is half on.
            Assert.AreEqual(1.125f * 0.75f, Rules.ThermalEnergyMultiplier(-16f, 18f, -21f, 1f), 1e-5f);
        }

        [Test]
        public void FounderGrowthAndDeathRatesComeFromTheirGenes()
        {
            Assert.That(Rules.GrowthRate(Founder()), Is.EqualTo((1f - 45f / 300f) / 60f - 1f / 300f).Within(1e-6f));
            Assert.That(Rules.DeathRate(Founder(), Mild), Is.EqualTo(1f / 300f + 1f / 150f).Within(1e-6f));
        }

        [Test]
        public void AnimalsJoinAndLeaveTheirCellsPopulation()
        {
            OffscreenPopulationStore store = CreateStore();
            OffscreenCell cell = Cell(store, Vector2Int.zero);
            OffscreenPopulation population = AddAnimals(store, cell, "A", 50);

            Assert.AreEqual(50f, population.count, 1e-4f);
            Assert.AreEqual(store.samplesPerPopulation, population.samples.Count);
            Assert.AreEqual(50f, store.CountSpecies("A"), 1e-4f);

            Assert.IsTrue(store.TryTakeAnimal(cell, population, out OffscreenSample sample));
            Assert.IsNotNull(sample);
            Assert.AreEqual(49f, population.count, 1e-4f);
        }

        [Test]
        public void TakingTheLastAnimalEmptiesTheCell()
        {
            OffscreenPopulationStore store = CreateStore();
            OffscreenCell cell = Cell(store, Vector2Int.zero);
            OffscreenPopulation population = AddAnimals(store, cell, "A", 1);

            Assert.IsTrue(store.TryTakeAnimal(cell, population, out _));
            Assert.IsNull(cell.Find("A"));
            Assert.IsFalse(store.TryTakeAnimal(cell, population, out _));
        }

        [Test]
        public void PopulationGrowsToWhatTheCellCanFeed()
        {
            OffscreenPopulationStore store = CreateStore();
            store.migrationRatePerSecond = 0f;
            OffscreenCell cell = Cell(store, Vector2Int.zero);
            OffscreenPopulation population = AddAnimals(store, cell, "A", 2);
            float capacity = store.Capacity(cell, population);

            store.UpdateCell(cell, 5000f);

            Assert.That(population.count, Is.EqualTo(capacity).Within(0.01f));
        }

        [Test]
        public void SpeciesInOneCellShareItsFood()
        {
            OffscreenPopulationStore store = CreateStore();
            store.migrationRatePerSecond = 0f;
            OffscreenCell cell = Cell(store, Vector2Int.zero);
            OffscreenPopulation first = AddAnimals(store, cell, "A", 2);
            OffscreenPopulation second = AddAnimals(store, cell, "B", 2, energyUse: 2f);

            for (int step = 0; step < 200; step++) store.UpdateCell(cell, 60f);

            // Each animal eats 1/K of the food, so the food is fully used and not more.
            float shareEaten = first.count / store.Capacity(cell, first) + second.count / store.Capacity(cell, second);
            Assert.That(shareEaten, Is.EqualTo(1f).Within(0.01f));
            Assert.Greater(first.count, 0f);
            Assert.Greater(second.count, 0f);
        }

        [Test]
        public void LiveAnimalsInTheCellEatPartOfItsFood()
        {
            OffscreenPopulationStore store = CreateStore();
            store.migrationRatePerSecond = 0f;
            OffscreenCell cell = Cell(store, Vector2Int.zero);
            OffscreenPopulation population = AddAnimals(store, cell, "A", 2);
            float capacity = store.Capacity(cell, population);
            int live = Mathf.RoundToInt(capacity * 0.25f);
            store.LiveAnimals = coordinate => coordinate == Vector2Int.zero ? live : 0;

            store.UpdateCell(cell, 5000f);

            Assert.That(population.count, Is.EqualTo(capacity - live).Within(0.05f));
        }

        [Test]
        public void WithoutFoodAPopulationDiesOut()
        {
            OffscreenPopulationStore store = CreateStore(plantEnergy: 0f);
            store.migrationRatePerSecond = 0f;
            OffscreenCell cell = Cell(store, Vector2Int.zero);
            OffscreenPopulation population = AddAnimals(store, cell, "A", 20);
            float deathRate = Rules.DeathRate(Founder(), Mild);

            store.UpdateCell(cell, 60f);

            Assert.That(population.count, Is.EqualTo(20f * Mathf.Exp(-deathRate * 60f)).Within(0.01f));
        }

        [Test]
        public void MigrationMovesAnimalsAndGenomesWithoutLosingAny()
        {
            OffscreenPopulationStore store = CreateStore();
            store.migrationRatePerSecond = 0.05f;
            OffscreenCell source = Cell(store, Vector2Int.zero);
            OffscreenCell destination = Cell(store, Vector2Int.right);
            OffscreenPopulation population = AddAnimals(store, source, "A", 1);
            // At its capacity the population neither grows nor shrinks, so only migration changes it.
            float capacity = store.Capacity(source, population);
            population.count = capacity;

            store.UpdateCell(source, 10f);
            store.ApplyTransfers();

            OffscreenPopulation arrived = destination.Find("A");
            Assert.IsNotNull(arrived);
            Assert.Greater(arrived.count, 0f);
            Assert.Greater(arrived.samples.Count, 0);
            Assert.That(population.count + arrived.count, Is.EqualTo(capacity).Within(0.001f));
        }

        [Test]
        public void AdvanceUpdatesNearbyCellsMoreOften()
        {
            OffscreenPopulationStore store = CreateStore();
            store.migrationRatePerSecond = 0f;
            store.nearbyRadius = 10f;
            OffscreenCell nearby = Cell(store, Vector2Int.zero);
            OffscreenCell distant = Cell(store, Vector2Int.right);
            OffscreenPopulation near = AddAnimals(store, nearby, "A", 2);
            OffscreenPopulation far = AddAnimals(store, distant, "A", 2);

            store.Advance(1f, nearby.Centre);

            Assert.Greater(near.count, 2f);
            Assert.AreEqual(2f, far.count, 1e-5f);
        }

        [Test]
        public void RemovingASpeciesClearsEveryCell()
        {
            OffscreenPopulationStore store = CreateStore();
            AddAnimals(store, Cell(store, Vector2Int.zero), "A", 3);
            AddAnimals(store, Cell(store, Vector2Int.right), "A", 3);
            AddAnimals(store, Cell(store, Vector2Int.right), "B", 2);

            store.RemoveSpecies("A");

            Assert.AreEqual(0f, store.CountSpecies("A"));
            Assert.AreEqual(2f, store.CountSpecies("B"), 1e-4f);
            Assert.IsFalse(store.CountBySpecies().ContainsKey("A"));
        }

        // A cell surveyed as one spot of flat, fairly cold grassland (temperature index 0.3, about -14 °C).
        static OffscreenPopulationStore CreateSurveyedStore(out OffscreenCell cell)
        {
            EnvironmentSample grassland = new EnvironmentSample
            {
                isValid = true, isLand = true, position = new Vector3(40f, 30f, -25f), temperature = 0.3f,
                moisture = 0.5f, slope = 0f, grassBiomass = 0.6f
            };
            OffscreenPopulationStore store = new OffscreenPopulationStore(CellSize, Rules, new System.Random(7));
            store.AddCell(Vector2Int.zero, PlantEnergy, Rules.ToCelsius(0.3f),
                          new[] { new OffscreenGround(grassland, PlantEnergy) });
            cell = Cell(store, Vector2Int.zero);
            return store;
        }

        [Test]
        public void WinterLeavesACellLessFoodAndColdWeather()
        {
            OffscreenPopulationStore store = CreateSurveyedStore(out OffscreenCell cell);
            OffscreenPopulation population = AddAnimals(store, cell, "A", 10);
            float baseCapacity = store.Capacity(cell, population);
            Assert.AreEqual(PlantEnergy, cell.PlantEnergyPerSecond, 1e-4f);

            store.season = new SeasonConditions { state = SeasonState.For(BorealSeason.Winter), growthBalance = 1f };
            store.seasonStep = 1;
            float winterCapacity = store.Capacity(cell, population);

            // Growth at about 5%, and most of the grass under snow.
            Assert.Less(cell.PlantEnergyPerSecond, PlantEnergy * 0.05f * 0.3f);
            // The coldest the terrain gets.
            Assert.AreEqual(Rules.coldestCelsius, cell.Celsius, 1e-3f);
            Assert.Less(winterCapacity, baseCapacity * 0.02f);

            // Back to the base climate once seasons are off.
            store.season = SeasonConditions.None;
            store.seasonStep = -1;
            Assert.AreEqual(baseCapacity, store.Capacity(cell, population), 1e-3f);
            Assert.AreEqual(cell.baseCelsius, cell.Celsius, 1e-3f);
        }

        [Test]
        public void BalancedGrowthMakesSummerABoom()
        {
            OffscreenPopulationStore store = CreateSurveyedStore(out OffscreenCell cell);
            OffscreenPopulation population = AddAnimals(store, cell, "A", 10);

            store.season = new SeasonConditions
            {
                state = SeasonState.For(BorealSeason.Summer), growthBalance = SeasonClock.YearlyGrowthBalance(1f)
            };
            store.seasonStep = 1;
            store.Capacity(cell, population);

            // Summer's full growth, a little less on drier ground, times about 1.9.
            Assert.That(cell.PlantEnergyPerSecond, Is.InRange(PlantEnergy * 1.75f, PlantEnergy * 1.9f));
        }

        [Test]
        public void LimitedBurialLeavesWinterFoodToFind()
        {
            OffscreenPopulationStore store = CreateSurveyedStore(out OffscreenCell cell);
            OffscreenPopulation population = AddAnimals(store, cell, "A", 10);
            SeasonConditions winter = new SeasonConditions { state = SeasonState.For(BorealSeason.Winter), growthBalance = 1f };

            store.season = winter;
            store.seasonStep = 1;
            store.Capacity(cell, population);
            float fullyBuried = cell.PlantEnergyPerSecond;

            winter.leastUncovered = 0.5f;
            store.season = winter;
            store.seasonStep = 2;
            store.Capacity(cell, population);

            // Half the grass stays uncovered instead of 7 to 27%.
            Assert.Greater(cell.PlantEnergyPerSecond, fullyBuried * 1.8f);
        }

        [Test]
        public void SeasonsLeaveCellsWithoutSurveyedGroundAlone()
        {
            OffscreenPopulationStore store = CreateStore();
            OffscreenCell cell = Cell(store, Vector2Int.zero);
            OffscreenPopulation population = AddAnimals(store, cell, "A", 10);
            float baseCapacity = store.Capacity(cell, population);

            store.season = new SeasonConditions { state = SeasonState.For(BorealSeason.Winter), growthBalance = 1f };
            store.seasonStep = 1;

            Assert.AreEqual(baseCapacity, store.Capacity(cell, population), 1e-4f);
            Assert.AreEqual(Mild, cell.Celsius);
        }
    }
}
