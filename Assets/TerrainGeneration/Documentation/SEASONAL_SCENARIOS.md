# Seasonal scenario mockup

## Report workflow

1. Open **Tools → Boreal Ecosystem → Seasonal Scenario Preview**.
2. Click **Generate seasonal preview**. The window initially searches for a forest location using the current default assets. Use **Find forest location** again after changing settings, or select a chunk manually.
3. Compare all four views, or select a season. Drag a view to orbit; scroll over it to zoom. Camera changes apply to every season.
4. Click **Export four PNGs + captions…**. Choose a folder outside `Assets`.

Exports: `01-spring.png`, `02-summer.png`, `03-fall.png`, `04-winter.png`, `FIGURE-CAPTIONS.md`, `seasonal-summary.csv`, and `settings-snapshot.json`. Put the four images in a 2 × 2 figure in that order. The captions contain the region, camera, asset/preset settings, and measured environmental-index means.

This is an **editor-only appearance mockup plus a runtime environmental query API**. Opening/generating/exporting it does not change scene objects, materials, saved settings, or existing gameplay. There is no automatic runtime season controller or animal-behavior integration yet. It uses simplified report shading, with actual production LOD-0 terrain geometry, vegetation placement constraints, and procedural pine/grass/rock meshes. All four seasons reuse identical geometry and placements. Existing hydraulic erosion settings are held fixed across the comparison.

Snow coverage, wetness darkening, ground/grass color and lighting tint vary. Pine foliage stays evergreen. Snow on branches is an upward-facing shading approximation, not snow geometry. Water remains water: there are no ice colliders, frozen-water access rules, water-level changes or flooding. No biome, height, slope, collision mesh or vegetation placement is changed by the season.

## Public API for the animal team

Use the existing `TerrainEnvironmentSampler` (for example `terrainGenerator.WorldEnvironmentSampler`) and pass an explicit shared season. No terrain mesh or loaded GameObject is needed when constructing the sampler directly from its usual settings assets.

```csharp
SeasonState season = SeasonState.For(BorealSeason.Winter);
SeasonalEnvironmentSample current = sampler.SampleSeasonal(new Vector2(worldX, worldZ), season);
float temperature = current.temperature;
float availableFood = current.accessibleGrassBiomass;
float suggestedSnowCost = current.movementCostMultiplier;
BiomeId biome = current.baseEnvironment.biome;
```

`TrySampleSeasonal(Vector2, SeasonState, out SeasonalEnvironmentSample)` and the Vector3 overload return false for invalid coordinates or unconfigured samplers. `SampleSeasonal` throws on invalid queries, matching the existing `Sample` convention.

For a cached ecosystem cell, apply the same pure calculation to its base environment:

```csharp
SeasonalEnvironmentSample current = SeasonalEnvironment.Evaluate(cellEnvironment, season);
```

Always supply the **original base environment**, not previously season-adjusted values. This preserves stable terrain caches and avoids accumulating temperature offsets. Store one simulation-wide season or year-progress value and pass it to both near and distant queries at the same simulation time. Do not advance the season independently in each cell or derive it from camera visibility.

Optional interpolation: `SeasonState.AtYearProgress(yearProgress)` wraps at 1. Spring, summer, fall and winter anchors are 0, 0.25, 0.5 and 0.75. This is an illustrative cycle, not a real calendar; the preview buttons use the four fixed anchors. Species-specific responses belong to the animal system.

| Returned value | Meaning |
| --- | --- |
| `baseEnvironment` | Original sample, including unchanged height, normal, slope, biome, land/water, resource stock and water availability |
| `temperature`, `coldness` | Normalized 0–1 indices, **not degrees Celsius**; coldness is 1 − temperature |
| `moisture` | Normalized seasonal moisture index; not soil-water volume |
| `snowCover` | Fractional surface-cover index, not depth in meters |
| `grassGrowthMultiplier` | Proposed seasonal growth-rate factor; does not itself grow or consume resources |
| `accessibleGrassBiomass` | Accessibility-adjusted baseline grass index; does not replace stored resource stock |
| `movementCostMultiplier` | Proposed snow-related cost, 1–1.75; not pathfinding, traversability, or species-specific movement |

Never infer that water is walkable because its snow cost is 1. Existing land/water and slope constraints still apply. Water availability is unchanged; this mockup does not model freezing or drinking through ice. To include grazing or regrowth, the animal/resource system must combine these factors with its actual remaining resources. Existing ecology/population/heatmap systems are not silently switched to seasonal values.

## Presets and rules

| Season | Temperature offset | Moisture offset | Growth multiplier | Snow potential | Dormancy |
| --- | ---: | ---: | ---: | ---: | ---: |
| Spring | −0.16 | +0.18 | 0.70 | 0.50 | 0.15 |
| Summer | +0.08 | −0.04 | 1.00 | 0.00 | 0.00 |
| Fall | −0.12 | +0.08 | 0.35 | 0.08 | 0.85 |
| Winter | −0.60 | −0.12 | 0.05 | 1.00 | 1.00 |

Temperature and moisture are clamped after adding their offsets. Snow potential is reduced by warmer local temperatures, steep slopes, and a deterministic world-space patch factor. Elevation affects snow through the existing elevation-dependent base temperature. Seasonal snow is added only to land, independently of permanent high-elevation biome coloring.

Accessible grass = baseline grass × (1 − 0.9 × snow cover) × lerp(1, 0.75, dormancy). Growth factor = preset growth × lerp(0.25, 1, seasonal moisture). Suggested movement cost = 1 + 0.75 × snow cover. These coefficients are design assumptions, not biological measurements. Existing grass biomass is itself a normalized habitat/resource proxy, not kilograms of forage.

Displayed/exported means use land points in a uniform 33 × 33 grid over the selected region. They are not animal survival statistics or measured habitat-suitability scores. Preview vertex colors interpolate the same CPU environmental model across the mesh; branch shading uses the local surface normal. Resource/sample results are independent of visual lighting.

## Scope and verification

Label the report figure **“Seasonal scenario mockup”**. It demonstrates changing conditions on a fixed terrain; it does not establish animal adaptation, evolution, or measured accessibility outcomes. The presets represent a stylized inland-boreal scenario, not calibrated weather for all of Norway. No seasonal erosion reruns, meltwater transport, persistent snowpack, rainfall, dynamic flooding, ice physics, food-stock simulation, or automatic animal response is included.

EditMode tests cover deterministic/off-screen sampling, negative coordinates and chunk borders, unchanged base geometry/biomes/resource stock, bounded outputs, colder/snowier winters, evergreen appearance, water invariants, year wrapping, invalid inputs, identical preview geometry and cancellation. The report export is also rendered and visually checked separately.
