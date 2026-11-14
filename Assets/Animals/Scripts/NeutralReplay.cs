using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Boreal.Animals
{
    // Item 59: replays a recorded run with selection taken out. Every animal of the record keeps its place in time,
    // born when it was born and gone when it died, so the population, its age structure and the seasons are those of
    // the real run, and the young get their genes by the game's own rules (Recombine and CreateChild). Two ways:
    //
    // - Random parents (the default): each birth takes its parents at random from the animals alive then, among those
    //   about the age of the real parents. Who breeds and how many young they have no longer depend on genes, so what
    //   a trait does in the replays is what drift and the mutation rules do on their own.
    // - Family tree: each animal keeps its real parents, so the family tree is the real one, and only the mutations
    //   down it are drawn afresh. Which lines grew stays as it was, luck and place included; what is gone is any link
    //   between those lines and the mutations they carried. Its demography is exact, but the tree itself was shaped by
    //   selection, so it finds less: a check, not the main test.
    //
    // A trait that also moves this far in the replays moved by chance or by a bias in mutation, not by adaptation. The
    // do-nothing markers (item 56) should end outside the replays' band as often as the band says, either way; on 56
    // recorded runs they did (PROGRESS.md, item 59).
    //
    // Pure code with no scene, so it also runs outside Unity (Tools/replay, through Tools/neutral_replay.py).
    public static class NeutralReplay
    {
        public const string FounderOrigin = "founder";
        public const string BornOrigin = "born";

        // One animal of a recording (a row of lives.csv).
        public sealed class Life
        {
            public int id;
            public int firstParent;
            // 0 for a clone.
            public int secondParent;
            public string origin = BornOrigin;
            public float born;
            // Infinity for an animal still alive when the recording stopped.
            public float ended = float.PositiveInfinity;
            public int generation;
            // Its real genes. The replay starts founders from them, and reads them for the real run's own line.
            public AnimalGenome genome;
        }

        // The rules SeekFood breeds with (its Mutation and Perks settings), the same for every animal of a run.
        public struct Settings
        {
            public float mutationChance;
            public float mutationMagnitude;
            public BodyPlanMutation bodyPlanMutation;
            public float perkGainChance;
            public float perkLossChance;
            public float harmfulMutationChance;
            // A parent is picked among the animals whose age is within this share of the real parent's age (and at
            // least Minimum Age Window seconds either side); with none, the animal nearest in age.
            public float ageWindowShare;
            public float minimumAgeWindow;
            // False (the default): random parents. True: every animal keeps its real parents and only the mutations are
            // drawn afresh.
            public bool keepFamilyTree;

            // SeekFood's defaults and the founders' (AnimalTerrainDemoBootstrap).
            public static Settings Default => new Settings
            {
                mutationChance = 5f,
                mutationMagnitude = 0.1f,
                bodyPlanMutation = BodyPlanMutation.Default,
                perkGainChance = 0.2f,
                perkLossChance = 2f,
                harmfulMutationChance = 10f,
                ageWindowShare = 0.25f,
                minimumAgeWindow = 5f,
                keepFamilyTree = false
            };
        }

        // An animal alive in the replay (or, with replay false, in the real run) at a sample time.
        public readonly struct Living
        {
            public readonly AnimalGenome genome;
            public readonly int generation;

            public Living(AnimalGenome genome, int generation)
            {
                this.genome = genome;
                this.generation = generation;
            }
        }

        sealed class Slot
        {
            public Life life;
            public AnimalGenome genome;
            public int generation;
            public int index;
        }

        enum EventKind { Arrive = 0, Birth = 1, Leave = 2 }

        readonly struct Event
        {
            public readonly float time;
            public readonly EventKind kind;
            // Arrive and Leave: the one animal. Birth: the litter, all born together to the same parents.
            public readonly List<Life> lives;

            public Event(float time, EventKind kind, List<Life> lives)
            {
                this.time = time;
                this.kind = kind;
                this.lives = lives;
            }
        }

        // Runs one replay, or the real record itself with replay false, and hands the animals alive at every sample
        // time (0, interval, 2 × interval … up to endTime) to sample. Seed UnityEngine.Random before calling it.
        public static void Run(IReadOnlyList<Life> lives, Settings settings, float interval, float endTime, bool replay,
                               Action<float, IReadOnlyList<Living>> sample)
        {
            if (interval <= 0f) throw new ArgumentOutOfRangeException(nameof(interval));
            Dictionary<int, Life> byId = new Dictionary<int, Life>();
            foreach (Life life in lives) byId[life.id] = life;
            List<Event> events = BuildEvents(lives);

            List<Slot> living = new List<Slot>();
            Dictionary<int, Slot> slotById = new Dictionary<int, Slot>();
            List<Living> snapshot = new List<Living>();
            float nextSample = 0f;
            int next = 0;
            while (nextSample <= endTime + 1e-4f)
            {
                // Everything that happened up to and at this sample time, then the sample.
                while (next < events.Count && events[next].time <= nextSample)
                {
                    Apply(events[next], living, slotById, byId, settings, replay);
                    next++;
                }

                snapshot.Clear();
                foreach (Slot slot in living) snapshot.Add(new Living(slot.genome, slot.generation));
                sample(nextSample, snapshot);
                nextSample += interval;
            }
        }

        static List<Event> BuildEvents(IReadOnlyList<Life> lives)
        {
            List<Event> events = new List<Event>();
            Dictionary<(int, int, long), List<Life>> litters = new Dictionary<(int, int, long), List<Life>>();
            foreach (Life life in lives)
            {
                if (life.origin == BornOrigin && life.firstParent != 0)
                {
                    // Young of one birth share their parents and their birth time.
                    var key = (life.firstParent, life.secondParent, (long)Math.Round(life.born * 1000.0));
                    if (!litters.TryGetValue(key, out List<Life> litter))
                    {
                        litter = new List<Life>();
                        litters[key] = litter;
                        events.Add(new Event(life.born, EventKind.Birth, litter));
                    }
                    litter.Add(life);
                }
                else
                {
                    events.Add(new Event(life.born, EventKind.Arrive, new List<Life> { life }));
                }

                if (!float.IsInfinity(life.ended))
                {
                    events.Add(new Event(life.ended, EventKind.Leave, new List<Life> { life }));
                }
            }

            // In time order; at the same moment, arrivals and births before deaths. The sort is stable, so ties keep
            // the record's order and a replay with the same seed gives the same result.
            List<Event> ordered = new List<Event>(events.Count);
            ordered.AddRange(events);
            MergeSort(ordered);
            return ordered;
        }

        static void MergeSort(List<Event> list)
        {
            Event[] items = list.ToArray();
            Event[] buffer = new Event[items.Length];
            for (int width = 1; width < items.Length; width *= 2)
            {
                for (int start = 0; start < items.Length; start += 2 * width)
                {
                    int middle = Math.Min(start + width, items.Length);
                    int end = Math.Min(start + 2 * width, items.Length);
                    int left = start, right = middle, write = start;
                    while (left < middle && right < end)
                        buffer[write++] = Compare(items[right], items[left]) < 0 ? items[right++] : items[left++];
                    while (left < middle) buffer[write++] = items[left++];
                    while (right < end) buffer[write++] = items[right++];
                }

                (items, buffer) = (buffer, items);
            }

            list.Clear();
            list.AddRange(items);
        }

        static int Compare(Event a, Event b)
        {
            int byTime = a.time.CompareTo(b.time);
            return byTime != 0 ? byTime : ((int)a.kind).CompareTo((int)b.kind);
        }

        static void Apply(Event e, List<Slot> living, Dictionary<int, Slot> slotById, Dictionary<int, Life> byId,
                          Settings settings, bool replay)
        {
            switch (e.kind)
            {
                case EventKind.Arrive:
                {
                    Life life = e.lives[0];
                    AnimalGenome genome = life.genome;
                    int generation = life.generation;
                    // An animal back from an off-screen population descends from the island's own animals: in the
                    // replay it is a copy of one alive now. Founders bring their own genes.
                    if (replay && life.origin != FounderOrigin && living.Count > 0)
                    {
                        Slot source = living[Random.Range(0, living.Count)];
                        genome = source.genome;
                        generation = source.generation;
                    }
                    Add(living, slotById, life, genome != null ? genome.Clone() : null, generation);
                    break;
                }
                case EventKind.Birth:
                {
                    Life first = e.lives[0];
                    if (!replay)
                    {
                        foreach (Life child in e.lives) Add(living, slotById, child, child.genome?.Clone(), child.generation);
                        break;
                    }

                    Slot mother = settings.keepFamilyTree && slotById.TryGetValue(first.firstParent, out Slot realMother)
                        ? realMother
                        : PickParent(living, byId, first.firstParent, e.time, null, settings);
                    Slot father = null;
                    if (first.secondParent != 0)
                    {
                        father = settings.keepFamilyTree && slotById.TryGetValue(first.secondParent, out Slot realFather)
                            ? realFather
                            : PickParent(living, byId, first.secondParent, e.time, mother, settings);
                    }
                    if (mother == null)
                    {
                        // Nobody alive to be a parent (a record that starts mid-run): the young keep their real genes.
                        foreach (Life child in e.lives) Add(living, slotById, child, child.genome?.Clone(), child.generation);
                        break;
                    }

                    int generation = Math.Max(mother.generation, father != null ? father.generation : 0) + 1;
                    foreach (Life child in e.lives)
                    {
                        AnimalGenome inherited = father == null ? mother.genome : AnimalGenome.Recombine(mother.genome, father.genome);
                        AnimalGenome genome = inherited.CreateChild(settings.mutationChance, settings.mutationMagnitude,
                                                                    settings.bodyPlanMutation, settings.perkGainChance,
                                                                    settings.perkLossChance, settings.harmfulMutationChance,
                                                                    out _);
                        Add(living, slotById, child, genome, generation);
                    }
                    break;
                }
                case EventKind.Leave:
                {
                    if (!slotById.TryGetValue(e.lives[0].id, out Slot slot)) break;
                    // Swap-remove keeps removal cheap; the order of the living list carries no meaning.
                    Slot last = living[living.Count - 1];
                    living[slot.index] = last;
                    last.index = slot.index;
                    living.RemoveAt(living.Count - 1);
                    slotById.Remove(slot.life.id);
                    break;
                }
            }
        }

        static void Add(List<Slot> living, Dictionary<int, Slot> slotById, Life life, AnimalGenome genome, int generation)
        {
            if (genome == null || slotById.ContainsKey(life.id)) return;
            Slot slot = new Slot { life = life, genome = genome, generation = generation, index = living.Count };
            living.Add(slot);
            slotById[life.id] = slot;
        }

        // A parent for a birth at time: any animal alive then, other than excluded, whose age is near the real
        // parent's age at that birth. Age carries no genes in the replay, since every place's lifespan is the real
        // one, so this keeps the run's generation time without bringing selection back.
        static Slot PickParent(List<Slot> living, Dictionary<int, Life> byId, int realParent, float time, Slot excluded,
                               Settings settings)
        {
            int count = living.Count - (excluded != null ? 1 : 0);
            if (count <= 0) return null;

            float targetAge = byId.TryGetValue(realParent, out Life parent) ? Mathf.Max(0f, time - parent.born) : -1f;
            if (targetAge < 0f)
            {
                return RandomOther(living, excluded);
            }

            float window = Mathf.Max(settings.minimumAgeWindow, settings.ageWindowShare * targetAge);
            int candidates = 0;
            Slot nearest = null;
            float nearestGap = float.PositiveInfinity;
            foreach (Slot slot in living)
            {
                if (slot == excluded) continue;
                float gap = Mathf.Abs(time - slot.life.born - targetAge);
                if (gap <= window) candidates++;
                if (gap < nearestGap)
                {
                    nearestGap = gap;
                    nearest = slot;
                }
            }

            if (candidates == 0) return nearest;
            int pick = Random.Range(0, candidates);
            foreach (Slot slot in living)
            {
                if (slot == excluded || Mathf.Abs(time - slot.life.born - targetAge) > window) continue;
                if (pick-- == 0) return slot;
            }

            return nearest;
        }

        // Any animal alive other than excluded, all equally likely.
        static Slot RandomOther(List<Slot> living, Slot excluded)
        {
            if (excluded == null) return living[Random.Range(0, living.Count)];
            int pick = Random.Range(0, living.Count - 1);
            if (pick >= excluded.index) pick++;
            return living[pick];
        }

        // The traits the replay follows, by the names lives.csv and Tools/selection_gradients.py use: harmful
        // mutations, each perk's share, each gene, the total size of each kind of body part, and the generation.
        public static List<string> TraitNames()
        {
            List<string> names = new List<string> { "harmful_mutations" };
            foreach (AnimalPerk perk in Enum.GetValues(typeof(AnimalPerk))) names.Add("Perk_" + perk);
            foreach (AnimalGene gene in Enum.GetValues(typeof(AnimalGene))) names.Add(gene.ToString());
            foreach (BodyPartType type in Enum.GetValues(typeof(BodyPartType)))
            {
                if (type != BodyPartType.None) names.Add("Parts_" + type);
            }
            names.Add("generation");
            return names;
        }

        // One animal's value of every trait in TraitNames, in the same order.
        public static void TraitValues(Living animal, List<float> values)
        {
            values.Clear();
            AnimalGenome genome = animal.genome;
            values.Add(genome.HarmfulMutations);
            foreach (AnimalPerk perk in Enum.GetValues(typeof(AnimalPerk))) values.Add(genome.HasPerk(perk) ? 1f : 0f);
            foreach (AnimalGene gene in Enum.GetValues(typeof(AnimalGene))) values.Add(genome[gene]);
            foreach (BodyPartType type in Enum.GetValues(typeof(BodyPartType)))
            {
                if (type == BodyPartType.None) continue;
                float total = 0f;
                for (int site = 0; site < AnimalGenome.SiteCount; site++)
                {
                    if (genome.GetPartType((BodySite)site) == type) total += genome.GetPartSize((BodySite)site);
                }
                values.Add(total);
            }
            values.Add(animal.generation);
        }

        // The mean of every trait over the animals given, in TraitNames order; NaN when there are none.
        public static float[] Means(IReadOnlyList<Living> animals)
        {
            int count = TraitNames().Count;
            double[] sums = new double[count];
            List<float> values = new List<float>(count);
            foreach (Living animal in animals)
            {
                TraitValues(animal, values);
                for (int i = 0; i < count; i++) sums[i] += values[i];
            }

            float[] means = new float[count];
            for (int i = 0; i < count; i++) means[i] = animals.Count > 0 ? (float)(sums[i] / animals.Count) : float.NaN;
            return means;
        }
    }
}
