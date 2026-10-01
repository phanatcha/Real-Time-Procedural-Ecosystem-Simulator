using System.Collections.Generic;
using UnityEngine;

// Keeps animals far from the camera as numbers instead of GameObjects, so the island can hold far more
// animals than the live simulation can afford. Animals beyond the abstraction radius join their 250 m
// cell's population. Near the camera, populations become animals again, each carrying a real genome from
// the population. Off-screen, populations grow and spread by OffscreenPopulationStore's food rules.
// AnimalTerrainDemoBootstrap adds this when "Keep Distant Animals As Numbers" is on.
[DisallowMultipleComponent]
public sealed class OffscreenPopulationBridge : MonoBehaviour, IOffscreenPopulation
{
    public AnimalTerrainWorld terrainWorld;
    public HabitatNavigation habitatNavigation;
    public FoodSpawner foodSpawner;
    [Tooltip("An inactive parent to keep the animal template under, so the template never runs.")]
    public Transform templateParent;
    public Transform animalParent;

    [Header("Swapping")]
    [Min(25f)] public float cellSize = 250f;
    [Tooltip("Populations within this distance of the camera become animals.")]
    [Min(10f)] public float materializationRadius = 400f;
    [Tooltip("Animals beyond this distance from the camera become numbers. Keep it above the " +
             "materialization radius, so animals near the edge do not switch back and forth.")]
    [Min(10f)] public float abstractionRadius = 550f;
    [Tooltip("Live animals per cell and species; the rest of a nearby population stays as numbers.")]
    [Min(1)] public int maximumLivePerCellAndSpecies = 12;
    [Min(1)] public int maximumAnimalsCreatedPerRefresh = 8;
    [Tooltip("Real seconds between swaps.")]
    [Min(0.05f)] public float refreshInterval = 0.5f;

    [Header("Off-screen populations")]
    [Tooltip("Real genomes kept per population to stand for the rest.")]
    [Min(1)] public int samplesPerPopulation = 8;
    [Range(0f, 1f)] public float migrationRatePerSecond = 0.004f;
    [Min(0f)] public float nearbyRadius = 750f;
    [Min(0.05f)] public float nearbyUpdateInterval = 0.5f;
    [Min(0.1f)] public float distantUpdateInterval = 6f;
    [Tooltip("Plant values are copied from the Food Spawner, and animal values from the first animal to " +
             "leave the camera's area, so they match the live simulation. Harvest efficiency is set here.")]
    public OffscreenEcologyRules ecology = OffscreenEcologyRules.Defaults;
    [Tooltip("Terrain samples per cell side when measuring each cell's plant food and temperature.")]
    [Range(2, 16)] public int surveySamplesPerCellAxis = 8;

    [Header("Diagnostics")]
    [SerializeField] private int surveyedCells;
    [SerializeField] private int offscreenAnimals;
    [SerializeField] private int animalsAbstracted;
    [SerializeField] private int animalsMaterialized;

    sealed class SurveyedCell
    {
        public Vector2Int coordinate;
        public float plantEnergyPerSecond;
        public float celsius;
    }

    sealed class SurveyResult
    {
        public int version;
        public readonly List<SurveyedCell> cells = new List<SurveyedCell>();
    }

    readonly Dictionary<string, int> roundedCounts = new Dictionary<string, int>();
    readonly Dictionary<Vector2Int, int> liveByCell = new Dictionary<Vector2Int, int>();
    readonly Dictionary<(Vector2Int cell, string species), int> liveByCellAndSpecies =
        new Dictionary<(Vector2Int cell, string species), int>();
    readonly List<SeekFood> agents = new List<SeekFood>();
    readonly List<string> extinctSpecies = new List<string>();

    OffscreenPopulationStore store;
    GameObject template;
    int worldVersion;
    float refreshTimer;

