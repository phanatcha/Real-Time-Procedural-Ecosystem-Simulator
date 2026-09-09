using System;
using System.Collections.Generic;
using UnityEngine;

public class EcosystemSimulationController : MonoBehaviour
{
    [Header("Terrain environment source")]
    public TerrainGenerator terrainGenerator;
    public HeightMapSettings heightMapSettings;
    public MeshSettings meshSettings;
    public EnvironmentDefinitions environmentDefinitions;
    public VegetationSettings vegetationSettings;

    [Header("Ecosystem")]
    public EcosystemSimulationSettings simulationSettings;
    public EcosystemSpeciesDefinition[] speciesDefinitions;
    public Transform simulationFocus;
    public Transform materializedAnimalParent;
    public bool initializeOnStart = true;

    readonly List<EcosystemAnimalProxy> materializedAnimals = new List<EcosystemAnimalProxy>();

    TerrainEnvironmentSampler terrainSampler;
    System.Random materializationRandom;
    float materializationTimer;

    public EcosystemGrid Grid { get; private set; }
    public EcosystemPopulationSimulation PopulationSimulation { get; private set; }
    public bool IsInitialized => Grid != null && PopulationSimulation != null;
    public IReadOnlyList<EcosystemAnimalProxy> MaterializedAnimals => materializedAnimals;
    public event Action<EcosystemAnimalProxy> AnimalMaterialized;
    public event Action<EcosystemAnimalProxy> AnimalAbstracted;

    void Start()
    {
        if (initializeOnStart) Initialize();
    }

    void Update()
    {
        if (!IsInitialized) return;

        Vector2 focus = GetSimulationFocus();
        PopulationSimulation.Advance(Time.deltaTime, focus);
        materializationTimer -= Time.deltaTime;
        if (materializationTimer > 0f) return;

        materializationTimer = Mathf.Max(0.05f, simulationSettings.materializationRefreshInterval);
        RefreshMaterialization(focus);
    }

    public bool Initialize()
    {
        if (IsInitialized) ConvertAllAnimalsToPopulation();
        if (simulationSettings == null) return false;

        terrainSampler = ResolveTerrainSampler();
        if (terrainSampler == null || !terrainSampler.IsConfigured) return false;

        Grid = EcosystemGrid.Generate(terrainSampler, simulationSettings);
        PopulationSimulation = new EcosystemPopulationSimulation(
            Grid,
            simulationSettings,
            speciesDefinitions,
            true);
        materializationRandom = new System.Random(simulationSettings.materializationSeed);
        materializationTimer = 0f;
        return true;
    }

    public EcosystemSimulationStepStats AdvanceSimulation(float deltaTime)
    {
        if (!IsInitialized) return default;
        return PopulationSimulation.Advance(deltaTime, GetSimulationFocus());
    }

    public void RefreshMaterialization()
    {
        if (!IsInitialized) return;
        RefreshMaterialization(GetSimulationFocus());
    }

    public bool RegisterMaterializedAnimal(GameObject animal, string speciesId,
        float representedPopulation = 1f, bool removeFromAbstractPopulation = false)
    {
        if (!IsInitialized || animal == null || string.IsNullOrEmpty(speciesId)) return false;
        if (!Grid.TryGetCell(animal.transform.position, out EcosystemCell cell)) return false;

        float population = Mathf.Max(0.01f, representedPopulation);
        if (removeFromAbstractPopulation && cell.GetPopulation(speciesId) + 0.0001f < population) return false;

        EcosystemAnimalProxy proxy = animal.GetComponent<EcosystemAnimalProxy>();
        if (proxy == null) proxy = animal.AddComponent<EcosystemAnimalProxy>();
        if (materializedAnimals.Contains(proxy)) return true;

        if (removeFromAbstractPopulation) cell.AddPopulation(speciesId, -population);
        proxy.Initialize(this, speciesId, population, cell.Coordinate);
        materializedAnimals.Add(proxy);
        AnimalMaterialized?.Invoke(proxy);
        return true;
    }

