using System;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The sea-biome rules for the current world, ready for background threads: the rules plus what they need to know
// about the world (its water level, its seed, and how its normalised temperatures map to degrees).
public readonly struct SeaBiomeClassifier
{
    // Rough size, in world units, of reef patches and of patches of rock and sand.
    const float ReefPatchSize = 140f;
    const float SubstratePatchSize = 60f;

    public readonly SeaBiomeRules rules;
    readonly EnvironmentDefinitions definitions;
    readonly float waterLevel;
    readonly long reefSeed;
    readonly long substrateSeed;
    readonly float coldestCelsius;
    readonly float warmestCelsius;

    public SeaBiomeClassifier(SeaBiomeRules rules, EnvironmentDefinitions definitions, float waterLevel, int worldSeed,
                              float coldestCelsius, float warmestCelsius)
    {
        this.rules = rules;
        this.definitions = definitions;
        this.waterLevel = waterLevel;
        reefSeed = worldSeed + 7919L;
        substrateSeed = worldSeed + 104729L;
        this.coldestCelsius = coldestCelsius;
        this.warmestCelsius = warmestCelsius;
    }

    public bool IsValid => definitions != null && !float.IsInfinity(waterLevel);

    public SeaBiome Classify(in EnvironmentSample sample) => Classify(sample, out _, out _);

    // Also gives the depth and the surface temperature the biome was judged by.
    public SeaBiome Classify(in EnvironmentSample sample, out float depth, out float surfaceCelsius)
    {
        depth = 0f;
        surfaceCelsius = 0f;
        if (!IsValid || !sample.isValid || !sample.isWater) return SeaBiome.None;

        Vector2 position = new Vector2(sample.position.x, sample.position.z);
        depth = Mathf.Max(0f, waterLevel - sample.position.y);
        surfaceCelsius = SurfaceCelsius(position);
        return rules.Classify(depth, sample.slopeDegrees, sample.lakeStrength, surfaceCelsius,
                              Patch(reefSeed, position, ReefPatchSize),
                              Patch(substrateSeed, position, SubstratePatchSize));
    }

    // Same as Classify(sample) == SeaBiome.DeadZone, without working out the other biomes.
    public bool IsDeadZone(in EnvironmentSample sample)
    {
        return IsValid && sample.isValid && sample.isWater &&
               rules.IsDeadZone(Mathf.Max(0f, waterLevel - sample.position.y), sample.lakeStrength);
    }

    // The temperature at the water surface, which is what an animal in the water feels.
    public float SurfaceCelsius(Vector2 position)
    {
        float normalized = definitions.SampleTemperature(position, definitions.ShorelineThreshold);
        return ProceduralTerrainTemperatureProvider.NormalizedToCelsius(normalized, coldestCelsius, warmestCelsius);
    }

    static float Patch(long seed, Vector2 position, float size)
    {
        return 0.5f + 0.5f * OpenSimplex2.Noise2(seed, position.x / size, position.y / size);
    }
}

// The sea's biomes across the whole habitable square, drawn in the Sea map panel (the checkbox above the heatmap's,
// bottom left) and saved as a top-down picture with statistics, next to the survivability heatmap. The map is drawn
// while the panel is open, once per world and again whenever the rules change. Its rules are the ones navigation
// uses, so the map shows the sea the animals live with.
[DisallowMultipleComponent]
public class SeaBiomeMap : MonoBehaviour
{
    public static SeaBiomeMap Active { get; private set; }

    // The rules in use, or the defaults in a scene without a sea map.
    public static SeaBiomeRules CurrentRules => Active != null ? Active.rules : SeaBiomeRules.Default;

    // ProceduralTerrainTemperatureProvider's defaults, for a scene that doesn't use it.
    const float DefaultColdestCelsius = -28f;
    const float DefaultWarmestCelsius = 18f;

    const byte OutsideCell = 255;
    const int BiomeCount = 6;
    // The map is worked out in this many strips at once, each on its own thread.
    const int MapStrips = 4;

