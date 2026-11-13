#!/usr/bin/env python3
"""Run matched soak replicates for a two-way comparison (backlog item 46), one Unity batch run after another.

The default question is whether mutation load makes mating pay: the same world with harmful mutations at the usual
10% chance per birth and with them off. Every run is kept, extinctions included; summarise_runs.py reads the results.
"""
import argparse
from datetime import datetime, timezone
from pathlib import Path
import shlex
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--project-copy', type=Path, required=True)
parser.add_argument('--seed', default='MOSSY-FJORD')
parser.add_argument('--years', default='5')
parser.add_argument('--replicates', type=int, default=6)
parser.add_argument('--treatments', default='load-on=10,load-off=0',
                    help='label=harmful mutation chance pairs, comma separated; add :drive to lock sexual drive too, '
                         'e.g. clones=30:0,maters=30:1 (item 60). A value starting with -- is passed to run_soak.py as '
                         'it is, e.g. "old=--perk-gain 1 --perk-loss 1,new=--perk-gain 0.2 --perk-loss 2" (item 62)')
parser.add_argument('--minutes', default='25', help='Wall-clock limit for each run')
parser.add_argument('--output', type=Path, help='Defaults to Logs/SoakRuns/comparison-<UTC timestamp>')
args = parser.parse_args()

stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
output = (args.output or root / 'Logs/SoakRuns' / f'comparison-{stamp}').resolve()
output.mkdir(parents=True, exist_ok=True)
treatments = [t.split('=', 1) for t in args.treatments.split(',')]
failures = 0
# Treatments alternate, so a drift in the machine's state over the hour affects both alike.
for replicate in range(1, args.replicates + 1):
    for label, chance in treatments:
        run = output / f'{label}-r{replicate}'
        print(f'== {run.name}', flush=True)
        command = [sys.executable, str(root / 'Tools/run_soak.py'), '--project-copy', str(args.project_copy),
                   '--seed', args.seed, '--years', args.years, '--maps', '0', '--minutes', args.minutes,
                   '--experiment', f'{label} {args.seed}', '--output', str(run)]
        if chance.strip().startswith('--'):
            command += shlex.split(chance)
        else:
            chance, _, drive = chance.partition(':')
            command += ['--harmful-chance', chance]
            if drive:
                command += ['--sexual-drive', drive]
        result = subprocess.run(command, stdout=subprocess.DEVNULL)
        if result.returncode != 0:
            failures += 1
            print(f'   failed (exit {result.returncode}); see {run / "editor.log"}', flush=True)
print(f'Done: {output} ({failures} failed runs)')
raise SystemExit(1 if failures else 0)
