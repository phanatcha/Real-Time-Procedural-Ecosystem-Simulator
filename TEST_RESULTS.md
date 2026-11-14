# Latest verification — 14 November 2026, with the replay without selection (item 59)

**357 passed, 0 failed, 0 skipped** in Unity 6000.4.1f1 ([NUnit XML](Logs/TestResults/20261114T043451407722Z/results.xml)), on a working tree that also held item 67 (not committed). New: `NeutralReplayTests` (three Edit Mode tests, one of them run for both replay modes). `python3 Tools/neutral_replay.py --self-test` passes seven checks on the same kind of made-up records outside Unity. A first full run, before the family-tree mode, had 356 of 356.

# 14 November 2026, with family lines (item 65)

**353 passed, 0 failed, 0 skipped** in Unity 6000.4.1f1 ([NUnit XML](Logs/TestResults/20261113T193928177706Z/results.xml)), on a working tree that also held item 67 (not committed). New: five Edit Mode tests in `FamilyLinesTests` and `FamilyLinesPlayModeTests.TheViewRecordsBirthsAndDeathsAndBuildsAFounderLineEach`; `Tools/muller_plot.py --self-test` passes on the same made-up family. The first full run had 352 of 353: the new Play Mode test redrew the chart before a simulated second had passed (the founder clones within its first frames) and the view skipped redraws under 1 s; the view now draws from any time above 0, the test waits 2 s, and the six family line tests then passed on their own ([NUnit XML](Logs/TestResults/20261113T193855304370Z/results.xml)) before this full run.

# 14 November 2026, with natural events and wildfire (items 78 and 79)

**347 passed, 0 failed, 0 skipped** in Unity 6000.4.1f1 ([NUnit XML](Logs/TestResults/20261113T190355529596Z/results.xml)), with the final wildfire settings and the fire visuals (the fire map hooks in the Terrain, Vegetation and GrassField shaders, and the flame, spark and smoke particles); also 347 of 347 before the visuals ([NUnit XML](Logs/TestResults/20261113T182232492054Z/results.xml)). The visuals soaks' logs had no shader errors. Both runs were on a working tree that also held item 67 (body size scaling, not committed at the time), whose `BiggerBodiesStoreMoreAndLastLongerBetweenMeals` is one of the 347. New: 13 Edit Mode tests in `WildfireTests` (fire spreads through fuel and burns out; a river and bare rock stop it; snow and wet ground don't carry it; fuel follows trees, grass and rock; fire runs further downwind and uphill; the same seed burns the same cells; heat is felt in and beside the flames and smoke drifts downwind; putting a fire out; dryness peaks in late summer and stays up into fall; fire danger; god tool parsing; compass points) and 3 Play Mode tests in `WildfirePlayModeTests` (an animal near a fire runs from it; a trapped animal dies of fire, counted by the Species Manager and against its fire; a fire burns out, burns the plants inside it and not outside, and its start and end are recorded).

The first full run, before the tuning, had 345 of 347: `SeasonTests.ASeasonSummaryReportsWhatHappenedDuringTheSeason` expected the season line without the new `fire 0`, and `AnAnimalCaughtInTheFlamesDiesOfFire` lost its death callback across the domain reload when entering Play Mode (the callback is now made after it, as the other Play Mode tests do). Both fixed; the 13 Edit Mode tests were also run outside Unity after each tuning change.

# 13 November 2026, with body size scaling (item 67)

**331 passed, 0 failed, 0 skipped** in Unity 6000.4.1f1 ([NUnit XML](Logs/TestResults/20261113T164654069141Z/results.xml)). New: `BiggerBodiesStoreMoreAndLastLongerBetweenMeals`. The calibration test is unaffected (item 67 adds no gene).

# 13 November 2026, with ageing (item 68)

**330 passed, 0 failed, 0 skipped** in Unity 6000.4.1f1 ([NUnit XML](Logs/TestResults/20261113T151331352848Z/results.xml)). New: `AgeingAnimalsWeakenSlowDownAndDieBeforeTheirLifespan`. `SpeciesSplitCalibrationTests` passes with a median first split of 71 generations, near the top of its 40 to 75 band; a threshold of 0.045 would give 54.5.

# 13 November 2026, with perks easier to lose than to gain (item 62)

