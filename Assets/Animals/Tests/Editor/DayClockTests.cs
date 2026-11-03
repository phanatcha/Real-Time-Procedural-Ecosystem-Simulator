using NUnit.Framework;

namespace Boreal.Animals.Tests
{
    // How the day clock names the time for the panel and the soak logs (item 71).
    public class DayClockTests
    {
        [Test]
        public void TimesOfDayReadAsAClock()
        {
            Assert.AreEqual("00:00", DayClock.ClockTime(0f));
            Assert.AreEqual("07:12", DayClock.ClockTime(0.3f));
            Assert.AreEqual("12:00", DayClock.ClockTime(0.5f));
            Assert.AreEqual("00:00", DayClock.ClockTime(1f));
        }

        // After a long frame the light glides to the clock's time over a fraction of a second instead of jumping; a
        // new world, or a jump of over a quarter of a day, is shown at once.
        [Test]
        public void TheLightGlidesAfterALongFrame()
        {
            float shown = 2.0f;
            float exact = 2.1f;
            float next = DayClock.ShownDays(shown, exact, 1f / 60f, 0.15f);
            Assert.Greater(next, shown);
            Assert.Less(next, exact);
            for (int frame = 0; frame < 60; frame++) next = DayClock.ShownDays(next, exact, 1f / 60f, 0.15f);
            Assert.AreEqual(exact, next, 1e-3f, "caught up within a second");

            Assert.AreEqual(5f, DayClock.ShownDays(-1f, 5f, 1f / 60f, 0.15f), "a new world starts on time");
            Assert.AreEqual(3f, DayClock.ShownDays(2f, 3f, 1f / 60f, 0.15f), "a big jump is taken at once");
            Assert.AreEqual(2.1f, DayClock.ShownDays(2f, 2.1f, 1f / 60f, 0f), "0 shows the exact time");
        }

        [Test]
        public void MoonPhasesHaveTheirNames()
        {
            Assert.AreEqual("new moon", DayClock.MoonName(0f));
            Assert.AreEqual("first quarter", DayClock.MoonName(0.25f));
            Assert.AreEqual("full moon", DayClock.MoonName(0.5f));
            Assert.AreEqual("last quarter", DayClock.MoonName(0.75f));
            Assert.AreEqual("new moon", DayClock.MoonName(0.97f));
        }
    }
}
