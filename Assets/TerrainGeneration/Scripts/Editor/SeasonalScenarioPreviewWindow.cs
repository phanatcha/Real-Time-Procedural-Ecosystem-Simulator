using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class SeasonalScenarioPreviewWindow : EditorWindow
{
    const string SettingsFolder = "Assets/TerrainGeneration/Settings/";
    static readonly string[] SeasonNames = { "Spring", "Summer", "Fall", "Winter" };
    [SerializeField] HeightMapSettings heights;
    [SerializeField] MeshSettings meshes;
    [SerializeField] TextureData textures;
    [SerializeField] VegetationSettings vegetation;
    [SerializeField] Vector2Int centre;
    [SerializeField] int width = 3;
    [SerializeField] int selected = 1;
    [SerializeField] bool compare = true;
    [SerializeField] Vector2 orbit = new Vector2(-35f, 35f);
    [SerializeField] float zoom = 1.2f;
    Vector2 scroll;
    SeasonalReportData data;
    TerrainReportRenderer[] renderers;
    string error;
    string snapshot;

    [Serializable]
    sealed class Snapshot
    {
        public string generatedUtc;
        public string heightSettingsJson;
        public string meshSettingsJson;
        public string textureSettingsJson;
        public string vegetationSettingsJson;
        public string environmentDefinitionsJson;
        public Vector2Int centreChunk;
        public int regionWidth;
        public int treeCount;
        public int grassCount;
        public int rockCount;
        public SeasonState[] states;
    }

    [MenuItem("Tools/Boreal Ecosystem/Seasonal Scenario Preview")]
    public static void Open()
    {
        SeasonalScenarioPreviewWindow window = GetWindow<SeasonalScenarioPreviewWindow>("Seasonal Scenarios");
        window.minSize = new Vector2(740f, 700f);
        window.Show();
    }

    void OnEnable()
    {
        if (heights != null) return;
        heights = AssetDatabase.LoadAssetAtPath<HeightMapSettings>(SettingsFolder + "HeightMapSettings.asset");
        meshes = AssetDatabase.LoadAssetAtPath<MeshSettings>(SettingsFolder + "MeshSettings.asset");
        textures = AssetDatabase.LoadAssetAtPath<TextureData>(SettingsFolder + "TextureData.asset");
        vegetation = AssetDatabase.LoadAssetAtPath<VegetationSettings>(SettingsFolder + "VegetationSettings.asset");
        if (heights != null && meshes != null && textures != null && vegetation != null) FindForestRegion();
    }

    void OnDisable()
    {
        data?.Dispose(); data = null;
        if (renderers != null) foreach (TerrainReportRenderer renderer in renderers) renderer?.Dispose();
        renderers = null;
    }

    public void FindForestRegion()
    {
        error = null;
        try
        {
            if (textures == null || textures.environmentDefinitions == null) throw new InvalidOperationException("Assign environment definitions in Terrain colors.");
            TerrainEnvironmentSampler sampler = new TerrainEnvironmentSampler(heights, meshes, textures.environmentDefinitions, vegetation);
            float best = float.NegativeInfinity;
            int stride = meshes.numVertsPerLine - 3;
            for (int y = -3; y <= 3; y++)
                for (int x = -3; x <= 3; x++)
                {
                    Vector2Int coordinate = new Vector2Int(Mathf.RoundToInt(x * heights.worldRadius * 0.15f / stride),
                        Mathf.RoundToInt(y * heights.worldRadius * 0.15f / stride));
                    EnvironmentSample sample = sampler.Sample(TerrainGrid.ChunkCoordinateToWorldPosition(coordinate, meshes));
                    float score = sample.isLand ? sample.treeCover + sample.grassBiomass * 0.25f - sample.slope : -1f;
                    if (score <= best) continue;
                    best = score;
                    centre = coordinate;
                }
        }
        catch (Exception exception) { error = exception.Message; }
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Seasonal scenario mockup", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("One landscape, four illustrative seasonal presets. Preview only: no scene, gameplay materials, terrain heights or saved settings are changed.", MessageType.Info);
        heights = (HeightMapSettings)EditorGUILayout.ObjectField("Height settings", heights, typeof(HeightMapSettings), false);
        meshes = (MeshSettings)EditorGUILayout.ObjectField("Mesh settings", meshes, typeof(MeshSettings), false);
        textures = (TextureData)EditorGUILayout.ObjectField("Terrain colors", textures, typeof(TextureData), false);
        vegetation = (VegetationSettings)EditorGUILayout.ObjectField("Vegetation", vegetation, typeof(VegetationSettings), false);
        centre = EditorGUILayout.Vector2IntField("Center chunk (X, Z)", centre);
        width = EditorGUILayout.IntPopup("Region (chunks)", width, new[] { "1 x 1", "3 x 3", "5 x 5" }, new[] { 1, 3, 5 });
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Find forest location")) FindForestRegion();
            if (GUILayout.Button("Generate seasonal preview")) Generate();
            using (new EditorGUI.DisabledScope(data == null))
                if (GUILayout.Button("Export four PNGs + captions…")) Export();
        }
        if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        if (data == null) { EditorGUILayout.EndScrollView(); return; }
        EditorGUILayout.LabelField($"Snapshot: {data.TreeCount:N0} pines, {data.GrassCount:N0} grass clumps, {data.RockCount:N0} rocks. Placements are identical across seasons.", EditorStyles.wordWrappedMiniLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            int next = GUILayout.Toolbar(selected, SeasonNames);
            if (next != selected) { selected = next; compare = false; }
            compare = GUILayout.Toggle(compare, "Compare all four", "Button", GUILayout.Width(130f));
        }
        EditorGUILayout.LabelField("Drag to orbit; scroll over a view to zoom. All views share one camera. Temperature is normalized 0–1, not °C.", EditorStyles.wordWrappedMiniLabel);
        if (compare)
        {
            for (int row = 0; row < 2; row++)
                using (new EditorGUILayout.HorizontalScope())
                    for (int column = 0; column < 2; column++)
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width((position.width - 40f) * 0.5f)))
                            DrawFrame(row * 2 + column, Mathf.Max(210f, (position.width - 50f) * 0.36f));
        }
        else DrawFrame(selected, Mathf.Max(300f, position.height - 440f));
        EditorGUILayout.HelpBox("These are seasonal appearance and environmental scenarios, not measured animal behavior. No freezing-water physics, flooding, food consumption, or seasonal population response is simulated here.", MessageType.None);
        EditorGUILayout.EndScrollView();
    }

    public void Generate()
    {
        error = null;
        SeasonalReportData generated = null;
        try
        {
            generated = SeasonalReportData.Generate(heights, meshes, textures, vegetation, centre, width,
                p => EditorUtility.DisplayCancelableProgressBar("Seasonal scenarios", "Building one landscape and four seasonal appearances…", p));
            SeasonState[] states = new SeasonState[4];
            for (int i = 0; i < 4; i++) states[i] = generated.Frames[i].state;
            string json = JsonUtility.ToJson(new Snapshot
            {
                generatedUtc = DateTime.UtcNow.ToString("O"), heightSettingsJson = EditorJsonUtility.ToJson(heights, true),
                meshSettingsJson = EditorJsonUtility.ToJson(meshes, true), textureSettingsJson = EditorJsonUtility.ToJson(textures, true),
                vegetationSettingsJson = EditorJsonUtility.ToJson(vegetation, true),
                environmentDefinitionsJson = EditorJsonUtility.ToJson(textures.environmentDefinitions, true),
                centreChunk = centre, regionWidth = width, treeCount = generated.TreeCount, grassCount = generated.GrassCount,
                rockCount = generated.RockCount, states = states
            }, true);
            data?.Dispose(); data = generated; generated = null; snapshot = json;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { error = exception.Message; Debug.LogException(exception); }
        finally { generated?.Dispose(); EditorUtility.ClearProgressBar(); }
        Repaint();
    }

    TerrainReportRenderer Renderer(int index)
    {
        if (renderers == null) renderers = new TerrainReportRenderer[4];
        if (renderers[index] == null) renderers[index] = new TerrainReportRenderer();
        TerrainReportRenderer renderer = renderers[index];
        renderer.HeightRangeOverride = data.HeightRange;
        renderer.MeshesOverride = data.Frames[index].meshes;
        renderer.LightTint = SeasonalReportData.Lighting((BorealSeason)index);
        return renderer;
    }

    void DrawFrame(int index, float height)
    {
        SeasonalReportData.Frame frame = data.Frames[index];
        GUILayout.Label(SeasonNames[index], EditorStyles.boldLabel);
        Rect rect = GUILayoutUtility.GetRect(200f, height, GUILayout.ExpandWidth(true));
        Event current = Event.current;
        if (rect.Contains(current.mousePosition))
        {
            if (current.type == EventType.MouseDrag && current.button == 0)
            {
                orbit.x += current.delta.x * 0.5f;
                orbit.y = Mathf.Clamp(orbit.y + current.delta.y * 0.5f, 10f, 85f);
                current.Use(); Repaint();
            }
            if (current.type == EventType.ScrollWheel)
            {
                zoom = Mathf.Clamp(zoom * Mathf.Exp(-current.delta.y * 0.04f), 0.5f, 2f);
                current.Use(); Repaint();
            }
        }
        if (current.type == EventType.Repaint)
        {
            try { GUI.DrawTexture(rect, Renderer(index).Render(data.Terrain, rect, orbit, zoom), ScaleMode.StretchToFill, false); }
            catch (Exception exception) { error = exception.Message; }
        }
        string values = data.LandSampleCount == 0 ? "No land samples in this region."
            : string.Format(CultureInfo.InvariantCulture, "Land means — temperature {0:0.00} · moisture {1:0.00}\nSnow {2:P0} · accessible grass {3:0.00} · growth {4:0.00}",
                frame.temperature, frame.moisture, frame.snow, frame.accessibleGrass, frame.growth);
        GUILayout.Label(values, EditorStyles.wordWrappedMiniLabel);
    }

    void Export()
    {
        string parent = EditorUtility.OpenFolderPanel("Choose a folder outside Assets", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "");
        if (string.IsNullOrEmpty(parent)) return;
        string destination = Path.Combine(parent, "SeasonalScenarios-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        try { ExportSnapshot(destination); EditorUtility.RevealInFinder(destination); }
        catch (Exception exception) { error = $"Export incomplete; partial files may be in {destination}. {exception.Message}"; }
    }

    public void ExportSnapshot(string destination)
    {
        if (data == null) throw new InvalidOperationException("Generate a seasonal preview first.");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Use a new folder to avoid overwriting previous figures.");
        Directory.CreateDirectory(destination);
        StringBuilder captions = new StringBuilder("# Seasonal scenario mockup\n\n");
        captions.AppendLine("Four illustrative seasonal states of the same procedural boreal forest. Terrain geometry, erosion settings, pine/grass/rock placements, camera and scale are fixed. Only seasonal colors, surface snow/wetness shading and lighting tint vary. Ground grass browns in fall; evergreen pine foliage is not recolored orange. Snow is a surface overlay, not a change of biome or mesh height.\n");
        captions.AppendLine($"Region: {data.Terrain.CentreChunk}; {data.Terrain.ChunkCount} × {data.Terrain.ChunkCount} chunks; LOD 0; {data.TreeCount} pines, {data.GrassCount} grass clumps, {data.RockCount} rocks.\n");
        captions.AppendLine(string.Format(CultureInfo.InvariantCulture, "Camera: yaw {0:0.##}°, pitch {1:0.##}°, zoom {2:0.###}; each PNG is 2048 × 2048.\n", orbit.x, orbit.y, zoom));
        captions.AppendLine("| Figure | Mean temperature | Mean moisture | Mean snow coverage | Mean accessible grass | Mean grass growth multiplier |\n| --- | ---: | ---: | ---: | ---: | ---: |");
        StringBuilder csv = new StringBuilder("season,land_samples,temperature_01,moisture_01,snow_01,accessible_grass_01,growth_multiplier\n");
        for (int i = 0; i < 4; i++)
        {
            string name = $"{i + 1:00}-{SeasonNames[i].ToLowerInvariant()}.png";
            Texture2D image = Renderer(i).Export(data.Terrain, 2048, orbit, zoom);
            try { File.WriteAllBytes(Path.Combine(destination, name), image.EncodeToPNG()); }
            finally { DestroyImmediate(image); }
            SeasonalReportData.Frame f = data.Frames[i];
            captions.AppendLine(string.Format(CultureInfo.InvariantCulture, "| {0} | {1:0.000} | {2:0.000} | {3:0.000} | {4:0.000} | {5:0.000} |", name, f.temperature, f.moisture, f.snow, f.accessibleGrass, f.growth));
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:R},{3:R},{4:R},{5:R},{6:R}", SeasonNames[i], data.LandSampleCount, f.temperature, f.moisture, f.snow, f.accessibleGrass, f.growth));
        }
        captions.AppendLine($"\nMeans use {data.LandSampleCount} land locations from a uniform 33 × 33 region grid, using the same seasonal evaluator as the public API. If there are no land locations, zero values are placeholders, not measured land conditions. Temperature and resource values are normalized indices, not °C or kilograms. Snow coverage is not snow depth. The settings snapshot includes all source assets and preset values.\n");
        captions.AppendLine("Spring represents wetter ground, returning growth and residual snow; summer has no added seasonal snow and stronger growth; fall has browner ground vegetation and declining growth; winter has colder conditions, more surface snow and reduced access to ground food. This is a stylized inland-boreal scenario, not a calibrated model of all Norwegian climates.\n");
        captions.AppendLine("Implemented: an editor-only seasonal appearance mockup and mesh-independent seasonal environmental queries. Existing runtime shaders and animal behavior are unchanged. Shader lighting is simplified for report presentation; vegetation uses the production placement and procedural mesh builders. Branch snow is an upward-facing shading approximation. Water remains water; no walkable ice, water-level changes, dynamic snow depth, flooding, food consumption/regrowth integration, or animal-response measurements are implemented. These figures must not be described as observed animal adaptation or evolution.\n");
        captions.AppendLine("Suggested caption: ‘Seasonal scenario mockup of one procedurally generated boreal forest. The same terrain and vegetation are shown in spring, summer, fall and winter. Seasonal conditions affect ground appearance, snow cover and environmental resource-access indices; animal responses are not yet connected.’");
        File.WriteAllText(Path.Combine(destination, "FIGURE-CAPTIONS.md"), captions.ToString());
        File.WriteAllText(Path.Combine(destination, "seasonal-summary.csv"), csv.ToString());
        File.WriteAllText(Path.Combine(destination, "settings-snapshot.json"), snapshot);
    }
}
