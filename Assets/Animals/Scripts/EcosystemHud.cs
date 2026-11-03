using Boreal.TerrainGeneration;
using Boreal.Core;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Boreal.Animals
{
    // On-screen summary of the simulation, so evolution is visible without the Inspector. Keys: 1-5 set the
    // speed to 1x/5x/10x/20x/50x, P pauses or resumes, H hides the panel, G hides the chart under it.
    [DisallowMultipleComponent]
    public class EcosystemHud : MonoBehaviour
    {
        static readonly float[] SpeedPresets = { 1f, 5f, 10f, 20f, 50f };

        public bool visible = true;
        [Header("Chart")]
        [Tooltip("Animals, species and plants over simulated time, drawn under the panel. G shows or hides it.")]
        public bool showChart = true;
        [Tooltip("Simulated seconds between points, as the run recorder samples. Once the chart is full, it keeps " +
                 "every other point and samples half as often.")]
        [Min(1f)] public float chartSampleInterval = 10f;

        private struct Traits
        {
            public float diet;
            public float bulk;
            public float height;
            public float lifespan;
            public float sexualDrive;
            public float breedingThreshold;
            public float torpor;
            public float mutationRate;
            public float litter;
            public float harmfulMutations;
        }

        private struct ChartSample
        {
            public int animals;
            public int species;
            public int plants;
            public bool winter;
        }

        private const int ChartWidth = 420;
        private const int ChartHeight = 110;
        private const int ChartTopMargin = 20;
        private const int ChartMaxSamples = 840;
        private static readonly Color32 ChartBackground = new Color32(16, 20, 24, 210);
        private static readonly Color32 ChartWinter = new Color32(70, 100, 150, 110);
        private static readonly Color32 AnimalsLine = new Color32(255, 255, 255, 255);
        private static readonly Color32 SpeciesLine = new Color32(255, 208, 96, 255);
        private static readonly Color32 PlantsLine = new Color32(110, 220, 120, 255);
        private readonly List<ChartSample> chartSamples = new List<ChartSample>();
        private float chartWorldStartTime = -1f;
        private float nextChartSampleTime;
        private float chartIntervalNow;
        private Texture2D chartTexture;
        private Color32[] chartPixels;
        private bool chartDirty;
        private string chartLegend = "";
        private GUIStyle legendStyle;

        // Each species' average traits when it was first seen, to show how far it has drifted since.
        private readonly Dictionary<string, Traits> traitsWhenFirstSeen = new Dictionary<string, Traits>();
        private readonly StringBuilder text = new StringBuilder();
        private FoodSpawner foodSpawner;
        private HabitatNavigation habitatNavigation;
        private float speedBeforePause = 1f;
        private float lastWorldStartTime = -1f;
        private GUIStyle style;
        // The body-plan line is recounted a couple of times a second rather than on every GUI pass.
        private static readonly BodyPartType[] ShownParts =
        {
            BodyPartType.Legs, BodyPartType.Neck, BodyPartType.Horn,
            BodyPartType.Plates, BodyPartType.EyeStalks, BodyPartType.Fins
        };
        private readonly int[] animalsWithPart = new int[ShownParts.Length];
        private readonly float[] partSizeTotals = new float[ShownParts.Length];
        private string bodySummary = "";
        private string swimmingSummary = "";
        private string bodySummarySpecies;
        private float nextBodySummaryTime;

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            SpeciesManager manager = SpeciesManager.Instance;
            if (manager == null) return;
            SampleChart(manager);
            if (keyboard == null || GodCamera.IsTypingInUI()) return;

            if (keyboard.hKey.wasPressedThisFrame) visible = !visible;
            if (keyboard.gKey.wasPressedThisFrame) showChart = !showChart;

            if (keyboard.pKey.wasPressedThisFrame)
            {
                if (manager.simulationSpeed > 0f && !manager.IsSafetyPaused)
                {
                    speedBeforePause = manager.simulationSpeed;
                    manager.simulationSpeed = 0f;
                }
                else
                {
                    SetSpeed(manager, speedBeforePause);
                }
            }

            Key[] speedKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };
            for (int index = 0; index < speedKeys.Length; index++)
            {
                if (keyboard[speedKeys[index]].wasPressedThisFrame) SetSpeed(manager, SpeedPresets[index]);
            }
        }

        static void SetSpeed(SpeciesManager manager, float speed)
        {
            if (manager.IsSafetyPaused) manager.ResumeAfterSafetyPause();
            manager.simulationSpeed = Mathf.Max(0.01f, speed);
        }

        void OnGUI()
        {
            SpeciesManager manager = SpeciesManager.Instance;
            if (!visible || manager == null) return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 13,
                    richText = true,
                    padding = new RectOffset(10, 10, 8, 8)
                };
            }

            if (foodSpawner == null) foodSpawner = FindAnyObjectByType<FoodSpawner>();
            if (habitatNavigation == null) habitatNavigation = FindAnyObjectByType<HabitatNavigation>();

            string content = BuildText(manager);
            Vector2 size = style.CalcSize(new GUIContent(content));
            GUI.Box(new Rect(10f, 10f, size.x, size.y), content, style);
            if (showChart) DrawChart(10f, 10f + size.y + 6f);
        }

        // One point every Chart Sample Interval simulated seconds, from the world's start. A new world starts the
        // chart again.
        void SampleChart(SpeciesManager manager)
        {
            if (!Mathf.Approximately(manager.WorldStartTime, chartWorldStartTime))
            {
                chartSamples.Clear();
                chartWorldStartTime = manager.WorldStartTime;
                chartIntervalNow = Mathf.Max(1f, chartSampleInterval);
                nextChartSampleTime = 0f;
                chartDirty = true;
            }

            float simulated = Mathf.Max(0f, Time.time - manager.WorldStartTime);
            if (simulated < nextChartSampleTime) return;
            nextChartSampleTime = simulated + chartIntervalNow;

            if (foodSpawner == null) foodSpawner = FindAnyObjectByType<FoodSpawner>();
            chartSamples.Add(new ChartSample
            {
                animals = manager.TotalPopulation,
                species = manager.SpeciesAliveCount,
                plants = foodSpawner != null ? foodSpawner.ShownPlantCount : 0,
                winter = SeasonClock.IsRunning &&
                         SeasonClock.Active.SeasonName.EndsWith("winter", System.StringComparison.OrdinalIgnoreCase)
            });
            // Full, the chart keeps every other point and samples half as often, so a long run still fits.
            if (chartSamples.Count > ChartMaxSamples)
            {
                for (int index = chartSamples.Count - 1; index >= 0; index -= 2) chartSamples.RemoveAt(index);
                chartIntervalNow *= 2f;
            }
            chartDirty = true;
        }

        void DrawChart(float x, float y)
        {
            if (chartSamples.Count < 2) return;
            if (chartTexture == null)
            {
                chartTexture = new Texture2D(ChartWidth, ChartHeight, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave
                };
                chartPixels = new Color32[ChartWidth * ChartHeight];
                chartDirty = true;
            }
            if (legendStyle == null)
            {
                legendStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true };
                legendStyle.normal.textColor = Color.white;
            }
            if (chartDirty) RedrawChart();

            Rect area = new Rect(x, y, ChartWidth, ChartHeight);
            GUI.DrawTexture(area, chartTexture);
            GUI.Label(new Rect(area.x + 6f, area.y + 1f, ChartWidth - 12f, ChartTopMargin), chartLegend, legendStyle);
        }

        // Each series is scaled to its own highest value, named in the legend. Winters are shaded.
        void RedrawChart()
        {
            chartDirty = false;
            for (int index = 0; index < chartPixels.Length; index++) chartPixels[index] = ChartBackground;

            int count = chartSamples.Count;
            int maxAnimals = 1, maxSpecies = 1, maxPlants = 1;
            foreach (ChartSample sample in chartSamples)
            {
                maxAnimals = Mathf.Max(maxAnimals, sample.animals);
                maxSpecies = Mathf.Max(maxSpecies, sample.species);
                maxPlants = Mathf.Max(maxPlants, sample.plants);
            }

            int plotHeight = ChartHeight - ChartTopMargin - 4;
            for (int index = 0; index < count; index++)
            {
                if (!chartSamples[index].winter) continue;
                int from = ChartX(index, count), to = ChartX(index + 1, count);
                for (int px = from; px <= Mathf.Min(to, ChartWidth - 1); px++)
                    for (int py = 0; py < ChartHeight - ChartTopMargin; py++)
                        chartPixels[py * ChartWidth + px] = ChartWinter;
            }
            for (int index = 1; index < count; index++)
            {
                ChartSample a = chartSamples[index - 1], b = chartSamples[index];
                int x0 = ChartX(index - 1, count), x1 = ChartX(index, count);
                Plot(x0, ChartY(a.plants, maxPlants, plotHeight), x1, ChartY(b.plants, maxPlants, plotHeight), PlantsLine);
                Plot(x0, ChartY(a.species, maxSpecies, plotHeight), x1, ChartY(b.species, maxSpecies, plotHeight), SpeciesLine);
                Plot(x0, ChartY(a.animals, maxAnimals, plotHeight), x1, ChartY(b.animals, maxAnimals, plotHeight), AnimalsLine);
            }
            chartTexture.SetPixels32(chartPixels);
            chartTexture.Apply(false);

            ChartSample last = chartSamples[count - 1];
            string span = FormatDuration((count - 1) * chartIntervalNow);
            chartLegend = $"<color=#ffffff>animals {last.animals}</color> (peak {maxAnimals})   " +
                          $"<color=#ffd060>species {last.species}</color> (peak {maxSpecies})   " +
                          $"<color=#6edc78>plants shown {last.plants}</color> (peak {maxPlants})   last {span}" +
                          (SeasonClock.IsRunning ? ", winters shaded" : "");
        }

        static int ChartX(int index, int count)
        {
            return Mathf.Clamp(Mathf.RoundToInt(index * (ChartWidth - 1f) / Mathf.Max(1, count - 1)), 0, ChartWidth - 1);
        }

        static int ChartY(int value, int maximum, int plotHeight)
        {
            return 2 + Mathf.RoundToInt(plotHeight * Mathf.Clamp01(value / (float)maximum));
        }

        void Plot(int x0, int y0, int x1, int y1, Color32 colour)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                if (x0 >= 0 && x0 < ChartWidth && y0 >= 0 && y0 < ChartHeight) chartPixels[y0 * ChartWidth + x0] = colour;
                if (x0 == x1 && y0 == y1) break;
                int doubled = 2 * error;
                if (doubled >= dy) { error += dy; x0 += sx; }
                if (doubled <= dx) { error += dx; y0 += sy; }
            }
        }

        void OnDestroy()
        {
            if (chartTexture != null) Destroy(chartTexture);
        }

        string BuildText(SpeciesManager manager)
        {
            // A new world starts a new history, so drift is measured from its own first sightings.
            if (!Mathf.Approximately(manager.WorldStartTime, lastWorldStartTime))
            {
                traitsWhenFirstSeen.Clear();
                lastWorldStartTime = manager.WorldStartTime;
            }

            text.Clear();
            float simulatedSeconds = Mathf.Max(0f, Time.time - manager.WorldStartTime);
            string speed = manager.IsSafetyPaused ? "<color=#ff7070>SAFETY PAUSED</color> (press 1-5 to resume)"
                : manager.simulationSpeed <= 0f ? "<color=#ffd060>PAUSED</color>"
                : $"{manager.simulationSpeed:0.#}x (achieved {manager.AchievedSimulationSpeed:0.#}x)";
            text.Append($"<b>Speed</b> {speed}    1-5: 1x 5x 10x 20x 50x   P: pause   H: hide   G: chart   T: species tree   K: god tools   N: day view   click an animal to inspect it, F follows it\n");
            if (manager.IsSafetyPaused)
            {
                text.Append($"   <color=#ff9090>{manager.LastSafetyPauseReason}</color>\n");
            }

            text.Append($"<b>Simulated time</b> {FormatDuration(simulatedSeconds)}{DescribeSeason()}\n");
            string day = DayClock.Describe();
            if (day.Length > 0) text.Append(day.TrimStart()).Append('\n');

            bool seaCeiling = manager.seaPopulationCeiling > 0;
            string ceiling = manager.populationCeiling <= 0 ? ""
                : seaCeiling ? $" / ceilings {manager.populationCeiling} on land, {manager.seaPopulationCeiling} in water"
                : $" / {manager.populationCeiling} ceiling";
            bool landPaused = manager.IsAtPopulationCeiling;
            bool seaPaused = seaCeiling && manager.IsAtSeaPopulationCeiling;
            string paused = landPaused && (seaPaused || !seaCeiling) ? "births paused"
                : landPaused ? "births paused on land"
                : seaPaused ? "births paused in water" : null;
            string atCeiling = paused != null ? $" <color=#ffd060>({paused})</color>" : "";
            string buried = foodSpawner != null && foodSpawner.BuriedPlantCount > 0
                ? $", {foodSpawner.BuriedPlantCount} under snow"
                : "";
            string plants = foodSpawner != null
                ? $"    <b>Plants</b> {foodSpawner.ShownPlantCount} shown{buried}{DescribeSeaPlants()}"
                : "";
            string tiles = habitatNavigation != null ? $"    <b>Walkable tiles</b> {habitatNavigation.NavigableTileCount}" : "";
            string offscreen = manager.OffscreenPopulation != null ? $"    <b>Off-screen</b> {manager.OffscreenTotal}" : "";
            string water = WaterAccess.HasWater ? $"    <b>In water</b> {DescribeAnimalsInWater(manager)}" : "";
            text.Append($"<b>Animals</b> {manager.TotalPopulation}{ceiling}{atCeiling}{offscreen}{water}{plants}{tiles}\n");

            int speciesEver = 0;
            int extinct = 0;
            int births = 0;
            int mutatedBirths = 0;
            int sexualBirths = 0;
            foreach (SpeciesTelemetryRecord record in manager.Telemetry.Values)
            {
                speciesEver++;
                if (record.extinctionTime >= 0f) extinct++;
                births += record.reproductionEvents;
                mutatedBirths += record.mutatedOffspring;
                sexualBirths += record.sexualOffspring;
            }

            string birthShares = births > 0
                ? $" ({100f * sexualBirths / births:0}% from two parents, {100f * mutatedBirths / births:0}% carried mutations)"
                : "";
            text.Append($"<b>Births</b> {births}{birthShares}    <b>Generation</b> " +
                        $"{manager.AverageGeneration:0.0} on average, newest {manager.HighestGeneration}\n");
            text.Append($"<b>Species</b> {manager.SpeciesAliveCount} alive, {speciesEver} ever, {extinct} extinct    " +
                        $"(a group splits off once it is {manager.speciationThreshold:0.###} apart genetically, " +
                        "typically after 50-60 generations)\n");

            AppendLargestSpecies(manager);
            return text.ToString().TrimEnd();
        }

        void AppendLargestSpecies(SpeciesManager manager)
        {
            string largest = null;
            int largestPopulation = 0;
            foreach (KeyValuePair<string, int> species in manager.SpeciesPopulation)
            {
                if (species.Value > largestPopulation)
                {
                    largest = species.Key;
                    largestPopulation = species.Value;
                }
            }

            if (largest == null || !manager.Telemetry.TryGetValue(largest, out SpeciesTelemetryRecord record)) return;

            Traits now = new Traits
            {
                diet = record.dietAffinity,
                bulk = record.bodyBulk,
                height = record.bodyHeight,
                lifespan = record.maxLifespan,
                sexualDrive = record.sexualDrive,
                breedingThreshold = record.breedingThreshold,
                torpor = record.torpor,
                mutationRate = record.mutationRate,
                litter = record.litterSize,
                harmfulMutations = record.harmfulMutations
            };
            if (!traitsWhenFirstSeen.TryGetValue(largest, out Traits first))
            {
                first = now;
                traitsWhenFirstSeen.Add(largest, now);
            }

            text.Append($"<b>Largest species</b> {largest} ({largestPopulation}), {record.dietClassification}: " +
                        "average (lowest-highest), change in average since it appeared\n");
            text.Append($"   diet {Describe(now.diet, record.dietRange, now.diet - first.diet)}   " +
                        $"bulk {Describe(now.bulk, record.bodyBulkRange, now.bulk - first.bulk)}   " +
                        $"height {Describe(now.height, record.bodyHeightRange, now.height - first.height)}   " +
                        $"lifespan {now.lifespan:0}s {Signed(now.lifespan - first.lifespan, "0")}   " +
                        $"sexual drive {now.sexualDrive:0.00} {Signed(now.sexualDrive - first.sexualDrive, "0.00")}");
            text.Append($"\n   harmful mutations: {now.harmfulMutations:0.0} " +
                        $"{Signed(now.harmfulMutations - first.harmfulMutations, "0.0")}   " +
                        $"mutation rate {now.mutationRate:0.00}× {Signed(now.mutationRate - first.mutationRate, "0.00")}   " +
                        $"breeds at {100f * now.breedingThreshold:0}% energy " +
                        $"{Signed(100f * (now.breedingThreshold - first.breedingThreshold), "0")}   " +
                        $"torpor {now.torpor:0.00} {Signed(now.torpor - first.torpor, "0.00")}   " +
                        $"litter {now.litter:0.00} {Signed(now.litter - first.litter, "0.00")}");
            text.Append($"\n   perks: {AnimalPerks.DescribeShares(record.perkShares)}");
            text.Append($"\n   active: {DescribeChronotypes(manager, largest)}");
            text.Append($"\n   hunters: {DescribeHunters(manager, largest, record.predationDeaths)}");
            text.Append($"\n   body parts: {DescribeBodyParts(manager, largest)}");
            text.Append($"\n   swimming ability: {swimmingSummary}");
        }

        // The species' chronotype (item 74): its average and range, and how many of its animals are resting now.
        static string DescribeChronotypes(SpeciesManager manager, string species)
        {
            float total = 0f, lowest = 1f, highest = 0f;
            int counted = 0, resting = 0;
            foreach (SeekFood animal in manager.ActiveAgents)
            {
                if (animal == null || animal.speciesName != species) continue;
                float nocturnality = animal.nocturnality;
                total += nocturnality;
                lowest = Mathf.Min(lowest, nocturnality);
                highest = Mathf.Max(highest, nocturnality);
                counted++;
                if (animal.IsResting) resting++;
            }

            if (counted == 0) return "no animals in view";
            float average = total / counted;
            return $"{SeekFood.DescribeChronotype(average)} ({average:0.00}, {lowest:0.00}-{highest:0.00}), " +
                   $"{100f * resting / counted:0}% resting now";
        }

        // How many of the species' animals hunt their own kind (item 66), and how many it has lost to hunters.
        static string DescribeHunters(SpeciesManager manager, string species, int killed)
        {
            int counted = 0, hunters = 0;
            foreach (SeekFood animal in manager.ActiveAgents)
            {
                if (animal == null || animal.speciesName != species) continue;
                counted++;
                if (animal.HuntsOwnKind) hunters++;
            }

            if (counted == 0) return "no animals in view";
            return $"{100f * hunters / counted:0}% of those in view hunt their own kind, {killed} killed by hunters so far";
        }

        // The time of year and what it does compared with the terrain's base climate.
        static string DescribeSeason()
        {
            SeasonClock clock = SeasonClock.Active;
            if (clock == null) return "";
            if (!SeasonClock.IsRunning) return "    <b>Season</b> off";

            SeasonConditions conditions = SeasonClock.Conditions;
            SeasonState season = conditions.state;
            float celsius = SeasonClock.TemperatureShiftCelsius(season);
            string shift = float.IsNaN(celsius) ? ""
                : celsius <= -0.5f ? $"{-celsius:0} °C colder, "
                : celsius >= 0.5f ? $"{celsius:0} °C warmer, "
                : "usual temperature, ";

            string easing = clock.EffectiveStrength < clock.strength - 0.005f
                ? $", seasons at {100f * clock.EffectiveStrength / clock.strength:0}% while easing in"
                : "";
            return $"    <b>Season</b> {clock.SeasonName}, year {clock.Year} ({100f * clock.YearProgress:0}% through" +
                   $"{easing}): {shift}plants regrow at {100f * season.growthMultiplier * conditions.growthBalance:0}%, " +
                   $"snow {100f * season.snowAmount:0}%";
        }

        // How many of the shown plants are in the sea, and how many sea plants have been eaten in this world.
        string DescribeSeaPlants()
        {
            if (!foodSpawner.growSeaFood || !WaterAccess.HasWater) return "";

            return $" ({foodSpawner.ShownSeaPlantCount} in the sea, {foodSpawner.SeaPlantsEaten} sea plants eaten)";
        }

        static string DescribeAnimalsInWater(SpeciesManager manager)
        {
            int inWater = 0;
            int inDeadZones = 0;
            foreach (SeekFood animal in manager.ActiveAgents)
            {
                if (animal == null || !animal.IsInWater) continue;

                inWater++;
                if (animal.IsInDeadZone) inDeadZones++;
            }

            return inDeadZones > 0 ? $"{inWater} ({inDeadZones} in dead zones)" : inWater.ToString();
        }

        // Share of the species' animals carrying each part, with the average size of those parts (0-1). Also
        // refreshes the species' swimming summary.
        string DescribeBodyParts(SpeciesManager manager, string species)
        {
            if (species == bodySummarySpecies && Time.unscaledTime < nextBodySummaryTime)
            {
                return bodySummary;
            }

            bodySummarySpecies = species;
            nextBodySummaryTime = Time.unscaledTime + 0.5f;
            System.Array.Clear(animalsWithPart, 0, animalsWithPart.Length);
            System.Array.Clear(partSizeTotals, 0, partSizeTotals.Length);
            int members = 0;
            int deepWaterSwimmers = 0;
            float swimmingAbilityTotal = 0f;
            foreach (SeekFood animal in manager.ActiveAgents)
            {
                AnimalGenome genome = animal != null ? animal.Genome : null;
                if (genome == null || !genome.IsValid || animal.speciesName != species)
                {
                    continue;
                }

                members++;
                swimmingAbilityTotal += animal.SwimmingAbility;
                if (animal.CanSwimDeepWater) deepWaterSwimmers++;
                for (int part = 0; part < ShownParts.Length; part++)
                {
                    float largest = 0f;
                    for (int site = 0; site < AnimalGenome.SiteCount; site++)
                    {
                        if (genome.GetPartType((BodySite)site) == ShownParts[part])
                        {
                            largest = Mathf.Max(largest, genome.GetPartSize((BodySite)site));
                        }
                    }

                    if (largest > 0f)
                    {
                        animalsWithPart[part]++;
                        partSizeTotals[part] += largest;
                    }
                }
            }

            StringBuilder summary = new StringBuilder();
            for (int part = 0; part < ShownParts.Length; part++)
            {
                if (animalsWithPart[part] == 0) continue;
                if (summary.Length > 0) summary.Append(", ");
                summary.Append($"{AnimalBodyPlan.PartName(ShownParts[part])} {100f * animalsWithPart[part] / members:0}% " +
                               $"(size {partSizeTotals[part] / animalsWithPart[part]:0.00})");
            }

            bodySummary = summary.Length > 0 ? summary.ToString() : "none yet, still a plain capsule";
            swimmingSummary = members > 0
                ? $"{swimmingAbilityTotal / members:0.00} on average, {100f * deepWaterSwimmers / members:0}% can swim in deep water"
                : "";
            return bodySummary;
        }

        static string Describe(float average, Vector2 range, float change)
        {
            return $"{average:0.00} ({range.x:0.00}-{range.y:0.00}) {Signed(change, "0.00")}";
        }

        static string Signed(float value, string format)
        {
            return (value >= 0f ? "+" : "") + value.ToString(format);
        }

        static string FormatDuration(float seconds)
        {
            int total = Mathf.FloorToInt(seconds);
            return $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}";
        }
    }
}
