using System;
using System.Collections.Generic;
using UnityEngine;

public struct EcosystemSimulationStepStats
{
    public int nearbyCellsUpdated;
    public int distantCellsUpdated;
    public int migrationTransfers;
    public float migratedPopulation;
    // Net births minus deaths across the updated cells.
    public float populationChange;
}

public sealed class EcosystemPopulationSimulation
{
    struct PopulationTransfer
    {
        public EcosystemCell source;
        public EcosystemCell destination;
        public string speciesId;
        public float count;
    }

    readonly EcosystemGrid grid;
    readonly EcosystemSimulationSettings settings;
    readonly Dictionary<string, EcosystemSpeciesDefinition> speciesById = new Dictionary<string, EcosystemSpeciesDefinition>();
    readonly List<PopulationTransfer> pendingTransfers = new List<PopulationTransfer>();
    readonly List<string> growingSpecies = new List<string>();

    public EcosystemGrid Grid => grid;
    public EcosystemSimulationStepStats LastStepStats { get; private set; }
    public event Action<EcosystemCell, float> CellUpdated;

    // Materialized animals of a species standing in a cell. They take up part of the cell's capacity,
    // so the abstract population there only grows into the room they leave.
    public Func<EcosystemCell, string, float> OccupiedPopulation { get; set; }

    public EcosystemPopulationSimulation(EcosystemGrid grid, EcosystemSimulationSettings settings,
        IEnumerable<EcosystemSpeciesDefinition> speciesDefinitions = null, bool seedPopulations = true)
    {
        this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

        if (speciesDefinitions != null)
        {
            foreach (EcosystemSpeciesDefinition species in speciesDefinitions)
            {
                if (species == null || string.IsNullOrEmpty(species.Id)) continue;
                speciesById[species.Id] = species;
            }
        }

        if (seedPopulations) SeedPopulations();
    }

    public void SeedPopulations()
    {
        foreach (EcosystemCell cell in grid.Cells)
        {
            foreach (EcosystemSpeciesDefinition species in speciesById.Values)
            {
                if (!species.seedPopulation || cell.GetPopulation(species.Id) > 0f) continue;

                float capacity = species.GetCarryingCapacity(cell.Environment);
                if (capacity <= 0f) continue;
                float variation = Mathf.Lerp(0.85f, 1.15f, StableUnitValue(cell.Coordinate, species.Id));
                float initialCount = Mathf.Min(
                    capacity,
                    capacity * species.initialPopulationFraction * variation);
                cell.SetPopulation(species.Id, initialCount);
            }
        }
    }

    public EcosystemSimulationStepStats Advance(float deltaTime, Vector2 simulationFocus)
    {
        EcosystemSimulationStepStats stats = default;
        if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
        {
            LastStepStats = stats;
            return stats;
        }

        pendingTransfers.Clear();
        float nearbyRadiusSquared = settings.nearbySimulationRadius * settings.nearbySimulationRadius;

        foreach (EcosystemCell cell in grid.Cells)
        {
            bool isNearby = (cell.WorldCentre - simulationFocus).sqrMagnitude <= nearbyRadiusSquared;
            float interval = isNearby
                ? Mathf.Max(0.05f, settings.nearbyUpdateInterval)
                : Mathf.Max(settings.nearbyUpdateInterval, settings.distantUpdateInterval);
            cell.UpdateAccumulator += deltaTime;
            if (cell.UpdateAccumulator + 0.0001f < interval) continue;

            float elapsed = cell.UpdateAccumulator;
            cell.UpdateAccumulator = 0f;
            cell.UpdateCount++;
            cell.LastUpdateDuration = elapsed;
            CellUpdated?.Invoke(cell, elapsed);
            stats.populationChange += GrowPopulations(cell, elapsed);
            GatherMigration(cell, elapsed);

            if (isNearby) stats.nearbyCellsUpdated++;
            else stats.distantCellsUpdated++;
        }

        for (int i = 0; i < pendingTransfers.Count; i++)
        {
            PopulationTransfer transfer = pendingTransfers[i];
            transfer.source.AddPopulation(transfer.speciesId, -transfer.count);
            transfer.destination.AddPopulation(transfer.speciesId, transfer.count);
            stats.migrationTransfers++;
            stats.migratedPopulation += transfer.count;
        }

        LastStepStats = stats;
        return stats;
    }

    public EcosystemSpeciesDefinition GetSpecies(string speciesId)
    {
        if (string.IsNullOrEmpty(speciesId)) return null;
        return speciesById.TryGetValue(speciesId, out EcosystemSpeciesDefinition species) ? species : null;
    }

