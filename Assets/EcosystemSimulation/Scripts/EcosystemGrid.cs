using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct EcosystemCellEnvironment
{
    public bool isValid;
    public bool isHabitable;
    public int sampleCount;
    public float landFraction;
    public float waterFraction;
    public float shoreFraction;
    public EnvironmentSample average;
}

public sealed class EcosystemCell
{
    readonly Dictionary<string, float> populations = new Dictionary<string, float>();

    public Vector2Int Coordinate { get; }
    public Rect WorldBounds { get; }
    public Vector2 WorldCentre => WorldBounds.center;
    public EcosystemCellEnvironment Environment { get; }
    public IReadOnlyDictionary<string, float> Populations => populations;
    public int UpdateCount { get; internal set; }
    public float LastUpdateDuration { get; internal set; }
    internal float UpdateAccumulator { get; set; }

    public float TotalAbstractPopulation
    {
        get
        {
            float total = 0f;
            foreach (float count in populations.Values) total += count;
            return total;
        }
    }

    public EcosystemCell(Vector2Int coordinate, Rect worldBounds, EcosystemCellEnvironment environment)
    {
        Coordinate = coordinate;
        WorldBounds = worldBounds;
        Environment = environment;
    }

    public float GetPopulation(string speciesId)
    {
        if (string.IsNullOrEmpty(speciesId)) return 0f;
        return populations.TryGetValue(speciesId, out float count) ? count : 0f;
    }

    public void SetPopulation(string speciesId, float count)
    {
        if (string.IsNullOrEmpty(speciesId)) throw new ArgumentException("A species ID is required.", nameof(speciesId));

        float clampedCount = Mathf.Max(0f, count);
        if (clampedCount <= 0.0001f)
        {
            populations.Remove(speciesId);
            return;
        }

        populations[speciesId] = clampedCount;
    }

    public void AddPopulation(string speciesId, float amount)
    {
        SetPopulation(speciesId, GetPopulation(speciesId) + amount);
    }
}

public sealed class EcosystemGrid
{
    static readonly Vector2Int[] NeighborOffsets =
    {
        new Vector2Int(-1, 0),
        new Vector2Int(1, 0),
        new Vector2Int(0, -1),
        new Vector2Int(0, 1)
    };

    readonly Dictionary<Vector2Int, EcosystemCell> cells;

    public float WorldRadius { get; }
    public float CellSize { get; }
    public Rect WorldBounds { get; }
    public int CandidateCellCount { get; }
    public int CellCount => cells.Count;
    public IEnumerable<EcosystemCell> Cells => cells.Values;

    EcosystemGrid(float worldRadius, float cellSize, Rect worldBounds,
        int candidateCellCount, Dictionary<Vector2Int, EcosystemCell> cells)
    {
        WorldRadius = worldRadius;
        CellSize = cellSize;
        WorldBounds = worldBounds;
        CandidateCellCount = candidateCellCount;
        this.cells = cells;
    }

    public static EcosystemGrid Generate(TerrainEnvironmentSampler terrainSampler,
        EcosystemSimulationSettings settings)
    {
        if (terrainSampler == null) throw new ArgumentNullException(nameof(terrainSampler));
        if (!terrainSampler.IsConfigured) throw new InvalidOperationException("The terrain sampler is not configured.");
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        float cellSize = Mathf.Max(25f, settings.cellSize);
        float worldRadius = Mathf.Max(
            cellSize * 0.5f,
            terrainSampler.HeightMapSettings.worldRadius * terrainSampler.MeshSettings.meshScale);
        Rect worldBounds = Rect.MinMaxRect(-worldRadius, -worldRadius, worldRadius, worldRadius);
        float edgeInset = Mathf.Min(0.001f, cellSize * 0.00001f);
        Vector2Int minimumCoordinate = TerrainGrid.WorldToCell(worldBounds.min, cellSize);
        Vector2Int maximumCoordinate = TerrainGrid.WorldToCell(
            worldBounds.max - Vector2.one * edgeInset,
            cellSize);
        Dictionary<Vector2Int, EcosystemCell> generatedCells = new Dictionary<Vector2Int, EcosystemCell>();
        int candidateCount = 0;

        for (int y = minimumCoordinate.y; y <= maximumCoordinate.y; y++)
        {
            for (int x = minimumCoordinate.x; x <= maximumCoordinate.x; x++)
            {
                Vector2Int coordinate = new Vector2Int(x, y);
                Vector2 centre = TerrainGrid.CellToWorldPosition(coordinate, cellSize);
                if (Mathf.Max(Mathf.Abs(centre.x), Mathf.Abs(centre.y)) > worldRadius) continue;

                candidateCount++;
                Rect bounds = new Rect(
                    coordinate.x * cellSize,
                    coordinate.y * cellSize,
                    cellSize,
                    cellSize);
                EcosystemCellEnvironment environment = SampleEnvironment(
                    terrainSampler,
                    bounds,
                    settings.environmentSamplesPerAxis,
                    settings.minimumHabitableLandFraction);
                if (!environment.isHabitable) continue;

                generatedCells.Add(coordinate, new EcosystemCell(coordinate, bounds, environment));
            }
        }

        return new EcosystemGrid(worldRadius, cellSize, worldBounds, candidateCount, generatedCells);
    }

    public bool TryGetCell(Vector2Int coordinate, out EcosystemCell cell)
    {
        return cells.TryGetValue(coordinate, out cell);
    }

    public bool TryGetCell(Vector2 worldPosition, out EcosystemCell cell)
    {
        return TryGetCell(TerrainGrid.WorldToCell(worldPosition, CellSize), out cell);
    }

