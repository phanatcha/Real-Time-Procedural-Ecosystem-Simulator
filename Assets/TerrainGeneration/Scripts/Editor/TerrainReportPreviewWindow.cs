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
        "07-shaped-height-before-curve.png", "08-final-heightmap.png",
        "11-before-erosion-heightmap.png", "12-erosion-deposition-change.png", "13-droplet-flow.png"
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
    [SerializeField] bool previewErosion;
    Vector2 scroll;
    TerrainReportData data;
    TerrainReportData beforeErosionData;
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

    [MenuItem("Tools/Boreal Ecosystem/Hydraulic Erosion Example")]
    public static void OpenErosionExample()
    {
        Open();
        TerrainReportPreviewWindow window = GetWindow<TerrainReportPreviewWindow>();
        window.previewErosion = true;
        window.viewMode = 3;
        if (window.heightSettings != null && window.meshSettings != null)
            window.centreChunk = new Vector2Int(Mathf.RoundToInt(window.heightSettings.worldRadius * 0.2f / (window.meshSettings.numVertsPerLine - 3)), 0);
        window.regionWidth = 7;
        window.Repaint();
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
        beforeErosionData?.Dispose();
        beforeErosionData = null;
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
        previewErosion = heightSettings != null && heightSettings.erosionSettings != null && heightSettings.erosionSettings.enabled;
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
        previewErosion = EditorGUILayout.Toggle("Hydraulic erosion (preview)", previewErosion);
        if (previewErosion)
            EditorGUILayout.LabelField("Uses the asset's erosion parameters on a copy; does not enable erosion in your gameplay scene.", EditorStyles.wordWrappedMiniLabel);
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
        viewMode = GUILayout.Toolbar(viewMode, new[] { "Report overview", "Individual stages", "3D terrain", "Hydraulic erosion" });
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (viewMode == 0) DrawOverview();
        else if (viewMode == 1) DrawStage();
        else if (viewMode == 2) DrawTerrain();
        else DrawErosionComparison();
        EditorGUILayout.EndScrollView();
    }

    void Generate()
    {
        error = null;
        HeightMapSettings heights = null;
        MeshSettings meshes = null;
        TextureData textures = null;
        TerrainReportData generated = null;
        TerrainReportData before = null;
        try
        {
            heights = Instantiate(heightSettings);
            if (heights.erosionSettings == null) heights.erosionSettings = new HydraulicErosionSettings();
            heights.erosionSettings.enabled = previewErosion;
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
            if (previewErosion)
            {
                heights.erosionSettings.enabled = false;
                before = TerrainReportData.Generate(heights, meshes, textures, centreChunk, regionWidth, lod,
                    progress => EditorUtility.DisplayCancelableProgressBar("Hydraulic erosion comparison", "Building the unchanged terrain for comparison…", progress));
            }
            data?.Dispose();
            beforeErosionData?.Dispose();
            data = generated;
            generated = null;
            beforeErosionData = before;
            before = null;
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
            before?.Dispose();
            if (heights != null) DestroyImmediate(heights);
            if (meshes != null) DestroyImmediate(meshes);
            if (textures != null) DestroyImmediate(textures);
            EditorUtility.ClearProgressBar();
        }
    }

    void DrawErosionComparison()
    {
        if (beforeErosionData == null)
        {
            EditorGUILayout.HelpBox("Enable Hydraulic erosion (preview) above, then Generate report preview. This creates an unchanged/eroded pair without editing the settings asset.", MessageType.Info);
            return;
        }
        EditorGUILayout.LabelField($"Actual height change: {data.MinimumErosionChange:0.###} to +{data.MaximumErosionChange:0.###} units • mean absolute change {data.MeanAbsoluteErosionChange:0.###}", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField($"Bake: {data.Erosion.Resolution} × {data.Erosion.Resolution} • seed {data.Erosion.Seed} • {data.Erosion.BakeMilliseconds:0} ms (generation only, not per frame)", EditorStyles.wordWrappedMiniLabel);
        float tileSize = Mathf.Max(200f, (position.width - 56f) * 0.5f);
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawComparisonMesh("Before hydraulic erosion", beforeErosionData, tileSize);
            DrawComparisonMesh("After hydraulic erosion", data, tileSize);
        }
        GUILayout.Label("Drag either view to orbit both. Identical camera, colors and true vertical scale.", EditorStyles.miniLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawTile((int)TerrainReportStage.BeforeErosion, tileSize);
            DrawTile((int)TerrainReportStage.FinalHeight, tileSize);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawTile((int)TerrainReportStage.ErosionChange, tileSize);
            DrawTile((int)TerrainReportStage.DropletFlow, tileSize);
        }
        GUILayout.Label($"Change-map legend: blue −{data.ErosionDisplayRange:0.###}; white 0; orange +{data.ErosionDisplayRange:0.###} height units. This is a simplified generation-time model, not a calibrated flood simulation.", EditorStyles.wordWrappedMiniLabel);
    }

    void DrawComparisonMesh(string title, TerrainReportData terrain, float size)
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(size)))
        {
            GUILayout.Label(title, EditorStyles.boldLabel);
            Draw3D(GUILayoutUtility.GetRect(size, size), terrain);
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

    void Draw3D(Rect rect, TerrainReportData terrain = null)
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
            ConfigureRenderer();
            GUI.DrawTexture(rect, renderer.Render(terrain ?? data, rect, orbit, zoom), ScaleMode.StretchToFill, false);
        }
        catch (Exception exception) { error = exception.Message; }
    }

    void ConfigureRenderer()
    {
        if (renderer == null) renderer = new TerrainReportRenderer();
        renderer.HeightRangeOverride = beforeErosionData == null ? (Vector2?)null : new Vector2(
            Mathf.Min(data.MinimumHeight, beforeErosionData.MinimumHeight), Mathf.Max(data.MaximumHeight, beforeErosionData.MaximumHeight));
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
        ConfigureRenderer();
        Texture2D terrain = renderer.Export(data, 2048, orbit, zoom);
        try { File.WriteAllBytes(Path.Combine(newFolder, "09-matching-3d-terrain.png"), terrain.EncodeToPNG()); }
        finally { DestroyImmediate(terrain); }
        Texture2D overhead = renderer.Export(data, 2048, new Vector2(0f, 90f), 1f);
        try { File.WriteAllBytes(Path.Combine(newFolder, "10-top-down-terrain.png"), overhead.EncodeToPNG()); }
        finally { DestroyImmediate(overhead); }
        if (beforeErosionData != null)
        {
            Texture2D before = renderer.Export(beforeErosionData, 2048, orbit, zoom);
            try { File.WriteAllBytes(Path.Combine(newFolder, "14-before-erosion-3d.png"), before.EncodeToPNG()); }
            finally { DestroyImmediate(before); }
            ExportErosionCsv(Path.Combine(newFolder, "erosion-height-samples.csv"));
        }
        File.WriteAllText(Path.Combine(newFolder, "settings-snapshot.json"), snapshotJson);
        File.WriteAllText(Path.Combine(newFolder, "FIGURE-CAPTIONS.md"), snapshotCaption + string.Format(CultureInfo.InvariantCulture,
            "\nExported 3D camera: yaw {0:0.##}°, pitch {1:0.##}°, zoom {2:0.###}. Image size: 2048 × 2048.\n", orbit.x, orbit.y, zoom));
    }

    void ExportErosionCsv(string path)
    {
        using (StreamWriter writer = new StreamWriter(path))
        {
            writer.WriteLine("world_x,world_z,height_before,height_after,height_change,log_relative_droplet_flow");
            float spacing = data.WorldSize / (data.Size - 1);
            for (int y = 0; y < data.Size; y++)
                for (int x = 0; x < data.Size; x++)
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R},{3:R},{4:R},{5:R}",
                        data.WorldCentre.x - data.WorldSize * 0.5f + x * spacing,
                        data.WorldCentre.y + data.WorldSize * 0.5f - y * spacing,
                        data.Maps[(int)TerrainReportStage.BeforeErosion][x, y], data.Maps[(int)TerrainReportStage.FinalHeight][x, y],
                        data.Maps[(int)TerrainReportStage.ErosionChange][x, y], data.Maps[(int)TerrainReportStage.DropletFlow][x, y]));
        }
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
            "Before/after heightmaps use a fixed grayscale range: black = {0:0.###}, white = {1:0.###} world units. Actual final sampled range: {2:0.###}–{3:0.###}. PNGs are 8-bit visualizations, not lossless height data. Other grayscale stages use 0–1. Values outside the display range are clipped.\n",
            generated.HeightScaleMinimum, generated.HeightScaleMaximum, generated.MinimumHeight, generated.MaximumHeight));
        result.AppendLine("Pipeline: domain-warped sampling coordinates → seeded octave noise → mountain ridge blending → world falloff → river carving → lake carving → height curve and multiplier → optional hydraulic erosion → mesh. Stage 01 disables domain warp only as a comparison; stage 02 is the actual generator input. River and lake images show carving weights, not water occupancy. World falloff is a subtraction mask, not a heightmap.\n");
        if (generated.Erosion != null)
        {
            HydraulicErosionSettings erosion = heights.erosionSettings.ValidatedCopy();
            result.AppendLine("## 3.4 Hydraulic erosion example\n");
            result.AppendLine("A deterministic, generation-time droplet model modifies the actual terrain heights. Droplets follow the height gradient with inertia, remove sediment when below carrying capacity, transport it downslope, and deposit it when capacity falls or motion ends. Water evaporates at each step. The same world-space erosion field is sampled by every terrain chunk and environmental query.\n");
            result.AppendLine($"Bake resolution: {erosion.resolution} × {erosion.resolution}; droplets: {erosion.dropletCount}; maximum steps per droplet: {erosion.maxLifetime}; effective erosion seed: {generated.Erosion.Seed}; brush radius: {erosion.brushRadius} bake cells.");
            result.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Inertia: {0}; erosion rate: {1}; deposition rate: {2}; evaporation per step: {3}; capacity factor: {4}; maximum local change: ±{5} height units. Full parameter values are in settings-snapshot.json.\n",
                erosion.inertia, erosion.erosionRate, erosion.depositionRate, erosion.evaporationRate, erosion.sedimentCapacity, erosion.maxHeightChange));
            result.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Measured region changes: minimum {0:0.######}, maximum {1:0.######}, mean absolute {2:0.######} height units. Whole-world bake took {3:0.##} ms on the generating machine; this is not a gameplay frame-time benchmark.\n",
                generated.MinimumErosionChange, generated.MaximumErosionChange, generated.MeanAbsoluteErosionChange, generated.Erosion.BakeMilliseconds));
            result.AppendLine($"Change-map legend: blue = −{erosion.maxHeightChange}, white = 0, orange = +{erosion.maxHeightChange} height units. The change is final height minus original height. Flow uses log(1 + accumulated droplet water) / log(1 + whole-bake maximum), not physical water depth or discharge.\n");
            result.AppendLine("Use **14-before-erosion-3d.png** alongside **09-matching-3d-terrain.png**. Both use identical camera framing, LOD, colors and vertical scale. Compare **11-before-erosion-heightmap.png** with **08-final-heightmap.png**, then explain **12-erosion-deposition-change.png** and **13-droplet-flow.png**. Numerical samples are in erosion-height-samples.csv.\n");
            result.AppendLine("Limits: this is a simplified heightfield model, not a calibrated geological or flood simulation. Parameters and droplet counts are not years of rainfall. Changes taper to zero at the finite bake boundary; the exterior stays unchanged. The coarse, bilinearly interpolated erosion delta preserves the original higher-frequency terrain detail. Existing river/lake masks remain procedural features; they are not recomputed as a watershed network. Regenerate the world to apply changed settings.\n");
            result.AppendLine("Algorithm reference: Sebastian Lague, Hydraulic Erosion (2019), https://github.com/SebLague/Hydraulic-Erosion. The project includes attribution and the MIT notice in HYDRAULIC_EROSION.md.\n");
        }
        else result.AppendLine("Hydraulic erosion is disabled in this snapshot. Enable the preview checkbox to generate a before/after example. Existing erosion-like shader streaks alone are appearance effects, not hydraulic simulation.\n");
        result.AppendLine("The complete serialized configuration is recorded in settings-snapshot.json. Disabled effects produce unchanged heights or black masks. Changes to settings after generation do not affect this snapshot.");
        return result.ToString();
    }
}
