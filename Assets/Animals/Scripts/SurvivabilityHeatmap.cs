using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// What a survivability factor knows about one spot on the map.
public struct SurvivabilityCell
{
    public EnvironmentSample environment;
    public float celsius;
}

// One thing that makes a place easier or harder to live in. To add a variable to the heatmap, write another
// of these and add it to SurvivabilityHeatmap.Factors.
public interface ISurvivabilityFactor
{
    string Name { get; }

    // How much this factor counts against the others; zero leaves it out.
    float Weight { get; }

    // Runs on the main thread before each full recalculation, to pick up settings that may have changed.
    void Prepare();

    // 0 where this factor alone would make the place deadly, 1 where it is ideal.
    float Score(in SurvivabilityCell cell);
}

// Temperature against the comfort range: full marks inside it, falling to zero where the cold or heat starts
// to do damage (AnimalTemperature's stress of 1).
[Serializable]
public class TemperatureSurvivability : ISurvivabilityFactor
{
    [Min(0f)] public float weight = 1f;
    [Tooltip("Judge by the comfort range of the most numerous species, which drifts as it evolves.")]
    public bool followLargestSpecies = true;
    [Tooltip("Used when there is no species to follow. These match the founders.")]
    public float preferredTemperature = 2f;
    [Min(0f)] public float coldTolerance = 18f;
    [Min(0f)] public float heatTolerance = 16f;
    [Tooltip("Degrees beyond the comfort range per unit of temperature stress, as in AnimalTemperature.")]
    [Min(0.1f)] public float degreesPerStressUnit = 20f;

    private float minimumComfort;
    private float maximumComfort;
    private string comfortSource;

    public string Name => "Temperature";
    public float Weight => weight;

    public string Describe()
    {
        return $"Temperature comfort range: {minimumComfort:0.#} to {maximumComfort:0.#} C ({comfortSource}); " +
               $"the score reaches zero {degreesPerStressUnit:0.#} C beyond it";
    }

    public void Prepare()
    {
        float preferred = preferredTemperature;
        float cold = coldTolerance;
        float heat = heatTolerance;
        comfortSource = "the founders' settings";
        if (followLargestSpecies && TryGetLargestSpecies(out SpeciesTelemetryRecord record) &&
            record.coldTolerance + record.heatTolerance > 0f)
        {
            preferred = record.preferredTemperature;
            cold = record.coldTolerance;
            heat = record.heatTolerance;
            comfortSource = $"species {record.speciesName}, the most numerous";
        }

        minimumComfort = preferred - Mathf.Max(0f, cold);
        maximumComfort = preferred + Mathf.Max(0f, heat);
    }

    public float Score(in SurvivabilityCell cell)
    {
        float beyond = Mathf.Max(0f, Mathf.Max(minimumComfort - cell.celsius, cell.celsius - maximumComfort));
        return 1f - Mathf.Clamp01(beyond / Mathf.Max(0.1f, degreesPerStressUnit));
    }

    static bool TryGetLargestSpecies(out SpeciesTelemetryRecord record)
    {
        record = null;
        SpeciesManager manager = SpeciesManager.Instance;
        if (manager == null) return false;

        int largest = 0;
        foreach (KeyValuePair<string, int> species in manager.SpeciesPopulation)
        {
            if (species.Value > largest && manager.Telemetry.TryGetValue(species.Key, out SpeciesTelemetryRecord found))
            {
                largest = species.Value;
                record = found;
            }
        }

        return record != null;
    }
}

// Food supply: the nutrition plants regrow here each minute, by FoodSpawner's own placement and regrowth rules.
[Serializable]
public class FoodSurvivability : ISurvivabilityFactor
{
    [Min(0f)] public float weight = 1f;
    [Tooltip("Nutrition regrowing per simulated minute on one plant cell that counts as plentiful (full marks).")]
    [Min(0.01f)] public float plentifulNutritionPerMinute = 30f;

    private FoodSpawner foodSpawner;

    public string Name => "Food";

    // Without a food spawner there is no food model, so the factor is left out.
    public float Weight => foodSpawner != null ? weight : 0f;

    public void Prepare()
    {
        if (foodSpawner == null) foodSpawner = UnityEngine.Object.FindAnyObjectByType<FoodSpawner>();
    }

