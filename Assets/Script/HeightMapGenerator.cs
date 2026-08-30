using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class HeightMapGenerator {

	public static HeightMap GenerateHeightMap(int width, int height, HeightMapSettings settings, Vector2 sampleCentre) {
		float[,] values = Noise.GenerateNoiseMap (width, height, settings.noiseSettings, sampleCentre);

		bool useRidges = settings.ridgeSettings != null && settings.ridgeSettings.strength > 0f;
		float[,] ridgeValues = useRidges
			? Noise.GenerateRidgedNoiseMap (width, height, settings.noiseSettings, settings.ridgeSettings, sampleCentre)
			: null;

		AnimationCurve heightCurve_threadsafe = new AnimationCurve (settings.heightCurve.keys);

		float minValue = float.MaxValue;
		float maxValue = float.MinValue;

		float halfSize = width / 2f;

		for (int i = 0; i < width; i++) {
			for (int j = 0; j < height; j++) {
				float worldX = sampleCentre.x + (i - halfSize);
				float worldY = sampleCentre.y - (j - halfSize);

				float falloffValue = settings.useFalloff
					? FalloffGenerator.Evaluate(worldX, worldY, settings.worldRadius)
					: 0f;

				float noiseValue = values[i, j];

				if (useRidges) {
					float edgeCloseness = settings.useFalloff
						? falloffValue
						: FalloffGenerator.Evaluate(worldX, worldY, settings.worldRadius);

					RidgeSettings r = settings.ridgeSettings;
					float jitter = OpenSimplex2.Noise2(r.seed + 909, worldX / r.bandJitterScale, worldY / r.bandJitterScale) * r.bandJitterStrength;
					float bandDistance = Mathf.Abs(edgeCloseness - (r.bandCenter + jitter));
					float band = Mathf.Clamp01(1f - bandDistance / r.bandWidth);
					band = band * band * (3f - 2f * band);

					noiseValue = Mathf.Clamp01(noiseValue + ridgeValues[i, j] * band * r.strength);
				}

				if (settings.useFalloff) {
					noiseValue = Mathf.Clamp01(noiseValue - falloffValue);
				}

				values [i, j] = heightCurve_threadsafe.Evaluate (noiseValue) * settings.heightMultiplier;

				if (values [i, j] > maxValue) {
					maxValue = values [i, j];
				}
				if (values [i, j] < minValue) {
					minValue = values [i, j];
				}
			}
		}

		return new HeightMap (values, minValue, maxValue);
	}

}

public struct HeightMap {
	public readonly float[,] values;
	public readonly float minValue;
	public readonly float maxValue;

	public HeightMap (float[,] values, float minValue, float maxValue)
	{
		this.values = values;
		this.minValue = minValue;
		this.maxValue = maxValue;
	}
}
