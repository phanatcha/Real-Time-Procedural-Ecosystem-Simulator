using System;
using System.Collections.Generic;
using UnityEngine;

// What an animal needs from a place. The defaults copy the live simulation, so the analysis measures
// the habitat the founders actually live in:
// - movement: AnimalTerrainWorld's walkable ground (land, not shore, slope up to 32°) inside its 20 m border
// - food: FoodSpawner's ground plants (grass biomass of at least 0.02 and a regrowth rate of at least 5%),
//   using the plant growth profile AnimalTerrainDemoBootstrap gives plant food (-12, 2 to 13 and 25 °C)
// - temperature: the founders' comfort range, 2 °C preferred with 18 °C of cold and 16 °C of heat tolerance
[Serializable]
public struct HabitatRequirements
{
    [Header("Movement")]
    [Range(0f, 60f)] public float maximumSlopeDegrees;
    public bool excludeShore;
    [Tooltip("Animals stay this far inside the world edge.")]
    [Min(0f)] public float boundaryInset;

    [Header("Food")]
    [Range(0f, 1f)] public float minimumGrassBiomass;
    [Tooltip("Fraction of the ideal regrowth speed below which plants are not grown.")]
    [Range(0f, 1f)] public float minimumPlantGrowthRate;
    public float plantMinimumCelsius;
    public float plantOptimalMinimumCelsius;
    public float plantOptimalMaximumCelsius;
    public float plantMaximumCelsius;

    [Header("Temperature")]
    [Tooltip("Celsius at the terrain's coldest and warmest temperature index, as ProceduralTerrainTemperatureProvider.")]
    public float coldestCelsius;
    public float warmestCelsius;
    public float comfortMinimumCelsius;
    public float comfortMaximumCelsius;

    public static HabitatRequirements Founders => new HabitatRequirements
    {
        maximumSlopeDegrees = 32f,
        excludeShore = true,
        boundaryInset = 20f,
        minimumGrassBiomass = 0.02f,
        minimumPlantGrowthRate = 0.05f,
        plantMinimumCelsius = -12f,
        plantOptimalMinimumCelsius = 2f,
        plantOptimalMaximumCelsius = 13f,
        plantMaximumCelsius = 25f,
        coldestCelsius = -28f,
        warmestCelsius = 18f,
        comfortMinimumCelsius = -16f,
        comfortMaximumCelsius = 18f
    };

    public bool IsWalkable(EnvironmentSample sample)
    {
        return AnimalTerrainWorld.IsWalkable(sample, maximumSlopeDegrees, excludeShore);
    }

    public float ToCelsius(EnvironmentSample sample)
    {
        return ProceduralTerrainTemperatureProvider.NormalizedToCelsius(sample.temperature, coldestCelsius, warmestCelsius);
    }

    // The same curve as FoodItem.EvaluateGrowthMultiplier: zero at the limits, one across the optimal range.
    public float PlantGrowthRate(float celsius)
    {
        float minimum = plantMinimumCelsius;
        float maximum = Mathf.Max(minimum + 0.01f, plantMaximumCelsius);
        float optimalMinimum = Mathf.Clamp(plantOptimalMinimumCelsius, minimum, maximum);
        float optimalMaximum = Mathf.Clamp(plantOptimalMaximumCelsius, optimalMinimum, maximum);
        if (celsius <= minimum || celsius >= maximum) return 0f;
        if (celsius < optimalMinimum) return Mathf.InverseLerp(minimum, optimalMinimum, celsius);
        return celsius <= optimalMaximum ? 1f : 1f - Mathf.InverseLerp(optimalMaximum, maximum, celsius);
    }

    // Walkable ground where plant food grows and the animal is within its comfort range.
    public bool IsSuitable(EnvironmentSample sample)
    {
        if (!IsWalkable(sample) || sample.grassBiomass < minimumGrassBiomass) return false;

        float celsius = ToCelsius(sample);
        if (celsius < comfortMinimumCelsius || celsius > comfortMaximumCelsius) return false;
        return PlantGrowthRate(celsius) >= minimumPlantGrowthRate;
    }
}

// Ordered so each class from Barrier on is land, from Passable on is walkable and from AccessibleHabitat
// on is suitable habitat. HabitatAnalysis compares classes with >= to rely on this.
public enum HabitatClass : byte
{
    NoData,
    Water,
    // Land an animal cannot walk on: too steep, or the shore band.
    Barrier,
    // Walkable, but without the food or temperature the animal needs. Animals can still cross it.
    Passable,
    // Suitable habitat an animal can walk to from the start.
    AccessibleHabitat,
    // Suitable habitat cut off from the start by water or barriers.
    IsolatedHabitat
}

