# Noise maps and matching terrain figures

Open **Tools → Boreal Ecosystem → Terrain Report Preview** in Unity. No plane, mesh GameObject, or Play mode is required. The old Map Preview inspector also has a button for this window.

## Generate and export

1. Click **Load from active terrain** to read the current terrain settings. During Play mode this includes the runtime seed/settings; in Edit mode it reads the scene's assigned assets. If no generator exists, the default terrain settings assets are used.
2. Choose **Origin / lowlands**, **Mountain belt**, or enter a center chunk coordinate. A 9 × 9 region provides more context than a single chunk. The mountain preset is an approximate region, not a guarantee of mountains for every configuration.
3. Use **3D mesh LOD 0** for the full-resolution comparison, then click **Generate report preview**. Regenerate after changing settings; previews are fixed snapshots, not live updates.
4. Inspect **Report overview**, **Individual stages**, or **3D terrain**. Drag the 3D view to orbit and scroll over it to zoom. **North-up oblique** and **Top down** make comparison with the maps easiest.
5. Click **Export all PNGs + captions…** and choose a folder outside `Assets`, such as Downloads. A new timestamped folder contains the stage-map PNGs, 2048 × 2048 terrain PNGs, suggested captions, and a serialized settings snapshot. With hydraulic erosion enabled, it also includes the before-erosion 3D view and numerical CSV samples. Existing exports are not overwritten.

The preview does not modify terrain settings, scenes, gameplay, or animal systems. Closing the window releases its temporary meshes, textures, and rendering resources. The editor scripts and preview shader do not ship with the game.

## Suggested Section 3.2 figures

| Report topic | Export |
| --- | --- |
| Base noise | `01-base-noise.png` |
| Intermediate shaping maps | `02-domain-warped-noise.png`, `03-mountain-ridge-blend.png`, and relevant masks `04`–`06` |
| Final heightmap | `08-final-heightmap.png` |
| Matching 3D terrain | `09-matching-3d-terrain.png`; optionally `10-top-down-terrain.png` |

Use one region and seed for the complete sequence. Use the generated `FIGURE-CAPTIONS.md` for precise descriptions. For a short presentation, show base noise → mountain ridge blend → final heightmap → matching 3D terrain, and explain the additional masks separately.

## What the figures mean

- Base noise is the configured octave-noise map with domain warp disabled for comparison. Domain-warped noise is the actual input to the terrain evaluator.
- Ridge blending, world falloff, river carving, lake carving, and height-curve evaluation reuse the production terrain calculations. The preview does not implement a separate approximation of the terrain algorithm.
- River and lake masks show carving weights, not every position classified as water. World falloff shows height subtraction. A disabled effect has a black mask or leaves the previous height unchanged.
- All 2D maps have north (+Z) at the top and east (+X) at the right. Shared boundary samples overlap once when chunks are stitched. Global normalization is recommended; local normalization retains its existing chunk-dependent behavior.
- Grayscale uses a fixed 0–1 range for intermediate maps and the settings' minimum/maximum heights for the final heightmap. The sampled region's range is also reported. PNGs are 8-bit illustrations, not lossless numerical height data; values beyond their display range are clipped.
- The 3D view uses the production mesh generator and the selected LOD, with no vertical exaggeration. Simplified biome-tint lighting makes the geometry readable. It does not reproduce the runtime shader's textures, vegetation, or water-surface meshes. Use an in-game screenshot separately if you also need to demonstrate the finished environment's appearance.
- For Section 3.4, use **Tools → Boreal Ecosystem → Hydraulic Erosion Example**, then generate and inspect the **Hydraulic erosion** tab. See [HYDRAULIC_EROSION.md](HYDRAULIC_EROSION.md) for controls, algorithms, limits, and captions. The preview checkbox enables the real height-changing model on a temporary settings copy. Existing erosion-like shader streaks remain a separate visual effect.

## Tests

For four seasonal views with actual procedural vegetation, open **Tools → Boreal Ecosystem → Seasonal Scenario Preview**. See [SEASONAL_SCENARIOS.md](SEASONAL_SCENARIOS.md) for the report workflow, API and mockup limitations. The original noise/erosion views remain unchanged.

In Unity's **Window → General → Test Runner**, run the EditMode fixture `TerrainReportDataTests`. It checks runtime/preview agreement across whole regions and shared borders (including negative coordinates), mesh agreement at different LODs, disabled effects, grayscale orientation, repeatability, unchanged settings, cancellation, and size limits.
