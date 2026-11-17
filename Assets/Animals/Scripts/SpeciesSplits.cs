using System.Collections.Generic;

namespace Boreal.Animals
{
    // Item 64: follows groups that have drifted apart from their species from one census to the next, so a group is
    // named a species only once the gap has lasted. A group seen at a census is the same candidate as one seen before
    // when most of its members were in that candidate, or are children of its members; each census it is seen again
    // adds to its streak, and a candidate not seen at a census is forgotten, so its count starts again.
    // Pure code, so it is tested without a scene.
    public sealed class SpeciesSplitTracker
    {
        // An animal of a group: its ID and its parents' (0 for none).
        public readonly struct Member
        {
            public readonly int id, firstParent, secondParent;

            public Member(int id, int firstParent, int secondParent)
            {
                this.id = id;
                this.firstParent = firstParent;
                this.secondParent = secondParent;
            }
        }

        readonly Dictionary<int, int> candidateOfAnimal = new Dictionary<int, int>();
        readonly Dictionary<int, int> streaks = new Dictionary<int, int>();
        readonly HashSet<int> seenThisCensus = new HashSet<int>();
        readonly Dictionary<int, int> tally = new Dictionary<int, int>();
        readonly List<int> scratch = new List<int>();
        int nextCandidate = 1;

        public int CandidateCount => streaks.Count;

        // Call once for each separated group at a census. Returns how many censuses in a row this group has now been
        // seen apart, 1 for a group seen for the first time.
        public int Observe(IReadOnlyList<Member> group)
        {
            tally.Clear();
            foreach (Member member in group)
            {
                if (!candidateOfAnimal.TryGetValue(member.id, out int candidate) &&
                    !candidateOfAnimal.TryGetValue(member.firstParent, out candidate) &&
                    !candidateOfAnimal.TryGetValue(member.secondParent, out candidate)) continue;
                tally.TryGetValue(candidate, out int count);
                tally[candidate] = count + 1;
            }

            int best = 0, bestCount = 0;
            foreach (KeyValuePair<int, int> entry in tally)
            {
                if (entry.Value > bestCount)
                {
                    best = entry.Key;
                    bestCount = entry.Value;
                }
            }

            // The same candidate when most of the group belongs to it, and no other group has claimed it this census.
            bool known = best != 0 && bestCount * 2 > group.Count && streaks.ContainsKey(best) &&
                         !seenThisCensus.Contains(best);
            int id = known ? best : nextCandidate++;
            streaks.TryGetValue(id, out int streak);
            streaks[id] = ++streak;
            seenThisCensus.Add(id);
            foreach (Member member in group) candidateOfAnimal[member.id] = id;
            return streak;
        }

        // Call at the end of each census: candidates not seen at it are forgotten.
        public void EndCensus()
        {
            scratch.Clear();
            foreach (int candidate in streaks.Keys)
            {
                if (!seenThisCensus.Contains(candidate)) scratch.Add(candidate);
            }
            foreach (int candidate in scratch) streaks.Remove(candidate);

            scratch.Clear();
            foreach (KeyValuePair<int, int> entry in candidateOfAnimal)
            {
                if (!streaks.ContainsKey(entry.Value)) scratch.Add(entry.Key);
            }
            foreach (int animal in scratch) candidateOfAnimal.Remove(animal);
            seenThisCensus.Clear();
        }

        // A group that has become a species is no longer a candidate.
        public void Forget(IReadOnlyList<Member> group)
        {
            foreach (Member member in group)
            {
                if (candidateOfAnimal.TryGetValue(member.id, out int candidate))
                {
                    streaks.Remove(candidate);
                    seenThisCensus.Remove(candidate);
                }
            }
        }

        public void Clear()
        {
            candidateOfAnimal.Clear();
            streaks.Clear();
            seenThisCensus.Clear();
            nextCandidate = 1;
        }
    }
}
