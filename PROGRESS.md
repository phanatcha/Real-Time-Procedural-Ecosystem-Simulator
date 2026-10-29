# Implementation handover — 28 October 2026

Source backlog: https://claude.ai/artifact/6Pz81LoYV7y36T4RN6n8oe

## Starting point

Checked local `main` at `f8b84768`, the architecture rules, existing code/tests, and the 52-item artifact. The terrain, sea, body plans, mating, perks, spatial queries, and layered assemblies were already present. The seasonal work was stranded in local commit `9d29f45` before the assembly split. Existing uncommitted Grubby model/editor assets and the generated solution were preserved.

## Implemented in the working tree

- Ported the seasonal commit to the current assemblies: live season clock/summary, seasonal regrowth and snow burial/movement, climate-vs-season temperature queries, off-screen seasonal ecology, breeding threshold/torpor/mutation-rate genes, telemetry/HUD updates, and species calibration. Ported its tests as well.
- Resolved forest-food changes by retaining both the spruce/forest visuals and the seasonal site data. Preserved the newer terrain implementation. The only scene edit sets the species threshold from 0.02 to 0.05; the stashed canvas deletion was not applied.
- Added `Boreal.World.WorldSeason`: the Animals clock publishes terrain's season value into this lower layer, and the World temperature provider reads it without an upward dependency. State clears when the clock disables and at subsystem registration. Added a lifecycle regression test.
- Added an opt-in CSV recorder as the first part of item 30. See `RUN_RECORDING.md` for setup, outputs, and limitations. Added three tests covering locale-safe CSV, stable population statistics, and simulation-time scheduling across pause, skipped frames and reset.

## Verification

- All 16 runtime, editor and test assemblies compile using the project's Unity 6000.4.1f1 Roslyn compiler and assembly response files, with added source/reference entries. Existing obsolete-API and unused-event warnings remain.
- Recorder's three pure tests passed outside Unity using the actual NUnit assertions and source files (CSV formatting, population statistics, sampling cadence).
- Full Unity Edit Mode execution now passes: **257 passed, 0 failed, 0 skipped**, including tests that enter Play Mode and the GPU ocean tests. The local licensing connection was resolved. See `TEST_RESULTS.md` for results, fixes and evidence, and `TESTING.md` for the repeatable command-line workflow.
- Committed and pushed straight to `main` on 29 October 2026, in four commits: the seasonal port, the recorder, the Grubby model with its Editor tools, and these runners and notes. Checking by eye in the Editor is still outstanding.

## Soak runs, 29 October 2026

Five simulated years of the full Terrain scene per run with `Tools/run_soak.py` (see `TESTING.md`): 50×, recorder on, the scene's 3 founders, off-screen mode off. One run per setting, so this is a first look, not evidence.

| Run | Animals in years 2–5 | Average | Births (from two parents) | Deaths: starvation / old age / cold |
|---|---:|---:|---:|---:|
| FOREST-001, seasons on | 5–20 | 12 | 122 (11) | 88 / 27 / 0 |
| FOREST-001, seasons off | 16–32 | 23 | 235 (47) | 146 / 65 / 0 |
| MOSSY-FJORD, seasons on | 71–227 | 124 | 1297 (244) | 879 / 320 / 0 |

- Both seeds got through five winters. Once seasons reach full strength in year 3, each winter is 15–17 °C colder, snow covers about 60% of the ground and buries about 40–45% of the land plants in reach (the cap is 50%), and the population roughly halves (MOSSY-FJORD 221 → 105 and 217 → 98, FOREST-001 15 → 8); summer and autumn rebuild it.
- Winter kills only through hunger. No run had a death from cold, and the torpor gene stayed at 0.00–0.03, so the cold itself puts no pressure on the animals and torpor isn't selected.
- FOREST-001, the scene's default seed, stays small even without seasons: about 110 land plants in reach, against 450–740 on MOSSY-FJORD, so it levels out near 25–30 animals and seasons halve that. Its lowest point was 4 animals, in year 4's spring. Re-check founder placement and balance there (backlog item 9) before demos.
- Seasons cut the share of births from two parents on FOREST-001 from 20% to 9%.
- Spruces stand in north–south lines on every seed, in the top-down map and in the game camera. The tree cells are 9.5 m apart (phanatcha's forest floor, `6ecd5e4`) and `ValueNoise.Hash21` multiplies z by 456.21: 9.5 × 456.21 = 4333.995, so the hash input moves by only 0.005 per cell and 5–7 neighbouring cells along z get the same x offset. Grass (3.5 m) and rock (16 m) spacings don't resonate. This is phanatcha's terrain code and hasn't been changed.
- The top-down map tool now switches phanatcha's atmosphere off during a capture. It hides everything more than 790 m from the camera, so earlier maps from this branch were mostly haze.
- In batch mode the terrain ground draws black. Trees, water, sea plants and animals look right. Not checked in the Editor.

## Next work, in priority order

1. Automated tests are green (257/257). Use `Tools/run_unity_tests.py` after further code changes; the suite runs in an isolated copy without UI automation.
2. Batch soak runs above cover population survival through winters on FOREST-001 and MOSSY-FJORD, and seasons off. Still to check by eye in the Editor: the ground's look, food burial and unburial, disabling seasons and regenerating the world while playing, and the base-climate heatmaps.
3. Recorder lifecycle tests now cover pause/resume, regeneration including repeated paused resets, disable/re-enable and quit finalization. Next, inspect exported animal counts against the HUD in the full Terrain scene at 1×/50× and through extinction. Extend with blocked-breeding counters and automatic revision stamping, then charts.
4. Review and commit the seasonal port and recorder, keeping a deliberate decision about the pre-existing model/editor assets. Open a PR for the port (artifact item 52); don't call the unverified checklist items Done.
5. Run mutation-load on/off comparisons (46) with off-screen mode disabled and multiple replicates, retaining extinctions. Then juvenile growth (47), followed by litter size (16). Family tree (28) can build on the species CSV. Model hookup (33) needs a lower-detail rendering plan before hundreds of 14k-triangle animals.

Frozen lakes, thirst, disease, pregnancy and paired perk alleles remain later work. Presentation source is not in this repository.