    public float Score(in SurvivabilityCell cell)
    {
        float nutrition = foodSpawner.EstimateNutritionPerMinute(cell.environment, cell.celsius);
        return Mathf.Clamp01(nutrition / plentifulNutritionPerMinute);
    }
}

// How easy it is to survive across the island, painted over the terrain from red (harsh) to green (lenient)
// and switched on with the Heatmap checkbox in the bottom-left corner. Each factor scores a spot from 0 to 1,
// and the scores are combined as a weighted geometric mean, so one hopeless factor makes a place harsh
// however good the others are. Water, shore and cliffs that animals cannot use are left unpainted.
[DisallowMultipleComponent]
public class SurvivabilityHeatmap : MonoBehaviour
{
    static readonly int HeatmapId = Shader.PropertyToID("_SurvivalHeatmap");
    static readonly int RectId = Shader.PropertyToID("_SurvivalHeatmapRect");
    static readonly int OpacityId = Shader.PropertyToID("_SurvivalHeatmapOpacity");

    // Harsh to lenient.
    static readonly Color[] Ramp =
    {
        new Color32(200, 40, 40, 255),
        new Color32(236, 112, 30, 255),
        new Color32(246, 204, 52, 255),
        new Color32(134, 196, 64, 255),
        new Color32(38, 160, 84, 255),
    };
    static readonly Color32 Unpainted = new Color32(0, 0, 0, 0);

    // What each map cell holds, for the saved map and statistics.
    const byte OutsideCell = 0;
    const byte HabitableCell = 1;
    const byte WaterCell = 2;
    const byte BlockedCell = 3;
    const byte NoLimit = 255;
    static readonly Color32 SavedWaterColour = new Color32(170, 198, 214, 255);
    static readonly Color32 SavedBlockedColour = new Color32(190, 192, 184, 255);
    static readonly Color32 SavedOutsideColour = new Color32(255, 255, 255, 255);

    // Shared with the other small panels in the bottom-left corner (SeaBiomeMap).
    internal static readonly Color PillColor = new Color32(14, 22, 24, 235);
    internal static readonly Color PillHoverColor = new Color32(28, 39, 42, 240);
    internal static readonly Color OutlineColor = new Color(1f, 1f, 1f, 0.09f);
    internal static readonly Color BoxColor = new Color(1f, 1f, 1f, 0.4f);
    internal static readonly Color AccentColor = new Color32(94, 214, 168, 255);
    internal static readonly Color OnAccentColor = new Color32(9, 32, 25, 255);
    internal static readonly Color TextColor = new Color32(236, 243, 240, 255);
    internal static readonly Color MutedTextColor = new Color32(140, 160, 154, 255);

    static Sprite tickSprite;

    [Tooltip("Show the heatmap as soon as the scene starts.")]
    public bool showOnStart;
    [Range(0f, 1f)] public float opacity = 0.75f;

    [Header("Map")]
    [Tooltip("Cells along each side of the map, which spans the whole habitable area.")]
    [Range(32, 256)] public int resolution = 128;
    [Tooltip("Real seconds between recalculations while shown, so the map follows the evolving species.")]
    [Min(1f)] public float refreshInterval = 20f;
    [Tooltip("Real milliseconds per frame spent recalculating, so the map fills in without a hitch.")]
    [Min(0.5f)] public float millisecondsPerFrame = 3f;
    [Tooltip("Save the first complete map of each world as a top-down PNG, with statistics, in the " +
             "Heatmaps folder under Application.persistentDataPath.")]
    public bool saveMaps = true;

    [Header("Factors")]
    public TemperatureSurvivability temperature = new TemperatureSurvivability();
    public FoodSurvivability food = new FoodSurvivability();

    // Every variable in the map. New factors go here.
    ISurvivabilityFactor[] Factors => new ISurvivabilityFactor[] { temperature, food };

    AnimalTerrainWorld terrainWorld;
    ISurvivabilityFactor[] factors;
    Texture2D texture;
    Color32[] pixels;
    float[] scores;
    byte[] cellKinds;
    byte[] limitingFactors;
    double[] factorScoreTotals;
    float coldestHabitable;
    float warmestHabitable;
    bool saveWhenFinished = true;
    int mapResolution;
    float mapExtent;
    int nextCell = -1;
    bool hasMap;
    bool shown;
    float shownOpacity;
    float nextRefreshTime;

