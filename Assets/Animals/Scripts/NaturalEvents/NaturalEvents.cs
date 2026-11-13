using System;
using Boreal.TerrainGeneration;
using UnityEngine;

namespace Boreal.Animals
{
    // Natural events (item 78): things that happen to the world by themselves, such as a wildfire (item 79), chosen at
    // random from the conditions of the moment. The NaturalEventDirector keeps the conditions, asks every kind of event
    // how often it starts under them, starts and runs the events, and records each start, its progress and its end with
    // the Species Manager, so they land in the recording's events.csv. Drought, flood and storm can be added as kinds of
    // their own: a NaturalEventKind that says how often it starts and makes NaturalEvents, registered with the director.

    // What a natural event can read about the world at one moment.
    public struct NaturalConditions
    {
        // Simulated seconds since the world began.
        public float time;
        public bool seasonsRunning;
        public int year;
        // 0 to 1 through the year (0 the height of spring, 0.25 summer, 0.5 fall, 0.75 winter), as SeasonClock counts it.
        public float yearProgress;
        public string seasonName;
        // The season as the terrain and plants feel it now, already scaled by the clock's strength.
        public SeasonState season;
        public float yearLength;
        // The day's average temperature on land where the animals live, in Celsius, or NaN when unknown.
        public float celsius;
        // 0 wet to 1 tinder-dry: how dry the ground's dead plant matter is (NaturalWeather).
        public float dryness;
        // The direction the wind blows towards (x, z; unit length) and its speed in m/s.
        public Vector2 windDirection;
        public float windSpeed;
        // 0 to 1, from the day clock; 1 all the time without day and night.
        public float daylight;

        // "NE" for wind blowing from the south-west towards the north-east.
        public string WindTowards => CompassPoint(windDirection);

        public static string CompassPoint(Vector2 direction)
        {
            if (direction.sqrMagnitude < 1e-6f) return "-";
            string[] points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            float bearing = Mathf.Repeat(Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg, 360f);
            return points[Mathf.RoundToInt(bearing / 45f) % 8];
        }
    }

    // How and when one kind of natural event starts. Kinds are registered with the director, which rolls for a start
    // every check with the chance StartsPerSecond gives.
    public abstract class NaturalEventKind
    {
        // Lower case, as events.csv names it: "wildfire" (its progress is "wildfire-spread", its end "wildfire-end").
        public abstract string Name { get; }
        public abstract string ProgressName { get; }

        // The expected number of starts per simulated second under these conditions (0 for none).
        public abstract float StartsPerSecond(in NaturalConditions conditions);

        // Tries to start one event at a place the kind picks itself; null when nowhere suits.
        public abstract NaturalEvent TryStartAtRandom(in NaturalConditions conditions, System.Random random);

        // Starts one where asked, from a god tool or a schedule. amount and duration mean what the kind says.
        public abstract NaturalEvent StartAt(Vector3 position, float amount, float duration, string cause,
                                             in NaturalConditions conditions, System.Random random);

        // Runs every running event of this kind one step, in simulated seconds.
        public abstract void Tick(float deltaTime, in NaturalConditions conditions);

        // Forgets everything, for a new world.
        public abstract void ResetWorld();
    }

    // One event, from its start to its end.
    public abstract class NaturalEvent
    {
        public NaturalEventKind Kind { get; }
        // Counts from 1 in each world, per kind.
        public int Id { get; }
        public float StartTime { get; }
        public Vector3 Origin { get; }
        public string Cause { get; }
        public float EndTime { get; protected set; } = -1f;
        public bool IsOver => EndTime >= 0f;

        protected NaturalEvent(NaturalEventKind kind, int id, float startTime, Vector3 origin, string cause)
        {
            Kind = kind;
            Id = id;
            StartTime = startTime;
            Origin = origin;
            Cause = cause;
        }

        // events.csv's details: where and why it started, how it is going, and what it did by its end.
        public abstract string DescribeStart(in NaturalConditions conditions);
        public abstract string DescribeProgress(float time);
        public abstract string DescribeEnd();

        // Ends it now, from a god tool or a scheduled duration.
        public abstract void Stop(float time, string reason);
    }

