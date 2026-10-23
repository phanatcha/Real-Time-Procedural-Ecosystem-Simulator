using UnityEngine;

// How the sexual drive gene (0 to 1) chooses between cloning and mating, and how fertile two animals are
// together. An animal ready to breed with a drive below CloneOnlyBelow clones at once; above MateOnlyAbove it
// only mates; in between it looks for a mate for up to drive x secondsPerDrive simulated seconds, then clones.
public static class MatingRules
{
    public const float CloneOnlyBelow = 0.15f;
    public const float MateOnlyAbove = 0.85f;

    public static bool CanMate(float drive) => drive >= CloneOnlyBelow;

    public static bool CanClone(float drive) => drive <= MateOnlyAbove;

    // Simulated seconds an animal ready to breed looks for a mate before cloning instead: none for animals that
    // only clone, forever for animals that only mate.
    public static float MateSearchTime(float drive, float secondsPerDrive)
    {
        if (!CanMate(drive)) return 0f;
        if (!CanClone(drive)) return float.PositiveInfinity;
        return drive * Mathf.Max(0f, secondsPerDrive);
    }

    // The chance that a courtship ends in a child: 1 for animals closer than fadeStart times the species
    // threshold, fading to 0 at the threshold itself, so animals about to split into separate species rarely
    // interbreed.
    public static float Fertility(float geneticDistance, float speciesThreshold, float fadeStart)
    {
        float threshold = Mathf.Max(0.0001f, speciesThreshold);
        float fadeFrom = Mathf.Clamp01(fadeStart) * threshold;
        if (geneticDistance >= threshold) return 0f;
        if (geneticDistance <= fadeFrom) return 1f;
        return 1f - (geneticDistance - fadeFrom) / (threshold - fadeFrom);
    }
}