    Toggle toggle;
    GameObject checkmark;
    GameObject legend;

    // Added to any scene with terrain, so it needs no scene setup.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AddToScene()
    {
        if (FindAnyObjectByType<SurvivabilityHeatmap>() == null && FindAnyObjectByType<TerrainGenerator>() != null)
        {
            new GameObject("Survivability Heatmap").AddComponent<SurvivabilityHeatmap>();
        }
    }

    void Start()
    {
        BuildToggle();
        SetShown(showOnStart);
        if (SeedManager.Instance != null) SeedManager.Instance.SeedChanged += ForgetMap;
    }

    void OnDestroy()
    {
        if (SeedManager.Instance != null) SeedManager.Instance.SeedChanged -= ForgetMap;
        Shader.SetGlobalFloat(OpacityId, 0f);
        if (texture != null) Destroy(texture);
    }

    void Update()
    {
        shownOpacity = Mathf.MoveTowards(shownOpacity, shown ? opacity : 0f, 3f * Time.unscaledDeltaTime);
        // Until the map texture exists the shader would read Unity's default texture, so it stays hidden.
        Shader.SetGlobalFloat(OpacityId, texture != null ? shownOpacity : 0f);
        if (!shown) return;

        if (nextCell < 0 && Time.unscaledTime >= nextRefreshTime) BeginRecalculation();
        if (nextCell >= 0) ContinueRecalculation();
    }

