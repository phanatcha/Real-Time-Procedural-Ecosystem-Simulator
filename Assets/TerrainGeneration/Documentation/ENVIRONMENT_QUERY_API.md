# Terrain Environment Query API

This is the terrain-facing contract for animals, plants, food systems, spawning, and navigation. Animal code should read the environment through this API instead of duplicating terrain noise, water thresholds, or biome rules.

## Recommended call from an animal

Give the animal system a reference to the scene's `TerrainGenerator`, then query with a world-space position:

```csharp
[SerializeField] TerrainGenerator terrainGenerator;

bool TryReadEnvironment(Vector3 worldPosition, out EnvironmentSample environment)
{
    return terrainGenerator.TryGetEnvironmentSample(worldPosition, out environment);
}
```

The query works for visible terrain, hidden terrain, and chunks that have never been rendered. It does not require a mesh collider or a loaded terrain GameObject at the requested position.

Typical animal logic can then use one coherent sample:

```csharp
if (terrainGenerator.TryGetEnvironmentSample(transform.position, out EnvironmentSample environment))
{
    bool canWalk = environment.isLand && environment.slopeDegrees <= 35f;
    float foodPotential = environment.grassBiomass + environment.treeCover * 0.25f;
    float drinkingPotential = environment.waterAvailability;
}
```

## Querying without any TerrainGenerator GameObject

Simulation code can own a plain C# sampler. Assign the same four settings assets used by the terrain:

```csharp
[SerializeField] HeightMapSettings heightMapSettings;
[SerializeField] MeshSettings meshSettings;
[SerializeField] EnvironmentDefinitions environmentDefinitions;
[SerializeField] VegetationSettings vegetationSettings;

TerrainEnvironmentSampler environmentSampler;

void Awake()
{
    environmentSampler = new TerrainEnvironmentSampler(
        heightMapSettings,
        meshSettings,
        environmentDefinitions,
        vegetationSettings);
}

bool TryReadEnvironment(float worldX, float worldZ, out EnvironmentSample environment)
{
    return environmentSampler.TrySample(worldX, worldZ, out environment);
}
```

`TerrainEnvironmentSampler` is not a `MonoBehaviour` or `UnityEngine.Object`. The first query in an uncached terrain chunk generates that chunk's deterministic data synchronously; subsequent queries reuse the cached data. The default cache holds 128 chunks. Construct it with a different final argument to change that limit, or call `ClearCache()` after changing generation settings.

Call it from Unity's main thread because its configuration is stored in Unity `ScriptableObject` assets.

For high-frequency runtime systems that must never generate terrain synchronously, use the cached-only variant:

```csharp
if (environmentSampler.TrySampleCached(worldPosition, out EnvironmentSample environment))
{
    ReadEnvironment(environment);
}
```

`TrySampleCached` returns `false` when the requested chunk is not already cached and leaves the cache unchanged. The procedural temperature bridge uses this behavior by default.

## `EnvironmentSample` contract

All continuous environmental values are normalized to `0..1` unless stated otherwise.

| Field | Meaning |
|---|---|
| `isValid` | `true` when the query succeeded |
| `position` | Requested world X/Z with Y set to the terrain surface or underwater bed |
| `height` | Terrain surface height in world units |
| `normalizedHeight` | Terrain height normalized with the canonical terrain minimum and maximum |
| `surfaceNormal` | Terrain normal used by placement and movement systems |
| `slope` | Shader-compatible slope where `0` is flat and larger values are steeper |
| `slopeDegrees` | Human-readable slope angle in degrees |
| `isLand`, `isWater`, `isShore` | Classification from the shared canonical thresholds |
| `biome` | `DeepWater`, `ShallowWater`, `Shore`, `BorealForest`, `HighlandTaiga`, `Tundra`, `Mountain`, or `Snow` |
| `moisture` | Deterministic regional moisture |
| `temperature`, `coldness` | Deterministic climate values; `coldness` is `1 - temperature` |
| `treeCover` | Procedural tree habitat/food-cover potential at this point |
| `grassBiomass` | Procedural grass habitat/food potential at this point |
| `rockDensity` | Procedural rock potential at this point |
| `riverStrength` | Effective river-carving influence at this point |
| `lakeStrength` | Effective lake-carving influence at this point |
| `hydrology` | The stronger of `riverStrength` and `lakeStrength` |
| `waterAvailability` | Combined open-water, shoreline proximity, hydrology, and moisture availability |

`treeCover`, `grassBiomass`, and `rockDensity` describe deterministic environmental capacity. If animals consume food, keep the mutable amount in the animal/ecosystem system and use these terrain values as carrying capacity or regrowth input.

## Coordinate helpers

The helpers consistently handle negative coordinates:

```csharp
Vector2Int cell = TerrainGrid.WorldToCell(worldPosition, 10f);
Vector2 cellCentreXZ = TerrainGrid.CellToWorldPosition(cell, 10f);

Vector2Int chunk = TerrainGrid.WorldToChunkCoordinate(worldPosition, meshSettings);
Vector2 chunkCentreXZ = TerrainGrid.ChunkCoordinateToWorldPosition(chunk, meshSettings);
```

`Vector2.x` represents world X and `Vector2.y` represents world Z. `CellToWorldPosition` returns the center of the cell. Terrain chunks use center-based coordinates, while general simulation cells use conventional floor-based grid coordinates.

## Behavioral guarantees

- The same `HeightMapGenerator` and `TerrainHeightEvaluator` produce rendered chunks and off-screen samples.
- Results are deterministic for the same settings and world X/Z position.
- Adjacent chunks return matching values on their shared border.
- Water, shoreline, biome, vegetation, and animal queries use the same `EnvironmentDefinitions` asset.
- `TryGetEnvironmentSample` and `TrySample` return `false` for missing configuration or non-finite coordinates; otherwise they return a valid sample for any finite world X/Z position.

The settings assets are in `Assets/TerrainGeneration/Settings`. The automated Edit Mode tests are in `Assets/TerrainGeneration/Tests/Editor/TerrainEnvironmentSamplerTests.cs`.
