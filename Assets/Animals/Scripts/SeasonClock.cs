using Boreal.World;
using Boreal.Temperature;
using Boreal.TerrainGeneration;
using UnityEngine;

namespace Boreal.Animals
{
    // One season clock for the whole island, so every system sees the same time of year wherever the camera is. The
    // year runs on simulated time from the start of the current world and follows the terrain's seasonal presets
    // (SeasonState and SeasonalEnvironment): spring, summer, fall and winter, blended smoothly. Seasons shift the
    // temperature animals feel (through ProceduralTerrainTemperatureProvider), slow plant regrowth and bury ground
    // plants under snow (FoodSpawner), slow animals on snow (SeekFood), and do the same for animals kept as numbers
    // off-screen (OffscreenPopulationStore). They change how well an animal does, never its genes. At the end of each
    // season it writes a SeasonSummary line to the log. Adds itself to scenes with a SpeciesManager, with seasons on or
    // off as Tools > Boreal Ecosystem > Seasons On was last set.
    [DefaultExecutionOrder(-800)]
    [DisallowMultipleComponent]
    public sealed class SeasonClock : MonoBehaviour
    {
        // How often a year slower systems (plant sites, off-screen cells) refresh what they take from the season.
        public const int StepsPerYear = 240;

        public static SeasonClock Active { get; private set; }

        [Tooltip("Off, every day is the terrain's base climate, as before seasons. A new clock starts as Tools > " +
                 "Boreal Ecosystem > Seasons On is set.")]
        public bool seasonsOn = true;
        [Tooltip("Simulated seconds in a year. At 400 s the leanest stretch of winter, when plants barely regrow and " +
                 "most ground plants are under snow, lasts about 110 s, about as long as a well-fed founder lasts " +
                 "without food in the cold. Longer years make longer winters.")]
        [Min(10f)] public float yearLength = 400f;
        [Tooltip("Where in the year a new world begins: 0 is spring, 0.25 summer, 0.5 fall and 0.75 winter. The " +
                 "default, between spring and summer, gives the founders summer and fall before their first winter.")]
        [Range(0f, 1f)] public float startOfYear = 0.125f;
        [Tooltip("1 uses the terrain's seasonal presets as they are. Lower values soften every season towards the " +
                 "base climate, and 0 is the same as no seasons. At 0.7 winter is 19 °C colder and plants regrow at " +
                 "about half their usual pace. In test runs, winters at about 0.6 cost a few animals, while winters " +
                 "at 0.9 and above starved 70-90% of them, leaving survivors chosen by luck rather than by their " +
                 "traits.")]
        [Range(0f, 1f)] public float strength = 0.7f;
        [Tooltip("Years over which seasons grow from nothing to full strength after a world begins, so the founders " +
                 "can multiply and spread before the hard winters. With seasons off from the start, three founders " +
                 "took about two years to reach 20 animals. 0 starts at full strength.")]
        [Min(0f)] public float easeInYears = 3f;
        [Tooltip("Speeds plant regrowth up by the same amount all year, so that over a whole year plants regrow as much " +
                 "as in the base climate: summer brings a boom, fall still slows and winter stays lean. Off, seasons " +
                 "only ever take growth away.")]
        public bool balanceYearlyGrowth = true;
        [Tooltip("The largest share of ground plants snow and withered grass may hide, so a winter forager can still " +
                 "find what grew in fall. 1 lets the terrain's seasonal query decide, which hides about 90% in deep " +
                 "winter; with that, full-strength winters starved 70-90% of the animals.")]
        [Range(0f, 1f)] public float maximumBuriedShare = 0.5f;

        [Header("Log")]
        [Tooltip("Writes one line to the Console at the end of each season, starting \"Season summary\": animals, " +
                 "births, deaths by cause, meals, energy and plants. It also writes the season so far when every animal " +
                 "has died or Play stops. With seasons off it still writes one each quarter year, so runs with and " +
                 "without seasons line up.")]
        public bool logSeasonSummaries = true;

        [Header("Runtime")]
        [SerializeField] private string currentSeason;
        [SerializeField] private int currentYear;
        [SerializeField, Range(0f, 1f)] private float progressThroughYear;
        [SerializeField, Tooltip("Strength now, while seasons ease in.")] private float effectiveStrength;
        [SerializeField, Tooltip("What plant regrowth is multiplied by all year to balance the year's growth.")]
        private float growthBalance = 1f;

        SeasonState state = Neutral;
        // startOfYear plus the years since the world began.
        float yearsSinceStart;

        SeasonSummary summary;
        // The season the summary covers, counted in seasons since the world began.
        int summarySeason;
        int summaryWorldRevision = -1;
        FoodSpawner foodSpawner;

