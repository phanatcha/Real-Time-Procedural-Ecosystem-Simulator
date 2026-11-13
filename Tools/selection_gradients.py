#!/usr/bin/env python3
"""Measure selection on each gene from recorded life records (backlog item 58).

usage: selection_gradients.py <run, recording or comparison folder> [...] [--each] [--fitness young|bred]
                              [--place] [--min-cohort N]

For every animal born in the simulation that has died, lives.csv (item 57) gives the genome it was born with and how
many young it had. Animals are grouped into cohorts by the year and season they were born in. Within each cohort,
fitness is made relative (young / the cohort's average) and every trait is standardised (mean 0, spread 1). Then:

- S, the selection differential: the covariance of relative fitness with the trait. It is the total change selection
  makes to the trait in one generation, including what it picks up from traits it travels with.
- beta, the selection gradient (Lande and Arnold 1983): the slope of relative fitness on the trait in a multiple
  regression on all traits together. It is the direct pressure on the trait itself. A perk carried along by a
  winning family line has a large S and a beta near zero.

The traits are the harmful mutations, every perk, every gene and the total size of each kind of body part.

beta is printed with a 95% range, overall and for each season (pooled over the years). An asterisk marks a range that
excludes zero. The ranges allow for brothers and sisters being alike (standard errors clustered by first parent), but
more distant relatives are alike too, so they are still a little narrow: the marker genes and the marker perk do
nothing, and the largest |beta| among them is the honest floor. "Clearly selected" lists the traits whose range
excludes zero and whose |beta| is above every marker's. Field studies find a typical |beta| of about 0.15.

Family lines live in different places, and a line in a rich patch leaves more young whatever its genes. --place holds
the birthplace equal (a smooth surface in the birth position within each cohort), so a trait that only rode along with
a well-placed line loses its beta.

Runs with the same experiment label are pooled (cohorts stay within their own run); --each prints each run too.
Fitness is lifetime young by default. --fitness bred counts only young that had young themselves, and uses only
animals that died at least a year before the recording ended, so their young had time to breed.
Animals still alive at the end, or that left for an off-screen population, are left out: their young are not final.
"""
import argparse
import bisect
import csv
import json
import math
from pathlib import Path

YEAR = 400.0
SEASONS = ('spring', 'summer', 'fall', 'winter')
NOT_FINAL = ('alive', 'left')


def recordings_under(path):
    if (path / 'lives.csv').is_file():
        return [path]
    if (path / 'recording' / 'lives.csv').is_file():
        return [path / 'recording']
    return sorted(p / 'recording' for p in path.iterdir() if (p / 'recording' / 'lives.csv').is_file())


def season_lookup(recording):
    times, names = [], []
    with (recording / 'population.csv').open() as handle:
        for row in csv.DictReader(handle):
            times.append(float(row['time']))
            names.append(row['season'].split()[-1].lower())

    def season_at(time):
        if not times:
            return 'none'
        index = min(max(bisect.bisect_left(times, time), 0), len(times) - 1)
        if index > 0 and abs(times[index - 1] - time) < abs(times[index] - time):
            index -= 1
        return names[index]
    return season_at


PART_TYPES = ('Legs', 'Neck', 'Horn', 'EyeStalks', 'Plates', 'Fins')
PLACE = ('place_x', 'place_z', 'place_x2', 'place_z2', 'place_xz')


def trait_columns(header):
    # harmful_mutations, every Perk_ column, the genes (between the last perk and the first body part), then the total
    # size of each kind of body part across the sites.
    perks = [c for c in header if c.startswith('Perk_')]
    last_perk = header.index(perks[-1]) if perks else header.index('harmful_mutations')
    first_part = next((i for i, c in enumerate(header) if c.startswith('Part_')), len(header))
    parts = [f'Parts_{t}' for t in PART_TYPES] if first_part < len(header) else []
    return ['harmful_mutations'] + perks + header[last_perk + 1:first_part] + parts


