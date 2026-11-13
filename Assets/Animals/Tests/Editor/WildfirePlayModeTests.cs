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
    // Wildfires in a running scene (items 78 and 79), built as AnimalBehaviourPlayModeTests builds its scenes: flat
    // ground with a NavMesh, a Species Manager, a natural event director with random events off, and a stand-in fuel
    // landscape instead of terrain. Entering Play Mode reloads scripts, so each test builds everything afterwards.
    public class WildfirePlayModeTests
    {
        const string Species = "Test";

        // Dry forest everywhere within radius of the origin (everywhere when radius is 0), nothing beyond.
        static void UseForest(float radius = 0f)
        {
            WildfireKind.LandscapeOverride = new FireLandscapeFunction(150f, (position, dryness) =>
                radius > 0f && position.magnitude > radius
                    ? new FireFuel { burnable = false }
                    : new FireFuel { burnable = true, load = 1f, crown = 0.85f, moisture = 0.2f });
        }

        // An animal 35 m from a fire runs from it, or at least is hurt by it.
        [UnityTest]
        public IEnumerator AnAnimalNearAFireRunsFromIt()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            UseForest();
            BakeNavMesh(CreateGround(Vector3.zero, 200f));
            SpeciesManager manager = StartSimulation(5f);
            NaturalEventDirector director = StartDirector();
            SeekFood animal = CreateAnimal(new Vector3(35f, 0f, 0f), Genome(), energyFraction: 0.5f);
            yield return null;

            NaturalEvent fire = director.Force("wildfire", Vector3.zero, 1f, 0f, "test");
            Assert.IsNotNull(fire, "The fire did not start.");
            float startDistance = Vector3.Distance(animal.transform.position, Vector3.zero);
            bool fled = false;
            float deadline = Time.time + 30f;
            while (animal != null && !fled && Time.time < deadline)
            {
                fled = animal.IsFleeingFire;
                yield return null;
            }

            Assert.IsTrue(animal == null || fled || animal.currentHealth < animal.maxHealth,
                          "The animal neither ran from the fire nor was hurt by it within 30 simulated seconds.");
            Assert.IsTrue(fled, "The animal did not run from the fire.");
            float runUntil = Time.time + 6f;
            while (animal != null && Time.time < runUntil) yield return null;
            Assert.IsNotNull(animal, "The animal died although it had time to run.");
            Assert.Greater(Vector3.Distance(animal.transform.position, Vector3.zero), startDistance + 10f,
                           "The animal did not get further from the fire.");
            Assert.IsTrue(HasEvent(manager, "wildfire"), "The fire's start was not recorded.");
        }

        // An animal that can't get away (its ground is a 6 m patch in the middle of the fire) dies of fire, and the
        // death is counted as fire by the Species Manager and against the fire that killed it.
        [UnityTest]
        public IEnumerator AnAnimalCaughtInTheFlamesDiesOfFire()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            // The death callback's captured state is made after the domain reload: a closure made in this iterator
            // before EnterPlayMode is not restored by the test runner.
            yield return WatchAnAnimalBurn();
        }

        static IEnumerator WatchAnAnimalBurn()
        {
            UseForest();
            BakeNavMesh(CreateGround(Vector3.zero, 6f));
            SpeciesManager manager = StartSimulation(5f);
            NaturalEventDirector director = StartDirector();
            SeekFood animal = CreateAnimal(Vector3.zero, Genome(), energyFraction: 0.8f);
            AgentDeathCause? cause = null;
            manager.AnimalDied += (dead, why) => { if (dead == animal) cause = why; };
            yield return null;

            WildfireEvent fire = director.Force("wildfire", Vector3.zero, 3f, 0f, "test") as WildfireEvent;
            Assert.IsNotNull(fire, "The fire did not start.");
            float deadline = Time.time + 40f;
            while (animal != null && Time.time < deadline) yield return null;

            Assert.IsTrue(animal == null, "The animal survived 40 simulated seconds in the middle of a fire.");
            Assert.AreEqual(AgentDeathCause.Fire, cause);
            Assert.AreEqual(1, Telemetry().fireDeaths);
            Assert.AreEqual(1, fire.AnimalsKilled);
        }

        // A fire in a patch of forest 30 m across burns out, burns the plants in it but not those outside, and its
        // start and end are recorded.
        [UnityTest]
        public IEnumerator AFireBurnsOutBurnsItsPlantsAndIsRecorded()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            UseForest(radius: 30f);
            SpeciesManager manager = StartSimulation(20f);
            NaturalEventDirector director = StartDirector();
            FoodItem inside = CreatePlant(new Vector3(12f, 0f, 4f));
            FoodItem outside = CreatePlant(new Vector3(60f, 0f, 0f));
            yield return null;

            WildfireEvent fire = director.Force("wildfire", Vector3.zero, 1f, 0f, "test") as WildfireEvent;
            Assert.IsNotNull(fire, "The fire did not start.");
            float deadline = Time.time + 400f;
            while (!fire.IsOver && Time.time < deadline) yield return null;
            yield return null;

            Assert.IsTrue(fire.IsOver, "The fire was still burning after 400 simulated seconds.");
            WildfireGrid grid = director.Wildfire.Grid;
            Assert.AreEqual(0, grid.BurningCount);
            Assert.Greater(grid.EverBurnedCount, 20, "The fire should have burned most of the patch.");
            for (int index = 0; index < grid.size * grid.size; index++)
            {
                if (grid.StateOf(index) == WildfireGrid.CellState.Unburned) continue;
                Assert.LessOrEqual(grid.CellCentre(index).magnitude, 30f, "The fire burned outside its fuel.");
            }
            Assert.IsTrue(inside == null, "The plant inside the fire did not burn.");
            Assert.IsTrue(outside != null && outside.IsAvailable, "The plant outside the fire burned.");
            Assert.GreaterOrEqual(fire.PlantsBurned, 1);
            Assert.IsTrue(HasEvent(manager, "wildfire"), "The fire's start was not recorded.");
            Assert.IsTrue(HasEvent(manager, "wildfire-end"), "The fire's end was not recorded.");
            Assert.AreEqual(0, director.Running.Count);
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            WildfireKind.LandscapeOverride = null;
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        static bool ReloadsDomain => !EditorSettings.enterPlayModeOptionsEnabled ||
                                     (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0;

        static bool HasEvent(SpeciesManager manager, string kind)
        {
            foreach (SpeciesManager.SimulationEvent happening in manager.Events)
            {
                if (happening.kind == kind) return true;
            }
            return false;
        }

        static SpeciesManager StartSimulation(float speed)
        {
            SpeciesManager manager = new GameObject("Species Manager").AddComponent<SpeciesManager>();
            manager.simulationSpeed = speed;
            return manager;
        }

        // Dry, calm and with no random fires, so only the test's fire burns.
        static NaturalEventDirector StartDirector()
        {
            NaturalEventDirector director = new GameObject("Natural Events").AddComponent<NaturalEventDirector>();
            director.randomEvents = false;
            director.weather.seasonlessDryness = 0.9f;
            director.weather.averageWindSpeed = 0f;
            director.weather.windSpeedSpread = 0f;
            director.weather.spellStrength = 0f;
            return director;
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

        // Founder-like genes, as in AnimalBehaviourPlayModeTests.
        static AnimalGenome Genome()
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
            genome[AnimalGene.MaturityTime] = 200f;
            genome[AnimalGene.MaxLifespan] = 1200f;
            genome[AnimalGene.AgeingOnset] = 1200f;
            genome[AnimalGene.DietAffinity] = 0.15f;
            genome[AnimalGene.PreferredTemperature] = 2f;
            genome[AnimalGene.ColdTolerance] = 18f;
            genome[AnimalGene.HeatTolerance] = 16f;
            genome[AnimalGene.SexualDrive] = 0f;
            genome[AnimalGene.BreedingThreshold] = 0.95f;
            genome[AnimalGene.MutationRate] = 1f;
            return genome;
        }

        static SeekFood CreateAnimal(Vector3 position, AnimalGenome genome, float energyFraction)
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
            behaviour.enableThirst = false;
            behaviour.PrepareFromOffscreenPopulation(genome, Species, Color.green, 0, 10f, energyFraction);
            animal.SetActive(true);
            return behaviour;
        }

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

        static SpeciesTelemetryRecord Telemetry()
        {
            Assert.IsNotNull(SpeciesManager.Instance);
            Assert.IsTrue(SpeciesManager.Instance.Telemetry.TryGetValue(Species, out SpeciesTelemetryRecord record),
                          "The Species Manager has no record for the test species.");
            return record;
        }
    }
}
