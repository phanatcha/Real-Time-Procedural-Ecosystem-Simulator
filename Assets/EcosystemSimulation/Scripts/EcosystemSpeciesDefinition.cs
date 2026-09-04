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
        maximumTemperature = Mathf.Max(minimumTemperature, maximumTemperature);
        populationPerGameObject = Mathf.Max(0.01f, populationPerGameObject);
        maximumMaterializedPerCell = Mathf.Max(0, maximumMaterializedPerCell);
    }
}
