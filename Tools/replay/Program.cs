// Item 59: replays a recording's lives.csv with selection taken out (Boreal.Animals.NeutralReplay) and writes the mean
// of every trait over the living animals at each sample time, for the real run (replay 0) and each replay (1 to N).
//
//   NeutralReplay <lives.csv> <out.csv> [--replays 20] [--seed 1] [--interval 10] [--mutation-chance 5]
//                 [--mutation-magnitude 0.1] [--perk-gain 0.2] [--perk-loss 2] [--harmful-chance 10] [--age-window 0.25]
//                 [--mode tree|random]
//   NeutralReplay --self-test
//
// Tools/neutral_replay.py builds and runs it; see TESTING.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Boreal.Animals;

namespace Boreal.Tools.Replay
{
    static class Program
    {
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        static int Main(string[] args)
        {
            try
            {
                if (args.Contains("--self-test")) return SelfTest();
                if (args.Length < 2) throw new ArgumentException("usage: NeutralReplay <lives.csv> <out.csv> [options]");

                NeutralReplay.Settings settings = NeutralReplay.Settings.Default;
                int replays = 20, seed = 1;
                float interval = 10f;
                for (int i = 2; i < args.Length; i += 2)
                {
                    if (i + 1 >= args.Length) throw new ArgumentException($"{args[i]} needs a value");
                    if (args[i] == "--mode")
                    {
                        if (args[i + 1] != "tree" && args[i + 1] != "random")
                            throw new ArgumentException("--mode is tree or random");
                        settings.keepFamilyTree = args[i + 1] == "tree";
                        continue;
                    }
                    float v = float.Parse(args[i + 1], Invariant);
                    switch (args[i])
                    {
                        case "--replays": replays = (int)v; break;
                        case "--seed": seed = (int)v; break;
                        case "--interval": interval = v; break;
                        case "--mutation-chance": settings.mutationChance = v; break;
                        case "--mutation-magnitude": settings.mutationMagnitude = v; break;
                        case "--perk-gain": settings.perkGainChance = v; break;
                        case "--perk-loss": settings.perkLossChance = v; break;
                        case "--harmful-chance": settings.harmfulMutationChance = v; break;
                        case "--age-window": settings.ageWindowShare = v; break;
                        default: throw new ArgumentException($"unknown option {args[i]}");
                    }
                }

                List<NeutralReplay.Life> lives = ReadLives(args[0], out float endTime);
                using StreamWriter writer = new StreamWriter(args[1], false, new UTF8Encoding(false));
                List<string> traits = NeutralReplay.TraitNames();
                writer.WriteLine("replay,time,live," + string.Join(",", traits));
                for (int replay = 0; replay <= replays; replay++)
                {
                    UnityEngine.Random.InitState(seed * 1000 + replay);
                    NeutralReplay.Run(lives, settings, interval, endTime, replay > 0, (time, living) =>
                    {
                        float[] means = NeutralReplay.Means(living);
                        writer.Write(replay.ToString(Invariant));
                        writer.Write(',');
                        writer.Write(time.ToString("0.###", Invariant));
                        writer.Write(',');
                        writer.Write(living.Count.ToString(Invariant));
                        foreach (float mean in means)
                        {
                            writer.Write(',');
                            if (!float.IsNaN(mean)) writer.Write(mean.ToString("0.######", Invariant));
                        }
                        writer.WriteLine();
                    });
                }
                Console.WriteLine($"{lives.Count} lives, {replays} replays to {endTime:0} s: {args[1]}");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(args.Contains("--self-test") ? error.ToString() : error.Message);
                return 1;
            }
        }

        // lives.csv as the recorder writes it (RUN_RECORDING.md): plain comma-separated values, no quoting.
        static List<NeutralReplay.Life> ReadLives(string path, out float endTime)
        {
            string[] lines = File.ReadAllLines(path);
            string[] header = lines[0].Split(',');
            Dictionary<string, int> column = new Dictionary<string, int>();
            for (int i = 0; i < header.Length; i++) column[header[i]] = i;

            List<NeutralReplay.Life> lives = new List<NeutralReplay.Life>();
            endTime = 0f;
            for (int row = 1; row < lines.Length; row++)
            {
                if (lines[row].Length == 0) continue;
                string[] cells = lines[row].Split(',');
                string Cell(string name) => column.TryGetValue(name, out int at) && at < cells.Length ? cells[at] : "";
                float Number(string name) => float.Parse(Cell(name), Invariant);
                int Id(string name) => Cell(name) == "" ? 0 : int.Parse(Cell(name), Invariant);

                NeutralReplay.Life life = new NeutralReplay.Life
                {
                    id = Id("id"),
                    firstParent = Id("first_parent"),
                    secondParent = Id("second_parent"),
                    origin = Cell("origin"),
                    born = Number("born"),
                    generation = Id("generation"),
                    genome = ReadGenome(Cell)
                };
                // An animal alive when the recording stopped has no end, and its age runs to the end of the record.
                bool alive = Cell("cause") == "alive" || Cell("ended") == "";
                float ended = alive ? life.born + Number("age") : Number("ended");
                endTime = Math.Max(endTime, Math.Max(ended, life.born));
                if (!alive) life.ended = ended;
                lives.Add(life);
            }

            return lives;
        }