    void SetShown(bool value)
    {
        shown = value;
        if (toggle != null) toggle.SetIsOnWithoutNotify(value);
        if (checkmark != null) checkmark.SetActive(value);
        if (legend != null) legend.SetActive(value);
        // Nothing stays selected, so Enter cannot flip the checkbox again and the camera keys stay free.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    // A new world makes the old map wrong, so it is cleared and redrawn from scratch.
    void ForgetMap()
    {
        nextCell = -1;
        nextRefreshTime = 0f;
        hasMap = false;
        saveWhenFinished = true;
        if (texture == null) return;

        Array.Clear(pixels, 0, pixels.Length);
        texture.SetPixels32(pixels);
        texture.Apply(false);
    }

    void BeginRecalculation()
    {
        if (terrainWorld == null)
        {
            terrainWorld = AnimalTerrainWorld.Active != null ? AnimalTerrainWorld.Active
                : FindAnyObjectByType<AnimalTerrainWorld>();
        }

        float extent = terrainWorld != null ? terrainWorld.HabitableExtent : 0f;
        if (extent <= 0f)
        {
            // The terrain is not ready yet.
            nextRefreshTime = Time.unscaledTime + 1f;
            return;
        }

        if (texture == null || texture.width != resolution)
        {
            if (texture != null) Destroy(texture);
            texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "Survivability Heatmap",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            pixels = new Color32[resolution * resolution];
            scores = new float[pixels.Length];
            cellKinds = new byte[pixels.Length];
            limitingFactors = new byte[pixels.Length];
            hasMap = false;
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        mapResolution = resolution;
        mapExtent = extent;
        factors = Factors;
        foreach (ISurvivabilityFactor factor in factors) factor.Prepare();
        factorScoreTotals = new double[factors.Length];
        coldestHabitable = float.PositiveInfinity;
        warmestHabitable = float.NegativeInfinity;

        // The map spans the habitable square, centred on the world origin.
        Shader.SetGlobalTexture(HeatmapId, texture);
        Shader.SetGlobalVector(RectId, new Vector4(-extent, -extent, 0.5f / extent, 0.5f / extent));
        nextCell = 0;
    }

    void ContinueRecalculation()
    {
        float deadline = Time.realtimeSinceStartup + millisecondsPerFrame * 0.001f;
        int total = mapResolution * mapResolution;
        float cellSize = 2f * mapExtent / mapResolution;
        while (nextCell < total)
        {
            int x = nextCell % mapResolution;
            int z = nextCell / mapResolution;
            Vector3 position = new Vector3(-mapExtent + (x + 0.5f) * cellSize, 0f, -mapExtent + (z + 0.5f) * cellSize);
            Evaluate(nextCell++, position);
            if ((nextCell & 31) == 0 && Time.realtimeSinceStartup > deadline) break;
        }

        // The first map fills in as it goes; later ones replace the old map once complete.
        bool finished = nextCell >= total;
        if (finished || !hasMap)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        if (finished)
        {
            hasMap = true;
            nextCell = -1;
            nextRefreshTime = Time.unscaledTime + refreshInterval;
            if (saveMaps && saveWhenFinished) SaveMap();
            saveWhenFinished = false;
        }
    }

    void Evaluate(int index, Vector3 position)
    {
        pixels[index] = Unpainted;
        if (!terrainWorld.TryGetSample(position, out EnvironmentSample environment) || !environment.isValid)
        {
            cellKinds[index] = OutsideCell;
            return;
        }

        if (!terrainWorld.IsWalkable(environment))
        {
            cellKinds[index] = !environment.isLand || environment.isWater ? WaterCell : BlockedCell;
            return;
        }

        SurvivabilityCell cell = new SurvivabilityCell { environment = environment, celsius = Celsius(environment) };
        float score = Combine(cell, out int limiting);
        cellKinds[index] = HabitableCell;
        scores[index] = score;
        limitingFactors[index] = limiting < 0 ? NoLimit : (byte)limiting;
        pixels[index] = ColourFor(score);
        coldestHabitable = Mathf.Min(coldestHabitable, cell.celsius);
        warmestHabitable = Mathf.Max(warmestHabitable, cell.celsius);
    }

    // Weighted geometric mean of the factor scores. The limiting factor is the one scoring lowest, or -1 when
    // every factor gives full marks.
    float Combine(in SurvivabilityCell cell, out int limiting)
    {
        float logSum = 0f;
        float weightSum = 0f;
        float lowest = 0.999f;
        limiting = -1;
        for (int index = 0; index < factors.Length; index++)
        {
            float weight = factors[index].Weight;
            if (weight <= 0f) continue;

            float score = factors[index].Score(cell);
            factorScoreTotals[index] += score;
            if (score < lowest)
            {
                lowest = score;
                limiting = index;
            }

            logSum += weight * Mathf.Log(Mathf.Max(0.001f, score));
            weightSum += weight;
        }

        return weightSum > 0f ? Mathf.Exp(logSum / weightSum) : 1f;
    }

    // Writes the map as a top-down picture (north up, four pixels per cell; water blue, unusable shore and
    // cliffs grey) and a text summary, for reports.
    void SaveMap()
    {
        try
        {
            string folder = Path.Combine(Application.persistentDataPath, "Heatmaps");
            Directory.CreateDirectory(folder);

            const int scale = 4;
            int size = mapResolution * scale;
            Color32[] picture = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int cell = y / scale * mapResolution + x / scale;
                    byte kind = cellKinds[cell];
                    picture[y * size + x] = kind == HabitableCell ? pixels[cell]
                        : kind == WaterCell ? SavedWaterColour
                        : kind == BlockedCell ? SavedBlockedColour : SavedOutsideColour;
                }
            }

            Texture2D image = new Texture2D(size, size, TextureFormat.RGBA32, false);
            image.SetPixels32(picture);
            image.Apply(false);
            File.WriteAllBytes(Path.Combine(folder, "survivability-map.png"), image.EncodeToPNG());
            Destroy(image);

            File.WriteAllText(Path.Combine(folder, "survivability-stats.txt"), DescribeMap());
            Debug.Log($"Saved the survivability map and its statistics to {folder}");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Could not save the survivability map: {exception.Message}");
        }
    }

