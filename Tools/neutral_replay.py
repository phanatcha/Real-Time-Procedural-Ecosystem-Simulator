#!/usr/bin/env python3
"""Replay recorded runs without selection and set every trait against what chance alone gives (backlog item 59).

    python3 Tools/neutral_replay.py <run, recording or comparison folder> [...] [--replays 20] [--band 0.9]
    python3 Tools/neutral_replay.py --self-test

Each recording's lives.csv is replayed by Tools/replay (the game's own NeutralReplay.cs and AnimalGenome.cs, built with
dotnet). Every animal keeps its place in time, born when it was born and gone when it died, but each birth takes its
parents at random among the animals alive then of about the real parents' age, and the young get their genes by the
game's rules. Nothing about who lives or breeds depends on genes any more, so the replays show how far each trait
moves by drift and the mutation rules alone. The real run is set against the band the replays span: all but the k
highest and k lowest replays, where k / (replays + 1) is the chance a neutral trait ends above it (and below it).
--mode tree keeps the real family tree instead and draws only the mutations afresh: exact demography, but it finds
less, because selection shaped the tree.

Written beside each recording, in <run>/neutral/:
  replays-<mode>.csv   the mean of every trait over the living animals every 10 s, for the real run (replay 0) and
                       each replay
  neutral-<mode>.svg   a chart per trait: the real run as a line over the replays' band (light), their middle half
                       (dark) and their median (dashed)

Printed: per run, each trait's start and end, the replays' median and band at the end, and whether the real run ended
above, below or inside it; then, for runs pooled by experiment label, how many ended above and below, against the share
chance alone puts outside the band. The do-nothing markers (item 56) should end inside it about as often as the band
says; that is the check that the replay is fair.

The mutation settings come from each run's soak log (editor.log): the perk gain and loss chances and any harmful
mutation chance the soak set. A log without the perk line predates item 62, when perks flipped at 1% each way.
Recordings without a log get SeekFood's defaults. --perk-gain, --perk-loss, --harmful-chance, --mutation-chance and
--mutation-magnitude override them. Not replayed: god tools that change mutation rates, sexual drive locks (item 60)
and species boundaries (all animals are one pool). Genes a recording has no column for (added after it was made) are
left out.
"""
import argparse
import csv
import json
import math
import re
import subprocess
import sys
import tempfile
from collections import defaultdict
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
PROJECT = REPO / 'Tools' / 'replay' / 'NeutralReplay.csproj'
BUILD = Path(tempfile.gettempdir()) / 'boreal-neutral-replay'
DLL = BUILD / 'bin' / 'NeutralReplay' / 'release' / 'NeutralReplay.dll'

# Charted first, in this order; any other trait that moves follows.
HEADLINE = ['BodyBulk', 'BodyHeight', 'MaturityTime', 'MoveSpeed', 'VisionRadius', 'MaxEnergy', 'BreedingThreshold',
            'LitterSize', 'DietAffinity', 'ColdTolerance', 'HeatTolerance', 'Torpor', 'SexualDrive', 'MutationRate',
            'AgeingOnset', 'Nocturnality', 'NightVision', 'harmful_mutations', 'Perk_ThickFur', 'Perk_ScavengerGut',
            'Perk_Camouflage', 'Perk_Venom', 'Perk_Regeneration', 'Parts_Legs', 'Parts_Neck', 'Parts_Fins',
            'Parts_Plates', 'Parts_Horn', 'Parts_EyeStalks', 'MarkerA', 'MarkerB', 'MarkerC', 'Perk_Marker']
MARKERS = ('MarkerA', 'MarkerB', 'MarkerC', 'Perk_Marker')
MODES = {'tree': 'real family tree, mutations drawn afresh', 'random': 'parents at random'}


def build():
    command = ['dotnet', 'build', str(PROJECT), '-c', 'Release', '--nologo', '-v', 'quiet',
               '--artifacts-path', str(BUILD)]
    try:
        result = subprocess.run(command, capture_output=True, text=True)
    except FileNotFoundError:
        sys.exit('dotnet was not found; install the .NET SDK (10) to run the replays.')
    if result.returncode != 0 or not DLL.is_file():
        sys.exit('Building Tools/replay failed:\n' + result.stdout + result.stderr)


