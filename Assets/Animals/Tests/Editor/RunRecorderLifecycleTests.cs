using Boreal.World;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Boreal.Animals.Tests
{
    public class RunRecorderLifecycleTests
    {
        readonly List<string> directories = new List<string>();
        GameObject owner;

        [UnityTest]
        public IEnumerator ClockPublishesToWorldAndClearsStateWhenDisabled()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);
            owner = new GameObject("Season bridge test");
            var manager = owner.AddComponent<SpeciesManager>();
            manager.ResetSimulation();
            var clock = owner.AddComponent<SeasonClock>();
            clock.easeInYears = 0f;
            clock.startOfYear = 0.75f;
            clock.logSeasonSummaries = false;
            yield return null;
            yield return null;
            Assert.Less(WorldSeason.Current.temperatureOffset, 0f);
            Assert.AreEqual(SeasonClock.Current.temperatureOffset, WorldSeason.Current.temperatureOffset);
            clock.seasonsOn = false;
            yield return null;
            yield return null;
            Assert.AreEqual(0f, WorldSeason.Current.temperatureOffset);
            clock.seasonsOn = true;
            yield return null;
            yield return null;
            Assert.Less(WorldSeason.Current.temperatureOffset, 0f);
            clock.enabled = false;
            Assert.AreEqual(0f, WorldSeason.Current.temperatureOffset);
            Assert.AreEqual(1f, WorldSeason.Current.growthMultiplier);
        }

        [UnityTest]
        public IEnumerator PauseResetAndDisableKeepSeparateFlushedRuns()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);
            owner = new GameObject("Recorder lifecycle test");
            var manager = owner.AddComponent<SpeciesManager>();
            manager.simulationSpeed = 1;
            var recorder = owner.AddComponent<SimulationRunRecorder>();
            recorder.sampleInterval = 0.1f;
            recorder.sourceRevision = "test-revision";
            recorder.experiment = "lifecycle";
            yield return null;
            yield return null;
            string first = Remember(recorder.OutputDirectory);
            string csv = Path.Combine(first, "population.csv");
            Assert.GreaterOrEqual(File.ReadAllLines(csv).Length, 2);
            StringAssert.Contains("test-revision", File.ReadAllText(Path.Combine(first, "metadata.json")));

            manager.simulationSpeed = 0;
            yield return null;
            yield return null;
            int pausedRows = File.ReadAllLines(csv).Length;
            for (int frame = 0; frame < 10; frame++) yield return null;
            Assert.AreEqual(pausedRows, File.ReadAllLines(csv).Length, "Paused time must not create observations.");

            manager.simulationSpeed = 5;
            // Edit Mode UnityTest enumerators do not implement WaitForSeconds, even after EnterPlayMode.
            float deadline = Time.realtimeSinceStartup + 10f;
            while (File.ReadAllLines(csv).Length <= pausedRows && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.Greater(File.ReadAllLines(csv).Length, pausedRows);
            int beforeReset = File.ReadAllLines(csv).Length;
            manager.ResetSimulation();
            yield return null;
            yield return null;
            string second = Remember(recorder.OutputDirectory);
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(beforeReset, File.ReadAllLines(csv).Length, "The new world must not append to the old run.");
            Assert.AreEqual("world reset", File.ReadAllText(Path.Combine(first, "ended.txt")));

            recorder.enabled = false;
            Assert.IsTrue(File.Exists(Path.Combine(second, "ended.txt")));
            // Exclusive access proves that the CSV streams were closed, not just flushed.
            foreach (string name in new[] { "population.csv", "species.csv", "traits.csv" })
                using (File.Open(Path.Combine(second, name), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            recorder.enabled = true;
            yield return null;
            yield return null;
            Assert.AreNotEqual(second, Remember(recorder.OutputDirectory));
        }

        [UnityTest]
        public IEnumerator RepeatedWorldResetsWhilePausedAlwaysStartNewRuns()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);
            owner = new GameObject("Paused reset test");
            var manager = owner.AddComponent<SpeciesManager>();
            manager.simulationSpeed = 0;
            var recorder = owner.AddComponent<SimulationRunRecorder>();
            yield return null;
            yield return null;
            string previous = Remember(recorder.OutputDirectory);
            for (int reset = 0; reset < 2; reset++)
            {
                manager.ResetSimulation();
                yield return null;
                yield return null;
                string current = Remember(recorder.OutputDirectory);
                Assert.AreNotEqual(previous, current, "Regeneration must start a run even when simulated time stays fixed.");
                Assert.AreEqual("world reset", File.ReadAllText(Path.Combine(previous, "ended.txt")));
                previous = current;
            }
        }

        [UnityTest]
        public IEnumerator QuitCallbackFinalizesFilesWithoutDuplicatingOnDisable()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);
            owner = new GameObject("Recorder quit test");
            owner.AddComponent<SpeciesManager>();
            var recorder = owner.AddComponent<SimulationRunRecorder>();
            yield return null;
            yield return null;
            string directory = Remember(recorder.OutputDirectory);
            recorder.SendMessage("OnApplicationQuit");
            string csv = File.ReadAllText(Path.Combine(directory, "population.csv"));
            recorder.enabled = false;
            Assert.AreEqual("application quit", File.ReadAllText(Path.Combine(directory, "ended.txt")));
            Assert.AreEqual(csv, File.ReadAllText(Path.Combine(directory, "population.csv")));
        }

        string Remember(string path)
        {
            Assert.IsNotNull(path, "Recorder did not create an output directory.");
            directories.Add(path);
            return path;
        }

        static bool ReloadsDomain => !EditorSettings.enterPlayModeOptionsEnabled ||
            (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (owner != null) Object.DestroyImmediate(owner);
            foreach (string directory in directories)
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            directories.Clear();
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