**329 passed, 0 failed, 0 skipped** in Unity 6000.4.1f1 ([NUnit XML](Logs/TestResults/20261113T133026425931Z/results.xml)). New: `PerksAreGainedAndLostAtTheirOwnChances`. `SpeciesSplitCalibrationTests` passes with a median first split of 66.5 generations (was about 51.5), inside its 40 to 75 band.

# 13 November 2026, with the carcass rules fixed (item 61)

**328 passed, 0 failed, 0 skipped** in Unity 6000.4.1f1 ([NUnit XML](Logs/TestResults/20261113T082017918454Z/results.xml)). New: `ACarcassHoldsNoMoreThanTheBodyCostAndTheStore`. Its first run failed because the dying animal ate its own carcass before it was removed, which the test loop allows and the game loop does not; a dying animal can no longer eat. The hunting and scavenging tests pass with the leaner carcasses.

# 13 November 2026, with the energy ledger (item 61)

**327 passed, 0 failed, 0 skipped** in Unity 6000.4.1f1 ([NUnit XML](Logs/TestResults/20261113T081125841990Z/results.xml)). Two new Play Mode tests: `TheEnergyLedgerBalancesThroughEatingALitterGrowthAndADeath` (also passed three times on its own) and `HarmfulMutationsRaiseUpkeepByTheirFullCost`. `MeatLeaningAnimalsHuntTheirOwnKind`, which failed once in the run before, passed here; it has not been changed.

# 13 November 2026, with the sexual-drive lock (item 60)

**324 passed, 1 failed, 0 skipped** of 325 in Unity 6000.4.1f1 ([NUnit XML](Logs/TestResults/20261113T074614389119Z/results.xml)). The new `ALockedSexualDriveHoldsInTheChildrenWhateverTheyInherit` passed, and three times on its own. The failure was `MeatLeaningAnimalsHuntTheirOwnKind` ("did not kill its own kind within 30 s"), which the lock does not touch (it is off by default). Run alone six times afterwards, it passed five times and one run reported no result, so it looks like a third test that depends on chance: the chase has to succeed inside 30 s. Not fixed.

# 13 November 2026, after the test fixes

**324 passed, 0 failed, 0 skipped** in Unity 6000.4.1f1 ([NUnit XML](Logs/TestResults/20261113T070910670535Z/results.xml)), with two tests that failed now and then made independent of chance. Each then passed eight times in a row on its own. Only test code changed.

- `NewbornsStartSmallAndGrowToFullSizeByMaturity` compares a clone with its parent, but children mutate (5% per gene, up to 10% of the value), so a mutation in vision, body size or a body part sometimes pushed a comparison past its tolerance. The test now turns mutation off for that parent.
- `NightEyesFindFoodOnAMoonlessNight` left the ordinary-eyed animal free to wander 20 m at a time, so it could walk within its 16.5 m of night sight of a plant 25 m away. It now stays where it is.

# Earlier on 13 November 2026

**322 passed, 2 failed, 0 skipped** of 324 in Unity 6000.4.1f1 with life records (item 57) ([NUnit XML](Logs/TestResults/20261113T064931125928Z/results.xml)). The suite has not been re-run in full since.

- `EveryAnimalLeavesOneLifeRecordThatNamesItsParent` (new) failed because it expected the origin `founder` for an animal the test helper places the way off-screen arrivals come back; the expectation was corrected to `offscreen`. It then passed, as did `RunRecordingTests` (5) and `RunRecorderLifecycleTests` (4) run on their own.
- `NightEyesFindFoodOnAMoonlessNight` failed with "the animal with ordinary eyes found food it should not see". Run alone three times it passed twice and failed once, so it fails now and then. The likely cause, not confirmed: the ordinary-eyed animal wanders up to 20 m and its plant is 25 m away, so it can come within its 16.5 m night sight. Item 57 does not touch seeing or wandering.

# 10 November 2026

**321 passed, 1 failed, 0 skipped** of 322 in Unity 6000.4.1f1 with generations recorded (item 54) ([NUnit XML](Logs/TestResults/20261110T085806513443Z/results.xml)). Two tests cover the item: `GeneLimitsCountAnimalsAtEitherEndOfAGenesRange`, and the recorder's live-animal test now checks the generation and gene-limit columns. The failure was `NewbornsStartSmallAndGrowToFullSizeByMaturity`: a child's vision read 110.55 against 110 ± 0.5. It passed twice when run alone afterwards, so it looks like an occasional failure, possibly a vision mutation in the child; the cause is not confirmed.

