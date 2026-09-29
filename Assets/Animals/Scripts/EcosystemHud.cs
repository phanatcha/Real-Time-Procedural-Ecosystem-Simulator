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
    }

    // Each species' average traits when it was first seen, to show how far it has drifted since.
    private readonly Dictionary<string, Traits> traitsWhenFirstSeen = new Dictionary<string, Traits>();
    private readonly StringBuilder text = new StringBuilder();
    private FoodSpawner foodSpawner;
    private HabitatNavigation habitatNavigation;
    private float speedBeforePause = 1f;
    private float lastWorldStartTime = -1f;
    private GUIStyle style;

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

        string ceiling = manager.populationCeiling > 0 ? $" / {manager.populationCeiling} ceiling" : "";
        string atCeiling = manager.IsAtPopulationCeiling ? " <color=#ffd060>(births paused)</color>" : "";
        string plants = foodSpawner != null ? $"    <b>Plants</b> {foodSpawner.ShownPlantCount} shown" : "";
        string tiles = habitatNavigation != null ? $"    <b>Walkable tiles</b> {habitatNavigation.NavigableTileCount}" : "";
        text.Append($"<b>Animals</b> {manager.TotalPopulation}{ceiling}{atCeiling}{plants}{tiles}\n");

        int speciesEver = 0;
        int extinct = 0;
        int births = 0;
        int mutatedBirths = 0;
        foreach (SpeciesTelemetryRecord record in manager.Telemetry.Values)
        {
            speciesEver++;
            if (record.extinctionTime >= 0f) extinct++;
            births += record.reproductionEvents;
            mutatedBirths += record.mutatedOffspring;
        }

        string mutationShare = births > 0 ? $" ({100f * mutatedBirths / births:0}% carried mutations)" : "";
        text.Append($"<b>Births</b> {births}{mutationShare}    <b>Generation</b> " +
                    $"{manager.AverageGeneration:0.0} on average, newest {manager.HighestGeneration}\n");
        text.Append($"<b>Species</b> {manager.SpeciesPopulation.Count} alive, {speciesEver} ever, {extinct} extinct    " +
                    $"(a group splits off once it is {manager.speciationThreshold:0.###} apart genetically, " +
                    "typically after 40-50 generations)\n");

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
            lifespan = record.maxLifespan
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
                    $"lifespan {now.lifespan:0}s {Signed(now.lifespan - first.lifespan, "0")}");
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