    // Panel layout, in canvas units.
    const float ToggleBottom = 64f;
    const float PanelBottom = 108f;
    const float MapSize = 320f;
    const float Padding = 12f;
    const float HeaderHeight = 48f;
    const float LegendRowHeight = 18f;
    const int LegendColumns = 2;

    // Indexed by SeaBiome; None is land.
    static readonly Color32[] BiomeColours =
    {
        new Color32(214, 206, 178, 255), // land
        new Color32(124, 186, 104, 255), // seagrass meadow
        new Color32(146, 116, 54, 255),  // kelp forest
        new Color32(238, 112, 132, 255), // cold-water reef
        new Color32(54, 106, 166, 255),  // open sea
        new Color32(48, 40, 58, 255),    // dead zone
    };
    // The key lists the sea first, land last.
    static readonly SeaBiome[] LegendOrder =
    {
        SeaBiome.SeagrassMeadow, SeaBiome.KelpForest, SeaBiome.ColdWaterReef, SeaBiome.OpenSea, SeaBiome.DeadZone,
        SeaBiome.None
    };
    static readonly Color32 Unpainted = new Color32(0, 0, 0, 0);
    static readonly Color32 SavedOutsideColour = new Color32(255, 255, 255, 255);

    [Tooltip("How the sea divides into biomes. Changes apply to areas and maps built afterwards.")]
    public SeaBiomeRules rules = SeaBiomeRules.Default;

    [Header("Map")]
    [Tooltip("Show the Sea map panel as soon as the scene starts.")]
    public bool showOnStart;
    [Tooltip("Width of one map cell in world units.")]
    [Min(4f)] public float cellSize = 16f;
    [Tooltip("Save each completed map (one per world, and again after the rules change) as a top-down PNG, " +
             "with statistics, in the Heatmaps folder under Application.persistentDataPath.")]
    public bool saveMaps = true;

    AnimalTerrainWorld terrainWorld;
    Texture2D texture;
    byte[] cells;
    SeaBiomeStatistics statistics;
    SeaBiomeRules mappedRules;
    int mappedSeed;
    int mapResolution;
    float mapExtent;
    int mapVersion;
    int stripsPending;
    bool mappingFailed;
    bool hasMap;
    bool shown;
    float nextAttemptTime;
    float nextReadoutTime;

    Toggle toggle;
    GameObject checkmark;
    GameObject panel;
    RawImage mapImage;
    RectTransform cameraMarker;
    TextMeshProUGUI status;
    readonly TextMeshProUGUI[] legendLabels = new TextMeshProUGUI[BiomeCount];

    // Added to any scene with terrain, so it needs no scene setup.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AddToScene()
    {
        if (FindAnyObjectByType<SeaBiomeMap>() == null && FindAnyObjectByType<TerrainGenerator>() != null)
        {
            new GameObject("Sea Biome Map").AddComponent<SeaBiomeMap>();
        }
    }

    // The classifier for the current world. Needs the terrain and its water level (set by HabitatNavigation);
    // main thread only.
    public static bool TryCreateClassifier(AnimalTerrainWorld world, out SeaBiomeClassifier classifier)
    {
        classifier = default;
        TerrainEnvironmentSampler sampler = world != null ? world.Sampler : null;
        if (sampler == null || !sampler.IsConfigured || sampler.EnvironmentDefinitions == null ||
            sampler.HeightMapSettings == null || !WaterAccess.HasWater)
        {
            return false;
        }

        float coldest = DefaultColdestCelsius;
        float warmest = DefaultWarmestCelsius;
        TemperatureSystem system = TemperatureSystem.Active;
        if (system != null && system.provider is ProceduralTerrainTemperatureProvider provider)
        {
            coldest = provider.coldestTemperatureCelsius;
            warmest = provider.warmestTemperatureCelsius;
        }

        classifier = new SeaBiomeClassifier(CurrentRules, sampler.EnvironmentDefinitions, WaterAccess.SurfaceHeight,
                                            sampler.HeightMapSettings.noiseSettings.seed, coldest, warmest);
        return true;
    }