    // The weather natural events read: how dry the ground is and where the wind blows. Pure, so it can be tested.
    //
    // Dryness relaxes towards a target set by the season, with a lag of about a month (a twelfth of the year), so the
    // ground is driest after the warmest weather, in late summer, and still dry into early fall while dead grass stands
    // cured; snowmelt keeps spring wet and snow keeps winter at nothing. Seeded dry and wet spells move the target up
    // and down over weeks. The wind wanders around a prevailing westerly and changes speed.
    [Serializable]
    public sealed class NaturalWeather
    {
        [Tooltip("Months it takes the ground to dry out or wet up, as a share of the year (1/12 is about a month).")]
        [Range(0.01f, 0.5f)] public float dryingLag = 1f / 12f;
        [Tooltip("How far dry and wet spells push the dryness target, as a standard deviation.")]
        [Range(0f, 0.5f)] public float spellStrength = 0.12f;
        [Tooltip("How long a dry or wet spell lasts, as a share of the year.")]
        [Range(0.01f, 1f)] public float spellLength = 0.15f;
        [Tooltip("Where the wind usually blows towards, in degrees clockwise from north (60: from the south-west " +
                 "towards the east-north-east).")]
        [Range(0f, 360f)] public float prevailingWindTowards = 60f;
        [Tooltip("Average wind speed in m/s.")]
        [Range(0f, 20f)] public float averageWindSpeed = 4.5f;
        [Tooltip("How much the wind speed varies, as a standard deviation in m/s.")]
        [Range(0f, 10f)] public float windSpeedSpread = 2.2f;
        [Tooltip("Without seasons, the dryness target all year.")]
        [Range(0f, 1f)] public float seasonlessDryness = 0.45f;

        [SerializeField] float dryness;
        [SerializeField] float spell;
        [SerializeField] float windBearing;
        [SerializeField] float windSpeed;
        System.Random random;

        public float Dryness => dryness;
        public float WindSpeed => windSpeed;
        public Vector2 WindDirection
        {
            get
            {
                float radians = windBearing * Mathf.Deg2Rad;
                return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
            }
        }

        public void Reset(int seed, float startingDryness)
        {
            random = new System.Random(seed);
            dryness = Mathf.Clamp01(startingDryness);
            spell = 0f;
            windBearing = prevailingWindTowards;
            windSpeed = averageWindSpeed;
        }

        // What dryness heads for in this season, before dry and wet spells: warm and dry weather raises it, wet weather
        // and snow lower it, and dormant (cured) grass adds to it unless snow covers it.
        public static float SeasonalDryness(SeasonState season)
        {
            SeasonState s = season.Validated();
            float target = 0.55f + 2.5f * s.temperatureOffset - 2f * s.moistureOffset - 1.2f * s.snowAmount +
                           0.35f * s.dormancy * (1f - s.snowAmount);
            return Mathf.Clamp01(target);
        }

        // Advances the weather by deltaTime simulated seconds.
        public void Step(float deltaTime, bool seasonsRunning, SeasonState season, float yearLength)
        {
            if (deltaTime <= 0f) return;
            if (random == null) Reset(1, seasonlessDryness);
            float year = Mathf.Max(1f, yearLength);

            // Ornstein-Uhlenbeck spells: correlated over spellLength, with spellStrength as their spread.
            float spellTime = Mathf.Max(1f, spellLength * year);
            spell += -spell * deltaTime / spellTime +
                     spellStrength * Mathf.Sqrt(2f * deltaTime / spellTime) * Gaussian();
            float target = Mathf.Clamp01((seasonsRunning ? SeasonalDryness(season) : seasonlessDryness) + spell);
            float lag = Mathf.Max(1f, dryingLag * year);
            dryness += (target - dryness) * (1f - Mathf.Exp(-deltaTime / lag));
            dryness = Mathf.Clamp01(dryness);

            // The wind's bearing wanders and is pulled back towards the prevailing one over about a week of the year.
            float settle = Mathf.Max(1f, year / 50f);
            float offset = Mathf.DeltaAngle(prevailingWindTowards, windBearing);
            windBearing += -offset * deltaTime / settle + 35f * Mathf.Sqrt(deltaTime / settle) * Gaussian();
            windBearing = Mathf.Repeat(windBearing, 360f);
            windSpeed += (averageWindSpeed - windSpeed) * deltaTime / settle +
                         windSpeedSpread * Mathf.Sqrt(2f * deltaTime / settle) * Gaussian();
            windSpeed = Mathf.Clamp(windSpeed, 0.3f, 16f);
        }

        float Gaussian()
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
    }
}
