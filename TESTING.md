# Running the Unity tests

From the repository root on macOS:

```sh
python3 Tools/run_unity_tests.py
```

The runner finds the installed Unity version specified by `ProjectSettings/ProjectVersion.txt`, copies Assets, Packages and ProjectSettings to a temporary directory, and runs the Edit Mode suite without touching the open project's scene. Graphics stay enabled because the ocean tests execute compute shaders. It uses the existing local licensing channel recorded by the open editor when one is available; it does not copy account tokens.

For subsequent runs, reuse the dedicated test directory printed by the first run:

```sh
python3 Tools/run_unity_tests.py --project-copy /path/to/dedicated/test-copy
python3 Tools/run_unity_tests.py --project-copy /path/to/dedicated/test-copy --filter Boreal.Animals.Tests.RunRecorderLifecycleTests
```

Reused directories must contain the runner's `.boreal-test-copy` marker. Its Assets, Packages and ProjectSettings folders are mirrors of the source and stale files there are deleted. Do not use a working checkout as the test copy. Tests save XML results and the Unity log under `Logs/TestResults/<UTC timestamp>/` in this repository. These outputs are ignored by Git. A failed test or failure to produce results returns a nonzero exit status.

Use `--editor /path/to/Unity` to override the installed editor path. `--license-channel` can override automatic local channel discovery. Keep Unity Hub/the licensed editor running if batch startup cannot initialize licensing. No computer-use or UI automation is required.

## What the tests cover

The Edit Mode suite includes regular unit tests and Unity coroutine tests that enter Play Mode to exercise NavMesh food seeking, reproduction, venom, regeneration, scavenging, body rendering, seasonal state publication, and CSV recording lifecycle. The recording tests verify pause/resume, world regeneration, disable/re-enable, file closure and the quit callback. The quit callback test calls the callback explicitly; it does not terminate the test editor to simulate a process crash.

The suite does not establish ecological balance on generated terrain or prove that a particular trait is favored by evolution. That still requires recorded runs across seeds and treatments.

## Soak runs

`Tools/run_soak.py` plays the full Terrain scene in batch mode on one seed, with the run recorder on, for a number of simulated years. It uses the same kind of dedicated test copy:

```sh
python3 Tools/run_soak.py --project-copy /path/to/dedicated/test-copy --seed FOREST-001 --years 5
python3 Tools/run_soak.py --project-copy /path/to/dedicated/test-copy --seed FOREST-001 --years 5 --seasons 0 --maps 0
```

It copies `Tools/soak` into the test copy only, so the project never compiles the harness. The run starts at 1× and switches to `--speed` (50 by default) once the founders exist, with the fast-forward safety pause off: world generation runs below 10 FPS in batch mode, which would otherwise pause it for good. The step per frame stays capped at high speed, so a slow frame rate only lowers the speed reached. Output goes to `Logs/SoakRuns/<seed>-<timestamp>/`: the Unity log with one `Season summary` line per season, the recorder's CSV files under `recording/`, a camera shot at each season's peak (the main camera, and one looking down on the largest group of animals), and top-down maps of the whole world unless `--maps 0`.

In batch mode the terrain ground draws black (dark grey where the atmosphere covers it in the camera shots), while trees, water and animals look right, so judge the ground's look in the Editor, not from these images.
