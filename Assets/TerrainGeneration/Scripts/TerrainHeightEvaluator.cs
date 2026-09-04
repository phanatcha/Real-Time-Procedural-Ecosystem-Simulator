using UnityEngine;

public struct TerrainHeightEvaluation
{
    public float height;
    public float normalizedHeightInput;
    public float riverStrength;
    public float lakeStrength;
}

public static class TerrainHeightEvaluator
{
    public static TerrainHeightEvaluation Evaluate(float baseNoiseValue, float ridgeNoiseValue,
        Vector2 terrainPosition, HeightMapSettings settings, AnimationCurve heightCurve)
    {
        float falloffValue = settings.useFalloff
            ? FalloffGenerator.Evaluate(terrainPosition.x, terrainPosition.y, settings.worldRadius)
            : 0f;
        float noiseValue = baseNoiseValue;

        if (settings.ridgeSettings != null && settings.ridgeSettings.strength > 0f)
        {
            RidgeSettings ridge = settings.ridgeSettings;
            float edgeCloseness = settings.useFalloff
                ? falloffValue
                : FalloffGenerator.Evaluate(terrainPosition.x, terrainPosition.y, settings.worldRadius);
            float jitter = OpenSimplex2.Noise2(
                ridge.seed + 909,
                terrainPosition.x / ridge.bandJitterScale,
                terrainPosition.y / ridge.bandJitterScale) * ridge.bandJitterStrength;
            float bandDistance = Mathf.Abs(edgeCloseness - (ridge.bandCenter + jitter));
            float band = Mathf.Clamp01(1f - bandDistance / ridge.bandWidth);
            band = band * band * (3f - 2f * band);
            float ridgeAmount = ridgeNoiseValue * band * ridge.strength;
            noiseValue = Mathf.Clamp01(noiseValue + ridgeAmount * (1f - noiseValue));
        }

        float riverStrength = 0f;
        if (settings.riverSettings != null && settings.riverSettings.enabled)
        {
            RiverSettings river = settings.riverSettings;
            float riverNoise = OpenSimplex2.Noise2(
                river.seed + 1717,
                terrainPosition.x / river.scale,
                terrainPosition.y / river.scale);
            float riverMask = 1f - Mathf.Clamp01(Mathf.Abs(riverNoise) / river.width);
            riverMask = riverMask * riverMask * (3f - 2f * riverMask);
            float presence = Mathf.InverseLerp(
                river.minHeightPercent,
                river.minHeightPercent + 0.05f,
                noiseValue)
                * (1f - Mathf.InverseLerp(
                    river.maxHeightPercent - 0.05f,
                    river.maxHeightPercent,
                    noiseValue));
            riverStrength = Mathf.Clamp01(riverMask * presence);
            noiseValue = Mathf.Lerp(noiseValue, river.bedLevel, riverStrength);
        }

        float lakeStrength = 0f;
        if (settings.lakeSettings != null && settings.lakeSettings.enabled)
        {
            LakeSettings lake = settings.lakeSettings;
            float lakeNoise = (OpenSimplex2.Noise2(
                lake.seed + 2929,
                terrainPosition.x / lake.scale,
                terrainPosition.y / lake.scale) + 1f) * 0.5f;
            float lakeMask = Mathf.Clamp01(Mathf.InverseLerp(
                lake.threshold - 0.05f,
                lake.threshold,
                lakeNoise));
            lakeMask = lakeMask * lakeMask * (3f - 2f * lakeMask);
            float presence = Mathf.InverseLerp(
                lake.minHeightPercent,
                lake.minHeightPercent + 0.05f,
                noiseValue)
                * (1f - Mathf.InverseLerp(
                    lake.maxHeightPercent - 0.05f,
                    lake.maxHeightPercent,
                    noiseValue));
            lakeStrength = Mathf.Clamp01(lakeMask * presence);
            noiseValue = Mathf.Lerp(noiseValue, lake.bedLevel, lakeStrength);
        }

        if (settings.useFalloff)
        {
            noiseValue = Mathf.Clamp01(noiseValue - falloffValue);
        }

        return new TerrainHeightEvaluation
        {
            height = heightCurve.Evaluate(noiseValue) * settings.heightMultiplier,
            normalizedHeightInput = noiseValue,
            riverStrength = riverStrength,
            lakeStrength = lakeStrength
        };
    }
}