def recordings_under(path):
    if (path / 'lives.csv').is_file():
        return [path]
    if (path / 'recording' / 'lives.csv').is_file():
        return [path / 'recording']
    return sorted(p / 'recording' for p in path.iterdir() if (p / 'recording' / 'lives.csv').is_file())


def run_settings(recording):
    """The mutation settings the run used, from its soak log, and a note on where they came from."""
    log = recording.parent / 'editor.log'
    settings = {}
    if not log.is_file():
        return settings, "SeekFood's defaults (no soak log)"
    text = log.read_text(errors='replace')
    perks = re.search(r'Soak: perks gained at ([0-9.]+)% and lost at ([0-9.]+)% per birth', text)
    if perks:
        settings['perk-gain'], settings['perk-loss'] = perks.group(1), perks.group(2)
        note = f'perks {perks.group(1)}%/{perks.group(2)}%'
    else:
        settings['perk-gain'] = settings['perk-loss'] = '1'
        note = 'perks 1%/1% (log before item 62)'
    harmful = re.search(r'Soak: harmful mutation chance set to ([0-9.]+)%', text)
    if harmful:
        settings['harmful-chance'] = harmful.group(1)
        note += f', harmful {harmful.group(1)}%'
    return settings, note


def experiment(recording):
    meta = recording / 'metadata.json'
    if meta.is_file():
        try:
            label = json.loads(meta.read_text()).get('experiment') or ''
            if label:
                return label
        except json.JSONDecodeError:
            pass
    return re.sub(r'-r\d+$', '', recording.parent.name)


def year_length(recording):
    try:
        seasons = json.loads(json.loads((recording / 'metadata.json').read_text())['seasonSettings'])
        return float(seasons.get('yearLength', 400.0))
    except (OSError, KeyError, ValueError, TypeError):
        return 400.0


def replay(recording, args, settings):
    out = recording.parent / 'neutral'
    out.mkdir(exist_ok=True)
    target = out / f'replays-{args.mode}.csv'
    command = ['dotnet', str(DLL), str(recording / 'lives.csv'), str(target), '--replays', str(args.replays),
               '--seed', str(args.seed), '--mode', args.mode]
    merged = dict(settings)
    for key in ('mutation-chance', 'mutation-magnitude', 'perk-gain', 'perk-loss', 'harmful-chance', 'age-window'):
        value = getattr(args, key.replace('-', '_'))
        if value is not None:
            merged[key] = str(value)
    for key, value in merged.items():
        command += ['--' + key, value]
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f'replay of {recording} failed: {result.stderr.strip()}')
    return target


def read_replays(path):
    with path.open() as handle:
        reader = csv.reader(handle)
        header = next(reader)
        traits = header[3:]
        runs = defaultdict(lambda: {'time': [], 'live': [], 'values': defaultdict(list)})
        for row in reader:
            run = runs[int(row[0])]
            run['time'].append(float(row[1]))
            run['live'].append(int(row[2]))
            for trait, cell in zip(traits, row[3:]):
                run['values'][trait].append(float(cell) if cell else math.nan)
    real = runs.pop(0)
    return traits, real, [runs[k] for k in sorted(runs)]


def quantile(values, q):
    values = sorted(v for v in values if not math.isnan(v))
    if not values:
        return math.nan
    position = q * (len(values) - 1)
    low = int(math.floor(position))
    high = min(low + 1, len(values) - 1)
    return values[low] + (values[high] - values[low]) * (position - low)


def last_value(series):
    for value in reversed(series):
        if not math.isnan(value):
            return value
    return math.nan


def band_rank(replays, band):
    """How many replays sit outside the band on each side: k, so the band runs from the k-th lowest replay to the k-th
    highest. A neutral trait's real value then lands below it with chance k / (n + 1), and above it the same, exactly
    (for continuous values; ties count as inside)."""
    return max(1, round((1 - band) / 2 * (len(replays) + 1)))


