using System;
using System.Collections.Generic;
using UnityEngine;

// The food and energy rules of the live simulation, as formulas for animals kept as numbers off-screen.
// A population off-screen should grow about as fast, and to about the same size, as it would on screen.
[Serializable]
public struct OffscreenEcologyRules
{
    [Header("Plant food (FoodSpawner)")]
    [Min(1f)] public float plantSiteSpacing;
    [Range(0f, 1f)] public float minimumGrassBiomass;
    [Range(0f, 1f)] public float minimumPlantGrowthRate;
    [Min(0f)] public float plantNutrition;
    public Vector2 moistureNutritionMultiplier;
    [Min(1f)] public float plantRegrowSeconds;

    [Header("Animals (SeekFood and AnimalTemperature)")]
    [Range(0f, 1f)] public float minimumDietEfficiency;
    [Min(0.01f)] public float reproductionCooldown;
    [Min(0.1f)] public float degreesPerStressUnit;
    [Min(0f)] public float extraEnergyDrainPerStressUnit;
    [Min(1f)] public float maximumStress;

    [Header("Calibration")]
    [Tooltip("Share of the plant energy growing in a cell that its animals manage to eat. Live animals " +
             "wander and miss plants, so this is below 1. Not measured yet; compare live and off-screen " +
             "densities to tune it.")]
    [Range(0.01f, 1f)] public float harvestEfficiency;

    public static OffscreenEcologyRules Defaults => new OffscreenEcologyRules
    {
        plantSiteSpacing = 16f,
        minimumGrassBiomass = 0.02f,
        minimumPlantGrowthRate = 0.05f,
        plantNutrition = 75f,
        moistureNutritionMultiplier = new Vector2(0.6f, 1.4f),
        plantRegrowSeconds = 90f,
        minimumDietEfficiency = 0.2f,
        reproductionCooldown = 60f,
        degreesPerStressUnit = 20f,
        extraEnergyDrainPerStressUnit = 0.5f,
        maximumStress = 3f,
        harvestEfficiency = 0.5f
    };

    // Plant energy an area of walkable ground grows per second. FoodSpawner puts one plant site per
    // spacing² of ground, holding a plant with a chance equal to the grass biomass. A plant is worth its
    // nutrition, scaled by moisture, and regrows after the regrow time divided by the growth rate.
    public float PlantEnergyPerSecond(float area, float grassBiomass, float moisture, float growthRate)
    {
        if (area <= 0f || grassBiomass < minimumGrassBiomass || growthRate < minimumPlantGrowthRate) return 0f;

        float sites = area / (plantSiteSpacing * plantSiteSpacing);
        float nutrition = plantNutrition * Mathf.Lerp(moistureNutritionMultiplier.x, moistureNutritionMultiplier.y, moisture);
        return sites * Mathf.Clamp01(grassBiomass) * nutrition * growthRate / plantRegrowSeconds;
    }

    // The share of plant energy an animal digests, as SeekFood.GetDigestionEfficiency.
    public float PlantDigestion(float dietAffinity)
    {
        float minimum = Mathf.Clamp01(minimumDietEfficiency);
        return minimum + (1f - minimum) * (1f - Mathf.Clamp01(dietAffinity));
    }

    // Extra energy use outside the comfort range, as AnimalTemperature.EvaluateAtTemperature.
    public float ThermalEnergyMultiplier(float comfortMinimum, float comfortMaximum, float celsius)
    {
        float span = Mathf.Max(0.1f, degreesPerStressUnit);
        float cap = Mathf.Max(1f, maximumStress);
        float coldStress = Mathf.Clamp((comfortMinimum - celsius) / span, 0f, cap);
        float heatStress = Mathf.Clamp((celsius - comfortMaximum) / span, 0f, cap);
        return 1f + Mathf.Max(coldStress, heatStress) * Mathf.Max(0f, extraEnergyDrainPerStressUnit);
    }

