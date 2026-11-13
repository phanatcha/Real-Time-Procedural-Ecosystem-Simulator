using System.Collections.Generic;
using Boreal.Core;
using Boreal.Temperature;
using Boreal.TerrainGeneration;
using Boreal.World;
using UnityEngine;

namespace Boreal.Animals
{
    // Runs the world's natural events (item 78): keeps their weather, rolls for random starts from the conditions, runs
    // the events and records their start, progress and end with the Species Manager. Adds itself to scenes with a
    // Species Manager, as the god tools do. A new world starts clean, with weather seeded from the world seed.
    [DisallowMultipleComponent]
    public sealed class NaturalEventDirector : MonoBehaviour
    {
        public static NaturalEventDirector Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Active = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToScene()
        {
            if (FindAnyObjectByType<NaturalEventDirector>() == null && FindAnyObjectByType<SpeciesManager>() != null)
            {
                new GameObject("Natural Events").AddComponent<NaturalEventDirector>();
            }
        }

        [Tooltip("Natural events start by themselves, as the conditions make them likely. Off, they happen only when " +
                 "god tools start them.")]
        public bool randomEvents = true;
        [Tooltip("Simulated seconds between rolls for a random start.")]
        [Min(0.5f)] public float checkInterval = 5f;
        [Tooltip("Simulated seconds between progress lines for each running event in events.csv.")]
        [Min(1f)] public float progressInterval = 30f;
        public NaturalWeather weather = new NaturalWeather();
        public WildfireSettings wildfire = new WildfireSettings();

        readonly List<NaturalEventKind> kinds = new List<NaturalEventKind>();
        readonly List<NaturalEvent> running = new List<NaturalEvent>();
        readonly Dictionary<NaturalEvent, float> nextProgress = new Dictionary<NaturalEvent, float>();
        System.Random random;
        int worldRevision = -1;
        float checkTimer;
        float celsius = float.NaN;
        float temperatureTimer;

        public WildfireKind Wildfire { get; private set; }
        public NaturalConditions Conditions { get; private set; }
        public IReadOnlyList<NaturalEvent> Running => running;
        public IReadOnlyList<NaturalEventKind> Kinds => kinds;

        void Awake()
        {
            Wildfire = new WildfireKind(this);
            Register(Wildfire);
            // How the fires look: the fire map for the terrain shaders and the shared flame, smoke and spark particles.
            if (GetComponent<WildfireVisuals>() == null) gameObject.AddComponent<WildfireVisuals>();
        }

        void OnEnable()
        {
            if (Active == null) Active = this;
        }

        void OnDisable()
        {
            if (Active == this) Active = null;
            Wildfire?.Detach();
        }

        // Adds a kind of event; drought, flood or storm would each be one.
        public void Register(NaturalEventKind kind)
        {
            if (kind != null && !kinds.Contains(kind)) kinds.Add(kind);
        }

        public NaturalEventKind FindKind(string name)
        {
            foreach (NaturalEventKind kind in kinds)
            {
                if (string.Equals(kind.Name, name, System.StringComparison.OrdinalIgnoreCase)) return kind;
            }
            return null;
        }

        // Starts an event of the named kind where asked (god tools and schedules). Returns it, or null when it couldn't
        // start there.
        public NaturalEvent Force(string kindName, Vector3 position, float amount, float duration, string cause)
        {
            EnsureWorld();
            NaturalEventKind kind = FindKind(kindName);
            if (kind == null) return null;
            RefreshConditions();
            NaturalEvent started = kind.StartAt(position, amount, duration, cause, Conditions, random);
            if (started != null) Begin(started);
            return started;
        }

        // Ends every running event now, for the god tools.
        public int StopAll(string reason)
        {
            int stopped = 0;
            float time = SimulatedTime;
            foreach (NaturalEvent happening in running)
            {
                if (happening.IsOver) continue;
                happening.Stop(time, reason);
                stopped++;
            }
            Finish();
            return stopped;
        }

        float SimulatedTime
        {
            get
            {
                SpeciesManager manager = SpeciesManager.Instance;
                return manager != null ? Mathf.Max(0f, Time.time - manager.WorldStartTime) : Time.time;
            }
        }

        void EnsureWorld()
        {
            SpeciesManager manager = SpeciesManager.Instance;
            int revision = manager != null ? manager.WorldRevision : 0;
            if (revision != worldRevision || random == null) ResetWorld(revision);
        }

