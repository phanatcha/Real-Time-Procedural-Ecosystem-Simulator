using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Boreal.Core;
using Boreal.TerrainGeneration;
using Boreal.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Boreal.Animals.Editor
{
    // Renders the whole world from straight above into one large PNG, for slides and reports. The game only loads
    // terrain within 800 m of the camera and fades it into fog, so this builds its own full-detail copy of every chunk
    // (terrain, water and vegetation, with the world's own materials), renders it in tiles with an orthographic camera
    // and stitches them. The menu items enter Play mode when needed and leave it again afterwards. While the tiles
    // render (a single frame), fog, the atmosphere, the vignette and bloom, and the game's own terrain are switched
    // off; everything is put back straight after. Animals and plant food (the Wildlife layer) are left out. The
    // heatmap versions turn on the survivability heatmap, as its toggle does, and wait for its map first; the bare one
    // leaves out the trees, grass and rocks so they don't hide it. The PNG and a caption file go to
    // Recordings/TopDownMap, which git ignores.
    [InitializeOnLoad]
    public static class TopDownMapCapture
    {
        const string MenuPath = "Tools/Ecosystem/Capture Top-Down Map";
        const string HeatmapMenuPath = "Tools/Ecosystem/Capture Top-Down Map With Heatmap";
        const string BareHeatmapMenuPath = "Tools/Ecosystem/Capture Top-Down Map With Heatmap, No Plants";
        const string PendingKey = "TopDownMapCapture.Pending";
        // Written by scripts that want a capture without clicking the menu; containing "heatmap" turns the heatmap on
        // and "noplants" leaves out the vegetation. The editor checks for it every second.
        const string RequestFile = "Temp/TopDownMapCapture.request";
        const string OutputFolder = "Recordings/TopDownMap";

        const int OutputSize = 8192;
        // Small tiles, because the water calms its waves with distance from the camera and big tiles would show it.
        const int TilesPerSide = 16;
        const int Supersampling = 2;
        // Output pixels rendered around each tile and thrown away, so screen-space effects (SSAO) leave no seams.
        const int TilePadding = 32;
        // Chunks loaded around the origin when the world has no falloff edge.
        const int UnboundedChunkRadius = 8;
        const int MaxLoadingChunks = 64;
        const double WorldTimeout = 60.0;
        // Chunks still loading after this long without any finishing are left out.
        const double StallTimeout = 60.0;
        const double HeatmapTimeout = 120.0;
        // Sea kept around the land, as a share of its width.
        const float Margin = 0.06f;

        static Capture running;

        static TopDownMapCapture()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            // A capture interrupted by a script reload leaves its progress bar behind.
            EditorUtility.ClearProgressBar();
            EditorApplication.update += CheckForRequest;
        }

        static double nextRequestCheck;

        static void CheckForRequest()
        {
            if (running != null || EditorApplication.timeSinceStartup < nextRequestCheck)
                return;
            nextRequestCheck = EditorApplication.timeSinceStartup + 1.0;
            if (!File.Exists(RequestFile))
                return;
            string request = File.ReadAllText(RequestFile);
            File.Delete(RequestFile);
            Run(false, request.Contains("heatmap"), !request.Contains("noplants"));
        }

        [MenuItem(MenuPath)]
        static void RunFromMenu()
        {
            Run(true, false, true);
        }

        [MenuItem(HeatmapMenuPath)]
        static void RunWithHeatmapFromMenu()
        {
            Run(true, true, true);
        }

        [MenuItem(BareHeatmapMenuPath)]
        static void RunWithBareHeatmapFromMenu()
        {
            Run(true, true, false);
        }

        [MenuItem(MenuPath, true)]
        [MenuItem(HeatmapMenuPath, true)]
        [MenuItem(BareHeatmapMenuPath, true)]
        static bool CanRun()
        {
            return running == null;
        }

        static void Run(bool revealWhenDone, bool showHeatmap, bool showPlants)
        {
            if (running != null)
                return;

            SessionState.SetBool(PendingKey + ".Reveal", revealWhenDone);
            SessionState.SetBool(PendingKey + ".Heatmap", showHeatmap);
            SessionState.SetBool(PendingKey + ".Plants", showPlants);
            if (EditorApplication.isPlaying)
            {
                if (WorldIsIntact())
                {
                    running = new Capture(false, revealWhenDone, showHeatmap, showPlants);
                    return;
                }

                // Scripts were recompiled while playing, which loses the chunk loader and the water surface.
                // Play the scene again from the start, and leave it playing afterwards as it was.
                Debug.Log("Top-down map: scripts were reloaded during Play mode, so Play mode restarts first.");
                SessionState.SetBool(PendingKey, true);
                SessionState.SetBool(PendingKey + ".Restart", true);
                SessionState.SetBool(PendingKey + ".Exit", false);
                EditorApplication.ExitPlaymode();
                return;
            }

            SessionState.SetBool(PendingKey, true);
            SessionState.SetBool(PendingKey + ".Exit", true);
            EditorApplication.EnterPlaymode();
        }

        static bool WorldIsIntact()
        {
            FieldInfo requester = typeof(ThreadedDataRequester).GetField("instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (requester != null && requester.GetValue(null) == null)
                return false;

            TerrainGenerator generator = Object.FindAnyObjectByType<TerrainGenerator>();
            return generator == null || !generator.IsGenerated || generator.textureSettings == null
                || generator.textureSettings.waterMaterial == null || generator.HasWaterSurface;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(PendingKey + ".Restart", false))
            {
                SessionState.EraseBool(PendingKey + ".Restart");
                EditorApplication.delayCall += EditorApplication.EnterPlaymode;
            }
            else if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
            {
                bool reveal = SessionState.GetBool(PendingKey + ".Reveal", false);
                bool exit = SessionState.GetBool(PendingKey + ".Exit", true);
                bool heatmap = SessionState.GetBool(PendingKey + ".Heatmap", false);
                bool plants = SessionState.GetBool(PendingKey + ".Plants", true);
                SessionState.EraseBool(PendingKey);
                SessionState.EraseBool(PendingKey + ".Reveal");
                SessionState.EraseBool(PendingKey + ".Exit");
                SessionState.EraseBool(PendingKey + ".Heatmap");
                SessionState.EraseBool(PendingKey + ".Plants");
                running = new Capture(exit, reveal, heatmap, plants);
            }
            else if (change == PlayModeStateChange.ExitingPlayMode && running != null)
            {
                running.Stop("Play mode ended before the capture finished.");
            }
        }

        sealed class Capture
        {
            enum Phase { WaitingForWorld, Loading, WaitingForHeatmap, Done }

            // SurvivabilityHeatmap keeps these private; its toggle calls SetShown.
            static readonly MethodInfo SetHeatmapShown = typeof(SurvivabilityHeatmap).GetMethod("SetShown",
                BindingFlags.Instance | BindingFlags.NonPublic);
            static readonly FieldInfo HeatmapShown = typeof(SurvivabilityHeatmap).GetField("shown",
                BindingFlags.Instance | BindingFlags.NonPublic);
            static readonly FieldInfo HeatmapHasMap = typeof(SurvivabilityHeatmap).GetField("hasMap",
                BindingFlags.Instance | BindingFlags.NonPublic);

            readonly bool exitPlayModeAfter;
            readonly bool revealWhenDone;
            readonly bool showHeatmap;
            readonly bool showPlants;
            readonly double startTime;
            readonly List<TerrainChunk> chunks = new List<TerrainChunk>();
            readonly Queue<TerrainChunk> waiting = new Queue<TerrainChunk>();
            readonly List<TerrainChunk> loading = new List<TerrainChunk>();
            Phase phase;
            double lastProgressTime;
            double heatmapStartTime;
            int chunkRadius;
            TerrainGenerator generator;
            GameObject root;
            SurvivabilityHeatmap heatmap;
            bool heatmapWasShown;

            public Capture(bool exitPlayModeAfter, bool revealWhenDone, bool showHeatmap, bool showPlants)
            {
                this.exitPlayModeAfter = exitPlayModeAfter;
                this.revealWhenDone = revealWhenDone;
                this.showHeatmap = showHeatmap;
                this.showPlants = showPlants;
                startTime = EditorApplication.timeSinceStartup;
                EditorApplication.update += Update;
                Debug.Log("Top-down map: waiting for the world to generate.");
            }

            void Update()
            {
                try
                {
                    if (phase == Phase.WaitingForWorld)
                        WaitForWorld();
                    else if (phase == Phase.Loading)
                        LoadChunks();
                    else if (phase == Phase.WaitingForHeatmap)
                        WaitForHeatmap();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    Stop("The capture failed.");
                }
            }

            void WaitForWorld()
            {
                if (EditorApplication.timeSinceStartup - startTime > WorldTimeout)
                {
                    Stop("No generated TerrainGenerator was found. Open the Terrain scene and try again.");
                    return;
                }

                if (generator == null)
                    generator = Object.FindAnyObjectByType<TerrainGenerator>();
                // A moment after generation, so the game's own first chunks are not competing for threads.
                if (generator == null || !generator.IsGenerated || EditorApplication.timeSinceStartup - startTime < 2.0)
                    return;

                // The falloff radius is in noise samples, each meshScale metres apart. Past it everything is sea, so one
                // more chunk all round leaves a strip of sea to frame the land with.
                float chunkSize = generator.meshSettings.meshWorldSize;
                HeightMapSettings heights = generator.heightMapSettings;
                chunkRadius = heights.useFalloff
                    ? Mathf.CeilToInt(heights.worldRadius * generator.meshSettings.meshScale / chunkSize + 0.5f) + 1
                    : UnboundedChunkRadius;
                float loadedHalf = (chunkRadius + 0.5f) * chunkSize;

                root = new GameObject("Top-Down Map Terrain");
                Transform viewer = new GameObject("Viewer").transform;
                viewer.SetParent(root.transform, false);
                // Outside every chunk, so none of them builds a collider. Each chunk still draws its full-detail mesh,
                // because the first detail level ends before any distance and the last one reaches past all of them.
                viewer.position = new Vector3(0f, 0f, loadedHalf + 100f);
                LODInfo[] detailLevels =
                {
                    new LODInfo { lod = MeshSettings.numSupportedLODs - 1, visibleDstThreshold = -1f },
                    new LODInfo { lod = 0, visibleDstThreshold = 3f * loadedHalf + 1000f }
                };

                WaterSurface water = typeof(TerrainGenerator)
                    .GetField("waterSurface", BindingFlags.Instance | BindingFlags.NonPublic)?
                    .GetValue(generator) as WaterSurface;

                for (int y = -chunkRadius; y <= chunkRadius; y++)
                {
                    for (int x = -chunkRadius; x <= chunkRadius; x++)
                    {
                        TerrainChunk chunk = new TerrainChunk(new Vector2Int(x, y), generator.heightMapSettings,
                            generator.meshSettings, detailLevels, 0, root.transform, viewer, generator.mapMaterial,
                            // Chunks without vegetation settings never request any.
                            showPlants ? generator.vegetationSettings : null,
                            showPlants ? generator.vegetationMaterial : null, generator.EnvironmentDefinitions,
                            generator.WorldEnvironmentSampler);
                        if (water != null)
                            chunk.onRenderMeshReady += water.AddTileIfNeeded;
                        chunks.Add(chunk);
                        waiting.Enqueue(chunk);
                    }
                }

                phase = Phase.Loading;
                lastProgressTime = EditorApplication.timeSinceStartup;
                Debug.Log($"Top-down map: loading {chunks.Count} chunks at full detail.");
            }

            void LoadChunks()
            {
                if (loading.RemoveAll(IsReady) > 0)
                    lastProgressTime = EditorApplication.timeSinceStartup;
                while (loading.Count < MaxLoadingChunks && waiting.Count > 0)
                {
                    TerrainChunk chunk = waiting.Dequeue();
                    chunk.Load();
                    loading.Add(chunk);
                }

                int ready = chunks.Count - waiting.Count - loading.Count;
                if (EditorUtility.DisplayCancelableProgressBar("Top-down map",
                        $"Loading terrain chunks ({ready} of {chunks.Count})", 0.85f * ready / chunks.Count))
                {
                    Stop("Cancelled.");
                    return;
                }

                bool stalled = EditorApplication.timeSinceStartup - lastProgressTime > StallTimeout;
                if (loading.Count > 0 && !stalled)
                    return;

                if (loading.Count > 0)
                    Debug.LogWarning($"Top-down map: {loading.Count + waiting.Count} chunks did not finish loading " +
                                     "and may be missing.");

                if (showHeatmap)
                {
                    heatmap = Object.FindAnyObjectByType<SurvivabilityHeatmap>();
                    if (heatmap != null && SetHeatmapShown != null && HeatmapShown != null && HeatmapHasMap != null)
                    {
                        heatmapWasShown = (bool)HeatmapShown.GetValue(heatmap);
                        SetHeatmapShown.Invoke(heatmap, new object[] { true });
                        heatmapStartTime = EditorApplication.timeSinceStartup;
                        phase = Phase.WaitingForHeatmap;
                        return;
                    }
                    heatmap = null;
                    Debug.LogWarning("Top-down map: no survivability heatmap was found, so the map has none.");
                }

                Finish();
            }

            void WaitForHeatmap()
            {
                double waited = EditorApplication.timeSinceStartup - heatmapStartTime;
                if (EditorUtility.DisplayCancelableProgressBar("Top-down map", "Waiting for the heatmap", 0.88f))
                {
                    Stop("Cancelled.");
                    return;
                }

                // The heatmap fades in over a quarter of a second once shown, and hasMap means its texture is complete.
                bool hasMap = (bool)HeatmapHasMap.GetValue(heatmap);
                if (hasMap ? waited < 1.5 : waited < HeatmapTimeout)
                    return;
                if (!hasMap)
                    Debug.LogWarning("Top-down map: the heatmap had not finished, so parts of it may be missing.");

                Finish();
            }

            void Finish()
            {
                phase = Phase.Done;
                string path = Render();
                Stop(null);
                Debug.Log($"Top-down map: saved {Path.GetFullPath(path)}");
                if (revealWhenDone)
                    EditorUtility.RevealInFinder(path);
            }

            bool IsReady(TerrainChunk chunk)
            {
                if (chunk.CurrentMesh == null)
                    return false;
                if (!showPlants || generator.vegetationSettings == null || generator.vegetationMaterial == null)
                    return true;

                // Vegetation layers are renamed with their instance counts once their meshes are built.
                Transform vegetation = chunk.GameObject.transform.Find("Vegetation");
                return vegetation != null && vegetation.childCount > 0 && vegetation.GetChild(0).name.Contains("(");
            }

            string Render()
            {
                // The land: every mesh point above the water, plus the height range for the camera.
                float waterLevel = generator.WaterLevelHeight;
                Vector2 landMin = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 landMax = new Vector2(float.MinValue, float.MinValue);
                float lowest = float.MaxValue;
                float highest = float.MinValue;
                foreach (TerrainChunk chunk in chunks)
                {
                    Mesh mesh = chunk.CurrentMesh;
                    if (mesh == null)
                        continue;
                    Vector3 origin = chunk.GameObject.transform.position;
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Vector3 point = origin + vertex;
                        lowest = Mathf.Min(lowest, point.y);
                        highest = Mathf.Max(highest, point.y);
                        if (point.y <= waterLevel)
                            continue;
                        landMin = Vector2.Min(landMin, new Vector2(point.x, point.z));
                        landMax = Vector2.Max(landMax, new Vector2(point.x, point.z));
                    }
                }

                float chunkSize = generator.meshSettings.meshWorldSize;
                float loadedHalf = (chunkRadius + 0.5f) * chunkSize;
                Vector2 centre = Vector2.zero;
                float side = 2f * loadedHalf;
                if (landMax.x > landMin.x)
                {
                    centre = (landMin + landMax) * 0.5f;
                    Vector2 extent = landMax - landMin;
                    side = Mathf.Max(extent.x, extent.y) * (1f + 2f * Margin);
                }
                float limit = loadedHalf - side / OutputSize * TilePadding;
                side = Mathf.Min(side, 2f * limit);
                centre.x = Mathf.Clamp(centre.x, -limit + side * 0.5f, limit - side * 0.5f);
                centre.y = Mathf.Clamp(centre.y, -limit + side * 0.5f, limit - side * 0.5f);

                int tilePixels = OutputSize / TilesPerSide;
                int renderPixels = (tilePixels + 2 * TilePadding) * Supersampling;
                float metresPerPixel = side / OutputSize;
                float tileWorld = side / TilesPerSide;
                // High enough that the shallow-water caustics, which fade out by 160 m from the camera, are off
                // everywhere instead of only at the tile edges.
                float cameraHeight = highest + 200f;

                GameObject cameraObject = new GameObject("Top-Down Map Camera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 0.5f * (tilePixels + 2 * TilePadding) * metresPerPixel;
                camera.nearClipPlane = 1f;
                camera.farClipPlane = cameraHeight - lowest + 20f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = RenderSettings.fogColor;
                camera.cullingMask = ~LayerMaskOf(WildlifeLayer.Name, "UI");
                camera.allowHDR = true;
                camera.allowMSAA = false;
                camera.useOcclusionCulling = false;
                cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.renderPostProcessing = true;
                cameraData.antialiasing = AntialiasingMode.None;
                cameraData.renderShadows = true;
                cameraData.requiresDepthOption = CameraOverrideOption.On;
                cameraData.requiresColorOption = CameraOverrideOption.On;

                RenderTexture target = new RenderTexture(renderPixels, renderPixels, 24, RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB);
                target.Create();
                camera.targetTexture = target;
                camera.aspect = 1f;
                Texture2D readback = new Texture2D(renderPixels, renderPixels, TextureFormat.RGBA32, false, false);
                byte[] output = new byte[OutputSize * OutputSize * 3];

                // Vignette and bloom are screen effects, so each tile would get its own dark corners and glow.
                VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.Add<Vignette>().intensity.Override(0f);
                profile.Add<Bloom>().intensity.Override(0f);
                profile.Add<ChromaticAberration>().intensity.Override(0f);
                profile.Add<FilmGrain>().intensity.Override(0f);
                profile.Add<LensDistortion>().intensity.Override(0f);
                profile.Add<MotionBlur>().intensity.Override(0f);
                profile.Add<DepthOfField>().mode.Override(DepthOfFieldMode.Off);
                profile.Add<PaniniProjection>().distance.Override(0f);
                GameObject volumeObject = new GameObject("Top-Down Map Volume");
                Volume volume = volumeObject.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10000f;
                volume.sharedProfile = profile;

                bool fog = RenderSettings.fog;
                UniversalRenderPipelineAsset pipeline = UniversalRenderPipeline.asset;
                float shadowDistance = pipeline != null ? pipeline.shadowDistance : 0f;
                int shadowCascades = pipeline != null ? pipeline.shadowCascadeCount : 0;
                List<Renderer> hidden = new List<Renderer>();
                List<AtmosphericFog> atmospheres = new List<AtmosphericFog>();
                RenderTexture previousActive = RenderTexture.active;
                try
                {
                    RenderSettings.fog = false;
                    // The atmosphere hides everything more than 790 m from the camera, which is all of it from up here.
                    foreach (AtmosphericFog atmosphere in Object.FindObjectsByType<AtmosphericFog>(FindObjectsSortMode.None))
                    {
                        if (!atmosphere.enabled)
                            continue;
                        atmosphere.enabled = false;
                        atmospheres.Add(atmosphere);
                    }
                    if (pipeline != null)
                    {
                        // Shadows normally stop 50 m from the camera, which is far below this one.
                        pipeline.shadowDistance = camera.farClipPlane;
                        pipeline.shadowCascadeCount = 1;
                    }
                    foreach (Renderer renderer in generator.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!renderer.enabled)
                            continue;
                        renderer.enabled = false;
                        hidden.Add(renderer);
                    }

                    // Every tile renders in this one frame, so the animated water has the same waves across seams.
                    float originX = centre.x - side * 0.5f;
                    float originZ = centre.y - side * 0.5f;
                    int samples = Supersampling * Supersampling;
                    for (int tileY = 0; tileY < TilesPerSide; tileY++)
                    {
                        for (int tileX = 0; tileX < TilesPerSide; tileX++)
                        {
                            EditorUtility.DisplayProgressBar("Top-down map",
                                $"Rendering tile {tileY * TilesPerSide + tileX + 1} of {TilesPerSide * TilesPerSide}",
                                0.9f + 0.08f * (tileY * TilesPerSide + tileX) / (TilesPerSide * TilesPerSide));
                            cameraObject.transform.position = new Vector3(originX + (tileX + 0.5f) * tileWorld,
                                cameraHeight, originZ + (tileY + 0.5f) * tileWorld);
                            camera.Render();
                            RenderTexture.active = target;
                            readback.ReadPixels(new Rect(0, 0, renderPixels, renderPixels), 0, 0, false);
                            RenderTexture.active = previousActive;
                            Color32[] source = readback.GetPixels32();

                            // Crop the padding and average each block of supersamples into one output pixel.
                            for (int v = 0; v < tilePixels; v++)
                            {
                                int outputRow = (tileY * tilePixels + v) * OutputSize + tileX * tilePixels;
                                for (int u = 0; u < tilePixels; u++)
                                {
                                    int red = 0, green = 0, blue = 0;
                                    for (int b = 0; b < Supersampling; b++)
                                    {
                                        int sourceRow = ((TilePadding + v) * Supersampling + b) * renderPixels;
                                        for (int a = 0; a < Supersampling; a++)
                                        {
                                            Color32 sample = source[sourceRow + (TilePadding + u) * Supersampling + a];
                                            red += sample.r;
                                            green += sample.g;
                                            blue += sample.b;
                                        }
                                    }
                                    int index = (outputRow + u) * 3;
                                    output[index] = (byte)((red + samples / 2) / samples);
                                    output[index + 1] = (byte)((green + samples / 2) / samples);
                                    output[index + 2] = (byte)((blue + samples / 2) / samples);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    RenderTexture.active = previousActive;
                    foreach (Renderer renderer in hidden)
                        if (renderer != null)
                            renderer.enabled = true;
                    foreach (AtmosphericFog atmosphere in atmospheres)
                        if (atmosphere != null)
                            atmosphere.enabled = true;
                    RenderSettings.fog = fog;
                    if (pipeline != null)
                    {
                        pipeline.shadowDistance = shadowDistance;
                        pipeline.shadowCascadeCount = shadowCascades;
                    }
                    camera.targetTexture = null;
                    Object.DestroyImmediate(cameraObject);
                    Object.DestroyImmediate(volumeObject);
                    Object.DestroyImmediate(profile);
                    target.Release();
                    Object.DestroyImmediate(target);
                    Object.DestroyImmediate(readback);
                }

                EditorUtility.DisplayProgressBar("Top-down map", "Saving the PNG", 0.99f);
                Texture2D image = new Texture2D(OutputSize, OutputSize, TextureFormat.RGB24, false, false);
                byte[] png;
                try
                {
                    image.SetPixelData(output, 0);
                    png = image.EncodeToPNG();
                }
                finally
                {
                    Object.DestroyImmediate(image);
                }

                string seed = SeedManager.Instance != null ? SeedManager.Instance.SeedString : "unknown seed";
                string safeSeed = string.Join("_", seed.Split(Path.GetInvalidFileNameChars()));
                string kind = (heatmap != null ? "-heatmap" : "") + (showPlants ? "" : "-no-plants");
                string name = $"top-down-map{kind}-{safeSeed}-{DateTime.Now:yyyyMMdd-HHmmss}";
                Directory.CreateDirectory(OutputFolder);
                string path = Path.Combine(OutputFolder, name + ".png");
                File.WriteAllBytes(path, png);
                File.WriteAllText(Path.Combine(OutputFolder, name + ".txt"),
                    $"Top-down view of the whole world, world seed {seed}.\n" +
                    $"Area: {side:F0} m x {side:F0} m, centred on x = {centre.x:F0} m, z = {centre.y:F0} m.\n" +
                    $"Land spans x {landMin.x:F0} to {landMax.x:F0} m and z {landMin.y:F0} to {landMax.y:F0} m.\n" +
                    $"Scale: {metresPerPixel:F3} m per pixel ({OutputSize} x {OutputSize} px). North (+Z) is up, east (+X) is right.\n" +
                    (heatmap != null
                        ? $"Survivability heatmap shown ({heatmap.coverage} coverage, {heatmap.opacity:P0} opacity): " +
                          "red is harsh, green is lenient.\n"
                        : "") +
                    (showPlants
                        ? "Rendered with the game's terrain, water and vegetation materials; "
                        : "Rendered with the game's terrain and water materials; trees, grass and rocks are left out, ") +
                    "animals and plant food are hidden, and fog, the atmosphere, vignette and bloom are off.\n");
                return path;
            }

            static int LayerMaskOf(params string[] layerNames)
            {
                int mask = 0;
                foreach (string layerName in layerNames)
                {
                    int layer = LayerMask.NameToLayer(layerName);
                    if (layer >= 0)
                        mask |= 1 << layer;
                }
                return mask;
            }

            public void Stop(string problem)
            {
                EditorApplication.update -= Update;
                EditorUtility.ClearProgressBar();
                if (heatmap != null && !heatmapWasShown)
                    SetHeatmapShown.Invoke(heatmap, new object[] { false });
                if (root != null)
                {
                    Object.Destroy(root);
                    Resources.UnloadUnusedAssets();
                }
                if (problem != null)
                    Debug.LogWarning("Top-down map: " + problem);
                running = null;
                if (exitPlayModeAfter && EditorApplication.isPlaying)
                    EditorApplication.delayCall += EditorApplication.ExitPlaymode;
            }
        }
    }
}
