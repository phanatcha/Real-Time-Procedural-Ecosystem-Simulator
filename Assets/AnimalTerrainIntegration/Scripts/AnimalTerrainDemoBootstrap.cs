using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class AnimalTerrainDemoBootstrap : MonoBehaviour
{
    public TerrainGenerator terrainGenerator;
    public Transform simulationFocus;

    [Header("Disposable Primitive Demo")]
    [Range(0, 12)] public int founderAnimalCount = 3;
    [Range(0, 500)] public int initialFoodCount = 90;
    [Min(10f)] public float animalSpawnRadius = 120f;
    [Min(10f)] public float foodSpawnRadius = 380f;
    public bool createWorldBorders = true;

    AnimalTerrainWorld terrainWorld;
    ProceduralTerrainNavMesh proceduralNavMesh;
    FoodSpawner foodSpawner;
    Transform animalParent;
    Transform foodParent;
    Transform templateParent;
    Material animalMaterial;
    Material foodMaterial;

    void Awake()
    {
        if (terrainGenerator == null) terrainGenerator = FindAnyObjectByType<TerrainGenerator>();
        if (terrainGenerator == null)
        {
            Debug.LogError("The placeholder animal demo needs a TerrainGenerator in the scene.", this);
            enabled = false;
            return;
        }

        if (simulationFocus == null) simulationFocus = terrainGenerator.viewer;
        if (simulationFocus == null) simulationFocus = transform;

        terrainWorld = GetOrAddComponent<AnimalTerrainWorld>();
        terrainWorld.terrainGenerator = terrainGenerator;

        proceduralNavMesh = GetOrAddComponent<ProceduralTerrainNavMesh>();
        proceduralNavMesh.terrainWorld = terrainWorld;
        proceduralNavMesh.focus = simulationFocus;
        proceduralNavMesh.buildRadius = Mathf.Max(600f, foodSpawnRadius + 100f);

        ProceduralTerrainTemperatureProvider temperatureProvider =
            GetOrAddComponent<ProceduralTerrainTemperatureProvider>();
        temperatureProvider.terrainWorld = terrainWorld;

        TemperatureSystem temperatureSystem = FindAnyObjectByType<TemperatureSystem>();
        if (temperatureSystem == null) temperatureSystem = gameObject.AddComponent<TemperatureSystem>();
        if (temperatureSystem.provider == null) temperatureSystem.provider = temperatureProvider;
        temperatureSystem.fallbackTemperatureCelsius = 2f;

        if (FindAnyObjectByType<SpeciesManager>() == null) gameObject.AddComponent<SpeciesManager>();

        if (createWorldBorders)
        {
            AutoTerrainBorders borders = GetOrAddComponent<AutoTerrainBorders>();
            borders.proceduralTerrain = terrainWorld;
            borders.wallHeight = 300f;
            borders.wallThickness = 8f;
        }

        animalParent = GetOrCreateChild("Placeholder Animals", true);
        foodParent = GetOrCreateChild("Placeholder Food", true);
        templateParent = GetOrCreateChild("Runtime Templates", false);
        animalMaterial = CreatePlaceholderMaterial("Cylinder Animal Material", Color.white, 0.28f);
        foodMaterial = CreatePlaceholderMaterial(
            "Plant Food Material", new Color(0.35f, 0.8f, 0.24f), 0.12f);

        foodSpawner = GetOrAddComponent<FoodSpawner>();
        foodSpawner.spawnCenter = simulationFocus;
        foodSpawner.spawnedFoodParent = foodParent;
        foodSpawner.spawnRadius = foodSpawnRadius;
        foodSpawner.maxFoodCount = Mathf.Max(100, initialFoodCount * 3);
        foodSpawner.spawnChancePerSecond = 8f;
        foodSpawner.surfaceOffset = 1f;
        foodSpawner.terrainSearchRadius = 42f;
        foodSpawner.navMeshSampleDistance = 18f;
        foodSpawner.foodPrefab = CreateFoodTemplate();
    }

    IEnumerator Start()
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (!proceduralNavMesh.IsReady && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        if (!proceduralNavMesh.IsReady)
        {
            Debug.LogError("The placeholder animals were not created because no walkable NavMesh could be built.", this);
            yield break;
        }

        int existingAnimals = FindObjectsByType<SeekFood>().Length;
        int createdAnimals = existingAnimals == 0 ? SpawnFounderAnimals() : 0;

        int missingFood = Mathf.Max(0, initialFoodCount - FoodItem.ActiveCount);
        int spawnedFood = foodSpawner.SpawnImmediately(missingFood);
        if (spawnedFood < missingFood)
        {
            Debug.LogWarning($"Placed {spawnedFood} of {missingFood} requested placeholder food cubes.", this);
        }
        Debug.Log(
            $"Animal terrain demo ready: {existingAnimals + createdAnimals} cylinder animals, " +
            $"{FoodItem.ActiveCount} food items, and land-only navigation are active.",
            this);
    }

    int SpawnFounderAnimals()
    {
        Color[] colors =
        {
            new Color(0.25f, 0.72f, 0.95f),
            new Color(0.95f, 0.67f, 0.22f),
            new Color(0.82f, 0.28f, 0.38f)
        };
        float[] diets = { 0.08f, 0.5f, 0.92f };
        int created = 0;
        int primaryAttempts = Mathf.Max(20, founderAnimalCount * 20);
        int attempts = Mathf.Max(60, founderAnimalCount * 60);
        int totalAttempts = attempts;
        float fallbackRadius = Mathf.Max(
            animalSpawnRadius,
            Mathf.Min(foodSpawnRadius,
                proceduralNavMesh.buildRadius - proceduralNavMesh.sampleSpacing * 2f));

        while (created < founderAnimalCount && attempts-- > 0)
        {
            int attemptIndex = totalAttempts - attempts - 1;
            Vector2 offset;
            if (attemptIndex < primaryAttempts || fallbackRadius <= animalSpawnRadius)
            {
                offset = Random.insideUnitCircle * animalSpawnRadius;
            }
            else
            {
                Vector2 direction = Random.insideUnitCircle;
                if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;
                offset = direction.normalized * Random.Range(animalSpawnRadius, fallbackRadius);
            }
            Vector3 candidate = simulationFocus.position + new Vector3(offset.x, 0f, offset.y);
            if (!proceduralNavMesh.TryProjectToNavigation(candidate, 40f, out Vector3 position)) continue;

            int variant = created % colors.Length;
            CreateFounder(created, position, colors[variant], diets[variant]);
            created++;
        }

        if (created < founderAnimalCount)
        {
            Debug.LogWarning($"Placed {created} of {founderAnimalCount} requested cylinder founders.", this);
        }
        return created;
    }

    void CreateFounder(int index, Vector3 position, Color color, float dietAffinity)
    {
        GameObject animal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        animal.SetActive(false);
        animal.name = $"Cylinder_Founder_{(char)('A' + index)}";
        animal.transform.SetParent(animalParent, true);
        animal.transform.position = position;
        animal.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        animal.transform.localScale = new Vector3(2.2f, 2.5f, 2.2f);
        ShiftPrimitiveMeshToGround(animal);
        animal.GetComponent<MeshRenderer>().sharedMaterial = animalMaterial;

        Collider bodyCollider = animal.GetComponent<Collider>();
        if (bodyCollider is CapsuleCollider capsuleCollider)
        {
            capsuleCollider.center = Vector3.up;
        }

        NavMeshAgent agent = animal.AddComponent<NavMeshAgent>();
        agent.agentTypeID = proceduralNavMesh.agentTypeId;
        agent.radius = 1.1f;
        agent.height = 5f;
        agent.baseOffset = 0f;
        agent.angularSpeed = 240f;
        agent.acceleration = 36f;

        UtilityDecisionPolicy policy = animal.AddComponent<UtilityDecisionPolicy>();
        policy.minimumFoodUtility = 0.05f;

        AnimalTemperature temperature = animal.AddComponent<AnimalTemperature>();
        temperature.preferredTemperature = 2f;
        temperature.coldTolerance = 18f;
        temperature.heatTolerance = 16f;

        SeekFood behavior = animal.AddComponent<SeekFood>();
        behavior.speciesName = ((char)('A' + index)).ToString();
        behavior.speciesColor = color;
        behavior.dietAffinity = dietAffinity;
        behavior.currentEnergy = 105f;
        behavior.moveSpeed = 12f;
        behavior.visionRadius = 110f;
        behavior.wanderRadius = 75f;
        behavior.foodInteractionRange = 5f;
        behavior.attackRange = 5f;
        behavior.baseFeedingReach = 5f;
        behavior.fleeDistance = 70f;
        behavior.maturityTime = 45f;
        behavior.maxLifespan = 300f;
        behavior.mutationChance = 5f;
        behavior.mutationMagnitude = 0.1f;

        animal.SetActive(true);
    }

    GameObject CreateFoodTemplate()
    {
        Transform existing = templateParent.Find("Cube_PlantFood_Template");
        if (existing != null) return existing.gameObject;

        GameObject food = GameObject.CreatePrimitive(PrimitiveType.Cube);
        food.name = "Cube_PlantFood_Template";
        food.transform.SetParent(templateParent, false);
        food.transform.localScale = Vector3.one * 2f;
        food.tag = "Food";

        Collider collider = food.GetComponent<Collider>();
        collider.isTrigger = true;
        Rigidbody body = food.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        FoodItem foodItem = food.AddComponent<FoodItem>();
        foodItem.foodType = FoodType.Plant;
        foodItem.nutritionValue = 50f;
        foodItem.minimumGrowthTemperature = -12f;
        foodItem.optimalGrowthTemperatureMin = 2f;
        foodItem.optimalGrowthTemperatureMax = 13f;
        foodItem.maximumGrowthTemperature = 25f;
        foodItem.referenceLifetime = 180f;
        foodItem.spoilageReferenceTemperature = 8f;

        MeshRenderer renderer = food.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = foodMaterial;
        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        Color color = new Color(0.35f, 0.8f, 0.24f);
        properties.SetColor(Shader.PropertyToID("_BaseColor"), color);
        properties.SetColor(Shader.PropertyToID("_Color"), color);
        renderer.SetPropertyBlock(properties);
        return food;
    }

    Material CreatePlaceholderMaterial(string materialName, Color color, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return null;

        Material material = new Material(shader) { name = materialName };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        return material;
    }

    void ShiftPrimitiveMeshToGround(GameObject animal)
    {
        MeshFilter filter = animal.GetComponent<MeshFilter>();
        Mesh shiftedMesh = Instantiate(filter.sharedMesh);
        shiftedMesh.name = "Grounded Cylinder Placeholder";
        Vector3[] vertices = shiftedMesh.vertices;
        for (int index = 0; index < vertices.Length; index++) vertices[index].y += 1f;
        shiftedMesh.vertices = vertices;
        shiftedMesh.RecalculateBounds();
        filter.sharedMesh = shiftedMesh;
    }

    Transform GetOrCreateChild(string childName, bool active)
    {
        Transform existing = transform.Find(childName);
        if (existing != null)
        {
            existing.gameObject.SetActive(active);
            return existing;
        }

        GameObject child = new GameObject(childName);
        child.transform.SetParent(transform, false);
        child.SetActive(active);
        return child.transform;
    }

    T GetOrAddComponent<T>() where T : Component
    {
        T component = GetComponent<T>();
        return component == null ? gameObject.AddComponent<T>() : component;
    }

    void OnDestroy()
    {
        if (!Application.isPlaying) return;
        if (animalMaterial != null) Destroy(animalMaterial);
        if (foodMaterial != null) Destroy(foodMaterial);
    }
}