def side_chance(replays, band):
    return band_rank(replays, band) / (len(replays) + 1)


def order_band(values, k):
    values = sorted(v for v in values if not math.isnan(v))
    if not values:
        return math.nan, math.nan
    k = min(k, len(values))
    return values[k - 1], values[-k]


def recorded_traits(recording, traits):
    """The traits this recording has columns for: a gene added after it was made would sit at its minimum in the
    record while the replays mutate it."""
    with (recording / 'lives.csv').open() as handle:
        header = set(next(csv.reader(handle)))
    parts = any(c.startswith('Part_') for c in header)
    return [t for t in traits if t in header or t == 'generation' or (parts and t.startswith('Parts_'))]


def summarise(traits, real, replays, band):
    k = band_rank(replays, band)
    rows = []
    for trait in traits:
        series = real['values'][trait]
        ends = [last_value(r['values'][trait]) for r in replays]
        values = [v for v in series if not math.isnan(v)] + [v for r in replays for v in r['values'][trait] if not math.isnan(v)]
        if not values or max(values) - min(values) < 1e-6:
            continue
        end = last_value(series)
        (low, high), median = order_band(ends, k), quantile(ends, 0.5)
        verdict = 'above' if end > high else 'below' if end < low else 'inside'
        start = next((v for v in series if not math.isnan(v)), math.nan)
        rows.append({'trait': trait, 'start': start, 'end': end, 'median': median, 'low': low, 'high': high,
                     'verdict': verdict})
    return rows


def binomial_tail(k, n, p):
    """P(X >= k) for X ~ Binomial(n, p)."""
    return sum(math.comb(n, i) * p ** i * (1 - p) ** (n - i) for i in range(k, n + 1))


def ordered(traits, rows):
    moving = {r['trait'] for r in rows}
    first = [t for t in HEADLINE if t in moving]
    return first + [t for t in traits if t in moving and t not in first and t != 'generation'] + \
        (['generation'] if 'generation' in moving else [])


