using UnityEngine;

// The kinds of sea the water divides into. Every patch of water gets one from its depth, the slope of its bed
// (steep beds are rock, flat beds are sand and mud), the temperature at the surface, and whether it lies in one of
// the terrain's carved basins.
public enum SeaBiome : byte
{
    None,           // not water
    SeagrassMeadow, // shallow sand and mud
    KelpForest,     // rocky beds, from the shore down to where light fades
    ColdWaterReef,  // rocky beds in the warmest water, in patches, like the reefs off Norway
    OpenSea,        // deeper water, where little grows
    DeadZone        // deep, stagnant basins short of oxygen, where almost nothing survives
}

// The rules for dividing the sea into biomes. Depths are in metres below the water surface, slopes in degrees,
// temperatures in degrees Celsius at the surface.
[System.Serializable]
public struct SeaBiomeRules : System.IEquatable<SeaBiomeRules>
{
    [Tooltip("Sandy and muddy beds down to this depth are seagrass meadows; deeper ones are open sea.")]
    public float seagrassMaxDepth;
    [Tooltip("Rocky beds down to this depth are kelp forest; deeper ones are open sea.")]
    public float kelpMaxDepth;
    [Tooltip("Shallowest a reef grows.")]
    public float reefMinDepth;
    [Tooltip("Deepest a reef grows.")]
    public float reefMaxDepth;
    [Tooltip("Reefs only grow where the surface is at least this warm.")]
    public float reefMinTemperature;
    [Tooltip("Roughly the share of rocky water that is warm and deep enough for a reef that becomes reef. Reefs " +
             "grow in patches; 0 means no reefs, 1 means all such water.")]
    [Range(0f, 1f)] public float reefCoverage;
    [Tooltip("Beds at least this steep are rock; flatter beds are sand and mud.")]
    public float rockySlope;
    [Tooltip("How far patches of rock and sand blur that line, in degrees either way, so the bed isn't sorted " +
             "purely by slope.")]
    [Min(0f)] public float rockySlopeJitter;
    [Tooltip("How much a spot must lie in one of the terrain's carved basins (0-1) to be a dead zone.")]
    [Range(0f, 1f)] public float deadZoneBasin;
    [Tooltip("Basins only turn into dead zones from this depth down, where the water stops mixing.")]
    public float deadZoneMinDepth;
    [Tooltip("Energy use while swimming in a dead zone, as a multiple of the use anywhere else in the water. Route " +
             "planning charges the same, so animals cross dead zones only when there is no way around.")]
    [Min(1f)] public float deadZoneEnergyCost;

    public static SeaBiomeRules Default => new SeaBiomeRules
    {
        seagrassMaxDepth = 6f,
        kelpMaxDepth = 12f,
        reefMinDepth = 3f,
        reefMaxDepth = 16f,
        reefMinTemperature = 1f,
        reefCoverage = 0.5f,
        rockySlope = 6f,
        rockySlopeJitter = 3f,
        deadZoneBasin = 0.5f,
        deadZoneMinDepth = 12f,
        deadZoneEnergyCost = 3f
    };

    // The biome of one patch of water. basin is how much the spot lies in a carved basin (0-1); reefPatch and
    // substratePatch are smooth noise (0-1) that make reefs patchy and mix rock and sand on gentle slopes.
    public SeaBiome Classify(float depth, float slopeDegrees, float basin, float surfaceCelsius,
                             float reefPatch, float substratePatch)
    {
        if (IsDeadZone(depth, basin)) return SeaBiome.DeadZone;

        bool rocky = slopeDegrees + (substratePatch - 0.5f) * 2f * rockySlopeJitter >= rockySlope;
        if (rocky && depth >= reefMinDepth && depth <= reefMaxDepth && surfaceCelsius >= reefMinTemperature &&
            reefPatch <= reefCoverage)
        {
            return SeaBiome.ColdWaterReef;
        }

        if (rocky) return depth <= kelpMaxDepth ? SeaBiome.KelpForest : SeaBiome.OpenSea;
        return depth <= seagrassMaxDepth ? SeaBiome.SeagrassMeadow : SeaBiome.OpenSea;
    }

    // Dead zones depend only on depth and basin, so this is the quick check for code that needs nothing else.
    public bool IsDeadZone(float depth, float basin) => basin >= deadZoneBasin && depth >= deadZoneMinDepth;

    public bool Equals(SeaBiomeRules other) =>
        seagrassMaxDepth == other.seagrassMaxDepth && kelpMaxDepth == other.kelpMaxDepth &&
        reefMinDepth == other.reefMinDepth && reefMaxDepth == other.reefMaxDepth &&
        reefMinTemperature == other.reefMinTemperature && reefCoverage == other.reefCoverage &&
        rockySlope == other.rockySlope && rockySlopeJitter == other.rockySlopeJitter &&
        deadZoneBasin == other.deadZoneBasin && deadZoneMinDepth == other.deadZoneMinDepth &&
        deadZoneEnergyCost == other.deadZoneEnergyCost;

    public override bool Equals(object other) => other is SeaBiomeRules rules && Equals(rules);

    public override int GetHashCode() => System.HashCode.Combine(seagrassMaxDepth, kelpMaxDepth, reefMaxDepth,
                                                                 reefMinTemperature, reefCoverage, rockySlope,
                                                                 deadZoneMinDepth, deadZoneEnergyCost);

    public static string Name(SeaBiome biome) => biome switch
    {
        SeaBiome.SeagrassMeadow => "Seagrass meadow",
        SeaBiome.KelpForest => "Kelp forest",
        SeaBiome.ColdWaterReef => "Cold-water reef",
        SeaBiome.OpenSea => "Open sea",
        SeaBiome.DeadZone => "Dead zone",
        _ => "Land"
    };
}
