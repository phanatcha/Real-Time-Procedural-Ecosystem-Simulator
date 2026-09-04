using UnityEngine;

public static class HeightMapGenerator
{
    public static HeightMap GenerateHeightMap(int width, int height, HeightMapSettings settings, Vector2 sampleCentre)
    {
        float[,] baseNoiseValues = Noise.GenerateNoiseMap(width, height, settings.noiseSettings, sampleCentre);

        bool useRidges = settings.ridgeSettings != null && settings.ridgeSettings.strength > 0f;
        float[,] ridgeValues = useRidges
            ? Noise.GenerateRidgedNoiseMap(width, height, settings.noiseSettings, settings.ridgeSettings, sampleCentre)
            : null;

        float[,] heightValues = new float[width, height];
        float[,] riverStrengthValues = new float[width, height];
        float[,] lakeStrengthValues = new float[width, height];
        AnimationCurve heightCurve = new AnimationCurve(settings.heightCurve.keys);

        float minValue = float.MaxValue;
        float maxValue = float.MinValue;
        float halfWidth = width / 2f;
        float halfHeight = height / 2f;

        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                Vector2 terrainPosition = new Vector2(
                    sampleCentre.x + i - halfWidth,
                    sampleCentre.y - (j - halfHeight));

                TerrainHeightEvaluation evaluation = TerrainHeightEvaluator.Evaluate(
                    baseNoiseValues[i, j],
                    useRidges ? ridgeValues[i, j] : 0f,
                    terrainPosition,
                    settings,
                    heightCurve);

                heightValues[i, j] = evaluation.height;
                riverStrengthValues[i, j] = evaluation.riverStrength;
                lakeStrengthValues[i, j] = evaluation.lakeStrength;
                minValue = Mathf.Min(minValue, evaluation.height);
                maxValue = Mathf.Max(maxValue, evaluation.height);
            }
        }

        return new HeightMap(heightValues, minValue, maxValue, riverStrengthValues, lakeStrengthValues);
    }
}

public struct HeightMap
{
    public readonly float[,] values;
    public readonly float minValue;
    public readonly float maxValue;
    public readonly float[,] riverStrengthValues;
    public readonly float[,] lakeStrengthValues;

    public HeightMap(float[,] values, float minValue, float maxValue)
        : this(values, minValue, maxValue, null, null)
    {
    }

    public HeightMap(float[,] values, float minValue, float maxValue,
        float[,] riverStrengthValues, float[,] lakeStrengthValues)
    {
        this.values = values;
        this.minValue = minValue;
        this.maxValue = maxValue;
        this.riverStrengthValues = riverStrengthValues;
        this.lakeStrengthValues = lakeStrengthValues;
    }
}
