using UnityEngine;

namespace Boreal.TerrainGeneration
{
    // One moment of the day: where the sun and moon are, how light it is, and how much an animal has to see by.
    [System.Serializable]
    public struct DayState
    {
        [Tooltip("0 to 1 through the day from midnight: 0.25 is 06:00, 0.5 noon.")]
        [Range(0f, 1f)] public float timeOfDay;
        [Tooltip("Days since the world began, counting the first as 1.")]
        public int day;
        [Tooltip("The sun's height above the horizon, in degrees.")]
        public float sunElevation;
        [Tooltip("Unit vector from the ground towards the sun (x east, y up, z north).")]
        public Vector3 sunDirection;
        [Tooltip("Share of today the sun is above the horizon.")]
        [Range(0f, 1f)] public float dayLength;
        [Tooltip("Sunlight, 0 to 1: full with the sun 10° up, fading through twilight to nothing 6° below the horizon.")]
        [Range(0f, 1f)] public float daylight;
        [Tooltip("1 around sunrise and sunset, when the sun is near the horizon, 0 in full day and full night.")]
        [Range(0f, 1f)] public float twilight;
        [Tooltip("0 to 1 through the moon's month: 0 new moon, 0.25 first quarter, 0.5 full, 0.75 last quarter.")]
        [Range(0f, 1f)] public float moonPhase;
        [Tooltip("Lit share of the moon's face: 0 at new moon, 1 at full.")]
        [Range(0f, 1f)] public float moonBrightness;
        public float moonElevation;
        [Tooltip("Unit vector from the ground towards the moon (x east, y up, z north).")]
        public Vector3 moonDirection;
        [Tooltip("Moonlight on the ground, 0 to 1: the lit share of the moon while it is up.")]
        [Range(0f, 1f)] public float moonlight;
        [Tooltip("What an animal has to see by, 0 to 1: 1 in daylight, about 0.3 at sunrise, 0.08 under a full moon " +
                 "and 0.01 on a moonless night.")]
        [Range(0f, 1f)] public float light;

        public bool IsNight => daylight < 0.5f;
    }

    // The sun and moon over the island through the day, by the usual sun-angle formula: the sun's height comes from the
    // latitude, its declination (how far north of the equator it stands, which the season sets) and the hour, so the
    // day's length falls out of the latitude by itself. At 62° N summer days are about 19 h long and winter days about
    // 5 h. The moon is simplified: it rises later each day through its month, stands opposite the sun at full moon, and
    // is high in winter when full, as the real full moon is. Light for seeing spans day to starlight on a 0-1 scale
    // that keeps the real order of sizes (moonlight a hundredth of day, starlight a tenth of moonlight).
    public static class DayCycle
    {
        // How far north of the equator the sun stands at midsummer, in degrees.
        public const float AxialTilt = 23.44f;
        // Light for seeing on a moonless night (starlight) and under a full moon high in the sky.
        public const float StarLight = 0.01f;
        public const float FullMoonLight = 0.08f;

        // Midday at the equinox: what a world without day and night always has.
        public static DayState Neutral => new DayState
        {
            timeOfDay = 0.5f, day = 1, sunElevation = 90f, sunDirection = Vector3.up, dayLength = 0.5f,
            daylight = 1f, moonPhase = 0.5f, moonElevation = -90f, moonDirection = Vector3.down, light = 1f
        };

