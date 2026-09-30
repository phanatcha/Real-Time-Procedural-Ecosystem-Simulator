# Off-screen population model

`EcosystemSimulationController` keeps distant animals as numbers instead of GameObjects. The island is divided into 250 m cells (`EcosystemSimulationSettings.cellSize`), and each cell stores a count per species. Near the camera, counts become animals (within 400 m); far away, animals return to counts (beyond 550 m).

This model uses fixed species from `EcosystemSpeciesDefinition` assets. For the evolving animals, see `OffscreenPopulationBridge` and `AnimalTerrainIntegration/Documentation/OFFSCREEN_EVOLVING_ANIMALS.md`, which reuse its growth formula.

## Each cell update

Cells near the focus update every 0.5 s and distant ones every 6 s. Each update covers the time since the last one.

1. **Capacity.** `K = carryingCapacityPerCell × habitat suitability`. Suitability combines land share, slope, temperature and food.
2. **Growth.** Logistic growth toward the room left in the cell:

   `N(t) = K / (1 + (K − N) / N · e^(−r·t))`

   The formula is exact for any time step, so a distant cell updated every 6 s ends where it would after many short steps. Small populations grow by about `r` per second, growth slows to zero at `K`, and populations above `K` fall back to it. Where a cell has no room, the population dies out at `deathRatePerSecond` instead.
3. **Room left.** Animals materialized in the cell count against `K`, so counts and GameObjects together never exceed the cell's capacity.
4. **Migration.** A share of each population moves to side neighbours, weighted toward suitable cells with room left.

## Default rates

| Setting | Default | Where it comes from |
| --- | --- | --- |
| `growthRatePerSecond` (r) | 0.011 | `EstimateGrowthRate(45, 60, 300)`: founders mature at 45 s, breed at most once per 60 s and live 300 s. `(1 − 45/300) / 60 − 1/300 ≈ 0.011` |
| `deathRatePerSecond` | 1/300 | One founder lifespan |

`r` is the fastest a population of live animals could grow. Food and cold slow the live animals down, so it is an upper bound until step 3 derives rates from each group's genes.

## Limits

- A model of the simulation's own rules, not of real populations.
- Species are fixed; there are no genes, mutation or speciation off-screen yet.
- Growth and migration use cell averages; the fine-grained barriers in `HABITAT_ANALYSIS.md` are not applied inside a cell.

## Tests

`EcosystemPopulationGrowthTests` checks the growth formula against step-by-step integration, one long step against many short ones, growth at low density, the approach to capacity and the default rates. `EcosystemSimulationTests` checks growth and the room taken by materialized animals on the real terrain grid.
