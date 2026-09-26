using System;
using System.Collections.Generic;
using UnityEngine;

public static class HydraulicErosion
{
    struct BrushPoint
    {
        public int x;
        public int y;
        public float weight;
    }

    public static HydraulicErosionMap Simulate(float[,] original, HydraulicErosionSettings configuration,
        int seed, float radius, float minimumHeight, float maximumHeight, Func<float, bool> cancel = null)
    {
        if (original == null || original.GetLength(0) != original.GetLength(1) || original.GetLength(0) < 3)
            throw new ArgumentException("Erosion needs a square height grid of at least 3 x 3 samples.");
        if (configuration == null || !Finite(radius) || radius <= 0f || !Finite(minimumHeight)
            || !Finite(maximumHeight) || maximumHeight < minimumHeight)
            throw new ArgumentException("Erosion needs finite, ordered height bounds and a positive world radius.");

        HydraulicErosionSettings settings = configuration.ValidatedCopy();
        int size = original.GetLength(0);
        int count = size * size;
        float scale = Mathf.Max(0.0001f, maximumHeight - minimumHeight);
        float[] baseline = new float[count];
        float[] heights = new float[count];
        float[] floors = new float[count];
        float[] ceilings = new float[count];
        float[] flow = new float[count];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float value = original[x, y];
                if (!Finite(value)) throw new ArgumentException("Erosion cannot process non-finite height samples.");
                int index = y * size + x;
                baseline[index] = heights[index] = (value - minimumHeight) / scale;
                float edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
                float limit = settings.maxHeightChange / scale * Mathf.SmoothStep(0f, 1f, edge / settings.edgeFadeCells);
                floors[index] = Mathf.Min(heights[index], Mathf.Max(0f, heights[index] - limit));
                ceilings[index] = Mathf.Max(heights[index], Mathf.Min(1f, heights[index] + limit));
            }

        BrushPoint[] brush = MakeBrush(settings.brushRadius);
        System.Random random = new System.Random(seed);
        double removed = 0d;
        double deposited = 0d;
        double unsettled = 0d;
        for (int drop = 0; drop < settings.dropletCount && settings.maxHeightChange > 0f; drop++)
        {
            if (drop % 256 == 0 && cancel != null && cancel(drop / (float)settings.dropletCount))
                throw new OperationCanceledException();
            float x = (float)random.NextDouble() * (size - 1.001f);
            float y = (float)random.NextDouble() * (size - 1.001f);
            Vector2 direction = Vector2.zero;
            float speed = 1f;
            float water = 1f;
            float sediment = 0f;
            for (int age = 0; age < settings.maxLifetime && water > 0.01f; age++)
            {
                float currentHeight = HeightAndGradient(heights, size, x, y, out Vector2 gradient);
                AddFlow(flow, size, x, y, water);
                direction = direction * settings.inertia - gradient * (1f - settings.inertia);
                if (direction.sqrMagnitude < 1e-12f) break;
                direction.Normalize();
                float nextX = x + direction.x;
                float nextY = y + direction.y;
                if (nextX < 0f || nextY < 0f || nextX >= size - 1f || nextY >= size - 1f) break;

                float nextHeight = HeightAndGradient(heights, size, nextX, nextY, out _);
                float rise = nextHeight - currentHeight;
                float capacity = Mathf.Max(settings.minimumCapacity,
                    Mathf.Max(0f, -rise) * speed * water * settings.sedimentCapacity);
                if (rise > 0f || sediment > capacity)
                {
                    float amount = rise > 0f ? Mathf.Min(sediment, rise) : (sediment - capacity) * settings.depositionRate;
                    float actual = Deposit(heights, ceilings, size, x, y, amount);
                    sediment = Mathf.Max(0f, sediment - actual);
                    deposited += actual;
                }
                else if (rise < 0f)
                {
                    float amount = Mathf.Min((capacity - sediment) * settings.erosionRate, -rise);
                    float actual = Erode(heights, floors, size, (int)x, (int)y, amount, brush);
                    sediment += actual;
                    removed += actual;
                }

                speed = Mathf.Sqrt(Mathf.Max(0f, speed * speed - rise * settings.gravity));
                water *= 1f - settings.evaporationRate;
                x = nextX;
                y = nextY;
            }

            float settled = Deposit(heights, ceilings, size, x, y, sediment);
            deposited += settled;
            unsettled += Mathf.Max(0f, sediment - settled);
        }

        float[] deltas = new float[count];
        for (int index = 0; index < count; index++) deltas[index] = (heights[index] - baseline[index]) * scale;
        return new HydraulicErosionMap(deltas, flow, size, radius, minimumHeight, maximumHeight, seed,
            removed * scale, deposited * scale, unsettled * scale);
    }

    static float HeightAndGradient(float[] heights, int size, float x, float y, out Vector2 gradient)
    {
        int ix = (int)x;
        int iy = (int)y;
        float tx = x - ix;
        float ty = y - iy;
        int index = iy * size + ix;
        float nw = heights[index];
        float ne = heights[index + 1];
        float sw = heights[index + size];
        float se = heights[index + size + 1];
        gradient = new Vector2(Mathf.Lerp(ne - nw, se - sw, ty), Mathf.Lerp(sw - nw, se - ne, tx));
        return Mathf.Lerp(Mathf.Lerp(nw, ne, tx), Mathf.Lerp(sw, se, tx), ty);
    }

    static float Deposit(float[] heights, float[] ceilings, int size, float x, float y, float amount)
    {
        if (amount <= 0f) return 0f;
        int ix = (int)x;
        int iy = (int)y;
        float tx = x - ix;
        float ty = y - iy;
        int index = iy * size + ix;
        return Raise(heights, ceilings, index, amount * (1f - tx) * (1f - ty))
            + Raise(heights, ceilings, index + 1, amount * tx * (1f - ty))
            + Raise(heights, ceilings, index + size, amount * (1f - tx) * ty)
            + Raise(heights, ceilings, index + size + 1, amount * tx * ty);
    }

    static float Raise(float[] heights, float[] ceilings, int index, float amount)
    {
        float before = heights[index];
        heights[index] = Mathf.Min(ceilings[index], before + amount);
        return heights[index] - before;
    }

    static void AddFlow(float[] flow, int size, float x, float y, float water)
    {
        int index = (int)y * size + (int)x;
        float tx = x - (int)x;
        float ty = y - (int)y;
        flow[index] += water * (1f - tx) * (1f - ty);
        flow[index + 1] += water * tx * (1f - ty);
        flow[index + size] += water * (1f - tx) * ty;
        flow[index + size + 1] += water * tx * ty;
    }

    static float Erode(float[] heights, float[] floors, int size, int x, int y, float amount, BrushPoint[] brush)
    {
        float total = 0f;
        foreach (BrushPoint point in brush)
        {
            int nx = x + point.x;
            int ny = y + point.y;
            if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
            int index = ny * size + nx;
            float before = heights[index];
            heights[index] = Mathf.Max(floors[index], before - amount * point.weight);
            total += before - heights[index];
        }
        return total;
    }

    static BrushPoint[] MakeBrush(int radius)
    {
        List<BrushPoint> points = new List<BrushPoint>();
        float sum = 0f;
        for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                float distance = Mathf.Sqrt(x * x + y * y);
                if (distance >= radius) continue;
                float weight = 1f - distance / radius;
                points.Add(new BrushPoint { x = x, y = y, weight = weight });
                sum += weight;
            }
        BrushPoint[] brush = points.ToArray();
        for (int index = 0; index < brush.Length; index++) brush[index].weight /= sum;
        return brush;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