    public bool IsReady => store != null;
    public int TotalOffscreen => offscreenAnimals;
    public int CountOffscreen(string speciesName) => roundedCounts.TryGetValue(speciesName, out int count) ? count : 0;
    public IEnumerable<string> OffscreenSpecies => roundedCounts.Keys;
    public OffscreenPopulationStore Store => store;

    void OnEnable()
    {
        RegisterWithSpeciesManager();
    }

    void OnDisable()
    {
        if (SpeciesManager.Instance != null && ReferenceEquals(SpeciesManager.Instance.OffscreenPopulation, this))
        {
            SpeciesManager.Instance.OffscreenPopulation = null;
        }
    }

    // Starts over for newly generated terrain: forgets every population and measures the new cells. Until
    // the measurement finishes, animals stay live everywhere.
    public void ResetWorld()
    {
        worldVersion++;
        store = null;
        roundedCounts.Clear();
        liveByCell.Clear();
        liveByCellAndSpecies.Clear();
        offscreenAnimals = 0;
        surveyedCells = 0;

        TerrainEnvironmentSampler liveSampler = terrainWorld != null ? terrainWorld.Sampler : null;
        if (liveSampler == null || !liveSampler.IsConfigured)
        {
            Debug.LogWarning("Off-screen populations are off: the terrain sampler is not ready.", this);
            return;
        }

        CopyPlantRules();
        // A sampler of its own, so the survey does not push the live game's terrain out of its cache.
        TerrainEnvironmentSampler sampler = new TerrainEnvironmentSampler(liveSampler.HeightMapSettings,
            liveSampler.MeshSettings, liveSampler.EnvironmentDefinitions, liveSampler.VegetationSettings);
        HabitatRequirements ground = GroundRules();
        OffscreenEcologyRules rules = ecology;
        int version = worldVersion;
        float extent = terrainWorld.HabitableExtent;
        float size = cellSize;
        int axis = surveySamplesPerCellAxis;
        ThreadedDataRequester.RequestData(
            () => SurveyCells(version, sampler, ground, rules, extent, size, axis),
            OnCellsSurveyed);
    }

    void Update()
    {
        if (store == null) return;

        RegisterWithSpeciesManager();
        Vector2 focus = GetFocus();
        store.Advance(Time.deltaTime, focus);

        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer > 0f) return;

