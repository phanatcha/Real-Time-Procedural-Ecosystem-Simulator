#!/usr/bin/env python3
"""Summarise the wildfires in soak runs (items 78 and 79): one line per fire and a total per run.

Usage: Tools/summarise_fires.py <run folder or folder of runs> [...]

Reads each run's recording/events.csv (wildfire starts and ends) and population.csv (deaths by fire, animals alive,
fire weather). For every fire it prints when and where it started and why, the season, dryness and wind, how long it
burned, hectares burned, the most cells alight at once, animals killed and animals that ran, plants burned, and the
animals alive at its start, at its end and a year after. Pure Python, no packages.
"""
import csv
import re
import sys
from pathlib import Path

YEAR = 400.0
START = re.compile(r'fire (\d+) started by (.+?) in (forest|grass) at x (-?\d+) z (-?\d+) in (.+?), (.+?), '
                   r'dryness ([\d.]+), fire danger ([\d.]+), wind ([\d.]+) m/s towards (\S+)')
END = re.compile(r'fire (\d+) (.+?) after (\d+) s: ([\d.]+) ha burned, peak (\d+) cells burning, (\d+) animals killed, '
                 r'(\d+) fled, (\d+) plants burned')


def runs_under(path):
    if (path / 'recording' / 'events.csv').is_file():
        return [path]
    return sorted(p for p in path.iterdir() if (p / 'recording' / 'events.csv').is_file())


def alive_at(population, time):
    best = None
    for row in population:
        if float(row['time']) <= time + 1e-6:
            best = row
        else:
            break
    return int(best['live']) if best else None


def summarise(run):
    recording = run / 'recording'
    events = list(csv.DictReader((recording / 'events.csv').open()))
    population = list(csv.DictReader((recording / 'population.csv').open()))
    fires = {}
    for event in events:
        time = float(event['time'])
        if event['event'] == 'wildfire':
            match = START.match(event['detail'])
            if not match:
                continue
            number = int(match[1])
            fires[number] = {
                'start': time, 'cause': match[2], 'fuel': match[3], 'x': int(match[4]), 'z': int(match[5]),
                'season': match[6], 'celsius': match[7], 'dryness': float(match[8]), 'danger': float(match[9]),
                'wind': float(match[10]), 'towards': match[11],
            }
        elif event['event'] == 'wildfire-end':
            match = END.match(event['detail'])
            if not match or int(match[1]) not in fires:
                continue
            fires[int(match[1])].update({
                'end': time, 'how': match[2], 'seconds': int(match[3]), 'hectares': float(match[4]),
                'peak': int(match[5]), 'killed': int(match[6]), 'fled': int(match[7]), 'plants': int(match[8]),
            })

    last = population[-1] if population else {}
    print(f'\n{run.name}: {len(fires)} fires in {float(last.get("time", 0)) / YEAR:.1f} years')
    if fires:
        print(f'  {"fire":>4} {"year":>5} {"season":<13}{"cause":<10}{"fuel":<7}{"dry":>5}{"danger":>7}{"wind":>5}'
              f'{"secs":>6}{"ha":>7}{"peak":>6}{"killed":>7}{"fled":>6}{"plants":>7}{"alive":>7}{"after":>7}{"+1 yr":>7}')
    killed = hectares = 0
    for number, fire in sorted(fires.items()):
        end = fire.get('end')
        before = alive_at(population, fire['start'])
        after = alive_at(population, end) if end is not None else None
        later = alive_at(population, end + YEAR) if end is not None and float(last['time']) >= end + YEAR else None
        killed += fire.get('killed', 0)
        hectares += fire.get('hectares', 0.0)
        cell = lambda value, width, form='': f'{value:>{width}{form}}' if value is not None else f'{"-":>{width}}'
        print(f'  {number:>4} {fire["start"] / YEAR + 1:>5.2f} {fire["season"][:12]:<13}{fire["cause"][:9]:<10}'
              f'{fire["fuel"]:<7}{fire["dryness"]:>5.2f}{fire["danger"]:>7.2f}{fire["wind"]:>5.1f}'
              f'{cell(fire.get("seconds"), 6)}{cell(fire.get("hectares"), 7, ".1f")}{cell(fire.get("peak"), 6)}'
              f'{cell(fire.get("killed"), 7)}{cell(fire.get("fled"), 6)}{cell(fire.get("plants"), 7)}'
              f'{cell(before, 7)}{cell(after, 7)}{cell(later, 7)}'
              + ('' if end is not None else '  still burning'))
    deaths = int(last.get('deaths', 0) or 0)
    fire_deaths = int(last.get('fire', 0) or 0) if 'fire' in last else 0
    burned = float(last['burned_hectares']) if last.get('burned_hectares') else hectares
    live = [int(row['live']) for row in population]
    print(f'  in all: {burned:.1f} ha burned, {fire_deaths} of {deaths} deaths by fire '
          f'({100.0 * fire_deaths / deaths if deaths else 0:.1f}%), {killed} counted against fires; animals alive '
          f'{min(live) if live else "-"} to {max(live) if live else "-"}, {live[-1] if live else "-"} at the end')
    if population and population[0].get('dryness') not in (None, ''):
        by_season = {}
        for row in population:
            season = row['season'].split()[-1].lower()
            by_season.setdefault(season, []).append(float(row['dryness']))
        print('  average dryness: ' + ', '.join(f'{season} {sum(v) / len(v):.2f}' for season, v in by_season.items()))
    return len(fires), fire_deaths, burned


def main():
    paths = [Path(p) for p in sys.argv[1:]] or [Path('Logs/SoakRuns')]
    totals = [summarise(run) for path in paths for run in runs_under(path)]
    if not totals:
        sys.exit('no recorded runs found')
    if len(totals) > 1:
        fires = sum(t[0] for t in totals)
        print(f'\n{len(totals)} runs: {fires} fires, {sum(t[1] for t in totals)} animals killed by fire, '
              f'{sum(t[2] for t in totals):.1f} ha burned')


if __name__ == '__main__':
    main()
