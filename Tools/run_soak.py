#!/usr/bin/env python3
"""Play the Terrain scene in batch mode on one seed for some simulated years, in a dedicated project copy (macOS).

Saves the Unity log (with a season summary line per season), the run recorder's CSV files, a camera shot at each
season's peak and, unless --maps 0, top-down maps of the whole world.
"""
import argparse
from datetime import datetime, timezone
from pathlib import Path
import re
import shutil
import subprocess

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--project-copy', type=Path, required=True,
                    help='Dedicated test copy, as for run_unity_tests.py; reusing one avoids a full reimport')
parser.add_argument('--seed', default='FOREST-001')
parser.add_argument('--years', default='5', help='Simulated years of 400 s')
parser.add_argument('--speed', default='50', help='Simulation speed once the founders exist')
parser.add_argument('--seasons', default='1', help='0 switches seasons off for a control run')
parser.add_argument('--maps', default='1', help='0 skips the top-down maps at the end')
parser.add_argument('--minutes', default='40', help='Wall-clock limit for the run')
parser.add_argument('--experiment', help='Recorder experiment label; defaults to "soak <seed>"')
parser.add_argument('--harmful-chance', help='Harmful mutation chance in per cent set on every animal, for item 46 comparisons')
parser.add_argument('--sexual-drive', help='Lock sexual drive at this value (0 only clones, 1 only mates) on every animal, for item 60')
parser.add_argument('--sexual-drive-at', default='800',
                    help='Simulated seconds before the lock starts (default 800, two years: three founders that can only mate die out)')
parser.add_argument('--ledger', default='0', help='1 adds up every change to the animals\' energy and logs any gap (item 61)')
parser.add_argument('--perk-gain', help='Perk gain chance in per cent per birth on every animal (item 62; default 0.2)')
parser.add_argument('--perk-loss', help='Perk loss chance in per cent per birth on every animal (item 62; default 2)')
parser.add_argument('--founder-perks', help='Perks the founders start with, e.g. "Glow" (item 76; comma-separated)')
parser.add_argument('--ageing', default='1', help='0 switches ageing off on every animal (item 68), for control runs')
parser.add_argument('--size-scaling', default='1',
                    help='0 gives every animal the store and upkeep rule from before item 67 (size ignored), for control runs')
parser.add_argument('--thirst', default='1', help='0 switches thirst off on every animal')
parser.add_argument('--own-kind', default='1', help='0 stops animals hunting their own species (item 66), for control runs')
parser.add_argument('--daynight', default='1', help='0 switches day and night off (always midday), for control runs')
parser.add_argument('--natural-events', default='1',
                    help='0 stops natural events (wildfires, item 78) starting by themselves; god tools can still start them')
parser.add_argument('--fires', default='1', help='0 switches wildfires off altogether (item 79), for control runs')
parser.add_argument('--fire-strikes', help='Lightning strikes on fuel per simulated year at full fire danger (item 79; default 45)')
parser.add_argument('--fire-spread', help='Wildfire spread strength (item 79): how readily fire passes to the next cell')
parser.add_argument('--fuel-probe', default='0', help='1 logs what fuel lightning would find around the animals, for tuning item 79')
parser.add_argument('--start-search', help='Founder start search radius in metres; 0 starts at the simulation focus as before item 9')
parser.add_argument('--events', help='God tools to schedule, e.g. "drought@800:200x0.2,extinction@1200x0.5" (see GodTools.TryParse)')
parser.add_argument('--output', type=Path, help='Defaults to Logs/SoakRuns/<seed>-<UTC timestamp>')
parser.add_argument('--editor', type=Path, help='Unity executable; defaults to the project version in Unity Hub')
parser.add_argument('--license-channel', help='Existing local Unity licensing channel, if needed')
args = parser.parse_args()

version = re.search(r'm_EditorVersion: (\S+)', (root / 'ProjectSettings/ProjectVersion.txt').read_text())[1]
editor = args.editor or Path('/Applications/Unity/Hub/Editor') / version / 'Unity.app/Contents/MacOS/Unity'
if not editor.is_file():
    parser.error(f'Unity executable not found: {editor}; use --editor')
