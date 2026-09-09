# Animal–Terrain Placeholder Integration

This folder is the bridge between the procedural boreal terrain and the temporary animal simulation. It is deliberately separate from both `Assets/TerrainGeneration` and `Assets/Animals`, so either system can be replaced without mixing ownership.

## Add the demo to the terrain scene

1. Open the scene that contains `TerrainGenerator`.
2. Choose **Tools > Boreal Ecosystem > Add Placeholder Animal Demo**.
3. Save the scene.
4. Enter Play mode.

The demo creates three grounded cylinder founders, cube-shaped plant food, a boreal temperature provider, finite world borders, and a local runtime NavMesh. These objects are generated only in Play mode and are disposable.

## Terrain guarantees used by animals

`AnimalTerrainWorld.Active` is the shared runtime entry point.

```csharp
AnimalTerrainWorld world = AnimalTerrainWorld.Active;

if (world != null &&
    world.TryGetSample(transform.position, out EnvironmentSample environment))
{
    float groundHeight = environment.height;
    float slopeDegrees = environment.slopeDegrees;
    bool isWater = environment.isWater;
    BiomeId biome = environment.biome;
    float foodPotential = environment.grassBiomass;
}
```

For a terrestrial spawn or destination, use the walkability query rather than checking height alone:

```csharp
if (world.TryFindWalkableGround(candidate, 40f, out Vector3 groundPosition))
{
    transform.position = groundPosition;
}
```

The walkability query uses the same water, shoreline, and slope definitions as rendering and vegetation. It also clamps positions to the finite habitable area derived from `HeightMapSettings.worldRadius`.

## Temperature contract

Existing animal code can continue to call:

```csharp
if (TemperatureSystem.TryGetTemperatureAt(transform.position, out float celsius))
{
    ApplyTemperature(celsius);
}
```

`ProceduralTerrainTemperatureProvider` converts the terrain's normalized temperature to a configurable boreal Celsius range. Its default runtime mode reads only cached terrain chunks, so an animal query cannot unexpectedly generate a terrain chunk on the main thread.

## Replacing the primitive objects

`AnimalTerrainDemoBootstrap` is only a test harness. When proper animal and food prefabs are ready:

1. Disable or remove `AnimalTerrainDemoBootstrap` from the scene.
2. Keep `AnimalTerrainWorld`, `ProceduralTerrainTemperatureProvider`, `TemperatureSystem`, and `ProceduralTerrainNavMesh`.
3. Spawn the real animal prefab only through `TryFindWalkableGround` or `ProceduralTerrainNavMesh.TryProjectToNavigation`.
4. Keep food tagged `Food` and border triggers tagged `Border` unless the imported interaction code is refactored to avoid tags.
5. Give terrestrial animals a `NavMeshAgent`, collider, `SeekFood` or its replacement behavior, and `AnimalTemperature`.

## Off-screen simulation boundary

The current placeholder harness proves terrain sampling, temperature, grounded spawning, food placement, boundaries, and visible navigation. `EcosystemSimulationController` still represents distant creatures as population counts. Before evolved individual animals use automatic materialization and abstraction, their genome and survival state must be serialized into an animal snapshot and restored later. Abstraction is already separated from death telemetry, preventing a distant animal from being recorded as dead merely because its GameObject was removed.
