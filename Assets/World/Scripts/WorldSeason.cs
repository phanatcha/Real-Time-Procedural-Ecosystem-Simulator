using Boreal.TerrainGeneration;
using UnityEngine;

namespace Boreal.World
{
    // Shared seasonal state. The Animals clock publishes; World only depends on terrain's value type.
    public static class WorldSeason
    {
        public static SeasonState Neutral => new SeasonState
        {
            season = BorealSeason.Summer, growthMultiplier = 1f
        };

        public static SeasonState Current { get; set; } = Neutral;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() => Current = Neutral;
    }
}
