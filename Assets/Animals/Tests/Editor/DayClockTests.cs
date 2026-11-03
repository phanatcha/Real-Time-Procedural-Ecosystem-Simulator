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
