using UnityEngine;

public struct TerrainHeightEvaluation
{
    public float height;
    public float normalizedHeightInput;
    public float riverStrength;
    public float lakeStrength;
    public float valleyStrength;
    public float heightAfterRidges;
    public float falloff;
    public float heightBeforeErosion;
    public float erosionDelta;
}

public static class TerrainHeightEvaluator
{
    public static TerrainHeightEvaluation Evaluate(float baseNoiseValue, float ridgeNoiseValue,
        Vector2 terrainPosition, HeightMapSettings settings, AnimationCurve heightCurve, HydraulicErosionMap erosion = null)
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

        float heightAfterRidges = noiseValue;

        // The island falloff comes before the carving, so the river and lake height ranges are tested against the
        // height the ground really ends up with, and a bed level is the channel's final height before the curve.
        // Testing them before the falloff made rivers pick ground the falloff later sank into the sea.
        if (settings.useFalloff)
        {
            noiseValue = Mathf.Clamp01(noiseValue - falloffValue);
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
            float lakeNoise = LakeNoise(lake, terrainPosition);
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

        // Valleys come last and only cut the ground down, so rivers and lakes already carved stay as they are. Each
        // valley's own river is cut into its floor down to the rivers' bed level.
        float valleyStrength = 0f;
        float valleyRiverStrength = 0f;
        if (settings.valleySettings != null && settings.valleySettings.enabled)
        {
            ValleySettings valley = settings.valleySettings;
            float distance = ValleyDistance(valley, terrainPosition);
            float cut = Mathf.Clamp01(valley.strength);
            if (valley.shoulderHeight > 0f)
            {
                // Land beside the valley rises from the floor's edge to a crest a third of the way across the
                // shoulder, then falls away; raising it before the cut makes the valley walls taller too.
                float crest = valley.halfWidth + valley.shoulderWidth / 3f;
                float rise = Mathf.InverseLerp(valley.floorHalfWidth, crest, distance);
                float fall = 1f - Mathf.InverseLerp(crest, valley.halfWidth + valley.shoulderWidth, distance);
                float shoulder = Mathf.Min(rise * rise * (3f - 2f * rise), fall * fall * (3f - 2f * fall));
                float onLand = Mathf.InverseLerp(valley.floorLevel - 0.05f, valley.floorLevel + 0.05f, noiseValue);
                noiseValue += (1f - noiseValue) * valley.shoulderHeight * shoulder * onLand * cut;
            }

            float wall = Mathf.InverseLerp(valley.floorHalfWidth, valley.halfWidth, distance);
            valleyStrength = 1f - wall * wall * (3f - 2f * wall);
            noiseValue = Mathf.Min(noiseValue, Mathf.Lerp(noiseValue, valley.floorLevel, valleyStrength * cut));

            if (valley.riverHalfWidth > 0f)
            {
                float channel = 1f - Mathf.Clamp01(distance / valley.riverHalfWidth);
                channel = channel * channel * (3f - 2f * channel) * cut;
                float bed = settings.riverSettings != null ? settings.riverSettings.bedLevel : valley.floorLevel;
                float carved = Mathf.Lerp(noiseValue, bed, channel);
                if (carved < noiseValue)
                {
                    valleyRiverStrength = channel;
                    noiseValue = carved;
                }
            }
        }

        riverStrength = Mathf.Max(riverStrength, valleyRiverStrength);

        float beforeErosion = heightCurve.Evaluate(noiseValue) * settings.heightMultiplier;
        float height = erosion == null ? beforeErosion : erosion.ApplyHeight(terrainPosition, beforeErosion);
        return new TerrainHeightEvaluation
        {
            height = height,
            heightBeforeErosion = beforeErosion,
            erosionDelta = height - beforeErosion,
            normalizedHeightInput = noiseValue,
            riverStrength = riverStrength,
            lakeStrength = lakeStrength,
            valleyStrength = valleyStrength,
            heightAfterRidges = heightAfterRidges,
            falloff = falloffValue
        };
    }

    // How far inside one of the lake noise's patches a point lies: 0 at the patch edge (the lake threshold), 1 where
    // the noise peaks. Lakes are only carved into the land, so the sea map uses this to keep its dead zones in deep
    // water inside these patches.
    public static float LakeBasin(LakeSettings lake, Vector2 terrainPosition)
    {
        if (lake == null || !lake.enabled) return 0f;
        return Mathf.InverseLerp(lake.threshold, 1f, LakeNoise(lake, terrainPosition));
    }

    // Distance in world units from a point to the centre line of the nearest valley, where the valley noise crosses
    // zero. |noise| / |gradient| is that distance to first order; near a valley, up to two Newton steps along the
    // gradient walk onto the line, so valleys keep their set width even where the line bends. Points well away from
    // every valley keep the first estimate, so the rest of the map costs no extra noise samples.
    public static float ValleyDistance(ValleySettings valley, Vector2 terrainPosition)
    {
        float reach = valley.halfWidth + (valley.shoulderHeight > 0f ? valley.shoulderWidth : 0f);
        Vector2 point = terrainPosition;
        for (int step = 0; ; step++)
        {
            float here = ValleyNoise(valley, point.x, point.y);
            float gradientX = ValleyNoise(valley, point.x + 1f, point.y) - here;
            float gradientY = ValleyNoise(valley, point.x, point.y + 1f) - here;
            float squaredGradient = Mathf.Max(gradientX * gradientX + gradientY * gradientY, 1e-12f);
            float remaining = Mathf.Abs(here) / Mathf.Sqrt(squaredGradient);
            float walked = (point - terrainPosition).magnitude;
            if (step == 2 || remaining < 0.5f || walked + remaining > reach * 1.5f)
            {
                return walked + remaining;
            }

            point -= new Vector2(gradientX, gradientY) * (here / squaredGradient);
        }
    }

    // The valley noise is read at a sideways-bent position, so valleys wind. Bending the position instead of adding
    // finer noise keeps the noise smooth, which keeps ValleyDistance accurate across the whole valley.
    static float ValleyNoise(ValleySettings valley, float x, float y)
    {
        if (valley.meander > 0f)
        {
            float bendScale = valley.scale * 0.5f;
            float bentX = x + valley.meander * OpenSimplex2.Noise2(valley.seed + 5353, x / bendScale, y / bendScale);
            float bentY = y + valley.meander * OpenSimplex2.Noise2(valley.seed + 6363, x / bendScale, y / bendScale);
            x = bentX;
            y = bentY;
        }

        return OpenSimplex2.Noise2(valley.seed + 4343, x / valley.scale, y / valley.scale);
    }

    static float LakeNoise(LakeSettings lake, Vector2 terrainPosition)
    {
        return (OpenSimplex2.Noise2(
            lake.seed + 2929,
            terrainPosition.x / lake.scale,
            terrainPosition.y / lake.scale) + 1f) * 0.5f;
    }
}
