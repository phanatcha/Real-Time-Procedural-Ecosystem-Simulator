using UnityEngine;

namespace Boreal.Animals
{
    // How the sexual drive gene (0 to 1) chooses between cloning and mating, and how fertile two animals are
    // together. An animal ready to breed with a drive below CloneOnlyBelow clones at once; above MateOnlyAbove it
    // only mates; in between it looks for a mate for up to drive x secondsPerDrive simulated seconds, then clones.
    // How fertility falls with genetic distance between two parents (item 63).
    public enum FertilityCurve
    {
        // 1 - (distance / threshold)^2: ever faster, as incompatibilities snowball.
        Snowball,
        // Full up to Fertility Fade Start times the threshold, then a straight fade to 0 at the threshold.
        Linear
    }

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

        // The chance that a courtship ends in a child, by the curve chosen (item 63): the snowball by default, or the
        // straight fade used before it, for control runs. Either way animals about to split into separate species
        // rarely interbreed.
        public static float Fertility(float geneticDistance, float speciesThreshold, float fadeStart, FertilityCurve curve) =>
            curve == FertilityCurve.Snowball
                ? SnowballFertility(geneticDistance, speciesThreshold)
                : Fertility(geneticDistance, speciesThreshold, fadeStart);

        // The snowball (item 63): problems in hybrids pile up roughly with the square of how far two groups have
        // drifted apart, because each new difference can clash with every earlier one (Orr 1995). So fertility falls
        // ever faster with distance: 1 - (distance / threshold)^2, nothing at the threshold. Close relatives are
        // nearly fully fertile (0.94 at a quarter of the threshold), animals half the threshold apart 0.75.
        public static float SnowballFertility(float geneticDistance, float speciesThreshold)
        {
            float share = Mathf.Max(0f, geneticDistance) / Mathf.Max(0.0001f, speciesThreshold);
            return share >= 1f ? 0f : 1f - share * share;
        }

        // A child of two parents more than half the species threshold apart counts as a hybrid (item 63): about one
        // animal species in ten hybridises in the wild.
        public const float HybridFromShareOfThreshold = 0.5f;

        public static bool IsHybrid(float parentDistance, float speciesThreshold) =>
            parentDistance > HybridFromShareOfThreshold * Mathf.Max(0.0001f, speciesThreshold);

        // The chance that a courtship ends in a child under the straight fade used before item 63: 1 for animals closer
        // than fadeStart times the species threshold, fading in a straight line to 0 at the threshold itself.
        public static float Fertility(float geneticDistance, float speciesThreshold, float fadeStart)
        {
            float threshold = Mathf.Max(0.0001f, speciesThreshold);
            float fadeFrom = Mathf.Clamp01(fadeStart) * threshold;
            if (geneticDistance >= threshold) return 0f;
            if (geneticDistance <= fadeFrom) return 1f;
            return 1f - (geneticDistance - fadeFrom) / (threshold - fadeFrom);
        }
    }
}
