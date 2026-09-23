using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class TerrainReportPreviewWindow : EditorWindow
{
    const string SettingsFolder = "Assets/TerrainGeneration/Settings/";
    static readonly int[] RegionWidths = { 1, 3, 5, 7, 9, 11, 13, 15 };
    static readonly string[] RegionLabels = { "1 x 1", "3 x 3", "5 x 5", "7 x 7", "9 x 9", "11 x 11", "13 x 13", "15 x 15" };
    static readonly string[] StageFiles =
    {
        "01-base-noise.png", "02-domain-warped-noise.png", "03-mountain-ridge-blend.png",
        "04-river-carving-mask.png", "05-lake-carving-mask.png", "06-world-falloff-mask.png",
        "07-shaped-height-before-curve.png", "08-final-heightmap.png"
    };

    [SerializeField] HeightMapSettings heightSettings;
    [SerializeField] MeshSettings meshSettings;
    [SerializeField] TextureData textureSettings;
    [SerializeField] Vector2Int centreChunk;
    [SerializeField] int regionWidth = 9;
    [SerializeField] int lod;
    [SerializeField] int viewMode;
    [SerializeField] int selectedStage;
    [SerializeField] Vector2 orbit = new Vector2(-35f, 35f);
    [SerializeField] float zoom = 1f;
    Vector2 scroll;
    TerrainReportData data;
    TerrainReportRenderer renderer;
    string snapshotJson;
    string snapshotCaption;
    string sourceDescription;
    string error;
    int generatedSeed;

    [Serializable]
    sealed class SettingsSnapshot
    {
        public string generatedUtc;
        public string source;
        public string heightSettingsJson;
        public string meshSettingsJson;
        public string textureSettingsJson;
        public string environmentDefinitionsJson;
        public Vector2Int centreChunk;
        public Vector2 worldCentre;
        public float worldSize;
        public int chunksPerAxis;
        public int mapResolution;
        public int meshLod;
        public float finalMapBlackHeight;
        public float finalMapWhiteHeight;
    }

    [MenuItem("Tools/Boreal Ecosystem/Terrain Report Preview")]
    public static void Open()
    {
        TerrainReportPreviewWindow window = GetWindow<TerrainReportPreviewWindow>("Terrain Report");
        window.minSize = new Vector2(700f, 640f);
        window.Show();
    }

    void OnEnable()
    {
        if (heightSettings == null || meshSettings == null)
        {
            LoadSettings();
            SetMountainRegion();
        }
    }

    void OnDisable()
    {
        data?.Dispose();
        data = null;
        renderer?.Dispose();
        renderer = null;
    }

    void LoadSettings()
    {
        TerrainGenerator generator = FindAnyObjectByType<TerrainGenerator>();
        heightSettings = generator != null ? generator.heightMapSettings : null;
        meshSettings = generator != null ? generator.meshSettings : null;
        textureSettings = generator != null ? generator.textureSettings : null;
        if (heightSettings == null) heightSettings = AssetDatabase.LoadAssetAtPath<HeightMapSettings>(SettingsFolder + "HeightMapSettings.asset");
        if (meshSettings == null) meshSettings = AssetDatabase.LoadAssetAtPath<MeshSettings>(SettingsFolder + "MeshSettings.asset");
        if (textureSettings == null) textureSettings = AssetDatabase.LoadAssetAtPath<TextureData>(SettingsFolder + "TextureData.asset");
        sourceDescription = generator != null
            ? (EditorApplication.isPlaying ? "Active terrain (Play mode settings)" : "Active terrain (Edit mode settings)")
            : "Default terrain settings assets";
    }

    void SetMountainRegion()
    {
        if (heightSettings == null || meshSettings == null) return;
        int stride = meshSettings.numVertsPerLine - 3;
        centreChunk = new Vector2Int(Mathf.RoundToInt(heightSettings.worldRadius * 0.65f / stride), 0);
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Noise maps → matching terrain", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Editor-only snapshots. No scene objects or terrain settings are changed.", EditorStyles.miniLabel);
        heightSettings = (HeightMapSettings)EditorGUILayout.ObjectField("Height settings", heightSettings, typeof(HeightMapSettings), false);
        meshSettings = (MeshSettings)EditorGUILayout.ObjectField("Mesh settings", meshSettings, typeof(MeshSettings), false);
        textureSettings = (TextureData)EditorGUILayout.ObjectField("Terrain colors", textureSettings, typeof(TextureData), false);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Load from active terrain")) LoadSettings();
            if (GUILayout.Button("Origin / lowlands")) centreChunk = Vector2Int.zero;
            if (GUILayout.Button("Mountain belt")) SetMountainRegion();
        }
        centreChunk = EditorGUILayout.Vector2IntField("Center chunk (X, Z)", centreChunk);
        using (new EditorGUILayout.HorizontalScope())
        {
            regionWidth = EditorGUILayout.IntPopup("Region (chunks)", regionWidth, RegionLabels, RegionWidths);
            lod = EditorGUILayout.IntSlider("3D mesh LOD", lod, 0, MeshSettings.numSupportedLODs - 1);
        }
        if (heightSettings != null && heightSettings.noiseSettings != null && heightSettings.noiseSettings.normalizeMode == Noise.NormalizeMode.Local)
            EditorGUILayout.HelpBox("Local normalization is chunk-dependent and may create seams. This preview preserves the generator's current settings.", MessageType.Warning);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(heightSettings == null || meshSettings == null))
                if (GUILayout.Button("Generate report preview", GUILayout.Height(28f))) Generate();
            using (new EditorGUI.DisabledScope(data == null))
                if (GUILayout.Button("Export all PNGs + captions…", GUILayout.Height(28f))) Export();
        }
        if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        if (data == null)
        {
            EditorGUILayout.HelpBox("Choose a region and generate. Every map and mesh uses the same seed, region and terrain calculations. Play mode is not required.", MessageType.Info);
            return;
        }
        EditorGUILayout.LabelField($"Snapshot: seed {generatedSeed} • {data.Size} × {data.Size} • {data.ChunkCount} × {data.ChunkCount} chunks • LOD {data.Lod}", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField($"Center X/Z {data.WorldCentre} • width {data.WorldSize:0.##} world units • height {data.MinimumHeight:0.##}–{data.MaximumHeight:0.##}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Regenerate after changing settings or seeds. Maps: north (+Z) at top, east (+X) at right.", EditorStyles.miniLabel);
        viewMode = GUILayout.Toolbar(viewMode, new[] { "Report overview", "Individual stages", "3D terrain" });
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (viewMode == 0) DrawOverview();
        else if (viewMode == 1) DrawStage();
        else DrawTerrain();
        EditorGUILayout.EndScrollView();
    }

    void Generate()
    {
        error = null;
        HeightMapSettings heights = null;
        MeshSettings meshes = null;
        TextureData textures = null;
        TerrainReportData generated = null;
        try
        {
            heights = Instantiate(heightSettings);
            meshes = Instantiate(meshSettings);
            if (textureSettings != null) textures = Instantiate(textureSettings);
            generated = TerrainReportData.Generate(heights, meshes, textures, centreChunk, regionWidth, lod,
                progress => EditorUtility.DisplayCancelableProgressBar("Terrain report preview", "Sampling the actual terrain pipeline…", progress));
            SettingsSnapshot snapshot = new SettingsSnapshot
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                source = sourceDescription,
                heightSettingsJson = EditorJsonUtility.ToJson(heights, true),
                meshSettingsJson = EditorJsonUtility.ToJson(meshes, true),
                textureSettingsJson = textures == null ? "" : EditorJsonUtility.ToJson(textures, true),
                environmentDefinitionsJson = textures == null || textures.environmentDefinitions == null ? "" : EditorJsonUtility.ToJson(textures.environmentDefinitions, true),
                centreChunk = centreChunk,
                worldCentre = generated.WorldCentre,
                worldSize = generated.WorldSize,
                chunksPerAxis = regionWidth,
                mapResolution = generated.Size,
                meshLod = lod,
                finalMapBlackHeight = heights.minHeight,
                finalMapWhiteHeight = heights.maxHeight
            };
            string json = JsonUtility.ToJson(snapshot, true);
            int seed = heights.noiseSettings.seed;
            string caption = BuildCaptions(generated, heights);
            data?.Dispose();
            data = generated;
            generated = null;
            snapshotJson = json;
            snapshotCaption = caption;
            generatedSeed = seed;
            zoom = 1f;
            scroll = Vector2.zero;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { error = exception.Message; Debug.LogException(exception); }
        finally
        {
            generated?.Dispose();
            if (heights != null) DestroyImmediate(heights);
            if (meshes != null) DestroyImmediate(meshes);
            if (textures != null) DestroyImmediate(textures);
            EditorUtility.ClearProgressBar();
        }
    }

    void DrawOverview()
    {
        float tileSize = Mathf.Max(200f, (position.width - 56f) * 0.5f);
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawTile((int)TerrainReportStage.BaseNoise, tileSize);
            DrawTile((int)TerrainReportStage.RidgeBlend, tileSize);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawTile((int)TerrainReportStage.FinalHeight, tileSize);
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(tileSize)))
            {
                GUILayout.Label("09  Matching 3D terrain", EditorStyles.boldLabel);
                Rect rect = GUILayoutUtility.GetRect(tileSize, tileSize);
                Draw3D(rect);
                GUILayout.Label("Same geometry; simplified biome colors. No vertical exaggeration.", EditorStyles.wordWrappedMiniLabel);
            }
        }
    }

    void DrawTile(int stage, float size)
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(size)))
        {
            GUILayout.Label(TerrainReportData.StageTitles[stage], EditorStyles.boldLabel);
            Rect rect = GUILayoutUtility.GetRect(size, size);
            GUI.DrawTexture(rect, data.Textures[stage], ScaleMode.ScaleToFit);
            GUILayout.Label(TerrainReportData.StageDescriptions[stage], EditorStyles.wordWrappedMiniLabel);
        }
    }

    void DrawStage()
    {
        selectedStage = EditorGUILayout.Popup("Pipeline stage", selectedStage, TerrainReportData.StageTitles);
        float size = Mathf.Max(240f, Mathf.Min(position.width - 40f, position.height - 385f));
        DrawTile(selectedStage, size);
        if (selectedStage == (int)TerrainReportStage.FinalHeight)
            EditorGUILayout.LabelField($"Black = {data.HeightScaleMinimum:0.###}; white = {data.HeightScaleMaximum:0.###} world units. Fixed scale across regions.", EditorStyles.wordWrappedMiniLabel);
    }

    void DrawTerrain()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("North-up oblique")) { orbit = new Vector2(0f, 55f); zoom = 1f; }
            if (GUILayout.Button("Angled 3D view")) { orbit = new Vector2(-35f, 35f); zoom = 1f; }
            if (GUILayout.Button("Top down (+Z up)")) { orbit = new Vector2(0f, 90f); zoom = 1f; }
        }
        GUILayout.Label("Drag to orbit • scroll to zoom • original height scale • no vegetation or water-surface mesh", EditorStyles.miniLabel);
        Rect rect = GUILayoutUtility.GetRect(200f, Mathf.Max(240f, position.height - 405f), GUILayout.ExpandWidth(true));
        Draw3D(rect);
        GUILayout.Label("The terrain mesh matches the final heightmap. Report shading uses height-based biome tints, not the runtime terrain shader.", EditorStyles.wordWrappedMiniLabel);
    }

    void Draw3D(Rect rect)
    {
        Event current = Event.current;
        if (rect.Contains(current.mousePosition))
        {
            if (current.type == EventType.MouseDrag && current.button == 0)
            {
                orbit.x += current.delta.x * 0.5f;
                orbit.y = Mathf.Clamp(orbit.y + current.delta.y * 0.5f, 10f, 90f);
                current.Use();
                Repaint();
            }
            if (current.type == EventType.ScrollWheel)
            {
                zoom = Mathf.Clamp(zoom * Mathf.Exp(-current.delta.y * 0.04f), 0.5f, 3f);
                current.Use();
                Repaint();
            }
        }
        if (current.type != EventType.Repaint) return;
        try
        {
            if (renderer == null) renderer = new TerrainReportRenderer();
            GUI.DrawTexture(rect, renderer.Render(data, rect, orbit, zoom), ScaleMode.StretchToFill, false);
        }
        catch (Exception exception) { error = exception.Message; }
    }

    void Export()
    {
        string parent = EditorUtility.OpenFolderPanel("Choose a folder for report figures", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "");
        if (string.IsNullOrEmpty(parent)) return;
        string folder = Path.Combine(parent, $"TerrainReport-seed-{generatedSeed}-{DateTime.Now:yyyyMMdd-HHmmss-fff}");
        try
        {
            ExportSnapshot(folder);
            EditorUtility.RevealInFinder(folder);
            ShowNotification(new GUIContent("Report figures exported"));
        }
        catch (Exception exception) { error = $"Export did not finish. Partial files may be in {folder}. {exception.Message}"; Debug.LogException(exception); }
    }

    public void ExportSnapshot(string newFolder)
    {
        if (data == null) throw new InvalidOperationException("Generate a report preview before exporting.");
        if (Directory.Exists(newFolder) || File.Exists(newFolder))
            throw new IOException("Choose a new folder so existing report figures are not overwritten.");
        Directory.CreateDirectory(newFolder);
        for (int stage = 0; stage < data.Textures.Length; stage++)
            File.WriteAllBytes(Path.Combine(newFolder, StageFiles[stage]), data.Textures[stage].EncodeToPNG());
        if (renderer == null) renderer = new TerrainReportRenderer();
        Texture2D terrain = renderer.Export(data, 2048, orbit, zoom);
        try { File.WriteAllBytes(Path.Combine(newFolder, "09-matching-3d-terrain.png"), terrain.EncodeToPNG()); }
        finally { DestroyImmediate(terrain); }
        Texture2D overhead = renderer.Export(data, 2048, new Vector2(0f, 90f), 1f);
        try { File.WriteAllBytes(Path.Combine(newFolder, "10-top-down-terrain.png"), overhead.EncodeToPNG()); }
        finally { DestroyImmediate(overhead); }
        File.WriteAllText(Path.Combine(newFolder, "settings-snapshot.json"), snapshotJson);
        File.WriteAllText(Path.Combine(newFolder, "FIGURE-CAPTIONS.md"), snapshotCaption + string.Format(CultureInfo.InvariantCulture,
            "\nExported 3D camera: yaw {0:0.##}°, pitch {1:0.##}°, zoom {2:0.###}. Image size: 2048 × 2048.\n", orbit.x, orbit.y, zoom));
    }

    static string BuildCaptions(TerrainReportData generated, HeightMapSettings heights)
    {
        StringBuilder result = new StringBuilder();
        result.AppendLine("# Noise Maps and Corresponding Terrain Outputs\n");
        result.AppendLine($"Base noise seed: {heights.noiseSettings.seed}. Ridge seed: {heights.ridgeSettings?.seed}. River seed: {heights.riverSettings?.seed}. Lake seed: {heights.lakeSettings?.seed}.");
        result.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "Region: chunk center ({0}, {1}); {2} × {2} chunks; world X/Z center ({3:0.###}, {4:0.###}); width {5:0.###} world units; {6} × {6} height samples; mesh LOD {7}.\n",
            generated.CentreChunk.x, generated.CentreChunk.y, generated.ChunkCount, generated.WorldCentre.x, generated.WorldCentre.y, generated.WorldSize, generated.Size, generated.Lod));
        result.AppendLine("All figures use one settings snapshot and the same world region. North (+Z) is up and east (+X) is right in all 2D maps. The 3D camera may be rotated independently.\n");
        for (int stage = 0; stage < StageFiles.Length; stage++)
            result.AppendLine($"- **{StageFiles[stage]}** — {TerrainReportData.StageDescriptions[stage]}");
        result.AppendLine("- **09-matching-3d-terrain.png** — Terrain generated from the final height samples using the production mesh generator, at the selected LOD. Vertical scale is unchanged. Simplified biome-tint lighting is used; runtime vegetation, water surfaces and shader detail are intentionally excluded.");
        result.AppendLine("- **10-top-down-terrain.png** — The same terrain geometry, viewed from above with +Z up, for comparison against the heightmap.\n");
        result.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "The final heightmap uses a fixed grayscale range: black = {0:0.###}, white = {1:0.###} world units. Actual sampled range: {2:0.###}–{3:0.###}. PNGs are 8-bit visualizations, not lossless height data. Other stages use 0–1. Values outside the display range are clipped.\n",
            generated.HeightScaleMinimum, generated.HeightScaleMaximum, generated.MinimumHeight, generated.MaximumHeight));
        result.AppendLine("Pipeline: domain-warped sampling coordinates → seeded octave noise → mountain ridge blending → river carving → lake carving → world falloff → height curve and multiplier → mesh. Stage 01 disables domain warp only as a comparison; stage 02 is the actual generator input. River and lake images show carving weights, not water occupancy. World falloff is a subtraction mask, not a heightmap.\n");
        result.AppendLine("No hydraulic erosion simulation is represented in these figures. Existing shader erosion-like streaks are appearance effects and should not be described as simulated hydraulic erosion. Use mesh LOD 0 for the closest full-resolution geometry comparison.\n");
        result.AppendLine("The complete serialized configuration is recorded in settings-snapshot.json. Disabled effects produce unchanged heights or black masks. Changes to settings after generation do not affect this snapshot.");
        return result.ToString();
    }
}
