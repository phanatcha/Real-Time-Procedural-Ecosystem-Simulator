using UnityEngine;

namespace Boreal.TerrainGeneration
{
    // Rivers and lakes are fresh water; the sea is salt. Animals drink fresh water, and only fresh water freezes. Both
    // come from how strongly the terrain carved a point as river or lake. A lake's water reaches out to where its carving
    // starts, where its strength is still tiny, so any lake strength counts; rivers fade out into the sea, so a river
    // only counts where it is cut in clearly.
    public static class FreshWater
    {
        // 0 for the sea, 1 for a river or lake, blending over a short band where a river meets the sea.
        public static float Amount(float riverStrength, float lakeStrength)
        {
            return Mathf.Max(Mathf.Clamp01(lakeStrength * 100f), Mathf.Clamp01((riverStrength - 0.02f) / 0.06f));
        }

        public static bool IsFresh(float riverStrength, float lakeStrength) => Amount(riverStrength, lakeStrength) >= 0.5f;
    }
}
