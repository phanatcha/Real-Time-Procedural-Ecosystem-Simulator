#!/usr/bin/env python3
"""Follow the harmful-mutation load through recorded runs (backlog item 60: clones against maters).

usage: summarise_load.py <comparison folder or run folders> [--every YEARS]

For each run it prints, at regular intervals, the animals alive, the average generation, and the lowest and the
average number of harmful mutations carried. The lowest count is Muller's ratchet: among clones it can only step up
(a "click", when the last animals with the fewest mutations die out), while animals that mate can bring it back down.
Then one line per run: how often the lowest count stepped up and down, and when the population died out, if it did.
"""
import argparse
import csv
import json
from pathlib import Path

YEAR = 400.0


def runs_under(path):
    if (path / 'recording').is_dir():
        return [path]
    return sorted(p for p in path.iterdir() if (p / 'recording').is_dir())


def series(run):
    recording = run / 'recording'
    population = {float(r['time']): r for r in csv.DictReader((recording / 'population.csv').open())}
    # Across every species alive at a sample: the lowest count, and the average weighted by animals.
    load = {}
    for r in csv.DictReader((recording / 'traits.csv').open()):
        if r['trait'] != 'HarmfulMutations' or r['mean'] == '':
            continue
        entry = load.setdefault(float(r['time']), [float('inf'), 0.0, 0])
        count = int(r['live_sample_count'])
        entry[0] = min(entry[0], float(r['minimum']))
        entry[1] += float(r['mean']) * count
        entry[2] += count
    rows = []
    for time in sorted(population):
        p = population[time]
        lowest, total, count = load.get(time, [None, 0.0, 0])
        rows.append({'time': time, 'live': int(p['live']),
                     'generation': float(p['average_generation']) if p.get('average_generation') else None,
                     'lowest': lowest if count else None, 'mean': total / count if count else None})
    return rows


def describe(run, every):
    rows = series(run)
    metadata = json.load((run / 'recording' / 'metadata.json').open())
    print(f'{run.name}  ({metadata.get("experiment", "")})')
    print(f'  {"year":>5}{"animals":>9}{"generation":>12}{"lowest":>8}{"average":>9}')
    next_mark = 0.0
    for row in rows:
        if row['time'] + 1e-6 >= next_mark * YEAR or row is rows[-1]:
            def cell(value, form):
                return format(value, form) if value is not None else '-'
            print(f'  {row["time"] / YEAR:>5.1f}{row["live"]:>9}{cell(row["generation"], ".1f"):>12}'
                  f'{cell(row["lowest"], ".0f"):>8}{cell(row["mean"], ".2f"):>9}')
            while next_mark * YEAR <= row['time'] + 1e-6:
                next_mark += every
    ups = downs = 0
    previous = None
    first_click = None
    for row in rows:
        if row['lowest'] is None:
            continue
        if previous is not None and row['lowest'] > previous:
            ups += 1
            if first_click is None:
                first_click = row
        if previous is not None and row['lowest'] < previous:
            downs += 1
        previous = row['lowest']
    died = next((row for row in rows[1:] if row['live'] == 0), None)
    summary = f'  lowest count stepped up {ups} times and down {downs} times'
    if first_click:
        summary += f'; first step up at year {first_click["time"] / YEAR:.1f}'
        if first_click['generation'] is not None:
            summary += f', generation {first_click["generation"]:.1f}'
    summary += f'; died out at year {died["time"] / YEAR:.1f}' if died else '; alive at the end'
    print(summary + '\n')
    last = next((row for row in reversed(rows) if row['mean'] is not None), None)
    return {'run': run.name, 'experiment': metadata.get('experiment', ''), 'ups': ups, 'downs': downs,
            'died': died['time'] / YEAR if died else None, 'years': rows[-1]['time'] / YEAR,
            'final': rows[-1]['live'], 'lowest': last['lowest'] if last else None,
            'mean': last['mean'] if last else None, 'generation': last['generation'] if last else None}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('paths', nargs='+', type=Path)
    parser.add_argument('--every', type=float, default=5.0, help='Years between printed rows')
    args = parser.parse_args()
    results = [describe(run, args.every) for path in args.paths for run in runs_under(path)]
    print(f'{"run":<24}{"years":>6}{"final":>6}{"died":>6}{"gen":>7}{"lowest":>7}{"avg":>7}{"up":>4}{"down":>5}')
    for r in results:
        def cell(value, form):
            return format(value, form) if value is not None else '-'
        print(f'{r["run"]:<24}{r["years"]:>6.1f}{r["final"]:>6}{cell(r["died"], ".1f"):>6}{cell(r["generation"], ".1f"):>7}'
              f'{cell(r["lowest"], ".0f"):>7}{cell(r["mean"], ".2f"):>7}{r["ups"]:>4}{r["downs"]:>5}')


if __name__ == '__main__':
    main()