    void Awake()
    {
        Active = this;
    }

    void Start()
    {
        BuildPanel();
        SetShown(showOnStart);
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
        if (texture != null) Destroy(texture);
    }

    void Update()
    {
        if (!shown) return;

        // A new world has a new terrain seed, and makes the old map wrong.
        if ((hasMap || stripsPending > 0) && TerrainSeed() != mappedSeed) ForgetMap();

        bool mapping = stripsPending > 0;
        bool stale = hasMap && !mappedRules.Equals(rules);
        if (!mapping && (!hasMap || stale) && Time.unscaledTime >= nextAttemptTime) BeginMapping();

        UpdateCameraMarker();
        if (Time.unscaledTime >= nextReadoutTime)
        {
            nextReadoutTime = Time.unscaledTime + 0.25f;
            UpdateStatus();
        }
    }

    void SetShown(bool value)
    {
        shown = value;
        if (toggle != null) toggle.SetIsOnWithoutNotify(value);
        if (checkmark != null) checkmark.SetActive(value);
        if (panel != null) panel.SetActive(value);
        // Nothing stays selected, so Enter cannot flip the checkbox again and the camera keys stay free.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    int TerrainSeed()
    {
        TerrainEnvironmentSampler sampler = terrainWorld != null ? terrainWorld.Sampler : null;
        return sampler != null && sampler.HeightMapSettings != null ? sampler.HeightMapSettings.noiseSettings.seed : 0;
    }

    // Clears the map so it is drawn again, for example for a new world.
    void ForgetMap()
    {
        mapVersion++;
        stripsPending = 0;
        hasMap = false;
        nextAttemptTime = 0f;
        if (texture == null) return;

        texture.SetPixels32(new Color32[texture.width * texture.height]);
        texture.Apply(false);
    }

    void BeginMapping()
    {
        nextAttemptTime = Time.unscaledTime + 1f;
        if (terrainWorld == null)
        {
            terrainWorld = AnimalTerrainWorld.Active != null ? AnimalTerrainWorld.Active
                : FindAnyObjectByType<AnimalTerrainWorld>();
        }

        // Until the terrain and its water are ready there is nothing to map; this is tried again shortly.
        float extent = terrainWorld != null ? terrainWorld.HabitableExtent : 0f;
        if (extent <= 0f || !TryCreateClassifier(terrainWorld, out SeaBiomeClassifier classifier)) return;

        int resolution = Mathf.Clamp(Mathf.RoundToInt(2f * extent / Mathf.Max(4f, cellSize)), 32, 1024);
        if (texture == null || texture.width != resolution)
        {
            if (texture != null) Destroy(texture);
            texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "Sea Biome Map",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            texture.SetPixels32(new Color32[resolution * resolution]);
            texture.Apply(false);
            mapImage.texture = texture;
        }

        cells = new byte[resolution * resolution];
        statistics = new SeaBiomeStatistics();
        mapResolution = resolution;
        mapExtent = extent;
        mappedRules = rules;
        mappedSeed = TerrainSeed();
        mappingFailed = false;
        int version = ++mapVersion;
        TerrainEnvironmentSampler sampler = terrainWorld.Sampler;
        int rowsPerStrip = Mathf.CeilToInt(resolution / (float)MapStrips);
        stripsPending = MapStrips;
        for (int strip = 0; strip < MapStrips; strip++)
        {
            int first = Mathf.Min(resolution, strip * rowsPerStrip);
            int end = Mathf.Min(resolution, first + rowsPerStrip);
            ThreadedDataRequester.RequestData(
                () => MapRows(version, sampler, classifier, extent, resolution, first, end), OnRowsMapped);
        }
    }

    sealed class MappedRows
    {
        public int version;
        public int first;
        public byte[] cells;
        public SeaBiomeStatistics statistics;
        public string error;
    }

    // Runs on a background thread. Rows first to end of the map, each cell judged at its centre.
    static MappedRows MapRows(int version, TerrainEnvironmentSampler sampler, SeaBiomeClassifier classifier,
                              float extent, int resolution, int first, int end)
    {
        MappedRows rows = new MappedRows
        {
            version = version,
            first = first,
            cells = new byte[(end - first) * resolution],
            statistics = new SeaBiomeStatistics()
        };

        // A job that throws never reports back, so failures are caught and reported instead.
        try
        {
            float size = 2f * extent / resolution;
            for (int z = first; z < end; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector2 position = new Vector2(-extent + (x + 0.5f) * size, -extent + (z + 0.5f) * size);
                    byte kind = OutsideCell;
                    if (sampler.TrySample(position, out EnvironmentSample sample) && sample.isValid)
                    {
                        SeaBiome biome = classifier.Classify(sample, out float depth, out float celsius);
                        kind = (byte)biome;
                        if (biome == SeaBiome.None) rows.statistics.landCells++;
                        else rows.statistics.Add(biome, depth, celsius, sample.slopeDegrees,
                                                 sample.lakeStrength >= classifier.rules.deadZoneBasin);
                    }

                    rows.cells[(z - first) * resolution + x] = kind;
                }
            }
        }
        catch (Exception exception)
        {
            rows.error = exception.Message;
        }

        return rows;
    }