def chart(path, title, traits, real, replays, band, year):
    k = band_rank(replays, band)
    columns, cell_w, cell_h, pad = 4, 230, 150, 34
    rows = (len(traits) + columns - 1) // columns
    width, height = columns * cell_w + 20, rows * cell_h + 60
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" '
           f'font-family="Helvetica, Arial, sans-serif" font-size="11">',
           f'<rect width="{width}" height="{height}" fill="#ffffff"/>',
           f'<text x="10" y="22" font-size="15" font-weight="bold">{escape(title)}</text>',
           f'<text x="10" y="40" fill="#555">Real run (blue) against {len(replays)} replays without selection: '
           f'light grey all but the {k} highest and {k} lowest, dark grey the middle half, dashed their median. '
           f'x in years.</text>']
    times = real['time']
    for index, trait in enumerate(traits):
        x0 = 10 + (index % columns) * cell_w
        y0 = 50 + (index // columns) * cell_h
        series = real['values'][trait]
        per_time = list(zip(*[r['values'][trait] for r in replays])) if replays else []
        lows = [order_band(v, k)[0] for v in per_time]
        highs = [order_band(v, k)[1] for v in per_time]
        q1 = [quantile(v, 0.25) for v in per_time]
        q3 = [quantile(v, 0.75) for v in per_time]
        medians = [quantile(v, 0.5) for v in per_time]
        values = [v for v in series + lows + highs if not math.isnan(v)]
        if not values:
            continue
        lo, hi = min(values), max(values)
        if hi - lo < 1e-9:
            lo, hi = lo - 0.5, hi + 0.5
        margin = 0.06 * (hi - lo)
        lo, hi = lo - margin, hi + margin
        plot_w, plot_h = cell_w - pad - 12, cell_h - pad - 14
        px0, py0 = x0 + pad, y0 + 16
        t_end = times[-1] if times and times[-1] > 0 else 1.0

        def X(t):
            return px0 + plot_w * t / t_end

        def Y(v):
            return py0 + plot_h * (1 - (v - lo) / (hi - lo))

        out.append(f'<text x="{x0 + 4}" y="{y0 + 10}" font-weight="bold">{escape(trait)}</text>')
        out.append(f'<rect x="{px0}" y="{py0}" width="{plot_w}" height="{plot_h}" fill="none" stroke="#ccc"/>')
        for band_low, band_high, fill in ((lows, highs, '#e3e3e3'), (q1, q3, '#c2c2c2')):
            points = [(X(t), Y(v)) for t, v in zip(times, band_low) if not math.isnan(v)]
            back = [(X(t), Y(v)) for t, v in zip(times, band_high) if not math.isnan(v)]
            if points and back:
                polygon = ' '.join(f'{x:.1f},{y:.1f}' for x, y in points + back[::-1])
                out.append(f'<polygon points="{polygon}" fill="{fill}" stroke="none"/>')
        out.append(polyline(times, medians, X, Y, '#777', 'stroke-dasharray="4 3"'))
        out.append(polyline(times, series, X, Y, '#1f5fa8', 'stroke-width="1.6"'))
        for value in (lo + margin, hi - margin):
            out.append(f'<text x="{px0 - 3}" y="{Y(value) + 3:.1f}" text-anchor="end" fill="#777" font-size="9">'
                       f'{format_value(value)}</text>')
        years = int(t_end // year)
        step = max(1, years // 5)
        for y in range(0, years + 1, step):
            out.append(f'<text x="{X(y * year):.1f}" y="{py0 + plot_h + 11}" text-anchor="middle" fill="#777" '
                       f'font-size="9">{y}</text>')
    out.append('</svg>')
    path.write_text('\n'.join(out))


def polyline(times, values, X, Y, colour, extra=''):
    points = ' '.join(f'{X(t):.1f},{Y(v):.1f}' for t, v in zip(times, values) if not math.isnan(v))
    return f'<polyline points="{points}" fill="none" stroke="{colour}" {extra}/>'


def format_value(value):
    return f'{value:.0f}' if abs(value) >= 100 else f'{value:.2f}' if abs(value) >= 1 else f'{value:.3f}'


def escape(text):
    return text.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('paths', nargs='*', type=Path)
    parser.add_argument('--replays', type=int, default=39,
                        help='Replays per run (default 39: with --band 0.9 the band leaves out the two highest and two '
                             'lowest, and a neutral trait falls outside it 10%% of the time)')
    parser.add_argument('--seed', type=int, default=1)
    parser.add_argument('--band', type=float, default=0.9,
                        help='Share of the time a neutral trait should end inside the band (default 0.9)')
    parser.add_argument('--mutation-chance', type=float)
    parser.add_argument('--mutation-magnitude', type=float)
    parser.add_argument('--perk-gain', type=float)
    parser.add_argument('--perk-loss', type=float)
    parser.add_argument('--harmful-chance', type=float)
    parser.add_argument('--age-window', type=float,
                        help='With --mode random, parents are picked within this share of the real parent\'s age')
    parser.add_argument('--mode', choices=('random', 'tree'), default='random',
                        help='random (default): pick every parent at random among animals of the real parent\'s age; '
                             'tree: keep the real family tree and draw only the mutations afresh (exact demography, '
                             'but finds less, since selection shaped the tree)')
    parser.add_argument('--quiet', action='store_true', help='Only the pooled table')
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()

    build()
    if args.self_test:
        sys.exit(subprocess.run(['dotnet', str(DLL), '--self-test']).returncode)
    if not args.paths:
        parser.error('give a run, a recording or a comparison folder')

    pooled = defaultdict(list)
    for path in args.paths:
        for recording in recordings_under(path):
            settings, note = run_settings(recording)
            target = replay(recording, args, settings)
            traits, real, replays = read_replays(target)
            rows = summarise(recorded_traits(recording, traits), real, replays, args.band)
            label = experiment(recording)
            pooled[label].append(rows)
            names = ordered(traits, rows)
            svg = recording.parent / 'neutral' / f'neutral-{args.mode}.svg'
            chart(svg, f'{recording.parent.name} ({label}), {MODES[args.mode]}', names, real, replays, args.band,
                  year_length(recording))
            if args.quiet:
                continue
            gen = next((r for r in rows if r['trait'] == 'generation'), None)
            print(f'\n{recording.parent.name} ({label}; {note}; {len(replays)} replays, {MODES[args.mode]}; '
                  f'a neutral trait ends outside the band {2 * side_chance(replays, args.band):.0%} of the time)')
            if gen:
                print(f'  generation at the end: real {gen["end"]:.1f}, replays {gen["median"]:.1f} '
                      f'({gen["low"]:.1f} to {gen["high"]:.1f})')
            print(f'  {"trait":<20}{"start":>9}{"end":>9}{"replays":>9}  {"band":<19} verdict')
            by_name = {r['trait']: r for r in rows}
            for name in names:
                r = by_name[name]
                if name == 'generation':
                    continue
                flag = '' if r['verdict'] == 'inside' else ('  <-- ' + r['verdict'])
                print(f'  {name:<20}{format_value(r["start"]):>9}{format_value(r["end"]):>9}'
                      f'{format_value(r["median"]):>9}  {format_value(r["low"]):>8} to {format_value(r["high"]):<8}'
                      f'{flag}')
            print(f'  chart: {svg}')

    side = side_chance(range(args.replays), args.band)
    for label, runs in pooled.items():
        counts = defaultdict(lambda: {'above': 0, 'below': 0, 'inside': 0, 'shift': []})
        for rows in runs:
            for r in rows:
                c = counts[r['trait']]
                c[r['verdict']] += 1
                width = (r['high'] - r['low']) / 2 or 1e-9
                c['shift'].append((r['end'] - r['median']) / width)
        n = len(runs)
        print(f'\n{label}: {n} runs pooled. Chance alone puts {side:.1%} of runs above the band and {side:.1%} below.')
        print(f'  {"trait":<20}{"above":>6}{"below":>6}{"inside":>7}  {"shift":>6}  verdict')
        print('  (shift: the real end less the replays\' median, in half-widths of the band; median over runs)')
        names = [t for t in HEADLINE if t in counts] + sorted(t for t in counts if t not in HEADLINE)
        for name in names:
            c = counts[name]
            runs_counted = c['above'] + c['below'] + c['inside']
            p_up = binomial_tail(c['above'], runs_counted, side)
            p_down = binomial_tail(c['below'], runs_counted, side)
            verdict = ''
            if min(p_up, p_down) < 0.01:
                verdict = ('higher' if p_up < p_down else 'lower') + f' than chance (p {min(p_up, p_down):.4f})'
            if name in MARKERS and verdict:
                verdict += '  [a marker: the replay may not be fair]'
            shift = quantile(c['shift'], 0.5)
            print(f'  {name:<20}{c["above"]:>6}{c["below"]:>6}{c["inside"]:>7}  {shift:>+6.2f}  {verdict}')

    # The fairness check: markers do nothing, so they should end outside the band as often as chance says.
    outside = total = 0
    for runs in pooled.values():
        for rows in runs:
            for r in rows:
                if r['trait'] in MARKERS:
                    total += 1
                    outside += r['verdict'] != 'inside'
    if total:
        expected = 2 * side * total
        p = binomial_tail(outside, total, 2 * side)
        print(f'\nMarkers outside the band: {outside} of {total} ({outside / total:.0%}); chance alone gives about '
              f'{expected:.1f} ({2 * side:.0%}). ' + ('Fair.' if p >= 0.01 else f'Too many (p {p:.4f}): the band is too narrow.'))
        low_tail = sum(math.comb(total, i) * (2 * side) ** i * (1 - 2 * side) ** (total - i) for i in range(0, outside + 1))
        if low_tail < 0.01:
            print(f'Too few markers outside (p {low_tail:.4f}): the band is too wide.')


if __name__ == '__main__':
    main()
