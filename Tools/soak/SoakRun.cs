using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Boreal.Animals;
using Boreal.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Boreal.Soak.Editor
{
    // Batch-mode soak run: plays the Terrain scene on one seed with the run recorder on, for a number of simulated
    // years, saving a camera shot at each season's peak and a top-down map at the end, then quits.
    // Launched by Tools/run_soak.py, which copies this folder into a test copy of the project; see TESTING.md.
    [InitializeOnLoad]
    public static class SoakRun
    {
        const string Key = "SoakRun.";
        const string MapFolder = "Recordings/TopDownMap";
        const int ShotWidth = 1600, ShotHeight = 900;

        static SoakRun()
        {
            if (SessionState.GetBool(Key + "Active", false))
                EditorApplication.update += Tick;
        }

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Arg(string name, string fallback)
            {
                int i = Array.IndexOf(args, "-" + name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
            }

            string seed = Arg("soakSeed", "FOREST-001");
            string output = Path.GetFullPath(Arg("soakOutput", "SoakRuns/" + seed));
            Directory.CreateDirectory(output);
            SessionState.SetBool(Key + "Active", true);
            SessionState.SetString(Key + "Output", output);
            SessionState.SetFloat(Key + "Years", float.Parse(Arg("soakYears", "5"), CultureInfo.InvariantCulture));
            SessionState.SetBool(Key + "Maps", Arg("soakMaps", "1") == "1");
            SessionState.SetFloat(Key + "Deadline", (float)(EditorApplication.timeSinceStartup +
                60.0 * float.Parse(Arg("soakMinutes", "40"), CultureInfo.InvariantCulture)));
            SessionState.SetInt(Key + "Stage", 0);
            SessionState.SetBool(Key + "SeasonsOn", Arg("soakSeasons", "1") == "1");
            SessionState.SetInt(Key + "LastPeak", -1);
            SessionState.SetFloat(Key + "LastStatus", -1f);

            EditorSceneManager.OpenScene("Assets/_Max/Scenes/Terrain.unity");
            SeedManager seedManager = Object.FindAnyObjectByType<SeedManager>();
            SerializedObject seedObject = new SerializedObject(seedManager);
            seedObject.FindProperty("seedString").stringValue = seed;
            seedObject.ApplyModifiedPropertiesWithoutUndo();

            SpeciesManager manager = Object.FindAnyObjectByType<SpeciesManager>();
            // Starts at 1x: world generation runs below 10 FPS in batch mode, which would trip the safety pause.
            manager.simulationSpeed = 1f;
            SessionState.SetFloat(Key + "Speed", float.Parse(Arg("soakSpeed", "50"), CultureInfo.InvariantCulture));
            SessionState.SetBool(Key + "Fast", false);
            SimulationRunRecorder recorder = manager.gameObject.AddComponent<SimulationRunRecorder>();
            recorder.sourceRevision = Arg("soakRevision", "unrecorded");
            recorder.experiment = "soak " + seed;

            Debug.Log($"Soak: starting {seed} for {SessionState.GetFloat(Key + "Years", 0f)} years, output {output}");
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "Deadline", float.MaxValue))
            {
                Finish("timed out");
                return;
            }

            int stage = SessionState.GetInt(Key + "Stage", 0);
            if (!EditorApplication.isPlaying)
            {
                if (stage >= 3 && !EditorApplication.isPlayingOrWillChangePlaymode)
                    Quit();
                return;
            }

            SeasonClock clock = SeasonClock.Active;
            if (clock == null)
                return;

            if (!SessionState.GetBool(Key + "SeasonsOn", true) && clock.seasonsOn)
            {
                clock.seasonsOn = false;
                Debug.Log("Soak: seasons switched off for this run");
            }

            float yearsSinceStart = clock.Year - 1 + clock.YearProgress;
            SeekFood[] animals = Object.FindObjectsByType<SeekFood>(FindObjectsSortMode.None);
            SpeciesManager manager = SpeciesManager.Instance;
            float simulated = yearsSinceStart * clock.yearLength;

            if (simulated - SessionState.GetFloat(Key + "LastStatus", -1f) >= 50f)
            {
                SessionState.SetFloat(Key + "LastStatus", simulated);
                Debug.Log($"Soak: t={simulated:0} s year {clock.Year} {clock.SeasonName}, animals {animals.Length}, " +
                          $"species {(manager != null ? manager.SpeciesAliveCount : -1)}, " +
                          $"achieved speed {(manager != null ? manager.AchievedSimulationSpeed : 0f):0.0}x");
            }

            if (stage == 0 && animals.Length > 0 && !SessionState.GetBool(Key + "Fast", false) && manager != null)
            {
                // The step per frame stays capped at high speed, so a slow frame rate only lowers the speed reached.
                SessionState.SetBool(Key + "Fast", true);
                manager.enableSafetyPause = false;
                manager.simulationSpeed = SessionState.GetFloat(Key + "Speed", 50f);
                Debug.Log($"Soak: {animals.Length} founders at t={simulated:0} s, speeding up to {manager.simulationSpeed}x");
            }

            if (stage == 0)
            {
                int peak = Mathf.FloorToInt(yearsSinceStart * 4f + 0.5f);
                if (peak != SessionState.GetInt(Key + "LastPeak", -1))
                {
                    SessionState.SetInt(Key + "LastPeak", peak);
                    string label = $"y{clock.Year}-{peak:00}-{clock.SeasonName.Replace(' ', '-').ToLowerInvariant()}";
                    Shot(Camera.main, Path.Combine(Out, label + "-camera.png"));
                    ShotOverAnimals(animals, Path.Combine(Out, label + "-animals.png"));
                }

                if (yearsSinceStart >= SessionState.GetFloat(Key + "Years", 5f) || animals.Length == 0 && SessionState.GetBool(Key + "Fast", false))
                {
                    Debug.Log(animals.Length == 0 ? "Soak: every animal has died." : "Soak: reached the target years.");
                    SimulationRunRecorder recorder = Object.FindAnyObjectByType<SimulationRunRecorder>();
                    if (recorder != null) SessionState.SetString(Key + "Recording", recorder.OutputDirectory ?? "");
                    if (manager != null) manager.simulationSpeed = 1f;
                    stage = SessionState.GetBool(Key + "Maps", true) ? 1 : 3;
                    SessionState.SetInt(Key + "Stage", stage);
                    if (stage == 1) RequestMap("");
                    else EditorApplication.ExitPlaymode();
                }
            }
            else if (stage == 1 || stage == 2)
            {
                // Waits for the map tool to save each map, then asks for the heatmap version, then leaves Play mode.
                string[] maps = Directory.Exists(MapFolder) ? Directory.GetFiles(MapFolder, "*.png") : new string[0];
                string newest = maps.Where(p => File.GetLastWriteTimeUtc(p) > MapRequestedUtc)
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (newest == null || (DateTime.UtcNow - File.GetLastWriteTimeUtc(newest)).TotalSeconds < 3)
                    return;
                File.Copy(newest, Path.Combine(Out, (stage == 1 ? "map-" : "map-heatmap-") + Path.GetFileName(newest)), true);
                string caption = Path.ChangeExtension(newest, ".txt");
                if (File.Exists(caption))
                    File.Copy(caption, Path.Combine(Out, (stage == 1 ? "map-" : "map-heatmap-") + Path.GetFileName(caption)), true);
                SessionState.SetInt(Key + "Stage", stage + 1);
                if (stage == 1) RequestMap("heatmap");
                else EditorApplication.ExitPlaymode();
            }
        }

        static string Out => SessionState.GetString(Key + "Output", "SoakRuns");

        static DateTime MapRequestedUtc =>
            new DateTime(long.Parse(SessionState.GetString(Key + "MapRequested", "0")), DateTimeKind.Utc);

        static void RequestMap(string kind)
        {
            SessionState.SetString(Key + "MapRequested", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/TopDownMapCapture.request", kind);
            Debug.Log("Soak: requested top-down map " + kind);
        }

        static void ShotOverAnimals(SeekFood[] animals, string path)
        {
            if (animals.Length == 0 || Camera.main == null)
                return;
            // Looks down at 50 degrees on the middle of the largest group: the animal with the most neighbours within 60 m.
            Vector3 centre = animals.Select(a => a.transform.position)
                .OrderByDescending(p => animals.Count(b => (b.transform.position - p).sqrMagnitude < 3600f)).First();
            GameObject holder = new GameObject("Soak Camera");
            Camera camera = holder.AddComponent<Camera>();
            camera.CopyFrom(Camera.main);
            holder.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            holder.transform.position = centre - holder.transform.forward * 70f;
            Shot(camera, path);
            Object.DestroyImmediate(holder);
        }

        static void Shot(Camera camera, string path)
        {
            if (camera == null)
                return;
            RenderTexture target = RenderTexture.GetTemporary(ShotWidth, ShotHeight, 24);
            RenderTexture previousTarget = camera.targetTexture;
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = previousTarget;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D image = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
            image.Apply();
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target);
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }

        static void Finish(string reason)
        {
            Debug.Log("Soak: " + reason);
            SessionState.SetInt(Key + "Stage", 3);
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            else Quit();
        }

        static void Quit()
        {
            EditorApplication.update -= Tick;
            string recording = SessionState.GetString(Key + "Recording", "");
            if (!string.IsNullOrEmpty(recording) && Directory.Exists(recording))
            {
                string target = Path.Combine(Out, "recording");
                Directory.CreateDirectory(target);
                foreach (string file in Directory.GetFiles(recording))
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            }
            SessionState.SetBool(Key + "Active", false);
            Debug.Log("Soak: done, output " + Out);
            EditorApplication.Exit(0);
        }
    }
}
