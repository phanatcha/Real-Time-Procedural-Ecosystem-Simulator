using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Answers the habitat research question for a list of seeds: how much suitable habitat each world has
// (availability) and how much of it the founders can walk to (accessibility). Every seed uses the same
// terrain settings and animal requirements, so the worlds can be compared. Exports maps and CSV tables.
public sealed class HabitatAnalysisWindow : EditorWindow
{
    const string TerrainSettingsFolder = "Assets/TerrainGeneration/Settings/";
    const string EcosystemSettingsPath = "Assets/EcosystemSimulation/Settings/EcosystemSimulationSettings.asset";
    const float DefaultCellSize = 250f;
    const int ExportMapSize = 2048;

    static readonly Color32 NoDataColor = new Color32(32, 36, 42, 255);
    static readonly Color32 WaterColor = new Color32(91, 127, 166, 255);
    static readonly Color32 BarrierColor = new Color32(140, 134, 126, 255);
    static readonly Color32 PassableColor = new Color32(226, 221, 208, 255);
    static readonly Color32 AccessibleColor = new Color32(46, 125, 79, 255);
    static readonly Color32 IsolatedColor = new Color32(214, 140, 38, 255);
    static readonly Color32 StartOutlineColor = new Color32(255, 255, 255, 255);
    static readonly Color32 StartColor = new Color32(15, 15, 15, 255);

    static readonly (HabitatClass habitatClass, Color32 color, string label)[] Legend =
    {
        (HabitatClass.AccessibleHabitat, AccessibleColor, "Suitable habitat the founders can reach"),
        (HabitatClass.IsolatedHabitat, IsolatedColor, "Suitable habitat cut off from the start"),
        (HabitatClass.Passable, PassableColor, "Walkable, but no plant food or too cold/hot"),
        (HabitatClass.Barrier, BarrierColor, "Land too steep to walk, or shore"),
        (HabitatClass.Water, WaterColor, "Water")
    };

    sealed class SeedResult
    {
        public string seedText;
        public WorldSeeds worldSeeds;
        public HabitatAnalysis analysis;
        public Texture2D map;
    }

    [Serializable]
    sealed class SettingsSnapshot
    {
        public string generatedUtc;
        public string source;
        public string[] seeds;
        public int[] terrainSeeds;
        public WorldSeeds[] worldSeeds;
        public float sampleSpacing;
        public float cellSize;
        public Vector2 requestedStart;
        public HabitatRequirements requirements;
        public string heightSettingsJson;
        public string meshSettingsJson;
        public string environmentDefinitionsJson;
        public string vegetationSettingsJson;
    }

    [SerializeField] HeightMapSettings heightSettings;
    [SerializeField] MeshSettings meshSettings;
    [SerializeField] EnvironmentDefinitions environmentDefinitions;
    [SerializeField] VegetationSettings vegetationSettings;
    [SerializeField] string seeds = "FOREST-001";
    [SerializeField] float sampleSpacing = 8f;
    [SerializeField] Vector2 start;
    [SerializeField] HabitatRequirements requirements = HabitatRequirements.Founders;

    readonly List<SeedResult> results = new List<SeedResult>();
    SerializedObject serializedWindow;
    string sourceDescription;
    string snapshotJson;
    int selected;
    Vector2 scroll;
    string error;

    [MenuItem("Tools/Boreal Ecosystem/Habitat Analysis")]
    static void Open()
    {
        GetWindow<HabitatAnalysisWindow>("Habitat Analysis").minSize = new Vector2(420f, 520f);
    }

    void OnEnable()
    {
        serializedWindow = new SerializedObject(this);
        if (heightSettings == null || meshSettings == null || environmentDefinitions == null)
        {
            LoadSettings();
            UseSceneStart();
        }
    }

    void OnDisable()
    {
        ClearResults();
    }