def trait_values(row, traits, part_sites):
    sizes = dict.fromkeys(PART_TYPES, 0.0)
    for site in part_sites:
        if row[site]:
            kind, _, size = row[site].partition(':')
            if kind in sizes:
                sizes[kind] += float(size)
    values = []
    for t in traits:
        if t.startswith('Parts_'):
            values.append(sizes[t[6:]])
        else:
            values.append(float(row[t]) if row[t] != '' else 0.0)
    return values


def load(recording, fitness_kind, min_cohort, place):
    with (recording / 'lives.csv').open() as handle:
        reader = csv.DictReader(handle)
        header = reader.fieldnames
        rows = list(reader)
    with (recording / 'population.csv').open() as handle:
        population = list(csv.DictReader(handle))
    end_time = float(population[-1]['time']) if population else 0.0
    season_at = season_lookup(recording)
    traits = trait_columns(header)
    part_sites = [c for c in header if c.startswith('Part_')]

    bred = {}
    if fitness_kind == 'bred':
        for row in rows:
            if int(row['young']) > 0:
                for parent in (row['first_parent'], row['second_parent']):
                    if parent:
                        bred[parent] = bred.get(parent, 0) + 1

    cohorts = {}
    for row in rows:
        if row['origin'] != 'born' or row['cause'] in NOT_FINAL or row['ended'] == '':
            continue
        if fitness_kind == 'bred' and float(row['ended']) > end_time - YEAR:
            continue
        born = float(row['born'])
        season = season_at(born)
        fitness = bred.get(row['id'], 0) if fitness_kind == 'bred' else int(row['young'])
        values = trait_values(row, traits, part_sites)
        if place:
            x, z = float(row['birth_x']) / 100.0, float(row['birth_z']) / 100.0
            values += [x, z, x * x, z * z, x * z]
        # Brothers and sisters share a first parent; the standard errors treat each such family as one cluster.
        family = row['first_parent'] or row['id']
        cohorts.setdefault((int(born // YEAR), season), []).append((fitness, values, family))
    usable = {key: members for key, members in cohorts.items()
              if len(members) >= min_cohort and sum(f for f, _, _ in members) > 0}
    return traits + (list(PLACE) if place else []), usable


def standardise(cohorts, trait_count):
    """Relative fitness and within-cohort z-scores. Returns rows of (season, w, z[]) and each trait's pooled
    within-cohort spread in its own units."""
    out = []
    squares = [0.0] * trait_count
    weight = 0
    for (run_year, season), members in cohorts.items():
        n = len(members)
        mean_fitness = sum(f for f, _, _ in members) / n
        means = [sum(v[j] for _, v, _ in members) / n for j in range(trait_count)]
        sds = []
        for j in range(trait_count):
            variance = sum((v[j] - means[j]) ** 2 for _, v, _ in members) / n
            squares[j] += variance * n
            sds.append(math.sqrt(variance))
        weight += n
        for fitness, values, family in members:
            z = [(values[j] - means[j]) / sds[j] if sds[j] > 1e-12 else 0.0 for j in range(trait_count)]
            out.append((season, fitness / mean_fitness, z, (run_year[0], family)))
    spreads = [math.sqrt(s / weight) if weight else 0.0 for s in squares]
    return out, spreads


def regress(rows, columns):
    """Ordinary least squares of w on an intercept and the z-scores in `columns`, with standard errors clustered by
    family. Returns {column: (beta, se)}; a column that moves with the others so closely it can't be told apart gets
    None."""
    k = len(columns) + 1
    n = len(rows)
    if n <= k + 2:
        return {}
    xtx = [[0.0] * k for _ in range(k)]
    xty = [0.0] * k
    yty = 0.0
    for _, w, z, _ in rows:
        x = [1.0] + [z[c] for c in columns]
        yty += w * w
        for a in range(k):
            xa = x[a]
            if xa == 0.0:
                continue
            xty[a] += xa * w
            row = xtx[a]
            for b in range(a, k):
                row[b] += xa * x[b]
    for a in range(k):
        for b in range(a):
            xtx[a][b] = xtx[b][a]
    # Gauss-Jordan inverse; a pivot that has all but vanished marks a column that can't be separated.
    original = [xtx[i][i] for i in range(k)]
    inverse = [[1.0 if i == j else 0.0 for j in range(k)] for i in range(k)]
    matrix = [r[:] for r in xtx]
    dropped = set()
    for p in range(k):
        if abs(matrix[p][p]) <= 1e-9 * max(original[p], 1e-12):
            dropped.add(p)
            for i in range(k):
                matrix[p][i] = matrix[i][p] = 0.0
                inverse[p][i] = inverse[i][p] = 0.0
            matrix[p][p] = inverse[p][p] = 1.0
            continue
        pivot = matrix[p][p]
        for j in range(k):
            matrix[p][j] /= pivot
            inverse[p][j] /= pivot
        for i in range(k):
            if i != p and matrix[i][p] != 0.0:
                factor = matrix[i][p]
                for j in range(k):
                    matrix[i][j] -= factor * matrix[p][j]
                    inverse[i][j] -= factor * inverse[p][j]
    beta = [sum(inverse[i][j] * xty[j] for j in range(k)) for i in range(k)]
    # The sandwich: (X'X)^-1 (sum over families of u u') (X'X)^-1, where u adds up x times the residual over a family.
    scores = {}
    for _, w, z, family in rows:
        x = [1.0] + [z[c] for c in columns]
        residual = w - sum(beta[i] * x[i] for i in range(k))
        u = scores.get(family)
        if u is None:
            u = scores[family] = [0.0] * k
        for i in range(k):
            u[i] += x[i] * residual
    meat = [[0.0] * k for _ in range(k)]
    for u in scores.values():
        for a in range(k):
            ua = u[a]
            if ua == 0.0:
                continue
            row = meat[a]
            for b in range(a, k):
                row[b] += ua * u[b]
    for a in range(k):
        for b in range(a):
            meat[a][b] = meat[b][a]
    families = len(scores)
    correction = families / max(1, families - 1) * (n - 1) / max(1, n - k)
    result = {}
    for index, column in enumerate(columns, start=1):
        if index in dropped:
            result[column] = None
            continue
        bread = inverse[index]
        variance = correction * sum(bread[a] * sum(meat[a][b] * bread[b] for b in range(k)) for a in range(k))
        result[column] = (beta[index], math.sqrt(max(0.0, variance)))
    return result


def differentials(rows, columns):
    n = len(rows)
    return {c: sum(w * z[c] for _, w, z, _ in rows) / n for c in columns} if n else {}


def describe(label, groups, traits, fitness_kind):
    # Cohorts stay within their run: the key is ((run, year), season).
    cohorts = {((run_index, year), season): members
               for run_index, run_cohorts in enumerate(groups)
               for (year, season), members in run_cohorts.items()}
    rows, spreads = standardise(cohorts, len(traits))
    if not rows:
        print(f'{label}: no complete lives to measure\n')
        return
    # Leave out traits with no variation within any cohort: nothing to measure.
    columns = [j for j in range(len(traits)) if any(abs(z[j]) > 0 for _, _, z, _ in rows)]
    overall = regress(rows, columns)
    totals = differentials(rows, columns)
    by_season = {s: regress([r for r in rows if r[0] == s], columns) for s in SEASONS}
    animals = len(rows)
    mean_young = sum(sum(f for f, _, _ in members) for members in cohorts.values()) / animals
    measure = 'lifetime young' if fitness_kind == 'young' else 'young that bred'
    print(f'{label}: {animals} animals in {len(cohorts)} cohorts, {mean_young:.2f} {measure} on average')
    print(f'  {"trait":<20}{"S total":>9}{"beta direct":>13}  {"95% range":<17}' +
          ''.join(f'{s:>9}' for s in SEASONS))

    def cell(estimate):
        if estimate is None:
            return '  linked'
        b, se = estimate
        return f'{b:+.3f}' + ('*' if abs(b) > 1.96 * se else ' ')

    marker_betas = []
    for j in columns:
        name = traits[j]
        if name in PLACE:
            continue
        estimate = overall.get(j)
        if estimate is None:
            line = f'  {name:<20}{totals[j]:>+9.3f}{"linked":>13}  {"(moves with another trait)":<17}'
        else:
            b, se = estimate
            star = '*' if abs(b) > 1.96 * se else ' '
            line = f'  {name:<20}{totals[j]:>+9.3f}{b:>+12.3f}{star}  [{b - 1.96 * se:+.3f}, {b + 1.96 * se:+.3f}]'
            if name.startswith('Marker') or name == 'Perk_Marker':
                marker_betas.append(abs(b))
        line += ''.join(f'{cell(by_season[s].get(j)) if by_season[s] else "-":>9}' for s in SEASONS)
        print(line)
    harmful = traits.index('harmful_mutations')
    if harmful in columns and overall.get(harmful) and spreads[harmful] > 0:
        b, se = overall[harmful]
        per = b / spreads[harmful]
        half = 1.96 * se / spreads[harmful]
        print(f'  Each harmful mutation changes {measure} by {100 * per:+.1f}% '
              f'(range {100 * (per - half):+.1f}% to {100 * (per + half):+.1f}%), the other traits held equal.')
    if marker_betas:
        floor = max(marker_betas)
        print(f'  The do-nothing markers reach |beta| up to {floor:.3f}: smaller values are within chance.')
        clear = [f'{traits[j]} {overall[j][0]:+.3f}' for j in columns
                 if traits[j] not in PLACE and overall.get(j) and not traits[j].startswith('Marker')
                 and traits[j] != 'Perk_Marker' and abs(overall[j][0]) > max(floor, 1.96 * overall[j][1])]
        print('  Clearly selected: ' + (', '.join(clear) if clear else 'nothing beyond the markers'))
    if any(t in PLACE for t in traits):
        print('  Birthplace held equal (--place).')
    print()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('paths', nargs='+', type=Path)
    parser.add_argument('--each', action='store_true', help='Print every run, not only each experiment pooled')
    parser.add_argument('--fitness', choices=('young', 'bred'), default='young')
    parser.add_argument('--min-cohort', type=int, default=10, help='Smallest cohort used (default 10)')
    parser.add_argument('--place', action='store_true', help='Hold the birthplace equal')
    args = parser.parse_args()

    recordings = [r for path in args.paths for r in recordings_under(path)]
    if not recordings:
        raise SystemExit('no recordings with lives.csv found (recordings before schema 8 have none)')
    by_label = {}
    traits = None
    for recording in recordings:
        metadata = json.load((recording / 'metadata.json').open()) if (recording / 'metadata.json').is_file() else {}
        run_traits, cohorts = load(recording, args.fitness, args.min_cohort, args.place)
        if traits is None:
            traits = run_traits
        elif run_traits != traits:
            raise SystemExit(f'{recording}: its lives.csv has different traits from the first run; summarise apart')
        label = metadata.get('experiment') or recording.parent.name
        by_label.setdefault(label, []).append((recording, cohorts))
        if args.each:
            describe(f'{recording.parent.name} ({label})', [cohorts], traits, args.fitness)
    for label, runs in by_label.items():
        if args.each and len(runs) == 1:
            continue
        describe(f'{label}: {len(runs)} run{"s" if len(runs) > 1 else ""} pooled', [c for _, c in runs], traits,
                 args.fitness)


if __name__ == '__main__':
    main()
