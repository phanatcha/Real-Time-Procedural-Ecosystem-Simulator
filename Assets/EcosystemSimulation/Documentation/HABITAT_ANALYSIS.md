# Habitat availability and accessibility

Open **Tools → Boreal Ecosystem → Habitat Analysis** in Unity. It measures, for each seed, how much suitable habitat the world has and how much of it the founders can walk to. Play mode is not required, and no scene objects or settings are changed.

## Run and export

1. The window loads the active terrain's settings and the founders' start point. **Load from active terrain** and **Use scene start point** refresh them.
2. Enter seeds, one per line, exactly as typed on the seed screen (for example `FOREST-001`). Each seed builds the same world as in the game: its terrain, ridges, rivers, lakes, moisture, temperature and plant patches all take their seeds from it. `settings-snapshot.json` lists them under `worldSeeds`.
3. Keep **Sample spacing** at 8 m, the spacing the animals' navigation samples terrain at. 16–32 m is faster for quick comparisons but can miss narrow barriers.
4. Click **Analyse**. The results list shows availability, accessibility and habitat regions per seed; select one to see its map.
5. Click **Export maps + CSV…** and choose a folder outside `Assets`. A new timestamped folder contains:
   - `habitat-summary.csv`: one row per seed, for comparing worlds.
   - `NN-<seed>-habitat-map.png`: the map, scaled up without smoothing, north at the top.
   - `NN-<seed>-cells.csv`: fractions per 250 m cell, on the off-screen population model's grid.
   - `FIGURE-CAPTIONS.md` and `settings-snapshot.json`: definitions, results and the full settings used.

To test a terrain feature, change one setting (for example ridge strength or river width) in a copy of the height settings, run the same seeds again and compare the two summary files.

## What is measured

| Term | Meaning |
| --- | --- |
| Walkable | Land, not shore, with a slope of at most 32°: the rule in `AnimalTerrainWorld`. |
| Suitable habitat | Walkable ground where ground plants grow (grass biomass ≥ 0.02 and a regrowth rate ≥ 5%) and the temperature is in the founders' comfort range (−16 °C to 18 °C). |
| Availability | Suitable habitat as a share of all land. |
| Accessibility | Share of suitable habitat reachable from the start, moving between side-by-side walkable samples. Walkable ground without food still counts as a route. |
| Habitat regions | Separate walkable areas that hold any suitable habitat. |

The defaults copy the live simulation: `AnimalTerrainWorld` for walking, `FoodSpawner` for where plants grow, the plant growth profile from `AnimalTerrainDemoBootstrap` (−12 °C, 2–13 °C, 25 °C) and the founders' temperature genes. **Reset requirements to the founders'** restores them after experimenting.

## Limits

- This is a model of the simulation's rules, not a field measurement.
- Barriers narrower than the sample spacing can be missed. The NavMesh's agent size and step height are not reproduced exactly.
- Tall forest food is excluded because the founders cannot reach it. Evolved animals with longer necks could use more habitat than shown.
- Temperatures are the static climate; seasons are not modelled.

## Tests

In **Window → General → Test Runner**, run the EditMode fixture `HabitatAnalysisTests`. It checks rivers, steep ground and shore as barriers, cold or bare ground as passable but not habitat, the start moving to the nearest walkable ground, per-cell totals, that the plant growth rule matches `FoodItem`, and that the real terrain gives the same result twice.
