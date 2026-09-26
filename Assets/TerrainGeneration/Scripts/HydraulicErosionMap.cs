using UnityEngine;

public sealed class HydraulicErosionMap
{
    readonly float[] deltas;
    readonly float[] flow;
    public int Resolution { get; }
    public float Radius { get; }
    public float MinimumHeight { get; }
    public float MaximumHeight { get; }
    public int Seed { get; }
    public double RemovedHeightSum { get; }
    public double DepositedHeightSum { get; }
    public double UnsettledHeightSum { get; }
    public double BakeMilliseconds { get; internal set; }
    public float MaximumFlow { get; }

    internal HydraulicErosionMap(float[] deltas, float[] flow, int resolution, float radius,
        float minimumHeight, float maximumHeight, int seed, double removed, double deposited, double unsettled)
    {
        this.deltas = deltas;
        this.flow = flow;
        Resolution = resolution;
        Radius = radius;
        MinimumHeight = minimumHeight;
        MaximumHeight = maximumHeight;
        Seed = seed;
        RemovedHeightSum = removed;
        DepositedHeightSum = deposited;
        UnsettledHeightSum = unsettled;
        float peak = 0f;
        foreach (float value in flow) peak = Mathf.Max(peak, value);
        MaximumFlow = peak;
    }

    public float SampleDelta(Vector2 terrainPosition) => Sample(deltas, terrainPosition);
    public float SampleFlow(Vector2 terrainPosition) => Sample(flow, terrainPosition);

    public float ApplyHeight(Vector2 terrainPosition, float originalHeight)
    {
        float delta = SampleDelta(terrainPosition);
        return delta == 0f ? originalHeight : Mathf.Clamp(originalHeight + delta, MinimumHeight, MaximumHeight);
    }

    float Sample(float[] values, Vector2 position)
    {
        if (position.x <= -Radius || position.x >= Radius || position.y <= -Radius || position.y >= Radius) return 0f;
        float x = (position.x + Radius) / (2f * Radius) * (Resolution - 1);
        float y = (Radius - position.y) / (2f * Radius) * (Resolution - 1);
        int ix = Mathf.Clamp(Mathf.FloorToInt(x), 0, Resolution - 2);
        int iy = Mathf.Clamp(Mathf.FloorToInt(y), 0, Resolution - 2);
        float tx = x - ix;
        float ty = y - iy;
        int index = iy * Resolution + ix;
        return Mathf.Lerp(Mathf.Lerp(values[index], values[index + 1], tx),
            Mathf.Lerp(values[index + Resolution], values[index + Resolution + 1], tx), ty);
    }
}