    string DescribeMap()
    {
        int habitable = 0, water = 0, blocked = 0, unlimited = 0;
        int[] bands = new int[5];
        int[] limitedBy = new int[factors.Length];
        double scoreTotal = 0.0;
        for (int index = 0; index < cellKinds.Length; index++)
        {
            byte kind = cellKinds[index];
            if (kind == WaterCell) water++;
            else if (kind == BlockedCell) blocked++;
            if (kind != HabitableCell) continue;

            habitable++;
            scoreTotal += scores[index];
            bands[Mathf.Min(4, (int)(scores[index] * 5f))]++;
            if (limitingFactors[index] == NoLimit) unlimited++;
            else limitedBy[limitingFactors[index]]++;
        }

        float cellSize = 2f * mapExtent / mapResolution;
        float cellSquareKilometres = cellSize * cellSize / 1e6f;
        string seed = SeedManager.Instance != null ? SeedManager.Instance.SeedString : "unknown";
        StringBuilder text = new StringBuilder();
        text.AppendLine($"Survivability map, world seed {seed}, saved {DateTime.Now:yyyy-MM-dd HH:mm}");
        text.AppendLine($"{mapResolution} x {mapResolution} cells of {cellSize:0.#} m over the {2f * mapExtent / 1000f:0.##} km " +
                        "habitable square; north (+Z) is up in the picture");
        text.AppendLine($"Habitable land: {habitable} cells, {habitable * cellSquareKilometres:0.##} km2. Water: {water} cells. " +
                        $"Shore and cliffs animals cannot use: {blocked} cells.");
        if (habitable == 0) return text.ToString();

        string[] bandNames =
        {
            "0.0-0.2 harsh (red)", "0.2-0.4 (orange)", "0.4-0.6 (yellow)", "0.6-0.8 (light green)", "0.8-1.0 lenient (green)"
        };
        text.AppendLine("Share of habitable land by score:");
        for (int band = 0; band < bands.Length; band++)
        {
            text.AppendLine($"  {bandNames[band]}: {100.0 * bands[band] / habitable:0.0}%");
        }

        text.AppendLine($"Average score: {scoreTotal / habitable:0.00}");
        for (int index = 0; index < factors.Length; index++)
        {
            if (factors[index].Weight <= 0f) continue;
            text.AppendLine($"{factors[index].Name}: average score {factorScoreTotals[index] / habitable:0.00}, " +
                            $"the limiting factor on {100.0 * limitedBy[index] / habitable:0.0}% of habitable land");
        }

        text.AppendLine($"No limiting factor (every score full): {100.0 * unlimited / habitable:0.0}% of habitable land");
        text.AppendLine($"Temperature on habitable land: {coldestHabitable:0.#} to {warmestHabitable:0.#} C");
        text.AppendLine(temperature.Describe());
        text.AppendLine($"Food counts as plentiful at {food.plentifulNutritionPerMinute:0.#} nutrition per minute per plant cell");
        return text.ToString();
    }

    // The temperature an animal would feel here. The terrain provider only answers for loaded terrain, so its
    // conversion is applied to the sample directly; an override or another provider goes through the system.
    static float Celsius(in EnvironmentSample environment)
    {
        TemperatureSystem system = TemperatureSystem.Active;
        if (system != null && !system.useGlobalOverride && system.provider is ProceduralTerrainTemperatureProvider terrain)
        {
            return ProceduralTerrainTemperatureProvider.NormalizedToCelsius(environment.temperature,
                terrain.coldestTemperatureCelsius, terrain.warmestTemperatureCelsius);
        }

        TemperatureSystem.TryGetTemperatureAt(environment.position, out float celsius);
        return celsius;
    }

    static Color32 ColourFor(float score)
    {
        float along = Mathf.Clamp01(score) * (Ramp.Length - 1);
        int index = Mathf.Min(Mathf.FloorToInt(along), Ramp.Length - 2);
        return Color.Lerp(Ramp[index], Ramp[index + 1], along - index);
    }