        // daysElapsed: days since the world began, fractions included (its fraction is the time of day).
        // yearProgress: 0 to 1 through the year from the spring equinox, as SeasonState.AtYearProgress counts it, or
        // NaN for no seasons, which keeps the sun at its equinox path all year.
        // monthLength: days from one new moon to the next. moonPhaseAtStart: the phase when daysElapsed is 0.
        public static DayState Evaluate(float daysElapsed, float latitudeDegrees, float yearProgress, float monthLength,
                                        float moonPhaseAtStart)
        {
            float timeOfDay = Mathf.Repeat(daysElapsed, 1f);
            float declination = float.IsNaN(yearProgress) ? 0f : SunDeclination(yearProgress);
            float latitude = Mathf.Clamp(latitudeDegrees, -89.9f, 89.9f);

            float hourAngle = (timeOfDay - 0.5f) * 360f;
            Vector3 sun = SkyDirection(latitude, declination, hourAngle);
            float sunElevation = Mathf.Asin(Mathf.Clamp(sun.y, -1f, 1f)) * Mathf.Rad2Deg;

            float moonPhase = Mathf.Repeat(moonPhaseAtStart + daysElapsed / Mathf.Max(0.1f, monthLength), 1f);
            float moonBrightness = 0.5f * (1f - Mathf.Cos(moonPhase * Mathf.PI * 2f));
            // A new moon stands with the sun and a full moon opposite it, on the far side of the sky's equator.
            Vector3 moon = SkyDirection(latitude, declination * Mathf.Cos(moonPhase * Mathf.PI * 2f),
                                        hourAngle - moonPhase * 360f);
            float moonElevation = Mathf.Asin(Mathf.Clamp(moon.y, -1f, 1f)) * Mathf.Rad2Deg;

            float daylight = Daylight(sunElevation);
            float moonlight = moonBrightness * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-2f, 8f, moonElevation));
            float night = StarLight + (FullMoonLight - StarLight) * moonlight;
            return new DayState
            {
                timeOfDay = timeOfDay,
                day = Mathf.FloorToInt(daysElapsed) + 1,
                sunElevation = sunElevation,
                sunDirection = sun,
                dayLength = DayLength(latitude, declination),
                daylight = daylight,
                twilight = Mathf.Clamp01(1f - Mathf.Abs(sunElevation + 2f) / 8f),
                moonPhase = moonPhase,
                moonBrightness = moonBrightness,
                moonElevation = moonElevation,
                moonDirection = moon,
                moonlight = moonlight,
                light = daylight + (1f - daylight) * night
            };
        }

        // The sun's declination in degrees: 0 at the equinoxes, the axial tilt at midsummer (yearProgress 0.25) and its
        // negative at midwinter.
        public static float SunDeclination(float yearProgress) => AxialTilt * Mathf.Sin(yearProgress * Mathf.PI * 2f);

        // Share of the day the sun is above the horizon: 0.5 everywhere at the equinoxes, 0 in polar night, 1 under the
        // midnight sun.
        public static float DayLength(float latitudeDegrees, float declinationDegrees)
        {
            float cosine = -Mathf.Tan(latitudeDegrees * Mathf.Deg2Rad) * Mathf.Tan(declinationDegrees * Mathf.Deg2Rad);
            return Mathf.Acos(Mathf.Clamp(cosine, -1f, 1f)) / Mathf.PI;
        }

        // Sunlight from the sun's height: full 10° up, about a third at sunrise, none once it is 6° down (the end of
        // civil twilight).
        public static float Daylight(float sunElevation) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-6f, 10f, sunElevation));

        // Direction to a body at this declination and hour angle (0 when it crosses the meridian, negative before),
        // seen from this latitude: x east, y up, z north.
        static Vector3 SkyDirection(float latitudeDegrees, float declinationDegrees, float hourAngleDegrees)
        {
            float latitude = latitudeDegrees * Mathf.Deg2Rad;
            float declination = declinationDegrees * Mathf.Deg2Rad;
            float hour = hourAngleDegrees * Mathf.Deg2Rad;
            float east = -Mathf.Cos(declination) * Mathf.Sin(hour);
            float up = Mathf.Sin(latitude) * Mathf.Sin(declination) +
                       Mathf.Cos(latitude) * Mathf.Cos(declination) * Mathf.Cos(hour);
            float north = Mathf.Cos(latitude) * Mathf.Sin(declination) -
                          Mathf.Sin(latitude) * Mathf.Cos(declination) * Mathf.Cos(hour);
            return new Vector3(east, up, north).normalized;
        }
    }
}

namespace Boreal.TerrainGeneration
{
    // How much warmer or colder than the day's average it is at an hour of the day (item 73). The swing is coldest just
    // before sunrise and warmest two thirds of the way from sunrise to sunset, a few hours after noon, as the ground
    // warms through the day; it averages to nothing over the day, so the seasons' balance stays as it was. How large it
    // is depends on the place: largest on dry, high, open ground, much smaller under trees, on the shore and over
    // water, larger when days are long, and colder still on snowy nights, when snow radiates the heat away.
    public static class DailyTemperature
    {
        // Peak to trough, in °C, on open ground of middling moisture and height on a day of equal light and dark.
        public const float BaseSwing = 8f;
        // Over open water the swing is about this much in all.
        public const float WaterSwing = 1f;

        // Peak-to-trough swing in °C for this place on a day this long (DayState.dayLength). snowCover 0-1 is the snow
        // lying there (SeasonalEnvironment); it deepens the night half only, so it is applied in Offset.
        public static float Swing(in EnvironmentSample place, float dayLength)
        {
            if (!place.isValid) return 0f;
            if (place.isWater) return WaterSwing;

            float swing = BaseSwing;
            swing *= Mathf.Lerp(1.3f, 0.6f, Mathf.Clamp01(place.moisture));
            swing *= Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(place.treeCover));
            swing *= Mathf.Lerp(1f, 1.3f, Mathf.InverseLerp(0.35f, 0.9f, place.normalizedHeight));
            if (place.isShore) swing *= 0.5f;
            swing *= Mathf.Lerp(0.8f, 1.2f, Mathf.InverseLerp(0.2f, 0.8f, dayLength));
            return swing;
        }

        // °C above (or below) the day's average at this time of day, for a place whose swing is swing.
        public static float Offset(float swing, float timeOfDay, float dayLength, float snowCover = 0f)
        {
            float offset = swing * Shape(timeOfDay, dayLength);
            if (offset < 0f) offset *= 1f + 0.3f * Mathf.Clamp01(snowCover);
            return offset;
        }

        // From -0.5 just before sunrise to 0.5 in mid-afternoon: a half cosine up from sunrise to the warmest hour and
        // another down from there to the next sunrise. Each half averages to nothing, so the day does too. Under the
        // midnight sun or in polar night the shape keeps a short dark or light spell, so the swing never vanishes.
        public static float Shape(float timeOfDay, float dayLength)
        {
            float light = Mathf.Clamp(dayLength, 0.1f, 0.9f);
            float sunrise = 0.5f - light * 0.5f;
            float warmest = sunrise + light * 2f / 3f;
            float rising = warmest - sunrise;
            float since = Mathf.Repeat(timeOfDay - sunrise, 1f);
            return since < rising
                ? -0.5f * Mathf.Cos(Mathf.PI * since / rising)
                : 0.5f * Mathf.Cos(Mathf.PI * (since - rising) / (1f - rising));
        }
    }
}
