
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

        // Ageing (item 68): past its onset an animal's vigour falls, which caps its health, slows it and ends its life
        // well before its lifespan; with ageing off it stays in full health. A later onset costs upkeep.
        [UnityTest]
        public IEnumerator AgeingAnimalsWeakenSlowDownAndDieBeforeTheirLifespan()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 80f));
            StartSimulation();
            AnimalGenome genome = Genome(maturityTime: 200f);
            genome[AnimalGene.AgeingOnset] = 100f;
            // Halfway through a 20 s decline: vigour 0.5.
            SeekFood aged = CreateAnimal(new Vector3(-20f, 0f, 0f), genome, energyFraction: 0.9f, age: 110f);
            aged.ageingSpan = 20f;
            SeekFood young = CreateAnimal(new Vector3(0f, 0f, 20f), genome, energyFraction: 0.9f, age: 50f);
            SeekFood unaged = CreateAnimal(new Vector3(20f, 0f, 0f), genome, energyFraction: 0.9f, age: 110f);
            unaged.enableAgeing = false;
            yield return null;
            yield return null;

            Assert.AreEqual(1f, young.Vigour);
            Assert.AreEqual(1f, unaged.Vigour, "With ageing off an animal keeps its vigour.");
            Assert.That(aged.Vigour, Is.InRange(0.35f, 0.5f));
            Assert.LessOrEqual(aged.currentHealth, aged.maxHealth * aged.Vigour + 0.01f, "Health is capped by vigour.");
            Assert.AreEqual(unaged.maxHealth, unaged.currentHealth, 0.01f);
            float speedShare = aged.GetComponent<NavMeshAgent>().speed / young.GetComponent<NavMeshAgent>().speed;
            Assert.That(speedShare, Is.EqualTo(Mathf.Lerp(aged.agedSpeedFloor, 1f, aged.Vigour)).Within(0.05f),
                        "An ageing animal is slower by its vigour.");

            // A later onset costs upkeep, charged against the onset it started with like the other traits.
            float before = young.CurrentEnergyDrainPerSecond;
            young.ageingOnset *= 2f;
            young.RefreshMetabolicRate();
            float total = young.speedMetabolicWeight + young.strengthMetabolicWeight + young.bodyMassMetabolicWeight +
                          young.visionMetabolicWeight + young.energyCapacityMetabolicWeight +
                          young.staminaCapacityMetabolicWeight + young.healthMetabolicWeight + young.repairMetabolicWeight;
            Assert.That(young.CurrentEnergyDrainPerSecond / before,
                        Is.EqualTo(1f + young.repairMetabolicWeight / total).Within(1e-3f));

            // Vigour runs out at age 120, long before the 1200 s lifespan.
            float deadline = Time.time + 20f;
            while (aged != null && Time.time < deadline) yield return null;
            Assert.IsTrue(aged == null, "The ageing animal did not die when its vigour ran out.");
            Assert.AreEqual(1, Telemetry().oldAgeDeaths);
            Assert.IsTrue(unaged != null, "With ageing off the animal lives on.");
        }

        // The energy ledger (item 61): through eating, growing, a litter and a death, every change to the animals' stores
        // is accounted for, and no rule makes energy from nothing.
        [UnityTest]
        public IEnumerator TheEnergyLedgerBalancesThroughEatingALitterGrowthAndADeath()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            SpeciesManager manager = StartSimulation();
            // No births until the ledger is counting.
            manager.populationCeiling = 2;
            AnimalGenome litterOfThree = Genome(sexualDrive: 0f, maturityTime: 5f);
            litterOfThree[AnimalGene.LitterSize] = 3f;
            SeekFood parent = CreateAnimal(Vector3.zero, litterOfThree, energyFraction: 1f, age: 10f);
            SeekFood eater = CreateAnimal(new Vector3(20f, 0f, 0f), Genome(maturityTime: 200f), energyFraction: 0.3f, age: 10f);
            CreatePlant(new Vector3(26f, 0f, 0f));
            yield return null;
            yield return null;
            try
            {
                EnergyLedger.Begin(Object.FindObjectsByType<SeekFood>(FindObjectsSortMode.None));
                manager.populationCeiling = 400;

                float deadline = Time.time + 20f;
                while ((AnimalCount() < 4 || EnergyLedger.Total(EnergyFlow.Plants) <= 0) && Time.time < deadline) yield return null;
                Assert.GreaterOrEqual(AnimalCount(), 4, "No litter was born within 20 simulated seconds.");
                Assert.Greater(EnergyLedger.Total(EnergyFlow.Plants), 0, "The hungry animal did not eat its plant.");
                // Let the young grow for a while, then one of them dies.
                float grownBy = Time.time + 3f;
                while (Time.time < grownBy) yield return null;
                Child(parent).Kill(AgentDeathCause.Predation);
                yield return null;
                yield return null;

                SeekFood[] living = Object.FindObjectsByType<SeekFood>(FindObjectsSortMode.None);
                string report = EnergyLedger.Report(living);
                Assert.AreEqual(0, EnergyLedger.Violations, report);
                Assert.AreEqual(0.0, EnergyLedger.Gap(living), 0.01, report);
                // Each kind of change was seen, so the balance above covers them.
                Assert.Less(EnergyLedger.Total(EnergyFlow.GivenToYoung), 0, report);
                Assert.Greater(EnergyLedger.Total(EnergyFlow.BornWith), 0, report);
                Assert.Less(EnergyLedger.Total(EnergyFlow.Upkeep), 0, report);
                Assert.Less(EnergyLedger.Total(EnergyFlow.Growth), 0, report);
                Assert.Less(EnergyLedger.Total(EnergyFlow.LostAtDeath), 0, report);
                // The litter got no more than the parent handed over.
                Assert.LessOrEqual(EnergyLedger.Total(EnergyFlow.BornWith), -EnergyLedger.Total(EnergyFlow.GivenToYoung) + 0.01, report);

                // A change made behind the ledger's back is caught and named.
                eater.currentEnergy += 5f;
                yield return null;
                yield return null;
                Assert.AreEqual(1, EnergyLedger.Violations);
                Assert.AreEqual(5.0, EnergyLedger.Total(EnergyFlow.Untracked), 0.001);
            }
            finally
            {
                EnergyLedger.End();
            }
        }

        // Harmful mutations are paid for in full (items 12 and 61): ten of them at 5% each cost half as much again.
        [UnityTest]
        public IEnumerator HarmfulMutationsRaiseUpkeepByTheirFullCost()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            AnimalGenome loaded = Genome(maturityTime: 200f);
            for (int count = 0; count < 10; count++) loaded.TryAddHarmfulMutation(100f);
            SeekFood clean = CreateAnimal(new Vector3(-20f, 0f, 0f), Genome(maturityTime: 200f), energyFraction: 0.9f, age: 250f);
            SeekFood burdened = CreateAnimal(new Vector3(20f, 0f, 0f), loaded, energyFraction: 0.9f, age: 250f);
            yield return null;
            yield return null;
            Assert.AreEqual(10, burdened.Genome.HarmfulMutations);
            Assert.That(burdened.CurrentEnergyDrainPerSecond,
                        Is.EqualTo(clean.CurrentEnergyDrainPerSecond * (1f + 10 * burdened.harmfulMutationEnergyCost)).Within(1e-4f));
        }

        // The experiment lock (item 60): a locked sexual drive holds through the generations whatever the gene mutates to.
        [UnityTest]
        public IEnumerator ALockedSexualDriveHoldsInTheChildrenWhateverTheyInherit()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            // The gene says mate-or-clone (0.5) and every gene mutates at every birth; the lock says clone only.
            SeekFood parent = CreateAnimal(Vector3.zero, Genome(sexualDrive: 0.5f, maturityTime: 5f), energyFraction: 1f, age: 10f);
            parent.lockedSexualDrive = 0f;
            parent.mutationChance = 100f;

            float deadline = Time.time + 20f;
            while (AnimalCount() < 2 && Time.time < deadline) yield return null;
            yield return null;
            SeekFood child = Child(parent);
            Assert.IsNotNull(child, "No clone was born within 20 simulated seconds.");
            Assert.AreEqual(0f, parent.sexualDrive, "The lock applies to the animal it is set on.");
            Assert.AreEqual(0f, child.lockedSexualDrive, "Children copy the lock.");
            Assert.AreEqual(0f, child.sexualDrive);
            Assert.AreEqual(0f, child.Genome[AnimalGene.SexualDrive], "The recorded gene agrees with the behaviour.");
            Assert.AreNotEqual(parent.Genome[AnimalGene.VisionRadius], child.Genome[AnimalGene.VisionRadius],
                               "The other genes still mutate.");
            Assert.AreEqual(0, Telemetry().sexualOffspring);
        }

        // Life records (item 57): every animal gets one row in lives.csv, as it dies or when the recording closes, and a
        // child's row names its parent.
        [UnityTest]
        public IEnumerator EveryAnimalLeavesOneLifeRecordThatNamesItsParent()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            SpeciesManager manager = StartSimulation();
            manager.enableSafetyPause = false;
            SeekFood parent = CreateAnimal(Vector3.zero, Genome(sexualDrive: 0f, maturityTime: 5f), energyFraction: 1f, age: 10f);
            var recorder = manager.gameObject.AddComponent<SimulationRunRecorder>();
            recorder.sourceRevision = "test";
            yield return null;
            yield return null;
            string directory = recorder.OutputDirectory;
            try
            {
                Assert.Greater(parent.AnimalId, 0);
                float deadline = Time.time + 20f;
                while (AnimalCount() < 2 && Time.time < deadline) yield return null;
                Assert.GreaterOrEqual(AnimalCount(), 2, "No clone was born within 20 simulated seconds.");
                // Stop further births, then kill one child and let it be destroyed.
                manager.populationCeiling = 1;
                SeekFood child = null;
                foreach (SeekFood animal in Object.FindObjectsByType<SeekFood>(FindObjectsSortMode.None))
                    if (animal != parent && animal.FirstParentId == parent.AnimalId) child = animal;
                Assert.IsNotNull(child, "The clone does not name its parent.");
                int childId = child.AnimalId;
                Assert.AreNotEqual(parent.AnimalId, childId);
                child.Kill(AgentDeathCause.Predation);
                yield return null;
                yield return null;
                int parentId = parent.AnimalId, young = parent.YoungBorn, born = Telemetry().reproductionEvents;
                int deaths = Telemetry().deaths;
                Assert.GreaterOrEqual(young, 1);
                recorder.enabled = false;

                string[] rows = System.IO.File.ReadAllLines(System.IO.Path.Combine(directory, "lives.csv"));
                string[] columns = rows[0].Split(',');
                int Column(string name) => System.Array.IndexOf(columns, name);
                var byId = new Dictionary<string, string[]>();
                for (int row = 1; row < rows.Length; row++)
                {
                    string[] cells = rows[row].Split(',');
                    Assert.AreEqual(columns.Length, cells.Length);
                    Assert.IsFalse(byId.ContainsKey(cells[Column("id")]), "An animal has two life records.");
                    byId[cells[Column("id")]] = cells;
                }
                string[] childRow = byId[childId.ToString()];
                Assert.AreEqual(parentId.ToString(), childRow[Column("first_parent")]);
                Assert.AreEqual("", childRow[Column("second_parent")]);
                Assert.AreEqual("Predation", childRow[Column("cause")]);
                Assert.AreEqual("born", childRow[Column("origin")]);
                Assert.AreEqual("1", childRow[Column("generation")]);
                Assert.AreNotEqual("", childRow[Column("ended")]);
                string[] parentRow = byId[parentId.ToString()];
                // The test helper places its animals the way off-screen arrivals come back, so that is their origin.
                Assert.AreEqual("offscreen", parentRow[Column("origin")]);
                Assert.AreEqual(LifeRecords.StillAlive, parentRow[Column("cause")]);
                Assert.AreEqual("", parentRow[Column("ended")]);
                Assert.AreEqual(young.ToString(), parentRow[Column("young")]);
                Assert.AreEqual("5", parentRow[Column("MaturityTime")]);
                // The rows add up to the manager's counts: one founder plus every birth, and every death.
                Assert.AreEqual(1 + born, byId.Count);
                int ended = 0;
                foreach (string[] cells in byId.Values) if (cells[Column("ended")] != "") ended++;
                Assert.AreEqual(deaths, ended);
            }
            finally
            {
                recorder.enabled = false;
                if (directory != null && System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
            }
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
                // Item 54: the lone founder is generation 0, and its sexual drive and maturity time sit at their minimum.
                Assert.AreEqual("0", final[System.Array.IndexOf(columns, "average_generation")]);
                Assert.AreEqual("0", final[System.Array.IndexOf(columns, "highest_generation")]);
                string[] speciesRows = System.IO.File.ReadAllLines(System.IO.Path.Combine(directory, "species.csv"));
                string[] speciesColumns = speciesRows[0].Split(',');
                string[] speciesFinal = speciesRows[speciesRows.Length - 1].Split(',');
                Assert.AreEqual(speciesColumns.Length, speciesFinal.Length);
                Assert.AreEqual("0", speciesFinal[System.Array.IndexOf(speciesColumns, "highest_generation")]);
                string[] traitRows = System.IO.File.ReadAllLines(System.IO.Path.Combine(directory, "traits.csv"));
                string[] traitColumns = traitRows[0].Split(',');
                int traitName = System.Array.IndexOf(traitColumns, "trait");
                int atMinimum = System.Array.IndexOf(traitColumns, "at_minimum");
                int atMaximum = System.Array.IndexOf(traitColumns, "at_maximum");
                string[] Last(string trait) =>
                    System.Array.FindLast(traitRows, row => row.Split(',')[traitName] == trait).Split(',');
                foreach (string trait in new[] { "SexualDrive", "MaturityTime" })
                {
                    Assert.AreEqual(traitColumns.Length, Last(trait).Length);
                    Assert.AreEqual("1", Last(trait)[atMinimum], trait);
                    Assert.AreEqual("0", Last(trait)[atMaximum], trait);
                }
                Assert.AreEqual("0", Last("MoveSpeed")[atMinimum]);
                Assert.AreEqual("", Last("HarmfulMutations")[atMinimum], "Only genes have limits.");
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
            // The child is compared with its parent, so it must be an exact copy: a mutation in vision, body size or a
            // body part (about one birth in several) would otherwise fail the comparisons below by chance.
            parent.mutationChance = 0f;
            parent.perkGainChance = 0f;
            parent.perkLossChance = 0f;
            parent.harmfulMutationChance = 0f;
            parent.bodyPlanMutation = default;

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
            // What it paid to grow is the meat its body is made of (item 61): no more than that goes into its carcass.
            Assert.That(child.BodyEnergyPaid, Is.EqualTo(growthCost).Within(0.02f * growthCost));
            Assert.LessOrEqual(child.EstimatedCarcassRawEnergy, child.BodyEnergyPaid + child.currentEnergy);
        }

        // A carcass never holds more than went into the animal (item 61): the cost of building its body and the store
        // it died with, less the transfer loss. A big body does not multiply the store.
        [UnityTest]
        public IEnumerator ACarcassHoldsNoMoreThanTheBodyCostAndTheStore()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            // The largest body the genes allow, with a large store.
            AnimalGenome giant = Genome(maturityTime: 200f);
            giant[AnimalGene.BodyBulk] = 1.8f;
            giant[AnimalGene.BodyHeight] = 2.2f;
            giant[AnimalGene.MaxEnergy] = 400f;
            SeekFood animal = CreateAnimal(Vector3.zero, giant, energyFraction: 0.9f, age: 250f);
            yield return null;
            yield return null;
            try
            {
                EnergyLedger.Begin(Object.FindObjectsByType<SeekFood>(FindObjectsSortMode.None));
                float store = animal.currentEnergy, body = animal.BodyEnergyPaid;
                Assert.Greater(animal.AdultBodyMassFactor, 4f, "The test animal should be far heavier than a founder.");
                Assert.Greater(store, 300f);
                Assert.That(body, Is.EqualTo(animal.growthEnergyCost * animal.carcassBodyEnergy * animal.AdultBodyMassFactor).Within(0.01f),
                            "A placed animal's body is worth what a body its size costs to grow.");
                float expected = (body + store) * animal.carcassEnergyTransferEfficiency;
                Assert.That(animal.EstimatedCarcassRawEnergy, Is.EqualTo(expected).Within(0.01f));

                animal.Kill(AgentDeathCause.Other);
                yield return null;
                yield return null;
                FoodItem carcass = null;
                foreach (FoodItem food in Object.FindObjectsByType<FoodItem>(FindObjectsSortMode.None))
                    if (food.foodType == FoodType.Meat) carcass = food;
                Assert.IsNotNull(carcass, "The dead animal left no carcass.");
                Assert.That(carcass.nutritionValue, Is.EqualTo(expected).Within(0.01f * expected));
                Assert.Less(carcass.nutritionValue, body + store);
                Assert.AreEqual(0, EnergyLedger.Violations, EnergyLedger.Report(new SeekFood[0]));
            }
            finally
            {
                EnergyLedger.End();
            }
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
            // No mutations, so the young's genes are exactly the parent's.
            parent.mutationChance = 0f;

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

        // Hunger overrides the hours: a starving day animal still walks to food at midnight. The food is within the
        // 15% of its vision that ordinary eyes see on a moonless night (item 75).
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
                SeekFood animal = CreateAnimal(new Vector3(-5f, 0f, 0f), Genome(), energyFraction: 0.15f, age: 10f);
                FoodItem plant = CreatePlant(new Vector3(5f, 0f, 0f));

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

        // Item 75: on a moonless night a hungry night animal with ordinary eyes (15% of its vision) misses food that
        // one with night eyes (55%) sees and eats.
        [UnityTest]
        public IEnumerator NightEyesFindFoodOnAMoonlessNight()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 80f));
            StartSimulation();
            try
            {
                SetTimeOfDay(0f);
                Assert.AreEqual(Boreal.TerrainGeneration.DayCycle.StarLight, Boreal.World.WorldDay.Current.light, 1e-3f,
                                "The test night should be moonless.");
                AnimalGenome ordinaryEyed = Genome();
                ordinaryEyed[AnimalGene.Nocturnality] = 1f;
                SeekFood ordinary = CreateAnimal(new Vector3(-35f, 0f, 35f), ordinaryEyed, energyFraction: 0.5f, age: 10f);
                // It must stay where it is: wandering 20 m at a time, it could walk within its 16.5 m of sight of the
                // plant 25 m away and eat it, which is not what this test is about.
                ordinary.wanderRadius = 0f;
                AnimalGenome nightEyed = Genome();
                nightEyed[AnimalGene.Nocturnality] = 1f;
                nightEyed[AnimalGene.NightVision] = 1f;
                SeekFood seer = CreateAnimal(new Vector3(-35f, 0f, -35f), nightEyed, energyFraction: 0.5f, age: 10f);
                // Each plant 25 m from its animal: beyond 15% of 110 m, within 55%. The other animal's plant is 74 m
                // away, out of sight for both.
                FoodItem unseen = CreatePlant(new Vector3(-10f, 0f, 35f));
                FoodItem seen = CreatePlant(new Vector3(-10f, 0f, -35f));

                float deadline = Time.time + 20f;
                while (seen != null && Time.time < deadline) yield return null;
                Assert.IsTrue(seen == null, "The night-eyed animal did not find the food within 20 s.");
                Assert.IsTrue(unseen != null, "The animal with ordinary eyes found food it should not see.");
            }
            finally
            {
                Boreal.World.WorldDay.Reset();
            }
        }

        // Night eyes cost energy: the best ones 6% more than ordinary eyes.
        [UnityTest]
        public IEnumerator NightEyesCostEnergy()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            StartSimulation();
            AnimalGenome nightEyed = Genome();
            nightEyed[AnimalGene.NightVision] = 1f;
            SeekFood ordinary = CreateAnimal(new Vector3(-10f, 0f, 0f), Genome(), energyFraction: 1f, age: 10f);
            SeekFood seer = CreateAnimal(new Vector3(10f, 0f, 0f), nightEyed, energyFraction: 1f, age: 10f);
            yield return null;

            Assert.AreEqual(1f + SeekFood.NightVisionEnergyCost,
                            seer.CurrentEnergyDrainPerSecond / ordinary.CurrentEnergyDrainPerSecond, 1e-3f);
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
            // No ageing within any test's time unless the test asks for it (item 68).
            genome[AnimalGene.AgeingOnset] = 1200f;
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
