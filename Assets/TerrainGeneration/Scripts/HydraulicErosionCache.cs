using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;

public static class HydraulicErosionCache
{
    sealed class Entry
    {
        public readonly object gate = new object();
        public ulong signature;
        public HydraulicErosionMap map;
    }

    static readonly ConditionalWeakTable<HeightMapSettings, Entry> Entries = new ConditionalWeakTable<HeightMapSettings, Entry>();

    public static HydraulicErosionMap Get(HeightMapSettings settings, Func<float, bool> cancel = null)
    {
        if (ReferenceEquals(settings, null) || settings.erosionSettings == null || !settings.erosionSettings.enabled) return null;
        Entry entry = Entries.GetValue(settings, _ => new Entry());
        lock (entry.gate)
        {
            ulong signature = Signature(settings);
            if (entry.map != null && entry.signature == signature) return entry.map;
            HydraulicErosionMap map = Bake(settings, cancel);
            entry.signature = signature;
            entry.map = map;
            return map;
        }
    }

    public static HydraulicErosionMap Bake(HeightMapSettings settings, Func<float, bool> cancel = null)
    {
        if (ReferenceEquals(settings, null) || settings.noiseSettings == null || settings.heightCurve == null
            || settings.erosionSettings == null) throw new ArgumentException("Assign valid height and erosion settings.");
        if (settings.noiseSettings.normalizeMode != Noise.NormalizeMode.Global)
            throw new InvalidOperationException("Hydraulic erosion requires Global noise normalization so all chunks share one height field.");
        if (float.IsNaN(settings.worldRadius) || float.IsInfinity(settings.worldRadius) || settings.worldRadius <= 0f)
            throw new ArgumentException("Hydraulic erosion requires a finite, positive worldRadius.");
        if (cancel != null && cancel(0f)) throw new OperationCanceledException();

        Stopwatch timer = Stopwatch.StartNew();
        HydraulicErosionSettings erosion = settings.erosionSettings.ValidatedCopy();
        int size = erosion.resolution;
        float radius = settings.worldRadius;
        float spacing = 2f * radius / (size - 1);
        Vector2 centre = new Vector2(spacing * 0.5f, -spacing * 0.5f);
        float[,] noise = Noise.GenerateNoiseMap(size, size, settings.noiseSettings, centre, spacing);
        bool useRidges = settings.ridgeSettings != null && settings.ridgeSettings.strength > 0f;
        float[,] ridges = useRidges ? Noise.GenerateRidgedNoiseMap(size, size, settings.noiseSettings, settings.ridgeSettings, centre, spacing) : null;
        float[,] heights = new float[size, size];
        AnimationCurve curve = new AnimationCurve(settings.heightCurve.keys);
        for (int y = 0; y < size; y++)
        {
            if (y % 16 == 0 && cancel != null && cancel(0.15f * y / size)) throw new OperationCanceledException();
            for (int x = 0; x < size; x++)
            {
                Vector2 position = new Vector2(-radius + x * spacing, radius - y * spacing);
                heights[x, y] = TerrainHeightEvaluator.Evaluate(noise[x, y], useRidges ? ridges[x, y] : 0f,
                    position, settings, curve).height;
            }
        }
        int seed = unchecked(settings.noiseSettings.seed * 397 ^ erosion.seed);
        HydraulicErosionMap map = HydraulicErosion.Simulate(heights, erosion, seed, radius, settings.minHeight, settings.maxHeight,
            cancel == null ? null : new Func<float, bool>(progress => cancel(0.15f + 0.85f * progress)));
        timer.Stop();
        map.BakeMilliseconds = timer.Elapsed.TotalMilliseconds;
        return map;
    }

    public static void Invalidate(HeightMapSettings settings)
    {
        if (!ReferenceEquals(settings, null)) Entries.Remove(settings);
    }

    static ulong Signature(HeightMapSettings settings)
    {
        Fingerprint key = new Fingerprint();
        NoiseSettings noise = settings.noiseSettings;
        key.Add(noise.seed);
        key.Add((int)noise.normalizeMode);
        key.Add(noise.octaves);
        key.Add(noise.scale, noise.persistance, noise.lacunarity, noise.offset.x, noise.offset.y,
            noise.domainWarpStrength, noise.domainWarpScale, noise.globalHeightBias, noise.globalContrast);
        RidgeSettings ridge = settings.ridgeSettings;
        key.Add(ridge != null);
        if (ridge != null)
        {
            key.Add(ridge.seed);
            key.Add(ridge.octaves);
            key.Add(ridge.scale, ridge.persistance, ridge.lacunarity, ridge.strength,
                ridge.bandCenter, ridge.bandWidth, ridge.bandJitterScale, ridge.bandJitterStrength);
        }
        RiverSettings river = settings.riverSettings;
        key.Add(river != null);
        if (river != null)
        {
            key.Add(river.enabled);
            key.Add(river.seed);
            key.Add(river.scale, river.width, river.minHeightPercent, river.maxHeightPercent, river.bedLevel);
        }
        LakeSettings lake = settings.lakeSettings;
        key.Add(lake != null);
        if (lake != null)
        {
            key.Add(lake.enabled);
            key.Add(lake.seed);
            key.Add(lake.scale, lake.threshold, lake.minHeightPercent, lake.maxHeightPercent, lake.bedLevel);
        }
        key.Add(settings.useFalloff);
        key.Add(settings.worldRadius, settings.heightMultiplier);
        foreach (Keyframe frame in settings.heightCurve.keys)
        {
            key.Add(frame.time, frame.value, frame.inTangent, frame.outTangent, frame.inWeight, frame.outWeight);
            key.Add((int)frame.weightedMode);
        }
        HydraulicErosionSettings erosion = settings.erosionSettings;
        key.Add(erosion.resolution);
        key.Add(erosion.dropletCount);
        key.Add(erosion.seed);
        key.Add(erosion.maxLifetime);
        key.Add(erosion.brushRadius);
        key.Add(erosion.edgeFadeCells);
        key.Add(erosion.inertia, erosion.erosionRate, erosion.depositionRate, erosion.evaporationRate,
            erosion.sedimentCapacity, erosion.minimumCapacity, erosion.gravity, erosion.maxHeightChange);
        return key.value;
    }

    sealed class Fingerprint
    {
        public ulong value = 14695981039346656037UL;
        public void Add(int number) { unchecked { value = (value ^ (uint)number) * 1099511628211UL; } }
        public void Add(bool flag) => Add(flag ? 1 : 0);
        public void Add(params float[] numbers) { foreach (float number in numbers) Add(number.GetHashCode()); }
    }
}
