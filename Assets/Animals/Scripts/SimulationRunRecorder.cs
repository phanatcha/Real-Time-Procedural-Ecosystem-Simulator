using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Boreal.Core;
using Boreal.TerrainGeneration;
using UnityEngine;

namespace Boreal.Animals
{
    // Opt in by adding this to the Species Manager. Each regenerated world gets its own files.
    [RequireComponent(typeof(SpeciesManager))]
    [DefaultExecutionOrder(900)]
    public sealed class SimulationRunRecorder : MonoBehaviour
    {
        [Min(0.1f)] public float sampleInterval = 10f;
        [Tooltip("Explicit revision label, or unrecorded to resolve the Git commit automatically in the Editor.")]
        public string sourceRevision = "unrecorded";
        // Supplied by the Editor assembly. Runtime builds can set sourceRevision explicitly.
        public static Func<string> RevisionProvider { get; set; }
        public static string ResolveRevision(string explicitRevision)
        {
            if (!string.IsNullOrWhiteSpace(explicitRevision) && explicitRevision != "unrecorded") return explicitRevision;
            return RevisionProvider?.Invoke() ?? "unrecorded";
        }
        public string experiment = "exploratory";
        public string OutputDirectory { get; private set; }

        SpeciesManager manager;
        StreamWriter population, species, traits, events, lives;
        int eventsWritten;
        float worldStart;
        int worldRevision = -1;
        double lastSample = -1;
        readonly SimulationSampleSchedule schedule = new SimulationSampleSchedule();
        bool failed;

        [Serializable]
        sealed class RunMetadata
        {
            // 4: population.csv gained the time of day, light, moon and deaths at night (item 70); traits.csv the
            // marker genes and perk (item 56). 5: population.csv gained the share of live animals resting, traits.csv the
            // Nocturnality gene (item 74). 6: traits.csv gained the NightVision gene (item 75). 7: population.csv and
            // species.csv gained the live animals' average and highest generation, traits.csv how many animals sit at
            // each gene's limits (item 54). 8: a new lives.csv with one row per animal (item 57). 9: traits.csv
            // and lives.csv gained the AgeingOnset gene (item 68).
            public int schemaVersion = 9;
            public string startedUtc, sourceRevision, experiment, unityVersion, worldSeed;
            public float sampleInterval;
            public bool offscreenEnabled;
            public string managerSettings, seasonSettings, dayClockSettings, foodSettings, representativeAnimalSettings;
        }

        void OnEnable()
        {
            manager = GetComponent<SpeciesManager>();
            failed = false;
            manager.AnimalDied += OnAnimalDied;
            manager.AnimalLeft += OnAnimalLeft;
        }

        void OnAnimalDied(SeekFood animal, AgentDeathCause cause) => WriteLife(animal, cause.ToString(), true);
        void OnAnimalLeft(SeekFood animal) => WriteLife(animal, LifeRecords.LeftForOffscreen, true);

