using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boreal.Animals
{
    // One animal's life, for the family lines (item 65): its ID, its first parent's ID (0 for a founder or an arrival
    // from off-screen), when it was born and when it ended (positive infinity while alive), in simulated seconds, and
    // the perks it was born with, as bits in AnimalPerk order.
    public struct LifeSpan
    {
        public int id;
        public int parent;
        public double born;
        public double ended;
        public int perks;

        public bool AliveAt(double time) => born <= time && time < ended;
    }

    // Which branches of the family tree count as lines of their own. Shares are of all animals alive at a sample, and
    // only samples with at least Minimum Animals alive count, so the first few animals don't each look like a takeover.
    [Serializable]
    public sealed class FamilyLineRules
    {
        [Tooltip("A branch becomes a line of its own once its descendants alive have been at least this share of all " +
                 "the animals at once.")]
        [Range(0.01f, 1f)] public float lineShare = 0.1f;
        [Tooltip("...and the line it grew in has, at some time while the branch lived, had at least this share of all " +
                 "the animals outside it, so a branch that is simply the whole of its line again isn't a line.")]
        [Range(0f, 1f)] public float outsideShare = 0.1f;
        [Tooltip("Shares count only at samples with at least this many animals alive.")]
        [Min(1)] public int minimumAnimals = 20;
        [Tooltip("Most branch lines drawn; Line Share is raised until no more than this many qualify.")]
        [Min(1)] public int maximumLines = 24;
        [Tooltip("Perks (bits in AnimalPerk order) whose gains start a line of their own, drawn striped.")]
        public int perkMask = (1 << AnimalGenome.PerkCount) - 1;
        [Tooltip("A branch that began with a perk gain is drawn once its descendants have been this share of all the " +
                 "animals.")]
        [Range(0.001f, 1f)] public float perkLineShare = 0.03f;
        [Min(0)] public int maximumPerkLines = 8;
    }

    // One family line: a founder's descendants, or a branch inside a line that grew big, nested in the line it grew in.
    public sealed class FamilyLine
    {
        // "F<id>" for a founder's line, "L<id>" for a branch, after the animal it descends from.
        public string name;
        public int parent = -1;
        public readonly List<int> children = new List<int>();
        public int rootAnimal;
        public double born;
        public bool founder;
        // The perk whose gain started this line (an AnimalPerk index), or -1.
        public int perk = -1;
        public int depth;
        // Animals alive in the line at each sample, with and without the lines inside it.
        public int[] clade;
        public int[] own;
        public float peakShare;
        public int peakSample;
        // The share of the line's animals alive at its peak that carry each perk.
        public float[] perkShares = new float[AnimalGenome.PerkCount];
    }

    // The family lines of a run, sampled at the given times (item 65): which lines there are, how many animals each
    // has at each sample, and how to stack them into a Muller plot, each line's band nested inside its parent's.
    // Lines follow first parents: a child of two parents belongs to the line of the parent that led the courtship.
    public sealed class FamilyLineChart
    {
        public readonly double[] times;
        public readonly int[] totals;
        // In drawing order: each line before the lines inside it, founders by ID, branches by birth.
        public readonly List<FamilyLine> lines = new List<FamilyLine>();
        public float LineShareUsed { get; private set; }

        FamilyLineChart(double[] times)
        {
            this.times = times;
            totals = new int[times.Length];
        }

        public static FamilyLineChart Build(IReadOnlyList<LifeSpan> lives, double[] times, FamilyLineRules rules)
        {
            rules ??= new FamilyLineRules();
            FamilyLineChart chart = new FamilyLineChart(times ?? new double[0]);
            int count = lives?.Count ?? 0;
            int samples = chart.times.Length;
            if (count == 0 || samples == 0) return chart;

            // The family tree, by first parent, in depth-first order so each animal's descendants are one range.
            Dictionary<int, int> byId = new Dictionary<int, int>(count);
            for (int i = 0; i < count; i++) byId[lives[i].id] = i;
            int[] parent = new int[count];
            List<int>[] kids = new List<int>[count];
            List<int> roots = new List<int>();
            for (int i = 0; i < count; i++)
            {
                parent[i] = lives[i].parent > 0 && byId.TryGetValue(lives[i].parent, out int p) && p != i ? p : -1;
                if (parent[i] < 0) roots.Add(i);
                else (kids[parent[i]] ??= new List<int>()).Add(i);
            }
            roots.Sort((a, b) => lives[a].id.CompareTo(lives[b].id));
            int[] order = new int[count];
            int[] enter = new int[count];
            int[] leave = new int[count];
            int next = 0;
            Stack<(int node, int child)> stack = new Stack<(int, int)>();
            foreach (int root in roots)
            {
                stack.Push((root, 0));
                enter[root] = next;
                order[next++] = root;
                while (stack.Count > 0)
                {
                    (int node, int child) = stack.Pop();
                    List<int> list = kids[node];
                    if (list != null && child < list.Count)
                    {
                        stack.Push((node, child + 1));
                        int kid = list[child];
                        enter[kid] = next;
                        order[next++] = kid;
                        stack.Push((kid, 0));
                    }
                    else
                    {
                        leave[node] = next;
                    }
                }
            }

            // Pass 1: each animal's largest share of all the animals, from prefix sums of the living over that order.
            int[] alive = new int[count + 1];
            float[] peak = new float[count];
            bool[] valid = new bool[samples];
            for (int s = 0; s < samples; s++)
            {
                PrefixAlive(lives, order, chart.times[s], alive);
                chart.totals[s] = alive[count];
                valid[s] = alive[count] >= Mathf.Max(1, rules.minimumAnimals);
                if (!valid[s]) continue;
                float total = alive[count];
                for (int i = 0; i < count; i++)
                {
                    int clade = alive[leave[i]] - alive[enter[i]];
                    if (clade > 0 && clade / total > peak[i]) peak[i] = clade / total;
                }
            }

            // The candidates: founders, branches big enough to be a line, and branches that began with a perk gain.
            float lowest = Mathf.Min(rules.lineShare, rules.perkLineShare);
            List<int> candidates = new List<int>();
            for (int k = 0; k < count; k++)
            {
                int i = order[k];
                if (parent[i] < 0 || peak[i] >= lowest) candidates.Add(i);
            }
            Dictionary<int, int[]> series = new Dictionary<int, int[]>(candidates.Count);
            foreach (int i in candidates) series[i] = new int[samples];
            for (int s = 0; s < samples; s++)
            {
                PrefixAlive(lives, order, chart.times[s], alive);
                foreach (int i in candidates) series[i][s] = alive[leave[i]] - alive[enter[i]];
            }

            // Pass 2: promote in tree order, so each candidate is judged against the nearest line above it. Too many
            // branch lines raise the share needed until they fit.
            float share = rules.lineShare;
            Dictionary<int, int> promoted = null;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                promoted = Promote(lives, candidates, parent, peak, series, valid, chart.totals, rules, share,
                                   out int branchLines);
                if (branchLines <= rules.maximumLines) break;
                share *= 1.2f;
            }
            chart.LineShareUsed = share;

            // The lines in tree order, with their parents and counts.
            Dictionary<int, int> lineOf = new Dictionary<int, int>();
            foreach (int i in candidates)
            {
                if (!promoted.TryGetValue(i, out int perk)) continue;
                FamilyLine line = new FamilyLine
                {
                    rootAnimal = lives[i].id,
                    born = lives[i].born,
                    founder = parent[i] < 0,
                    perk = perk,
                    clade = series[i],
                    own = (int[])series[i].Clone(),
                    peakShare = peak[i]
                };
                line.name = (line.founder ? "F" : "L") + line.rootAnimal;
                int above = NearestPromoted(parent, i, promoted);
                if (above >= 0)
                {
                    line.parent = lineOf[above];
                    FamilyLine parentLine = chart.lines[line.parent];
                    line.depth = parentLine.depth + 1;
                    parentLine.children.Add(chart.lines.Count);
                    for (int s = 0; s < samples; s++) parentLine.own[s] -= line.clade[s];
                }
                lineOf[i] = chart.lines.Count;
                chart.lines.Add(line);
            }

            // Each line's peak, and the perks its animals carried then.
            foreach (KeyValuePair<int, int> entry in lineOf)
            {
                FamilyLine line = chart.lines[entry.Value];
                int best = 0;
                for (int s = 0; s < samples; s++)
                {
                    if (line.clade[s] > line.clade[best]) best = s;
                }
                line.peakSample = best;
                int carriers = 0;
                int[] withPerk = new int[AnimalGenome.PerkCount];
                for (int k = enter[entry.Key]; k < leave[entry.Key]; k++)
                {
                    LifeSpan life = lives[order[k]];
                    if (!life.AliveAt(chart.times[best])) continue;
                    carriers++;
                    for (int p = 0; p < AnimalGenome.PerkCount; p++) if ((life.perks & (1 << p)) != 0) withPerk[p]++;
                }
                for (int p = 0; p < AnimalGenome.PerkCount; p++) line.perkShares[p] = carriers > 0 ? withPerk[p] / (float)carriers : 0f;
            }
            return chart;
        }

        // Fills alive[k + 1] with how many of the first k animals in tree order are alive at the time.
        static void PrefixAlive(IReadOnlyList<LifeSpan> lives, int[] order, double time, int[] alive)
        {
            alive[0] = 0;
            for (int k = 0; k < order.Length; k++) alive[k + 1] = alive[k] + (lives[order[k]].AliveAt(time) ? 1 : 0);
        }

        // The animals that start a line, each with the perk that started it (or -1).
        static Dictionary<int, int> Promote(IReadOnlyList<LifeSpan> lives, List<int> candidates, int[] parent, float[] peak,
                                            Dictionary<int, int[]> series, bool[] valid, int[] totals, FamilyLineRules rules,
                                            float share, out int branchLines)
        {
            Dictionary<int, int> promoted = new Dictionary<int, int>();
            branchLines = 0;
            List<(int animal, float peak, int perk)> perkLines = new List<(int, float, int)>();
            foreach (int i in candidates)
            {
                if (parent[i] < 0)
                {
                    promoted[i] = -1;
                    continue;
                }
                int gained = lives[i].perks & ~lives[parent[i]].perks & rules.perkMask;
                if (gained != 0 && peak[i] >= rules.perkLineShare)
                {
                    perkLines.Add((i, peak[i], LowestBit(gained)));
                    continue;
                }
                if (peak[i] < share) continue;
                int above = NearestPromoted(parent, i, promoted);
                if (above < 0) continue;
                int[] mine = series[i], theirs = series[above];
                bool distinct = false;
                for (int s = 0; s < mine.Length && !distinct; s++)
                {
                    if (!valid[s] || mine[s] <= 0) continue;
                    distinct = (theirs[s] - mine[s]) / (float)totals[s] >= rules.outsideShare;
                }
                if (!distinct) continue;
                promoted[i] = -1;
                branchLines++;
            }

            // The largest perk lines, added afterwards so they never stop a branch around them being judged.
            perkLines.Sort((a, b) => b.peak.CompareTo(a.peak));
            for (int k = 0; k < perkLines.Count && k < rules.maximumPerkLines; k++)
                promoted[perkLines[k].animal] = perkLines[k].perk;
            return promoted;
        }

        static int NearestPromoted(int[] parent, int animal, Dictionary<int, int> promoted)
        {
            for (int above = parent[animal]; above >= 0; above = parent[above])
            {
                if (promoted.ContainsKey(above)) return above;
            }
            return -1;
        }

        static int LowestBit(int bits)
        {
            for (int p = 0; p < 31; p++) if ((bits & (1 << p)) != 0) return p;
            return -1;
        }

        // Each line's band at a sample, as shares from 0 to 1: a line takes the share of its whole clade, its own animals
        // split half below and half above the lines inside it, as Muller plots draw them. Lines with nobody alive get an
        // empty band. lower and upper must hold one value per line.
        public void Bands(int sample, float[] lower, float[] upper)
        {
            int total = totals[sample];
            float y = 0f;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].parent < 0) y = Place(i, sample, y, total, lower, upper);
            }
        }

        float Place(int index, int sample, float y, int total, float[] lower, float[] upper)
        {
            FamilyLine line = lines[index];
            float height = total > 0 ? line.clade[sample] / (float)total : 0f;
            lower[index] = y;
            upper[index] = y + height;
            float inner = y + (total > 0 ? Mathf.Max(0, line.own[sample]) * 0.5f / total : 0f);
            foreach (int child in line.children) inner = Place(child, sample, inner, total, lower, upper);
            return y + height;
        }
    }
}