    public bool AbstractAnimal(EcosystemAnimalProxy proxy)
    {
        if (proxy == null) return false;
        int index = materializedAnimals.IndexOf(proxy);
        if (index < 0) return false;

        EcosystemCell destination = null;
        Grid.TryGetCell(proxy.transform.position, out destination);
        if (destination == null && proxy.HasLastHabitableCell)
        {
            Grid.TryGetCell(proxy.LastHabitableCell, out destination);
        }
        if (destination == null && !Grid.TryGetNearestCell(
            new Vector2(proxy.transform.position.x, proxy.transform.position.z), out destination))
        {
            return false;
        }

        destination.AddPopulation(proxy.SpeciesId, proxy.RepresentedPopulation);
        materializedAnimals.RemoveAt(index);
        MonoBehaviour[] behaviours = proxy.GetComponents<MonoBehaviour>();
        for (int behaviourIndex = 0; behaviourIndex < behaviours.Length; behaviourIndex++)
        {
            if (behaviours[behaviourIndex] is IEcosystemMaterializationLifecycle lifecycle)
            {
                lifecycle.PrepareForAbstraction();
            }
        }
        proxy.Detach(true);
        AnimalAbstracted?.Invoke(proxy);
        DestroyManagedObject(proxy.gameObject);
        return true;
    }

    public bool ReportAnimalDeath(EcosystemAnimalProxy proxy)
    {
        if (proxy == null) return false;
        int index = materializedAnimals.IndexOf(proxy);
        if (index < 0) return false;

        materializedAnimals.RemoveAt(index);
        proxy.Detach(false);
        DestroyManagedObject(proxy.gameObject);
        return true;
    }

    public void ConvertAllAnimalsToPopulation()
    {
        for (int i = materializedAnimals.Count - 1; i >= 0; i--)
        {
            EcosystemAnimalProxy proxy = materializedAnimals[i];
            if (proxy == null)
            {
                materializedAnimals.RemoveAt(i);
                continue;
            }

            AbstractAnimal(proxy);
        }
    }

    public float GetTotalPopulation(string speciesId = null)
    {
        float total = PopulationSimulation == null
            ? 0f
            : PopulationSimulation.GetTotalAbstractPopulation(speciesId);

        for (int i = 0; i < materializedAnimals.Count; i++)
        {
            EcosystemAnimalProxy proxy = materializedAnimals[i];
            if (proxy == null) continue;
            if (!string.IsNullOrEmpty(speciesId) && proxy.SpeciesId != speciesId) continue;
            total += proxy.RepresentedPopulation;
        }

        return total;
    }

    void RefreshMaterialization(Vector2 focus)
    {
        float abstractionRadiusSquared = simulationSettings.abstractionRadius * simulationSettings.abstractionRadius;

        for (int i = materializedAnimals.Count - 1; i >= 0; i--)
        {
            EcosystemAnimalProxy proxy = materializedAnimals[i];
            if (proxy == null)
            {
                materializedAnimals.RemoveAt(i);
                continue;
            }

            Vector2 position = new Vector2(proxy.transform.position.x, proxy.transform.position.z);
            if (Grid.TryGetCell(position, out EcosystemCell currentCell))
            {
                proxy.SetLastHabitableCell(currentCell.Coordinate);
            }

            if ((position - focus).sqrMagnitude > abstractionRadiusSquared)
            {
                AbstractAnimal(proxy);
            }
        }

        if (speciesDefinitions == null || speciesDefinitions.Length == 0) return;

        float materializationRadiusSquared = simulationSettings.materializationRadius * simulationSettings.materializationRadius;
        foreach (EcosystemCell cell in Grid.Cells)
        {
            if ((cell.WorldCentre - focus).sqrMagnitude > materializationRadiusSquared) continue;

            for (int speciesIndex = 0; speciesIndex < speciesDefinitions.Length; speciesIndex++)
            {
                EcosystemSpeciesDefinition species = speciesDefinitions[speciesIndex];
                if (species == null || species.prefab == null || species.maximumMaterializedPerCell <= 0) continue;
                if (species.GetHabitatSuitability(cell.Environment) <= 0f) continue;

                int currentCount = CountMaterializedAnimals(cell.Coordinate, species.Id);
                float representedPopulation = Mathf.Max(0.01f, species.populationPerGameObject);
                while (currentCount < species.maximumMaterializedPerCell
                    && cell.GetPopulation(species.Id) + 0.0001f >= representedPopulation)
                {
                    if (!TryMaterializeAnimal(cell, species, representedPopulation, focus)) break;
                    currentCount++;
                }
            }
        }
    }

