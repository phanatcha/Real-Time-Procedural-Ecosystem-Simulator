using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Boreal.Core;
using UnityEngine;

namespace Boreal.Animals
{
    // Opt in by adding this to the Species Manager. Each regenerated world gets its own files.
    [RequireComponent(typeof(SpeciesManager))]
    [DefaultExecutionOrder(900)]
    public sealed class SimulationRunRecorder : MonoBehaviour
    {
        [Min(0.1f)] public float sampleInterval = 10f;
        [Tooltip("Revision and dirty-state label for this build. Set before recording experiments.")]
        public string sourceRevision = "unrecorded";
        public string experiment = "exploratory";
        public string OutputDirectory { get; private set; }

        SpeciesManager manager;
        StreamWriter population, species, traits;
        float worldStart;
        int worldRevision = -1;
        double lastSample = -1;
        readonly SimulationSampleSchedule schedule = new SimulationSampleSchedule();
        bool failed;

        [Serializable]
        sealed class RunMetadata
        {
            public int schemaVersion = 1;
            public string startedUtc, sourceRevision, experiment, unityVersion, worldSeed;
            public float sampleInterval;
            public bool offscreenEnabled;
            public string managerSettings, seasonSettings, foodSettings, representativeAnimalSettings;
        }

        void OnEnable() { manager = GetComponent<SpeciesManager>(); failed = false; }

        void LateUpdate()
        {
            if (failed || manager == null) return;
            try
            {
                if (worldRevision != manager.WorldRevision)
                {
                    Close("world reset");
                    Begin();
                }
                double time = Math.Max(0, Time.timeAsDouble - worldStart);
                if (!schedule.Take(time, Interval)) return;
                Sample(time);
                // Never invent historical states when a long frame crosses several sample boundaries.
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                failed = true;
                DisposeWriters();
                Debug.LogError("Run recording stopped: " + e.Message, this);
            }
        }

        double Interval => float.IsNaN(sampleInterval) || float.IsInfinity(sampleInterval)
            ? 10 : Math.Max(0.1, sampleInterval);

