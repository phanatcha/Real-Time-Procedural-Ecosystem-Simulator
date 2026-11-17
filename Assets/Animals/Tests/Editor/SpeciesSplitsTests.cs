using System.Collections.Generic;
using NUnit.Framework;

namespace Boreal.Animals.Tests
{
    // Species splits that stick (item 64): the tracker that follows separated groups from census to census, and the
    // trait that sets two groups furthest apart.
    public class SpeciesSplitsTests
    {
        static List<SpeciesSplitTracker.Member> Group(params int[] ids)
        {
            var group = new List<SpeciesSplitTracker.Member>();
            foreach (int id in ids) group.Add(new SpeciesSplitTracker.Member(id, 0, 0));
            return group;
        }

        [Test]
        public void AGroupSeenApartCensusAfterCensusBuildsAStreak()
        {
            var tracker = new SpeciesSplitTracker();
            for (int census = 1; census <= 6; census++)
            {
                Assert.AreEqual(census, tracker.Observe(Group(1, 2, 3)));
                tracker.EndCensus();
            }
        }

        [Test]
        public void AGroupThatRejoinsForOneCensusStartsAgain()
        {
            var tracker = new SpeciesSplitTracker();
            Assert.AreEqual(1, tracker.Observe(Group(1, 2, 3)));
            tracker.EndCensus();
            Assert.AreEqual(2, tracker.Observe(Group(1, 2, 3)));
            tracker.EndCensus();
            // A census where an in-between animal links the group back: it isn't seen apart.
            tracker.EndCensus();
            Assert.AreEqual(0, tracker.CandidateCount);
            Assert.AreEqual(1, tracker.Observe(Group(1, 2, 3)), "The count starts again after a census together.");
        }

        [Test]
        public void TheGroupIsFollowedThroughDeathsAndBirths()
        {
            var tracker = new SpeciesSplitTracker();
            tracker.Observe(Group(1, 2, 3));
            tracker.EndCensus();
            // Animal 1 died; 4 and 5 are children of 2 and 3; 6 is a child of two parents, the second in the group.
            var next = new List<SpeciesSplitTracker.Member>
            {
                new SpeciesSplitTracker.Member(2, 0, 0), new SpeciesSplitTracker.Member(3, 0, 0),
                new SpeciesSplitTracker.Member(4, 2, 0), new SpeciesSplitTracker.Member(5, 3, 0),
                new SpeciesSplitTracker.Member(6, 99, 3)
            };
            Assert.AreEqual(2, tracker.Observe(next));
            tracker.EndCensus();
            // A generation on, only the children are left.
            Assert.AreEqual(3, tracker.Observe(new List<SpeciesSplitTracker.Member>
            {
                new SpeciesSplitTracker.Member(7, 4, 0), new SpeciesSplitTracker.Member(8, 5, 6),
                new SpeciesSplitTracker.Member(9, 6, 0)
            }));
        }

        [Test]
        public void TwoGroupsAtOnceKeepTheirOwnCounts()
        {
            var tracker = new SpeciesSplitTracker();
            tracker.Observe(Group(1, 2, 3));
            tracker.EndCensus();
            Assert.AreEqual(2, tracker.Observe(Group(1, 2, 3)));
            Assert.AreEqual(1, tracker.Observe(Group(10, 11, 12)), "A different group is a new candidate.");
            tracker.EndCensus();
            Assert.AreEqual(2, tracker.CandidateCount);
            Assert.AreEqual(2, tracker.Observe(Group(10, 11, 12)));
            // A group mostly of strangers is not the old candidate.
            Assert.AreEqual(1, tracker.Observe(Group(1, 20, 21, 22)));
        }

        [Test]
        public void AGroupNamedASpeciesIsForgotten()
        {
            var tracker = new SpeciesSplitTracker();
            tracker.Observe(Group(1, 2, 3));
            tracker.Forget(Group(1, 2, 3));
            tracker.EndCensus();
            Assert.AreEqual(0, tracker.CandidateCount);
        }

        [Test]
        public void TheMostDifferentTraitIsTheLargestTermOfTheDistance()
        {
            AnimalGenome Base()
            {
                AnimalGenome genome = AnimalGenome.Create();
                genome[AnimalGene.DietAffinity] = 0.2f;
                genome[AnimalGene.MoveSpeed] = 6f;
                return genome;
            }

            var first = new List<AnimalGenome> { Base(), Base() };
            AnimalGenome meatEater = Base();
            meatEater[AnimalGene.DietAffinity] = 0.9f;
            meatEater[AnimalGene.MoveSpeed] = 6.3f;
            var second = new List<AnimalGenome> { meatEater, meatEater.Clone() };
            Assert.AreEqual("DietAffinity", AnimalGenome.MostDifferentTrait(first, second, out float share));
            Assert.That(share, Is.InRange(0.9f, 1f));

            AnimalGenome legged = Base();
            legged.SetPart(BodySite.FrontPair, BodyPartType.Legs, 1f);
            Assert.AreEqual("body part at FrontPair",
                            AnimalGenome.MostDifferentTrait(first, new List<AnimalGenome> { legged }, out _));

            AnimalGenome furred = Base();
            furred.SetPerk(AnimalPerk.ThickFur, true);
            Assert.AreEqual("perk ThickFur", AnimalGenome.MostDifferentTrait(first, new List<AnimalGenome> { furred }, out _));

            // The marker perk counts for nothing, as in Distance.
            AnimalGenome marked = Base();
            marked.SetPerk(AnimalPerk.Marker, true);
            AnimalGenome.MostDifferentTrait(first, new List<AnimalGenome> { marked }, out float none);
            Assert.AreEqual(0f, none);

            Assert.AreEqual(AnimalGenome.Distance(Base(), meatEater),
                            AnimalGenome.ClosestDistance(first, second), 1e-6f);
        }
    }
}
