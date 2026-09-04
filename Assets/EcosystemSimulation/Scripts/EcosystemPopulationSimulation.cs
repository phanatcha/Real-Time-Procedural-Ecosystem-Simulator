using System;
using System.Collections.Generic;
using UnityEngine;

public struct EcosystemSimulationStepStats
{
    public int nearbyCellsUpdated;
    public int distantCellsUpdated;
    public int migrationTransfers;
    public float migratedPopulation;
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

    public EcosystemGrid Grid => grid;
    public EcosystemSimulationStepStats LastStepStats { get; private set; }
    public event Action<EcosystemCell, float> CellUpdated;

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
