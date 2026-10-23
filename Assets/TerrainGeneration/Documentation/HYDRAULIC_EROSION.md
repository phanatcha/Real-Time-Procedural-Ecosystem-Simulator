# Hydraulic erosion: implementation and Section 3.4 figures

## Generate the report example

1. Open **Tools → Boreal Ecosystem → Hydraulic Erosion Example**.
2. Click **Generate report preview**. The preview enables erosion on a temporary settings copy, not on the saved gameplay asset.
3. Select **Hydraulic erosion** to compare the unchanged and eroded terrain. Both panels use the same seed, region, camera, LOD, lighting, and vertical scale. Drag either panel to orbit both views.
4. Click **Export all PNGs + captions…** and choose a folder outside `Assets`.

| Figure | File | Meaning |
| --- | --- | --- |
| Before erosion, 3D | `14-before-erosion-3d.png` | Original geometry after noise shaping, falloff, river/lake carving, and the height curve |
| After erosion, 3D | `09-matching-3d-terrain.png` | The same geometry with the hydraulic erosion delta applied |
| Before / after heightmaps | `11-before-erosion-heightmap.png`, `08-final-heightmap.png` | Identical grayscale height limits for a fair comparison |
| Erosion / deposition | `12-erosion-deposition-change.png` | Blue: material removed; orange: material deposited; white: unchanged |
| Droplet flow | `13-droplet-flow.png` | Water-weighted droplet visits, log-scaled; not water depth or discharge |

Exports include `FIGURE-CAPTIONS.md`, the exact settings snapshot, and `erosion-height-samples.csv` with world X/Z, before/after heights, actual height difference, and log-relative droplet flow. The printed change statistics are measured over the exported region. The recorded bake duration covers the whole erosion field, not frame rendering.

Suggested caption: “Comparison of the same procedural terrain before and after seeded droplet-based hydraulic erosion. Blue areas indicate height reduction through erosion, while orange areas indicate deposition. Camera framing and vertical scale are identical in both 3D views.” Add the seed, droplet count, resolution, and measured changes from the exported captions.

## Enable erosion in the actual terrain

Erosion is **off by default**, preserving existing worlds. Select the `HeightMapSettings` asset under `Assets/TerrainGeneration/Settings`, expand **Erosion Settings**, and enable it. Use **Global** noise normalization. Exit Play mode before changing settings, then start and regenerate the world through its existing seed/Generate controls.

This changes real vertex heights, not only shader colors. Terrain render meshes, collision meshes, vegetation ground samples, and off-screen environmental queries all use `HeightMapGenerator`. Eroded slopes, normals, land/water classification, and biome classification are therefore derived from the modified surface. The existing render/collision LOD approximations still apply. Settings are generation-time inputs: changing them does not update already rendered chunks or an existing sampler's cached chunks in place; regenerate the world.

The report checkbox only affects preview copies. To compare with gameplay, use the same height settings, erosion parameters, and runtime terrain seed; **Load from active terrain** can capture the active scene's settings.

## Algorithm

