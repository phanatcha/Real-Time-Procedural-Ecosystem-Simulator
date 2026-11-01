using Boreal.EcosystemSimulation;
using Boreal.Animals;
using Boreal.Temperature;
using Boreal.TerrainGeneration;
using Boreal.World;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Boreal.AnimalTerrainIntegration
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class AnimalTerrainDemoBootstrap : MonoBehaviour
    {
        public TerrainGenerator terrainGenerator;
        public Transform simulationFocus;

        [Header("Disposable Primitive Demo")]
        [Range(0, 12)] public int founderAnimalCount = 3;
        [Tooltip("Founders start within this distance of the simulation focus, then spread across the island. " +
                 "Plants load around the camera and the animals; tune them on the Food Spawner component.")]
        [Min(10f)] public float animalSpawnRadius = 120f;
        [Tooltip("How far around the simulation focus to look for the best place to start, in metres. Candidate " +
                 "centres are scored by the walkable land and plant food within the spawn radius, by the food " +
                 "spawner's own rules, so the founders start on real habitat rather than a shore or a strait. " +
                 "The camera moves with them. 0 starts exactly at the focus.")]
        [Min(0f)] public float startSearchRadius = 600f;
        public bool createWorldBorders = true;

        [Header("Founder Species")]
        [Tooltip("Spawn every founder as an identical member of one species, so all diversity has to evolve. " +
                 "Disable for the original three-diet demo.")]
        public bool singleFounderSpecies = true;
        [Tooltip("0 = herbivore, 1 = carnivore. Meat-eating has to evolve from this starting diet.")]
        [Range(0f, 1f)] public float founderDietAffinity = 0.15f;

        [Header("Off-screen Population")]
        [Tooltip("Animals far from the camera become numbers per 250 m cell that grow and spread by the food " +
                 "their cells grow, and become animals again near the camera. Read when Play starts.")]
        public bool keepDistantAnimalsAsNumbers;

        AnimalTerrainWorld terrainWorld;
        OffscreenPopulationBridge offscreenPopulation;
        HabitatNavigation habitatNavigation;
        FoodSpawner foodSpawner;
        Transform animalParent;
        Transform foodParent;
        Transform templateParent;
        Material animalMaterial;
        Material foodMaterial;
        Coroutine ecosystemBuild;
        bool hasBuiltEcosystem;
        // Where the founders are placed: the simulation focus, or the best spot found near it.
        Vector3 founderCentre;

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

            // Navigation streams in tiles around the animals, so they can spread across the whole island.
            habitatNavigation = GetOrAddComponent<HabitatNavigation>();
            habitatNavigation.terrainWorld = terrainWorld;

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
            animalMaterial = CreatePlaceholderMaterial("Animal Material", Color.white, 0.28f);
            foodMaterial = CreatePlaceholderMaterial(
                "Plant Food Material", new Color(0.35f, 0.8f, 0.24f), 0.12f);

            // Reuse a Food Spawner already in the scene, so its tuning is kept.
            foodSpawner = FindAnyObjectByType<FoodSpawner>();
            if (foodSpawner == null) foodSpawner = gameObject.AddComponent<FoodSpawner>();
            foodSpawner.spawnedFoodParent = foodParent;
            foodSpawner.terrainGenerator = terrainGenerator;
            foodSpawner.terrainWorld = terrainWorld;
            foodSpawner.foodPrefab = CreateFoodTemplate();

            if (keepDistantAnimalsAsNumbers)
            {
                offscreenPopulation = GetOrAddComponent<OffscreenPopulationBridge>();
                offscreenPopulation.terrainWorld = terrainWorld;
                offscreenPopulation.habitatNavigation = habitatNavigation;
                offscreenPopulation.foodSpawner = foodSpawner;
                offscreenPopulation.templateParent = templateParent;
                offscreenPopulation.animalParent = animalParent;
            }

            GetOrAddComponent<EcosystemHud>();

            terrainGenerator.onTerrainGenerated += HandleTerrainGenerated;
        }

        void Start()
        {
            LimitWildlifeDrawDistance();

            // A SeedManager may already have generated terrain; if not, generate it here. Either way,
            // HandleTerrainGenerated builds the ecosystem for it.
            if (!terrainGenerator.IsGenerated)
            {
                GenerateTerrainIfNeeded();
            }
            else if (!hasBuiltEcosystem && ecosystemBuild == null)
            {
                HandleTerrainGenerated();
            }
        }

        // Animals and plants can live beyond the terrain's view distance, where they would float over ground
        // that is not drawn. The camera stops drawing them at the same distance as the terrain. Drawing only:
        // they keep living there.
        void LimitWildlifeDrawDistance()
        {
            Camera camera = Camera.main;
            int layer = WildlifeLayer.Index;
            LODInfo[] detailLevels = terrainGenerator.detailLevels;
            if (camera == null || layer < 0 || detailLevels == null || detailLevels.Length == 0) return;

            float[] distances = camera.layerCullDistances;
            distances[layer] = detailLevels[detailLevels.Length - 1].visibleDstThreshold;
            camera.layerCullDistances = distances;
            // Measure as a sphere, so turning the camera does not change what is drawn.
            camera.layerCullSpherical = true;
        }

        // Terrain is only drawn once something calls GenerateTerrain. SeedManager does that from its
        // Generate button, but scenes without one need it done here. Reusing the terrain asset's own seed
        // keeps the default world the same one the terrain team designed.
        void GenerateTerrainIfNeeded()
        {
            if (terrainGenerator.IsGenerated || terrainGenerator.heightMapSettings == null) return;
            if (terrainGenerator.viewer == null) terrainGenerator.viewer = simulationFocus;

            terrainGenerator.GenerateTerrain(terrainGenerator.heightMapSettings.noiseSettings.seed);
        }

        // Everything living on the old terrain is invalid once new terrain is generated, so rebuild the
        // navigation, food and animals from scratch.
        void HandleTerrainGenerated()
        {
            if (ecosystemBuild != null) StopCoroutine(ecosystemBuild);
            ecosystemBuild = StartCoroutine(BuildEcosystem());
        }

        IEnumerator BuildEcosystem()
        {
            // The first build keeps any animals placed in the scene by hand, as the demo always has.
            bool keepPlacedAnimals = !hasBuiltEcosystem;
            hasBuiltEcosystem = true;
            ClearEcosystem(keepPlacedAnimals);
            habitatNavigation.ResetWorld();
            // The terrain seed also seeds the plant layout, so a world seed always grows the same plants.
            foodSpawner.ResetWorld(terrainGenerator.heightMapSettings.noiseSettings.seed);
            if (offscreenPopulation != null) offscreenPopulation.ResetWorld();
            yield return null;

            // Only the founders' area is built up front; afterwards navigation grows with the animals.
            Vector3 center = ChooseFounderStart();
            float founderAreaRadius = animalSpawnRadius * 2f;
            habitatNavigation.RequestArea(center, founderAreaRadius);
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!habitatNavigation.IsAreaReady(center, founderAreaRadius))
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    Debug.LogError("No animals were created because no walkable NavMesh could be built around " +
                                   "the simulation focus on the generated terrain.", this);
                    ecosystemBuild = null;
                    yield break;
                }

                yield return null;
            }

            int existingAnimals = FindObjectsByType<SeekFood>().Length;
            int createdAnimals = existingAnimals == 0 ? SpawnFounderAnimals() : 0;
            Debug.Log(
                $"Ecosystem ready: {existingAnimals + createdAnimals} founder animals. Navigation and plants grow " +
                "outward as the animals spread, and plants also load wherever the camera looks.",
                this);
            ecosystemBuild = null;
        }

        void ClearEcosystem(bool keepPlacedAnimals)
        {
            // Plants belong to the Food Spawner, which clears them itself; carcasses are left to clear here.
            foreach (FoodItem food in new List<FoodItem>(FoodItem.ActiveItems))
            {
                if (food != null && food.foodType == FoodType.Meat) Destroy(food.gameObject);
            }

            if (keepPlacedAnimals) return;

            // Reset first, so the animals removed below are not recorded as deaths of the new world.
            if (SpeciesManager.Instance != null) SpeciesManager.Instance.ResetSimulation();
            foreach (SeekFood animal in FindObjectsByType<SeekFood>())
            {
                Destroy(animal.gameObject);
            }
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
            // The whole founder area is navigable before founders are placed.
            float fallbackRadius = animalSpawnRadius * 2f;

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
                Vector3 candidate = founderCentre + new Vector3(offset.x, 0f, offset.y);
                if (!habitatNavigation.TryProjectToNavigation(candidate, 40f, out Vector3 position)) continue;

                if (singleFounderSpecies)
                {
                    CreateFounder(created, "A", position, colors[0], founderDietAffinity);
                }
                else
                {
                    int variant = created % colors.Length;
                    CreateFounder(created, ((char)('A' + created)).ToString(), position,
                        colors[variant], diets[variant]);
                }
                created++;
            }

            if (created < founderAnimalCount)
            {
                Debug.LogWarning($"Placed {created} of {founderAnimalCount} requested founders. " +
                                 habitatNavigation.DescribeArea(founderCentre, fallbackRadius), this);
            }
            return created;
        }

        // The best place to start within Start Search Radius of the simulation focus: the candidate centre whose
        // spawn radius holds the most walkable land and plant food, with a small preference for staying near the
        // focus. On FOREST-001 the focus sits in a strait, which put the founders on two islands' shore lowlands.
        Vector3 ChooseFounderStart()
        {
            Vector3 focus = simulationFocus.position;
            founderCentre = focus;
            float search = Mathf.Max(0f, startSearchRadius);
            if (search <= 0f || terrainWorld == null || foodSpawner == null) return focus;

            float spacing = Mathf.Max(20f, animalSpawnRadius * 0.4f);
            int steps = Mathf.CeilToInt(search / spacing);
            float bestScore = ScoreStart(focus);
            Vector3 best = focus;
            for (int gx = -steps; gx <= steps; gx++)
            {
                for (int gz = -steps; gz <= steps; gz++)
                {
                    if (gx == 0 && gz == 0) continue;
                    Vector3 candidate = focus + new Vector3(gx * spacing, 0f, gz * spacing);
                    float distance = new Vector2(gx, gz).magnitude * spacing;
                    if (distance > search) continue;
                    // Nearer wins a tie, so good ground beside the focus beats slightly better ground far away.
                    float score = ScoreStart(candidate) * (1f - 0.25f * distance / search);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best != focus)
            {
                Debug.Log($"Founders start {Vector3.Distance(best, focus):0} m from the simulation focus, at " +
                          $"x {best.x:0} z {best.z:0}, where the spawn radius holds more walkable land and plant food.",
                          this);
                // The camera keeps its offset from the focus, so the founders are in view.
                Camera camera = Camera.main;
                if (camera != null) camera.transform.position += best - focus;
            }
            founderCentre = best;
            return best;
        }

        // Walkable land within the spawn radius of a centre, and the plant food it grows per minute by the food
        // spawner's own rules, sampled on three rings. Each walkable point counts a little even where nothing grows,
        // so the founders can at least move.
        float ScoreStart(Vector3 centre)
        {
            const int Rings = 3;
            const int PointsPerRing = 8;
            float total = 0f;
            for (int ring = 0; ring < Rings; ring++)
            {
                float radius = animalSpawnRadius * (ring + 0.5f) / Rings;
                for (int index = 0; index < PointsPerRing; index++)
                {
                    float angle = (index + 0.5f * ring) * Mathf.PI * 2f / PointsPerRing;
                    Vector3 point = centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    if (!terrainWorld.TryGetWalkableSample(point, out EnvironmentSample sample)) continue;
                    if (!TemperatureSystem.TryGetClimateTemperatureAt(point, out float celsius)) celsius = 2f;
                    total += 0.2f + foodSpawner.EstimateNutritionPerMinute(sample, celsius);
                }
            }
            return total;
        }

        // The founder is a plain "cell": a capsule lying on the ground with no body parts. Legs, necks, horns
        // and other parts grow on its descendants through mutation.
        void CreateFounder(int index, string speciesName, Vector3 position, Color color, float dietAffinity)
        {
            GameObject animal = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            animal.SetActive(false);
            animal.name = $"Capsule_Founder_{index + 1}";
            // Newborns and animals from off-screen populations are copies, so they inherit the layer.
            WildlifeLayer.Apply(animal);
            animal.transform.SetParent(animalParent, true);
            animal.transform.position = position;
            animal.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            animal.transform.localScale = new Vector3(2.2f, 2f, 2.2f);
            LayCapsuleOnGround(animal);
            animal.GetComponent<MeshRenderer>().sharedMaterial = animalMaterial;

            Collider bodyCollider = animal.GetComponent<Collider>();
            if (bodyCollider is CapsuleCollider capsuleCollider)
            {
                capsuleCollider.direction = 2;
                capsuleCollider.center = new Vector3(0f, 0.5f, 0f);
            }

            NavMeshAgent agent = animal.AddComponent<NavMeshAgent>();
            agent.agentTypeID = habitatNavigation.AgentTypeId;
            agent.radius = 1.1f;
            agent.height = 2f;
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
            behavior.speciesName = speciesName;
            behavior.speciesColor = color;
            behavior.dietAffinity = dietAffinity;
            behavior.currentEnergy = 105f;
            // A legless capsule crawls: two full-size leg pairs double this.
            behavior.moveSpeed = 6f;
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
            WildlifeLayer.Apply(food);
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
            // The founders' habitat rules hold the plant growth profile, so the Habitat Analysis window measures the
            // same plants the game grows.
            HabitatRequirements founders = HabitatRequirements.Founders;
            foodItem.minimumGrowthTemperature = founders.plantMinimumCelsius;
            foodItem.optimalGrowthTemperatureMin = founders.plantOptimalMinimumCelsius;
            foodItem.optimalGrowthTemperatureMax = founders.plantOptimalMaximumCelsius;
            foodItem.maximumGrowthTemperature = founders.plantMaximumCelsius;
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

        // Turns the capsule primitive to lie along the forward axis, with its underside at the pivot.
        void LayCapsuleOnGround(GameObject animal)
        {
            MeshFilter filter = animal.GetComponent<MeshFilter>();
            Mesh lyingMesh = Instantiate(filter.sharedMesh);
            lyingMesh.name = "Lying Capsule Body";
            Quaternion layDown = Quaternion.Euler(90f, 0f, 0f);
            Vector3 raise = new Vector3(0f, 0.5f, 0f);
            Vector3[] vertices = lyingMesh.vertices;
            Vector3[] normals = lyingMesh.normals;
            Vector4[] tangents = lyingMesh.tangents;
            for (int index = 0; index < vertices.Length; index++)
            {
                vertices[index] = layDown * vertices[index] + raise;
                if (index < normals.Length) normals[index] = layDown * normals[index];
                if (index < tangents.Length)
                {
                    Vector3 tangent = layDown * (Vector3)tangents[index];
                    tangents[index] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[index].w);
                }
            }

            lyingMesh.vertices = vertices;
            lyingMesh.normals = normals;
            if (tangents.Length > 0) lyingMesh.tangents = tangents;
            lyingMesh.RecalculateBounds();
            filter.sharedMesh = lyingMesh;
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
            if (terrainGenerator != null) terrainGenerator.onTerrainGenerated -= HandleTerrainGenerated;
            if (!Application.isPlaying) return;
            if (animalMaterial != null) Destroy(animalMaterial);
            if (foodMaterial != null) Destroy(foodMaterial);
        }
    }
}