    void OnRowsMapped(object result)
    {
        MappedRows rows = (MappedRows)result;
        // Rows of a world or rules that have since changed.
        if (rows.version != mapVersion || stripsPending == 0) return;

        stripsPending--;
        if (rows.error != null)
        {
            mappingFailed = true;
            Debug.LogWarning($"Could not map part of the sea: {rows.error}");
        }
        else
        {
            Array.Copy(rows.cells, 0, cells, rows.first * mapResolution, rows.cells.Length);
            statistics.Merge(rows.statistics);
        }

        if (stripsPending > 0) return;

        Color32[] pixels = new Color32[cells.Length];
        for (int index = 0; index < cells.Length; index++)
        {
            pixels[index] = cells[index] == OutsideCell ? Unpainted : BiomeColours[cells[index]];
        }

        texture.SetPixels32(pixels);
        texture.Apply(false);
        hasMap = true;
        UpdateLegend();
        if (!mappingFailed && saveMaps) SaveMap(pixels);
    }

    void UpdateLegend()
    {
        int water = statistics.WaterCells;
        for (int entry = 0; entry < LegendOrder.Length; entry++)
        {
            SeaBiome biome = LegendOrder[entry];
            string share = biome == SeaBiome.None || water == 0
                ? ""
                : $"  {100f * statistics.cells[(int)biome] / water:0}%";
            legendLabels[entry].text = SeaBiomeRules.Name(biome) + share;
        }
    }

    void UpdateCameraMarker()
    {
        Camera view = Camera.main;
        bool visible = view != null && hasMap && mapExtent > 0f;
        if (visible)
        {
            Vector3 position = view.transform.position;
            float across = (position.x + mapExtent) / (2f * mapExtent);
            float up = (position.z + mapExtent) / (2f * mapExtent);
            visible = across >= 0f && across <= 1f && up >= 0f && up <= 1f;
            if (visible) cameraMarker.anchoredPosition = new Vector2(across * MapSize, up * MapSize);
        }

        if (cameraMarker.gameObject.activeSelf != visible) cameraMarker.gameObject.SetActive(visible);
    }

