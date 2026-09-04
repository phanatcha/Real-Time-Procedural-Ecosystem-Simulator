using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class EcosystemSimulationTests
{
    const string HeightSettingsPath = "Assets/TerrainGeneration/Settings/HeightMapSettings.asset";
    const string MeshSettingsPath = "Assets/TerrainGeneration/Settings/MeshSettings.asset";
    const string EnvironmentDefinitionsPath = "Assets/TerrainGeneration/Settings/EnvironmentDefinitions.asset";
    const string VegetationSettingsPath = "Assets/TerrainGeneration/Settings/VegetationSettings.asset";

    HeightMapSettings heightSettings;
    MeshSettings meshSettings;
    EnvironmentDefinitions environmentDefinitions;
    VegetationSettings vegetationSettings;
    readonly List<Object> temporaryObjects = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        heightSettings = AssetDatabase.LoadAssetAtPath<HeightMapSettings>(HeightSettingsPath);
        meshSettings = AssetDatabase.LoadAssetAtPath<MeshSettings>(MeshSettingsPath);
        environmentDefinitions = AssetDatabase.LoadAssetAtPath<EnvironmentDefinitions>(EnvironmentDefinitionsPath);
        vegetationSettings = AssetDatabase.LoadAssetAtPath<VegetationSettings>(VegetationSettingsPath);

        Assert.IsNotNull(heightSettings);
        Assert.IsNotNull(meshSettings);
        Assert.IsNotNull(environmentDefinitions);
        Assert.IsNotNull(vegetationSettings);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = temporaryObjects.Count - 1; i >= 0; i--)
        {
            if (temporaryObjects[i] != null) Object.DestroyImmediate(temporaryObjects[i]);
        }
        temporaryObjects.Clear();
    }

    [Test]
    public void GridIsFiniteHabitableAndDoesNotRequireMeshesOrGameObjects()
    {
        EcosystemSimulationSettings settings = CreateFastSettings();
        EcosystemGrid grid = CreateGrid(settings);

        Assert.IsFalse(typeof(Object).IsAssignableFrom(typeof(EcosystemGrid)));
        Assert.That(grid.WorldRadius, Is.EqualTo(heightSettings.worldRadius * meshSettings.meshScale).Within(0.001f));
        Assert.Greater(grid.CandidateCellCount, 0);
        Assert.Greater(grid.CellCount, 0);
        Assert.LessOrEqual(grid.CellCount, grid.CandidateCellCount);

        foreach (EcosystemCell cell in grid.Cells)
        {
            Assert.LessOrEqual(Mathf.Max(Mathf.Abs(cell.WorldCentre.x), Mathf.Abs(cell.WorldCentre.y)), grid.WorldRadius + 0.001f);
            Assert.IsTrue(cell.Environment.isValid);
            Assert.IsTrue(cell.Environment.isHabitable);
            Assert.GreaterOrEqual(cell.Environment.landFraction + 0.0001f, settings.minimumHabitableLandFraction);
            Assert.That(cell.Environment.average.moisture, Is.InRange(0f, 1f));
            Assert.That(cell.Environment.average.waterAvailability, Is.InRange(0f, 1f));
        }
    }

    [Test]
    public void CellEnvironmentGenerationIsDeterministic()
    {
        EcosystemSimulationSettings settings = CreateFastSettings();
        EcosystemGrid first = CreateGrid(settings);
        EcosystemGrid second = CreateGrid(settings);

        Assert.AreEqual(first.CellCount, second.CellCount);
        foreach (EcosystemCell firstCell in first.Cells)
        {
            Assert.IsTrue(second.TryGetCell(firstCell.Coordinate, out EcosystemCell secondCell));
            Assert.That(secondCell.Environment.landFraction, Is.EqualTo(firstCell.Environment.landFraction).Within(0.0001f));
            Assert.That(secondCell.Environment.average.height, Is.EqualTo(firstCell.Environment.average.height).Within(0.0001f));
            Assert.That(secondCell.Environment.average.moisture, Is.EqualTo(firstCell.Environment.average.moisture).Within(0.0001f));
            Assert.That(secondCell.Environment.average.treeCover, Is.EqualTo(firstCell.Environment.average.treeCover).Within(0.0001f));
            Assert.AreEqual(firstCell.Environment.average.biome, secondCell.Environment.average.biome);
        }
    }

    [Test]
    public void SpeciesSlopeSuitabilityUsesConfiguredDegreeThreshold()
    {
        EcosystemSpeciesDefinition species = Track(ScriptableObject.CreateInstance<EcosystemSpeciesDefinition>());
        species.minimumLandFraction = 0.2f;
        species.maximumSlopeDegrees = 40f;
        species.resourcePreference = EcosystemResourcePreference.None;
        species.resourceImportance = 0f;

        EnvironmentSample point = new EnvironmentSample
        {
            isValid = true,
            isLand = true,
            slopeDegrees = 10f,
            temperature = 0.5f
        };
        EcosystemCellEnvironment cell = new EcosystemCellEnvironment
        {
            isValid = true,
            isHabitable = true,
            landFraction = 1f,
            average = point
        };

        Assert.Greater(species.GetPointSuitability(point), 0f);
        Assert.Greater(species.GetHabitatSuitability(cell), 0f);

        point.slopeDegrees = 41f;
        cell.average = point;
        Assert.AreEqual(0f, species.GetPointSuitability(point));
        Assert.AreEqual(0f, species.GetHabitatSuitability(cell));
    }

    [Test]
    public void NearbyAndDistantCellsUseDifferentUpdateRates()
    {
        EcosystemSimulationSettings settings = CreateFastSettings();
        settings.nearbyUpdateInterval = 0.25f;
        settings.distantUpdateInterval = 2f;
        settings.nearbySimulationRadius = settings.cellSize * 0.2f;
        settings.defaultMigrationRatePerSecond = 0f;
        EcosystemGrid grid = CreateGrid(settings);
        EcosystemCell nearby = FirstCell(grid);
        EcosystemCell distant = FarthestCell(grid, nearby.WorldCentre);
        EcosystemPopulationSimulation simulation = new EcosystemPopulationSimulation(grid, settings, null, false);

        EcosystemSimulationStepStats firstStep = simulation.Advance(0.3f, nearby.WorldCentre);
        Assert.Greater(firstStep.nearbyCellsUpdated, 0);
        Assert.AreEqual(0, firstStep.distantCellsUpdated);
        Assert.Greater(nearby.UpdateCount, 0);
        Assert.AreEqual(0, distant.UpdateCount);

        EcosystemSimulationStepStats secondStep = simulation.Advance(1.8f, nearby.WorldCentre);
        Assert.Greater(secondStep.distantCellsUpdated, 0);
        Assert.Greater(distant.UpdateCount, 0);
    }

    [Test]
    public void MigrationMovesPopulationToNeighborsWithoutChangingTheTotal()
    {
        EcosystemSimulationSettings settings = CreateFastSettings();
        settings.nearbyUpdateInterval = 0.1f;
        settings.distantUpdateInterval = 0.1f;
        settings.nearbySimulationRadius = 100000f;
        settings.defaultMigrationRatePerSecond = 1f;
        EcosystemGrid grid = CreateGrid(settings);
        EcosystemCell source = CellWithNeighbor(grid, out EcosystemCell neighbor);
        source.SetPopulation("test-species", 100f);
        EcosystemPopulationSimulation simulation = new EcosystemPopulationSimulation(grid, settings, null, false);

        float totalBefore = simulation.GetTotalAbstractPopulation("test-species");
        EcosystemSimulationStepStats stats = simulation.Advance(0.5f, source.WorldCentre);
        float totalAfter = simulation.GetTotalAbstractPopulation("test-species");

        Assert.Greater(stats.migrationTransfers, 0);
        Assert.Greater(stats.migratedPopulation, 0f);
        Assert.Less(source.GetPopulation("test-species"), 100f);
        Assert.Greater(neighbor.GetPopulation("test-species"), 0f);
        Assert.That(totalAfter, Is.EqualTo(totalBefore).Within(0.001f));
    }

    [Test]
    public void MaterializedAnimalReturnsToPopulationAfterLeavingTheActiveArea()
    {
        EcosystemSimulationSettings settings = CreateFastSettings();
        settings.materializationRadius = settings.cellSize;
        settings.abstractionRadius = settings.cellSize * 1.5f;
        settings.spawnAttemptsPerAnimal = 64;

        EcosystemSpeciesDefinition species = Track(ScriptableObject.CreateInstance<EcosystemSpeciesDefinition>());
        species.speciesId = "test-animal";
        species.seedPopulation = false;
        species.resourcePreference = EcosystemResourcePreference.None;
        species.resourceImportance = 0f;
        species.minimumLandFraction = 0f;
        species.maximumSlopeDegrees = 90f;
        species.maximumMaterializedPerCell = 1;
        species.populationPerGameObject = 1f;
        species.prefab = Track(new GameObject("Test Animal Prefab"));

        GameObject focusObject = Track(new GameObject("Simulation Focus"));
        GameObject controllerObject = Track(new GameObject("Ecosystem Controller"));
        EcosystemSimulationController controller = controllerObject.AddComponent<EcosystemSimulationController>();
        controller.heightMapSettings = heightSettings;
        controller.meshSettings = meshSettings;
        controller.environmentDefinitions = environmentDefinitions;
        controller.vegetationSettings = vegetationSettings;
        controller.simulationSettings = settings;
        controller.speciesDefinitions = new[] { species };
        controller.simulationFocus = focusObject.transform;

        Assert.IsTrue(controller.Initialize());
        EcosystemCell source = MostSuitableCell(controller.Grid, species);
        focusObject.transform.position = new Vector3(source.WorldCentre.x, 0f, source.WorldCentre.y);
        source.SetPopulation(species.Id, 2f);
        float totalBefore = controller.GetTotalPopulation(species.Id);

        controller.RefreshMaterialization();
        Assert.AreEqual(1, controller.MaterializedAnimals.Count);
        Assert.That(source.GetPopulation(species.Id), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(controller.GetTotalPopulation(species.Id), Is.EqualTo(totalBefore).Within(0.0001f));

        focusObject.transform.position += Vector3.right * controller.Grid.WorldRadius * 3f;
        controller.RefreshMaterialization();

        Assert.AreEqual(0, controller.MaterializedAnimals.Count);
        Assert.That(source.GetPopulation(species.Id), Is.EqualTo(2f).Within(0.0001f));
        Assert.That(controller.GetTotalPopulation(species.Id), Is.EqualTo(totalBefore).Within(0.0001f));
    }

    EcosystemSimulationSettings CreateFastSettings()
    {
        EcosystemSimulationSettings settings = Track(ScriptableObject.CreateInstance<EcosystemSimulationSettings>());
        settings.cellSize = 500f;
        settings.environmentSamplesPerAxis = 2;
        settings.minimumHabitableLandFraction = 0.2f;
        settings.nearbyUpdateInterval = 0.5f;
        settings.distantUpdateInterval = 5f;
        settings.nearbySimulationRadius = 750f;
        settings.defaultMigrationRatePerSecond = 0.004f;
        settings.materializationRadius = 500f;
        settings.abstractionRadius = 750f;
        settings.materializationRefreshInterval = 0.5f;
        settings.spawnAttemptsPerAnimal = 16;
        settings.materializationSeed = 1947;
        return settings;
    }

    EcosystemGrid CreateGrid(EcosystemSimulationSettings settings)
    {
        TerrainEnvironmentSampler sampler = new TerrainEnvironmentSampler(
            heightSettings,
            meshSettings,
            environmentDefinitions,
            vegetationSettings,
            256);
        return EcosystemGrid.Generate(sampler, settings);
    }

    static EcosystemCell FirstCell(EcosystemGrid grid)
    {
        foreach (EcosystemCell cell in grid.Cells) return cell;
        Assert.Fail("The generated ecosystem grid has no habitable cells.");
        return null;
    }

    static EcosystemCell FarthestCell(EcosystemGrid grid, Vector2 position)
    {
        EcosystemCell farthest = null;
        float farthestDistance = -1f;
        foreach (EcosystemCell cell in grid.Cells)
        {
            float distance = (cell.WorldCentre - position).sqrMagnitude;
            if (distance <= farthestDistance) continue;
            farthestDistance = distance;
            farthest = cell;
        }

        Assert.IsNotNull(farthest);
        return farthest;
    }

    static EcosystemCell CellWithNeighbor(EcosystemGrid grid, out EcosystemCell neighbor)
    {
        foreach (EcosystemCell cell in grid.Cells)
        {
            foreach (EcosystemCell candidate in grid.GetNeighbors(cell.Coordinate))
            {
                neighbor = candidate;
                return cell;
            }
        }

        Assert.Fail("The generated ecosystem grid has no neighboring habitable cells.");
        neighbor = null;
        return null;
    }

    static EcosystemCell MostSuitableCell(EcosystemGrid grid, EcosystemSpeciesDefinition species)
    {
        EcosystemCell best = null;
        float bestSuitability = 0f;
        foreach (EcosystemCell cell in grid.Cells)
        {
            float suitability = species.GetHabitatSuitability(cell.Environment);
            if (suitability <= bestSuitability) continue;
            bestSuitability = suitability;
            best = cell;
        }

        Assert.IsNotNull(best, "The generated ecosystem grid has no cell suitable for this species.");
        return best;
    }

    T Track<T>(T target) where T : Object
    {
        temporaryObjects.Add(target);
        return target;
    }
}