    // How many animals like this a cell can feed on its own: the plant energy they digest divided by the
    // energy each one uses.
    public float CarryingCapacity(OffscreenSample sample, float plantEnergyPerSecond, float celsius)
    {
        float digested = harvestEfficiency * plantEnergyPerSecond * PlantDigestion(sample.DietAffinity) *
                         sample.plantDigestionMultiplier;
        return digested / EnergyUse(sample, celsius);
    }

    public float EnergyUse(OffscreenSample sample, float celsius)
    {
        return Mathf.Max(0.001f, sample.baseEnergyUse *
                                 ThermalEnergyMultiplier(sample.comfortMinimum, sample.comfortMaximum, celsius));
    }

    // Growth while food is plentiful, from the animal's maturity time, breeding cooldown and lifespan.
    public float GrowthRate(OffscreenSample sample)
    {
        return Mathf.Max(0f, EcosystemSpeciesDefinition.EstimateGrowthRate(
            sample.genome[AnimalGene.MaturityTime], reproductionCooldown, sample.genome[AnimalGene.MaxLifespan]));
    }

    // Deaths where a cell has no food to spare: old age, plus starving through a full energy store.
    public float DeathRate(OffscreenSample sample, float celsius)
    {
        return 1f / Mathf.Max(1f, sample.genome[AnimalGene.MaxLifespan]) +
               EnergyUse(sample, celsius) / Mathf.Max(1f, sample.genome[AnimalGene.MaxEnergy]);
    }
}

// One real animal's inherited traits, kept to stand for part of an off-screen population.
public sealed class OffscreenSample
{
    public readonly AnimalGenome genome;
    // Energy used per second at a comfortable temperature, measured on the live animal.
    public readonly float baseEnergyUse;
    public readonly int generation;
    // Comfort range after body parts and perks, which change the inherited tolerances.
    public readonly float comfortMinimum;
    public readonly float comfortMaximum;
    // Below 1 for a scavenger gut, which digests plants worse.
    public readonly float plantDigestionMultiplier;

    public float DietAffinity => genome[AnimalGene.DietAffinity];

    public OffscreenSample(AnimalGenome genome, float baseEnergyUse, int generation)
    {
        this.genome = genome ?? throw new ArgumentNullException(nameof(genome));
        this.baseEnergyUse = Mathf.Max(0.001f, baseEnergyUse);
        this.generation = Mathf.Max(0, generation);

        BodyPlanEffects effects = AnimalBodyPlan.Evaluate(genome);
        PerkEffects perks = AnimalPerks.Evaluate(genome);
        float preferred = genome[AnimalGene.PreferredTemperature];
        comfortMinimum = preferred - Mathf.Max(0f, genome[AnimalGene.ColdTolerance] + effects.coldToleranceChange +
                                                   perks.coldToleranceChange);
        comfortMaximum = preferred + Mathf.Max(0f, genome[AnimalGene.HeatTolerance] + effects.heatToleranceChange +
                                                   perks.heatToleranceChange);
        plantDigestionMultiplier = perks.plantDigestionMultiplier;
    }
}

// Animals of one species in one cell: how many there are, and a few real animals that stand for them.
public sealed class OffscreenPopulation
{
    public readonly string speciesName;
    public Color color;
    public float count;
    public readonly List<OffscreenSample> samples = new List<OffscreenSample>();

    public OffscreenPopulation(string speciesName, Color color)
    {
        this.speciesName = speciesName;
        this.color = color;
    }
}

public sealed class OffscreenCell
{
    public readonly Vector2Int coordinate;
    public readonly Rect bounds;
    // Plant energy growing on the cell's walkable ground per second.
    public readonly float plantEnergyPerSecond;
    // Average temperature of the walkable ground.
    public readonly float celsius;
    public readonly List<OffscreenPopulation> populations = new List<OffscreenPopulation>();
    internal float updateAccumulator;

    public Vector2 Centre => bounds.center;

