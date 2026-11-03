using Boreal.Temperature;
using Boreal.TerrainGeneration;
using UnityEngine;

namespace Boreal.World
{
    [DisallowMultipleComponent]
    public sealed class ProceduralTerrainTemperatureProvider : TemperatureProvider
    {
        public AnimalTerrainWorld terrainWorld;
        public bool useCachedTerrainOnly = true;
        public float coldestTemperatureCelsius = -28f;
        public float warmestTemperatureCelsius = 18f;

        // Shifted by the season on SeasonClock, as the terrain's seasonal query shifts its temperature index, and swinging
        // through the day while the day clock runs (DailyTemperature).
        public override bool TryGetTemperature(Vector3 worldPosition, out float temperatureCelsius)
        {
            if (!TryGetTemperature(worldPosition, WorldSeason.Current, out temperatureCelsius, out EnvironmentSample place,
                                   out float snowCover))
            {
                return false;
            }

            if (WorldDay.IsRunning)
            {
                DayState day = WorldDay.Current;
                temperatureCelsius += DailyTemperature.Offset(DailyTemperature.Swing(place, day.dayLength), day.timeOfDay,
                                                              day.dayLength, snowCover);
            }
            return true;
        }

        public override bool TryGetClimateTemperature(Vector3 worldPosition, out float temperatureCelsius)
        {
            return TryGetTemperature(worldPosition, WorldSeason.Neutral, out temperatureCelsius);
        }

        // The day's average temperature in this season.
        public bool TryGetTemperature(Vector3 worldPosition, SeasonState season, out float temperatureCelsius)
        {
            return TryGetTemperature(worldPosition, season, out temperatureCelsius, out _, out _);
        }

        bool TryGetTemperature(Vector3 worldPosition, SeasonState season, out float temperatureCelsius,
                               out EnvironmentSample place, out float snowCover)
        {
            temperatureCelsius = 0f;
            place = default;
            snowCover = 0f;
            ResolveTerrainWorld();
            if (terrainWorld == null || !IsFinite(worldPosition)) return false;

            bool sampled = useCachedTerrainOnly
                ? terrainWorld.TryGetCachedSample(worldPosition, out EnvironmentSample environment)
                : terrainWorld.TryGetSample(worldPosition, out environment);
            if (!sampled || !environment.isValid) return false;

            // Over water the sample describes the bed, which reads warmer the deeper it lies because temperature
            // falls with height. An animal in the water feels the surface, so use the temperature at water level.
            float normalizedTemperature = environment.temperature;
            EnvironmentDefinitions definitions = terrainWorld.Sampler != null
                ? terrainWorld.Sampler.EnvironmentDefinitions
                : null;
            if (environment.isWater && definitions != null)
            {
                normalizedTemperature = definitions.SampleTemperature(
                    new Vector2(worldPosition.x, worldPosition.z),
                    definitions.ShorelineThreshold);
            }

            environment.temperature = normalizedTemperature;
            SeasonalEnvironmentSample seasonal = SeasonalEnvironment.Evaluate(environment, season);
            temperatureCelsius = NormalizedToCelsius(
                seasonal.temperature,
                coldestTemperatureCelsius,
                warmestTemperatureCelsius);
            place = environment;
            snowCover = seasonal.snowCover;
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
}