    void LoadSettings()
    {
        TerrainGenerator generator = FindAnyObjectByType<TerrainGenerator>();
        heightSettings = generator != null ? generator.heightMapSettings : null;
        meshSettings = generator != null ? generator.meshSettings : null;
        environmentDefinitions = generator != null ? generator.EnvironmentDefinitions : null;
        vegetationSettings = generator != null ? generator.vegetationSettings : null;
        if (heightSettings == null) heightSettings = AssetDatabase.LoadAssetAtPath<HeightMapSettings>(TerrainSettingsFolder + "HeightMapSettings.asset");
        if (meshSettings == null) meshSettings = AssetDatabase.LoadAssetAtPath<MeshSettings>(TerrainSettingsFolder + "MeshSettings.asset");
        if (environmentDefinitions == null) environmentDefinitions = AssetDatabase.LoadAssetAtPath<EnvironmentDefinitions>(TerrainSettingsFolder + "EnvironmentDefinitions.asset");
        if (vegetationSettings == null) vegetationSettings = AssetDatabase.LoadAssetAtPath<VegetationSettings>(TerrainSettingsFolder + "VegetationSettings.asset");
        sourceDescription = generator != null
            ? (EditorApplication.isPlaying ? "Active terrain (Play mode settings)" : "Active terrain (Edit mode settings)")
            : "Default terrain settings assets";
    }

    // Founders appear around the ecosystem's simulation focus, so the analysis starts there too.
    void UseSceneStart()
    {
        AnimalTerrainDemoBootstrap bootstrap = FindAnyObjectByType<AnimalTerrainDemoBootstrap>();
        TerrainGenerator generator = FindAnyObjectByType<TerrainGenerator>();
        Transform focus = bootstrap != null && bootstrap.simulationFocus != null ? bootstrap.simulationFocus
            : generator != null ? generator.viewer : null;
        start = focus != null ? new Vector2(focus.position.x, focus.position.z) : Vector2.zero;
    }