    public OffscreenCell(Vector2Int coordinate, Rect bounds, float plantEnergyPerSecond, float celsius)
    {
        this.coordinate = coordinate;
        this.bounds = bounds;
        this.plantEnergyPerSecond = Mathf.Max(0f, plantEnergyPerSecond);
        this.celsius = celsius;
    }

    public OffscreenPopulation Find(string speciesName)
    {
        for (int i = 0; i < populations.Count; i++)
        {
            if (populations[i].speciesName == speciesName) return populations[i];
        }

        return null;
    }
}

// Keeps animals away from the camera as numbers per cell and species. Each update, populations grow toward
// the number of animals the cell's plants can feed, share that food with the other populations and live
// animals there, and some move to neighbouring cells. A few real genomes per population stand for the rest,
// so animals created near the camera again carry genes the population actually has.
public sealed class OffscreenPopulationStore
{
    static readonly Vector2Int[] NeighbourOffsets =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1)
    };

    const float RemovalCount = 0.01f;

    readonly Dictionary<Vector2Int, OffscreenCell> cells = new Dictionary<Vector2Int, OffscreenCell>();
    readonly System.Random random;
    readonly List<(OffscreenPopulation source, OffscreenCell destination, float count)> transfers =
        new List<(OffscreenPopulation, OffscreenCell, float)>();
    readonly List<float> capacities = new List<float>();
    readonly List<OffscreenCell> neighbours = new List<OffscreenCell>();
    readonly List<float> weights = new List<float>();

    public OffscreenEcologyRules rules;
    // Real genomes kept per population to stand for the rest.
    public int samplesPerPopulation = 8;
    public float migrationRatePerSecond = 0.004f;
    public float nearbyRadius = 750f;
    public float nearbyUpdateInterval = 0.5f;
    public float distantUpdateInterval = 6f;

    public float CellSize { get; }
    public IEnumerable<OffscreenCell> Cells => cells.Values;
    public int CellCount => cells.Count;
    // Live animals in a cell. They eat the cell's food too, so off-screen populations only get the rest.
    public Func<Vector2Int, int> LiveAnimals { get; set; }

    public OffscreenPopulationStore(float cellSize, OffscreenEcologyRules rules, System.Random random)
    {
        if (!(cellSize > 0f)) throw new ArgumentOutOfRangeException(nameof(cellSize));
        CellSize = cellSize;
        this.rules = rules;
        this.random = random ?? new System.Random();
    }

    public void AddCell(Vector2Int coordinate, float plantEnergyPerSecond, float celsius)
    {
        Rect bounds = new Rect(coordinate.x * CellSize, coordinate.y * CellSize, CellSize, CellSize);
        cells[coordinate] = new OffscreenCell(coordinate, bounds, plantEnergyPerSecond, celsius);
    }

    public Vector2Int WorldToCell(Vector2 position)
    {
        return TerrainGrid.WorldToCell(position, CellSize);
    }

    public bool TryGetCell(Vector2Int coordinate, out OffscreenCell cell)
    {
        return cells.TryGetValue(coordinate, out cell);
    }

    public bool TryGetNearestCell(Vector2 position, out OffscreenCell nearest)
    {
        if (cells.TryGetValue(WorldToCell(position), out nearest)) return true;

        float nearestDistance = float.MaxValue;
        foreach (OffscreenCell cell in cells.Values)
        {
            float distance = (cell.Centre - position).sqrMagnitude;
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearest = cell;
        }

        return nearest != null;
    }

    // Adds one animal that has left the camera's area.
    public void AddAnimal(OffscreenCell cell, string speciesName, Color color, OffscreenSample sample)
    {
        OffscreenPopulation population = GetOrCreate(cell, speciesName, color);
        AddSamples(population, new[] { sample }, 1f);
    }

    // Removes one animal to be created near the camera, returning a real genome from the population.
    public bool TryTakeAnimal(OffscreenCell cell, OffscreenPopulation population, out OffscreenSample sample)
    {
        sample = null;
        if (population.count < 1f || population.samples.Count == 0) return false;

        sample = population.samples[random.Next(population.samples.Count)];
        population.count -= 1f;
        if (population.count < RemovalCount) cell.populations.Remove(population);
        return true;
    }

    public float CountSpecies(string speciesName)
    {
        float total = 0f;
        foreach (OffscreenCell cell in cells.Values)
        {
            OffscreenPopulation population = cell.Find(speciesName);
            if (population != null) total += population.count;
        }

        return total;
    }

    public Dictionary<string, float> CountBySpecies()
    {
        Dictionary<string, float> totals = new Dictionary<string, float>();
        foreach (OffscreenCell cell in cells.Values)
        {
            foreach (OffscreenPopulation population in cell.populations)
            {
                totals.TryGetValue(population.speciesName, out float total);
                totals[population.speciesName] = total + population.count;
            }
        }

        return totals;
    }

    public void RemoveSpecies(string speciesName)
    {
        foreach (OffscreenCell cell in cells.Values)
        {
            cell.populations.RemoveAll(population => population.speciesName == speciesName);
        }
    }

    public void Clear()
    {
        foreach (OffscreenCell cell in cells.Values) cell.populations.Clear();
    }

    // How many animals like this population's the cell could feed if it had the cell to itself.
    public float Capacity(OffscreenCell cell, OffscreenPopulation population)
    {
        if (population.samples.Count == 0) return 0f;

        float total = 0f;
        for (int i = 0; i < population.samples.Count; i++)
        {
            total += rules.CarryingCapacity(population.samples[i], cell.plantEnergyPerSecond, cell.celsius);
        }

        return total / population.samples.Count;
    }

    // Cells near the focus update more often; each update covers the time since the last one.
    public void Advance(float deltaTime, Vector2 focus)
    {
        if (!(deltaTime > 0f) || float.IsInfinity(deltaTime)) return;

        transfers.Clear();
        float nearbyRadiusSquared = nearbyRadius * nearbyRadius;
        foreach (OffscreenCell cell in cells.Values)
        {
            bool isNearby = (cell.Centre - focus).sqrMagnitude <= nearbyRadiusSquared;
            float interval = isNearby ? Mathf.Max(0.05f, nearbyUpdateInterval)
                                      : Mathf.Max(nearbyUpdateInterval, distantUpdateInterval);
            cell.updateAccumulator += deltaTime;
            if (cell.updateAccumulator + 0.0001f < interval) continue;

            float elapsed = cell.updateAccumulator;
            cell.updateAccumulator = 0f;
            UpdateCell(cell, elapsed);
        }

        ApplyTransfers();
    }

    // Grows every population in the cell and queues migration. Public so tests can step one cell.
    public void UpdateCell(OffscreenCell cell, float elapsed)
    {
        if (cell.populations.Count == 0 || elapsed <= 0f) return;

        Grow(cell, elapsed);
        for (int i = 0; i < cell.populations.Count; i++)
        {
            GatherMigration(cell, cell.populations[i], elapsed);
        }
    }

    public void ApplyTransfers()
    {
        foreach ((OffscreenPopulation source, OffscreenCell destination, float count) in transfers)
        {
            float moved = Mathf.Min(count, source.count);
            if (moved <= 0f) continue;

            source.count -= moved;
            AddSamples(GetOrCreate(destination, source.speciesName, source.color), source.samples, moved);
        }

        transfers.Clear();
        foreach (OffscreenCell cell in cells.Values)
        {
            cell.populations.RemoveAll(population => population.count < RemovalCount);
        }
    }

    // Each animal of a population eats 1/K of the cell's food, where K is how many of them the cell could
    // feed alone. A population grows logistically toward the room the others leave it.
    void Grow(OffscreenCell cell, float elapsed)
    {
        capacities.Clear();
        float usedShare = 0f;
        float averageInverseCapacity = 0f;
        int fed = 0;
        foreach (OffscreenPopulation population in cell.populations)
        {
            float capacity = Capacity(cell, population);
            capacities.Add(capacity);
            if (capacity <= 0f) continue;

            usedShare += population.count / capacity;
            averageInverseCapacity += 1f / capacity;
            fed++;
        }

        // Live animals eat like an average member of the populations here.
        int live = LiveAnimals == null ? 0 : Mathf.Max(0, LiveAnimals(cell.coordinate));
        if (fed > 0) usedShare += live * averageInverseCapacity / fed;

        for (int i = 0; i < cell.populations.Count; i++)
        {
            OffscreenPopulation population = cell.populations[i];
            float capacity = capacities[i];
            float ownShare = capacity > 0f ? population.count / capacity : 0f;
            float room = capacity * Mathf.Max(0f, 1f - (usedShare - ownShare));
            population.count = EcosystemPopulationSimulation.GrowLogistic(
                population.count, room, AverageGrowthRate(population), AverageDeathRate(population, cell.celsius), elapsed);
        }
    }

    // A share of the population leaves for the side neighbours, favouring those with more room for it.
    void GatherMigration(OffscreenCell source, OffscreenPopulation population, float elapsed)
    {
        float leaving = population.count * (1f - Mathf.Exp(-Mathf.Max(0f, migrationRatePerSecond) * elapsed));
        if (leaving <= 0.0001f) return;

        neighbours.Clear();
        weights.Clear();
        float totalWeight = 0f;
        for (int i = 0; i < NeighbourOffsets.Length; i++)
        {
            if (!cells.TryGetValue(source.coordinate + NeighbourOffsets[i], out OffscreenCell neighbour)) continue;

            OffscreenPopulation resident = neighbour.Find(population.speciesName);
            float room = Capacity(neighbour, population) - (resident == null ? 0f : resident.count);
            if (room <= 0f) continue;

            neighbours.Add(neighbour);
            weights.Add(room);
            totalWeight += room;
        }

        for (int i = 0; i < neighbours.Count; i++)
        {
            transfers.Add((population, neighbours[i], leaving * weights[i] / totalWeight));
        }
    }

    float AverageGrowthRate(OffscreenPopulation population)
    {
        if (population.samples.Count == 0) return 0f;

        float total = 0f;
        foreach (OffscreenSample sample in population.samples) total += rules.GrowthRate(sample);
        return total / population.samples.Count;
    }

    float AverageDeathRate(OffscreenPopulation population, float celsius)
    {
        if (population.samples.Count == 0) return 0f;

        float total = 0f;
        foreach (OffscreenSample sample in population.samples) total += rules.DeathRate(sample, celsius);
        return total / population.samples.Count;
    }

    OffscreenPopulation GetOrCreate(OffscreenCell cell, string speciesName, Color color)
    {
        OffscreenPopulation population = cell.Find(speciesName);
        if (population != null) return population;

        population = new OffscreenPopulation(speciesName, color);
        cell.populations.Add(population);
        return population;
    }

    // Adds animals to a population and lets their genomes replace some of its samples, so each sample slot
    // comes from the newcomers with a chance equal to their share of the new total.
    void AddSamples(OffscreenPopulation population, IReadOnlyList<OffscreenSample> incoming, float count)
    {
        float newTotal = population.count + count;
        population.count = newTotal;
        if (incoming.Count == 0 || newTotal <= 0f) return;

        int limit = Mathf.Max(1, samplesPerPopulation);
        int draws = Mathf.Clamp(Mathf.CeilToInt(count), 1, limit);
        float replaceChance = Mathf.Clamp01(limit * count / (newTotal * draws));
        for (int i = 0; i < draws; i++)
        {
            OffscreenSample sample = incoming[random.Next(incoming.Count)];
            if (population.samples.Count < limit) population.samples.Add(sample);
            else if (random.NextDouble() < replaceChance) population.samples[random.Next(limit)] = sample;
        }
    }
}