    // Mapping progress, then what lies under the camera.
    void UpdateStatus()
    {
        if (stripsPending > 0)
        {
            status.text = $"Mapping the sea... {MapStrips - stripsPending} of {MapStrips} parts done";
            return;
        }

        if (mappingFailed)
        {
            status.text = "Part of the map failed; see the Console";
            return;
        }

        Camera view = Camera.main;
        if (!hasMap || view == null || terrainWorld == null ||
            !TryCreateClassifier(terrainWorld, out SeaBiomeClassifier classifier))
        {
            status.text = "Waiting for the terrain...";
            return;
        }

        if (!terrainWorld.TryGetSample(view.transform.position, out EnvironmentSample sample) || !sample.isValid)
        {
            status.text = "Under the camera: outside the habitat";
            return;
        }

        SeaBiome biome = classifier.Classify(sample, out float depth, out float celsius);
        status.text = biome == SeaBiome.None
            ? "Under the camera: land"
            : $"Under the camera: {SeaBiomeRules.Name(biome)}, {depth:0.#} m deep, {celsius:0.#} C";
    }

    // Writes the map as a top-down picture (north up, two pixels per cell) and a text summary, beside the
    // survivability heatmap's. Each new map replaces the last.
    void SaveMap(Color32[] pixels)
    {
        try
        {
            string folder = Path.Combine(Application.persistentDataPath, "Heatmaps");
            Directory.CreateDirectory(folder);

            const int scale = 2;
            int size = mapResolution * scale;
            Color32[] picture = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int cell = y / scale * mapResolution + x / scale;
                    picture[y * size + x] = cells[cell] == OutsideCell ? SavedOutsideColour : pixels[cell];
                }
            }

            Texture2D image = new Texture2D(size, size, TextureFormat.RGBA32, false);
            image.SetPixels32(picture);
            image.Apply(false);
            File.WriteAllBytes(Path.Combine(folder, "sea-biome-map.png"), image.EncodeToPNG());
            Destroy(image);

