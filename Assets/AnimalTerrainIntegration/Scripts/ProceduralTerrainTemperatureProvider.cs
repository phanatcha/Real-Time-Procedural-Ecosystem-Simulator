using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProceduralTerrainTemperatureProvider : TemperatureProvider
{
    public AnimalTerrainWorld terrainWorld;
    public bool useCachedTerrainOnly = true;
    public float coldestTemperatureCelsius = -28f;
    public float warmestTemperatureCelsius = 18f;

    public override bool TryGetTemperature(Vector3 worldPosition, out float temperatureCelsius)
    {
        temperatureCelsius = 0f;
        ResolveTerrainWorld();
        if (terrainWorld == null || !IsFinite(worldPosition)) return false;

        bool sampled = useCachedTerrainOnly
            ? terrainWorld.TryGetCachedSample(worldPosition, out EnvironmentSample environment)
            : terrainWorld.TryGetSample(worldPosition, out environment);
        if (!sampled || !environment.isValid) return false;

        temperatureCelsius = NormalizedToCelsius(
            environment.temperature,
            coldestTemperatureCelsius,
            warmestTemperatureCelsius);
        return IsFinite(temperatureCelsius);
    }

    public static float NormalizedToCelsius(float normalizedTemperature,
        float coldestTemperatureCelsius, float warmestTemperatureCelsius)
    {
        float minimum = Mathf.Min(coldestTemperatureCelsius, warmestTemperatureCelsius);
        float maximum = Mathf.Max(coldestTemperatureCelsius, warmestTemperatureCelsius);
        return Mathf.Lerp(minimum, maximum, Mathf.Clamp01(normalizedTemperature));
    }

    void Awake()
    {
        ResolveTerrainWorld();
    }

    void ResolveTerrainWorld()
    {
        if (terrainWorld == null) terrainWorld = AnimalTerrainWorld.Active;
        if (terrainWorld == null) terrainWorld = FindAnyObjectByType<AnimalTerrainWorld>();
    }
}
