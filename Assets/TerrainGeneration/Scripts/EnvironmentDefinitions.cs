using UnityEngine;

public enum BiomeId : byte
{
    DeepWater,
    ShallowWater,
    Shore,
    BorealForest,
    HighlandTaiga,
    Tundra,
    Mountain,
    Snow
}

public enum EnvironmentResourceType : byte
{
    Tree,
    Grass,
    Rock
}

[System.Serializable]
public struct EnvironmentSample
{
    public bool isValid;
    public Vector3 position;
    public float height;
    public float normalizedHeight;
    public Vector3 surfaceNormal;
    public float slope;
    public float slopeDegrees;
    public bool isLand;
    public bool isWater;
    public bool isShore;
    public BiomeId biome;
    public float moisture;
    public float temperature;
    public float coldness;
    public float treeCover;
    public float grassBiomass;
    public float rockDensity;
    public float riverStrength;
    public float lakeStrength;
    public float hydrology;
    public float waterAvailability;
}

public struct TerrainSurfaceSample
{
    public float height;
    public Vector3 normal;
    public float slope;
    public float riverStrength;
    public float lakeStrength;
}

[CreateAssetMenu(menuName = "Terrain/Environment Definitions")]
public class EnvironmentDefinitions : UpdatableData
{
    [Header("Canonical water and shoreline")]
    [Range(0f, 1f)]
    public float normalizedWaterLevel = 0.31f;
    [Range(0f, 1f)]
    public float shallowWaterDepth = 0.08f;
    [Range(0f, 1f)]
    public float shorelineWidth = 0.06f;

    [Header("Land biome thresholds")]
    [Range(0f, 1f)]
    public float highlandTaigaStart = 0.73f;
    [Range(0f, 1f)]
    public float tundraStart = 0.87f;
    [Range(0f, 1f)]
    public float mountainStart = 0.94f;
    [Range(0f, 1f)]
    public float snowStart = 1f;

    [Header("Moisture")]
    [Min(0.01f)]
    public float moistureScale = 300f;
    public Vector2 moistureOffset;

    [Header("Temperature")]
    [Range(0f, 1f)]
    public float baseTemperature = 0.78f;
    [Range(0f, 1f)]
    public float elevationCooling = 0.62f;
    [Range(0f, 0.5f)]
    public float temperatureVariation = 0.12f;
    [Min(0.01f)]
    public float temperatureScale = 1200f;
    public Vector2 temperatureOffset = new Vector2(417.3f, -286.9f);

    [Header("Resources")]
    [Min(0.01f)]
    public float resourcePatchScale = 180f;
    [Range(0.01f, 1f)]
    public float waterAvailabilityFalloff = 0.18f;
    [Range(0f, 1f)]
    public float moistureWaterContribution = 0.65f;

    public float ShallowWaterThreshold => Mathf.Max(0f, normalizedWaterLevel - shallowWaterDepth);
    public float ShorelineThreshold => normalizedWaterLevel;
    public float LandThreshold => Mathf.Min(1f, normalizedWaterLevel + shorelineWidth);

    public BiomeId GetBiome(float normalizedHeight)
    {
        float height = Mathf.Clamp01(normalizedHeight);

        if (height < ShallowWaterThreshold) return BiomeId.DeepWater;
        if (height < ShorelineThreshold) return BiomeId.ShallowWater;
        if (height < LandThreshold) return BiomeId.Shore;
        if (height < highlandTaigaStart) return BiomeId.BorealForest;
        if (height < tundraStart) return BiomeId.HighlandTaiga;
        if (height < mountainStart) return BiomeId.Tundra;
        if (height < snowStart) return BiomeId.Mountain;
        return BiomeId.Snow;
    }