# 1 November 2026, third round

**279 passed, 0 failed, 0 skipped** in Unity 6000.4.1f1 after thirst (item 27) ([NUnit XML](Logs/TestResults/20261101T165334883659Z/results.xml)). Two new Play Mode tests: a thirsty animal walks to water and drinks until nearly full; an animal with no water dies of thirst. The first run caught animals stopping their drink at the thirst threshold, now fixed.

# Second round, 1 November 2026

**277 passed, 0 failed, 0 skipped** in Unity 6000.4.1f1 after the founder start, the cold cost, the 5% mutation cost, the inspector, the species tree and the god tools ([NUnit XML](Logs/TestResults/20261101T160428677684Z/results.xml)). New: `GodToolsTests` for the schedule parser; the off-screen population tests expect the new cold cost.

# Earlier on 1 November 2026

**268 passed, 0 failed, 0 skipped** in Unity 6000.4.1f1 after juvenile growth, the litter size gene, the panel chart and the heatmap's season input ([NUnit XML](Logs/TestResults/20261101T151541325480Z/results.xml)). Two new Play Mode tests: a newborn starts at the newborn size fraction, is full-grown at maturity and paid for growing; a litter of three shares the birth energy equally.

# Previous verification — 29 October 2026

**266 passed, 0 failed, 0 skipped** in Unity 6000.4.1f1 after the high-priority backlog follow-up.

- [NUnit XML](Logs/TestResults/20261029T125629673218Z/results.xml)
- [Unity test log](Logs/TestResults/20261029T125629673218Z/editor.log)
- Added vegetation hash distribution/full-seed/stream regressions and actual spruce placement coverage.
- Added continuous breeding-wait episode tests and a live-animal CSV cross-check at 1× and 50×.
- Added automatic-vs-explicit revision label coverage.
- Full Terrain smoke run with maps: corrected soak duration recorded 400.014 s for a 400 s request (41 strictly increasing CSV observations), with recording closed before map capture. Evidence: `Logs/SoakRuns/2026-10-29-priority-review/`.
- The captured tree distribution is scattered; the pre-existing black ground in batch rendering remains unresolved.

The previous verification below is retained as history.

# Unity test results — 28 October 2026

Final run: **257 passed, 0 failed, 0 skipped** in Unity 6000.4.1f1. Tests ran from the command line against a separate project copy with graphics enabled. The original open editor was not controlled or closed.

Run again on 29 October 2026, after the top-down map tool change and before committing to `main`: **257 passed, 0 failed, 0 skipped**, with the same counts per assembly ([NUnit XML](Logs/TestResults/20261029T111602377685Z/results.xml)).

| Test assembly | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Boreal.Animals.Tests.dll | 133 | 0 | 0 |
| Boreal.AnimalTerrainIntegration.Tests.dll | 20 | 0 | 0 |
| Boreal.EcosystemSimulation.Tests.dll | 30 | 0 | 0 |
| Boreal.TerrainGeneration.Tests.dll | 74 | 0 | 0 |

## Fixes and coverage

- Connected the batch editor to the existing local licensing service; no UI automation needed.
- Fixed unreachable-food test state being lost across a Play Mode domain reload by allocating its callback closure in a separate post-reload iterator. Added explicit Play Mode teardown.
- Moved season publication/lifecycle verification into a genuine Play Mode test, including toggling seasons and disabling the clock.
- Enabled graphics for real GPU ocean verification; the final run skips no GPU tests.
- Added recorder tests for pause/resume, world resets, disable/re-enable, exclusive file access after close, and quit-callback finalization.
- A new repeated-paused-reset test exposed a product bug. Added SpeciesManager.WorldRevision and used it to separate recordings and reset seasonal summaries even when simulated time is unchanged.

## Evidence

- [Final NUnit XML](Logs/TestResults/20261028T163050898960Z/results.xml)
- [Final Unity log](Logs/TestResults/20261028T163050898960Z/editor.log)
- [Repeatable command-line runner](Tools/run_unity_tests.py)
- [Testing instructions](TESTING.md)

The XML/log are local ignored outputs. Tests do not establish long-run ecological balance, visual correctness of the complete island, or process-crash durability. The quit test invokes Unity's callback rather than terminating the process.
