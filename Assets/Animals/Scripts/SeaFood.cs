using UnityEngine;

// How much food one sea biome grows, compared with plants on land.
[System.Serializable]
public struct SeaFoodYield
{
    [Tooltip("Chance that a plant cell in this biome holds food.")]
    [Range(0f, 1f)] public float chance;
    [Tooltip("Nutrition of each plant, as a multiple of the Food Spawner's base nutrition.")]
    [Min(0f)] public float nutrition;
    [Tooltip("Time to regrow once eaten, as a multiple of the Food Spawner's base regrowth time.")]
    [Min(0.1f)] public float regrowth;

    public SeaFoodYield(float chance, float nutrition, float regrowth)
    {
        this.chance = chance;
        this.nutrition = nutrition;
        this.regrowth = regrowth;
    }

    public static SeaFoodYield None => default;
}

// What grows in the sea: one kind of plant per biome (see SeaBiomes), each with its own density, richness and
// regrowth time. All of them grow best in cold water, since the sea's surface is at most a few degrees above
// freezing. Dead zones grow nothing.
[System.Serializable]
public struct SeaFoodRules
{
    [Tooltip("Seagrass meadows: modest food on the sandy shallows.")]
    public SeaFoodYield seagrass;
    [Tooltip("Kelp forests: plentiful food along rocky coasts, the sea's main food source.")]
    public SeaFoodYield kelp;
    [Tooltip("Cold-water reefs: the richest food, in patches, slow to regrow.")]
    public SeaFoodYield reef;
    [Tooltip("Plankton in the open sea: sparse and poor, so crossing it takes energy reserves.")]
    public SeaFoodYield plankton;

    [Header("Growth Temperature")]
    [Tooltip("Sea plants stop growing at or below this surface temperature, in degrees Celsius.")]
    public float minimumGrowthTemperature;
    [Tooltip("Sea plants grow fastest between this temperature and the next.")]
    public float optimalGrowthTemperatureMin;
    public float optimalGrowthTemperatureMax;
    [Tooltip("Sea plants stop growing at or above this temperature.")]
    public float maximumGrowthTemperature;

    // Starting values, to be tuned in Unity. Measured on seed FOREST-001, the sea's surface ranges from about
    // -6.5 to +4.3 C, so even the coldest water still grows some food, at about a third of the best rate.
    public static SeaFoodRules Default => new SeaFoodRules
    {
        seagrass = new SeaFoodYield(0.4f, 0.8f, 1f),
        kelp = new SeaFoodYield(0.6f, 1.2f, 1f),
        reef = new SeaFoodYield(0.5f, 2f, 2f),
        plankton = new SeaFoodYield(0.03f, 0.6f, 1f),
        minimumGrowthTemperature = -10f,
        optimalGrowthTemperatureMin = 0f,
        optimalGrowthTemperatureMax = 10f,
        maximumGrowthTemperature = 20f
    };

    public SeaFoodYield Yield(SeaBiome biome) => biome switch
    {
        SeaBiome.SeagrassMeadow => seagrass,
        SeaBiome.KelpForest => kelp,
        SeaBiome.ColdWaterReef => reef,
        SeaBiome.OpenSea => plankton,
        _ => SeaFoodYield.None
    };

    // Fraction of the fastest regrowth at this surface temperature: zero outside the growth range, rising to 1
    // across the optimal range and falling back to zero above it.
    public float GrowthRate(float surfaceCelsius)
    {
        float minimum = minimumGrowthTemperature;
        float maximum = Mathf.Max(minimum + 0.01f, maximumGrowthTemperature);
        float optimalMin = Mathf.Clamp(optimalGrowthTemperatureMin, minimum, maximum);
        float optimalMax = Mathf.Clamp(optimalGrowthTemperatureMax, optimalMin, maximum);
        if (float.IsNaN(surfaceCelsius) || surfaceCelsius <= minimum || surfaceCelsius >= maximum) return 0f;
        if (surfaceCelsius < optimalMin) return Mathf.InverseLerp(minimum, optimalMin, surfaceCelsius);
        return surfaceCelsius <= optimalMax ? 1f : 1f - Mathf.InverseLerp(optimalMax, maximum, surfaceCelsius);
    }

    public static string PlantName(SeaBiome biome) => biome switch
    {
        SeaBiome.SeagrassMeadow => "Seagrass",
        SeaBiome.KelpForest => "Kelp",
        SeaBiome.ColdWaterReef => "Reef",
        SeaBiome.OpenSea => "Plankton",
        _ => "Plant"
    };
}
