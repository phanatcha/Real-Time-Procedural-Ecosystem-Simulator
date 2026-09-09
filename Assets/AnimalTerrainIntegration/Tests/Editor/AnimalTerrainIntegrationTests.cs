using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class AnimalTerrainIntegrationTests
{
    const string HeightSettingsPath = "Assets/TerrainGeneration/Settings/HeightMapSettings.asset";
    const string MeshSettingsPath = "Assets/TerrainGeneration/Settings/MeshSettings.asset";

    [Test]
    public void NormalizedTerrainTemperatureMapsToBorealCelsiusRange()
    {
        Assert.That(
            ProceduralTerrainTemperatureProvider.NormalizedToCelsius(0f, -28f, 18f),
            Is.EqualTo(-28f).Within(0.0001f));
        Assert.That(
            ProceduralTerrainTemperatureProvider.NormalizedToCelsius(0.5f, -28f, 18f),
            Is.EqualTo(-5f).Within(0.0001f));
        Assert.That(
            ProceduralTerrainTemperatureProvider.NormalizedToCelsius(1f, -28f, 18f),
            Is.EqualTo(18f).Within(0.0001f));
    }

    [Test]
    public void AnimalWalkabilityUsesTerrainBoundsWaterShoreAndSlope()
    {
        HeightMapSettings heightSettings = AssetDatabase.LoadAssetAtPath<HeightMapSettings>(HeightSettingsPath);
        MeshSettings meshSettings = AssetDatabase.LoadAssetAtPath<MeshSettings>(MeshSettingsPath);
        Assert.IsNotNull(heightSettings);
        Assert.IsNotNull(meshSettings);

        GameObject root = new GameObject("Animal Terrain Test");
        try
        {
            TerrainGenerator generator = root.AddComponent<TerrainGenerator>();
            generator.heightMapSettings = heightSettings;
            generator.meshSettings = meshSettings;
            AnimalTerrainWorld world = root.AddComponent<AnimalTerrainWorld>();
            world.terrainGenerator = generator;
            world.maximumWalkableSlopeDegrees = 32f;
            world.excludeShore = true;
            world.boundaryInset = 20f;

            float expectedRadius = heightSettings.worldRadius * meshSettings.meshScale;
            Assert.That(world.WorldRadius, Is.EqualTo(expectedRadius).Within(0.0001f));
            Assert.IsTrue(world.IsInsideHabitableBounds(Vector2.zero));
            Assert.IsFalse(world.IsInsideHabitableBounds(
                new Vector2(expectedRadius, expectedRadius)));

            EnvironmentSample sample = new EnvironmentSample
            {
                isValid = true,
                isLand = true,
                slopeDegrees = 31f,
                biome = BiomeId.BorealForest
            };
            Assert.IsTrue(world.IsWalkable(sample));

            sample.isShore = true;
            Assert.IsFalse(world.IsWalkable(sample));
            sample.isShore = false;
            sample.isWater = true;
            Assert.IsFalse(world.IsWalkable(sample));
            sample.isWater = false;
            sample.slopeDegrees = 33f;
            Assert.IsFalse(world.IsWalkable(sample));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