        void Begin()
        {
            worldStart = manager.WorldStartTime;
            worldRevision = manager.WorldRevision;
            schedule.Reset();
            lastSample = -1;
            OutputDirectory = Path.Combine(Application.persistentDataPath, "SimulationRuns",
                DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(OutputDirectory);
            SeekFood representative = null;
            foreach (SeekFood animal in manager.ActiveAgents)
                if (animal != null) { representative = animal; break; }
            FoodSpawner food = FindAnyObjectByType<FoodSpawner>();
            RunMetadata metadata = new RunMetadata
            {
                startedUtc = DateTime.UtcNow.ToString("O"), sourceRevision = sourceRevision,
                experiment = experiment, unityVersion = Application.unityVersion,
                worldSeed = SeedManager.Instance != null ? SeedManager.Instance.SeedString : "unrecorded",
                sampleInterval = (float)Interval, offscreenEnabled = manager.OffscreenPopulation != null,
                managerSettings = JsonUtility.ToJson(manager),
                seasonSettings = SeasonClock.Active != null ? JsonUtility.ToJson(SeasonClock.Active) : "",
                foodSettings = food != null ? JsonUtility.ToJson(food) : "",
                representativeAnimalSettings = representative != null ? JsonUtility.ToJson(representative) : ""
            };
            File.WriteAllText(Path.Combine(OutputDirectory, "metadata.json"), JsonUtility.ToJson(metadata, true));
            population = Open("population.csv", "time,live,offscreen,species_alive,births,cloned_births,sexual_births,deaths,starvation,predation,old_age,cold,heat,venom,other,land_ceiling,sea_ceiling,requested_speed,achieved_speed,season,season_strength,offscreen_enabled");
            species = Open("species.csv", "time,species,parent,origin,extinction,live,offscreen,births,sexual_births,deaths");
            traits = Open("traits.csv", "time,species,trait,live_sample_count,mean,standard_deviation,minimum,maximum");
            Debug.Log("Recording simulation to " + OutputDirectory, this);
        }

        StreamWriter Open(string name, string header)
        {
            var writer = new StreamWriter(Path.Combine(OutputDirectory, name), false, new UTF8Encoding(false));
            writer.WriteLine(header);
            writer.Flush();
            return writer;
        }

        void Sample(double time)
        {
            EcosystemTally total = EcosystemTally.Of(manager.Telemetry.Values);
            Row(population, time, manager.TotalPopulation, manager.OffscreenTotal, manager.SpeciesAliveCount,
                total.births, total.births - total.sexualBirths, total.sexualBirths, total.deaths,
                total.starvationDeaths, total.predationDeaths, total.oldAgeDeaths, total.coldDeaths,
                total.heatDeaths, total.venomDeaths, total.OtherDeaths, manager.IsAtPopulationCeiling,
                manager.IsAtSeaPopulationCeiling, manager.simulationSpeed, manager.AchievedSimulationSpeed,
                SeasonClock.Active != null ? SeasonClock.Active.SeasonName : "None",
                SeasonClock.IsRunning ? SeasonClock.Active.EffectiveStrength : 0f, manager.OffscreenPopulation != null);

            var groups = new Dictionary<string, List<AnimalGenome>>();
            foreach (SeekFood animal in manager.ActiveAgents)
            {
                if (animal == null || animal.Genome == null || !animal.Genome.IsValid) continue;
                if (!groups.TryGetValue(animal.speciesName, out var genomes))
                    groups[animal.speciesName] = genomes = new List<AnimalGenome>();
                genomes.Add(animal.Genome);
            }
            foreach (SpeciesTelemetryRecord record in manager.Telemetry.Values)
            {
                manager.SpeciesPopulation.TryGetValue(record.speciesName, out int live);
                Row(species, time, record.speciesName, record.parentSpeciesName, record.originTime,
                    record.extinctionTime, live, manager.OffscreenPopulation?.CountOffscreen(record.speciesName) ?? 0,
                    record.reproductionEvents, record.sexualOffspring, record.deaths);
                groups.TryGetValue(record.speciesName, out var genomes);
                // Empty populations have blank trait values, not misleading zero-valued genes. Their history remains.
                for (int gene = 0; gene < AnimalGenome.GeneCount; gene++)
                {
                    int index = gene;
                    WriteTrait(time, record.speciesName, ((AnimalGene)gene).ToString(), genomes, g => g[(AnimalGene)index]);
                }
                WriteTrait(time, record.speciesName, "HarmfulMutations", genomes, g => g.HarmfulMutations);
                for (int perk = 0; perk < AnimalGenome.PerkCount; perk++)
                {
                    AnimalPerk p = (AnimalPerk)perk;
                    WriteTrait(time, record.speciesName, "Perk_" + p, genomes, g => g.HasPerk(p) ? 1 : 0);
                }
            }
            population.Flush(); species.Flush(); traits.Flush();
            lastSample = time;
        }

        void WriteTrait(double time, string name, string trait, List<AnimalGenome> genomes, Func<AnimalGenome, double> value)
        {
            var summary = new RunningStatistics();
            if (genomes != null) foreach (var genome in genomes) summary.Add(value(genome));
            if (summary.Count == 0) Row(traits, time, name, trait, 0, "", "", "", "");
            else Row(traits, time, name, trait, summary.Count, summary.Mean, summary.StandardDeviation, summary.Minimum, summary.Maximum);
        }

        static void Row(TextWriter writer, params object[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) writer.Write(',');
                writer.Write(CsvEncoding.Cell(values[i]));
            }
            writer.WriteLine();
        }

        void OnDisable() { Close("recorder disabled or Play stopped"); worldRevision = -1; }
        void OnApplicationQuit() { Close("application quit"); }

        void Close(string reason)
        {
            if (population == null) return;
            try
            {
                // Do not read the new world's state into the previous run during regeneration.
                if (manager != null && manager.WorldRevision == worldRevision)
                {
                    double time = Math.Max(0, Time.timeAsDouble - worldStart);
                    if (time > lastSample) Sample(time);
                }
                File.WriteAllText(Path.Combine(OutputDirectory, "ended.txt"), reason);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            { Debug.LogError("Could not finalize run recording: " + e.Message, this); }
            finally { DisposeWriters(); }
        }

        void DisposeWriters()
        {
            try { population?.Dispose(); }
            finally
            {
                try { species?.Dispose(); }
                finally { try { traits?.Dispose(); } finally { population = species = traits = null; } }
            }
        }
    }

}