        // The base climate: no temperature or moisture shift, full growth, no snow and no withered grass.
        public static SeasonState Neutral => WorldSeason.Neutral;

        public static bool IsRunning => Active != null && Active.isActiveAndEnabled && Active.seasonsOn &&
                                        Active.strength > 0f;

        // The season to use now: the base climate when there is no clock or seasons are off.
        public static SeasonState Current => IsRunning ? Active.state : Neutral;

        // The season with the clock's adjustments, for plants: the base climate when there is no clock or seasons are
        // off.
        public static SeasonConditions Conditions => IsRunning
            ? new SeasonConditions
            {
                state = Active.state,
                growthBalance = Active.growthBalance,
                leastUncovered = 1f - Mathf.Clamp01(Active.maximumBuriedShare)
            }
            : SeasonConditions.None;

        // Changes StepsPerYear times a year, and is -1 without seasons, so slower systems refresh their seasonal values
        // when it changes instead of every frame.
        public static int Step => IsRunning ? Mathf.FloorToInt(Active.yearsSinceStart * StepsPerYear) : -1;

        // 0 to 1 through the current year, where 0 is the height of spring.
        public float YearProgress => Mathf.Repeat(yearsSinceStart, 1f);
        // The first year is 1.
        public int Year => Mathf.FloorToInt(yearsSinceStart) + 1;
        public string SeasonName => Describe(YearProgress);
        // The strength the seasons have now, below strength while they ease in.
        public float EffectiveStrength => effectiveStrength;
        // Seasons begin halfway between two seasons' heights, as SeasonState.AtYearProgress names them, so this counts
        // up by one at the start of each season. Its remainder by 4 is the BorealSeason.
        int SeasonsSinceStart => Mathf.FloorToInt(yearsSinceStart * 4f + 0.5f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active = null;
            WorldSeason.Reset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToScene()
        {
            if (FindAnyObjectByType<SeasonClock>() == null && FindAnyObjectByType<SpeciesManager>() != null)
            {
                new GameObject("Season Clock").AddComponent<SeasonClock>().seasonsOn = SeasonsOnAtStart;
            }
        }

    #if UNITY_EDITOR
        const string SeasonsOnMenu = "Tools/Boreal Ecosystem/Seasons On";
        const string SeasonsOnPreference = "BorealEcosystem.SeasonsOn";

        // Whether a run starts with seasons on. The clock only exists in Play mode, so the Editor remembers this
        // between Play sessions instead, for comparing runs with and without seasons.
        static bool SeasonsOnAtStart
        {
            get => UnityEditor.EditorPrefs.GetBool(SeasonsOnPreference, true);
            set => UnityEditor.EditorPrefs.SetBool(SeasonsOnPreference, value);
        }

        // Also switches the running clock, if any, so the menu always shows what is running.
        [UnityEditor.MenuItem(SeasonsOnMenu)]
        static void ToggleSeasonsOn()
        {
            SeasonsOnAtStart = !SeasonsOnAtStart;
            if (Active != null) Active.seasonsOn = SeasonsOnAtStart;
            Debug.Log(SeasonsOnAtStart ? "Seasons are on, now and in later runs."
                                       : "Seasons are off, now and in later runs.");
        }

        [UnityEditor.MenuItem(SeasonsOnMenu, true)]
        static bool ShowSeasonsOn()
        {
            UnityEditor.Menu.SetChecked(SeasonsOnMenu, SeasonsOnAtStart);
            return true;
        }
    #else
        static bool SeasonsOnAtStart => true;
    #endif

        void OnEnable()
        {
            Active = this;
            Refresh();
        }

        void OnDisable()
        {
            if (Active == this)
            {
                Active = null;
                WorldSeason.Reset();
            }
        }

        void Update()
        {
            Refresh();
            if (logSeasonSummaries)
            {
                UpdateSummary();
            }
            else
            {
                summary = null;
            }
        }

        void OnApplicationQuit()
        {
            if (summary != null && SpeciesManager.Instance != null) WriteSummary(SpeciesManager.Instance, "Play stopped");
        }

        void Refresh()
        {
            SpeciesManager manager = SpeciesManager.Instance;
            float elapsed = Mathf.Max(0f, Time.time - (manager != null ? manager.WorldStartTime : 0f));
            float yearsElapsed = elapsed / Mathf.Max(10f, yearLength);
            yearsSinceStart = startOfYear + yearsElapsed;
            effectiveStrength = EasedStrength(strength, yearsElapsed, easeInYears);
            state = Scale(SeasonState.AtYearProgress(yearsSinceStart), effectiveStrength);
            growthBalance = balanceYearlyGrowth ? YearlyGrowthBalance(effectiveStrength) : 1f;
            currentSeason = SeasonName;
            currentYear = Year;
            progressThroughYear = YearProgress;
            WorldSeason.Current = Current;
        }

        // Starts a summary when a season begins and writes it when the season ends, or sooner when every animal has
        // died. A new world starts afresh.
        void UpdateSummary()
        {
            SpeciesManager manager = SpeciesManager.Instance;
            if (manager == null) return;

            if (manager.WorldRevision != summaryWorldRevision)
            {
                summaryWorldRevision = manager.WorldRevision;
                summary = null;
            }

            int seasonCount = SeasonsSinceStart;
            if (summary != null && seasonCount != summarySeason)
            {
                WriteSummary(manager, "");
            }

            int animals = manager.TotalPopulation + manager.OffscreenTotal;
            if (summary == null)
            {
                // Wait for the founders, and stay quiet once every animal has died.
                if (animals == 0) return;

                if (foodSpawner == null) foodSpawner = FindAnyObjectByType<FoodSpawner>();
                summary = new SeasonSummary(Year, (BorealSeason)(seasonCount % 4), Time.time - manager.WorldStartTime,
                                            animals, EcosystemTally.Of(manager.Telemetry.Values));
                summarySeason = seasonCount;
            }

            SeasonConditions season = Conditions;
            summary.Sample(Time.deltaTime, animals, foodSpawner != null ? foodSpawner.ShownPlantCount : -1,
                           foodSpawner != null ? foodSpawner.ShownSeaPlantCount : -1,
                           foodSpawner != null ? foodSpawner.BuriedPlantCount : -1,
                           season.state.growthMultiplier * season.growthBalance, season.state.snowAmount,
                           TemperatureShiftCelsius(season.state));
            if (animals == 0) WriteSummary(manager, "ended early: no animals left");
        }

        void WriteSummary(SpeciesManager manager, string ending)
        {
            float energy = 0f;
            int live = 0;
            WinterTraits traits = new WinterTraits { perkShares = new float[AnimalGenome.PerkCount] };
            foreach (SeekFood animal in manager.ActiveAgents)
            {
                if (animal == null || animal.maxEnergy <= 0f) continue;

                energy += animal.currentEnergy / animal.maxEnergy;
                live++;
                traits.breedingThreshold += animal.reproductionThreshold;
                traits.maxEnergy += animal.maxEnergy;
                for (int perk = 0; perk < AnimalGenome.PerkCount; perk++)
                {
                    if (animal.HasPerk((AnimalPerk)perk)) traits.perkShares[perk]++;
                }

                if (animal.ThermalResponse != null)
                {
                    traits.torpor += animal.ThermalResponse.torpor;
                    traits.coldestComfort += animal.ThermalResponse.MinimumComfortTemperature;
                }
            }

            if (live > 0)
            {
                traits.animals = live;
                traits.breedingThreshold /= live;
                traits.torpor /= live;
                traits.maxEnergy /= live;
                traits.coldestComfort /= live;
                for (int perk = 0; perk < AnimalGenome.PerkCount; perk++)
                {
                    traits.perkShares[perk] /= live;
                }
            }

            string easing = effectiveStrength < strength - 0.005f ? $" easing in towards {strength:0.##}" : "";
            string balance = balanceYearlyGrowth ? ", growth balanced over the year" : "";
            string setting = IsRunning
                ? $"seasons on, strength {effectiveStrength:0.##}{easing}, {yearLength:0} s years{balance}, " +
                  $"at most {100f * Mathf.Clamp01(maximumBuriedShare):0}% of plants buried"
                : "seasons off";
            Debug.Log(summary.Describe(Time.time - manager.WorldStartTime, manager.TotalPopulation + manager.OffscreenTotal,
                                       manager.OffscreenTotal, live > 0 ? energy / live : float.NaN,
                                       manager.SpeciesAliveCount, EcosystemTally.Of(manager.Telemetry.Values), traits,
                                       setting, ending));
            summary = null;
        }

        // The season's temperature shift in Celsius on the terrain's scale, or NaN without the terrain's temperature
        // provider. Places it would take below the terrain's coldest stop there, so they feel less of it.
        public static float TemperatureShiftCelsius(SeasonState season)
        {
            TemperatureSystem temperature = TemperatureSystem.Active;
            return temperature != null && temperature.provider is ProceduralTerrainTemperatureProvider terrain
                ? season.temperatureOffset *
                  Mathf.Abs(terrain.warmestTemperatureCelsius - terrain.coldestTemperatureCelsius)
                : float.NaN;
        }

        // Strength rises in a straight line from 0 when a world begins to full strength after easeInYears.
        public static float EasedStrength(float strength, float yearsElapsed, float easeInYears)
        {
            float full = Mathf.Clamp01(strength);
            return easeInYears > 0f ? full * Mathf.Clamp01(yearsElapsed / easeInYears) : full;
        }

        // What to multiply plant regrowth by all year so that, averaged over a whole year, the seasons' growth comes to
        // the base climate's: 1 divided by the growth multiplier's average over the year at this strength. About 1.9
        // at full strength, so summer regrows about twice as fast as the base climate and fall about two thirds.
        public static float YearlyGrowthBalance(float strength)
        {
            const int Samples = 96;
            float total = 0f;
            for (int sample = 0; sample < Samples; sample++)
            {
                total += Scale(SeasonState.AtYearProgress((sample + 0.5f) / Samples), strength).growthMultiplier;
            }

            return Samples / Mathf.Max(0.01f, total);
        }

        // Moves every part of a season towards the base climate: strength 1 keeps it, 0 gives Neutral.
        public static SeasonState Scale(SeasonState season, float strength)
        {
            float amount = Mathf.Clamp01(strength);
            SeasonState valid = season.Validated();
            return new SeasonState
            {
                season = valid.season,
                temperatureOffset = valid.temperatureOffset * amount,
                moistureOffset = valid.moistureOffset * amount,
                growthMultiplier = Mathf.Lerp(1f, valid.growthMultiplier, amount),
                snowAmount = valid.snowAmount * amount,
                dormancy = valid.dormancy * amount
            };
        }

        // "Early summer", "Winter", "Late fall" and so on. Each season is named from halfway after the one before
        // to halfway before the next, as SeasonState.AtYearProgress names them.
        public static string Describe(float yearProgress)
        {
            float phase = Mathf.Repeat(yearProgress, 1f) * 4f;
            int nearest = Mathf.FloorToInt(phase + 0.5f);
            float fromPeak = phase - nearest;
            string name = ((BorealSeason)(nearest % 4)).ToString().ToLowerInvariant();
            return fromPeak < -1f / 6f ? $"Early {name}"
                : fromPeak > 1f / 6f ? $"Late {name}"
                : char.ToUpperInvariant(name[0]) + name.Substring(1);
        }
    }

