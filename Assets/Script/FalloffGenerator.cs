using UnityEngine;

public static class FalloffGenerator
{
    // Local-grid version, used only for the editor preview texture (DrawMode.FalloffMap) -
    // a self-contained square just for visualizing the curve shape.
    public static float[,] GenerateFalloffMap(int size)
    {
        float[,] map = new float[size, size];

        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                float x = i / (float)size * 2 - 1;
                float y = j / (float)size * 2 - 1;

                float value = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));
                map[i, j] = EvaluateCurve(value);
            }
        }

        return map;
    }

    // Evaluates the falloff at an absolute world position, not a position
    // local to one chunk - so every chunk agrees on where the island's
    // actual center and coastline are, instead of each one treating itself
    // as the center.
    public static float Evaluate(float worldX, float worldY, float worldRadius)
    {
        float x = worldX / worldRadius;
        float y = worldY / worldRadius;

        float value = Mathf.Clamp01(Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)));
        return EvaluateCurve(value);
    }

    static float EvaluateCurve(float value)
    {
        float a = 3;
        float b = 2.2f;

        return Mathf.Pow(value, a) / (Mathf.Pow(value, a) + Mathf.Pow(b - b * value, a));
    }
}
