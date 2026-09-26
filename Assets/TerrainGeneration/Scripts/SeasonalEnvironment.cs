using System;
using UnityEngine;

public enum BorealSeason { Spring, Summer, Fall, Winter }

[Serializable]
public struct SeasonState
{
    public BorealSeason season;
    public float temperatureOffset;
    public float moistureOffset;
    public float growthMultiplier;
    public float snowAmount;
    public float dormancy;

    public static SeasonState For(BorealSeason season)
    {
        switch (season)
        {
            case BorealSeason.Spring: return Create(season, -0.16f, 0.18f, 0.7f, 0.5f, 0.15f);
            case BorealSeason.Summer: return Create(season, 0.08f, -0.04f, 1f, 0f, 0f);
            case BorealSeason.Fall: return Create(season, -0.12f, 0.08f, 0.35f, 0.08f, 0.85f);
            case BorealSeason.Winter: return Create(season, -0.6f, -0.12f, 0.05f, 1f, 1f);
            default: throw new ArgumentOutOfRangeException(nameof(season));
        }
    }

    public static SeasonState AtYearProgress(float yearProgress)
    {
        if (float.IsNaN(yearProgress) || float.IsInfinity(yearProgress)) throw new ArgumentOutOfRangeException(nameof(yearProgress));
        float phase = Mathf.Repeat(yearProgress, 1f) * 4f;
        int index = Mathf.FloorToInt(phase);
        SeasonState a = For((BorealSeason)index);
        SeasonState b = For((BorealSeason)((index + 1) % 4));
        float blend = Mathf.SmoothStep(0f, 1f, phase - index);
        return Create(blend < 0.5f ? a.season : b.season,
            Mathf.Lerp(a.temperatureOffset, b.temperatureOffset, blend), Mathf.Lerp(a.moistureOffset, b.moistureOffset, blend),
            Mathf.Lerp(a.growthMultiplier, b.growthMultiplier, blend), Mathf.Lerp(a.snowAmount, b.snowAmount, blend),
            Mathf.Lerp(a.dormancy, b.dormancy, blend));
    }

    public SeasonState Validated()
    {
        return Create(season, Clamp(temperatureOffset, -1f, 1f), Clamp(moistureOffset, -1f, 1f),
            Clamp(growthMultiplier, 0f, 1f), Clamp(snowAmount, 0f, 1f), Clamp(dormancy, 0f, 1f));
    }

    static SeasonState Create(BorealSeason season, float temperature, float moisture, float growth, float snow, float dormancy)
        => new SeasonState { season = season, temperatureOffset = temperature, moistureOffset = moisture,
            growthMultiplier = growth, snowAmount = snow, dormancy = dormancy };

    static float Clamp(float value, float min, float max)
        => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, min, max);
}

[Serializable]
public struct SeasonalEnvironmentSample
{
    public EnvironmentSample baseEnvironment;
    public SeasonState season;
    public float temperature;
    public float coldness;
    public float moisture;
    public float snowCover;
    public float grassGrowthMultiplier;
    public float accessibleGrassBiomass;
    public float movementCostMultiplier;
    public bool IsValid => baseEnvironment.isValid;
}

public static class SeasonalEnvironment
{
    public static SeasonalEnvironmentSample Evaluate(EnvironmentSample environment, SeasonState configuration)
    {
        if (!environment.isValid) return default;
        SeasonState state = configuration.Validated();
        float temperature = Mathf.Clamp01(environment.temperature + state.temperatureOffset);
        float moisture = Mathf.Clamp01(environment.moisture + state.moistureOffset);
        float patch = ValueNoise.Sample(new Vector2(environment.position.x, environment.position.z) / 31f);
        float retention = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.55f, temperature));
        float slopeRetention = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.8f, environment.slope));
        float snow = environment.isLand
            ? Mathf.Clamp01(state.snowAmount * Mathf.Lerp(0.35f, 1f, retention) * Mathf.Lerp(0.72f, 1f, patch) * slopeRetention)
            : 0f;
        return new SeasonalEnvironmentSample
        {
            baseEnvironment = environment,
            season = state,
            temperature = temperature,
            coldness = 1f - temperature,
            moisture = moisture,
            snowCover = snow,
            grassGrowthMultiplier = environment.isLand ? state.growthMultiplier * Mathf.Lerp(0.25f, 1f, moisture) : 0f,
            accessibleGrassBiomass = environment.isLand
                ? Mathf.Clamp01(environment.grassBiomass) * (1f - 0.9f * snow) * Mathf.Lerp(1f, 0.75f, state.dormancy) : 0f,
            movementCostMultiplier = 1f + snow * 0.75f
        };
    }
}