        refreshTimer = refreshInterval;
        AbstractDistantAnimals(focus);
        CountLiveAnimals();
        MaterializeNearbyPopulations(focus);
        RefreshCounts();
    }

    void AbstractDistantAnimals(Vector2 focus)
    {
        SpeciesManager manager = SpeciesManager.Instance;
        if (manager == null) return;

        agents.Clear();
        agents.AddRange(manager.ActiveAgents);
        float radiusSquared = abstractionRadius * abstractionRadius;
        foreach (SeekFood animal in agents)
        {
            if (animal == null || !animal.IsAlive || animal.Genome == null || !animal.Genome.IsValid) continue;
            // The populations live on land, so an animal at sea stays a real animal rather than reappearing on
            // an island later.
            if (animal.IsInWater) continue;

            Vector2 position = new Vector2(animal.transform.position.x, animal.transform.position.z);
            if ((position - focus).sqrMagnitude <= radiusSquared) continue;
            if (!store.TryGetNearestCell(position, out OffscreenCell cell)) continue;

            CaptureTemplate(animal);
            AnimalTemperature thermal = animal.GetComponent<AnimalTemperature>();
            float thermalMultiplier = thermal != null ? Mathf.Max(0.01f, thermal.EnergyDrainMultiplier) : 1f;
            store.AddAnimal(cell, animal.speciesName, animal.speciesColor,
                new OffscreenSample(animal.Genome.Clone(), animal.CurrentEnergyDrainPerSecond / thermalMultiplier,
                                    animal.Generation));

            // Leaves without being recorded as a death; the population now counts it.
            animal.PrepareForAbstraction();
            Destroy(animal.gameObject);
            animalsAbstracted++;
        }
    }

    void MaterializeNearbyPopulations(Vector2 focus)
    {
        SpeciesManager manager = SpeciesManager.Instance;
        if (template == null || manager == null) return;

        int created = 0;
        float radiusSquared = materializationRadius * materializationRadius;
        foreach (OffscreenCell cell in store.Cells)
        {
            if (cell.populations.Count == 0 || DistanceSquaredToRect(focus, cell.bounds) > radiusSquared) continue;

            bool requestedNavigation = false;
            for (int index = cell.populations.Count - 1; index >= 0; index--)
            {
                OffscreenPopulation population = cell.populations[index];
                (Vector2Int, string) key = (cell.coordinate, population.speciesName);
                liveByCellAndSpecies.TryGetValue(key, out int live);
                // New animals only count toward the ceiling from their first frame, so count them here too. They
                // appear on land, so the land's ceiling is the one that matters.
                while (population.count >= 1f && live < maximumLivePerCellAndSpecies &&
                       created < maximumAnimalsCreatedPerRefresh && !manager.IsAtPopulationCeiling &&
                       (manager.populationCeiling <= 0 || manager.LandPopulation + created < manager.populationCeiling))
                {
                    if (!TryFindSpawnPoint(cell, focus, out Vector3 position))
                    {
                        // Navigation only streams in around animals, so ask for it here and try again later.
                        if (!requestedNavigation && habitatNavigation != null)
                        {
                            Vector2 centre = ClosestPointInRect(focus, cell.bounds);
                            habitatNavigation.RequestArea(new Vector3(centre.x, 0f, centre.y), cellSize * 0.5f);
                            requestedNavigation = true;
                        }
                        break;
                    }

                    if (!store.TryTakeAnimal(cell, population, out OffscreenSample sample)) break;

                    Spawn(sample, population, position);
                    live++;
                    created++;
                }

                liveByCellAndSpecies[key] = live;
            }
        }
    }

    void Spawn(OffscreenSample sample, OffscreenPopulation population, Vector3 position)
    {
        GameObject animal = Instantiate(template, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), animalParent);
        SeekFood behaviour = animal.GetComponent<SeekFood>();
        // Off-screen animals are of every age, and a population near its food limit is not well fed.
        float age = Random.Range(0f, 0.6f) * sample.genome[AnimalGene.MaxLifespan];
        behaviour.PrepareFromOffscreenPopulation(sample.genome, population.speciesName, population.color,
                                                 sample.generation, age, 0.7f);
        animalsMaterialized++;
    }

    bool TryFindSpawnPoint(OffscreenCell cell, Vector2 focus, out Vector3 position)
    {
        position = default;
        if (habitatNavigation == null) return false;

        for (int attempt = 0; attempt < 6; attempt++)
        {
            Vector2 point = new Vector2(Random.Range(cell.bounds.xMin, cell.bounds.xMax),
                                        Random.Range(cell.bounds.yMin, cell.bounds.yMax));
            // Pull points outside the radius back toward the camera, staying inside the cell.
            Vector2 offset = point - focus;
            float limit = materializationRadius * 0.95f;
            if (offset.sqrMagnitude > limit * limit) point = ClosestPointInRect(focus + offset.normalized * limit, cell.bounds);

            if (habitatNavigation.TryProjectToNavigation(new Vector3(point.x, 0f, point.y), 20f, out position) &&
                (new Vector2(position.x, position.z) - focus).sqrMagnitude <= materializationRadius * materializationRadius)
            {
                return true;
            }
        }

        return false;
    }

    void CountLiveAnimals()
    {
        liveByCell.Clear();
        liveByCellAndSpecies.Clear();
        SpeciesManager manager = SpeciesManager.Instance;
        if (manager == null) return;

        foreach (SeekFood animal in manager.ActiveAgents)
        {
            if (animal == null) continue;

            Vector2Int cell = store.WorldToCell(new Vector2(animal.transform.position.x, animal.transform.position.z));
            liveByCell.TryGetValue(cell, out int inCell);
            liveByCell[cell] = inCell + 1;
            (Vector2Int, string) key = (cell, animal.speciesName);
            liveByCellAndSpecies.TryGetValue(key, out int ofSpecies);
            liveByCellAndSpecies[key] = ofSpecies + 1;
        }
    }

    // Rounds the populations into whole animals and records species whose last animals have died out.
    void RefreshCounts()
    {
        SpeciesManager manager = SpeciesManager.Instance;
        roundedCounts.Clear();
        extinctSpecies.Clear();
        offscreenAnimals = 0;
        foreach (KeyValuePair<string, float> species in store.CountBySpecies())
        {
            int count = Mathf.RoundToInt(species.Value);
            if (count > 0)
            {
                roundedCounts[species.Key] = count;
                offscreenAnimals += count;
            }
            else if (manager == null || !manager.SpeciesPopulation.ContainsKey(species.Key))
            {
                extinctSpecies.Add(species.Key);
            }
        }

        foreach (string speciesName in extinctSpecies)
        {
            store.RemoveSpecies(speciesName);
            if (manager != null) manager.RecordOffscreenExtinction(speciesName);
        }
    }

    // The first animal to leave becomes the template for animals created later. The copy keeps its
    // components, tuning and metabolic reference (the founders'), which newborns also inherit by copying.
    void CaptureTemplate(SeekFood animal)
    {
        if (template != null) return;

        template = Instantiate(animal.gameObject, templateParent);
        template.name = "Offscreen_Animal_Template";

        SeekFood behaviour = template.GetComponent<SeekFood>();
        ecology.reproductionCooldown = behaviour.reproductionCooldown;
        ecology.minimumDietEfficiency = behaviour.minimumDietEfficiency;
        AnimalTemperature thermal = template.GetComponent<AnimalTemperature>();
        if (thermal != null)
        {
            ecology.degreesPerStressUnit = thermal.degreesPerStressUnit;
            ecology.extraEnergyDrainPerStressUnit = thermal.extraEnergyDrainPerStressUnit;
            ecology.maximumStress = thermal.maximumStress;
        }

        if (store != null) store.rules = ecology;
    }

    void CopyPlantRules()
    {
        if (foodSpawner == null) return;

        ecology.plantSiteSpacing = foodSpawner.siteSpacing;
        ecology.minimumGrassBiomass = foodSpawner.minimumGrassBiomass;
        ecology.minimumPlantGrowthRate = foodSpawner.minimumGrowthRate;
        ecology.plantNutrition = foodSpawner.baseNutrition;
        ecology.moistureNutritionMultiplier = foodSpawner.moistureNutritionMultiplier;
        ecology.plantRegrowSeconds = foodSpawner.baseRegrowSeconds;
    }

    // Walkable ground, plant growth and temperature as the live simulation defines them.
    HabitatRequirements GroundRules()
    {
        HabitatRequirements rules = HabitatRequirements.Founders;
        rules.maximumSlopeDegrees = terrainWorld.maximumWalkableSlopeDegrees;
        rules.excludeShore = terrainWorld.excludeShore;
        rules.boundaryInset = terrainWorld.boundaryInset;

        FoodItem plant = foodSpawner != null && foodSpawner.foodPrefab != null
            ? foodSpawner.foodPrefab.GetComponent<FoodItem>()
            : null;
        if (plant != null)
        {
            rules.plantMinimumCelsius = plant.minimumGrowthTemperature;
            rules.plantOptimalMinimumCelsius = plant.optimalGrowthTemperatureMin;
            rules.plantOptimalMaximumCelsius = plant.optimalGrowthTemperatureMax;
            rules.plantMaximumCelsius = plant.maximumGrowthTemperature;
        }

        ProceduralTerrainTemperatureProvider temperature = FindAnyObjectByType<ProceduralTerrainTemperatureProvider>();
        if (temperature != null)
        {
            rules.coldestCelsius = temperature.coldestTemperatureCelsius;
            rules.warmestCelsius = temperature.warmestTemperatureCelsius;
        }

        return rules;
    }

    // Runs on a background thread: measures each cell's plant energy and temperature on its walkable ground.
    static SurveyResult SurveyCells(int version, TerrainEnvironmentSampler sampler, HabitatRequirements ground,
        OffscreenEcologyRules rules, float extent, float size, int axis)
    {
        SurveyResult result = new SurveyResult { version = version };
        int first = Mathf.FloorToInt(-extent / size);
        int last = Mathf.FloorToInt((extent - 0.001f) / size);
        float step = size / axis;
        float sampleArea = step * step;
        for (int cellZ = first; cellZ <= last; cellZ++)
        {
            for (int cellX = first; cellX <= last; cellX++)
            {
                float plantEnergy = 0f;
                float celsiusTotal = 0f;
                int walkable = 0;
                for (int sampleZ = 0; sampleZ < axis; sampleZ++)
                {
                    for (int sampleX = 0; sampleX < axis; sampleX++)
                    {
                        Vector2 position = new Vector2((cellX * axis + sampleX + 0.5f) * step,
                                                       (cellZ * axis + sampleZ + 0.5f) * step);
                        if (Mathf.Abs(position.x) > extent || Mathf.Abs(position.y) > extent) continue;
                        if (!sampler.TrySample(position, out EnvironmentSample sample) || !ground.IsWalkable(sample)) continue;

                        float celsius = ground.ToCelsius(sample);
                        walkable++;
                        celsiusTotal += celsius;
                        plantEnergy += rules.PlantEnergyPerSecond(sampleArea, sample.grassBiomass, sample.moisture,
                                                                  ground.PlantGrowthRate(celsius));
                    }
                }

                if (walkable == 0) continue;
                result.cells.Add(new SurveyedCell
                {
                    coordinate = new Vector2Int(cellX, cellZ),
                    plantEnergyPerSecond = plantEnergy,
                    celsius = celsiusTotal / walkable
                });
            }
        }

        return result;
    }

    void OnCellsSurveyed(object data)
    {
        SurveyResult result = (SurveyResult)data;
        if (result.version != worldVersion) return;

        OffscreenPopulationStore surveyed = new OffscreenPopulationStore(cellSize, ecology, new System.Random())
        {
            samplesPerPopulation = samplesPerPopulation,
            migrationRatePerSecond = migrationRatePerSecond,
            nearbyRadius = nearbyRadius,
            nearbyUpdateInterval = nearbyUpdateInterval,
            distantUpdateInterval = distantUpdateInterval,
            LiveAnimals = cell => liveByCell.TryGetValue(cell, out int count) ? count : 0
        };
        foreach (SurveyedCell cell in result.cells)
        {
            surveyed.AddCell(cell.coordinate, cell.plantEnergyPerSecond, cell.celsius);
        }

        store = surveyed;
        surveyedCells = result.cells.Count;
        refreshTimer = 0f;
    }

    void RegisterWithSpeciesManager()
    {
        SpeciesManager manager = SpeciesManager.Instance;
        if (manager != null && manager.OffscreenPopulation == null) manager.OffscreenPopulation = this;
    }

    Vector2 GetFocus()
    {
        Transform focus = terrainWorld != null && terrainWorld.terrainGenerator != null &&
                          terrainWorld.terrainGenerator.viewer != null
            ? terrainWorld.terrainGenerator.viewer
            : transform;
        return new Vector2(focus.position.x, focus.position.z);
    }

    static Vector2 ClosestPointInRect(Vector2 point, Rect rect)
    {
        return new Vector2(Mathf.Clamp(point.x, rect.xMin, rect.xMax), Mathf.Clamp(point.y, rect.yMin, rect.yMax));
    }

    static float DistanceSquaredToRect(Vector2 point, Rect rect)
    {
        return (ClosestPointInRect(point, rect) - point).sqrMagnitude;
    }
}