        static AnimalGenome ReadGenome(Func<string, string> cell)
        {
            AnimalGenome genome = AnimalGenome.Create();
            foreach (AnimalGene gene in Enum.GetValues(typeof(AnimalGene)))
            {
                string value = cell(gene.ToString());
                if (value != "") genome[gene] = float.Parse(value, Invariant);
            }

            foreach (AnimalPerk perk in Enum.GetValues(typeof(AnimalPerk)))
            {
                if (cell("Perk_" + perk) == "1") genome.SetPerk(perk, true);
            }

            string harmful = cell("harmful_mutations");
            int load = harmful == "" ? 0 : int.Parse(harmful, Invariant);
            for (int i = 0; i < load; i++) genome.TryAddHarmfulMutation(100f);

            foreach (BodySite site in Enum.GetValues(typeof(BodySite)))
            {
                string part = cell("Part_" + site);
                if (part == "") continue;
                string[] typeAndSize = part.Split(':');
                genome.SetPart(site, (BodyPartType)Enum.Parse(typeof(BodyPartType), typeAndSize[0]),
                               float.Parse(typeAndSize[1], Invariant));
            }

            return genome;
        }

        // The same checks as NeutralReplayTests in Unity, on made-up records.
        static int SelfTest()
        {
            int failures = 0;
            void Check(bool ok, string what)
            {
                Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
                if (!ok) failures++;
            }

            foreach ((bool ok, string what) in SelfTests.All()) Check(ok, what);
            Console.WriteLine(failures == 0 ? "All self-tests passed." : $"{failures} self-tests failed.");
            return failures == 0 ? 0 : 1;
        }
    }

    // Made-up records with a known answer.
    static class SelfTests
    {
        public static IEnumerable<(bool, string)> All()
        {
            // Two founders, A (marker 0.2) and B (marker 0.8). Only B's line breeds in the record, so the real run ends
            // all B; a replay picks parents by age alone and should end near half and half on average.
            List<NeutralReplay.Life> lives = SweepRecord(out float end);
            NeutralReplay.Settings still = NeutralReplay.Settings.Default;
            still.keepFamilyTree = false;
            still.mutationChance = 0f;
            still.bodyPlanMutation = default;
            still.perkGainChance = still.perkLossChance = still.harmfulMutationChance = 0f;
            int marker = NeutralReplay.TraitNames().IndexOf("MarkerA");
            int generation = NeutralReplay.TraitNames().IndexOf("generation");

            float realEnd = 0f, realGeneration = 0f;
            List<int> realCounts = new List<int>();
            NeutralReplay.Run(lives, still, 10f, end, false, (t, living) =>
            {
                realCounts.Add(living.Count);
                float[] means = NeutralReplay.Means(living);
                realEnd = means[marker];
                realGeneration = means[generation];
            });
            yield return (Math.Abs(realEnd - 0.8f) < 1e-4f, $"the real record ends all B (marker {realEnd:0.000})");

            double total = 0, generations = 0;
            bool countsMatch = true;
            const int runs = 200;
            for (int r = 0; r < runs; r++)
            {
                UnityEngine.Random.InitState(r);
                int sample = 0;
                float last = 0f, lastGeneration = 0f;
                NeutralReplay.Run(lives, still, 10f, end, true, (t, living) =>
                {
                    countsMatch &= living.Count == realCounts[sample++];
                    float[] means = NeutralReplay.Means(living);
                    last = means[marker];
                    lastGeneration = means[generation];
                });
                total += last;
                generations += lastGeneration;
            }
            yield return (countsMatch, "every replay has the real run's number of animals at every sample");
            double average = total / runs;
            yield return (average > 0.4 && average < 0.6,
                          $"without selection the founders' lines end even on average (marker {average:0.000}, real 0.8)");
            double replayGeneration = generations / runs;
            yield return (Math.Abs(replayGeneration - realGeneration) < 0.25 * realGeneration,
                          $"the replays keep the generation time (generation {replayGeneration:0.0}, real {realGeneration:0.0})");

            // Keeping the family tree, the favoured line stays favoured: only mutations are drawn afresh.
            NeutralReplay.Settings tree = still;
            tree.keepFamilyTree = true;
            float treeEnd = 0f;
            UnityEngine.Random.InitState(7);
            NeutralReplay.Run(lives, tree, 10f, end, true, (t, living) => treeEnd = NeutralReplay.Means(living)[marker]);
            yield return (Math.Abs(treeEnd - 0.8f) < 1e-4f, $"keeping the family tree keeps B's line (marker {treeEnd:0.000})");

            // With mutation, a do-nothing gene wanders but its mean stays put on average, either way.
            NeutralReplay.Settings mutating = NeutralReplay.Settings.Default;
            mutating.keepFamilyTree = false;
            double drift = 0;
            for (int r = 0; r < runs; r++)
            {
                UnityEngine.Random.InitState(1000 + r);
                float last = 0f;
                NeutralReplay.Run(EvenRecord(out float evenEnd), mutating, 10f, evenEnd, true,
                                  (t, living) => last = NeutralReplay.Means(living)[NeutralReplay.TraitNames().IndexOf("MarkerB")]);
                drift += last - 0.5;
            }
            yield return (Math.Abs(drift / runs) < 0.02, $"random parents: mutation alone doesn't move a marker's mean (average change {drift / runs:+0.000;-0.000})");

            mutating.keepFamilyTree = true;
            drift = 0;
            double spread = 0;
            for (int r = 0; r < runs; r++)
            {
                UnityEngine.Random.InitState(5000 + r);
                float last = 0f;
                NeutralReplay.Run(EvenRecord(out float evenEnd), mutating, 10f, evenEnd, true,
                                  (t, living) => last = NeutralReplay.Means(living)[NeutralReplay.TraitNames().IndexOf("MarkerB")]);
                drift += last - 0.5;
                spread += Math.Abs(last - 0.5);
            }
            yield return (Math.Abs(drift / runs) < 0.02 && spread > 0,
                          $"family tree: mutation moves the marker (mean distance {spread / runs:0.000}) but not its mean (average change {drift / runs:+0.000;-0.000})");
        }

