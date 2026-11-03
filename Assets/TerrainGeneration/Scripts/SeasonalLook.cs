using UnityEngine;

namespace Boreal.TerrainGeneration
{
    // How the time of year looks, as the shaders draw it. Each part is 0 in high summer, so a scene without seasons
    // (or the Editor outside Play mode, where nothing sets them) keeps its summer look.
    [System.Serializable]
    public struct SeasonalLookState
    {
        [Tooltip("Birch and bush leaves turned yellow, orange and red.")]
        [Range(0f, 1f)] public float autumnColour;
        [Tooltip("Share of birch and bush leaves that have fallen; at 1 only the branches and twigs are left.")]
        [Range(0f, 1f)] public float leafFall;
        [Tooltip("The bright green of new leaves in spring.")]
        [Range(0f, 1f)] public float springFresh;
        [Tooltip("Share of the berries on the island's bushes that are gone. Edible plants keep theirs.")]
        [Range(0f, 1f)] public float fruitGone;
        [Tooltip("Share of the meadow flowers that have finished.")]
        [Range(0f, 1f)] public float flowersGone;
        [Tooltip("Grass and meadows dried to straw.")]
        [Range(0f, 1f)] public float grassDry;
        [Tooltip("Snow lying on the ground, rocks and spruces, as the season's snow amount the animals feel.")]
        [Range(0f, 1f)] public float snow;
        [Tooltip("How far rivers and lakes have frozen over; at 1 they are covered.")]
        [Range(0f, 1f)] public float ice;
    }

    // The seasons' look: from the time of year, when birches and bushes colour and lose their leaves, when berries and
    // flowers come and go and when the grass dries; snow and ice come from the season itself, so what is drawn matches
    // what the animals live with. Apply hands it to every shader as two global vectors.
    public static class SeasonalLook
    {
        // xyzw: autumn colour, leaf fall, spring freshness, fruit gone.
        static readonly int LeavesId = Shader.PropertyToID("_SeasonLeaves");
        // xyzw: snow, dry grass, flowers gone, ice.
        static readonly int GroundId = Shader.PropertyToID("_SeasonGround");

        // What was last applied; summer until then.
        public static SeasonalLookState Current { get; private set; }

        // yearProgress runs 0 to 1 through the year from the height of spring (0.25 summer, 0.5 fall, 0.75 winter), as
        // SeasonState.AtYearProgress counts it. The timings follow a Norwegian year: birches turn in early fall and are
        // bare before winter, berries ripen from late summer, flowers bloom from late spring to midsummer.
        public static SeasonalLookState At(float yearProgress, float snow, float ice)
        {
            float leaves = Window(yearProgress, 0.92f, 0.5f, 0.1f, 0.12f);
            return new SeasonalLookState
            {
                autumnColour = Window(yearProgress, 0.38f, 0.62f, 0.1f, 0.05f),
                leafFall = 1f - leaves,
                springFresh = Window(yearProgress, 0.92f, 0.08f, 0.06f, 0.1f),
                fruitGone = 1f - Window(yearProgress, 0.27f, 0.56f, 0.05f, 0.06f),
                flowersGone = 1f - Window(yearProgress, 0.98f, 0.4f, 0.06f, 0.06f),
                grassDry = Window(yearProgress, 0.42f, 0.98f, 0.12f, 0.1f),
                snow = Mathf.Clamp01(snow),
                ice = Mathf.Clamp01(ice)
            };
        }

        public static void Apply(SeasonalLookState state)
        {
            Current = state;
            Shader.SetGlobalVector(LeavesId, new Vector4(state.autumnColour, state.leafFall, state.springFresh, state.fruitGone));
            Shader.SetGlobalVector(GroundId, new Vector4(state.snow, state.grassDry, state.flowersGone, state.ice));
        }

        // Back to summer.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() => Apply(default);

        // 0 to 1 over the year: rising from 0 at start to 1 fadeIn later, held, then falling from 1 at end to 0 fadeOut
        // later. Times are fractions of the year and wrap round it, so a window can run across the turn of the year.
        public static float Window(float yearProgress, float start, float end, float fadeIn, float fadeOut)
        {
            float since = Mathf.Repeat(yearProgress - start, 1f);
            float length = Mathf.Repeat(end - start, 1f);
            if (since < fadeIn) return Mathf.SmoothStep(0f, 1f, since / Mathf.Max(fadeIn, 1e-5f));
            if (since <= length) return 1f;
            if (since < length + fadeOut) return 1f - Mathf.SmoothStep(0f, 1f, (since - length) / Mathf.Max(fadeOut, 1e-5f));
            return 0f;
        }
    }
}