            File.WriteAllText(Path.Combine(folder, "sea-biome-stats.txt"), DescribeMap());
            Debug.Log($"Saved the sea biome map and its statistics to {folder}");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Could not save the sea biome map: {exception.Message}");
        }
    }

    string DescribeMap()
    {
        float size = 2f * mapExtent / mapResolution;
        float cellSquareKilometres = size * size / 1e6f;
        int water = statistics.WaterCells;
        int total = mapResolution * mapResolution;
        string seed = SeedManager.Instance != null ? SeedManager.Instance.SeedString : "unknown";
        StringBuilder text = new StringBuilder();
        text.AppendLine($"Sea biome map, world seed {seed}, saved {DateTime.Now:yyyy-MM-dd HH:mm}");
        text.AppendLine($"{mapResolution} x {mapResolution} cells of {size:0.#} m over the {2f * mapExtent / 1000f:0.##} km " +
                        "habitable square; north (+Z) is up in the picture");
        text.AppendLine($"Water: {water} cells, {water * cellSquareKilometres:0.##} km2, {100.0 * water / total:0.0}% of the " +
                        $"square. Land: {statistics.landCells} cells.");
        if (water == 0) return text.ToString();

        text.AppendLine("Share of the sea by biome (depth range and average, surface temperature range):");
        for (int biome = 1; biome < BiomeCount; biome++)
        {
            int count = statistics.cells[biome];
            string detail = count == 0
                ? "none"
                : $"{100.0 * count / water:0.0}%, {statistics.shallowest[biome]:0.#}-{statistics.deepest[biome]:0.#} m " +
                  $"(average {statistics.depthTotals[biome] / count:0.#} m), " +
                  $"{statistics.coldest[biome]:0.#} to {statistics.warmest[biome]:0.#} C";
            text.AppendLine($"  {SeaBiomeRules.Name((SeaBiome)biome)}: {detail}");
        }

        text.AppendLine("Depth of the sea: " + DescribeBands(statistics.depthHistogram, SeaBiomeStatistics.DepthBands,
                                                             "m", water));
        text.AppendLine("Slope of the sea bed: " + DescribeBands(statistics.slopeHistogram,
                                                                 SeaBiomeStatistics.SlopeBands, "deg", water));
        text.AppendLine($"Surface temperature of the sea: {statistics.coldestWater:0.#} to {statistics.warmestWater:0.#} C");
        text.AppendLine($"Carved basins (strength {mappedRules.deadZoneBasin:0.##} or more): " +
                        $"{100.0 * statistics.basinCells / water:0.0}% of the sea" +
                        (statistics.basinCells > 0
                            ? $", {statistics.shallowestBasin:0.#}-{statistics.deepestBasin:0.#} m deep"
                            : ""));
        SeaBiomeRules used = mappedRules;
        text.AppendLine($"Rules: seagrass on sand to {used.seagrassMaxDepth:0.#} m; kelp on rock to {used.kelpMaxDepth:0.#} m; " +
                        $"reefs on rock {used.reefMinDepth:0.#}-{used.reefMaxDepth:0.#} m from {used.reefMinTemperature:0.#} C " +
                        $"(coverage {used.reefCoverage:0.##}); rock from {used.rockySlope:0.#} deg " +
                        $"(+/- {used.rockySlopeJitter:0.#}); dead zones in basins from {used.deadZoneMinDepth:0.#} m, " +
                        $"energy x{used.deadZoneEnergyCost:0.#}");
        return text.ToString();
    }

    static string DescribeBands(int[] counts, float[] edges, string unit, int total)
    {
        StringBuilder text = new StringBuilder();
        for (int band = 0; band < counts.Length; band++)
        {
            if (text.Length > 0) text.Append(", ");
            string range = band == 0 ? $"under {edges[0]:0.#} {unit}"
                : band == edges.Length ? $"{edges[band - 1]:0.#} {unit} or more"
                : $"{edges[band - 1]:0.#}-{edges[band]:0.#} {unit}";
            text.Append($"{range} {100.0 * counts[band] / total:0.0}%");
        }

        return text.ToString();
    }

    // A [x] Sea map checkbox above the heatmap's, with the map panel above it while shown.
    void BuildPanel()
    {
        RectTransform root = WorldSeedPanel.CreateRect("Sea Map", transform);
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

        Image pill = SurvivabilityHeatmap.CreateCard("Sea Map Toggle", root, 20f, 116f, ToggleBottom);
        pill.color = Color.white;
        pill.raycastTarget = true;

        Image box = WorldSeedPanel.CreateImage("Box", pill.rectTransform, WorldSeedPanel.RoundedSprite(4f, 1.5f),
                                               SurvivabilityHeatmap.BoxColor);
        WorldSeedPanel.PlaceLeft(box.rectTransform, 12f, 16f, 16f);
        Image fill = WorldSeedPanel.CreateImage("Checked", box.rectTransform, WorldSeedPanel.RoundedSprite(4f),
                                                SurvivabilityHeatmap.AccentColor);
        WorldSeedPanel.Stretch(fill.rectTransform, 0f, 0f, 0f, 0f);
        Image tick = WorldSeedPanel.CreateImage("Tick", fill.rectTransform, SurvivabilityHeatmap.TickSprite(),
                                                SurvivabilityHeatmap.OnAccentColor);
        WorldSeedPanel.PlaceCentre(tick.rectTransform, 12f, 12f);
        checkmark = fill.gameObject;

        TextMeshProUGUI label = SurvivabilityHeatmap.CreateLabel("Sea map", pill.rectTransform, 14f,
            SurvivabilityHeatmap.TextColor, true, TextAlignmentOptions.Left);
        WorldSeedPanel.PlaceLeft(label.rectTransform, 36f, 72f, 20f);

        toggle = pill.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = pill;
        toggle.transition = Selectable.Transition.ColorTint;
        ColorBlock tints = ColorBlock.defaultColorBlock;
        tints.normalColor = SurvivabilityHeatmap.PillColor;
        tints.highlightedColor = SurvivabilityHeatmap.PillHoverColor;
        tints.pressedColor = SurvivabilityHeatmap.PillHoverColor;
        tints.selectedColor = SurvivabilityHeatmap.PillColor;
        tints.fadeDuration = 0.08f;
        toggle.colors = tints;
        toggle.navigation = new Navigation { mode = Navigation.Mode.None };
        toggle.isOn = false;
        toggle.onValueChanged.AddListener(SetShown);

        // The panel: a title and a readout, the map with the camera's position, and a key with each biome's share
        // of the sea.
        int legendRows = Mathf.CeilToInt(LegendOrder.Length / (float)LegendColumns);
        float width = MapSize + 2f * Padding;
        float height = HeaderHeight + MapSize + Padding + legendRows * LegendRowHeight + Padding;
        Image card = SurvivabilityHeatmap.CreateCard("Sea Map Panel", root, 20f, width, PanelBottom, height);
        card.color = SurvivabilityHeatmap.PillColor;
        panel = card.gameObject;

        TextMeshProUGUI title = SurvivabilityHeatmap.CreateLabel("Sea biomes", card.rectTransform, 14f,
            SurvivabilityHeatmap.TextColor, true, TextAlignmentOptions.Left);
        PlaceTopLeft(title.rectTransform, Padding, 10f, MapSize, 18f);
        status = SurvivabilityHeatmap.CreateLabel("", card.rectTransform, 12f, SurvivabilityHeatmap.MutedTextColor,
                                                  false, TextAlignmentOptions.Left);
        PlaceTopLeft(status.rectTransform, Padding, 28f, MapSize, 16f);

        mapImage = WorldSeedPanel.CreateRect("Map", card.rectTransform).gameObject.AddComponent<RawImage>();
        mapImage.raycastTarget = false;
        PlaceTopLeft(mapImage.rectTransform, Padding, HeaderHeight, MapSize, MapSize);

        // A white dot with a dark ring, which shows on both the pale land and the dark sea.
        cameraMarker = WorldSeedPanel.CreateImage("Camera", mapImage.rectTransform, WorldSeedPanel.RoundedSprite(5f),
                                                  Color.white).rectTransform;
        cameraMarker.anchorMin = cameraMarker.anchorMax = Vector2.zero;
        cameraMarker.pivot = new Vector2(0.5f, 0.5f);
        cameraMarker.sizeDelta = new Vector2(10f, 10f);
        Image ring = WorldSeedPanel.CreateImage("Ring", cameraMarker, WorldSeedPanel.RoundedSprite(5f, 2f),
                                                SurvivabilityHeatmap.PillColor);
        WorldSeedPanel.Stretch(ring.rectTransform, 0f, 0f, 0f, 0f);
        cameraMarker.gameObject.SetActive(false);

        float columnWidth = MapSize / LegendColumns;
        for (int entry = 0; entry < LegendOrder.Length; entry++)
        {
            float left = Padding + entry % LegendColumns * columnWidth;
            float top = HeaderHeight + MapSize + Padding + entry / LegendColumns * LegendRowHeight;
            Image swatch = WorldSeedPanel.CreateImage("Swatch", card.rectTransform, WorldSeedPanel.RoundedSprite(2f),
                                                      BiomeColours[(int)LegendOrder[entry]]);
            PlaceTopLeft(swatch.rectTransform, left, top + 4f, 10f, 10f);
            legendLabels[entry] = SurvivabilityHeatmap.CreateLabel(SeaBiomeRules.Name(LegendOrder[entry]),
                card.rectTransform, 12f, SurvivabilityHeatmap.MutedTextColor, false, TextAlignmentOptions.Left);
            PlaceTopLeft(legendLabels[entry].rectTransform, left + 16f, top, columnWidth - 20f, LegendRowHeight);
        }
    }

    // Pins a rect's top-left corner at an offset from its parent's top-left corner.
    static void PlaceTopLeft(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(left, -top);
    }
}