public struct HabitatCellSummary
{
    public Vector2Int coordinate;
    public int samples;
    public int land;
    public int walkable;
    public int suitable;
    public int accessible;
}

public delegate bool EnvironmentSampleSource(Vector2 worldPosition, out EnvironmentSample sample);

// Measures how much suitable habitat a world has (availability) and how much of it an animal can walk to
// from a starting point (accessibility). Samples a square grid over the habitable area, then joins walkable
// samples into regions through their four side neighbours.
public sealed class HabitatAnalysis
{
    public const int MaximumSamplesPerAxis = 4096;

    static readonly Vector2Int[] NeighbourOffsets =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1)
    };

    readonly HabitatClass[] classes;
    readonly List<HabitatCellSummary> cells = new List<HabitatCellSummary>();

    public HabitatRequirements Requirements { get; }
    public int Size { get; }
    public float Spacing { get; }
    public float Extent { get; }
    public float CellSize { get; }
    public Vector2 RequestedStart { get; }
    public bool HasStart { get; private set; }
    public Vector2 Start { get; private set; }
    public float StartOffset => HasStart ? Vector2.Distance(RequestedStart, Start) : 0f;

    public int LandSamples { get; private set; }
    public int WalkableSamples { get; private set; }
    public int SuitableSamples { get; private set; }
    public int AccessibleSamples { get; private set; }
    // Separate walkable regions that hold any suitable habitat.
    public int HabitatRegionCount { get; private set; }
    public int LargestRegionSuitableSamples { get; private set; }

    public float SampleArea => Spacing * Spacing;
    // Share of land that is suitable habitat.
    public float Availability => LandSamples == 0 ? 0f : (float)SuitableSamples / LandSamples;
    // Share of suitable habitat an animal can walk to from the start.
    public float Accessibility => SuitableSamples == 0 ? 0f : (float)AccessibleSamples / SuitableSamples;
    public float LargestRegionShare => SuitableSamples == 0 ? 0f : (float)LargestRegionSuitableSamples / SuitableSamples;
    public IReadOnlyList<HabitatCellSummary> Cells => cells;

    HabitatAnalysis(HabitatRequirements requirements, int size, float spacing, float extent, float cellSize,
        Vector2 requestedStart)
    {
        Requirements = requirements;
        Size = size;
        Spacing = spacing;
        Extent = extent;
        CellSize = cellSize;
        RequestedStart = requestedStart;
        classes = new HabitatClass[size * size];
    }

    public HabitatClass GetClass(int x, int z)
    {
        return classes[z * Size + x];
    }

    public Vector2 GetPosition(int x, int z)
    {
        return new Vector2(-Extent + x * Spacing, -Extent + z * Spacing);
    }

    // Analyses the whole habitable area of a terrain, the square AnimalTerrainWorld lets animals use.
    // progress receives 0 to 1 and returns true to cancel, which throws OperationCanceledException.
    public static HabitatAnalysis Run(TerrainEnvironmentSampler sampler, HabitatRequirements requirements,
        float spacing, Vector2 start, float cellSize, Func<float, bool> progress = null)
    {
        if (sampler == null) throw new ArgumentNullException(nameof(sampler));
        if (!sampler.IsConfigured) throw new InvalidOperationException("The terrain sampler is not configured.");

        float worldRadius = sampler.HeightMapSettings.worldRadius * sampler.MeshSettings.meshScale;
        float extent = worldRadius - Mathf.Max(0f, requirements.boundaryInset);
        return Run(sampler.TrySample, extent, spacing, requirements, start, cellSize, progress);
    }

    public static HabitatAnalysis Run(EnvironmentSampleSource source, float extent, float spacing,
        HabitatRequirements requirements, Vector2 start, float cellSize, Func<float, bool> progress = null)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (!(extent > 0f)) throw new ArgumentOutOfRangeException(nameof(extent), "The habitable area is empty.");
        if (!(spacing > 0f)) throw new ArgumentOutOfRangeException(nameof(spacing), "Sample spacing must be positive.");
        if (!(cellSize > 0f)) throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be positive.");

        int size = Mathf.FloorToInt(extent * 2f / spacing + 0.0001f) + 1;
        if (size > MaximumSamplesPerAxis)
        {
            throw new ArgumentOutOfRangeException(nameof(spacing),
                $"{size} samples per axis is more than {MaximumSamplesPerAxis}. Use a larger spacing.");
        }

        HabitatAnalysis analysis = new HabitatAnalysis(requirements, size, spacing, extent, cellSize, start);
        analysis.Classify(source, progress);
        analysis.MarkAccessibleHabitat();
        analysis.SummariseCells();
        return analysis;
    }

    void Classify(EnvironmentSampleSource source, Func<float, bool> progress)
    {
        for (int z = 0; z < Size; z++)
        {
            if (progress != null && progress((float)z / Size)) throw new OperationCanceledException();

            for (int x = 0; x < Size; x++)
            {
                HabitatClass habitatClass = HabitatClass.NoData;
                if (source(GetPosition(x, z), out EnvironmentSample sample) && sample.isValid)
                {
                    if (!sample.isLand || sample.isWater) habitatClass = HabitatClass.Water;
                    else if (!Requirements.IsWalkable(sample)) habitatClass = HabitatClass.Barrier;
                    else if (!Requirements.IsSuitable(sample)) habitatClass = HabitatClass.Passable;
                    // Suitable habitat counts as cut off until the flood fill reaches it.
                    else habitatClass = HabitatClass.IsolatedHabitat;
                }

                classes[z * Size + x] = habitatClass;
                if (habitatClass >= HabitatClass.Barrier) LandSamples++;
                if (habitatClass >= HabitatClass.Passable) WalkableSamples++;
                if (habitatClass >= HabitatClass.AccessibleHabitat) SuitableSamples++;
            }
        }

        progress?.Invoke(1f);
    }

    // Joins walkable samples into regions, then marks the suitable samples in the start's region as accessible.
    void MarkAccessibleHabitat()
    {
        int[] regions = new int[classes.Length];
        int[] queue = new int[classes.Length];
        List<int> suitablePerRegion = new List<int> { 0 };

        for (int seed = 0; seed < classes.Length; seed++)
        {
            if (regions[seed] != 0 || classes[seed] < HabitatClass.Passable) continue;

            int region = suitablePerRegion.Count;
            int suitable = 0;
            int head = 0;
            int tail = 0;
            regions[seed] = region;
            queue[tail++] = seed;
            while (head < tail)
            {
                int index = queue[head++];
                if (classes[index] >= HabitatClass.AccessibleHabitat) suitable++;

                int x = index % Size;
                int z = index / Size;
                for (int i = 0; i < NeighbourOffsets.Length; i++)
                {
                    int neighbourX = x + NeighbourOffsets[i].x;
                    int neighbourZ = z + NeighbourOffsets[i].y;
                    if (neighbourX < 0 || neighbourX >= Size || neighbourZ < 0 || neighbourZ >= Size) continue;

                    int neighbour = neighbourZ * Size + neighbourX;
                    if (regions[neighbour] != 0 || classes[neighbour] < HabitatClass.Passable) continue;
                    regions[neighbour] = region;
                    queue[tail++] = neighbour;
                }
            }

            suitablePerRegion.Add(suitable);
            if (suitable > 0) HabitatRegionCount++;
            LargestRegionSuitableSamples = Mathf.Max(LargestRegionSuitableSamples, suitable);
        }

        // Animals start on the walkable ground nearest the requested start, as the founders do.
        int startIndex = -1;
        float nearestDistance = float.MaxValue;
        for (int index = 0; index < classes.Length; index++)
        {
            if (classes[index] < HabitatClass.Passable) continue;

            float distance = (GetPosition(index % Size, index / Size) - RequestedStart).sqrMagnitude;
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            startIndex = index;
        }

        if (startIndex < 0) return;

        HasStart = true;
        Start = GetPosition(startIndex % Size, startIndex / Size);
        int startRegion = regions[startIndex];
        for (int index = 0; index < classes.Length; index++)
        {
            if (regions[index] != startRegion || classes[index] != HabitatClass.IsolatedHabitat) continue;
            classes[index] = HabitatClass.AccessibleHabitat;
            AccessibleSamples++;
        }
    }

    // Totals per ecosystem cell, on the same grid as EcosystemGrid, so the off-screen model can use them.
    void SummariseCells()
    {
        Dictionary<Vector2Int, HabitatCellSummary> summaries = new Dictionary<Vector2Int, HabitatCellSummary>();
        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector2Int coordinate = TerrainGrid.WorldToCell(GetPosition(x, z), CellSize);
                summaries.TryGetValue(coordinate, out HabitatCellSummary summary);
                summary.coordinate = coordinate;

                HabitatClass habitatClass = classes[z * Size + x];
                summary.samples++;
                if (habitatClass >= HabitatClass.Barrier) summary.land++;
                if (habitatClass >= HabitatClass.Passable) summary.walkable++;
                if (habitatClass >= HabitatClass.AccessibleHabitat) summary.suitable++;
                if (habitatClass == HabitatClass.AccessibleHabitat) summary.accessible++;
                summaries[coordinate] = summary;
            }
        }

        cells.AddRange(summaries.Values);
        cells.Sort((first, second) => first.coordinate.y != second.coordinate.y
            ? first.coordinate.y.CompareTo(second.coordinate.y)
            : first.coordinate.x.CompareTo(second.coordinate.x));
    }
}
