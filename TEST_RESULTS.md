# Latest verification — 1 November 2026, second round

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
