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
4. Item 52 was already committed and pushed by Claude (see above). The 29 October follow-up below is local work; review and commit it separately. Do not mark visual checks Done based on automated tests alone.
5. Run mutation-load on/off comparisons (46) with off-screen mode disabled and multiple replicates, retaining extinctions. Then juvenile growth (47), followed by litter size (16). Family tree (28) can build on the species CSV. Model hookup (33) needs a lower-detail rendering plan before hundreds of 14k-triangle animals.

Frozen lakes, thirst, disease, pregnancy and paired perk alleles remain later work. Presentation source is not in this repository.


## High-priority follow-up — 29 October 2026

Re-read the updated 53-item artifact against clean `main` at `1ab76d2c` before editing. Claude's seasonal/model commits and soak runner are preserved.

- **53, spruce rows:** vegetation randomness now hashes integer cell coordinates, the full world seed, vegetation type and independent purpose streams. This removes the 9.5 m float-hash resonance without changing terrain/climate noise or merely adjusting tree spacing. All rendered vegetation placements change under the new algorithm; old screenshots are not exact placement references. Added near/far/negative-coordinate hash regressions, full-seed/stream checks, and a real spruce-placement regression alongside the existing grass test.
- **30, recording:** schema 2 appends breeding-wait episode counts and animal-seconds, globally and per species. Each continuous eligible-but-capped interval counts once; pause adds nothing and world regeneration clears totals. Added automatic full-commit/dirty stamping in the Editor while preserving explicit soak/experiment labels. Player build stamping and charts remain unfinished.
- **30, verification:** live-animal regression checks exported population and wait counters against the same manager values used by the HUD at 1× and 50×. Full Unity suite: **266 passed, 0 failed, 0 skipped** (`Logs/TestResults/20261029T125629673218Z/results.xml`).
- **46, prerequisite correction:** the soak runner previously included the initial seasonal phase in its duration and kept recording while maps rendered. It now measures elapsed simulation time from WorldStartTime, closes the recording at the horizon, and pauses while maps are made. A complete frame can slightly overshoot the requested horizon. Earlier soak results remain exploratory and their durations should not be assumed exact.
- Full Terrain visual/CSV smoke run on FOREST-001: the map shows scattered trees instead of the reported repeated north–south offsets. The pre-existing black ground in batch captures remains; this is not a claim that the terrain shading or the whole scene has passed an Editor visual check.

Next priorities: (1) diagnose the black ground and review the full scene visually; (2) investigate FOREST-001 founder placement/accessible food using multiple runs rather than retuning from one sample; (3) add mutation-load treatment controls and a replicated experiment (46), preserving failed/extinct runs; (4) charts (30), then juvenile growth (47). These follow-up changes went to `main` on 1 November.

Corrected-duration verification: FOREST-001 requested 400 s, recorded 400.014 s across 41 strictly increasing observations, and closed before map capture. Evidence is saved under `Logs/SoakRuns/2026-10-29-priority-review/`. The vegetation preview is from the preceding visual smoke run.

## Backlog items 9, 16, 30, 43, 46 and 47 — 1 November 2026

Built on the 29 October follow-up above, which was still uncommitted.

- **47, juvenile growth:** an animal born in the simulation starts at **Newborn Size Fraction** (0.4) of its adult size, speed, vision and feeding reach and grows to full size by its maturity time (`SeekFood.UpdateGrowth`). Growing costs energy in proportion to the body mass put on: **Growth Energy Cost** (0.5) times the adult body's own meat (Carcass Body Energy × body mass), spread over the youth, so maturing sooner is a shorter but costlier youth instead of a free advantage (item 9's note). A young animal has the mass of its current size, so it leaves a small carcass and costs less to run while small. The body, its parts and its upkeep follow in 5% steps. Founders placed by a scene and animals back from off-screen numbers are full-grown. The genes keep the adult values; species averages of feeding reach and body mass now include the young.
- **16, litter size gene:** `AnimalGene.LitterSize`, 1 to 4, 1 in founders, additive. A birth has the gene's whole part in young plus one more with the fraction as the chance, each with its own genome, and the energy the birth hands over (half a cloning parent's, or both mates' shares) is split equally between them, so reproduction never creates energy. Each child counts as a birth. A later child with nowhere to be born isn't born and its share stays with the parent. Shown on the panel's largest-species line and in `traits.csv`. Off-screen populations ignore it.
- **30, charts:** the panel now draws a chart underneath (G hides it): animals, species alive and plants shown over simulated time, each scaled to its own peak, with winters shaded. It samples every 10 simulated seconds, as the recorder does, and once full keeps every other point and samples half as often. The remaining part of item 30 is stamping the revision in player builds.
- **43, season input for the heatmap:** `SurvivabilityHeatmap.scoreCurrentSeason` (Inspector, off by default) scores the current season's temperatures instead of the base climate's and redoes the map whenever the season clock moves on a step. The saved statistics say which was used.
- **46, comparison runs:** `Tools/run_soak.py` takes `--harmful-chance` and `--experiment`; `Tools/run_comparison.py` runs matched replicates of mutation load on (10%) and off (0%), alternating, and `Tools/summarise_runs.py` prints each run and the mean and spread per treatment. First experiment, `Logs/SoakRuns/comparison-20261101T151729Z`: six five-year runs of MOSSY-FJORD each way at 50×, off-screen mode off, 3 founders, with growth and the litter gene in.

  | Treatment | Final animals | Births from two parents | Sexual drive | Harmful mutations carried |
  |---|---:|---:|---:|---:|
  | Load off (0%) | 84 (66–102) | 17% (10–20) | 0.49 | 0 |
  | Load on (10%) | 90 (79–106) | 18% (16–20) | 0.49 | 1.0 (0.7–1.4) |

  No extinctions, no species splits, every run at about 85 animals after year 2. Mutation load did not make mating pay in five years: one harmful mutation cost 2% of energy in these runs (5% since the second round), too little for selection to see. Raise the cost per mutation or run longer. Litter size drifted only to 1.01–1.06. Populations are lower than the earlier MOSSY-FJORD soak's 100–227 because growth now costs.

  **Correction, 10 November (item 55):** this note first said the largest species' average maturity time "rose from 30 s to 44–49 s". It did not. Founders start at 45 s (`AnimalTerrainDemoBootstrap` overrides `SeekFood`'s 30 s default), and the twelve runs' `traits.csv` files end at 44.1 to 48.6 s, some below the start and some above. The second experiment's twelve runs (`comparison-20261101T155009Z`) end at 42.7 to 45.7 s. Maturity time has not measurably moved, so juvenile growth is not shown to select for a longer youth.