    // A small [x] Heatmap checkbox in the bottom-left corner, with a harsh-to-lenient key beside it while shown.
    void BuildToggle()
    {
        RectTransform root = WorldSeedPanel.CreateRect("Heatmap Toggle", transform);
        Canvas canvas = root.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 |
                                           AdditionalCanvasShaderChannels.Normal |
                                           AdditionalCanvasShaderChannels.Tangent;
        CanvasScaler scaler = root.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.gameObject.AddComponent<GraphicRaycaster>();

        Image pill = CreateCard("Heatmap", root, 20f, 116f);
        pill.color = Color.white;
        pill.raycastTarget = true;

        Image box = WorldSeedPanel.CreateImage("Box", pill.rectTransform, WorldSeedPanel.RoundedSprite(4f, 1.5f), BoxColor);
        WorldSeedPanel.PlaceLeft(box.rectTransform, 12f, 16f, 16f);
        Image fill = WorldSeedPanel.CreateImage("Checked", box.rectTransform, WorldSeedPanel.RoundedSprite(4f), AccentColor);
        WorldSeedPanel.Stretch(fill.rectTransform, 0f, 0f, 0f, 0f);
        Image tick = WorldSeedPanel.CreateImage("Tick", fill.rectTransform, TickSprite(), OnAccentColor);
        WorldSeedPanel.PlaceCentre(tick.rectTransform, 12f, 12f);
        checkmark = fill.gameObject;

        TextMeshProUGUI label = CreateLabel("Heatmap", pill.rectTransform, 14f, TextColor, true, TextAlignmentOptions.Left);
        WorldSeedPanel.PlaceLeft(label.rectTransform, 36f, 72f, 20f);

        toggle = pill.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = pill;
        toggle.transition = Selectable.Transition.ColorTint;
        ColorBlock tints = ColorBlock.defaultColorBlock;
        tints.normalColor = PillColor;
        tints.highlightedColor = PillHoverColor;
        tints.pressedColor = PillHoverColor;
        tints.selectedColor = PillColor;
        tints.fadeDuration = 0.08f;
        toggle.colors = tints;
        toggle.navigation = new Navigation { mode = Navigation.Mode.None };
        toggle.isOn = false;
        toggle.onValueChanged.AddListener(SetShown);

        // Key: Harsh [red to green] Lenient.
        Image key = CreateCard("Key", root, 20f + 116f + 8f, 214f);
        key.color = PillColor;
        TextMeshProUGUI harsh = CreateLabel("Harsh", key.rectTransform, 12f, MutedTextColor, false, TextAlignmentOptions.Left);
        WorldSeedPanel.PlaceLeft(harsh.rectTransform, 12f, 40f, 20f);
        RawImage bar = WorldSeedPanel.CreateRect("Scale", key.rectTransform).gameObject.AddComponent<RawImage>();
        bar.texture = RampTexture();
        bar.raycastTarget = false;
        WorldSeedPanel.PlaceLeft(bar.rectTransform, 56f, 96f, 8f);
        TextMeshProUGUI lenient = CreateLabel("Lenient", key.rectTransform, 12f, MutedTextColor, false, TextAlignmentOptions.Left);
        WorldSeedPanel.PlaceLeft(lenient.rectTransform, 160f, 48f, 20f);
        legend = key.gameObject;
    }

    // A dark rounded card with a faint outline, pinned a distance from the bottom-left corner.
    internal static Image CreateCard(string objectName, RectTransform parent, float left, float width,
                                     float bottom = 20f, float height = 36f)
    {
        Image card = WorldSeedPanel.CreateImage(objectName, parent, WorldSeedPanel.RoundedSprite(10f), PillColor);
        RectTransform rect = card.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(left, bottom);
        rect.sizeDelta = new Vector2(width, height);

        Image outline = WorldSeedPanel.CreateImage("Outline", rect, WorldSeedPanel.RoundedSprite(10f, 1f), OutlineColor);
        WorldSeedPanel.Stretch(outline.rectTransform, 0f, 0f, 0f, 0f);
        return card;
    }

    internal static TextMeshProUGUI CreateLabel(string content, Transform parent, float size, Color color, bool strong,
                                                TextAlignmentOptions alignment)
    {
        TextMeshProUGUI label = WorldSeedPanel.CreateRect(content, parent).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = WorldSeedPanel.UiFont(strong);
        if (strong && label.font == WorldSeedPanel.UiFont(false)) label.fontStyle = FontStyles.Bold;
        label.fontSize = size;
        label.color = color;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.richText = false;
        label.raycastTarget = false;
        label.text = content;
        return label;
    }

    static Texture2D RampTexture()
    {
        const int width = 64;
        Texture2D ramp = new Texture2D(width, 1, TextureFormat.RGBA32, false)
        {
            name = "Survivability Scale",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        Color32[] colours = new Color32[width];
        for (int x = 0; x < width; x++) colours[x] = ColourFor(x / (width - 1f));
        ramp.SetPixels32(colours);
        ramp.Apply(false, true);
        return ramp;
    }

    internal static Sprite TickSprite()
    {
        if (tickSprite != null) return tickSprite;

        // Drawn at two texels per canvas unit, like the seed panel's sprites.
        Vector2 start = new Vector2(5.5f, 12.5f);
        Vector2 corner = new Vector2(10f, 8f);
        Vector2 end = new Vector2(18.5f, 16.5f);
        tickSprite = WorldSeedPanel.Paint("Tick", 24, 24, 0f, point =>
            2.1f - Mathf.Min(WorldSeedPanel.SegmentDistance(point, start, corner),
                             WorldSeedPanel.SegmentDistance(point, corner, end)));
        return tickSprite;
    }
}