    public float GetBiomeStartHeight(BiomeId biome)
    {
        switch (biome)
        {
            case BiomeId.DeepWater:
                return 0f;
            case BiomeId.ShallowWater:
                return ShallowWaterThreshold;
            case BiomeId.Shore:
                return ShorelineThreshold;
            case BiomeId.BorealForest:
                return LandThreshold;
            case BiomeId.HighlandTaiga:
                return highlandTaigaStart;
            case BiomeId.Tundra:
                return tundraStart;
            case BiomeId.Mountain:
                return mountainStart;
            case BiomeId.Snow:
                return snowStart;
            default:
                return 0f;
        }
    }

    public bool IsWater(float normalizedHeight)
    {
        return normalizedHeight < ShorelineThreshold;
    }

    public bool IsLand(float normalizedHeight)
    {
        return !IsWater(normalizedHeight);
    }

    public bool IsShore(float normalizedHeight)
    {
        return normalizedHeight >= ShorelineThreshold && normalizedHeight < LandThreshold;
    }

    public bool SupportsTerrestrialVegetation(float normalizedHeight)
    {
        return normalizedHeight >= LandThreshold;
    }

    public float SampleMoisture(Vector2 worldPosition)
    {
        return Mathf.Clamp01(ValueNoise.Sample((worldPosition + moistureOffset) / Mathf.Max(moistureScale, 0.01f)));
    }

    public float SampleTemperature(Vector2 worldPosition, float normalizedHeight)
    {
        float variation = ValueNoise.Sample((worldPosition + temperatureOffset) / Mathf.Max(temperatureScale, 0.01f)) * 2f - 1f;
        return Mathf.Clamp01(baseTemperature - Mathf.Clamp01(normalizedHeight) * elevationCooling + variation * temperatureVariation);
    }

    public float GetWaterAvailability(float normalizedHeight, float moisture)
    {
        return GetWaterAvailability(normalizedHeight, moisture, 0f);
    }

    public float GetWaterAvailability(float normalizedHeight, float moisture, float hydrology)
    {
        if (IsWater(normalizedHeight)) return 1f;

        float distanceAboveWater = Mathf.Max(0f, normalizedHeight - ShorelineThreshold);
        float proximity = 1f - Mathf.SmoothStep(0f, 1f, distanceAboveWater / Mathf.Max(waterAvailabilityFalloff, 0.01f));
        return Mathf.Clamp01(Mathf.Max(proximity, Mathf.Max(moisture * moistureWaterContribution, hydrology)));
    }

    public float GetBiomeResourcePotential(BiomeId biome, EnvironmentResourceType resourceType)
    {
        switch (resourceType)
        {
            case EnvironmentResourceType.Tree:
                if (biome == BiomeId.BorealForest) return 1f;
                if (biome == BiomeId.HighlandTaiga) return 0.72f;
                if (biome == BiomeId.Tundra) return 0.08f;
                return 0f;
            case EnvironmentResourceType.Grass:
                if (biome == BiomeId.BorealForest) return 1f;
                if (biome == BiomeId.HighlandTaiga) return 0.7f;
                if (biome == BiomeId.Tundra) return 0.4f;
                if (biome == BiomeId.Mountain) return 0.08f;
                return 0f;
            case EnvironmentResourceType.Rock:
                if (biome == BiomeId.BorealForest) return 0.55f;
                if (biome == BiomeId.HighlandTaiga) return 0.75f;
                if (biome == BiomeId.Tundra) return 0.9f;
                if (biome == BiomeId.Mountain) return 1f;
                if (biome == BiomeId.Snow) return 0.7f;
                return 0f;
            default:
                return 0f;
        }
    }