        static AnimalGenome Founder(float marker)
        {
            AnimalGenome genome = AnimalGenome.Create();
            genome[AnimalGene.MarkerA] = marker;
            genome[AnimalGene.MarkerB] = 0.5f;
            genome[AnimalGene.MarkerC] = 0.5f;
            genome[AnimalGene.MutationRate] = 1f;
            return genome;
        }

        // Ten founders of each kind at time 0, all living 61 s. Every 2 s one animal of B's line at least 30 s old has
        // a child that lives 60 s; A's founders die without breeding.
        static List<NeutralReplay.Life> SweepRecord(out float end)
        {
            List<NeutralReplay.Life> lives = new List<NeutralReplay.Life>();
            int id = 1;
            for (int i = 0; i < 10; i++)
                lives.Add(new NeutralReplay.Life { id = id++, origin = NeutralReplay.FounderOrigin, born = 0f, ended = 61f, genome = Founder(0.2f) });
            List<NeutralReplay.Life> bLine = new List<NeutralReplay.Life>();
            for (int i = 0; i < 10; i++)
            {
                var founder = new NeutralReplay.Life { id = id++, origin = NeutralReplay.FounderOrigin, born = 0f, ended = 61f, genome = Founder(0.8f) };
                lives.Add(founder);
                bLine.Add(founder);
            }

            for (float t = 30f; t < 398f; t += 2f)
            {
                NeutralReplay.Life parent = bLine.Last(l => t - l.born >= 30f && l.ended > t);
                var child = new NeutralReplay.Life
                {
                    id = id++, firstParent = parent.id, origin = NeutralReplay.BornOrigin, born = t,
                    ended = Math.Min(t + 60f, 400f), generation = parent.generation + 1, genome = Founder(0.8f)
                };
                lives.Add(child);
                bLine.Add(child);
            }

            // Sampled to 390 s, while A's founders are still alive.
            end = 390f;
            return lives;
        }

        // Twenty identical founders with the marker at 0.5, and a steady turnover of clones.
        static List<NeutralReplay.Life> EvenRecord(out float end)
        {
            List<NeutralReplay.Life> lives = new List<NeutralReplay.Life>();
            int id = 1;
            for (int i = 0; i < 20; i++)
                lives.Add(new NeutralReplay.Life { id = id++, origin = NeutralReplay.FounderOrigin, born = 0f, ended = 30f + i * 2f, genome = Founder(0.5f) });
            for (float t = 30f; t <= 300f; t += 2f)
                lives.Add(new NeutralReplay.Life { id = id++, firstParent = id - 20, origin = NeutralReplay.BornOrigin, born = t, ended = t + 40f, genome = Founder(0.5f) });
            end = 300f;
            return lives;
        }
    }
}
