using UnityEngine;

public enum EcosystemResourcePreference : byte
{
    None,
    Grass,
    Trees,
    MixedVegetation,
    Water
}

[CreateAssetMenu(menuName = "Ecosystem/Species Definition")]
public class EcosystemSpeciesDefinition : ScriptableObject
{
    public string speciesId = "species";
    public GameObject prefab;

    [Header("Abstract population")]
    public bool seedPopulation = true;
    [Min(0f)]
    public float carryingCapacityPerCell = 24f;
    [Range(0f, 1f)]
    public float initialPopulationFraction = 0.35f;
    public bool useCustomMigrationRate;
    [Range(0f, 1f)]
    public float migrationRatePerSecond = 0.004f;

    [Header("Growth")]
    [Tooltip("Births minus deaths per animal per simulated second while the population is small. " +
             "0.011 is the founders' rate from EstimateGrowthRate.")]
    [Min(0f)]
    public float growthRatePerSecond = 0.011f;
    [Tooltip("Share of the population dying per simulated second where a cell has no room at all. " +
             "1/300 is one lifetime of the founders' 300 s lifespan.")]
    [Min(0f)]
    public float deathRatePerSecond = 1f / 300f;

    [Header("Habitat")]
    [Range(0f, 1f)]
    public float minimumLandFraction = 0.5f;
    [Range(0f, 90f)]
    public float maximumSlopeDegrees = 38f;
    [Range(0f, 1f)]
    public float minimumTemperature;
    [Range(0f, 1f)]
    public float maximumTemperature = 1f;
    public EcosystemResourcePreference resourcePreference = EcosystemResourcePreference.Grass;
    [Range(0f, 1f)]
    public float resourceImportance = 0.65f;

    [Header("Materialized animals")]
    [Min(0.01f)]
    public float populationPerGameObject = 1f;
    [Range(0, 128)]
    public int maximumMaterializedPerCell = 8;
    public float spawnHeightOffset;

    public string Id
    {
        get
        {
            if (!string.IsNullOrEmpty(speciesId)) return speciesId;
            return string.IsNullOrEmpty(name) ? "species" : name;
        }
    }

    public float GetHabitatSuitability(EcosystemCellEnvironment environment)
    {
        if (!environment.isValid || !environment.isHabitable) return 0f;
        if (environment.landFraction < minimumLandFraction) return 0f;
        if (environment.average.slopeDegrees > maximumSlopeDegrees) return 0f;
        if (environment.average.temperature < minimumTemperature || environment.average.temperature > maximumTemperature) return 0f;

        float landSuitability = Mathf.InverseLerp(minimumLandFraction, 1f, environment.landFraction);
        float slopeSuitability = GetSlopeSuitability(environment.average.slopeDegrees);
        float resourceSuitability = GetResourceSuitability(environment.average);
        float resourceFactor = Mathf.Lerp(1f, resourceSuitability, resourceImportance);
        return Mathf.Clamp01(Mathf.Lerp(0.35f, 1f, landSuitability) * slopeSuitability * resourceFactor);
    }

    public float GetPointSuitability(EnvironmentSample environment)
    {
        if (!environment.isValid || !environment.isLand) return 0f;
        if (environment.slopeDegrees > maximumSlopeDegrees) return 0f;
        if (environment.temperature < minimumTemperature || environment.temperature > maximumTemperature) return 0f;

        float slopeSuitability = GetSlopeSuitability(environment.slopeDegrees);
        return Mathf.Clamp01(slopeSuitability * Mathf.Lerp(1f, GetResourceSuitability(environment), resourceImportance));
    }

    public float GetCarryingCapacity(EcosystemCellEnvironment environment)
    {
        return carryingCapacityPerCell * GetHabitatSuitability(environment);
    }

    public float GetMigrationRate(float defaultRate)
    {
        return useCustomMigrationRate ? migrationRatePerSecond : defaultRate;
    }

    // The fastest a small population of live animals can grow: every animal matures, then breeds once per
    // cooldown until it dies of old age. Births per animal per second are the share of life spent mature
    // divided by the cooldown; deaths are one per lifespan. Food and temperature only slow this down.
    // Founders: (1 − 45 / 300) / 60 − 1 / 300 ≈ 0.011 per second.
    public static float EstimateGrowthRate(float maturitySeconds, float reproductionCooldownSeconds,
        float lifespanSeconds)
    {
        float lifespan = Mathf.Max(0.01f, lifespanSeconds);
        float matureShare = Mathf.Clamp01(1f - Mathf.Max(0f, maturitySeconds) / lifespan);
        return matureShare / Mathf.Max(0.01f, reproductionCooldownSeconds) - 1f / lifespan;
    }

    float GetSlopeSuitability(float slopeDegrees)
    {
        float maximumSlope = Mathf.Max(maximumSlopeDegrees, 0.01f);
        float steepness = Mathf.InverseLerp(maximumSlope * 0.45f, maximumSlope, slopeDegrees);
        return 1f - Mathf.SmoothStep(0f, 1f, steepness);
    }

    float GetResourceSuitability(EnvironmentSample environment)
    {
        switch (resourcePreference)
        {
            case EcosystemResourcePreference.Grass:
                return environment.grassBiomass;
            case EcosystemResourcePreference.Trees:
                return environment.treeCover;
            case EcosystemResourcePreference.MixedVegetation:
                return Mathf.Clamp01((environment.grassBiomass + environment.treeCover) * 0.5f);
            case EcosystemResourcePreference.Water:
                return environment.waterAvailability;
            default:
                return 1f;
        }
    }

    void OnValidate()
    {
        carryingCapacityPerCell = Mathf.Max(0f, carryingCapacityPerCell);
        growthRatePerSecond = Mathf.Max(0f, growthRatePerSecond);
        deathRatePerSecond = Mathf.Max(0f, deathRatePerSecond);
        maximumTemperature = Mathf.Max(minimumTemperature, maximumTemperature);
        populationPerGameObject = Mathf.Max(0.01f, populationPerGameObject);
        maximumMaterializedPerCell = Mathf.Max(0, maximumMaterializedPerCell);
    }
}
