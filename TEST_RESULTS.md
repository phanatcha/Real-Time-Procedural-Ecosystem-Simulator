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
