using NUnit.Framework;

namespace Boreal.Animals.Tests
{
    // When animals are active (item 74): one chronotype gene covers day, night, and dawn-and-dusk animals.
    public class ChronotypeTests
    {
        const float Tolerance = 1e-4f;

        [Test]
        public void DayAnimalsAreActiveByDayAndNightAnimalsByNight()
        {
            Assert.AreEqual(1f, SeekFood.ActivityAt(0f, 1f, 0f), Tolerance);
            Assert.AreEqual(0f, SeekFood.ActivityAt(0f, 0f, 0f), Tolerance);
            Assert.AreEqual(0f, SeekFood.ActivityAt(1f, 1f, 0f), Tolerance);
            Assert.AreEqual(1f, SeekFood.ActivityAt(1f, 0f, 0f), Tolerance);

            // Founders (0.35) rest at night and are busy at dusk.
            Assert.Less(SeekFood.ActivityAt(0.35f, 0f, 0f), 0.5f);
            Assert.Greater(SeekFood.ActivityAt(0.35f, 1f, 0f), 0.5f);
            Assert.Greater(SeekFood.ActivityAt(0.35f, 0.3f, 1f), 1f);
        }

        // Halfway, an animal is never quite at rest and busiest at twilight: the crepuscular and cathemeral middle.
        [Test]
        public void MiddleChronotypesAreBusiestAtDawnAndDusk()
        {
            Assert.AreEqual(0.5f, SeekFood.ActivityAt(0.5f, 1f, 0f), Tolerance);
            Assert.AreEqual(0.5f, SeekFood.ActivityAt(0.5f, 0f, 0f), Tolerance);
            Assert.AreEqual(1.5f, SeekFood.ActivityAt(0.5f, 0.5f, 1f), Tolerance);
        }

        // Animals kept as numbers rest for their off hours' share of the day: the night for day animals, the day for
        // night animals.
        [Test]
        public void NumbersRestForTheirOffHoursShareOfTheDay()
        {
            Assert.AreEqual(0.2f, SeekFood.RestShareOfDay(0.35f, 0.8f), Tolerance, "a day animal sleeps the short summer night");
            Assert.AreEqual(0.8f, SeekFood.RestShareOfDay(0.9f, 0.8f), Tolerance, "a night animal sleeps the long summer day");
            Assert.AreEqual(0f, SeekFood.RestShareOfDay(0.5f, 0.8f), Tolerance);
        }

        [Test]
        public void ChronotypesHaveNames()
        {
            Assert.AreEqual("by day", SeekFood.DescribeChronotype(0f));
            Assert.AreEqual("mostly by day", SeekFood.DescribeChronotype(0.35f));
            Assert.AreEqual("at dawn and dusk", SeekFood.DescribeChronotype(0.5f));
            Assert.AreEqual("by night", SeekFood.DescribeChronotype(1f));
        }
    }
}