        // One row per animal as it dies or leaves; Close adds the animals still alive (item 57).
        void WriteLife(SeekFood animal, string cause, bool ended)
        {
            // A death in a world this run isn't recording (before its first frame, or after a reset) has no file.
            if (failed || lives == null || animal == null || manager == null || manager.WorldRevision != worldRevision) return;
            try
            {
                double time = Math.Max(0, Time.timeAsDouble - worldStart);
                Vector3 birth = animal.BirthPosition, end = animal.transform.position;
                Row(lives, LifeRecords.Cells(animal.AnimalId, animal.FirstParentId, animal.SecondParentId,
                    animal.speciesName, animal.Generation, animal.Origin, animal.BirthTime, ended ? time : (double?)null,
                    animal.CurrentAge, cause, birth.x, birth.z, end.x, end.z, animal.YoungBorn, animal.Genome).ToArray());
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                failed = true;
                DisposeWriters();
                Debug.LogError("Run recording stopped: " + e.Message, this);
            }
        }

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
                startedUtc = DateTime.UtcNow.ToString("O"), sourceRevision = ResolveRevision(sourceRevision),
                experiment = experiment, unityVersion = Application.unityVersion,
                worldSeed = SeedManager.Instance != null ? SeedManager.Instance.SeedString : "unrecorded",
                sampleInterval = (float)Interval, offscreenEnabled = manager.OffscreenPopulation != null,
                managerSettings = JsonUtility.ToJson(manager),
                seasonSettings = SeasonClock.Active != null ? JsonUtility.ToJson(SeasonClock.Active) : "",
                dayClockSettings = DayClock.Active != null ? JsonUtility.ToJson(DayClock.Active) : "",
                foodSettings = food != null ? JsonUtility.ToJson(food) : "",
                representativeAnimalSettings = representative != null ? JsonUtility.ToJson(representative) : ""
            };
            File.WriteAllText(Path.Combine(OutputDirectory, "metadata.json"), JsonUtility.ToJson(metadata, true));
            population = Open("population.csv", "time,live,offscreen,species_alive,births,cloned_births,sexual_births,deaths,starvation,predation,old_age,cold,heat,venom,thirst,other,land_ceiling,sea_ceiling,requested_speed,achieved_speed,season,season_strength,offscreen_enabled,breeding_wait_episodes,breeding_wait_seconds,day,time_of_day,daylight,light,moon_phase,deaths_at_night,resting,average_generation,highest_generation");
            species = Open("species.csv", "time,species,parent,origin,extinction,live,offscreen,births,sexual_births,deaths,breeding_wait_episodes,breeding_wait_seconds,average_generation,highest_generation");
            traits = Open("traits.csv", "time,species,trait,live_sample_count,mean,standard_deviation,minimum,maximum,at_minimum,at_maximum");
            events = Open("events.csv", "time,event,detail");
            lives = Open("lives.csv", LifeRecords.Header());
            eventsWritten = 0;
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
            DayState day = DayClock.Current;
            // Generations are read from the live animals at this sample, not from the manager's last census, so they
            // match the rest of the row (item 54). Founders are generation 0.
            var groups = new Dictionary<string, List<AnimalGenome>>();
            var generations = new Dictionary<string, RunningStatistics>();
            var allGenerations = new RunningStatistics();
            foreach (SeekFood animal in manager.ActiveAgents)
            {
                if (animal == null) continue;
                // The same animals the manager's census counts.
                if (animal.IsAlive)
                {
                    generations.TryGetValue(animal.speciesName, out RunningStatistics ofSpecies);
                    ofSpecies.Add(animal.Generation);
                    generations[animal.speciesName] = ofSpecies;
                    allGenerations.Add(animal.Generation);
                }
                if (animal.Genome == null || !animal.Genome.IsValid) continue;
                if (!groups.TryGetValue(animal.speciesName, out var genomes))
                    groups[animal.speciesName] = genomes = new List<AnimalGenome>();
                genomes.Add(animal.Genome);
            }
            Row(population, time, manager.TotalPopulation, manager.OffscreenTotal, manager.SpeciesAliveCount,
                total.births, total.births - total.sexualBirths, total.sexualBirths, total.deaths,
                total.starvationDeaths, total.predationDeaths, total.oldAgeDeaths, total.coldDeaths,
                total.heatDeaths, total.venomDeaths, total.thirstDeaths, total.OtherDeaths, manager.IsAtPopulationCeiling,
                manager.IsAtSeaPopulationCeiling, manager.simulationSpeed, manager.AchievedSimulationSpeed,
                SeasonClock.Active != null ? SeasonClock.Active.SeasonName : "None",
                SeasonClock.IsRunning ? SeasonClock.Active.EffectiveStrength : 0f, manager.OffscreenPopulation != null,
                manager.BreedingWaitEpisodes, manager.BreedingWaitSeconds, day.day, day.timeOfDay, day.daylight,
                day.light, day.moonPhase, manager.DeathsAtNight, RestingShare(),
                AverageGeneration(allGenerations), HighestGeneration(allGenerations));

