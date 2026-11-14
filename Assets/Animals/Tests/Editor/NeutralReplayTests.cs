using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Boreal.Animals.Tests
{
    // The replay without selection (item 59), on made-up records with a known answer. Tools/replay checks the same
    // records with --self-test, outside Unity.
    public class NeutralReplayTests
    {
        static AnimalGenome Founder(float marker)
        {
            AnimalGenome genome = AnimalGenome.Create();
            genome[AnimalGene.MarkerA] = marker;
            genome[AnimalGene.MarkerB] = 0.5f;
            genome[AnimalGene.MarkerC] = 0.5f;
            genome[AnimalGene.MutationRate] = 1f;
            return genome;
        }

        // Ten founders of each kind at time 0, all living 61 s. Every 2 s one animal of B's line at least 30 s old has a
        // child that lives 60 s; A's founders die without breeding. So the real run ends all B.
        static List<NeutralReplay.Life> SweepRecord()
        {
            List<NeutralReplay.Life> lives = new List<NeutralReplay.Life>();
            List<NeutralReplay.Life> bLine = new List<NeutralReplay.Life>();
            int id = 1;
            for (int i = 0; i < 10; i++)
                lives.Add(new NeutralReplay.Life { id = id++, origin = NeutralReplay.FounderOrigin, born = 0f, ended = 61f, genome = Founder(0.2f) });
            for (int i = 0; i < 10; i++)
            {
                var founder = new NeutralReplay.Life { id = id++, origin = NeutralReplay.FounderOrigin, born = 0f, ended = 61f, genome = Founder(0.8f) };
                lives.Add(founder);
                bLine.Add(founder);
            }

            for (float t = 30f; t < 398f; t += 2f)
            {
                NeutralReplay.Life parent = null;
                foreach (NeutralReplay.Life life in bLine)
                    if (t - life.born >= 30f && life.ended > t) parent = life;
                var child = new NeutralReplay.Life
                {
                    id = id++, firstParent = parent.id, origin = NeutralReplay.BornOrigin, born = t,
                    ended = Mathf.Min(t + 60f, 400f), generation = parent.generation + 1, genome = Founder(0.8f)
                };
                lives.Add(child);
                bLine.Add(child);
            }

            return lives;
        }

        static NeutralReplay.Settings NoMutation(bool keepFamilyTree)
        {
            NeutralReplay.Settings settings = NeutralReplay.Settings.Default;
            settings.keepFamilyTree = keepFamilyTree;
            settings.mutationChance = 0f;
            settings.bodyPlanMutation = default;
            settings.perkGainChance = settings.perkLossChance = settings.harmfulMutationChance = 0f;
            return settings;
        }

        // Replays keep every animal's place in time, so the head count matches the record at every sample. With random
        // parents a line the record favours ends near half and half on average; keeping the family tree, it stays.
        [Test]
        public void AReplayKeepsTheHeadCountButNotTheFavouredLine()
        {
            List<NeutralReplay.Life> lives = SweepRecord();
            int marker = NeutralReplay.TraitNames().IndexOf("MarkerA");
            int generation = NeutralReplay.TraitNames().IndexOf("generation");
            List<int> realCounts = new List<int>();
            float realEnd = 0f, realGeneration = 0f;
            NeutralReplay.Run(lives, NoMutation(false), 10f, 390f, false, (t, living) =>
            {
                realCounts.Add(living.Count);
                float[] means = NeutralReplay.Means(living);
                realEnd = means[marker];
                realGeneration = means[generation];
            });
            Assert.AreEqual(0.8f, realEnd, 1e-4f, "The record ends all B.");
            Assert.AreEqual(40, realCounts.Count);

            Random.State saved = Random.state;
            try
            {
                double total = 0, generations = 0;
                const int runs = 200;
                for (int r = 0; r < runs; r++)
                {
                    Random.InitState(r);
                    int sample = 0;
                    float last = 0f, lastGeneration = 0f;
                    NeutralReplay.Run(lives, NoMutation(false), 10f, 390f, true, (t, living) =>
                    {
                        Assert.AreEqual(realCounts[sample++], living.Count, $"head count at {t} s");
                        float[] means = NeutralReplay.Means(living);
                        last = means[marker];
                        lastGeneration = means[generation];
                    });
                    total += last;
                    generations += lastGeneration;
                }

                Assert.That(total / runs, Is.InRange(0.4, 0.6), "Without selection the two founders' lines end even.");
                Assert.That(generations / runs, Is.EqualTo(realGeneration).Within(0.25 * realGeneration),
                            "Parents of the real parents' age keep the generation time.");

                Random.InitState(7);
                float treeEnd = 0f;
                NeutralReplay.Run(lives, NoMutation(true), 10f, 390f, true, (t, living) => treeEnd = NeutralReplay.Means(living)[marker]);
                Assert.AreEqual(0.8f, treeEnd, 1e-4f, "Keeping the family tree keeps the favoured line.");
            }
            finally
            {
                Random.state = saved;
            }
        }

        // With mutation the replays use the game's own rules (CreateChild): a do-nothing gene wanders but its mean stays
        // put on average, whichever way the parents are found.
        [TestCase(true)]
        [TestCase(false)]
        public void ReplaysMutateByTheGamesRulesWithoutMovingAMarkerOnAverage(bool keepFamilyTree)
        {
            NeutralReplay.Settings settings = NeutralReplay.Settings.Default;
            settings.keepFamilyTree = keepFamilyTree;
            List<NeutralReplay.Life> lives = new List<NeutralReplay.Life>();
            int id = 1;
            for (int i = 0; i < 20; i++)
                lives.Add(new NeutralReplay.Life { id = id++, origin = NeutralReplay.FounderOrigin, born = 0f, ended = 30f + i * 2f, genome = Founder(0.5f) });
            for (float t = 30f; t <= 300f; t += 2f)
                lives.Add(new NeutralReplay.Life { id = id++, firstParent = id - 20, origin = NeutralReplay.BornOrigin, born = t, ended = t + 40f, genome = Founder(0.5f) });

            int marker = NeutralReplay.TraitNames().IndexOf("MarkerB");
            Random.State saved = Random.state;
            try
            {
                double drift = 0;
                bool anyMoved = false;
                const int runs = 200;
                for (int r = 0; r < runs; r++)
                {
                    Random.InitState(1000 + r);
                    float last = 0.5f;
                    NeutralReplay.Run(lives, settings, 10f, 300f, true, (t, living) =>
                    {
                        last = NeutralReplay.Means(living)[marker];
                    });
                    drift += last - 0.5;
                    anyMoved |= Mathf.Abs(last - 0.5f) > 1e-4f;
                }

                Assert.IsTrue(anyMoved, "Mutation should move the marker in some replays.");
                Assert.That(drift / runs, Is.EqualTo(0).Within(0.02), "Mutation alone doesn't move a marker's mean.");
            }
            finally
            {
                Random.state = saved;
            }
        }

        [Test]
        public void TraitNamesMatchTheLifeRecordColumns()
        {
            List<string> names = NeutralReplay.TraitNames();
            CollectionAssert.Contains(names, "harmful_mutations");
            CollectionAssert.Contains(names, "Perk_ThickFur");
            CollectionAssert.Contains(names, "BodyBulk");
            CollectionAssert.Contains(names, "AgeingOnset");
            CollectionAssert.Contains(names, "Parts_Legs");
            Assert.AreEqual("generation", names[names.Count - 1]);

            AnimalGenome genome = Founder(0.5f);
            genome.SetPart(BodySite.FrontPair, BodyPartType.Legs, 0.4f);
            genome.SetPart(BodySite.RearPair, BodyPartType.Legs, 0.3f);
            float[] means = NeutralReplay.Means(new[] { new NeutralReplay.Living(genome, 7) });
            Assert.AreEqual(0.7f, means[names.IndexOf("Parts_Legs")], 1e-5f, "Part totals add the sites, as the gradients tool does.");
            Assert.AreEqual(7f, means[names.IndexOf("generation")]);
        }
    }
}
