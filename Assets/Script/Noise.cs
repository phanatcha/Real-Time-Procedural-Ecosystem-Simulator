using Unity.Mathematics;
using UnityEngine;

public static class Noise
{
    public enum NormalizeMode {Local, Global};

    static Vector2 DomainWarp(long seed, float worldX, float worldY, float warpStrength, float warpScale)
    {
        if (warpStrength <= 0) return Vector2.zero;

        float wx = worldX / warpScale;
        float wy = worldY / warpScale;
        float warpX = OpenSimplex2.Noise2(seed + 101, wx, wy) * warpStrength;
        float warpY = OpenSimplex2.Noise2(seed + 202, wx + 5.2, wy + 1.3) * warpStrength;
        return new Vector2(warpX, warpY);
    }

    public static float[,] GenerateNoiseMap(int mapWidth, int mapHeight, int seed, float scale, int octaves, float persistance, float lacunarity, Vector2 offset, NormalizeMode normalizeMode, float warpStrength = 0f, float warpScale = 400f)
    {
        float[,] noiseMap = new float[mapWidth, mapHeight];

        System.Random prng = new System.Random(seed);
        Vector2[] octaveOffsets = new Vector2[octaves];

        float maxPossibleHeight = 0;
        float amplitude = 1;
        float frequency = 1;

        for (int i = 0; i < octaves; i++)
        {
            float offsetX = prng.Next(-100000, 100000) + offset.x;
            float offsetY = prng.Next(-100000, 100000) - offset.y;
            octaveOffsets[i] = new Vector2(offsetX, offsetY);

            maxPossibleHeight += amplitude;
            amplitude *= persistance;
        }

        long noiseSeed = seed;

        if (scale <= 0)
        {
            scale = 0.0001f;
        }

        float maxLocalNoiseHeight = float.MinValue;
        float minLocalNoiseHeight = float.MaxValue;

        float halfWidth = mapWidth / 2f;
        float halfHeight = mapHeight  / 2f;

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                Vector2 warp = DomainWarp(noiseSeed, x - halfWidth + offset.x, offset.y - (y - halfHeight), warpStrength, warpScale);

                float noiseHeight = 0;
                amplitude = 1;
                frequency = 1;
                for (int i = 0; i < octaves; i++)
                {
                    float sampleX = (x-halfWidth + octaveOffsets[i].x + warp.x) / scale * frequency;
                    float sampleY = (y-halfHeight + octaveOffsets[i].y + warp.y) / scale * frequency;

                    float noiseValue = OpenSimplex2.Noise2(noiseSeed, sampleX, sampleY);
                    noiseHeight += noiseValue * amplitude;

                    amplitude *= persistance;
                    frequency *= lacunarity;
                }

                if (noiseHeight > maxLocalNoiseHeight)
                {
                    maxLocalNoiseHeight = noiseHeight;
                }
                else if (noiseHeight < minLocalNoiseHeight)
                {
                    minLocalNoiseHeight = noiseHeight;
                }
                noiseMap[x, y] = noiseHeight;
            }
        }

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x =0; x< mapWidth; x++)
            {
                if (normalizeMode == NormalizeMode.Local)
                {
                    noiseMap[x,y] = Mathf.InverseLerp(minLocalNoiseHeight, maxLocalNoiseHeight, noiseMap[x,y]);
                }
                else
                {
                    float normalizedHeight = (noiseMap[x,y] + 1) / (maxPossibleHeight / 0.9f);
                    noiseMap[x,y] = Mathf.Clamp01(normalizedHeight);
                }
            }
        }
        return noiseMap;
    }
    public static float[,] GenerateNoiseMap(int mapWidth, int mapHeight, NoiseSettings settings, Vector2 sampleCentre)
    {
        return GenerateNoiseMap(mapWidth, mapHeight, settings.seed, settings.scale, settings.octaves, settings.persistance, settings.lacunarity, sampleCentre + settings.offset, settings.normalizeMode, settings.domainWarpStrength, settings.domainWarpScale);
    }

    public static float[,] GenerateRidgedNoiseMap(int mapWidth, int mapHeight, int seed, float scale, int octaves, float persistance, float lacunarity, Vector2 offset, float warpStrength, float warpScale)
    {
        float[,] noiseMap = new float[mapWidth, mapHeight];

        System.Random prng = new System.Random(seed);
        Vector2[] octaveOffsets = new Vector2[octaves];

        float maxPossibleHeight = 0;
        float amplitude = 1;

        for (int i = 0; i < octaves; i++)
        {
            float offsetX = prng.Next(-100000, 100000) + offset.x;
            float offsetY = prng.Next(-100000, 100000) - offset.y;
            octaveOffsets[i] = new Vector2(offsetX, offsetY);

            maxPossibleHeight += amplitude;
            amplitude *= persistance;
        }

        long noiseSeed = seed;

        if (scale <= 0)
        {
            scale = 0.0001f;
        }

        float halfWidth = mapWidth / 2f;
        float halfHeight = mapHeight / 2f;

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {

                Vector2 warp = DomainWarp(noiseSeed, x - halfWidth + offset.x, offset.y - (y - halfHeight), warpStrength, warpScale);

                float noiseHeight = 0;
                amplitude = 1;
                float frequency = 1;
                for (int i = 0; i < octaves; i++)
                {
                    float sampleX = (x - halfWidth + octaveOffsets[i].x + warp.x) / scale * frequency;
                    float sampleY = (y - halfHeight + octaveOffsets[i].y + warp.y) / scale * frequency;

                    float n = OpenSimplex2.Noise2(noiseSeed, sampleX, sampleY);
                    float ridge = 1f - Mathf.Abs(n);
                    ridge *= ridge;
                    noiseHeight += ridge * amplitude;

                    amplitude *= persistance;
                    frequency *= lacunarity;
                }

                noiseMap[x, y] = Mathf.Clamp01(noiseHeight / maxPossibleHeight);
            }
        }

        return noiseMap;
    }

    public static float[,] GenerateRidgedNoiseMap(int mapWidth, int mapHeight, NoiseSettings baseSettings, RidgeSettings ridgeSettings, Vector2 sampleCentre)
    {
        return GenerateRidgedNoiseMap(mapWidth, mapHeight, ridgeSettings.seed, ridgeSettings.scale, ridgeSettings.octaves, ridgeSettings.persistance, ridgeSettings.lacunarity, sampleCentre + baseSettings.offset, baseSettings.domainWarpStrength, baseSettings.domainWarpScale);
    }

}