- **9, FOREST-001's small population:** the scene's Simulation Start at (0, 0) is in a strait. The three founders landed about 350 m apart on two islands: two on the sandy lowland tip of the western island, 100 to 150 m from its forest (green on the heatmap), and one on the western limb of the island to the south-east, which the heatmap scores poorly. The camera's 300 m plant radius sits mostly over water, which is why so few plants show. Nothing was changed: either move the Simulation Start for this seed, have the bootstrap choose a start among candidates by walkable land and plant sites within the spawn radius, or demo on MOSSY-FJORD. Founder positions are now in the soak log.
## Backlog item 27, thirst — 1 November 2026, third round

- Every animal has hydration (`maxHydration` 100) that drains 0.4/s in comfort, plus as much again per unit of heat stress, so a full animal lasts about four minutes. Below `thirstThreshold` (50%) it is thirsty; with none left it loses `thirstDamagePerSecond` (2) health and dies of a new cause, **thirst**.
- Fresh water is the terrain's rivers and lakes (`riverStrength` or `lakeStrength` on the sample); the sea is salt. A thirsty animal walks rays out to its vision radius every two seconds and takes the first water each meets (the shore), picks the nearest fresh one, and asks the NavMesh for a spot at its edge it can stand or wade in (`SeekFood.TryFindDrinkingSpot`). Spots that can't be reached are remembered for 60 s, as unreachable food is.
- A **Drink** goal in `UtilityDecisionPolicy` competes with food and mates: worth `thirstValue` (1) × the animal's energy store when dry, nothing at the threshold, less travel, so a dying animal goes to water before food. Two states, seeking water and drinking; drinking refills at 25/s until 95%, and any animal standing in fresh water drinks for free. Newborns are born hydrated. Off-screen populations ignore thirst.
- Deaths by thirst are in the telemetry records, the season summaries, the telemetry log and `population.csv` (schema 3, a `thirst` column before `other`). The inspector shows water beside energy.
- Five-year soaks: FOREST-001 14 thirst deaths of 376 (starvation 305), average 36 animals; MOSSY-FJORD 42 of 873 (starvation 668), average 83. Thirst is a steady minor cause, 4 to 5% of deaths, a little higher in winter when animals range for buried food; populations are unchanged.
- Two Play Mode tests: a thirsty animal walks to a stand-in water point and drinks until nearly full (`SeekFood.DrinkingWaterLocator` supplies water where there is no terrain); with no water, an animal with no hydration dies of thirst and the death is counted. The first run caught a real bug: an animal stopped drinking the moment it crossed the thirst threshold, so a drink now keeps a quarter of its value until the animal is nearly full. Full Unity suite: **279 passed, 0 failed, 0 skipped** (`Logs/TestResults/20261101T165334883659Z/results.xml`).

## Backlog items 9, 12, 28, 29, 31, 32 and 51 — 1 November 2026, second round