// Counts and ranges per sea biome, gathered while mapping, for the key and the saved statistics.
sealed class SeaBiomeStatistics
{
    // Upper edges of each band; the last band is everything beyond.
    public static readonly float[] DepthBands = { 1f, 3f, 6f, 10f, 15f, 20f, 25f };
    public static readonly float[] SlopeBands = { 2f, 4f, 6f, 8f, 12f, 20f };

    const int BiomeCount = 6;

    public readonly int[] cells = new int[BiomeCount];
    public readonly double[] depthTotals = new double[BiomeCount];
    public readonly float[] shallowest = Filled(float.PositiveInfinity);
    public readonly float[] deepest = Filled(float.NegativeInfinity);
    public readonly float[] coldest = Filled(float.PositiveInfinity);
    public readonly float[] warmest = Filled(float.NegativeInfinity);
    public readonly int[] depthHistogram = new int[DepthBands.Length + 1];
    public readonly int[] slopeHistogram = new int[SlopeBands.Length + 1];
    public int landCells;
    public int basinCells;
    public float shallowestBasin = float.PositiveInfinity;
    public float deepestBasin = float.NegativeInfinity;
    public float coldestWater = float.PositiveInfinity;
    public float warmestWater = float.NegativeInfinity;

    public int WaterCells
    {
        get
        {
            int water = 0;
            for (int biome = 1; biome < BiomeCount; biome++) water += cells[biome];
            return water;
        }
    }

