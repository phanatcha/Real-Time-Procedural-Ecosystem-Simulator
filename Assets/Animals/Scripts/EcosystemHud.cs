using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

// On-screen summary of the simulation, so evolution is visible without the Inspector. Keys: 1-5 set the
// speed to 1x/5x/10x/20x/50x, P pauses or resumes, H hides the panel.
[DisallowMultipleComponent]
public class EcosystemHud : MonoBehaviour
{
    static readonly float[] SpeedPresets = { 1f, 5f, 10f, 20f, 50f };

    public bool visible = true;

    private struct Traits
    {
        public float diet;
        public float bulk;
        public float height;
        public float lifespan;
        public float sexualDrive;
        public float harmfulMutations;
    }

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
        if (keyboard == null || manager == null || GodCamera.IsTypingInUI()) return;

        if (keyboard.hKey.wasPressedThisFrame) visible = !visible;

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
        text.Append($"<b>Speed</b> {speed}    1-5: 1x 5x 10x 20x 50x   P: pause   H: hide\n");
        if (manager.IsSafetyPaused)
        {
            text.Append($"   <color=#ff9090>{manager.LastSafetyPauseReason}</color>\n");
        }

        text.Append($"<b>Simulated time</b> {FormatDuration(simulatedSeconds)}\n");

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
        string plants = foodSpawner != null ? $"    <b>Plants</b> {foodSpawner.ShownPlantCount} shown{DescribeSeaPlants()}" : "";
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
                    $"{Signed(now.harmfulMutations - first.harmfulMutations, "0.0")}");
        text.Append($"\n   body parts: {DescribeBodyParts(manager, largest)}");
        text.Append($"\n   swimming ability: {swimmingSummary}");
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
