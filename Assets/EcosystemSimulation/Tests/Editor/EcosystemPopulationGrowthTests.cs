using NUnit.Framework;
using UnityEngine;

public class EcosystemPopulationGrowthTests
{
    const float Capacity = 24f;
    const float GrowthRate = 0.011f;
    const float DeathRate = 1f / 300f;

    static float Grow(float population, float elapsed, float capacity = Capacity)
    {
        return EcosystemPopulationSimulation.GrowLogistic(population, capacity, GrowthRate, DeathRate, elapsed);
    }

    [Test]
    public void OneLongStepMatchesManyShortSteps()
    {
        float stepped = 2f;
        for (int step = 0; step < 600; step++) stepped = Grow(stepped, 1f);

        Assert.That(Grow(2f, 600f), Is.EqualTo(stepped).Within(0.001f));
    }

    [Test]
    public void MatchesStepByStepLogisticGrowth()
    {
        // Integrates dN/dt = r·N·(1 − N/K) in 1 ms steps.
        double population = 2.0;
        for (int step = 0; step < 200000; step++)
        {
            population += GrowthRate * population * (1.0 - population / Capacity) * 0.001;
        }

        Assert.That(Grow(2f, 200f), Is.EqualTo((float)population).Within(0.001f));
    }

    [Test]
    public void SmallPopulationsGrowAtTheGrowthRate()
    {
        float expected = 0.1f * Mathf.Exp(GrowthRate * 10f);
        Assert.That(Grow(0.1f, 10f, 100000f), Is.EqualTo(expected).Within(expected * 0.0001f));
    }

    [Test]
    public void PopulationApproachesButDoesNotPassCapacity()
    {
        Assert.Greater(Grow(1f, 100f), 1f);
        float previous = 1f;
        for (int step = 1; step <= 20; step++)
        {
            // Reaches the capacity exactly in float precision late on, so it only has to not fall.
            float population = Grow(1f, step * 100f);
            Assert.GreaterOrEqual(population, previous);
            Assert.LessOrEqual(population, Capacity);
            previous = population;
        }

        Assert.That(previous, Is.EqualTo(Capacity).Within(0.01f));
    }

    [Test]
    public void PopulationAboveCapacityFallsBackToIt()
    {
        float population = Grow(40f, 100f);

        Assert.Less(population, 40f);
        Assert.Greater(population, Capacity);
        Assert.That(Grow(40f, 5000f), Is.EqualTo(Capacity).Within(0.001f));
    }

    [Test]
    public void PopulationWithoutRoomDiesOutAtTheDeathRate()
    {
        Assert.That(Grow(10f, 300f, 0f), Is.EqualTo(10f * Mathf.Exp(-1f)).Within(0.0001f));
        Assert.That(Grow(10f, 300f, -5f), Is.EqualTo(10f * Mathf.Exp(-1f)).Within(0.0001f));
    }

    [Test]
    public void EmptyCellsStayEmptyAndNoTimeChangesNothing()
    {
        Assert.AreEqual(0f, Grow(0f, 1000f));
        Assert.AreEqual(5f, Grow(5f, 0f));
        Assert.AreEqual(5f, EcosystemPopulationSimulation.GrowLogistic(5f, Capacity, 0f, DeathRate, 100f));
    }

    [Test]
    public void EstimatedGrowthRateMatchesTheFounders()
    {
        // Founders: maturity 45 s, reproduction cooldown 60 s, lifespan 300 s.
        float expected = (1f - 45f / 300f) / 60f - 1f / 300f;

        Assert.That(EcosystemSpeciesDefinition.EstimateGrowthRate(45f, 60f, 300f), Is.EqualTo(expected).Within(1e-6f));
    }

    [Test]
    public void DefaultRatesAreTheFounders()
    {
        EcosystemSpeciesDefinition species = ScriptableObject.CreateInstance<EcosystemSpeciesDefinition>();
        try
        {
            Assert.That(species.growthRatePerSecond,
                Is.EqualTo(EcosystemSpeciesDefinition.EstimateGrowthRate(45f, 60f, 300f)).Within(0.0002f));
            Assert.That(species.deathRatePerSecond, Is.EqualTo(1f / 300f).Within(1e-6f));
        }
        finally
        {
            Object.DestroyImmediate(species);
        }
    }
}
