using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Boreal.Animals;
using Boreal.Core;
using Boreal.TerrainGeneration;
using Boreal.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Boreal.Soak.Editor
{
    // Batch-mode soak run: plays the Terrain scene on one seed with the run recorder on, for a number of simulated
    // years, saving a camera shot at each season's peak and a top-down map at the end, then quits.
    // Launched by Tools/run_soak.py, which copies this folder into a test copy of the project; see TESTING.md.
    [InitializeOnLoad]
    public static class SoakRun
    {
        const string Key = "SoakRun.";
        const string MapFolder = "Recordings/TopDownMap";
        const int ShotWidth = 1600, ShotHeight = 900;

        static SoakRun()
        {
            if (SessionState.GetBool(Key + "Active", false))
                EditorApplication.update += Tick;
        }

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Arg(string name, string fallback)
            {
                int i = Array.IndexOf(args, "-" + name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
            }

            string seed = Arg("soakSeed", "FOREST-001");
            string output = Path.GetFullPath(Arg("soakOutput", "SoakRuns/" + seed));
            Directory.CreateDirectory(output);
            SessionState.SetBool(Key + "Active", true);
            SessionState.SetString(Key + "Output", output);
            SessionState.SetFloat(Key + "Years", float.Parse(Arg("soakYears", "5"), CultureInfo.InvariantCulture));
            SessionState.SetBool(Key + "Maps", Arg("soakMaps", "1") == "1");
            SessionState.SetFloat(Key + "Deadline", (float)(EditorApplication.timeSinceStartup +
                60.0 * float.Parse(Arg("soakMinutes", "40"), CultureInfo.InvariantCulture)));
            SessionState.SetInt(Key + "Stage", 0);
            SessionState.SetFloat(Key + "WaterSeconds", 0f);
            SessionState.SetFloat(Key + "DrinkSeconds", 0f);
            SessionState.SetFloat(Key + "DrinkInWaterSeconds", 0f);
            SessionState.SetFloat(Key + "IceSeconds", 0f);
            SessionState.SetBool(Key + "SeasonsOn", Arg("soakSeasons", "1") == "1");
            SessionState.SetInt(Key + "LastPeak", -1);
            SessionState.SetFloat(Key + "LastStatus", -1f);

            EditorSceneManager.OpenScene("Assets/_Max/Scenes/Terrain.unity");
            SeedManager seedManager = Object.FindAnyObjectByType<SeedManager>();
            SerializedObject seedObject = new SerializedObject(seedManager);
            seedObject.FindProperty("seedString").stringValue = seed;
            seedObject.ApplyModifiedPropertiesWithoutUndo();

            SpeciesManager manager = Object.FindAnyObjectByType<SpeciesManager>();
            // Starts at 1x: world generation runs below 10 FPS in batch mode, which would trip the safety pause.
            manager.simulationSpeed = 1f;
            SessionState.SetFloat(Key + "Speed", float.Parse(Arg("soakSpeed", "50"), CultureInfo.InvariantCulture));
            SessionState.SetBool(Key + "Fast", false);
            SimulationRunRecorder recorder = manager.gameObject.AddComponent<SimulationRunRecorder>();
            recorder.sourceRevision = Arg("soakRevision", "unrecorded");
            recorder.experiment = Arg("soakExperiment", "soak " + seed);
            SessionState.SetFloat(Key + "HarmfulChance", float.Parse(Arg("soakHarmfulChance", "-1"), CultureInfo.InvariantCulture));
            SessionState.SetString(Key + "Events", Arg("soakEvents", ""));
            SessionState.SetBool(Key + "Thirst", Arg("soakThirst", "1") == "1");
            SessionState.SetBool(Key + "DayNight", Arg("soakDayNight", "1") == "1");
            SessionState.SetBool(Key + "OwnKind", Arg("soakOwnKind", "1") == "1");
            SessionState.SetFloat(Key + "SexualDrive", float.Parse(Arg("soakSexualDrive", "-1"), CultureInfo.InvariantCulture));
            SessionState.SetFloat(Key + "SexualDriveAt", float.Parse(Arg("soakSexualDriveAt", "0"), CultureInfo.InvariantCulture));
            SessionState.SetBool(Key + "DriveLocked", false);
            SessionState.SetBool(Key + "Ledger", Arg("soakLedger", "0") == "1");
            SessionState.SetFloat(Key + "PerkGain", float.Parse(Arg("soakPerkGain", "-1"), CultureInfo.InvariantCulture));
            SessionState.SetFloat(Key + "PerkLoss", float.Parse(Arg("soakPerkLoss", "-1"), CultureInfo.InvariantCulture));
            SessionState.SetBool(Key + "Ageing", Arg("soakAgeing", "1") == "1");
            SessionState.SetBool(Key + "NaturalEvents", Arg("soakNaturalEvents", "1") == "1");
            SessionState.SetBool(Key + "Fires", Arg("soakFires", "1") == "1");
            SessionState.SetBool(Key + "FuelProbe", Arg("soakFuelProbe", "0") == "1");
            SessionState.SetFloat(Key + "FireSpread", float.Parse(Arg("soakFireSpread", "-1"), CultureInfo.InvariantCulture));
            SessionState.SetFloat(Key + "FireStrikes", float.Parse(Arg("soakFireStrikes", "-1"), CultureInfo.InvariantCulture));

            // The founders' start search (item 9) lives in AnimalTerrainIntegration, which this assembly can't
            // reference, so it is set by its serialized name.
            string startSearch = Arg("soakStartSearch", "");
            if (startSearch.Length > 0)
            {
                foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                {
                    if (behaviour.GetType().Name != "AnimalTerrainDemoBootstrap") continue;
                    SerializedObject bootstrap = new SerializedObject(behaviour);
                    bootstrap.FindProperty("startSearchRadius").floatValue = float.Parse(startSearch, CultureInfo.InvariantCulture);
                    bootstrap.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log($"Soak: founder start search radius set to {startSearch} m");
                }
            }

            Debug.Log($"Soak: starting {seed} for {SessionState.GetFloat(Key + "Years", 0f)} years, output {output}");
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "Deadline", float.MaxValue))
            {
                Finish("timed out");
                return;
            }

            int stage = SessionState.GetInt(Key + "Stage", 0);
            if (!EditorApplication.isPlaying)
            {
                if (stage >= 3 && !EditorApplication.isPlayingOrWillChangePlaymode)
                    Quit();
                return;
            }

            SeasonClock clock = SeasonClock.Active;
            if (clock == null)
                return;

            if (!SessionState.GetBool(Key + "SeasonsOn", true) && clock.seasonsOn)
            {
                clock.seasonsOn = false;
                Debug.Log("Soak: seasons switched off for this run");
            }

            if (!SessionState.GetBool(Key + "DayNight", true) && DayClock.Active != null && DayClock.Active.dayAndNight)
            {
                DayClock.Active.dayAndNight = false;
                Debug.Log("Soak: day and night switched off for this run");
            }

            float yearsSinceStart = clock.Year - 1 + clock.YearProgress;
            SeekFood[] animals = Object.FindObjectsByType<SeekFood>(FindObjectsSortMode.None);
            SpeciesManager manager = SpeciesManager.Instance;
            // YearProgress includes the starting season offset; experiment duration must not.
            float simulated = manager != null ? Mathf.Max(0f, Time.time - manager.WorldStartTime) : 0f;

            if (stage == 0)
            {
                int wading = 0, drinkingNow = 0, drinkingInWater = 0, onIce = 0;
                foreach (SeekFood animal in animals)
                {
                    if (animal.IsInWater) wading++;
                    if (animal.IsOnIce) onIce++;
                    if (animal.IsDrinking) { drinkingNow++; if (animal.IsInWater) drinkingInWater++; }
                }
                float step = Time.deltaTime;
                SessionState.SetFloat(Key + "WaterSeconds", SessionState.GetFloat(Key + "WaterSeconds", 0f) + wading * step);
                SessionState.SetFloat(Key + "DrinkSeconds", SessionState.GetFloat(Key + "DrinkSeconds", 0f) + drinkingNow * step);
                SessionState.SetFloat(Key + "IceSeconds", SessionState.GetFloat(Key + "IceSeconds", 0f) + onIce * step);
                SessionState.SetFloat(Key + "DrinkInWaterSeconds",
                    SessionState.GetFloat(Key + "DrinkInWaterSeconds", 0f) + drinkingInWater * step);
            }

            // Item 60: from a set time on, the whole population only clones (0) or only mates (1). Children copy the lock.
            // It starts late by default because three founders that can only mate die out before they find each other.
            float lockedDrive = SessionState.GetFloat(Key + "SexualDrive", -1f);
            if (stage == 0 && lockedDrive >= 0f && manager != null && SessionState.GetBool(Key + "Fast", false) &&
                !SessionState.GetBool(Key + "DriveLocked", false) &&
                simulated >= SessionState.GetFloat(Key + "SexualDriveAt", 0f))
            {
                SessionState.SetBool(Key + "DriveLocked", true);
                foreach (SeekFood animal in animals)
                {
                    animal.lockedSexualDrive = lockedDrive;
                    animal.sexualDrive = Mathf.Clamp01(lockedDrive);
                    if (animal.Genome != null && animal.Genome.IsValid) animal.Genome[AnimalGene.SexualDrive] = lockedDrive;
                }
                manager.RecordEvent("sexual drive locked", lockedDrive.ToString("0.##", CultureInfo.InvariantCulture));
                Debug.Log($"Soak: sexual drive locked at {lockedDrive} on {animals.Length} animals and their descendants at {simulated:0} s");
            }

            if (stage == 0 && SessionState.GetBool(Key + "Fast", false)) UpdateFireShots(manager, simulated);

            if (simulated - SessionState.GetFloat(Key + "LastStatus", -1f) >= 50f)
            {
                SessionState.SetFloat(Key + "LastStatus", simulated);
                int inWater = 0, swimmers = 0, drinking = 0, thirsty = 0, hunters = 0;
                foreach (SeekFood animal in animals)
                {
                    if (animal.IsInWater) inWater++;
                    if (animal.CanSwimDeepWater) swimmers++;
                    if (animal.IsDrinking) drinking++;
                    if (animal.IsThirsty) thirsty++;
                    if (animal.HuntsOwnKind) hunters++;
                }
                FoodSpawner food = Object.FindAnyObjectByType<FoodSpawner>();
                Debug.Log($"Soak: t={simulated:0} s year {clock.Year} {clock.SeasonName}, " +
                          $"day {DayClock.Current.day} {DayClock.ClockTime(DayClock.Current.timeOfDay)}, animals {animals.Length}, " +
                          $"species {(manager != null ? manager.SpeciesAliveCount : -1)}, " +
                          $"achieved speed {(manager != null ? manager.AchievedSimulationSpeed : 0f):0.0}x, " +
                          $"in water {inWater}, swimmers {swimmers}, thirsty {thirsty}, drinking {drinking}, " +
                          $"hunt own kind {hunters}, " +
                          $"sea plants shown {(food != null ? food.ShownSeaPlantCount : -1)}, " +
                          $"sea plants eaten {(food != null ? food.SeaPlantsEaten : -1)}, " +
                          $"animal-seconds in water {SessionState.GetFloat(Key + "WaterSeconds", 0f):0}, " +
                          $"drinking {SessionState.GetFloat(Key + "DrinkSeconds", 0f):0} of which in water " +
                          $"{SessionState.GetFloat(Key + "DrinkInWaterSeconds", 0f):0}, " +
                          $"ice {WaterAccess.IceCover:0.00}{(WaterAccess.FreshWaterFrozen ? " frozen" : "")}, " +
                          $"animal-seconds on ice {SessionState.GetFloat(Key + "IceSeconds", 0f):0}");
                if (EnergyLedger.Enabled) Debug.Log("Soak: " + EnergyLedger.Report(animals));
                NaturalEventDirector natural = NaturalEventDirector.Active;
                if (natural != null)
                {
                    int fleeing = 0, inSmoke = 0;
                    foreach (SeekFood animal in animals)
                    {
                        if (animal.IsFleeingFire) fleeing++;
                        if (animal.SmokeFelt > 0.05f) inSmoke++;
                    }
                    if (SessionState.GetBool(Key + "FuelProbe", false)) ProbeFuel(natural, animals);
                    Debug.Log($"Soak: wildfire {GodTools.DescribeFireWeather(natural)}, " +
                              $"{natural.Wildfire.BurnedHectares:0.0} ha burned in all, fleeing {fleeing}, in smoke {inSmoke}, " +
                              $"lightning {natural.Wildfire.Strikes} strikes, {natural.Wildfire.StrikesCaught} caught");
                }
            }

            if (stage == 0 && animals.Length > 0 && !SessionState.GetBool(Key + "Fast", false) && manager != null)
            {
                // The step per frame stays capped at high speed, so a slow frame rate only lowers the speed reached.
                SessionState.SetBool(Key + "Fast", true);
                manager.enableSafetyPause = false;
                manager.simulationSpeed = SessionState.GetFloat(Key + "Speed", 50f);
                // Item 61: count every change to the animals' energy from here on, and report it with the status lines.
                if (SessionState.GetBool(Key + "Ledger", false))
                {
                    EnergyLedger.Begin(animals);
                    Debug.Log("Soak: energy ledger on");
                }
                // Children are copies of their parents, so a setting changed on the founders reaches every descendant.
                float harmfulChance = SessionState.GetFloat(Key + "HarmfulChance", -1f);
                foreach (SeekFood founder in animals)
                {
                    if (harmfulChance >= 0f) founder.harmfulMutationChance = harmfulChance;
                    // Item 62 comparisons: the perk gain and loss chances, copied by every descendant.
                    float perkGain = SessionState.GetFloat(Key + "PerkGain", -1f);
                    float perkLoss = SessionState.GetFloat(Key + "PerkLoss", -1f);
                    if (perkGain >= 0f) founder.perkGainChance = perkGain;
                    if (perkLoss >= 0f) founder.perkLossChance = perkLoss;
                    // Item 68 control runs: no ageing, full health until the lifespan runs out.
                    if (!SessionState.GetBool(Key + "Ageing", true))
                    {
                        founder.enableAgeing = false;
                        founder.RefreshMetabolicRate();
                    }
                    if (!SessionState.GetBool(Key + "Thirst", true)) founder.enableThirst = false;
                    // Item 66 off: no diet is high enough to hunt the animals' own species.
                    if (!SessionState.GetBool(Key + "OwnKind", true)) founder.huntOwnKindFromDiet = 2f;
                    Vector3 p = founder.transform.position;
                    Debug.Log($"Soak: founder {founder.name} at x {p.x:0} z {p.z:0} (height {p.y:0})");
                }
                if (harmfulChance >= 0f) Debug.Log($"Soak: harmful mutation chance set to {harmfulChance}% on the founders");
                if (animals.Length > 0)
                    Debug.Log($"Soak: perks gained at {animals[0].perkGainChance}% and lost at {animals[0].perkLossChance}% per birth; " +
                              (animals[0].enableAgeing ? $"ageing from {animals[0].ageingOnset} s over {animals[0].ageingSpan} s" : "ageing off"));
                // Items 78 and 79: natural events on or off, wildfires on or off, and how often lightning strikes.
                NaturalEventDirector director = Object.FindAnyObjectByType<NaturalEventDirector>();
                if (director != null)
                {
                    director.randomEvents = SessionState.GetBool(Key + "NaturalEvents", true);
                    director.wildfire.enabled = SessionState.GetBool(Key + "Fires", true);
                    float strikes = SessionState.GetFloat(Key + "FireStrikes", -1f);
                    if (strikes >= 0f) director.wildfire.strikesPerYear = strikes;
                    float spread = SessionState.GetFloat(Key + "FireSpread", -1f);
                    if (spread >= 0f) director.wildfire.rules.spreadStrength = spread;
                    Debug.Log($"Soak: natural events {(director.randomEvents ? "on" : "off")}, wildfires " +
                              $"{(director.wildfire.enabled ? $"on at {director.wildfire.strikesPerYear} strikes a year, spread strength {director.wildfire.rules.spreadStrength}" : "off")}");
                }
                // God tools scheduled for the run, e.g. "drought@800:200x0.2,extinction@1200x0.5" (GodTools.TryParse).
                string events = SessionState.GetString(Key + "Events", "");
                GodTools tools = Object.FindAnyObjectByType<GodTools>();
                if (!string.IsNullOrEmpty(events) && tools != null)
                    Debug.Log($"Soak: scheduled {tools.ScheduleAll(events)} god tool interventions from \"{events}\"");
                Debug.Log($"Soak: {animals.Length} founders at t={simulated:0} s, speeding up to {manager.simulationSpeed}x");
            }

            if (stage == 0)
            {
                int peak = Mathf.FloorToInt(yearsSinceStart * 4f + 0.5f);
                if (peak != SessionState.GetInt(Key + "LastPeak", -1))
                {
                    SessionState.SetInt(Key + "LastPeak", peak);
                    string label = $"y{clock.Year}-{peak:00}-{clock.SeasonName.Replace(' ', '-').ToLowerInvariant()}";
                    Shot(Camera.main, Path.Combine(Out, label + "-camera.png"));
                    ShotOverAnimals(animals, Path.Combine(Out, label + "-animals.png"));
                }

                // And from the camera at each season's height (full autumn colour, deepest snow and ice), at the next
                // noon and the next midnight, so seasons and nights compare shot to shot. Without day and night, at once.
                int height = Mathf.FloorToInt(yearsSinceStart * 4f);
                if (height != SessionState.GetInt(Key + "LastHeight", -1))
                {
                    SessionState.SetInt(Key + "LastHeight", height);
                    string season = new[] { "spring", "summer", "fall", "winter" }[height % 4];
                    SessionState.SetString(Key + "HeightLabel", $"y{clock.Year}-{height:00}-height-of-{season}");
                    SessionState.SetBool(Key + "NoonDue", true);
                    SessionState.SetBool(Key + "MidnightDue", DayClock.IsRunning);
                }

                Boreal.TerrainGeneration.DayState now = DayClock.Current;
                string heightLabel = SessionState.GetString(Key + "HeightLabel", "");
                if (SessionState.GetBool(Key + "NoonDue", false) &&
                    (!DayClock.IsRunning || now.timeOfDay >= 0.5f && now.timeOfDay < 0.6f))
                {
                    SessionState.SetBool(Key + "NoonDue", false);
                    Shot(Camera.main, Path.Combine(Out, heightLabel + "-noon-camera.png"));
                }
                if (SessionState.GetBool(Key + "MidnightDue", false) && DayClock.IsRunning && now.timeOfDay < 0.1f)
                {
                    SessionState.SetBool(Key + "MidnightDue", false);
                    Shot(Camera.main, Path.Combine(Out, heightLabel + "-midnight-camera.png"));
                }

                if (simulated >= SessionState.GetFloat(Key + "Years", 5f) * clock.yearLength || animals.Length == 0 && SessionState.GetBool(Key + "Fast", false))
                {
                    Debug.Log(animals.Length == 0 ? "Soak: every animal has died." : "Soak: reached the target years.");
                    if (EnergyLedger.Enabled) Debug.Log("Soak: final " + EnergyLedger.Report(animals));
                    // Item 65: the in-game Muller plot of the family lines, as the M panel draws it, with its legend.
                    FamilyLinesView lines = FamilyLinesView.Active;
                    if (lines != null && lines.SaveChart(Path.Combine(Out, "family-lines.png")))
                        Debug.Log($"Soak: family lines {lines.Latest.lines.Count}, saved {Path.Combine(Out, "family-lines.png")}");
                    SimulationRunRecorder recorder = Object.FindAnyObjectByType<SimulationRunRecorder>();
                    if (recorder != null)
                    {
                        SessionState.SetString(Key + "Recording", recorder.OutputDirectory ?? "");
                        recorder.enabled = false; // Final observation before maps; render time is not experiment time.
                    }
                    if (manager != null) { manager.simulationSpeed = 0f; Time.timeScale = 0f; }
                    stage = SessionState.GetBool(Key + "Maps", true) ? 1 : 3;
                    SessionState.SetInt(Key + "Stage", stage);
                    if (stage == 1) RequestMap("");
                    else EditorApplication.ExitPlaymode();
                }
            }
            else if (stage == 1 || stage == 2)
            {
                // Waits for the map tool to save each map, then asks for the heatmap version, then leaves Play mode.
                string[] maps = Directory.Exists(MapFolder) ? Directory.GetFiles(MapFolder, "*.png") : new string[0];
                string newest = maps.Where(p => File.GetLastWriteTimeUtc(p) > MapRequestedUtc)
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (newest == null || (DateTime.UtcNow - File.GetLastWriteTimeUtc(newest)).TotalSeconds < 3)
                    return;
                File.Copy(newest, Path.Combine(Out, (stage == 1 ? "map-" : "map-heatmap-") + Path.GetFileName(newest)), true);
                string caption = Path.ChangeExtension(newest, ".txt");
                if (File.Exists(caption))
                    File.Copy(caption, Path.Combine(Out, (stage == 1 ? "map-" : "map-heatmap-") + Path.GetFileName(caption)), true);
                SessionState.SetInt(Key + "Stage", stage + 1);
                if (stage == 1) RequestMap("heatmap");
                else EditorApplication.ExitPlaymode();
            }
        }

        static string Out => SessionState.GetString(Key + "Output", "SoakRuns");

        static DateTime MapRequestedUtc =>
            new DateTime(long.Parse(SessionState.GetString(Key + "MapRequested", "0")), DateTimeKind.Utc);

        // Item 79: up to three shots of each wildfire with 30 or more cells alight, 25 simulated seconds apart, from 150 m
        // upwind and 55 m up, looking at the burning cells' centre. The flames are drawn for the shot's camera and the
        // simulation runs at 1x for the 10 real seconds the particles (which run in real time) need to fill in and the
        // smoke to rise.
        static GameObject fireCamera;
        static double fireShotAt;
        static string fireShotPath;
        static float fireShotSpeed;
        static readonly Dictionary<int, int> fireShots = new Dictionary<int, int>();
        static readonly Dictionary<int, float> fireShotTimes = new Dictionary<int, float>();

        static void UpdateFireShots(SpeciesManager manager, float simulated)
        {
            NaturalEventDirector natural = NaturalEventDirector.Active;
            if (natural == null || manager == null) return;
            if (fireCamera != null)
            {
                if (EditorApplication.timeSinceStartup < fireShotAt) return;
                string light = DayClock.IsRunning && DayClock.Current.daylight < 0.5f ? "night" : "day";
                fireShotPath = fireShotPath.Replace("{light}", light);
                Shot(fireCamera.GetComponent<Camera>(), fireShotPath);
                Object.DestroyImmediate(fireCamera);
                fireCamera = null;
                WildfireVisuals.ViewerOverride = null;
                manager.simulationSpeed = fireShotSpeed;
                Debug.Log($"Soak: fire shot {fireShotPath}");
                return;
            }
            WildfireGrid grid = natural.Wildfire.Grid;
            if (grid == null || grid.BurningCount < 30 || Camera.main == null) return;
            foreach (WildfireEvent fire in natural.Wildfire.Fires)
            {
                if (fire.IsOver || fire.Tally.burning < 30) continue;
                fireShots.TryGetValue(fire.Id, out int taken);
                if (taken >= 3) continue;
                if (fireShotTimes.TryGetValue(fire.Id, out float last) && simulated - last < 25f) continue;
                Vector2 sum = Vector2.zero;
                float weight = 0f, height = 0f;
                for (int i = 0; i < grid.BurningCount; i++)
                {
                    int index = grid.BurningCellAt(i, out Vector2 centre, out float intensity, out FireFuel fuel);
                    if (grid.FireOf(index) != fire.FireId) continue;
                    float w = 0.05f + intensity;
                    sum += centre * w;
                    height += fuel.height * w;
                    weight += w;
                }
                if (weight <= 0f) continue;
                Vector2 middle = sum / weight;
                Vector3 target = new Vector3(middle.x, height / weight + 6f, middle.y);
                Vector2 wind = natural.Conditions.windDirection;
                fireCamera = new GameObject("Soak Fire Camera");
                Camera camera = fireCamera.AddComponent<Camera>();
                camera.CopyFrom(Camera.main);
                camera.enabled = false;
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, 3000f);
                fireCamera.transform.position = target - new Vector3(wind.x, 0f, wind.y) * 150f + Vector3.up * 55f;
                fireCamera.transform.LookAt(target);
                WildfireVisuals.ViewerOverride = camera;
                fireShotAt = EditorApplication.timeSinceStartup + 10.0;
                fireShotSpeed = manager.simulationSpeed;
                manager.simulationSpeed = 1f;
                fireShotPath = Path.Combine(SessionState.GetString(Key + "Output", "."),
                                            $"fire-{fire.Id}-{taken + 1}-{{light}}-t{simulated + 10f:0}.png");
                fireShots[fire.Id] = taken + 1;
                fireShotTimes[fire.Id] = simulated;
                return;
            }
        }

        // Item 79 tuning: what lightning would find within 500 m of the animals, from 300 random points.
        static void ProbeFuel(NaturalEventDirector natural, SeekFood[] animals)
        {
            AnimalTerrainWorld world = AnimalTerrainWorld.Active;
            if (world == null || animals.Length == 0) return;
            var tree = new List<float>(); var grass = new List<float>(); var moisture = new List<float>();
            var wet = new List<float>(); var load = new List<float>(); var fuelMoisture = new List<float>();
            var flammability = new List<float>();
            int land = 0, sampled = 0, flammable = 0;
            WildfireRules rules = natural.wildfire.rules;
            float dryness = natural.Conditions.dryness;
            for (int i = 0; i < 300; i++)
            {
                Vector3 around = animals[UnityEngine.Random.Range(0, animals.Length)].transform.position;
                Vector2 offset = UnityEngine.Random.insideUnitCircle * 500f;
                Vector2 at = new Vector2(around.x + offset.x, around.z + offset.y);
                if (!world.TryGetSample(at, out EnvironmentSample sample) || !sample.isValid) continue;
                sampled++;
                if (!sample.isLand || sample.isWater) continue;
                land++;
                if (!natural.Wildfire.Landscape.TrySampleFuel(at, dryness, out FireFuel fuel)) continue;
                tree.Add(sample.treeCover); grass.Add(sample.grassBiomass); moisture.Add(sample.moisture);
                wet.Add(Mathf.Max(sample.riverStrength, sample.lakeStrength, sample.isShore ? 0.6f : 0f));
                load.Add(fuel.load); fuelMoisture.Add(fuel.moisture);
                float f = rules.Flammability(fuel);
                flammability.Add(f);
                if (f >= natural.wildfire.minimumStrikeFlammability) flammable++;
            }
            string Q(List<float> v)
            {
                if (v.Count == 0) return "-";
                v.Sort();
                return $"{v[v.Count / 10]:0.00}/{v[v.Count / 2]:0.00}/{v[v.Count * 9 / 10]:0.00}";
            }
            Debug.Log($"Soak: fuel probe at dryness {dryness:0.00}: {sampled} sampled, {land} on land, {flammable} flammable; " +
                      $"10th/50th/90th percentiles: tree cover {Q(tree)}, grass {Q(grass)}, moisture {Q(moisture)}, " +
                      $"wetness {Q(wet)}, fuel load {Q(load)}, fuel moisture {Q(fuelMoisture)}, flammability {Q(flammability)}");
        }

        static void RequestMap(string kind)
        {
            SessionState.SetString(Key + "MapRequested", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/TopDownMapCapture.request", kind);
            Debug.Log("Soak: requested top-down map " + kind);
        }

        static void ShotOverAnimals(SeekFood[] animals, string path)
        {
            if (animals.Length == 0 || Camera.main == null)
                return;
            // Looks down at 50 degrees on the middle of the largest group: the animal with the most neighbours within 60 m.
            Vector3 centre = animals.Select(a => a.transform.position)
                .OrderByDescending(p => animals.Count(b => (b.transform.position - p).sqrMagnitude < 3600f)).First();
            GameObject holder = new GameObject("Soak Camera");
            Camera camera = holder.AddComponent<Camera>();
            camera.CopyFrom(Camera.main);
            holder.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            holder.transform.position = centre - holder.transform.forward * 70f;
            Shot(camera, path);
            Object.DestroyImmediate(holder);
        }

        static void Shot(Camera camera, string path)
        {
            if (camera == null)
                return;
            RenderTexture target = RenderTexture.GetTemporary(ShotWidth, ShotHeight, 24);
            RenderTexture previousTarget = camera.targetTexture;
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = previousTarget;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D image = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
            image.Apply();
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target);
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }

        static void Finish(string reason)
        {
            Debug.Log("Soak: " + reason);
            SessionState.SetInt(Key + "Stage", 3);
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            else Quit();
        }

        static void Quit()
        {
            EditorApplication.update -= Tick;
            string recording = SessionState.GetString(Key + "Recording", "");
            if (!string.IsNullOrEmpty(recording) && Directory.Exists(recording))
            {
                string target = Path.Combine(Out, "recording");
                Directory.CreateDirectory(target);
                foreach (string file in Directory.GetFiles(recording))
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            }
            SessionState.SetBool(Key + "Active", false);
            Debug.Log("Soak: done, output " + Out);
            EditorApplication.Exit(0);
        }
    }
}
