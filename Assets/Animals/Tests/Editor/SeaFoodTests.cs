using NUnit.Framework;

public class SeaFoodTests
{
    static readonly SeaFoodRules Rules = SeaFoodRules.Default;

    // Production per plant cell, relative to a land plant: how often a cell holds food, times its nutrition,
    // divided by how long it takes to regrow.
    static float Production(SeaFoodYield yield) => yield.chance * yield.nutrition / yield.regrowth;

    [Test]
    public void DeadZonesAndLandGrowNoSeaFood()
    {
        Assert.AreEqual(0f, Rules.Yield(SeaBiome.DeadZone).chance);
        Assert.AreEqual(0f, Rules.Yield(SeaBiome.None).chance);
    }

    [Test]
    public void EveryLivingBiomeGrowsSomething()
    {
        foreach (SeaBiome biome in new[]
                 {
                     SeaBiome.SeagrassMeadow, SeaBiome.KelpForest, SeaBiome.ColdWaterReef, SeaBiome.OpenSea
                 })
        {
            SeaFoodYield yield = Rules.Yield(biome);
            Assert.Greater(yield.chance, 0f, SeaBiomeRules.Name(biome));
            Assert.Greater(yield.nutrition, 0f, SeaBiomeRules.Name(biome));
        }
    }

    [Test]
    public void KelpIsTheMostProductiveAndOpenSeaTheLeast()
    {
        float kelp = Production(Rules.kelp);
        Assert.Greater(kelp, Production(Rules.seagrass));
        Assert.Greater(kelp, Production(Rules.reef));
        Assert.Less(Production(Rules.plankton), Production(Rules.seagrass));
        Assert.Less(Rules.plankton.chance, 0.1f, "open sea food should be sparse");
    }

    [Test]
    public void ReefPlantsAreTheRichestButSlowestToRegrow()
    {
        Assert.Greater(Rules.reef.nutrition, Rules.kelp.nutrition);
        Assert.Greater(Rules.reef.nutrition, Rules.seagrass.nutrition);
        Assert.Greater(Rules.reef.regrowth, Rules.kelp.regrowth);
    }

    [Test]
    public void GrowthFollowsTheTemperatureRange()
    {
        Assert.AreEqual(0f, Rules.GrowthRate(Rules.minimumGrowthTemperature), "too cold");
        Assert.AreEqual(0f, Rules.GrowthRate(Rules.maximumGrowthTemperature), "too warm");
        Assert.AreEqual(1f, Rules.GrowthRate(Rules.optimalGrowthTemperatureMin));
        Assert.AreEqual(1f, Rules.GrowthRate(Rules.optimalGrowthTemperatureMax));
        float halfwayUp = (Rules.minimumGrowthTemperature + Rules.optimalGrowthTemperatureMin) * 0.5f;
        Assert.AreEqual(0.5f, Rules.GrowthRate(halfwayUp), 1e-5f);
        float halfwayDown = (Rules.optimalGrowthTemperatureMax + Rules.maximumGrowthTemperature) * 0.5f;
        Assert.AreEqual(0.5f, Rules.GrowthRate(halfwayDown), 1e-5f);
        Assert.AreEqual(0f, Rules.GrowthRate(float.NaN));
    }

    [Test]
    public void TheWholeMeasuredSeaGrowsFood()
    {
        // Seed FOREST-001's sea surface runs from about -6.5 to +4.3 C. The Food Spawner drops sites growing
        // slower than 5% of the best rate, so all of that range must stay above it.
        for (float celsius = -6.5f; celsius <= 4.3f; celsius += 0.1f)
        {
            Assert.Greater(Rules.GrowthRate(celsius), 0.05f, $"{celsius:0.0} C");
        }

        Assert.AreEqual(1f, Rules.GrowthRate(4.3f), "the warmest water grows at the best rate");
    }

    [Test]
    public void EveryBiomeHasAPlantName()
    {
        foreach (SeaBiome biome in System.Enum.GetValues(typeof(SeaBiome)))
        {
            Assert.IsFalse(string.IsNullOrEmpty(SeaFoodRules.PlantName(biome)));
        }
    }
}
