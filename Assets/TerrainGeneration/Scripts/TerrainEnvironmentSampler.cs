using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TerrainEnvironmentSampler
{
    public const int DefaultMaxCachedChunks = 128;

    readonly HeightMapSettings heightMapSettings;
    readonly MeshSettings meshSettings;
    readonly EnvironmentDefinitions environmentDefinitions;
    readonly VegetationSettings vegetationSettings;
    readonly int maxCachedChunks;
    readonly Dictionary<Vector2Int, HeightMap> heightMapCache = new Dictionary<Vector2Int, HeightMap>();
    readonly Queue<Vector2Int> cacheOrder = new Queue<Vector2Int>();
    readonly object cacheLock = new object();

    public HeightMapSettings HeightMapSettings => heightMapSettings;
    public MeshSettings MeshSettings => meshSettings;
    public EnvironmentDefinitions EnvironmentDefinitions => environmentDefinitions;
    public VegetationSettings VegetationSettings => vegetationSettings;
    public bool IsConfigured => heightMapSettings != null
        && heightMapSettings.noiseSettings != null
        && heightMapSettings.heightCurve != null
        && meshSettings != null
        && meshSettings.meshScale > 0f
        && environmentDefinitions != null;

    public int CachedChunkCount
    {
        get
        {
            lock (cacheLock)
            {
                return heightMapCache.Count;
            }
        }
    }

    public TerrainEnvironmentSampler(HeightMapSettings heightMapSettings, MeshSettings meshSettings,
        EnvironmentDefinitions environmentDefinitions, VegetationSettings vegetationSettings = null,
        int maxCachedChunks = DefaultMaxCachedChunks)
    {
        this.heightMapSettings = heightMapSettings;
        this.meshSettings = meshSettings;
        this.environmentDefinitions = environmentDefinitions;
        this.vegetationSettings = vegetationSettings;
        this.maxCachedChunks = Mathf.Max(1, maxCachedChunks);
    }

    public bool TrySample(Vector3 worldPosition, out EnvironmentSample sample)
    {
        return TrySample(new Vector2(worldPosition.x, worldPosition.z), out sample);
    }

    public bool TrySample(float worldX, float worldZ, out EnvironmentSample sample)
    {
        return TrySample(new Vector2(worldX, worldZ), out sample);
    }

    public bool TrySample(Vector2 worldPosition, out EnvironmentSample sample)
    {
        sample = default;
        if (!IsConfigured || !IsFinite(worldPosition.x) || !IsFinite(worldPosition.y)) return false;

        Vector2Int chunkCoordinate = TerrainGrid.WorldToChunkCoordinate(worldPosition, meshSettings);
        HeightMap heightMap = GetOrGenerateHeightMap(chunkCoordinate);
        Vector2 chunkWorldCentre = TerrainGrid.ChunkCoordinateToWorldPosition(chunkCoordinate, meshSettings);

        return EnvironmentSampler.TrySample(
            worldPosition,
            heightMap,
            heightMapSettings.minHeight,
            heightMapSettings.maxHeight,
            environmentDefinitions,
            vegetationSettings,
            chunkWorldCentre,
            meshSettings.numVertsPerLine,
            meshSettings.meshScale,
            out sample);
    }

    public EnvironmentSample Sample(Vector2 worldPosition)
    {
        if (!TrySample(worldPosition, out EnvironmentSample sample))
        {
            throw new InvalidOperationException("The terrain environment sampler is not configured for this query.");
        }

        return sample;
    }

    public void CacheHeightMap(Vector2Int chunkCoordinate, HeightMap heightMap)
    {
        if (heightMap.values == null) return;

        lock (cacheLock)
        {
            AddToCache(chunkCoordinate, heightMap);
        }
    }

    public void ClearCache()
    {
        lock (cacheLock)
        {
            heightMapCache.Clear();
            cacheOrder.Clear();
        }
    }

    HeightMap GetOrGenerateHeightMap(Vector2Int chunkCoordinate)
    {
        lock (cacheLock)
        {
            if (heightMapCache.TryGetValue(chunkCoordinate, out HeightMap cachedHeightMap))
            {
                return cachedHeightMap;
            }
        }

        Vector2 chunkWorldCentre = TerrainGrid.ChunkCoordinateToWorldPosition(chunkCoordinate, meshSettings);
        Vector2 sampleCentre = chunkWorldCentre / meshSettings.meshScale;
        HeightMap generatedHeightMap = HeightMapGenerator.GenerateHeightMap(
            meshSettings.numVertsPerLine,
            meshSettings.numVertsPerLine,
            heightMapSettings,
            sampleCentre);

        lock (cacheLock)
        {
            if (heightMapCache.TryGetValue(chunkCoordinate, out HeightMap cachedHeightMap))
            {
                return cachedHeightMap;
            }

            AddToCache(chunkCoordinate, generatedHeightMap);
            return generatedHeightMap;
        }
    }

    void AddToCache(Vector2Int chunkCoordinate, HeightMap heightMap)
    {
        if (heightMapCache.ContainsKey(chunkCoordinate))
        {
            heightMapCache[chunkCoordinate] = heightMap;
            return;
        }

        heightMapCache.Add(chunkCoordinate, heightMap);
        cacheOrder.Enqueue(chunkCoordinate);

        while (heightMapCache.Count > maxCachedChunks)
        {
            Vector2Int oldestCoordinate = cacheOrder.Dequeue();
            heightMapCache.Remove(oldestCoordinate);
        }
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
