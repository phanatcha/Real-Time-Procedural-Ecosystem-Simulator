using UnityEngine;

// The seeds each part of the world takes from the world seed, so a new world seed changes the rivers, lakes,
// ridges, vegetation and climate as well as the base terrain. SeedManager.GetWorldSeeds derives them from the
// seed string. ApplyTo writes them into copies of the settings assets; the assets keep their own values, which
// scenes without a SeedManager and the Editor previews still use.
[System.Serializable]
public struct WorldSeeds
{
    // Moisture, temperature and resource patches are value noise sampled at the world position plus an offset.
    // Offsets stay within this many metres (the island is about 5 km across), where the noise hash keeps its
    // float precision. The terrain shader samples moisture too.
    const float MaximumNoiseOffset = 8192f;

    public int terrain;
    public int ridges;
    public int rivers;
    public int lakes;
    public int vegetation;
    public int moisture;
    public int temperature;
    public int resourcePatches;

    public void ApplyTo(HeightMapSettings heights)
    {
        if (heights == null) return;

        if (heights.noiseSettings != null) heights.noiseSettings.seed = terrain;
        if (heights.ridgeSettings != null) heights.ridgeSettings.seed = ridges;
        if (heights.riverSettings != null) heights.riverSettings.seed = rivers;
        if (heights.lakeSettings != null) heights.lakeSettings.seed = lakes;
    }

    public void ApplyTo(VegetationSettings vegetationSettings)
    {
        if (vegetationSettings == null) return;

        vegetationSettings.seed = vegetation;
    }

    public void ApplyTo(EnvironmentDefinitions definitions)
    {
        if (definitions == null) return;

        definitions.moistureOffset = NoiseOffset(moisture);
        definitions.temperatureOffset = NoiseOffset(temperature);
        definitions.resourcePatchOffset = NoiseOffset(resourcePatches);
    }

    public override string ToString()
    {
        return $"terrain {terrain}, ridges {ridges}, rivers {rivers}, lakes {lakes}, vegetation {vegetation}, " +
               $"moisture {moisture}, temperature {temperature}, resource patches {resourcePatches}";
    }

    static Vector2 NoiseOffset(int seed)
    {
        System.Random random = new System.Random(seed);
        return new Vector2((float)(random.NextDouble() * 2.0 - 1.0) * MaximumNoiseOffset,
                           (float)(random.NextDouble() * 2.0 - 1.0) * MaximumNoiseOffset);
    }
}