The implementation is a CPU, generation-time, particle/droplet approximation, inspired by [Sebastian Lague's hydraulic erosion project](https://github.com/SebLague/Hydraulic-Erosion). It is not an implementation of the full shallow-water equations.

1. Generate one coarse height grid over `[-worldRadius, +worldRadius]` on both terrain-sampling axes, using the same noise, ridge, river/lake, falloff, and height-curve calculations as the normal terrain. Noise generation accepts a sample spacing so the bake uses the existing coordinate convention. Heights are normalized by the configured height range for the droplet simulation.
2. Place droplets at pseudorandom positions with a deterministic seed derived from the terrain seed and erosion seed. Each starts with unit water and speed and no sediment.
3. Bilinearly sample the current height and gradient. Blend downhill movement with the previous direction using inertia, then move one bake cell.
4. Let `rise = nextHeight - currentHeight`. Carrying capacity is `max(minimumCapacity, max(0, -rise) × speed × water × sedimentCapacity)`.
5. If the droplet rises or carries more sediment than capacity, deposit sediment into the four surrounding nodes using bilinear weights. Otherwise, erode the lesser of the available carrying capacity times the erosion rate and the downhill height drop. Distribute erosion over a normalized radial brush.
6. Update speed as `sqrt(max(0, speed² - rise × gravity))` and water as `water × (1 - evaporationRate)`. Record the water-weighted visits for the flow diagnostic.
7. Stop on zero direction, boundary exit, low remaining water, or maximum lifetime. Deposit the remaining sediment at the last valid position, subject to the local height limits. Track any sediment that cannot settle separately; it is not silently added back or discarded from the accounting.
8. Store `erodedHeight - originalHeight` as an immutable, bilinearly sampled delta field. Add this delta to the normal, finer-resolution terrain height and clamp changed samples to the terrain's height range.

Changes are bounded by **Max Height Change** and taper to zero at the outer bake boundary. This avoids a step where the eroded finite area meets the unmodified exterior. Shared chunk edges sample the same cached field: droplets are not restarted independently per chunk. Noise-derived fine detail is retained because the runtime samples the delta, rather than replacing the terrain with the coarse bake itself.

The bake is cached per settings object with a parameter fingerprint and a lock. Concurrent chunk requests reuse one completed bake; cancelled bakes are not cached. Changing a seed, height curve, shaping parameters, or erosion parameters invalidates the bake. The cache uses weak keys so obsolete runtime settings do not remain permanently retained.

## Parameters and scope

- Defaults: 257 × 257 bake nodes, 6,000 droplets, at most 60 steps per droplet, brush radius 3, inertia 0.1, erosion/deposition rates 0.3, evaporation 0.02, capacity factor 4, and maximum local height change ±6 units.
- Bake resolution and droplet count are independent. Changing resolution also changes the world-space brush size and travel distance; comparisons should hold resolution fixed unless it is the variable under study.
- `worldRadius` is in the terrain generator's sampling coordinates. Multiply distances by `MeshSettings.meshScale` for rendered-world distances. Horizontal bake spacing is `2 × worldRadius × meshScale / (resolution - 1)`.
- The map is computed once per configuration, not every frame. It does not simulate ongoing rainfall, seasons, flooding, or animal-driven terrain changes.
- The existing procedural river/lake masks are not replaced by a watershed solution. The model does not solve standing-water levels, infiltration, soil layers, sediment density, groundwater, or sea-level hydraulics. Droplet flow is not an animal drinking-water map.
- Droplet counts and rates are dimensionless model controls, not calibrated rainfall amounts or geological years. Do not claim geophysical accuracy or that all slopes necessarily become gentler; erosion can cut channels as well as deposit material.
- The before/after report uses simplified biome tinting and no vegetation to expose geometry. Existing erosion-like streaks in the runtime shader remain a separate visual effect.
- Node-level height change is bounded; the bilinear delta preserves that bound. Numerical sediment accounting refers to the bake grid. Applying the delta to the finer terrain and clipping at height limits can alter that accounting; it should not be reported as exact physical mass conservation of the final rendered world.

## Verification

Run the EditMode fixture `HydraulicErosionTests` in Unity's Test Runner. Tests cover flat/zero-effect behavior, deterministic seeds, erosion and deposition, sediment accounting, height limits, unchanged outer boundaries, shared chunk borders, mesh/query agreement, concurrent cache reuse, parameter invalidation, cancellation, and report color semantics. Existing terrain/report tests remain applicable.

## Reference and license

Algorithm reference: Sebastian Lague, *Hydraulic Erosion* (2019), [repository](https://github.com/SebLague/Hydraulic-Erosion), [reference implementation](https://github.com/SebLague/Hydraulic-Erosion/blob/master/Assets/Scripts/Erosion.cs). This project's implementation adapts the droplet approach with shared-world delta sampling, bounded changes, boundary tapering, final sediment accounting, cancellation, and report diagnostics. The upstream MIT notice is retained below.

MIT License

Copyright (c) 2019 Sebastian Lague

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
