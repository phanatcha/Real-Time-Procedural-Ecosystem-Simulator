#!/usr/bin/env python3
"""Summarise recorded soak runs: one line per run, then the mean and spread per experiment label.

Reads the run recorder's files (RUN_RECORDING.md) under each run folder's recording/ directory. Pass run folders or a
comparison folder that holds them.
"""
import csv
import json
import statistics
import sys
from pathlib import Path


def runs_under(path):
    if (path / 'recording').is_dir():
        return [path]
    return sorted(p for p in path.iterdir() if (p / 'recording').is_dir())


def last_trait(rows, species, trait):
    values = [r for r in rows if r['species'] == species and r['trait'] == trait and r['mean'] != '']
    return float(values[-1]['mean']) if values else float('nan')


def summarise(run):
    recording = run / 'recording'
    metadata = json.load((recording / 'metadata.json').open())
    population = list(csv.DictReader((recording / 'population.csv').open()))
    species = list(csv.DictReader((recording / 'species.csv').open()))
    traits = list(csv.DictReader((recording / 'traits.csv').open()))
    last = population[-1]
    live = [int(r['live']) for r in population]
    final_time = float(last['time'])
    # The largest species at the end, by live members.
    final_species = [r for r in species if float(r['time']) == final_time]
    largest = max(final_species, key=lambda r: int(r['live']))['species'] if final_species else None
    births = int(last['births'])
    return {
        'run': run.name,
        'experiment': metadata.get('experiment', ''),
        'seed': metadata.get('worldSeed', ''),
        'years': final_time / 400,
        'final': live[-1],
        'mean_live': statistics.mean(live),
        'min_live': min(live[1:]) if len(live) > 1 else live[0],
        'births': births,
        'sexual_share': int(last['sexual_births']) / births if births else float('nan'),
        'starvation': int(last['starvation']),
        'species_alive': int(last['species_alive']),
        'sexual_drive': last_trait(traits, largest, 'SexualDrive') if largest else float('nan'),
        'harmful': last_trait(traits, largest, 'HarmfulMutations') if largest else float('nan'),
        'extinct': live[-1] == 0,
    }


def main():
    paths = [Path(p) for p in sys.argv[1:]] or [Path('Logs/SoakRuns')]
    rows = [summarise(run) for path in paths for run in runs_under(path)]
    if not rows:
        sys.exit('no recorded runs found')
    header = f'{"run":<22}{"experiment":<24}{"yrs":>5}{"final":>6}{"mean":>7}{"min":>5}{"births":>7}{"2par":>6}{"drive":>7}{"harm":>6}{"spp":>4}'
    print(header)
    for r in rows:
        print(f'{r["run"]:<22}{r["experiment"][:23]:<24}{r["years"]:>5.1f}{r["final"]:>6}{r["mean_live"]:>7.1f}{r["min_live"]:>5}'
              f'{r["births"]:>7}{r["sexual_share"]:>6.0%}{r["sexual_drive"]:>7.3f}{r["harmful"]:>6.2f}{r["species_alive"]:>4}'
              + ('  extinct' if r['extinct'] else ''))
    by_label = {}
    for r in rows:
        by_label.setdefault(r['experiment'], []).append(r)
    if len(by_label) > 1:
        print('\nPer experiment: mean (standard deviation) over replicates')
        for label, group in by_label.items():
            def stat(key):
                values = [g[key] for g in group if g[key] == g[key]]
                if not values:
                    return 'n/a'
                spread = statistics.stdev(values) if len(values) > 1 else 0.0
                return f'{statistics.mean(values):.3f} ({spread:.3f})'
            print(f'{label}: {len(group)} runs, {sum(g["extinct"] for g in group)} extinct; final animals {stat("final")}, '
                  f'births from two parents {stat("sexual_share")}, sexual drive {stat("sexual_drive")}, '
                  f'harmful mutations {stat("harmful")}')


if __name__ == '__main__':
    main()