            foreach (SpeciesTelemetryRecord record in manager.Telemetry.Values)
            {
                manager.SpeciesPopulation.TryGetValue(record.speciesName, out int live);
                generations.TryGetValue(record.speciesName, out RunningStatistics ofRecord);
                Row(species, time, record.speciesName, record.parentSpeciesName, record.originTime,
                    record.extinctionTime, live, manager.OffscreenPopulation?.CountOffscreen(record.speciesName) ?? 0,
                    record.reproductionEvents, record.sexualOffspring, record.deaths,
                    record.breedingWaitEpisodes, record.breedingWaitSeconds,
                    AverageGeneration(ofRecord), HighestGeneration(ofRecord));
                groups.TryGetValue(record.speciesName, out var genomes);
                // Empty populations have blank trait values, not misleading zero-valued genes. Their history remains.
                for (int gene = 0; gene < AnimalGenome.GeneCount; gene++)
                {
                    AnimalGene which = (AnimalGene)gene;
                    GeneLimits.Count(genomes, which, out int atMinimum, out int atMaximum);
                    WriteTrait(time, record.speciesName, which.ToString(), genomes, genome => genome[which], atMinimum, atMaximum);
                }
                WriteTrait(time, record.speciesName, "HarmfulMutations", genomes, g => g.HarmfulMutations);
                for (int perk = 0; perk < AnimalGenome.PerkCount; perk++)
                {
                    AnimalPerk p = (AnimalPerk)perk;
                    WriteTrait(time, record.speciesName, "Perk_" + p, genomes, g => g.HasPerk(p) ? 1 : 0);
                }
            }
            // Outside interventions (god tools) since the last sample, with the time they happened at.
            IReadOnlyList<SpeciesManager.SimulationEvent> happened = manager.Events;
            for (; eventsWritten < happened.Count; eventsWritten++)
            {
                SpeciesManager.SimulationEvent happening = happened[eventsWritten];
                Row(events, happening.time, happening.kind, happening.detail);
            }
            population.Flush(); species.Flush(); traits.Flush(); events.Flush(); lives.Flush();
            lastSample = time;
        }

        // Share of the live animals resting now (item 74).
        double RestingShare()
        {
            int live = 0, resting = 0;
            foreach (SeekFood animal in manager.ActiveAgents)
            {
                if (animal == null) continue;
                live++;
                if (animal.IsResting) resting++;
            }
            return live > 0 ? (double)resting / live : 0.0;
        }

        // No live animals leaves the generation cells blank, as empty populations' traits are.
        static object AverageGeneration(RunningStatistics generations) =>
            generations.Count > 0 ? (object)generations.Mean : "";
        static object HighestGeneration(RunningStatistics generations) =>
            generations.Count > 0 ? (object)(int)generations.Maximum : "";

        // atMinimum and atMaximum are the animals at the gene's limits; harmful mutations and perks have none, so
        // theirs stay blank.
        void WriteTrait(double time, string name, string trait, List<AnimalGenome> genomes, Func<AnimalGenome, double> value,
                        int atMinimum = -1, int atMaximum = -1)
        {
            var summary = new RunningStatistics();
            if (genomes != null) foreach (var genome in genomes) summary.Add(value(genome));
            if (summary.Count == 0) Row(traits, time, name, trait, 0, "", "", "", "", "", "");
            else Row(traits, time, name, trait, summary.Count, summary.Mean, summary.StandardDeviation, summary.Minimum,
                     summary.Maximum, atMinimum < 0 ? (object)"" : atMinimum, atMaximum < 0 ? (object)"" : atMaximum);
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

        void OnDisable()
        {
            Close("recorder disabled or Play stopped");
            worldRevision = -1;
            if (manager != null)
            {
                manager.AnimalDied -= OnAnimalDied;
                manager.AnimalLeft -= OnAnimalLeft;
            }
        }
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
                    // The animals still alive close the life records, so every animal has exactly one row.
                    foreach (SeekFood animal in manager.ActiveAgents)
                        WriteLife(animal, LifeRecords.StillAlive, false);
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
                finally
                {
                    try { traits?.Dispose(); }
                    finally
                    {
                        try { events?.Dispose(); }
                        finally { try { lives?.Dispose(); } finally { population = species = traits = events = lives = null; } }
                    }
                }
            }
        }
    }

}
