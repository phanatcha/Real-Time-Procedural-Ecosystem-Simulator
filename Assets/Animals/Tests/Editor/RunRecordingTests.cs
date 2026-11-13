using System;
using System.Globalization;
using NUnit.Framework;

namespace Boreal.Animals.Tests
{
    public class RunRecordingTests
    {
        [Test]
        public void CsvQuotesSeparatorsQuotesAndNewlinesAndUsesInvariantNumbers()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                Assert.AreEqual("1.25", CsvEncoding.Cell(1.25));
                Assert.AreEqual("\"a,\"\"b\"\"\nc\"", CsvEncoding.Cell("a,\"b\"\nc"));
                Assert.AreEqual("", CsvEncoding.Cell(null));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Test]
        public void StatisticsDescribeTheFullPopulationWithLargeOffsets()
        {
            var stats = new RunningStatistics();
            foreach (double value in new[] { 1000000001d, 1000000002d, 1000000003d }) stats.Add(value);
            Assert.AreEqual(3, stats.Count);
            Assert.AreEqual(1000000002d, stats.Mean);
            Assert.AreEqual(Math.Sqrt(2d / 3), stats.StandardDeviation, 1e-9);
            Assert.AreEqual(1000000001d, stats.Minimum);
            Assert.AreEqual(1000000003d, stats.Maximum);
        }

        [Test]
        public void GeneLimitsCountAnimalsAtEitherEndOfAGenesRange()
        {
            AnimalGeneDefinition range = AnimalGenome.GetDefinition(AnimalGene.MaturityTime);
            var genomes = new System.Collections.Generic.List<AnimalGenome>();
            // Two at the minimum, one at the maximum (a value past it clamps to it), two in between.
            foreach (float value in new[] { range.minimum, range.minimum, range.maximum + 50f, 45f, range.minimum + 0.5f })
            {
                AnimalGenome genome = AnimalGenome.Create();
                genome[AnimalGene.MaturityTime] = value;
                genomes.Add(genome);
            }
            GeneLimits.Count(genomes, AnimalGene.MaturityTime, out int atMinimum, out int atMaximum);
            Assert.AreEqual(2, atMinimum);
            Assert.AreEqual(1, atMaximum);
            GeneLimits.Count(null, AnimalGene.MaturityTime, out atMinimum, out atMaximum);
            Assert.AreEqual(0, atMinimum);
            Assert.AreEqual(0, atMaximum);
        }

        [Test]
        public void LifeRecordsHaveOneCellPerColumnAndLeaveMissingParentsBlank()
        {
            string[] columns = LifeRecords.Header().Split(',');
            AnimalGenome genome = AnimalGenome.Create();
            genome[AnimalGene.MaturityTime] = 45f;
            genome.SetPart(BodySite.FrontPair, BodyPartType.Legs, 0.35f);
            var cells = LifeRecords.Cells(7, 3, 0, "A", 2, "born", 12.5, null, 4.0, LifeRecords.StillAlive,
                                          1.0, 2.0, 3.0, 4.0, 1, genome);
            Assert.AreEqual(columns.Length, cells.Count);
            object Cell(string column) => cells[Array.IndexOf(columns, column)];
            Assert.AreEqual(7, Cell("id"));
            Assert.AreEqual(3, Cell("first_parent"));
            Assert.AreEqual("", Cell("second_parent"), "No second parent is blank, not 0.");
            Assert.AreEqual("", Cell("ended"), "An animal still alive has no end time.");
            Assert.AreEqual(45f, Cell("MaturityTime"));
            Assert.AreEqual("Legs:0.35", Cell("Part_FrontPair"));
            Assert.AreEqual("", Cell("Part_Head"));
            Assert.AreEqual(0, Cell("Perk_Venom"));
            // Every gene, perk and body site has a column.
            CollectionAssert.Contains(columns, AnimalGene.NightVision.ToString());
            Assert.AreEqual(16 + AnimalGenome.PerkCount + AnimalGenome.GeneCount + AnimalGenome.SiteCount, columns.Length);
        }

        [Test]
        public void SamplingPausesSkipsMissedFramesAndResetsWithoutFabricatingHistory()
        {
            var schedule = new SimulationSampleSchedule();
            Assert.IsTrue(schedule.Take(0, 10));
            Assert.IsFalse(schedule.Take(0, 10));
            Assert.IsFalse(schedule.Take(9.9, 10));
            Assert.IsTrue(schedule.Take(10.1, 10));
            Assert.IsTrue(schedule.Take(45, 10));
            Assert.IsFalse(schedule.Take(45, 10));
            Assert.IsTrue(schedule.Take(50, 10));
            schedule.Reset();
            Assert.IsTrue(schedule.Take(0, 10));
        }
    }
}