    // A season as plants feel it: the terrain's seasonal state, what plant regrowth is multiplied by all year to balance
    // the year's growth, and the share of ground plants always left uncovered.
    public struct SeasonConditions
    {
        public SeasonState state;
        public float growthBalance;
        public float leastUncovered;

        public static SeasonConditions None => new SeasonConditions { state = SeasonClock.Neutral, growthBalance = 1f };
    }

    // What a season does at one spot of ground, relative to the base climate, from the terrain's seasonal query
    // (SeasonalEnvironment.Evaluate).
    public struct SeasonalGround
    {
        // How fast plants regrow, as a multiple of their rate in the base climate.
        public float growth;
        // The share of ground plants not under snow or withered, as a fraction of those in the base climate.
        public float uncovered;
        // How much harder moving is: 1 without snow, up to 1.75 in deep snow.
        public float movementCost;
        // The terrain's temperature index (0 coldest, 1 warmest) shifted by the season.
        public float temperature;

        public static SeasonalGround None => new SeasonalGround { growth = 1f, uncovered = 1f, movementCost = 1f };

        // Water has no snow or grass: its plants regrow by the season's growth multiplier alone.
        public static SeasonalGround At(EnvironmentSample ground, SeasonState season)
        {
            if (!ground.isValid) return None;

            SeasonalEnvironmentSample now = SeasonalEnvironment.Evaluate(ground, season);
            if (!ground.isLand)
            {
                return new SeasonalGround
                {
                    growth = season.Validated().growthMultiplier, uncovered = 1f, movementCost = 1f,
                    temperature = now.temperature
                };
            }

            SeasonalEnvironmentSample baseClimate = SeasonalEnvironment.Evaluate(ground, SeasonClock.Neutral);
            return new SeasonalGround
            {
                growth = Ratio(now.grassGrowthMultiplier, baseClimate.grassGrowthMultiplier),
                uncovered = Ratio(now.accessibleGrassBiomass, baseClimate.accessibleGrassBiomass),
                movementCost = Mathf.Max(1f, now.movementCostMultiplier),
                temperature = now.temperature
            };
        }

        // The same with the clock's adjustments: regrowth times the year's growth balance, and never fewer ground plants
        // uncovered than the clock leaves.
        public static SeasonalGround At(EnvironmentSample ground, SeasonConditions conditions)
        {
            SeasonalGround effect = At(ground, conditions.state);
            effect.growth *= conditions.growthBalance;
            effect.uncovered = Mathf.Max(effect.uncovered, Mathf.Clamp01(conditions.leastUncovered));
            return effect;
        }

        // Without grass to measure against, a season's grass ratio says nothing, so it counts as unchanged.
        static float Ratio(float now, float baseClimate)
        {
            return baseClimate > 0.0001f ? Mathf.Max(0f, now / baseClimate) : 1f;
        }
    }
}
