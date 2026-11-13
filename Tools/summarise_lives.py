#!/usr/bin/env python3
"""Check and summarise a recording's lives.csv (backlog item 57).

usage: summarise_lives.py <run folder or recording folder> [...]

For each run it checks that the life records add up to population.csv, then prints how unevenly young are
shared out: the mean and variance of lifetime young, the share of all young left by the top tenth of parents, how
many animals left young that bred in turn, and a rough effective population size.
"""
import csv
import statistics
import sys
from pathlib import Path

NOT_DEAD = ('alive', 'left')


def load(recording):
    lives = list(csv.DictReader((recording / 'lives.csv').open()))
    population = list(csv.DictReader((recording / 'population.csv').open()))
    return lives, population[-1] if population else None


def summarise(recording):
    lives, last = load(recording)
    ids = [r['id'] for r in lives]
    problems = []
    if len(set(ids)) != len(ids):
        problems.append('some animals have more than one row')
    born = sum(r['origin'] == 'born' for r in lives)
    dead = [r for r in lives if r['cause'] not in NOT_DEAD]
    alive = sum(r['cause'] == 'alive' for r in lives)
    if last:
        for label, mine, theirs in (('births', born, int(last['births'])), ('deaths', len(dead), int(last['deaths'])),
                                    ('animals alive', alive, int(last['live']))):
            if mine != theirs:
                problems.append(f'{label}: lives.csv {mine}, population.csv {theirs}')
    known = set(ids)
    orphans = sum(1 for r in lives for p in (r['first_parent'], r['second_parent']) if p and p not in known)
    if orphans:
        problems.append(f'{orphans} parent IDs have no row')

    # Whether each animal's young went on to breed needs the whole table: a child may breed after its parent dies.
    young = {r['id']: int(r['young']) for r in lives}
    bred_young = dict.fromkeys(ids, 0)
    for r in lives:
        if young[r['id']] > 0:
            for parent in (r['first_parent'], r['second_parent']):
                if parent in bred_young:
                    bred_young[parent] += 1

    # Lifetime young is only complete for animals whose lives have ended.
    counts = sorted((int(r['young']) for r in dead), reverse=True)
    print(f'{recording}')
    print(f'  rows {len(lives)}: {born} born, {len(lives) - born} founders or arrivals; {len(dead)} dead, {alive} alive')
    print('  adds up to population.csv' if not problems else '  DOES NOT ADD UP: ' + '; '.join(problems))
    if len(counts) > 1 and sum(counts) > 0:
        mean, variance = statistics.mean(counts), statistics.pvariance(counts)
        top = counts[:max(1, len(counts) // 10)]
        childless = sum(c == 0 for c in counts)
        with_breeding_young = sum(bred_young[r['id']] > 0 for r in dead)
        # Variance effective size for one generation of a mostly cloning population, N * mean^2 / variance: equal to N
        # when young are shared out by chance alone (variance = mean = 1). Generations overlap here, so read it as a
        # rough ratio, not a census.
        ratio = mean * mean / variance if variance > 0 else float('nan')
        print(f'  lifetime young of the dead: mean {mean:.2f}, variance {variance:.2f}, most {counts[0]}')
        print(f'  {childless / len(counts):.0%} left no young; the top tenth of animals left {sum(top) / sum(counts):.0%} of all young')
        print(f'  {with_breeding_young / len(counts):.0%} left young that bred in turn')
        print(f'  effective size about {ratio:.2f} of the head count (rough)')
    causes = {}
    for r in dead:
        causes[r['cause']] = causes.get(r['cause'], 0) + 1
    if causes:
        print('  deaths: ' + ', '.join(f'{k} {v}' for k, v in sorted(causes.items(), key=lambda kv: -kv[1])))
    ages = [float(r['age']) for r in dead]
    if ages:
        print(f'  age at death: median {statistics.median(ages):.0f} s, oldest {max(ages):.0f} s')
    return not problems


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    ok = True
    for arg in sys.argv[1:]:
        path = Path(arg)
        recording = path / 'recording' if (path / 'recording').is_dir() else path
        if not (recording / 'lives.csv').is_file():
            print(f'{path}: no lives.csv (recordings before schema 8 have none)')
            ok = False
            continue
        ok = summarise(recording) and ok
    sys.exit(0 if ok else 1)


if __name__ == '__main__':
    main()