    public float SampleResourcePatch(Vector2 worldPosition, EnvironmentResourceType resourceType)
    {
        Vector2 salt;
        switch (resourceType)
        {
            case EnvironmentResourceType.Tree:
                salt = new Vector2(137.1f, 311.7f);
                break;
            case EnvironmentResourceType.Grass:
                salt = new Vector2(-219.4f, 83.6f);
                break;
            default:
                salt = new Vector2(496.2f, -157.8f);
                break;
        }

        float patch = ValueNoise.Sample((worldPosition + salt) / Mathf.Max(resourcePatchScale, 0.01f));
        return Mathf.Lerp(0.72f, 1f, patch);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        normalizedWaterLevel = Mathf.Clamp01(normalizedWaterLevel);
        shallowWaterDepth = Mathf.Clamp(shallowWaterDepth, 0f, normalizedWaterLevel);
        shorelineWidth = Mathf.Clamp(shorelineWidth, 0f, 1f - normalizedWaterLevel);
        highlandTaigaStart = Mathf.Clamp(highlandTaigaStart, LandThreshold, 1f);
        tundraStart = Mathf.Clamp(tundraStart, highlandTaigaStart, 1f);
        mountainStart = Mathf.Clamp(mountainStart, tundraStart, 1f);
        snowStart = Mathf.Clamp(snowStart, mountainStart, 1f);
        moistureScale = Mathf.Max(moistureScale, 0.01f);
        temperatureScale = Mathf.Max(temperatureScale, 0.01f);
        resourcePatchScale = Mathf.Max(resourcePatchScale, 0.01f);
        waterAvailabilityFalloff = Mathf.Max(waterAvailabilityFalloff, 0.01f);
        base.OnValidate();
    }
#endif
}

public static class TerrainSurfaceSampler
{
    public static bool TrySample(Vector2 worldPosition, HeightMap heightMap, Vector2 chunkWorldCentre,
        int numVertsPerLine, float meshScale, out TerrainSurfaceSample sample)
    {
        sample = default;
        if (heightMap.values == null || numVertsPerLine < 4 || meshScale <= 0f) return false;

        float halfWorldSize = (numVertsPerLine - 3) * meshScale * 0.5f;
        float fi = (worldPosition.x - chunkWorldCentre.x + halfWorldSize) / meshScale + 1f;
        float fj = (chunkWorldCentre.y + halfWorldSize - worldPosition.y) / meshScale + 1f;

        if (fi < 1f || fi > numVertsPerLine - 2f || fj < 1f || fj > numVertsPerLine - 2f) return false;

        int i0 = Mathf.Min(Mathf.FloorToInt(fi), numVertsPerLine - 3);
        int j0 = Mathf.Min(Mathf.FloorToInt(fj), numVertsPerLine - 3);

        float tx = fi - i0;
        float tz = fj - j0;
        float worldHeight = InterpolateTriangle(heightMap.values, i0, j0, tx, tz);
        float riverStrength = InterpolateTriangle(heightMap.riverStrengthValues, i0, j0, tx, tz);
        float lakeStrength = InterpolateTriangle(heightMap.lakeStrengthValues, i0, j0, tx, tz);

        int i = Mathf.RoundToInt(fi);
        int j = Mathf.RoundToInt(fj);
        float hL = heightMap.values[i - 1, j];
        float hR = heightMap.values[i + 1, j];
        float hPositiveZ = heightMap.values[i, j - 1];
        float hNegativeZ = heightMap.values[i, j + 1];
        float heightDerivativeX = (hR - hL) / (2f * meshScale);
        float heightDerivativeZ = (hPositiveZ - hNegativeZ) / (2f * meshScale);
        Vector3 normal = new Vector3(-heightDerivativeX, 1f, -heightDerivativeZ).normalized;

        sample = new TerrainSurfaceSample
        {
            height = worldHeight,
            normal = normal,
            slope = 1f - Mathf.Clamp01(normal.y),
            riverStrength = Mathf.Clamp01(riverStrength),
            lakeStrength = Mathf.Clamp01(lakeStrength)
        };
        return true;
    }

    static float InterpolateTriangle(float[,] values, int i0, int j0, float tx, float tz)
    {
        if (values == null) return 0f;

        float v00 = values[i0, j0];
        float v10 = values[i0 + 1, j0];
        float v01 = values[i0, j0 + 1];
        float v11 = values[i0 + 1, j0 + 1];

        if (tz >= tx)
        {
            return (1f - tz) * v00 + tx * v11 + (tz - tx) * v01;
        }

        return (1f - tx) * v00 + (tx - tz) * v10 + tz * v11;
    }
}

