
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace Boreal.Animals.Tests
{
    // Animals behaving in a running scene (item 40). These are Editor-assembly Edit Mode
    // tests that enter Play Mode themselves: each opens an empty scene, enters Play Mode, then builds flat ground with
    // a NavMesh, a Species Manager, animals and food, and watches them in simulated time (run at 5x). Entering Play
    // Mode reloads scripts, which clears every local variable set before it, so each test builds its scene afterwards.
    // The Test Runner saves and restores the scenes that were open, and leaves Play Mode when a test ends.
    //
    // Not covered: plant regrowth (FoodSpawner), navigation streaming (HabitatNavigation) and swimming all need
    // generated terrain with water.
    public class AnimalBehaviourPlayModeTests
    {
        const string Species = "Test";
        const float SimulationSpeed = 5f;

        [UnityTest]
        public IEnumerator HungryAnimalsWalkToPlantsAndEatThem()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            SeekFood animal = CreateAnimal(new Vector3(-10f, 0f, 0f), Genome(), energyFraction: 0.3f);
            FoodItem plant = CreatePlant(new Vector3(10f, 0f, 0f));
            yield return null;
            float energyBefore = animal.currentEnergy;

            float deadline = Time.time + 30f;
            while (plant != null && Time.time < deadline) yield return null;

            Assert.IsTrue(plant == null, "The animal did not eat the plant within 30 simulated seconds.");
            Assert.Greater(animal.currentEnergy, energyBefore);
        }

        // The plant stands on a second patch of ground across a 45 m gap, so no route reaches it.
        [UnityTest]
        public IEnumerator AnimalsGiveUpOnFoodTheyCannotReach()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            // Allocate the log callback's captured state after the domain reload. A display class created
            // in the outer iterator before EnterPlayMode is not restored by the test runner.
            yield return ObserveUnreachableFood();
        }

        static IEnumerator ObserveUnreachableFood()
        {
            BakeNavMesh(CreateGround(Vector3.zero, 40f), CreateGround(new Vector3(80f, 0f, 0f), 30f));
            SpeciesManager manager = StartSimulation();
            manager.logLifecycleEvents = true;
            bool gaveUp = false;
            Application.LogCallback watch = (message, stackTrace, type) =>
            {
                if (message.Contains("gave up on Test Plant")) gaveUp = true;
            };
            Application.logMessageReceived += watch;
            try
            {
                CreateAnimal(new Vector3(-10f, 0f, 0f), Genome(), energyFraction: 0.3f);
                FoodItem plant = CreatePlant(new Vector3(80f, 0f, 0f));

                float deadline = Time.time + 90f;
                while (!gaveUp && Time.time < deadline) yield return null;

                Assert.IsTrue(gaveUp, "The animal did not give up on the plant within 90 simulated seconds.");
                Assert.IsTrue(plant != null && plant.IsAvailable, "The plant should still be there.");
            }
            finally
            {
                Application.logMessageReceived -= watch;
            }
        }

        // A sexual drive of 0 means the animal only ever clones itself.
        [UnityTest]
        public IEnumerator WellFedAdultsWithNoSexualDriveCloneThemselves()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            CreateAnimal(Vector3.zero, Genome(sexualDrive: 0f, maturityTime: 5f), energyFraction: 1f, age: 10f);

            float deadline = Time.time + 20f;
            while (AnimalCount() < 2 && Time.time < deadline) yield return null;

            Assert.GreaterOrEqual(AnimalCount(), 2, "No clone was born within 20 simulated seconds.");
            SpeciesTelemetryRecord record = Telemetry();
            Assert.GreaterOrEqual(record.reproductionEvents, 1);
            Assert.AreEqual(0, record.sexualOffspring);
        }

        // A sexual drive of 1 means the animals only ever mate. Identical genomes are fully fertile together.
        [UnityTest]
        public IEnumerator PairsCourtAndHaveChildrenWithTwoParents()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            CreateAnimal(Vector3.zero, Genome(sexualDrive: 1f, maturityTime: 5f), energyFraction: 1f, age: 10f);
            CreateAnimal(new Vector3(3f, 0f, 0f), Genome(sexualDrive: 1f, maturityTime: 5f), energyFraction: 1f, age: 10f);

            float deadline = Time.time + 60f;
            while (AnimalCount() < 3 && Time.time < deadline) yield return null;

            Assert.GreaterOrEqual(AnimalCount(), 3, "The pair had no child within 60 simulated seconds.");
            SpeciesTelemetryRecord record = Telemetry();
            Assert.GreaterOrEqual(record.sexualOffspring, 1);
            Assert.AreEqual(record.reproductionEvents, record.sexualOffspring, "Animals that only mate should never clone.");
        }

        [UnityTest]
        public IEnumerator BitingAVenomousAnimalHurtsTheBiter()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            SeekFood venomous = CreateAnimal(Vector3.zero, Genome(perk: AnimalPerk.Venom), energyFraction: 0.8f);
            SeekFood plain = CreateAnimal(new Vector3(0f, 0f, 10f), Genome(), energyFraction: 0.8f);
            SeekFood biter = CreateAnimal(new Vector3(10f, 0f, 0f), Genome(), energyFraction: 0.8f);
            yield return null;
            yield return null;

            float healthBefore = biter.currentHealth;
            plain.TakeDamage(20f, biter);
            Assert.AreEqual(healthBefore, biter.currentHealth, 0.001f, "Biting an animal without venom should not hurt.");

            venomous.TakeDamage(20f, biter);
            Assert.AreEqual(healthBefore - 20f * AnimalPerks.VenomDamageReturned, biter.currentHealth, 0.001f);

            biter.currentHealth = 1f;
            venomous.TakeDamage(20f, biter);
            yield return null;
            Assert.IsTrue(biter == null, "A biter with no health left should die of the venom.");
            Assert.GreaterOrEqual(Telemetry().venomDeaths, 1);
        }

        [UnityTest]
        public IEnumerator RegenerationHealsWoundsOverTime()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            SeekFood healer = CreateAnimal(Vector3.zero, Genome(perk: AnimalPerk.Regeneration), energyFraction: 0.8f);
            SeekFood plain = CreateAnimal(new Vector3(10f, 0f, 0f), Genome(), energyFraction: 0.8f);
            yield return null;
            yield return null;

            healer.currentHealth = 0.5f * healer.maxHealth;
            plain.currentHealth = 0.5f * plain.maxHealth;
            float start = Time.time;
            while (Time.time < start + 4f) yield return null;
            float elapsed = Time.time - start;

            float expected = 0.5f * healer.maxHealth + AnimalPerks.RegenerationPerSecond * healer.maxHealth * elapsed;
            Assert.AreEqual(expected, healer.currentHealth, 0.05f * healer.maxHealth);
            Assert.AreEqual(0.5f * plain.maxHealth, plain.currentHealth, 0.001f, "Without the perk nothing heals.");
        }

        [UnityTest]
        public IEnumerator OnlyScavengersEatSpoiledCarcasses()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 80f));
            StartSimulation();
            FoodItem carcass = FoodItem.CreateCarcass(new Vector3(10f, 0f, 0f), 100f, "Other", lifetime: 0.2f,
                                                      scale: 2f, spoiledLifetime: 300f);
            float deadline = Time.time + 5f;
            while (!carcass.IsSpoiled && Time.time < deadline) yield return null;
            Assert.IsTrue(carcass.IsSpoiled, "The carcass did not go off.");

            SeekFood scavenger = CreateAnimal(new Vector3(-10f, 0f, 0f), Genome(diet: 1f, perk: AnimalPerk.ScavengerGut),
                                              energyFraction: 0.3f);
            SeekFood plain = CreateAnimal(new Vector3(30f, 0f, 20f), Genome(diet: 1f), energyFraction: 0.3f);
            yield return null;
            Assert.IsTrue(scavenger.CanEat(carcass));
            Assert.IsFalse(plain.CanEat(carcass));
            float energyBefore = scavenger.currentEnergy;

            deadline = Time.time + 30f;
            while (carcass != null && Time.time < deadline) yield return null;

            Assert.IsTrue(carcass == null, "The scavenger did not eat the spoiled carcass within 30 simulated seconds.");
            Assert.Greater(scavenger.currentEnergy, energyBefore);
        }

        [UnityTest]
        public IEnumerator BodyPartsAreBuiltFromTheGenome()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            AnimalGenome legged = Genome();
            legged.SetPart(BodySite.FrontPair, BodyPartType.Legs, 1f);
            legged.SetPart(BodySite.RearPair, BodyPartType.Legs, 1f);
            SeekFood walker = CreateAnimal(Vector3.zero, legged, energyFraction: 0.8f);
            SeekFood capsule = CreateAnimal(new Vector3(10f, 0f, 0f), Genome(), energyFraction: 0.8f);
            yield return null;
            yield return null;

            Mesh parts = PartsMesh(walker);
            Assert.IsNotNull(parts, "An animal with legs should have a body-parts mesh.");
            Assert.Greater(parts.vertexCount, 0);
            Assert.IsNull(PartsMesh(capsule), "A plain capsule has no body parts.");
            Assert.Greater(walker.moveSpeed, capsule.moveSpeed, "Legs should make the animal faster.");
        }

        [UnityTest]
        public IEnumerator CeilingWaitsAndRecordedCountsMatchLiveAnimalsAtNormalAndFastSpeed()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);
            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            SpeciesManager manager = StartSimulation();
            manager.enableSafetyPause = false;
            manager.populationCeiling = 1;
            SeekFood animal = CreateAnimal(Vector3.zero, Genome(sexualDrive: 0f, maturityTime: 5f), 1f, 10f);
            var recorder = manager.gameObject.AddComponent<SimulationRunRecorder>();
            recorder.sourceRevision = "test";
            recorder.sampleInterval = 0.1f;
            yield return null;
            yield return null;
            string directory = recorder.OutputDirectory;
            try
            {
                foreach (float speed in new[] { 1f, 50f })
                {
                    manager.simulationSpeed = speed;
                    float started = Time.time;
                    float deadline = Time.realtimeSinceStartup + 10f;
                    while (Time.time < started + 1f && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.GreaterOrEqual(Time.time - started, 1f);
                }
                Assert.AreEqual(1, manager.TotalPopulation);
                Assert.AreEqual(0, Telemetry().reproductionEvents);
                Assert.AreEqual(1, manager.BreedingWaitEpisodes);
                Assert.Greater(manager.BreedingWaitSeconds, 1.5);
                recorder.enabled = false;
                string[] rows = System.IO.File.ReadAllLines(System.IO.Path.Combine(directory, "population.csv"));
                string[] columns = rows[0].Split(',');
                string[] final = rows[rows.Length - 1].Split(',');
                Assert.AreEqual(manager.TotalPopulation.ToString(), final[System.Array.IndexOf(columns, "live")]);
                Assert.AreEqual(manager.BreedingWaitEpisodes.ToString(), final[System.Array.IndexOf(columns, "breeding_wait_episodes")]);
                Assert.AreEqual(manager.BreedingWaitSeconds, double.Parse(final[System.Array.IndexOf(columns, "breeding_wait_seconds")],
                    System.Globalization.CultureInfo.InvariantCulture), 0.0001);
                manager.ResetSimulation();
                Assert.AreEqual(0, manager.BreedingWaitEpisodes);
                Assert.AreEqual(0, manager.BreedingWaitSeconds);
            }
            finally
            {
                recorder.enabled = false;
                if (directory != null && System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
            }
        }

        // Juvenile growth (item 47): a newborn starts at a fraction of its parent's size, speed, vision and reach, pays
        // energy as it grows, and is full-grown once mature.
        [UnityTest]
        public IEnumerator NewbornsStartSmallAndGrowToFullSizeByMaturity()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            SeekFood parent = CreateAnimal(Vector3.zero, Genome(sexualDrive: 0f, maturityTime: 5f), energyFraction: 1f, age: 10f);

            float deadline = Time.time + 20f;
            while (AnimalCount() < 2 && Time.time < deadline) yield return null;
            yield return null;
            SeekFood child = Child(parent);
            Assert.IsNotNull(child, "No clone was born within 20 simulated seconds.");

            float fraction = child.newbornSizeFraction;
            Assert.That(child.Growth, Is.EqualTo(fraction).Within(0.05f), "A newborn starts at the newborn size fraction.");
            // A frame or two of growth has happened by now, so compare with the growth the child has, not the fraction.
            float grown = child.Growth;
            Assert.That(child.transform.localScale.x, Is.EqualTo(parent.transform.localScale.x * grown).Within(0.05f));
            Assert.That(child.FeedingReach, Is.EqualTo(parent.FeedingReach * grown).Within(0.3f));
            Assert.That(child.visionRadius, Is.EqualTo(parent.visionRadius * grown).Within(1f));
            Assert.Less(child.GetComponent<NavMeshAgent>().speed, parent.GetComponent<NavMeshAgent>().speed * 0.6f,
                        "A newborn should be much slower than its parent.");
            float energyAtBirth = child.currentEnergy;

            deadline = Time.time + 10f;
            while (!child.IsMature && Time.time < deadline) yield return null;
            yield return null;
            Assert.IsTrue(child.IsMature, "The child did not mature within 10 simulated seconds.");
            Assert.That(child.Growth, Is.EqualTo(1f).Within(0.001f));
            Assert.That(child.transform.localScale.x, Is.EqualTo(parent.transform.localScale.x).Within(0.01f));
            Assert.That(child.visionRadius, Is.EqualTo(parent.visionRadius).Within(0.5f));
            // Growing cost more than upkeep alone: the adult body's meat times Growth Energy Cost, less the newborn mass.
            float upkeep = child.CurrentEnergyDrainPerSecond * child.maturityTime;
            float growthCost = child.growthEnergyCost * child.carcassBodyEnergy * child.AdultBodyMassFactor *
                               (1f - fraction * fraction * fraction);
            Assert.Greater(energyAtBirth - child.currentEnergy, upkeep + 0.5f * growthCost,
                           "Growing up should have cost energy on top of upkeep.");
        }

        // Litter size (item 16): a gene of exactly 3 gives three young at every birth, sharing the energy one child
        // would have had.
        [UnityTest]
        public IEnumerator LittersShareTheBirthEnergyBetweenTheYoung()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            AnimalGenome genome = Genome(sexualDrive: 0f, maturityTime: 5f);
            genome[AnimalGene.LitterSize] = 3f;
            SeekFood parent = CreateAnimal(Vector3.zero, genome, energyFraction: 1f, age: 10f);

            float deadline = Time.time + 20f;
            while (AnimalCount() < 2 && Time.time < deadline) yield return null;
            yield return null;
            SeekFood[] animals = Object.FindObjectsByType<SeekFood>(FindObjectsSortMode.None);
            Assert.AreEqual(4, animals.Length, "A litter of three should be born together.");
            Assert.AreEqual(3, Telemetry().reproductionEvents, "Each child counts as a birth.");

            float total = 0f;
            float first = -1f;
            foreach (SeekFood animal in animals)
            {
                if (animal == parent) continue;
                Assert.AreEqual(3f, animal.litterSize, "The young inherit the litter size gene.");
                if (first < 0f) first = animal.currentEnergy;
                Assert.That(animal.currentEnergy, Is.EqualTo(first).Within(0.5f), "The young share the energy equally.");
                total += animal.currentEnergy;
            }
            // The parent kept half of what it had; the litter got the other half between them.
            Assert.That(total, Is.EqualTo(parent.currentEnergy).Within(4f));
        }

        // Thirst (item 27): a thirsty animal walks to fresh water and drinks until it is nearly full.
        [UnityTest]
        public IEnumerator ThirstyAnimalsWalkToFreshWaterAndDrink()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            // The test scene has no terrain, so TestWater stands in for a lake bank 20 m east. A static method, not a
            // lambda: a closure made before EnterPlayMode is lost in the domain reload.
            SeekFood.DrinkingWaterLocator = LocateTestWater;
            Vector3 water = TestWater;
            SeekFood animal = CreateAnimal(Vector3.zero, Genome(), energyFraction: 1f, age: 10f);
            animal.currentHydration = 10f;

            float deadline = Time.time + 30f;
            while (!animal.IsDrinking && Time.time < deadline) yield return null;
            Assert.IsTrue(animal.IsDrinking, "The thirsty animal did not reach the water within 30 simulated seconds.");
            Assert.Less(Vector3.Distance(animal.transform.position, water), animal.CurrentFoodInteractionRange + 1f);

            deadline = Time.time + 15f;
            while (animal.IsDrinking && Time.time < deadline) yield return null;
            Assert.GreaterOrEqual(animal.currentHydration, 0.9f * animal.maxHydration, "Drinking should refill hydration.");
            Assert.IsFalse(animal.IsThirsty);
        }

        // With no water anywhere, an animal with no hydration left dies of thirst, and the death is counted.
        [UnityTest]
        public IEnumerator AnimalsWithNoWaterDieOfThirst()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            SeekFood.DrinkingWaterLocator = null;
            SeekFood animal = CreateAnimal(Vector3.zero, Genome(), energyFraction: 1f, age: 10f);
            animal.currentHydration = 0f;
            animal.thirstDamagePerSecond = 50f;
            yield return null;
            yield return null;
            SpeciesTelemetryRecord record = Telemetry();

            float deadline = Time.time + 20f;
            while (AnimalCount() > 0 && Time.time < deadline) yield return null;
            Assert.AreEqual(0, AnimalCount(), "The animal should have died of thirst.");
            Assert.AreEqual(1, record.thirstDeaths);
            Assert.AreEqual(0, record.starvationDeaths);
        }

        // A day animal (chronotype 0) settles down at night and uses less energy, then wakes once it is day (item 74).
        // The test scene has no day clock, so the moment is set by hand.
        [UnityTest]
        public IEnumerator DayAnimalsRestAtNightOnLessEnergyAndWakeByDay()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            SeekFood animal = CreateAnimal(Vector3.zero, Genome(), energyFraction: 0.9f, age: 10f);
            yield return null;
            float awakeUse = animal.CurrentEnergyDrainPerSecond;
            try
            {
                SetTimeOfDay(0f);
                float deadline = Time.time + 10f;
                while (!(animal.IsResting && animal.RestEnergyMultiplier < 1f) && Time.time < deadline) yield return null;
                Assert.IsTrue(animal.IsResting, "The day animal did not rest at midnight within 10 simulated seconds.");
                Assert.Less(animal.CurrentEnergyDrainPerSecond, 0.75f * awakeUse, "Resting should save about 30% of energy.");

                SetTimeOfDay(0.5f);
                deadline = Time.time + 10f;
                while (animal.IsResting && Time.time < deadline) yield return null;
                Assert.IsFalse(animal.IsResting, "The day animal did not wake at noon within 10 simulated seconds.");
            }
            finally
            {
                Boreal.World.WorldDay.Reset();
            }
        }

        // Hunger overrides the hours: a starving day animal still walks to food at midnight.
        [UnityTest]
        public IEnumerator HungryAnimalsForageAtTheirOffHours()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            try
            {
                SetTimeOfDay(0f);
                SeekFood animal = CreateAnimal(new Vector3(-10f, 0f, 0f), Genome(), energyFraction: 0.15f, age: 10f);
                FoodItem plant = CreatePlant(new Vector3(10f, 0f, 0f));

                float deadline = Time.time + 30f;
                while (plant != null && Time.time < deadline) yield return null;
                Assert.IsTrue(plant == null, "The starving animal did not eat at night within 30 simulated seconds.");
                Assert.IsFalse(animal.IsResting);
            }
            finally
            {
                Boreal.World.WorldDay.Reset();
            }
        }

        // Thirst overrides the hours too, and sooner than hunger: a thirsty day animal goes to water at midnight
        // instead of resting until its water runs low.
        [UnityTest]
        public IEnumerator ThirstyAnimalsDrinkBeforeTheyRest()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            SeekFood.DrinkingWaterLocator = LocateTestWater;
            try
            {
                SetTimeOfDay(0f);
                SeekFood animal = CreateAnimal(Vector3.zero, Genome(), energyFraction: 0.9f, age: 10f);
                animal.currentHydration = 0.4f * animal.maxHydration;

                bool restedFirst = false;
                float deadline = Time.time + 30f;
                while (!animal.IsDrinking && Time.time < deadline)
                {
                    restedFirst |= animal.IsResting;
                    yield return null;
                }

                Assert.IsTrue(animal.IsDrinking, "The thirsty animal did not reach the water within 30 simulated seconds.");
                Assert.IsFalse(restedFirst, "The thirsty animal rested before it drank.");
            }
            finally
            {
                Boreal.World.WorldDay.Reset();
            }
        }

        // Item 66: a hungry meat-leaning animal hunts a member of its own species that isn't close kin, and the kill
        // counts as predation.
        [UnityTest]
        public IEnumerator MeatLeaningAnimalsHuntTheirOwnKind()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            SeekFood hunter = CreateAnimal(new Vector3(-10f, 0f, 0f), Genome(diet: 0.6f), energyFraction: 0.4f, age: 10f);
            SeekFood prey = CreateAnimal(new Vector3(10f, 0f, 0f), SlowGenome(), energyFraction: 0.9f, age: 10f);
            yield return null;
            Assert.IsTrue(hunter.HuntsOwnKind);
            prey.currentHealth = 20f;

            float deadline = Time.time + 30f;
            while (prey != null && prey.IsAlive && Time.time < deadline) yield return null;
            Assert.IsFalse(prey != null && prey.IsAlive, "The meat-leaning animal did not kill its own kind within 30 s.");
            Assert.AreEqual(1, Telemetry().predationDeaths);
        }

        // Below Hunt Own Kind From Diet, an animal leaves its own species alone however hungry it is.
        [UnityTest]
        public IEnumerator PlantEatersLeaveTheirOwnKindAlone()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            SeekFood hungry = CreateAnimal(new Vector3(-10f, 0f, 0f), Genome(), energyFraction: 0.4f, age: 10f);
            SeekFood other = CreateAnimal(new Vector3(10f, 0f, 0f), SlowGenome(), energyFraction: 0.9f, age: 10f);
            yield return null;
            Assert.IsFalse(hungry.HuntsOwnKind);
            Assert.IsFalse(hungry.IsViablePrey(other));
            other.currentHealth = 20f;

            float deadline = Time.time + 15f;
            while (other.IsAlive && Time.time < deadline) yield return null;
            Assert.IsTrue(other.IsAlive, "A plant-eater attacked its own kind.");
            Assert.AreEqual(0, Telemetry().predationDeaths);
        }

        // A plant-eater would never hunt a meat-eater of its own kind, but it still sees one hunting it, and flees or
        // fights back.
        [UnityTest]
        public IEnumerator PlantEatersNoticeAHunterOfTheirOwnKind()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            SeekFood hunter = CreateAnimal(new Vector3(-10f, 0f, 0f), Genome(diet: 0.6f), energyFraction: 0.4f, age: 10f);
            SeekFood prey = CreateAnimal(new Vector3(10f, 0f, 0f), Genome(), energyFraction: 0.9f, age: 10f);
            yield return null;
            Assert.IsFalse(prey.IsViablePrey(hunter));

            float deadline = Time.time + 20f;
            SpeciesTelemetryRecord record = Telemetry();
            while (record.fleeResponses + record.fightResponses == 0 && Time.time < deadline) yield return null;
            Assert.Greater(record.fleeResponses + record.fightResponses, 0,
                           "The plant-eater never fled from or fought the hunter of its own kind.");
        }

        // A founder-like animal that walks at a third of the usual speed, so a hunter can catch it.
        static AnimalGenome SlowGenome()
        {
            AnimalGenome genome = Genome();
            genome[AnimalGene.MoveSpeed] = 2f;
            return genome;
        }

        // An equinox day at 62° N, at this time of day (0 midnight, 0.5 noon), as the day clock would publish it.
        static void SetTimeOfDay(float timeOfDay)
        {
            Boreal.World.WorldDay.Current = Boreal.TerrainGeneration.DayCycle.Evaluate(timeOfDay, 62f, 0f, 4f, 0f);
            Boreal.World.WorldDay.IsRunning = true;
        }

        static readonly Vector3 TestWater = new Vector3(20f, 0f, 0f);

        static Vector3? LocateTestWater(Vector3 position, float radius) => TestWater;

        static SeekFood Child(SeekFood parent)
        {
            foreach (SeekFood animal in Object.FindObjectsByType<SeekFood>(FindObjectsSortMode.None))
            {
                if (animal != parent && animal.bornInSimulation) return animal;
            }
            return null;
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            SeekFood.DrinkingWaterLocator = null;
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        // Entering Play Mode reloads scripts unless the project's Enter Play Mode Options turn that off.
        static bool ReloadsDomain => !EditorSettings.enterPlayModeOptionsEnabled ||
                                     (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0;

        static SpeciesManager StartSimulation()
        {
            SpeciesManager manager = new GameObject("Species Manager").AddComponent<SpeciesManager>();
            manager.simulationSpeed = SimulationSpeed;
            return manager;
        }

        static GameObject CreateGround(Vector3 centre, float size)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = centre;
            ground.transform.localScale = new Vector3(size / 10f, 1f, size / 10f);
            return ground;
        }

        static void BakeNavMesh(params GameObject[] grounds)
        {
            List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
            Bounds bounds = new Bounds(grounds[0].transform.position, Vector3.zero);
            foreach (GameObject ground in grounds)
            {
                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = ground.GetComponent<MeshFilter>().sharedMesh,
                    transform = ground.transform.localToWorldMatrix,
                    area = 0
                });
                bounds.Encapsulate(ground.GetComponent<Renderer>().bounds);
            }

            bounds.Expand(new Vector3(2f, 10f, 2f));
            NavMeshData data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, bounds,
                                                                              Vector3.zero, Quaternion.identity);
            NavMesh.AddNavMeshData(data);
        }

        // Founder-like genes: a slow, plain herbivore capsule that lives long enough for any test.
        static AnimalGenome Genome(float sexualDrive = 0.5f, float diet = 0.15f, float maturityTime = 200f,
                                   AnimalPerk? perk = null)
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
            genome[AnimalGene.MaturityTime] = maturityTime;
            genome[AnimalGene.MaxLifespan] = 1200f;
            genome[AnimalGene.DietAffinity] = diet;
            genome[AnimalGene.PreferredTemperature] = 2f;
            genome[AnimalGene.ColdTolerance] = 18f;
            genome[AnimalGene.HeatTolerance] = 16f;
            genome[AnimalGene.SexualDrive] = sexualDrive;
            genome[AnimalGene.BreedingThreshold] = 0.8f;
            genome[AnimalGene.MutationRate] = 1f;
            if (perk.HasValue) genome.SetPerk(perk.Value, true);
            return genome;
        }

        // Built like the demo's founders, then given the genome, age and energy before its first frame.
        static SeekFood CreateAnimal(Vector3 position, AnimalGenome genome, float energyFraction, float age = 0f)
        {
            GameObject animal = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            animal.SetActive(false);
            animal.name = "Test Animal";
            animal.transform.position = position;

            NavMeshAgent agent = animal.AddComponent<NavMeshAgent>();
            agent.radius = 1.1f;
            agent.height = 2f;
            agent.baseOffset = 0f;
            agent.angularSpeed = 240f;
            agent.acceleration = 36f;
            animal.AddComponent<UtilityDecisionPolicy>().minimumFoodUtility = 0.05f;
            animal.AddComponent<AnimalTemperature>();

            SeekFood behaviour = animal.AddComponent<SeekFood>();
            behaviour.foodInteractionRange = 5f;
            behaviour.attackRange = 5f;
            behaviour.baseFeedingReach = 5f;
            behaviour.wanderRadius = 20f;
            behaviour.PrepareFromOffscreenPopulation(genome, Species, Color.green, 0, age, energyFraction);
            animal.SetActive(true);
            return behaviour;
        }

        // Built like the demo's plant food: a 2 m cube resting on the ground, with a trigger collider.
        static FoodItem CreatePlant(Vector3 groundPosition)
        {
            GameObject food = GameObject.CreatePrimitive(PrimitiveType.Cube);
            food.SetActive(false);
            food.name = "Test Plant";
            food.tag = "Food";
            food.transform.position = groundPosition + Vector3.up;
            food.transform.localScale = Vector3.one * 2f;
            food.GetComponent<Collider>().isTrigger = true;
            Rigidbody body = food.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            FoodItem item = food.AddComponent<FoodItem>();
            item.foodType = FoodType.Plant;
            item.nutritionValue = 50f;
            food.SetActive(true);
            return item;
        }

        static int AnimalCount() => Object.FindObjectsByType<SeekFood>(FindObjectsSortMode.None).Length;

        static SpeciesTelemetryRecord Telemetry()
        {
            Assert.IsNotNull(SpeciesManager.Instance);
            Assert.IsTrue(SpeciesManager.Instance.Telemetry.TryGetValue(Species, out SpeciesTelemetryRecord record),
                          "The Species Manager has no record for the test species.");
            return record;
        }

        static Mesh PartsMesh(SeekFood animal)
        {
            Transform parts = animal.transform.Find("Body Parts");
            MeshFilter filter = parts != null ? parts.GetComponent<MeshFilter>() : null;
            return filter != null ? filter.sharedMesh : null;
        }
    }
}
