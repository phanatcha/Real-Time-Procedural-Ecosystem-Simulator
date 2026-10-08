using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

// Animals behaving in a running scene (item 40). The project has no assembly definitions, so these are Edit Mode
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