- **9, founder start:** `AnimalTerrainDemoBootstrap.ChooseFounderStart` scores candidate centres within **Start Search Radius** (600 m) of the simulation focus by the walkable land and plant food within the spawn radius (the food spawner's own nutrition estimate on three rings of samples), with a small preference for staying near the focus, and places the founders there; the camera keeps its offset. FOREST-001's founders now start 594 m west, at about (−576, 144), on the western island's forest: a five-year soak averaged 37 animals (12 before) with 23% of births from two parents. MOSSY-FJORD's moved 215 m.
- **51, cold that costs:** `AnimalTemperature.extraEnergyDrainPerStressUnit` 0.5 → 1.5, so 10 °C beyond the comfort range costs 75% more energy and 20 °C beyond it 2.5×. The off-screen rules copy the same default. Winters still kill through hunger (no "cold" deaths, which need stress above 1), but thick fur is now selected: 14 to 17% of FOREST-001's animals by year 2, and 5% → 35% on MOSSY-FJORD between years 3 and 6. Torpor stays at 0.01.
- **12, mutation load:** `harmfulMutationEnergyCost` 0.02 → 0.05. Six-and-six five-year replicates on MOSSY-FJORD (`Logs/SoakRuns/comparison-20261101T155009Z`): load on 75 animals at the end (61–90), load off 81 (62–100); births from two parents 17% in both; sexual drive 0.50 against 0.48; 1.2 harmful mutations carried (0.5–1.7). Still no sign that sex pays. Clones also shed nothing, so with every birth equally likely to add a mutation the load builds slowly for everyone; a cost per mutation that bites would need to be far higher, or runs far longer.
- **29 and 32, inspector and follow:** `AnimalInspector` (added to any scene with a Species Manager): click an animal for a panel at the top right with its activity, age, growth, energy, health, temperature, every gene beside its current value, body parts, perks, harmful mutations and parents; F follows it with the camera until the camera is moved by hand or it dies; Escape closes.
- **28, species tree:** `SpeciesTreeView`, T to show: each species a bar from its origin to its extinction or now, in its colour under its parent, living members after its name, extinct species grey.
- **31, god tools:** `GodTools`, K to show: a drought (regrowth at 20% for 200 s, `FoodSpawner.growthMultiplier`), 30 plants dropped in front of the camera (`FoodSpawner.DropPlants`), a mass extinction of half the animals (`SeekFood.Kill`), mutation chances ×4 for 300 s (`SpeciesManager.mutationMultiplier`), and an end button. Each is recorded as an event with the Species Manager, and the recorder saves them in `events.csv`. For experiments they can be scheduled: `run_soak.py --events "drought@800:200x0.2,extinction@1200x0.5"` (`GodTools.TryParse`). A one-year check run fired all four on time.
- The panel's key hint lists T, K, click and F. Tests: `GodToolsTests` covers the schedule parser; the off-screen tests expect the new cold cost. Full Unity suite: **277 passed, 0 failed, 0 skipped** (`Logs/TestResults/20261101T160428677684Z/results.xml`). This round went to `main` on 1 November as six commits (`4bff837` to `e4ed518`).

- Two Play Mode tests cover growth and litters. The soak harness logs founder positions and can set the harmful mutation chance on every animal. Full Unity suite: **268 passed, 0 failed, 0 skipped** (`Logs/TestResults/20261101T151541325480Z/results.xml`), including `SpeciesSplitCalibrationTests` with the new gene. All of it, with the 29 October follow-up, went to `main` on 1 November as six commits (`408fc55` to `7b3b1a5`). Nothing has been checked by eye in the Editor.

## Backlog items 54 and 55, generations and stale numbers — 10 November 2026

- **54, generations recorded:** `population.csv` and `species.csv` end with `average_generation` and `highest_generation` of the live animals at each sample, and `traits.csv` with `at_minimum` and `at_maximum`, the animals sitting exactly at a gene's limit (recording schema 7, described in `RUN_RECORDING.md`). The panel's largest-species line shows the species' generation, and `Tools/summarise_runs.py` prints a `gen` column.
- **55, stale numbers:** the maturity claim in the 1 November section above is corrected in place, and the `AnimalGenome` comments give the gene weights as 23.
- First soaks with the new columns, one five-year run per seed at 50× with seasons, day and night, thirst and own-kind hunting on, off-screen mode off, 3 founders, revision `7e7f04b5+dirty` (items 54 and 55 uncommitted):

  | Seed | Final animals | Average while running | Births | From two parents | Average generation at the end | Newest generation |
  |---|---:|---:|---:|---:|---:|---:|
  | MOSSY-FJORD | 114 | 86 | 911 | 8% | 14.9 | 18 |
  | FOREST-001 | 30 | 33 | 359 | 13% | 17.5 | 23 |

  The average generation rose steadily, about 3 a year on MOSSY-FJORD (2.1, 5.9, 9.3, 12.0, 14.9 at the end of each year) and about 3.5 on FOREST-001 (3.5, 7.7, 10.3, 13.7, 17.5). That confirms the earlier rough count of 10 to 20 generations in five years, against the 50 to 60 the species threshold is tuned for; neither run split a species. At this pace a first split would need roughly 15 to 20 simulated years.
- Genes at a limit at the end of both runs: **Torpor** (110 of 114 animals at its minimum on MOSSY-FJORD, 7 of 30 on FOREST-001) and **LitterSize** (98 of 114, and 29 of 30). Founders start both at the minimum, so mutation can only push them up and their averages cannot show selection against them; read "torpor stays at 0.01" and "litter size drifted only to 1.01–1.06" above with that in mind. No other gene had any animal at a limit.
- One run per seed, so these are single observations, not a spread. Recordings: `Logs/SoakRuns/MOSSY-FJORD-20261110T091119Z` and `Logs/SoakRuns/FOREST-001-20261110T091230Z`.
- Full Unity suite with item 54 in: **321 passed, 1 failed** of 322 (`Logs/TestResults/20261110T085806513443Z/results.xml`); see `TEST_RESULTS.md` for the one failure, which passed twice when run alone.

## Backlog item 57, one record per animal — 13 November 2026

- Each recording has a new `lives.csv` (schema 8, described in `RUN_RECORDING.md`): one row per animal with its ID, both parents' IDs, species, generation, origin, birth and end time, age, cause of death, where it joined and ended, how many young it had, and the genome it was born with (harmful mutations, perks, every gene and body part). A row is written as the animal dies; the animals still alive are added when the recording closes, so every animal has exactly one row.
- The Species Manager gives each animal an ID as it joins (counting from 1 in each world) and raises `AnimalDied` and `AnimalLeft`; `SeekFood` keeps its parents' IDs, its birth time and place and a count of its young. Nothing about how animals behave changed.
- `Tools/summarise_lives.py <run folder>` checks the rows against the last row of `population.csv` (births, deaths, animals alive, no duplicate IDs, no parent without a row) and prints how unevenly young are shared out.
- First soaks, one five-year run per seed with the usual settings, revision `e8a94c77+dirty` (item 57 uncommitted). Both add up to `population.csv`.

  | Seed | Rows | Dead | Alive | Lifetime young of the dead: mean | Variance | Most | Left no young | Young from the top tenth | Left young that bred | Average generation |
  |---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
  | MOSSY-FJORD | 853 | 735 | 118 | 1.16 | 1.50 | 4 | 42% | 29% | 42% | 15.7 |
  | FOREST-001 | 404 | 371 | 33 | 1.18 | 1.40 | 4 | 40% | 28% | 44% | 18.2 |

  Young are shared out almost as evenly as chance alone would: the variance in lifetime young is close to the mean, no animal left more than 4, and the rough effective size is 0.90 and 0.99 of the head count. Wild populations average about a tenth, because a few animals have most of the young. Here nearly every animal that breeds has one to three, so there is little difference between animals for selection to work on. That fits the genes not moving (items 9 and 12) and is worth keeping in mind for item 58. The ratio is N × mean² ÷ variance for one generation of a mostly cloning population; generations overlap here, so it is a rough guide.
- Deaths: starvation 511, old age 155, thirst 69 on MOSSY-FJORD; starvation 272, old age 91, thirst 8 on FOREST-001. Median age at death about 200 s, half a simulated year.
- One run per seed, single observations. Recordings: `Logs/SoakRuns/MOSSY-FJORD-20261113T065528Z` and `Logs/SoakRuns/FOREST-001-20261113T065633Z`.
- Tests: an Edit Mode test for the row format and a Play Mode test that a clone's row names its parent, a killed animal's row has its cause, the survivor's row is `alive`, and the rows match the manager's births and deaths. Full Unity suite: **322 passed, 2 failed** of 324 (`Logs/TestResults/20261113T064931125928Z/results.xml`). One failure was the new Play Mode test expecting the wrong origin for the test helper's animals; corrected, and it and the nine recorder tests then passed. The other, `NightEyesFindFoodOnAMoonlessNight`, failed once more in three runs alone; see `TEST_RESULTS.md`.

## Backlog item 60, clones against maters over 25 years — 13 November 2026

- **The lock:** `SeekFood.lockedSexualDrive` (below 0, the default, leaves the gene in charge) holds an animal's and all its descendants' sexual drive at a value, overwriting the inherited gene so the recordings agree with the behaviour. `Tools/run_soak.py --sexual-drive 0|1|…` applies it to every live animal from `--sexual-drive-at` seconds on (800, two years, by default) and logs it in `events.csv`. `Tools/run_comparison.py --treatments "clones=30:0,maters=30:1"` takes `label=harmful chance:drive`. `Tools/summarise_load.py <folder>` follows the lowest and the average harmful-mutation count through each run. A Play Mode test checks the lock holds in a child whose every gene mutates.
- **Why the lock starts at year 2:** pilots showed three founders that can only mate die out within about two years; they breed too rarely to grow.
- **The experiment** (`Logs/SoakRuns/item60-20261113`): MOSSY-FJORD, 25 simulated years at 50×, harmful mutation chance 30% per birth at the usual 5% energy each, the normal gene for two years and then the lock, three replicates per treatment, alternating. Revision `3871ddfb+dirty` (the lock uncommitted).

  | Treatment | Runs | Alive at 25 years | Animals on average | Generations | Lowest count at the end | Average carried | Lowest count stepped up / down |
  |---|---:|---:|---:|---:|---:|---:|---:|
  | Clones only (drive 0) | 3 | 3 of 3 | 102, 117, 130 | 72, 86, 87 | 14, 15, 19 | 18.0, 18.3, 22.0 | 13/0, 13/0, 14/0 |
  | Mate first, clone if no partner (drive 0.84) | 3 | 3 of 3 | 68, 78, 89 | 69, 78, 82 | 4, 8, 9 | 11.0, 12.5, 16.4 | 12/6, 17/8, 23/14 |
  | Maters only (drive 1) | 3 | 0 of 3 | 22, 25, 32 | 31, 32, 49 at the end | — | — | died out at years 8.4, 8.9 and 13.1 |

- **Muller's ratchet shows.** In all three clone runs the lowest harmful-mutation count only ever stepped up, 13 to 14 times, to 14 to 19 at 25 years; the first step came at year 2.6 to 4.7 (generation 8 to 14). In the mate-first runs it stepped down 6 to 14 times as well as up and ended at 4 to 9, with a third less load carried on average (11 to 16 against 18 to 22). 15 to 18% of the mate-first births had two parents, and that was enough to shed mutations. The two treatments had parted clearly by year 10, about generation 30.
- **But shedding the load did not pay in animals.** The mate-first populations were about a third smaller than the clones' throughout (68 to 89 against 102 to 130 on average), even at year 25 when the clones carried 18 to 22 mutations. What mating costs here, in search time and courtship, outweighs what the load costs.
- **Mating-only populations cannot sustain themselves.** All three died out, and a control with no harmful mutations at all (`maters-noload-r1`, five years) fell from 85 animals at the lock to 7, so the cause is the mating itself, not the load: once every birth needs a partner, they breed too rarely to replace their dead. Item 18 (sexes and mate choice) and any "does sex pay" question need mating made cheaper first, or drive 1 will always lose.
- **Open question for item 61 (energy ledger):** clones carrying about 20 harmful mutations should use roughly twice the energy (5% each), yet their numbers did not fall over the 25 years (95 to 100 animals at year 5, 95 to 130 at year 25). Either the load's cost is being offset somewhere or it bites less than intended. Not investigated.
- **First species splits ever recorded.** Six of the nine runs split: the first new species appeared at years 10.8 to 22.1, at an average generation of 35 to 60, in line with the 50 to 60 the threshold is tuned for. Twelve daughter species appeared in all; most died out within one to six years, and three were alive at the end (4 to 13 animals).
- Along the way, perks spread far beyond what the 1% flip would give alone in some runs (thick fur 82 to 98%, camouflage 76% in one clone run, venom 95% and regeneration 80% in one mate-first run). In clones a perk rides along with whichever line takes over, so this is not evidence of selection; compare with `Perk_Marker` before reading anything into it.
- Life records: every run's `lives.csv` adds up to `population.csv` (7,396 rows in `clones-r1`). Young are shared less evenly over 25 years than over 5: in `clones-r1` half the animals left none and the rough effective size was 0.63 of the head count.
- Three replicates per treatment and one seed, so the direction is clear but the sizes are rough.

## Backlog item 61, energy ledger check — 13 November 2026

- **The ledger:** `EnergyLedger` (new, in `Assets/Animals/Scripts`) is a test mode that adds up every change to the animals' stored energy by kind: what they started with, plants, meat, energy handed to young and what the young were born with, anything a newborn couldn't store, upkeep, growth, attacks, and the store an animal died or left with. It is off unless `EnergyLedger.Begin` is called, and it only counts; it changes nothing in the simulation. Every change in `SeekFood` now goes through one `SetEnergy` call, and at the start of each frame an animal whose store differs from what the ledger last saw is logged as an untracked change.
- **Three rules are checked where they happen:** a litter never gets more than its parents handed over, a meal never gives more than the food held, and a carcass never holds more than the body's own meat and the store the animal died with.
- **Soaks with the ledger on** (`run_soak.py --ledger 1`, three years per seed; the status lines carry the running totals): no gap and no violations on either seed.

  | Seed | Plants | Meat | Upkeep | Growth | Given to young = born with | Died or left with | Attacks | Gap | Violations |
  |---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
  | MOSSY-FJORD | +100,210 | +5,780 | −80,172 | −10,923 | 32,851 | −7,310 | −14 | 0 | 0 |
  | FOREST-001 | +46,574 | +2,563 | −38,591 | −4,994 | 15,194 | −2,474 | 0 | 0 | 0 |

  So in these runs nothing makes energy from nothing: plants are 95% of what comes in, upkeep takes 76 to 79% of it, growth about 10%, and 5 to 7% is lost with the dead. Swimming and wading cost through upkeep, and drinking moves no energy.
- **Mutation load is paid in full.** A Play Mode test shows ten harmful mutations at 5% raise upkeep by exactly half. That answers the question left by item 60: the clones' numbers held up although they carried about 20, but not because the cost went unpaid. In those runs starvation deaths per year roughly doubled as the load grew (171 to 331 in `clones-r1`) while births rose with them (227 to 370) and old-age deaths fell: the animals lived shorter lives and turned over faster. Why the head count doesn't fall is still not explained.
- **Two rules that can hand back more energy than went in.** Neither showed in the soaks, where animals stay near the founders' size and most carcasses spoil uneaten (meat brought in half of what growth cost), but both are there for selection to find, more so now that animals hunt their own kind (item 66):
  1. A carcass holds (body meat + store) × 0.75 × body mass, so the store the animal died with is multiplied by its mass. For a founder-sized body (mass 1) that is a 25% loss, as meant. For the largest body the genes allow (bulk 1.8, height 2.2, mass about 4.6) it is 3.45 times the store: an animal that dies holding 100 leaves 345. The ledger's carcass check flags it whenever the store is above about 24 on such a body.
  2. A body's meat is worth more than it cost to grow. Growing to full size costs Growth Energy Cost (0.5) × 50 × mass = 25 × mass, less the newborn's share; the carcass gives 50 × 0.75 × mass = 37.5 × mass, and a full meat-eater digests all of it. Founders' bodies were never paid for at all.
  Nothing was changed: both are balance decisions. Suggested: scale only the body's meat by mass and take the store at its own value times the transfer efficiency, and bring the meat a body yields down to what it cost to grow (or the growth cost up).
- Tests: `TheEnergyLedgerBalancesThroughEatingALitterGrowthAndADeath` (no gap and no violations through a meal, a litter of three, growth and a death, and a change made behind the ledger's back is caught) and `HarmfulMutationsRaiseUpkeepByTheirFullCost`.

## Item 61 follow-up, both carcass rules fixed — 13 November 2026

- **The new rule:** a carcass holds (what was paid to build the body + the store the animal died with) × the transfer efficiency (0.75). Nothing is scaled by body mass any more, so a big body no longer multiplies the store, and a body's meat is never worth more than it cost. `SeekFood.BodyEnergyPaid` adds up what an animal actually pays as it grows; an animal too hungry to pay in full builds a leaner body. A founder or an arrival from off-screen is placed with a body worth what one its size costs to grow (Growth Energy Cost × Carcass Body Energy × mass). Hunters weigh prey by the same figure. The cost of growing is unchanged.
- **What it means in numbers:** a full-grown founder-sized animal that starves used to leave 37.5 of meat and now leaves about 17.6 (it paid 23.4 to grow from newborn size); a newborn that dies at once leaves only three quarters of its store. The largest body the genes allow, dying with a store of 100, left 518 and now leaves 161.
- **Also fixed:** a dying animal could eat its own carcass if physics ran before it was removed. It showed only in a test, never in the soaks (their ledgers had no gap), and is now ruled out.
- **Soaks with the ledger on, three years per seed, before and after** (one run each, so the head counts are single observations):

  | Seed | Rule | Meat in | Growth paid | Meat as a share of growth | Meat as a share of all intake | Animals on average | Gap | Violations |
  |---|---|---:|---:|---:|---:|---:|---:|---:|
  | MOSSY-FJORD | old | 5,780 | 10,923 | 53% | 5.5% | 74 | 0 | 0 |
  | MOSSY-FJORD | new | 2,308 | 7,992 | 29% | 3.0% | 52 | 0 | 0 |
  | FOREST-001 | old | 2,563 | 4,994 | 51% | 5.2% | 34 | 0 | 0 |
  | FOREST-001 | new | 1,583 | 5,560 | 28% | 3.1% | 36 | 0 | 0 |

  Meat now brings in a little over half of what it did, as intended. Whether the lower head count on MOSSY-FJORD is the rule or chance needs replicates: three-year averages vary a good deal between runs, and FOREST-001 did not move.
- **What this changes for earlier results:** runs before this commit had richer carcasses, so their numbers on meat-eating, scavenging and hunting their own kind (items 13 and 66) are not directly comparable with later ones. Meat-eating was already hard to evolve and is now harder.
- Still unpaid: the three founders' bodies, a one-off at the start of each world.
- Tests: `ACarcassHoldsNoMoreThanTheBodyCostAndTheStore` (the largest body with a large store) and two more checks in the newborn growth test. Full Unity suite: **328 passed, 0 failed** (`Logs/TestResults/20261113T082017918454Z/results.xml`).

## Backlog item 62, perks easier to lose than to gain — 13 November 2026

- **The rule:** a child gains each perk its parent lacked with **Perk Gain Chance** (0.2% per birth) and loses each perk its parent had with **Perk Loss Chance** (2%), both times the mutation rate gene and the god tools' mutation boost. Until now both ways were the same 1% (**Perk Flip Chance**, now gone). The marker perk follows the same rule, so it stays the yardstick: under chance alone it reaches about 2% of animals after 10 generations, 4% after 30, and levels out near 9%. `AnimalGenome.MutatePerks(gain, loss)` does it; `CreateChild` takes both chances.
- **Calibration (item 14):** the neutral-drift test's median first split moved from about 51.5 generations to 66.5 (runs 53 to 89). Perks now reach only a few per cent of the animals under drift, so they add much less to the distance between two animals. The test still passes (band 40 to 75), so the threshold stays at 0.05, which the Terrain scene also sets; a lower threshold would bring the first split back towards 55. The panel's hint now says about 65 generations.
- **Tools:** `run_soak.py --perk-gain X --perk-loss Y` sets the chances on every animal; `run_comparison.py` treatments may now be any `run_soak.py` arguments (`"old=--perk-gain 1 --perk-loss 1,new=--perk-gain 0.2 --perk-loss 2"`); `summarise_runs.py --traits Perk_ThickFur,…` adds the largest species' final share of each trait.
- **The comparison** (`Logs/SoakRuns/item62-20261113`): MOSSY-FJORD, 10 years (about 31 generations), four runs with the old 1% both ways and four with 0.2% and 2%, alternating, all else as usual. Largest species' final share of each perk:

  | Treatment | Thick fur | Venom | Camouflage | Regeneration | Scavenger gut | Marker (no effect) | Animals on average |
  |---|---|---|---|---|---|---|---:|
  | 1% both ways | 92, 39, 8, 46% | 99, 16, 9, 0% | 3, 20, 13, 1% | 13, 24, 3, 24% | 1, 12, 14, 0% | 51, 15, 74, 66% | 97 |
  | 0.2% gain, 2% loss | 0, 1, 13, 0% | 2, 0, 0, 4% | 0, 8, 0, 75% | 0, 0, 0, 0% | 0, 0, 0, 0% | 0, 0, 1, 0% | 97 |

- **Under the old rule, chance alone filled the perks.** The marker, which does nothing, reached 15 to 74% in ten years, against the 23% the average would predict, because in a population that mostly clones (91% of births) a whole family line takes over and carries whatever perks it has. Every real perk's share sat in that same range. So the reading in the 1 November second-round notes above, that thick fur "is now selected" (5% to 35% on MOSSY-FJORD), does not hold: the marker does as well.
- **Under the new rule, costly perks stay rare,** as item 62 hoped: venom 0 to 4%, regeneration and the scavenger gut 0%, the marker at most 1%.
- **But thick fur did not rise over the cold winters** (0 to 13%; there were no cold deaths in any run). Selection for it, if any, is too weak to lift a perk that appears about four times in a run and is lost by 2% of births.
- **And a costly perk can still sweep by riding on a family line.** In one new-rule run camouflage reached 75% with no animal killed by a hunter all run, so it protected against nothing and cost 5% energy. All but 7 of its 564 carriers descend from one gain, in an animal born in year 2.5; that line carried 0.21 harmful mutations on average against 1.94 in the rest, about 9% less upkeep, and left 1.12 young per animal against 1.05. The line won on its low mutation load and took camouflage with it. With only 7 to 9% of births from two parents, a perk stays tied to the line it arose in. Telling such hitchhiking from selection needs the per-line view (item 65) or the per-gene selection measure (item 58); the marker alone can't, since a sweep carries the marker only if the line happens to have it.
- Population and births did not differ between the rules (97 animals on average in both; 2,104 to 2,447 births per run).
- Tests: `PerksAreGainedAndLostAtTheirOwnChances` (new), and the callers of `CreateChild` updated. Full Unity suite: **329 passed, 0 failed** (`Logs/TestResults/20261113T133026425931Z/results.xml`).

## Backlog item 58, selection on each gene — 13 November 2026

- **The tool:** `Tools/selection_gradients.py <runs or comparison folder>` reads each run's `lives.csv` (item 57) and measures, for every trait (harmful mutations, every perk, every gene, and the total size of each kind of body part), how strongly it predicts the number of young an animal left. Animals born in the simulation that have died are grouped into cohorts by the year and season of their birth; within each cohort fitness is made relative and each trait standardised. It prints the selection differential S (total selection, including what a trait picks up from traits it travels with) and the Lande–Arnold gradient β (direct selection, from a multiple regression on all traits at once), with a 95% range, overall and for each season. Runs with the same experiment label are pooled. Pure Python, no packages; a 25-year, three-run pool takes about 5 s.
- **Honest ranges:** relatives are alike, so ordinary regression ranges are too narrow. The standard errors are clustered by first parent (brothers and sisters), and the do-nothing marker genes and marker perk set the floor: "clearly selected" lists only traits whose range excludes zero and whose |β| is above every marker's. `--place` holds the birthplace equal (a smooth surface in the birth position within each cohort), because family lines live in different places and a line in a rich patch leaves more young whatever its genes. `--fitness bred` counts only young that went on to breed.
- **Checked on made-up records with a known answer** (12,000 animals): a gene with direct selection of +0.3 came out at +0.29; a gene that only travelled with it (r = 0.7) had S +0.20 but β −0.01; a gene pushed up in winter and down in summer came out at +0.28 and −0.30 by season; a planted cost of 5% per harmful mutation came out at 5.4%; a perk planted on low-load animals had S +0.02 and β −0.01.
- **On the recorded runs** (birthplace held equal; pooled over replicates, since single five-year runs gave different "selected" lists each time):

  | Data | Animals | Marker floor | Thick fur (fall, winter) | Scavenger gut | Body height | Neck | Cost of one harmful mutation in young |
  |---|---:|---:|---|---:|---:|---:|---:|
  | Item 62, old perk rule, 4 × 10 years | 8,798 | 0.049 | **+0.061** (+0.15, +0.10) | **−0.086** | +0.047 | **+0.060** | 1.5% (−1 to 4) |
  | Item 62, new perk rule, 4 × 10 years | 8,618 | 0.062 | too rare to measure | −0.048 | +0.030 | +0.026 | 5.2% (3 to 8) |
  | Item 60, clones, 3 × 25 years | 22,383 | 0.019 | **+0.053** (+0.13, +0.09) | **−0.054** | **+0.047** | **+0.055** | 1.0% (0.1 to 1.9) |
  | Item 60, mate first, 3 × 25 years | 13,246 | 0.010 | **+0.039** (+0.08, +0.03) | **−0.050** | **+0.026** | +0.006 | 1.9% (1.1 to 2.7) |

  Bold: clearly selected (range excludes zero and |β| above every marker).
- **What it shows:**
  1. **Selection is weak.** Almost every |β| is below 0.07, against a typical 0.15 in field studies, and in a pool of four ten-year runs chance alone gives the markers up to 0.05 to 0.07. Single short runs can't tell selection from chance; pools of several runs, or long runs, can.
  2. **Thick fur is really favoured in the cold.** β is near zero in spring and summer and +0.08 to +0.16 in fall and winter in every large pool, and stronger counted as young that bred (+0.10, fall +0.18). Its share stayed within what chance gives (item 62) because this pressure is too weak to beat drift in ten years, not because there is none. This corrects the item 62 note above, which read thick fur's share alone.
  3. **The scavenger gut is selected against** in every pool and season (β −0.05 to −0.09), as its 30% plant penalty predicts.
  4. **Tall bodies are favoured** (body height +0.03 to +0.05, and the neck +0.06 in two pools): reaching the tall forest food (item 8) pays.
  5. **A harmful mutation costs only 1 to 2% of lifetime young** in the long runs, and about 5% in the new-rule ten-year runs. That is part of the answer to item 61's question: in the clone runs, carrying 18 to 22 of them cost each animal about a fifth of its young compared with an animal carrying none, but the differences between animals alive at the same time were a few mutations, worth a few per cent.
  6. **The camouflage sweep (item 62) was mostly place.** In that run camouflage's β was +0.12 with the birthplace free and +0.07 (range −0.05 to +0.18, within chance) with it held equal; its line lived about 270 m west of the rest. Most of camouflage's apparent advantage came from where its family line lived.
- Not read: `SexualDrive` in the item 60 pools (β −0.31 in clones, +0.27 in mate-first) comes from the lock switching animals' behaviour at year 2 (item 60), not from selection on the gene.
- Limits: the gradient is linear; it can't see selection for a middle value. Body parts are counted by total size of each kind. Animals alive at the end are left out, so the last year or so of each run adds little.

## Backlog item 68, ageing instead of a hard lifespan — 13 November 2026

- **The rule:** a new inherited gene, **Ageing Onset** (10 to 1,200 simulated seconds; founders 180 s, against their 300 s lifespan), sets when an animal starts to age. From then its vigour falls from 1 to 0 over **Ageing Span** (120 s, not inherited). Vigour caps health at that share of the maximum, slows the animal towards **Aged Speed Floor** (30% of its speed), and makes it take each chance to breed with that probability, resting as if it had bred otherwise. At vigour 0 it dies of old age. **Maximum Lifespan** stays an outer limit; at the founders' values both end at 300 s. A later onset costs upkeep, as repair does: **Repair Metabolic Weight** (0.2) charges the onset against the founders' like the other traits, so doubling it costs about 10% more energy. **Enable Ageing** off gives the old rule, with no decline and no cost. The inspector shows "ageing, vigour 62%" and the gene; the soak runner takes `--ageing 0`.
- The gene is last in the genome, so genomes saved before it keep their genes in place. `traits.csv` and `lives.csv` gain an `AgeingOnset` column (recording schema 9). Off-screen populations don't age.
- **Calibration (item 14):** one more gene moved the median first split from 66.5 generations to 71 (runs 40 to 86), near the top of the test's 40 to 75 band; the test passes. A sweep in the test copy gives 54.5 at a threshold of 0.045 and 44.5 at 0.04. Not changed, because the Terrain scene sets its own 0.05 and the Editor is open on it: lower both to 0.045 before the next gene.
- **The comparison** (`Logs/SoakRuns/item68-20261113`): MOSSY-FJORD, 10 years, four runs with ageing and four without, alternating.

  | | Ageing | No ageing |
  |---|---|---|
  | Animals on average | 81 (77 to 87) | 90 (84 to 94) |
  | Births per run | 1,855 (1,714 to 2,025) | 2,094 (1,941 to 2,164) |
  | Average generation after 10 years | 34.6 | 30.9 |
  | Of animals that reached 180 s, died of something else before 290 s | 78% | 59% |
  | Median age at death | 191 s | 166 s |
  | Ageing Onset at the end (largest species) | 178 to 194 s | 181 to 191 s (does nothing here) |
  | Selection gradient on Ageing Onset | +0.000 (−0.03 to +0.03) | +0.004 |

- **What it shows:** ageing works as built. Old animals weaken and slow, so of those that reached the onset, 78% starved or died of thirst before the end against 59% without it. Fewer old animals breed, so births fell 11% and the population 10%, and generations turned over about 12% faster. Median age at death rose, probably because with fewer animals fewer young starve (not checked).
- **But the onset is not selected.** Its gradient is zero within ±0.03, and it ended where it started. Only about half the animals live to 180 s, and by then most of their young are already born, so a later onset adds little and costs upkeep all life long. That is the classic reason ageing evolves at all: selection weakens with age once few animals live that long. Don't expect the onset to move without much longer runs, or a world where more animals reach old age.
- Tests: `AgeingAnimalsWeakenSlowDownAndDieBeforeTheirLifespan` (vigour, the health cap, the slowdown, the upkeep cost of a later onset, death when vigour runs out, and no ageing when it is off). The test animals elsewhere get an onset of 1,200 s, so nothing else ages. Full Unity suite: **330 passed, 0 failed** (`Logs/TestResults/20261113T151331352848Z/results.xml`).