    public float GetTotalAbstractPopulation(string speciesId = null)
    {
        float total = 0f;
        foreach (EcosystemCell cell in grid.Cells)
        {
            total += string.IsNullOrEmpty(speciesId)
                ? cell.TotalAbstractPopulation
                : cell.GetPopulation(speciesId);
        }

        return total;
    }

    // Logistic growth toward a capacity, exact for any time step, so a distant cell updated every few
    // seconds ends where it would after many short steps:
    //     N(t) = K / (1 + (K − N) / N · e^(−r·t))
    // Small populations grow by about r per second, slowing to zero at K; populations above K fall back
    // to it. Where the capacity is zero or less, the population instead dies out at the death rate.
    public static float GrowLogistic(float population, float capacity, float growthRate, float deathRate,
        float elapsed)
    {
        if (population <= 0f || elapsed <= 0f) return Mathf.Max(0f, population);
        if (capacity <= 0f) return population * Mathf.Exp(-Mathf.Max(0f, deathRate) * elapsed);
        if (growthRate <= 0f) return population;

        return capacity / (1f + (capacity - population) / population * Mathf.Exp(-growthRate * elapsed));
    }

    // Species without a definition keep their count; they only migrate.
    float GrowPopulations(EcosystemCell cell, float elapsed)
    {
        if (cell.Populations.Count == 0) return 0f;

        float change = 0f;
        growingSpecies.Clear();
        growingSpecies.AddRange(cell.Populations.Keys);
        for (int i = 0; i < growingSpecies.Count; i++)
        {
            string speciesId = growingSpecies[i];
            EcosystemSpeciesDefinition species = GetSpecies(speciesId);
            if (species == null) continue;

            float occupied = OccupiedPopulation == null ? 0f : Mathf.Max(0f, OccupiedPopulation(cell, speciesId));
            float capacity = species.GetCarryingCapacity(cell.Environment) - occupied;
            float before = cell.GetPopulation(speciesId);
            cell.SetPopulation(speciesId, GrowLogistic(
                before, capacity, species.growthRatePerSecond, species.deathRatePerSecond, elapsed));
            change += cell.GetPopulation(speciesId) - before;
        }

        return change;
    }

    void GatherMigration(EcosystemCell source, float elapsed)
    {
        if (source.Populations.Count == 0) return;

        List<KeyValuePair<string, float>> populations = new List<KeyValuePair<string, float>>(source.Populations);
        List<EcosystemCell> neighbors = new List<EcosystemCell>(grid.GetNeighbors(source.Coordinate));
        if (neighbors.Count == 0) return;

        foreach (KeyValuePair<string, float> population in populations)
        {
            if (population.Value <= 0f) continue;

            EcosystemSpeciesDefinition species = GetSpecies(population.Key);
            float migrationRate = species == null
                ? settings.defaultMigrationRatePerSecond
                : species.GetMigrationRate(settings.defaultMigrationRatePerSecond);
            float migratingCount = population.Value * (1f - Mathf.Exp(-Mathf.Max(0f, migrationRate) * elapsed));
            if (migratingCount <= 0.0001f) continue;

            float[] weights = new float[neighbors.Count];
            float totalWeight = 0f;
            for (int i = 0; i < neighbors.Count; i++)
            {
                EcosystemCell destination = neighbors[i];
                float suitability = species == null ? 1f : species.GetHabitatSuitability(destination.Environment);
                if (suitability <= 0f) continue;

                float capacityFactor = 1f;
                if (species != null)
                {
                    float capacity = species.GetCarryingCapacity(destination.Environment);
                    if (capacity <= 0f) continue;
                    float remainingCapacity = Mathf.Max(0f, capacity - destination.GetPopulation(species.Id));
                    capacityFactor = Mathf.Lerp(0.1f, 1f, Mathf.Clamp01(remainingCapacity / capacity));
                }

                weights[i] = suitability * capacityFactor;
                totalWeight += weights[i];
            }

            if (totalWeight <= 0f) continue;

            for (int i = 0; i < neighbors.Count; i++)
            {
                if (weights[i] <= 0f) continue;
                pendingTransfers.Add(new PopulationTransfer
                {
                    source = source,
                    destination = neighbors[i],
                    speciesId = population.Key,
                    count = migratingCount * weights[i] / totalWeight
                });
            }
        }
    }

    static float StableUnitValue(Vector2Int coordinate, string speciesId)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)coordinate.x) * 16777619u;
            hash = (hash ^ (uint)coordinate.y) * 16777619u;
            for (int i = 0; i < speciesId.Length; i++)
            {
                hash = (hash ^ speciesId[i]) * 16777619u;
            }

            return (hash & 0x00ffffffu) / 16777215f;
        }
    }
}
