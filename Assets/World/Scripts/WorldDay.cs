using Boreal.TerrainGeneration;
using UnityEngine;

namespace Boreal.World
{
    // The time of day the whole world shares, as WorldSeason shares the season. The Animals day clock publishes it;
    // without one it is always midday, so nothing changes for systems that read it.
    public static class WorldDay
    {
        public static DayState Current { get; set; } = DayCycle.Neutral;

        // Whether day and night are running. Without them nothing swings through the day: it is always midday, at the
        // day's average temperature.
        public static bool IsRunning { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            Current = DayCycle.Neutral;
            IsRunning = false;
        }
    }
}
