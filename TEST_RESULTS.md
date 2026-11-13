# Latest verification — 13 November 2026, with perks easier to lose than to gain (item 62)

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