    public void Add(SeaBiome biome, float depth, float celsius, float slopeDegrees, bool inBasin)
    {
        int index = (int)biome;
        cells[index]++;
        depthTotals[index] += depth;
        shallowest[index] = Mathf.Min(shallowest[index], depth);
        deepest[index] = Mathf.Max(deepest[index], depth);
        coldest[index] = Mathf.Min(coldest[index], celsius);
        warmest[index] = Mathf.Max(warmest[index], celsius);
        depthHistogram[Band(depth, DepthBands)]++;
        slopeHistogram[Band(slopeDegrees, SlopeBands)]++;
        coldestWater = Mathf.Min(coldestWater, celsius);
        warmestWater = Mathf.Max(warmestWater, celsius);
        if (!inBasin) return;

        basinCells++;
        shallowestBasin = Mathf.Min(shallowestBasin, depth);
        deepestBasin = Mathf.Max(deepestBasin, depth);
    }

    public void Merge(SeaBiomeStatistics other)
    {
        for (int biome = 0; biome < BiomeCount; biome++)
        {
            cells[biome] += other.cells[biome];
            depthTotals[biome] += other.depthTotals[biome];
            shallowest[biome] = Mathf.Min(shallowest[biome], other.shallowest[biome]);
            deepest[biome] = Mathf.Max(deepest[biome], other.deepest[biome]);
            coldest[biome] = Mathf.Min(coldest[biome], other.coldest[biome]);
            warmest[biome] = Mathf.Max(warmest[biome], other.warmest[biome]);
        }

        for (int band = 0; band < depthHistogram.Length; band++) depthHistogram[band] += other.depthHistogram[band];
        for (int band = 0; band < slopeHistogram.Length; band++) slopeHistogram[band] += other.slopeHistogram[band];
        landCells += other.landCells;
        basinCells += other.basinCells;
        shallowestBasin = Mathf.Min(shallowestBasin, other.shallowestBasin);
        deepestBasin = Mathf.Max(deepestBasin, other.deepestBasin);
        coldestWater = Mathf.Min(coldestWater, other.coldestWater);
        warmestWater = Mathf.Max(warmestWater, other.warmestWater);
    }

    static int Band(float value, float[] edges)
    {
        for (int band = 0; band < edges.Length; band++)
        {
            if (value < edges[band]) return band;
        }

        return edges.Length;
    }

    static float[] Filled(float value)
    {
        float[] values = new float[BiomeCount];
        for (int index = 0; index < values.Length; index++) values[index] = value;
        return values;
    }
}
