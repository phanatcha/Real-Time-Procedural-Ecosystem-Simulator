using System.Collections.Generic;
using NUnit.Framework;

namespace Boreal.Animals.Tests
{
    // Family lines for the Muller plot (item 65), on a made-up family with a known answer. Tools/muller_plot.py checks
    // the same family with --self-test, so the in-game view and the exported plot agree.
    public class FamilyLinesTests
    {
        const double Forever = double.PositiveInfinity;

        // Founder 1's line: 30 children that each leave nothing (no line each); branch 50, born at 250, with 25 children
        // living from 300 to 600 (a line that grows and dies out); branch 100, born at 200, whose 60 children are born
        // from 250 to 840 and live on (a line that takes over), with child 101 at 250 whose own line is only itself;
        // and 500, a child of 100 born at 600 that gained perk 0 and has 10 children with it (a perk line). Founder 2's
        // line: 20 children that all die at 500, so it dies out.
        public static List<LifeSpan> MadeUpFamily()
        {
            List<LifeSpan> lives = new List<LifeSpan>
            {
                new LifeSpan { id = 1, born = -10, ended = 300 },
                new LifeSpan { id = 2, born = -10, ended = 200 }
            };
            for (int id = 10; id < 40; id++) lives.Add(new LifeSpan { id = id, parent = 1, born = 10, ended = 300 });
            lives.Add(new LifeSpan { id = 50, parent = 1, born = 250, ended = 400 });
            for (int id = 51; id <= 75; id++) lives.Add(new LifeSpan { id = id, parent = 50, born = 300, ended = 600 });
            lives.Add(new LifeSpan { id = 100, parent = 1, born = 200, ended = 400 });
            for (int k = 0; k < 60; k++) lives.Add(new LifeSpan { id = 101 + k, parent = 100, born = 250 + 10 * k, ended = Forever });
            lives.Add(new LifeSpan { id = 500, parent = 100, born = 600, ended = Forever, perks = 1 });
            for (int id = 501; id <= 510; id++) lives.Add(new LifeSpan { id = id, parent = 500, born = 650, ended = Forever, perks = 1 });
            for (int id = 200; id < 220; id++) lives.Add(new LifeSpan { id = id, parent = 2, born = 10, ended = 500 });
            return lives;
        }

        public static double[] Samples()
        {
            double[] times = new double[21];
            for (int s = 0; s < times.Length; s++) times[s] = 50 * s;
            return times;
        }

        static List<string> Names(FamilyLineChart chart)
        {
            List<string> names = new List<string>();
            foreach (FamilyLine line in chart.lines) names.Add(line.name);
            return names;
        }

        [Test]
        public void FoundersAndBranchesThatGrewBigAreLinesNestedInTheirParents()
        {
            FamilyLineChart chart = FamilyLineChart.Build(MadeUpFamily(), Samples(), new FamilyLineRules());
            CollectionAssert.AreEqual(new[] { "F1", "L50", "L100", "L500", "F2" }, Names(chart));
            FamilyLine founder = chart.lines[0], dying = chart.lines[1], winner = chart.lines[2], perk = chart.lines[3];
            Assert.AreEqual(-1, founder.parent);
            Assert.AreEqual(0, dying.parent);
            Assert.AreEqual(0, winner.parent);
            Assert.AreEqual(2, perk.parent);
            Assert.AreEqual(0, perk.perk, "The perk line began with perk 0.");
            Assert.AreEqual(-1, winner.perk);
            Assert.IsTrue(chart.lines[4].founder);
            Assert.AreEqual(1f, perk.perkShares[0], 1e-6f);
        }

        [Test]
        public void LinesAddUpToThePopulationAndShowTheTakeover()
        {
            FamilyLineChart chart = FamilyLineChart.Build(MadeUpFamily(), Samples(), new FamilyLineRules());
            FamilyLine founder = chart.lines[0], dying = chart.lines[1], winner = chart.lines[2], other = chart.lines[4];
            int last = chart.times.Length - 1;
            for (int s = 0; s < chart.times.Length; s++)
            {
                Assert.AreEqual(chart.totals[s], founder.clade[s] + other.clade[s], $"Sample {s}: founders' lines don't add up.");
                Assert.AreEqual(founder.clade[s], founder.own[s] + dying.clade[s] + winner.clade[s], $"Sample {s}");
                foreach (FamilyLine line in chart.lines) Assert.GreaterOrEqual(line.own[s], 0);
            }
            Assert.AreEqual(71, chart.totals[last]);
            Assert.AreEqual(71, winner.clade[last], "Branch 100 should hold every animal at the end.");
            Assert.AreEqual(0, dying.clade[last]);
            Assert.AreEqual(0, other.clade[last], "Founder 2's line should have died out.");
            Assert.AreEqual(1f, winner.peakShare, 1e-6f);
        }

        [Test]
        public void BandsAreNestedInsideTheirParentsAndFillTheChart()
        {
            FamilyLineChart chart = FamilyLineChart.Build(MadeUpFamily(), Samples(), new FamilyLineRules());
            float[] lower = new float[chart.lines.Count], upper = new float[chart.lines.Count];
            for (int s = 1; s < chart.times.Length; s++)
            {
                chart.Bands(s, lower, upper);
                Assert.AreEqual(0f, lower[0], 1e-5f);
                Assert.AreEqual(1f, upper[4], 1e-5f, $"Sample {s}: the bands should fill the chart.");
                for (int i = 0; i < chart.lines.Count; i++)
                {
                    int parent = chart.lines[i].parent;
                    if (parent < 0) continue;
                    Assert.GreaterOrEqual(lower[i], lower[parent] - 1e-5f, $"Sample {s}: {chart.lines[i].name} sticks out below.");
                    Assert.LessOrEqual(upper[i], upper[parent] + 1e-5f, $"Sample {s}: {chart.lines[i].name} sticks out above.");
                }
            }
        }

        [Test]
        public void TooManyLinesRaiseTheShareNeeded()
        {
            FamilyLineRules rules = new FamilyLineRules { maximumLines = 1 };
            FamilyLineChart chart = FamilyLineChart.Build(MadeUpFamily(), Samples(), rules);
            List<string> names = Names(chart);
            CollectionAssert.Contains(names, "L100");
            CollectionAssert.DoesNotContain(names, "L50", "The smaller branch should drop out first.");
            Assert.Greater(chart.LineShareUsed, 0.45f);
        }

        // Without perk lines, branch 500 (21% of the animals at its peak) is still a line by the ordinary rule, unstriped;
        // asking for a quarter of the animals drops it.
        [Test]
        public void WithoutPerkLinesAPerkBranchIsJudgedLikeAnyOther()
        {
            FamilyLineChart chart = FamilyLineChart.Build(MadeUpFamily(), Samples(), new FamilyLineRules { perkMask = 0 });
            CollectionAssert.AreEqual(new[] { "F1", "L50", "L100", "L500", "F2" }, Names(chart));
            Assert.AreEqual(-1, chart.lines[3].perk);
            FamilyLineChart higher = FamilyLineChart.Build(MadeUpFamily(), Samples(),
                                                           new FamilyLineRules { perkMask = 0, lineShare = 0.25f });
            CollectionAssert.AreEqual(new[] { "F1", "L50", "L100", "F2" }, Names(higher));
        }
    }
}
