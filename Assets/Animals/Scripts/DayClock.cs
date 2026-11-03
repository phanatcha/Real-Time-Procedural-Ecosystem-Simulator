using Boreal.Core;
using Boreal.TerrainGeneration;
using Boreal.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Boreal.Animals
{
    // One clock for the time of day across the whole island, beside the season clock (item 71). The day runs on simulated
    // time from the start of the current world: the sun's path follows the latitude and the season clock's time of year,
    // so summer days are long and winter nights long, and a moon goes through its phases over a few days. It publishes
    // the moment as WorldDay for every system to read: the temperature swings through the day (item 73) and the island
    // is lit by the turning sun and the moon (item 72); the animals' hours come with items 74 to 77 (the Day and Night
    // Plan). Adds itself to scenes with a
    // SpeciesManager, on or off as Tools > Boreal Ecosystem > Day And Night On was last set.
    [DefaultExecutionOrder(-790)]
    [DisallowMultipleComponent]
    public sealed class DayClock : MonoBehaviour
    {
        public static DayClock Active { get; private set; }

        [Tooltip("Off, it is always midday, as before day and night. For comparison runs (run_soak.py --daynight 0).")]
        public bool dayAndNight = true;
        [Tooltip("Simulated seconds in a day. At 40 s a 400 s year has 10 days, two and a half a season, and a night is " +
                 "long enough to forage or rest through.")]
        [Min(1f)] public float dayLength = 40f;
        [Tooltip("Degrees north. At 62° (Trondheim) summer days are about 19 h long and winter days about 5 h; above " +
                 "66.5° there is midnight sun and polar night. Without seasons every day is an equinox.")]
        [Range(-89f, 89f)] public float latitude = 62f;
        [Tooltip("Days from one new moon to the next. At 4, each night of the month has its own phase: new, first " +
                 "quarter, full and last quarter, about two and a half months a year.")]
        [Min(0.5f)] public float monthLength = 4f;
        [Tooltip("The moon's phase when a world begins: 0 new, 0.5 full.")]
        [Range(0f, 1f)] public float moonPhaseAtStart = 0.5f;
        [Tooltip("The time of day a world begins at: 0 midnight, 0.25 06:00, 0.5 noon. Morning, so the founders start in " +
                 "daylight.")]
        [Range(0f, 1f)] public float startOfDay = 0.3f;

        [Header("Runtime")]
        [SerializeField] private string clockTime;
        [SerializeField] private string moon;
        [SerializeField] private DayState state = DayCycle.Neutral;

        public static bool IsRunning => Active != null && Active.isActiveAndEnabled && Active.dayAndNight;

        // The moment to use now: always midday when there is no clock or day and night are off.
        public static DayState Current => IsRunning ? Active.state : DayCycle.Neutral;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active = null;
            WorldDay.Reset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToScene()
        {
            if (FindAnyObjectByType<DayClock>() == null && FindAnyObjectByType<SpeciesManager>() != null)
            {
                new GameObject("Day Clock").AddComponent<DayClock>().dayAndNight = DayAndNightAtStart;
            }
        }

    #if UNITY_EDITOR
        const string DayAndNightMenu = "Tools/Boreal Ecosystem/Day And Night On";
        const string DayAndNightPreference = "BorealEcosystem.DayAndNightOn";

        static bool DayAndNightAtStart
        {
            get => UnityEditor.EditorPrefs.GetBool(DayAndNightPreference, true);
            set => UnityEditor.EditorPrefs.SetBool(DayAndNightPreference, value);
        }

        // Also switches the running clock, if any, so the menu always shows what is running.
        [UnityEditor.MenuItem(DayAndNightMenu)]
        static void ToggleDayAndNight()
        {
            DayAndNightAtStart = !DayAndNightAtStart;
            if (Active != null) Active.dayAndNight = DayAndNightAtStart;
            Debug.Log(DayAndNightAtStart ? "Day and night are on, now and in later runs."
                                         : "Day and night are off, now and in later runs.");
        }

        [UnityEditor.MenuItem(DayAndNightMenu, true)]
        static bool ShowDayAndNight()
        {
            UnityEditor.Menu.SetChecked(DayAndNightMenu, DayAndNightAtStart);
            return true;
        }
    #else
        static bool DayAndNightAtStart => true;
    #endif

        void OnEnable()
        {
            Active = this;
            if (!TryGetComponent(out DayNightLighting _)) gameObject.AddComponent<DayNightLighting>();
            Refresh();
        }

        void OnDisable()
        {
            if (Active == this)
            {
                Active = null;
                WorldDay.Reset();
                DayNightLighting.Publish(DayCycle.Neutral, false, latitude);
            }
        }

        void Update()
        {
            // N holds the day view (the scene's own afternoon light) for looking and screenshots; the simulation's
            // time of day carries on.
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.nKey.wasPressedThisFrame && !GodCamera.IsTypingInUI())
            {
                DayNightLighting.HoldDayView = !DayNightLighting.HoldDayView;
            }

            Refresh();
        }

        void Refresh()
        {
            SpeciesManager manager = SpeciesManager.Instance;
            float elapsed = Mathf.Max(0f, Time.time - (manager != null ? manager.WorldStartTime : 0f));
            float yearProgress = SeasonClock.IsRunning ? SeasonClock.Active.YearProgress : float.NaN;
            state = DayCycle.Evaluate(startOfDay + elapsed / Mathf.Max(1f, dayLength), latitude, yearProgress,
                                      monthLength, moonPhaseAtStart);
            clockTime = ClockTime(state.timeOfDay);
            moon = MoonName(state.moonPhase);
            WorldDay.Current = Current;
            WorldDay.IsRunning = IsRunning;
            DayNightLighting.Publish(Current, IsRunning, latitude);
        }

        // "07:12" for 0.3.
        public static string ClockTime(float timeOfDay)
        {
            int minutes = Mathf.FloorToInt(Mathf.Repeat(timeOfDay, 1f) * 24f * 60f);
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }

        // The nearest of the eight named phases.
        public static string MoonName(float phase)
        {
            string[] names = { "new moon", "waxing crescent", "first quarter", "waxing gibbous", "full moon",
                               "waning gibbous", "last quarter", "waning crescent" };
            return names[Mathf.FloorToInt(Mathf.Repeat(phase, 1f) * 8f + 0.5f) % 8];
        }

        // The panel's line: "Day 3, 07:12, day (19 h of daylight), full moon".
        public static string Describe()
        {
            if (Active == null) return "";
            if (!IsRunning) return "    <b>Day</b> always midday (day and night off)";

            DayState now = Active.state;
            string part = now.daylight >= 0.9f ? "day" : now.daylight <= 0.05f ? "night"
                : now.timeOfDay < 0.5f ? "dawn" : "dusk";
            return $"    <b>Day</b> {now.day}, {ClockTime(now.timeOfDay)}, {part} ({24f * now.dayLength:0} h of daylight), " +
                   $"{MoonName(now.moonPhase)}";
        }
    }
}