public static class EnvironmentSampler
{
    public static bool TrySample(Vector2 worldPosition, HeightMap heightMap,
        float terrainMinHeight, float terrainMaxHeight, EnvironmentDefinitions definitions,
        VegetationSettings vegetationSettings, Vector2 chunkWorldCentre,
        int numVertsPerLine, float meshScale, out EnvironmentSample sample)
    {
        if (!TrySampleBase(worldPosition, heightMap, terrainMinHeight, terrainMaxHeight, definitions,
            chunkWorldCentre, numVertsPerLine, meshScale, out sample))
        {
            return false;
        }

        sample.treeCover = GetResourceDensity(sample, definitions, vegetationSettings == null ? null : vegetationSettings.trees, EnvironmentResourceType.Tree);
        sample.grassBiomass = GetResourceDensity(sample, definitions, vegetationSettings == null ? null : vegetationSettings.grass, EnvironmentResourceType.Grass);
        sample.rockDensity = GetResourceDensity(sample, definitions, vegetationSettings == null ? null : vegetationSettings.rocks, EnvironmentResourceType.Rock);
        return true;
    }

    public static bool TrySampleBase(Vector2 worldPosition, HeightMap heightMap,
        float terrainMinHeight, float terrainMaxHeight, EnvironmentDefinitions definitions,
        Vector2 chunkWorldCentre, int numVertsPerLine, float meshScale, out EnvironmentSample sample)
    {
        sample = default;
        if (definitions == null || !TerrainSurfaceSampler.TrySample(worldPosition, heightMap, chunkWorldCentre,
            numVertsPerLine, meshScale, out TerrainSurfaceSample surface))
        {
            return false;
        }

        float normalizedHeight = Mathf.InverseLerp(terrainMinHeight, terrainMaxHeight, surface.height);
        float moisture = definitions.SampleMoisture(worldPosition);
        float temperature = definitions.SampleTemperature(worldPosition, normalizedHeight);
        float hydrology = Mathf.Max(surface.riverStrength, surface.lakeStrength);

        sample = new EnvironmentSample
        {
            isValid = true,
            position = new Vector3(worldPosition.x, surface.height, worldPosition.y),
            height = surface.height,
            normalizedHeight = normalizedHeight,
            surfaceNormal = surface.normal,
            slope = surface.slope,
            slopeDegrees = Mathf.Acos(Mathf.Clamp(surface.normal.y, -1f, 1f)) * Mathf.Rad2Deg,
            isLand = definitions.IsLand(normalizedHeight),
            isWater = definitions.IsWater(normalizedHeight),
            isShore = definitions.IsShore(normalizedHeight),
            biome = definitions.GetBiome(normalizedHeight),
            moisture = moisture,
            temperature = temperature,
            coldness = 1f - temperature,
            riverStrength = surface.riverStrength,
            lakeStrength = surface.lakeStrength,
            hydrology = hydrology,
            waterAvailability = definitions.GetWaterAvailability(normalizedHeight, moisture, hydrology)
        };
        return true;
    }

    public static float GetResourceDensity(EnvironmentSample sample, EnvironmentDefinitions definitions,
        VegetationTypeSettings settings, EnvironmentResourceType resourceType)
    {
        if (!sample.isValid || definitions == null || settings == null || !settings.enabled) return 0f;
        if (!definitions.SupportsTerrestrialVegetation(sample.normalizedHeight)) return 0f;
        if (sample.normalizedHeight < settings.minHeightPercent || sample.normalizedHeight > settings.maxHeightPercent) return 0f;
        if (sample.slope > settings.maxSlope) return 0f;
        if (sample.moisture < settings.minMoisture || sample.moisture > settings.maxMoisture) return 0f;

        float biomePotential = definitions.GetBiomeResourcePotential(sample.biome, resourceType);
        float patch = definitions.SampleResourcePatch(new Vector2(sample.position.x, sample.position.z), resourceType);
        return Mathf.Clamp01(settings.density * biomePotential * patch);
    }
}
