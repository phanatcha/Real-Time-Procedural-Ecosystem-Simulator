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