project = args.project_copy.resolve()
if project == root or root in project.parents or project in root.parents:
    parser.error('The test copy must be separate from the source project')
marker = project / '.boreal-test-copy'
if project.exists() and any(project.iterdir()) and not marker.is_file():
    parser.error('Refusing to overwrite an existing directory without a .boreal-test-copy marker')
project.mkdir(parents=True, exist_ok=True)
marker.touch()
for folder in ('Assets', 'Packages', 'ProjectSettings'):
    subprocess.run(['rsync', '-a', '--delete', str(root / folder) + '/', str(project / folder) + '/'], check=True)
# The harness only ever lives in the copy, so the project itself never compiles it.
harness = project / 'Assets/SoakRun/Editor'
harness.mkdir(parents=True, exist_ok=True)
for source in (root / 'Tools/soak').iterdir():
    shutil.copy(source, harness / source.name)
shutil.rmtree(project / 'Recordings/TopDownMap', ignore_errors=True)

channel = args.license_channel
editor_log = Path.home() / 'Library/Logs/Unity/Editor.log'
if not channel and editor_log.is_file():
    matches = re.findall(r'Successfully connected to: "(LicenseClient-[^"\r\n]+)"', editor_log.read_text(errors='replace'))
    channel = next((m for m in reversed(matches) if not m.endswith('-notifications')), None)
revision = subprocess.run(['git', '-C', str(root), 'rev-parse', '--short', 'HEAD'],
                          capture_output=True, text=True).stdout.strip()
if subprocess.run(['git', '-C', str(root), 'status', '--porcelain'], capture_output=True, text=True).stdout.strip():
    revision += '+dirty'

stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
output = (args.output or root / 'Logs/SoakRuns' / f'{args.seed}-{stamp}').resolve()
output.mkdir(parents=True, exist_ok=True)
command = [str(editor), '-batchmode', '-projectPath', str(project), '-logFile', str(output / 'editor.log'),
           '-executeMethod', 'Boreal.Soak.Editor.SoakRun.Run', '-soakSeed', args.seed, '-soakYears', args.years,
           '-soakSpeed', args.speed, '-soakSeasons', args.seasons, '-soakMaps', args.maps,
           '-soakMinutes', args.minutes, '-soakOutput', str(output), '-soakRevision', revision]
if args.experiment:
    command += ['-soakExperiment', args.experiment]
if args.harmful_chance is not None:
    command += ['-soakHarmfulChance', args.harmful_chance]
if args.sexual_drive is not None:
    command += ['-soakSexualDrive', args.sexual_drive, '-soakSexualDriveAt', args.sexual_drive_at]
if args.events:
    command += ['-soakEvents', args.events]
command += ['-soakLedger', args.ledger, '-soakAgeing', args.ageing, '-soakSizeScaling', args.size_scaling]
if args.perk_gain is not None:
    command += ['-soakPerkGain', args.perk_gain]
if args.perk_loss is not None:
    command += ['-soakPerkLoss', args.perk_loss]
if args.founder_perks:
    command += ['-soakFounderPerks', args.founder_perks]
command += ['-soakThirst', args.thirst, '-soakDayNight', args.daynight, '-soakOwnKind', args.own_kind]
command += ['-soakNaturalEvents', args.natural_events, '-soakFires', args.fires, '-soakFuelProbe', args.fuel_probe]
if args.fire_strikes is not None:
    command += ['-soakFireStrikes', args.fire_strikes]
if args.fire_spread is not None:
    command += ['-soakFireSpread', args.fire_spread]
if args.start_search is not None:
    command += ['-soakStartSearch', args.start_search]
if channel:
    command += ['-licensingIpc', channel]
print(f'Test copy: {project}\nOutput: {output}', flush=True)
run = subprocess.run(command, timeout=60 * (float(args.minutes) + 15))
log = (output / 'editor.log').read_text(errors='replace') if (output / 'editor.log').exists() else ''
for line in log.splitlines():
    if line.startswith(('Soak:', 'Season summary')):
        print(line)
finished = 'Soak: done' in log and 'Soak: timed out' not in log
raise SystemExit(run.returncode or (0 if finished else 1))