        void ResetWorld(int revision)
        {
            worldRevision = revision;
            int seed = SeedManager.Instance != null ? SeedManager.Instance.GetSeed("NaturalEvents") : 7919;
            seed = unchecked(seed * 31 + revision);
            random = new System.Random(seed);
            SeasonState season = SeasonClock.Current;
            weather.Reset(seed ^ 0x5f3759df, SeasonClock.IsRunning ? NaturalWeather.SeasonalDryness(season)
                                                                    : weather.seasonlessDryness);
            foreach (NaturalEventKind kind in kinds) kind.ResetWorld();
            running.Clear();
            nextProgress.Clear();
            checkTimer = checkInterval;
            temperatureTimer = 0f;
            celsius = float.NaN;
        }

        void Update()
        {
            SpeciesManager manager = SpeciesManager.Instance;
            if (manager == null) return;
            EnsureWorld();
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            SeasonClock clock = SeasonClock.Active;
            float yearLength = clock != null ? clock.yearLength : 400f;
            weather.Step(deltaTime, SeasonClock.IsRunning, SeasonClock.Current, yearLength);
            temperatureTimer -= deltaTime;
            if (temperatureTimer <= 0f)
            {
                temperatureTimer = checkInterval;
                celsius = SampleLandCelsius(manager);
            }
            RefreshConditions();
            NaturalConditions conditions = Conditions;

            foreach (NaturalEventKind kind in kinds) kind.Tick(deltaTime, conditions);

            checkTimer -= deltaTime;
            if (checkTimer <= 0f)
            {
                float interval = checkInterval;
                checkTimer += interval;
                if (checkTimer < 0f) checkTimer = interval;
                if (randomEvents) RollForStarts(conditions, interval);
            }

            float now = conditions.time;
            foreach (NaturalEvent happening in running)
            {
                if (happening.IsOver || !nextProgress.TryGetValue(happening, out float due) || now < due) continue;
                nextProgress[happening] = now + progressInterval;
                manager.RecordEvent(happening.Kind.ProgressName, happening.DescribeProgress(now));
            }
            Finish();
        }

        void RollForStarts(in NaturalConditions conditions, float interval)
        {
            foreach (NaturalEventKind kind in kinds)
            {
                float rate = kind.StartsPerSecond(conditions);
                if (rate <= 0f) continue;
                // A Poisson process: the chance of at least one start in the interval.
                float chance = 1f - Mathf.Exp(-rate * interval);
                if (random.NextDouble() >= chance) continue;
                NaturalEvent started = kind.TryStartAtRandom(conditions, random);
                if (started != null) Begin(started);
            }
        }

        void Begin(NaturalEvent happening)
        {
            running.Add(happening);
            nextProgress[happening] = happening.StartTime + progressInterval;
            SpeciesManager.Instance?.RecordEvent(happening.Kind.Name, happening.DescribeStart(Conditions));
        }

        void Finish()
        {
            SpeciesManager manager = SpeciesManager.Instance;
            for (int i = running.Count - 1; i >= 0; i--)
            {
                NaturalEvent happening = running[i];
                if (!happening.IsOver) continue;
                running.RemoveAt(i);
                nextProgress.Remove(happening);
                manager?.RecordEvent(happening.Kind.Name + "-end", happening.DescribeEnd());
            }
        }

        void RefreshConditions()
        {
            SeasonClock clock = SeasonClock.Active;
            bool seasons = SeasonClock.IsRunning;
            DayState day = WorldDay.Current;
            Conditions = new NaturalConditions
            {
                time = SimulatedTime,
                seasonsRunning = seasons,
                year = seasons ? clock.Year : 1,
                yearProgress = seasons ? clock.YearProgress : 0.25f,
                seasonName = seasons ? clock.SeasonName : "No seasons",
                season = SeasonClock.Current,
                yearLength = clock != null ? clock.yearLength : 400f,
                celsius = celsius,
                dryness = weather.Dryness,
                windDirection = weather.WindDirection,
                windSpeed = weather.WindSpeed,
                daylight = WorldDay.IsRunning ? Mathf.Clamp01(day.daylight) : 1f
            };
        }

        // The average day temperature under up to eight live animals on land, or at the camera without any.
        static float SampleLandCelsius(SpeciesManager manager)
        {
            float total = 0f;
            int count = 0;
            foreach (SeekFood animal in manager.ActiveAgents)
            {
                if (animal == null || animal.IsInWater) continue;
                if (TemperatureSystem.TryGetTemperatureAt(animal.transform.position, out float value))
                {
                    total += value;
                    count++;
                }
                if (count >= 8) break;
            }
            if (count == 0 && Camera.main != null &&
                TemperatureSystem.TryGetTemperatureAt(Camera.main.transform.position, out float atCamera))
            {
                return atCamera;
            }
            return count > 0 ? total / count : float.NaN;
        }
    }
}