    bool TryMaterializeAnimal(EcosystemCell cell, EcosystemSpeciesDefinition species,
        float representedPopulation, Vector2 focus)
    {
        int attempts = Mathf.Max(1, simulationSettings.spawnAttemptsPerAnimal);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Vector2 position = new Vector2(
                Mathf.Lerp(cell.WorldBounds.xMin, cell.WorldBounds.xMax, (float)materializationRandom.NextDouble()),
                Mathf.Lerp(cell.WorldBounds.yMin, cell.WorldBounds.yMax, (float)materializationRandom.NextDouble()));
            if (TryMaterializeAnimalAt(cell, species, representedPopulation, focus, position)) return true;
        }

        int sampleAxis = Mathf.Clamp(simulationSettings.environmentSamplesPerAxis, 1, 5);
        for (int sampleY = 0; sampleY < sampleAxis; sampleY++)
        {
            for (int sampleX = 0; sampleX < sampleAxis; sampleX++)
            {
                Vector2 position = new Vector2(
                    Mathf.Lerp(cell.WorldBounds.xMin, cell.WorldBounds.xMax, (sampleX + 0.5f) / sampleAxis),
                    Mathf.Lerp(cell.WorldBounds.yMin, cell.WorldBounds.yMax, (sampleY + 0.5f) / sampleAxis));
                if (TryMaterializeAnimalAt(cell, species, representedPopulation, focus, position)) return true;
            }
        }

        return false;
    }

    bool TryMaterializeAnimalAt(EcosystemCell cell, EcosystemSpeciesDefinition species,
        float representedPopulation, Vector2 focus, Vector2 position)
    {
        float materializationRadiusSquared = simulationSettings.materializationRadius * simulationSettings.materializationRadius;
        if ((position - focus).sqrMagnitude > materializationRadiusSquared) return false;
        if (!terrainSampler.TrySample(position, out EnvironmentSample environment)) return false;
        if (species.GetPointSuitability(environment) <= 0f) return false;

        Quaternion rotation = Quaternion.Euler(0f, (float)materializationRandom.NextDouble() * 360f, 0f);
        Vector3 worldPosition = new Vector3(
            position.x,
            environment.height + species.spawnHeightOffset,
            position.y);
        GameObject animal = Instantiate(species.prefab, worldPosition, rotation, materializedAnimalParent);
        EcosystemAnimalProxy proxy = animal.GetComponent<EcosystemAnimalProxy>();
        if (proxy == null) proxy = animal.AddComponent<EcosystemAnimalProxy>();

        cell.AddPopulation(species.Id, -representedPopulation);
        proxy.Initialize(this, species.Id, representedPopulation, cell.Coordinate);
        materializedAnimals.Add(proxy);
        if (!animal.activeSelf) animal.SetActive(true);
        AnimalMaterialized?.Invoke(proxy);
        return true;
    }

    int CountMaterializedAnimals(Vector2Int cellCoordinate, string speciesId)
    {
        int count = 0;
        for (int i = 0; i < materializedAnimals.Count; i++)
        {
            EcosystemAnimalProxy proxy = materializedAnimals[i];
            if (proxy == null) continue;
            if (proxy.SpeciesId == speciesId && proxy.HasLastHabitableCell
                && proxy.LastHabitableCell == cellCoordinate)
            {
                count++;
            }
        }

        return count;
    }

    TerrainEnvironmentSampler ResolveTerrainSampler()
    {
        if (terrainGenerator != null) return terrainGenerator.WorldEnvironmentSampler;
        return new TerrainEnvironmentSampler(
            heightMapSettings,
            meshSettings,
            environmentDefinitions,
            vegetationSettings,
            256);
    }

    Vector2 GetSimulationFocus()
    {
        Transform focus = simulationFocus == null ? transform : simulationFocus;
        return new Vector2(focus.position.x, focus.position.z);
    }

    static void DestroyManagedObject(GameObject target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    void OnDisable()
    {
        if (Application.isPlaying && IsInitialized) ConvertAllAnimalsToPopulation();
    }

    void OnDestroy()
    {
        if (Application.isPlaying && IsInitialized) ConvertAllAnimalsToPopulation();
    }
}