[System.Serializable]
public class NoiseSettings {
	public Noise.NormalizeMode normalizeMode;

	public float scale = 90;

	public int octaves = 4;
	[Range(0,1)]
	public float persistance =.45f;
	public float lacunarity = 2.2f;

	public int seed;
	public Vector2 offset;

	[Header("Domain Warp")]
	[Tooltip("Distorts sample coordinates so contours stop looking like a grid of pure noise bumps. 0 = off.")]
	public float domainWarpStrength = 0f;
	public float domainWarpScale = 400f;

	public void ValidateValues() {
		scale = Mathf.Max (scale, 0.01f);
		octaves = Mathf.Max (octaves, 1);
		lacunarity = Mathf.Max (lacunarity, 1);
		persistance = Mathf.Clamp01 (persistance);
		domainWarpScale = Mathf.Max (domainWarpScale, 0.01f);
	}
}

[System.Serializable]
public class RidgeSettings {
	public int seed;
	public float scale = 220f;
	public int octaves = 4;
	[Range(0,1)]
	public float persistance = 0.55f;
	public float lacunarity = 2f;

	[Header("Blend")]
	[Tooltip("Max contribution of ridged noise where the band is at its strongest.")]
	[Range(0,1)]
	public float strength = 0.6f;

	[Header("Mountain Band")]
	[Tooltip("Where the mountain band is centred, in falloff-curve units (0 = world centre, 1 = coastline).")]
	[Range(0,1)]
	public float bandCenter = 0.55f;
	[Range(0.01f,1)]
	public float bandWidth = 0.28f;
	[Tooltip("Large-scale noise that bends the band's position by location, so it isn't a perfectly uniform ring.")]
	public float bandJitterScale = 900f;
	[Range(0,1)]
	public float bandJitterStrength = 0.3f;

	public void ValidateValues() {
		scale = Mathf.Max (scale, 0.01f);
		octaves = Mathf.Max (octaves, 1);
		lacunarity = Mathf.Max (lacunarity, 1);
		persistance = Mathf.Clamp01 (persistance);
		bandWidth = Mathf.Max (bandWidth, 0.01f);
		bandJitterScale = Mathf.Max (bandJitterScale, 0.01f);
	}
}