    public bool TryGetCell(Vector3 worldPosition, out EcosystemCell cell)
    {
        return TryGetCell(new Vector2(worldPosition.x, worldPosition.z), out cell);
    }

    public bool TryGetNearestCell(Vector2 worldPosition, out EcosystemCell nearestCell)
    {
        if (TryGetCell(worldPosition, out nearestCell)) return true;

        nearestCell = null;
        float nearestDistance = float.MaxValue;
        foreach (EcosystemCell cell in cells.Values)
        {
            float distance = (cell.WorldCentre - worldPosition).sqrMagnitude;
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearestCell = cell;
        }

        return nearestCell != null;
    }

    public IEnumerable<EcosystemCell> GetNeighbors(Vector2Int coordinate)
    {
        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            if (cells.TryGetValue(coordinate + NeighborOffsets[i], out EcosystemCell neighbor))
            {
                yield return neighbor;
            }
        }
    }

    static EcosystemCellEnvironment SampleEnvironment(TerrainEnvironmentSampler terrainSampler,
        Rect bounds, int samplesPerAxis, float minimumHabitableLandFraction)
    {
        int sampleAxis = Mathf.Clamp(samplesPerAxis, 1, 5);
        int biomeCount = Enum.GetValues(typeof(BiomeId)).Length;
        int[] biomeSamples = new int[biomeCount];
        int sampleCount = 0;
        int landCount = 0;
        int waterCount = 0;
        int shoreCount = 0;
        float height = 0f;
        float normalizedHeight = 0f;
        Vector3 normal = Vector3.zero;
        float slope = 0f;
        float slopeDegrees = 0f;
        float moisture = 0f;
        float temperature = 0f;
        float coldness = 0f;
        float treeCover = 0f;
        float grassBiomass = 0f;
        float rockDensity = 0f;
        float riverStrength = 0f;
        float lakeStrength = 0f;
        float hydrology = 0f;
        float waterAvailability = 0f;

        for (int sampleY = 0; sampleY < sampleAxis; sampleY++)
        {
            for (int sampleX = 0; sampleX < sampleAxis; sampleX++)
            {
                Vector2 position = new Vector2(
                    Mathf.Lerp(bounds.xMin, bounds.xMax, (sampleX + 0.5f) / sampleAxis),
                    Mathf.Lerp(bounds.yMin, bounds.yMax, (sampleY + 0.5f) / sampleAxis));
                if (!terrainSampler.TrySample(position, out EnvironmentSample sample)) continue;

                sampleCount++;
                if (sample.isLand) landCount++;
                if (sample.isWater) waterCount++;
                if (sample.isShore) shoreCount++;
                biomeSamples[(int)sample.biome]++;
                height += sample.height;
                normalizedHeight += sample.normalizedHeight;
                normal += sample.surfaceNormal;
                slope += sample.slope;
                slopeDegrees += sample.slopeDegrees;
                moisture += sample.moisture;
                temperature += sample.temperature;
                coldness += sample.coldness;
                treeCover += sample.treeCover;
                grassBiomass += sample.grassBiomass;
                rockDensity += sample.rockDensity;
                riverStrength += sample.riverStrength;
                lakeStrength += sample.lakeStrength;
                hydrology += sample.hydrology;
                waterAvailability += sample.waterAvailability;
            }
        }

        if (sampleCount == 0) return default;

        float inverseSampleCount = 1f / sampleCount;
        float landFraction = landCount * inverseSampleCount;
        float waterFraction = waterCount * inverseSampleCount;
        float shoreFraction = shoreCount * inverseSampleCount;
        BiomeId dominantBiome = BiomeId.DeepWater;
        int dominantCount = -1;
        for (int i = 0; i < biomeSamples.Length; i++)
        {
            if (biomeSamples[i] <= dominantCount) continue;
            dominantCount = biomeSamples[i];
            dominantBiome = (BiomeId)i;
        }

        Vector2 centre = bounds.center;
        Vector3 averageNormal = normal.sqrMagnitude > 0f ? normal.normalized : Vector3.up;
        bool isHabitable = landFraction >= Mathf.Clamp01(minimumHabitableLandFraction);

        return new EcosystemCellEnvironment
        {
            isValid = true,
            isHabitable = isHabitable,
            sampleCount = sampleCount,
            landFraction = landFraction,
            waterFraction = waterFraction,
            shoreFraction = shoreFraction,
            average = new EnvironmentSample
            {
                isValid = true,
                position = new Vector3(centre.x, height * inverseSampleCount, centre.y),
                height = height * inverseSampleCount,
                normalizedHeight = normalizedHeight * inverseSampleCount,
                surfaceNormal = averageNormal,
                slope = slope * inverseSampleCount,
                slopeDegrees = slopeDegrees * inverseSampleCount,
                isLand = landFraction >= waterFraction,
                isWater = waterFraction > landFraction,
                isShore = shoreFraction >= 0.5f,
                biome = dominantBiome,
                moisture = moisture * inverseSampleCount,
                temperature = temperature * inverseSampleCount,
                coldness = coldness * inverseSampleCount,
                treeCover = treeCover * inverseSampleCount,
                grassBiomass = grassBiomass * inverseSampleCount,
                rockDensity = rockDensity * inverseSampleCount,
                riverStrength = riverStrength * inverseSampleCount,
                lakeStrength = lakeStrength * inverseSampleCount,
                hydrology = hydrology * inverseSampleCount,
                waterAvailability = waterAvailability * inverseSampleCount
            }
        };
    }
}