    void OnGUI()
    {
        serializedWindow.Update();
        EditorGUILayout.LabelField("Habitat availability and accessibility", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Editor-only analysis. No scene objects or settings are changed.", EditorStyles.miniLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(heightSettings)));
        EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(meshSettings)));
        EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(environmentDefinitions)));
        EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(vegetationSettings)));
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Load from active terrain")) LoadSettings();
            if (GUILayout.Button("Use scene start point")) UseSceneStart();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Seeds, one per line, as typed on the seed screen", EditorStyles.miniBoldLabel);
        SerializedProperty seedsProperty = serializedWindow.FindProperty(nameof(seeds));
        seedsProperty.stringValue = EditorGUILayout.TextArea(seedsProperty.stringValue, GUILayout.MinHeight(54f));
        SerializedProperty spacingProperty = serializedWindow.FindProperty(nameof(sampleSpacing));
        spacingProperty.floatValue = EditorGUILayout.Slider(
            new GUIContent("Sample spacing (m)", "8 m matches the animals' navigation sampling. Larger is faster but can miss narrow barriers."),
            spacingProperty.floatValue, 4f, 64f);
        EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(start)),
            new GUIContent("Start (world X, Z)", "Founders start on the walkable ground nearest this point."));
        EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(requirements)), true);
        serializedWindow.ApplyModifiedProperties();
        if (GUILayout.Button("Reset requirements to the founders'"))
        {
            requirements = HabitatRequirements.Founders;
            serializedWindow.Update();
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(heightSettings == null || meshSettings == null || environmentDefinitions == null))
            {
                if (GUILayout.Button("Analyse", GUILayout.Height(28f))) Analyse();
            }

            using (new EditorGUI.DisabledScope(results.Count == 0))
            {
                if (GUILayout.Button("Export maps + CSV…", GUILayout.Height(28f))) Export();
            }
        }

        if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        if (results.Count > 0) DrawResults();
        EditorGUILayout.EndScrollView();
    }

    void DrawResults()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Results (availability = suitable share of land; accessibility = reachable share of suitable)",
            EditorStyles.wordWrappedMiniLabel);
        for (int i = 0; i < results.Count; i++)
        {
            HabitatAnalysis analysis = results[i].analysis;
            string row = string.Format(CultureInfo.InvariantCulture,
                "{0}   availability {1:0.0}%   accessibility {2:0.0}%   {3} habitat regions",
                results[i].seedText, analysis.Availability * 100f, analysis.Accessibility * 100f, analysis.HabitatRegionCount);
            if (GUILayout.Toggle(selected == i, row, EditorStyles.radioButton)) selected = i;
        }

        SeedResult result = results[Mathf.Clamp(selected, 0, results.Count - 1)];
        Rect mapRect = GUILayoutUtility.GetAspectRect(1f, GUILayout.MaxWidth(position.width - 24f));
        GUI.DrawTexture(mapRect, result.map, ScaleMode.ScaleToFit);
        foreach ((HabitatClass _, Color32 color, string label) in Legend)
        {
            Rect line = EditorGUILayout.GetControlRect();
            EditorGUI.DrawRect(new Rect(line.x, line.y + 3f, 12f, 12f), color);
            EditorGUI.LabelField(new Rect(line.x + 18f, line.y, line.width - 18f, line.height), label, EditorStyles.miniLabel);
        }

        EditorGUILayout.LabelField(string.Format(CultureInfo.InvariantCulture,
            "Cross = start at ({0:0}, {1:0}), {2:0} m from the requested point. North (+Z) is up.",
            result.analysis.Start.x, result.analysis.Start.y, result.analysis.StartOffset), EditorStyles.miniLabel);
    }

    void Analyse()
    {
        error = null;
        List<string> seedList = ParseSeeds();
        if (seedList.Count == 0)
        {
            error = "Enter at least one seed.";
            return;
        }

        ClearResults();
        float cellSize = LoadCellSize();
        try
        {
            for (int i = 0; i < seedList.Count; i++)
            {
                int index = i;
                string seedText = seedList[i];
                HeightMapSettings heights = Instantiate(heightSettings);
                EnvironmentDefinitions environment = Instantiate(environmentDefinitions);
                try
                {
                    // The same seeds the seed screen gives the world, so the analysis matches the game's world.
                    WorldSeeds worldSeeds = SeedManager.GetWorldSeeds(seedText);
                    worldSeeds.ApplyTo(heights);
                    worldSeeds.ApplyTo(environment);
                    TerrainEnvironmentSampler sampler =
                        new TerrainEnvironmentSampler(heights, meshSettings, environment, vegetationSettings);
                    HabitatAnalysis analysis = HabitatAnalysis.Run(sampler, requirements, sampleSpacing, start, cellSize,
                        progress => EditorUtility.DisplayCancelableProgressBar("Habitat analysis",
                            $"Seed {index + 1} of {seedList.Count}: {seedText}", (index + progress) / seedList.Count));
                    results.Add(new SeedResult
                    {
                        seedText = seedText,
                        worldSeeds = worldSeeds,
                        analysis = analysis,
                        map = BuildMap(analysis)
                    });
                }
                finally
                {
                    HydraulicErosionCache.Invalidate(heights);
                    DestroyImmediate(heights);
                    DestroyImmediate(environment);
                }
            }

            snapshotJson = BuildSnapshotJson(cellSize);
            selected = 0;
        }
        catch (OperationCanceledException)
        {
            ClearResults();
        }
        catch (Exception exception)
        {
            ClearResults();
            error = exception.Message;
            Debug.LogException(exception);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    void Export()
    {
        string parent = EditorUtility.SaveFolderPanel("Export habitat analysis", "", "");
        if (string.IsNullOrEmpty(parent)) return;

        string folder = Path.Combine(parent, $"HabitatAnalysis-{DateTime.Now:yyyyMMdd-HHmmss-fff}");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "habitat-summary.csv"), BuildSummaryCsv());
            for (int i = 0; i < results.Count; i++)
            {
                string prefix = $"{i + 1:00}-{SafeFileName(results[i].seedText)}";
                Texture2D map = ScaleNearest(results[i].map, Mathf.Max(1, ExportMapSize / results[i].map.width));
                try { File.WriteAllBytes(Path.Combine(folder, prefix + "-habitat-map.png"), map.EncodeToPNG()); }
                finally { if (map != results[i].map) DestroyImmediate(map); }
                File.WriteAllText(Path.Combine(folder, prefix + "-cells.csv"), BuildCellsCsv(results[i].analysis));
            }

            File.WriteAllText(Path.Combine(folder, "FIGURE-CAPTIONS.md"), BuildCaptions());
            File.WriteAllText(Path.Combine(folder, "settings-snapshot.json"), snapshotJson);
            EditorUtility.RevealInFinder(folder);
        }
        catch (Exception exception)
        {
            error = $"Export did not finish. Partial files may be in {folder}. {exception.Message}";
            Debug.LogException(exception);
        }
    }

    List<string> ParseSeeds()
    {
        List<string> seedList = new List<string>();
        foreach (string line in (seeds ?? "").Split('\n'))
        {
            string seedText = line.Trim();
            if (seedText.Length > 0) seedList.Add(seedText);
        }

        return seedList;
    }

    static float LoadCellSize()
    {
        EcosystemSimulationSettings settings = AssetDatabase.LoadAssetAtPath<EcosystemSimulationSettings>(EcosystemSettingsPath);
        return settings != null ? Mathf.Max(25f, settings.cellSize) : DefaultCellSize;
    }

    static Texture2D BuildMap(HabitatAnalysis analysis)
    {
        int size = analysis.Size;
        Color32[] pixels = new Color32[size * size];
        for (int z = 0; z < size; z++)
        {
            for (int x = 0; x < size; x++)
            {
                pixels[z * size + x] = ColorOf(analysis.GetClass(x, z));
            }
        }

        if (analysis.HasStart)
        {
            int startX = Mathf.RoundToInt((analysis.Start.x + analysis.Extent) / analysis.Spacing);
            int startZ = Mathf.RoundToInt((analysis.Start.y + analysis.Extent) / analysis.Spacing);
            int arm = Mathf.Max(4, size / 60);
            DrawCross(pixels, size, startX, startZ, arm + 1, 1, StartOutlineColor);
            DrawCross(pixels, size, startX, startZ, arm, 0, StartColor);
        }

        // Texture rows run south to north, so the PNG has north at the top.
        Texture2D map = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        map.SetPixels32(pixels);
        map.Apply(false);
        return map;
    }

    static Color32 ColorOf(HabitatClass habitatClass)
    {
        foreach ((HabitatClass legendClass, Color32 color, string _) in Legend)
        {
            if (legendClass == habitatClass) return color;
        }

        return NoDataColor;
    }

    static void DrawCross(Color32[] pixels, int size, int centreX, int centreZ, int arm, int halfWidth, Color32 color)
    {
        for (int offset = -arm; offset <= arm; offset++)
        {
            for (int width = -halfWidth; width <= halfWidth; width++)
            {
                SetPixel(pixels, size, centreX + offset, centreZ + width, color);
                SetPixel(pixels, size, centreX + width, centreZ + offset, color);
            }
        }
    }

    static void SetPixel(Color32[] pixels, int size, int x, int z, Color32 color)
    {
        if (x >= 0 && x < size && z >= 0 && z < size) pixels[z * size + x] = color;
    }

    static Texture2D ScaleNearest(Texture2D source, int factor)
    {
        if (factor <= 1) return source;

        int size = source.width;
        int scaledSize = size * factor;
        Color32[] sourcePixels = source.GetPixels32();
        Color32[] pixels = new Color32[scaledSize * scaledSize];
        for (int z = 0; z < scaledSize; z++)
        {
            for (int x = 0; x < scaledSize; x++)
            {
                pixels[z * scaledSize + x] = sourcePixels[z / factor * size + x / factor];
            }
        }

        Texture2D scaled = new Texture2D(scaledSize, scaledSize, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        scaled.SetPixels32(pixels);
        scaled.Apply(false);
        return scaled;
    }

    string BuildSummaryCsv()
    {
        StringBuilder csv = new StringBuilder();
        csv.AppendLine("seed,terrain_seed,sample_spacing_m,land_km2,walkable_km2,suitable_km2,accessible_suitable_km2," +
                       "availability_percent,accessibility_percent,habitat_regions,largest_region_percent," +
                       "start_x,start_z,start_offset_m");
        foreach (SeedResult result in results)
        {
            HabitatAnalysis analysis = result.analysis;
            float squareKilometres = analysis.SampleArea / 1000000f;
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2:0.##},{3:0.###},{4:0.###},{5:0.###},{6:0.###},{7:0.##},{8:0.##},{9},{10:0.##},{11:0.#},{12:0.#},{13:0.#}",
                CsvText(result.seedText), result.worldSeeds.terrain, analysis.Spacing,
                analysis.LandSamples * squareKilometres, analysis.WalkableSamples * squareKilometres,
                analysis.SuitableSamples * squareKilometres, analysis.AccessibleSamples * squareKilometres,
                analysis.Availability * 100f, analysis.Accessibility * 100f, analysis.HabitatRegionCount,
                analysis.LargestRegionShare * 100f, analysis.Start.x, analysis.Start.y, analysis.StartOffset));
        }

        return csv.ToString();
    }

    static string BuildCellsCsv(HabitatAnalysis analysis)
    {
        StringBuilder csv = new StringBuilder();
        csv.AppendLine("cell_x,cell_z,centre_x,centre_z,samples,land_fraction,walkable_fraction,suitable_fraction," +
                       "accessible_fraction,accessible_share_of_suitable");
        foreach (HabitatCellSummary cell in analysis.Cells)
        {
            Vector2 centre = TerrainGrid.CellToWorldPosition(cell.coordinate, analysis.CellSize);
            float samples = Mathf.Max(1, cell.samples);
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2:0.#},{3:0.#},{4},{5:0.####},{6:0.####},{7:0.####},{8:0.####},{9:0.####}",
                cell.coordinate.x, cell.coordinate.y, centre.x, centre.y, cell.samples,
                cell.land / samples, cell.walkable / samples, cell.suitable / samples, cell.accessible / samples,
                cell.suitable == 0 ? 0f : (float)cell.accessible / cell.suitable));
        }

        return csv.ToString();
    }

    string BuildSnapshotJson(float cellSize)
    {
        SettingsSnapshot snapshot = new SettingsSnapshot
        {
            generatedUtc = DateTime.UtcNow.ToString("O"),
            source = sourceDescription,
            seeds = results.ConvertAll(result => result.seedText).ToArray(),
            terrainSeeds = results.ConvertAll(result => result.worldSeeds.terrain).ToArray(),
            worldSeeds = results.ConvertAll(result => result.worldSeeds).ToArray(),
            sampleSpacing = sampleSpacing,
            cellSize = cellSize,
            requestedStart = start,
            requirements = requirements,
            heightSettingsJson = EditorJsonUtility.ToJson(heightSettings, true),
            meshSettingsJson = EditorJsonUtility.ToJson(meshSettings, true),
            environmentDefinitionsJson = EditorJsonUtility.ToJson(environmentDefinitions, true),
            vegetationSettingsJson = vegetationSettings == null ? "" : EditorJsonUtility.ToJson(vegetationSettings, true)
        };
        return JsonUtility.ToJson(snapshot, true);
    }

    string BuildCaptions()
    {
        HabitatAnalysis first = results[0].analysis;
        HabitatRequirements used = first.Requirements;
        StringBuilder text = new StringBuilder();
        text.AppendLine("# Habitat availability and accessibility");
        text.AppendLine();
        text.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "Each map covers the habitable square the animals use ({0:0} m on a side), sampled every {1:0.##} m. " +
            "North (+Z) is up. The cross marks the start: the walkable ground nearest ({2:0}, {3:0}).",
            first.Extent * 2f, first.Spacing, first.RequestedStart.x, first.RequestedStart.y));
        text.AppendLine();
        text.AppendLine("## Definitions");
        text.AppendLine();
        text.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "- **Walkable:** land, not shore, with a slope of at most {0:0.#}°. The same rule the animals' navigation uses.",
            used.maximumSlopeDegrees));
        text.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "- **Suitable habitat:** walkable ground where ground plants grow (grass biomass at least {0:0.###} and a " +
            "regrowth rate of at least {1:0}% of ideal, with growth from {2:0.#} °C to {3:0.#} °C) and the temperature " +
            "is within the animal's comfort range ({4:0.#} °C to {5:0.#} °C).",
            used.minimumGrassBiomass, used.minimumPlantGrowthRate * 100f, used.plantMinimumCelsius, used.plantMaximumCelsius,
            used.comfortMinimumCelsius, used.comfortMaximumCelsius));
        text.AppendLine("- **Availability:** suitable habitat as a share of all land.");
        text.AppendLine("- **Accessibility:** the share of suitable habitat an animal can walk to from the start, moving " +
                        "between neighbouring walkable samples. Walkable ground without food still counts as a route.");
        text.AppendLine("- **Habitat regions:** separate walkable areas that hold any suitable habitat.");
        text.AppendLine();
        text.AppendLine("## Legend");
        text.AppendLine();
        foreach ((HabitatClass _, Color32 color, string label) in Legend)
        {
            text.AppendLine($"- #{ColorUtility.ToHtmlStringRGB(color)}: {label}");
        }

        text.AppendLine();
        text.AppendLine("## Results");
        text.AppendLine();
        foreach (SeedResult result in results)
        {
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "- **{0}** (terrain seed {1}): availability {2:0.0}%, accessibility {3:0.0}%, {4} habitat regions.",
                result.seedText, result.worldSeeds.terrain, result.analysis.Availability * 100f,
                result.analysis.Accessibility * 100f, result.analysis.HabitatRegionCount));
        }

        text.AppendLine();
        text.AppendLine("## Limits");
        text.AppendLine();
        text.AppendLine("- A model, not a measurement. The rules are the simulation's own design choices, not field data.");
        text.AppendLine("- Movement is checked between neighbouring samples. Barriers narrower than the sample spacing can be " +
                        "missed, and the animals' NavMesh (agent size, step height) is not reproduced exactly.");
        text.AppendLine("- Tall forest food is left out, because the founders cannot reach it. Seasons are not modelled.");
        text.AppendLine("- Per-cell CSV files use the off-screen population model's cells " +
                        string.Format(CultureInfo.InvariantCulture, "({0:0} m).", first.CellSize));
        text.AppendLine("- The full settings are in settings-snapshot.json. Only the seeds change between worlds: as on the " +
                        "seed screen, the terrain, ridges, rivers, lakes, moisture, temperature and plant patches each " +
                        "take theirs from the world seed (worldSeeds in the snapshot).");
        return text.ToString();
    }

    static string CsvText(string value)
    {
        return value.IndexOfAny(new[] { ',', '"', '\n' }) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    static string SafeFileName(string value)
    {
        StringBuilder safe = new StringBuilder(value.Length);
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char character in value)
        {
            safe.Append(Array.IndexOf(invalid, character) >= 0 || char.IsWhiteSpace(character) ? '_' : character);
        }

        return safe.ToString();
    }

    void ClearResults()
    {
        foreach (SeedResult result in results)
        {
            if (result.map != null) DestroyImmediate(result.map);
        }

        results.Clear();
        snapshotJson = null;
    }
}
