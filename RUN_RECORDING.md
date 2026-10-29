# Recording simulation runs

Add **Simulation Run Recorder** to the scene's **Species Manager** object using Add Component, before pressing Play. Recording is opt-in; the main scene does not acquire the component automatically. Set `Source Revision` to the commit plus a dirty-state label when applicable, and `Experiment` to the treatment/run label. The recorder deliberately says `unrecorded` rather than guessing a revision.

The Console prints the output directory: `Application.persistentDataPath/SimulationRuns/<UTC timestamp>-<unique ID>`. Each regenerated world opens a fresh directory. Disabling the component or leaving Play closes the files and captures the final state when possible. No run, including an extinction, is deleted. Abrupt termination may leave no `ended.txt`; the CSV files flush after every sample.

Files:

- `metadata.json`: schema version, UTC start, world seed, revision label, experiment, Unity version, interval, off-screen flag, and serialized starting settings for the manager, season clock, food spawner and one representative animal. If no representative exists yet, its settings are empty. This is an audit snapshot, not a replay/save format; Unity object references do not contain referenced asset contents.
- `population.csv`: cumulative births split into cloned/sexual, deaths by cause, live/off-screen counts, living species, population-ceiling flags, requested/achieved speed, season, effective strength and off-screen flag.
- `species.csv`: persistent species IDs/names, parent, origin/extinction, current counts and cumulative birth/death counters. Extinct species continue to appear.
- `traits.csv`: each inherited gene, harmful mutation count and perk indicator, with live sample count, mean, population standard deviation, min and max. Perk means are their population shares. Traits are sampled directly from live genomes, not seasonally adjusted movement/temperature values. Empty populations have blank statistics; previous observations remain in the file. Off-screen genomes are excluded and sample counts make this explicit.
- `ended.txt`: normal closure reason. Missing means completion was not confirmed.

The default cadence is 10 simulated seconds. Pausing does not add duplicate samples; higher speed changes wall-clock duration, not the sampling clock. Sampling occurs on the first frame reaching each boundary. If a long frame crosses multiple boundaries, the recorder writes one current observation with its actual time; it cannot recover unobserved history. Finalization may add a partial-interval observation. Do not assume perfectly equally spaced rows when graphing.

Use constant settings during evidence runs. Metadata captures initial settings, not a full intervention history. Keep **Keep Distant Animals As Numbers** off: that model does not include mating, mutation or individual selection. Record all replicates and extinctions.

Remaining for backlog item 30: in-game charts, counting individual breeding attempts blocked by ceilings (current CSV records ceiling state only), automatic build revision stamping, and broader integration tests with real animal populations. The Unity lifecycle suite covers separate runs on regeneration (including repeated resets while paused), no duplicate paused observations, disable/re-enable, stream closure, and the quit callback. Before comparisons, also cross-check exported animal counts against the HUD in the full Terrain scene. Then run matched mutation-load on/off replicates (item 46).
