#!/usr/bin/env python3
"""Draw a run's family lines as a Muller plot and export them for ggmuller (backlog item 65).

Usage: Tools/muller_plot.py <run folder, recording folder or folder of runs> [...] [options]
       Tools/muller_plot.py --self-test

Reads each recording's lives.csv (schema 8 or later): every animal's ID, first parent, birth, end and perks. A family
line is a founder's descendants, or a branch inside a line that grew big: one whose descendants alive were at least
--share of all the animals at once, while the line it grew in had at least --outside of all the animals outside it at
some time it lived (so a branch that is just its whole line again isn't a line). A branch that began with a perk gain
is a line of its own once it reaches --perk-share, drawn striped. Lines follow first parents. Shares count only at
samples with at least --minimum animals alive. These are the same rules as the in-game view (FamilyLineChart, M key).

Writes into the recording folder (or --out):
  muller_edges.csv        Parent,Identity for ggmuller, with a root "0" above the founders' lines
  muller_populations.csv  Generation (simulated years),Identity,Population: each line's own animals (not counting the
                          lines inside it) at each sample, as ggmuller's get_Muller_df(edges, pop_df) reads them
  muller_lines.csv        each line's parent, first animal, birth, peak, share at the end and perk shares at its peak
  muller.svg              the Muller plot: each line's share of the animals over time, nested in its parent's band,
                          with winters marked under the time axis
and prints each line, and when a branch line first held half its parent line and half of all the animals.
Pure Python, no packages.
"""
import argparse
import colorsys
import csv
import math
import sys
from pathlib import Path

YEAR = 400.0
INF = float('inf')


class Life:
    __slots__ = ('id', 'parent', 'born', 'ended', 'perks')

    def __init__(self, id, parent, born, ended, perks):
        self.id, self.parent, self.born, self.ended, self.perks = id, parent, born, ended, perks

    def alive(self, t):
        return self.born <= t < self.ended


class Line:
    def __init__(self, root, founder, perk):
        self.root = root
        self.founder = founder
        self.perk = perk
        self.name = ('F' if founder else 'L') + str(root.id)
        self.parent = -1
        self.children = []
        self.depth = 0
        self.clade = None
        self.own = None
        self.peak = 0.0
        self.peak_sample = 0
        self.perk_shares = []


def build(lives, times, share=0.1, outside=0.1, minimum=20, max_lines=24, perk_mask=-1, perk_share=0.03, max_perk_lines=8,
          perk_count=0):
    """The same rules as FamilyLineChart.Build. Returns (lines in drawing order, totals per sample, share used)."""
    n, samples = len(lives), len(times)
    by_id = {life.id: i for i, life in enumerate(lives)}
    parent = [by_id.get(life.parent, -1) if life.parent > 0 else -1 for life in lives]
    parent = [p if p != i else -1 for i, p in enumerate(parent)]
    kids = [[] for _ in range(n)]
    roots = []
    for i in range(n):
        (roots if parent[i] < 0 else kids[parent[i]]).append(i)
    roots.sort(key=lambda i: lives[i].id)
    order, enter, leave = [], [0] * n, [0] * n
    for root in roots:
        stack = [(root, 0)]
        enter[root] = len(order)
        order.append(root)
        while stack:
            node, child = stack.pop()
            if child < len(kids[node]):
                stack.append((node, child + 1))
                kid = kids[node][child]
                enter[kid] = len(order)
                order.append(kid)
                stack.append((kid, 0))
            else:
                leave[node] = len(order)

    def prefix(t):
        alive = [0] * (n + 1)
        running = 0
        for k, i in enumerate(order):
            if lives[i].born <= t < lives[i].ended:
                running += 1
            alive[k + 1] = running
        return alive

    totals, valid, peak = [0] * samples, [False] * samples, [0.0] * n
    prefixes = []
    for s, t in enumerate(times):
        alive = prefix(t)
        prefixes.append(alive)
        totals[s] = alive[n]
        valid[s] = alive[n] >= max(1, minimum)
        if not valid[s]:
            continue
        total = float(alive[n])
        for i in range(n):
            c = alive[leave[i]] - alive[enter[i]]
            if c and c / total > peak[i]:
                peak[i] = c / total
    lowest = min(share, perk_share)
    candidates = [i for i in order if parent[i] < 0 or peak[i] >= lowest]
    series = {i: [prefixes[s][leave[i]] - prefixes[s][enter[i]] for s in range(samples)] for i in candidates}
    del prefixes

    def nearest(i, promoted):
        above = parent[i]
        while above >= 0:
            if above in promoted:
                return above
            above = parent[above]
        return -1

    def promote(needed):
        promoted, branches, perk_lines = {}, 0, []
        for i in candidates:
            if parent[i] < 0:
                promoted[i] = -1
                continue
            gained = lives[i].perks & ~lives[parent[i]].perks & perk_mask
            if gained and peak[i] >= perk_share:
                perk_lines.append((i, peak[i], (gained & -gained).bit_length() - 1))
                continue
            if peak[i] < needed:
                continue
            above = nearest(i, promoted)
            if above < 0:
                continue
            mine, theirs = series[i], series[above]
            if any(valid[s] and mine[s] > 0 and (theirs[s] - mine[s]) / totals[s] >= outside for s in range(samples)):
                promoted[i] = -1
                branches += 1
        perk_lines.sort(key=lambda entry: -entry[1])
        for i, _, perk in perk_lines[:max_perk_lines]:
            promoted[i] = perk
        return promoted, branches

    needed = share
    for _ in range(40):
        promoted, branches = promote(needed)
        if branches <= max_lines:
            break
        needed *= 1.2

    lines, line_of = [], {}
    for i in candidates:
        if i not in promoted:
            continue
        line = Line(lives[i], parent[i] < 0, promoted[i])
        line.clade = series[i]
        line.own = list(series[i])
        line.peak = peak[i]
        above = nearest(i, promoted)
        if above >= 0:
            line.parent = line_of[above]
            parent_line = lines[line.parent]
            line.depth = parent_line.depth + 1
            parent_line.children.append(len(lines))
            parent_line.own = [a - b for a, b in zip(parent_line.own, line.clade)]
        line_of[i] = len(lines)
        lines.append(line)
    for i, index in line_of.items():
        line = lines[index]
        best = max(range(samples), key=lambda s: (line.clade[s], -s))
        line.peak_sample = best
        carriers, with_perk = 0, [0] * perk_count
        for k in range(enter[i], leave[i]):
            life = lives[order[k]]
            if life.alive(times[best]):
                carriers += 1
                for p in range(perk_count):
                    if life.perks & (1 << p):
                        with_perk[p] += 1
        line.perk_shares = [c / carriers if carriers else 0.0 for c in with_perk]
    return lines, totals, needed


def bands(lines, totals, s):
    """Each line's (lower, upper) share at sample s, its own animals split half below and half above its sub-lines."""
    lower, upper = [0.0] * len(lines), [0.0] * len(lines)
    total = totals[s]

    def place(i, y):
        line = lines[i]
        height = line.clade[s] / total if total else 0.0
        lower[i], upper[i] = y, y + height
        inner = y + (max(0, line.own[s]) * 0.5 / total if total else 0.0)
        for child in line.children:
            inner = place(child, inner)
        return y + height

    y = 0.0
    for i, line in enumerate(lines):
        if line.parent < 0:
            y = place(i, y)
    return lower, upper


def colours(lines):
    """Founders' lines well-separated hues; a branch shades gently away from its parent's colour (hue turned a little
    per sibling, a little lighter or darker by depth), so a family reads as one family and a chain as one band."""
    hues, drift = [0.0] * len(lines), [0.0] * len(lines)
    sats, vals, result, founders = [0.0] * len(lines), [0.0] * len(lines), [], 0
    for i, line in enumerate(lines):
        if line.parent < 0:
            hues[i], sats[i], vals[i] = (0.58 + founders * 0.381966) % 1.0, 0.55, 0.8
            founders += 1
        else:
            p = line.parent
            sibling = lines[p].children.index(i)
            # The turn from the founder's hue fades with depth, so a family never wanders more than about 0.12 away.
            drift[i] = drift[p] * 0.6 + (sibling + 1) * 0.05 * (1 if sibling % 2 == 0 else -1)
            founder = p
            while lines[founder].parent >= 0:
                founder = lines[founder].parent
            hues[i] = (hues[founder] + drift[i]) % 1.0
            sats[i] = min(0.8, max(0.3, sats[p] + (0.06 if line.depth % 3 == 1 else -0.04)))
            vals[i] = min(0.95, max(0.5, vals[p] + (0.07 if line.depth % 3 != 2 else -0.12)))
        rgb = colorsys.hsv_to_rgb(hues[i], sats[i], vals[i])
        result.append('#%02x%02x%02x' % tuple(int(round(c * 255)) for c in rgb))
    return result


def took_over(lines, i, last):
    """Whether line i had become (nearly) all of its parent line by the last sample the parent line had animals: its
    parent's only branch line, with the parent keeping under 5% of its animals outside it. Such a line is shown with
    its parent, as one chain."""
    line = lines[i]
    if line.parent < 0:
        return False
    parent = lines[line.parent]
    alive = next((s for s in range(last, -1, -1) if parent.clade[s] > 0), None)
    return len(parent.children) == 1 and alive is not None and line.clade[alive] >= 0.95 * parent.clade[alive]


def chains(lines, last):
    """Lines grouped into chains of successive takeovers, each chain from its outermost line down."""
    groups = []
    for i in range(len(lines)):
        if took_over(lines, i, last):
            continue
        group = [i]
        while len(lines[group[-1]].children) == 1 and took_over(lines, lines[group[-1]].children[0], last):
            group.append(lines[group[-1]].children[0])
        groups.append(group)
    return groups


def read_recording(recording):
    lives, perk_names = [], []
    with (recording / 'lives.csv').open() as handle:
        reader = csv.DictReader(handle)
        perk_names = [name[5:] for name in reader.fieldnames if name.startswith('Perk_')]
        for row in reader:
            perks = 0
            for p, name in enumerate(perk_names):
                if row['Perk_' + name] == '1':
                    perks |= 1 << p
            lives.append(Life(int(row['id']), int(row['first_parent']) if row['first_parent'] else 0, float(row['born']),
                              float(row['ended']) if row['ended'] else INF, perks))
    population = list(csv.DictReader((recording / 'population.csv').open()))
    return lives, perk_names, population


def season_at(population, t):
    best = None
    for row in population:
        if float(row['time']) <= t + 1e-6:
            best = row
        else:
            break
    return (best or population[0])['season'].split()[-1].lower() if population else ''


def write_outputs(out, lines, totals, times, perk_names, population, title):
    out.mkdir(parents=True, exist_ok=True)
    with (out / 'muller_edges.csv').open('w', newline='') as handle:
        writer = csv.writer(handle)
        writer.writerow(['Parent', 'Identity'])
        for line in lines:
            writer.writerow(['0' if line.parent < 0 else lines[line.parent].name, line.name])
    with (out / 'muller_populations.csv').open('w', newline='') as handle:
        writer = csv.writer(handle)
        writer.writerow(['Generation', 'Identity', 'Population'])
        for s, t in enumerate(times):
            year = round(t / YEAR, 4)
            writer.writerow([year, '0', 0])
            for line in lines:
                writer.writerow([year, line.name, max(0, line.own[s])])
    last = len(times) - 1
    with (out / 'muller_lines.csv').open('w', newline='') as handle:
        writer = csv.writer(handle)
        writer.writerow(['line', 'parent', 'first_animal', 'born', 'began_with_perk', 'peak_share', 'peak_time',
                         'share_at_end'] + ['Perk_' + name for name in perk_names])
        for line in lines:
            writer.writerow([line.name, lines[line.parent].name if line.parent >= 0 else '', line.root.id,
                             round(line.root.born, 3), perk_names[line.perk] if line.perk >= 0 else '',
                             round(line.peak, 4), round(times[line.peak_sample], 1),
                             round(line.clade[last] / totals[last], 4) if totals[last] else 0]
                            + [round(v, 4) for v in line.perk_shares])
    svg(out / 'muller.svg', lines, totals, times, perk_names, population, title)


def svg(path, lines, totals, times, perk_names, population, title):
    width, height, left, top, right = 1200, 420, 60, 46, 20
    plot_w = width - left - right
    groups = sorted(chains(lines, len(times) - 1), key=lambda g: -lines[g[0]].peak)[:16]
    total_h = top + height + 64 + 18 * len(groups) + 10
    end = times[-1] if times[-1] > 0 else 1.0
    x = lambda t: left + plot_w * t / end
    y = lambda share: top + height * (1.0 - share)
    fills = colours(lines)
    all_bands = [bands(lines, totals, s) if totals[s] else None for s in range(len(times))]
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{total_h}" '
           f'font-family="Helvetica, Arial, sans-serif" font-size="12">',
           '<rect width="100%" height="100%" fill="#fbfcfa"/>',
           f'<text x="{left}" y="22" font-size="16" font-weight="bold" fill="#1b2420">{escape(title)}</text>',
           f'<text x="{left}" y="38" fill="#5a6962">Each band is a family line\'s share of the animals, nested in the line '
           f'it grew from; striped bands began with a perk gain. Lines follow first parents.</text>', '<defs>']
    for i, line in enumerate(lines):
        if line.perk >= 0:
            out.append(f'<pattern id="stripes{i}" width="8" height="8" patternUnits="userSpaceOnUse" '
                       f'patternTransform="rotate(45)"><rect width="8" height="8" fill="{fills[i]}"/>'
                       f'<rect width="4" height="8" fill="#000" fill-opacity="0.32"/></pattern>')
    out.append('</defs>')
    out.append(f'<rect x="{left}" y="{top}" width="{plot_w}" height="{height}" fill="#1c211e"/>')
    for i, line in enumerate(lines):
        upper_points, lower_points = [], []
        for s, t in enumerate(times):
            if all_bands[s] is None:
                continue
            lower, upper = all_bands[s]
            upper_points.append(f'{x(t):.1f},{y(upper[i]):.1f}')
            lower_points.append(f'{x(t):.1f},{y(lower[i]):.1f}')
        if not upper_points or max(line.clade) == 0:
            continue
        fill = f'url(#stripes{i})' if line.perk >= 0 else fills[i]
        out.append(f'<polygon points="{" ".join(upper_points + lower_points[::-1])}" fill="{fill}"/>')
    # Labels on the bigger lines, where each is widest; a chain's lines share one label, on its outermost line.
    inner = {i for group in chains(lines, len(times) - 1) for i in group[1:]}
    for i, line in enumerate(lines):
        s = line.peak_sample
        if i in inner or all_bands[s] is None or line.clade[s] / max(1, totals[s]) < 0.06:
            continue
        lower, upper = all_bands[s]
        label = line.name + (f' {perk_names[line.perk]}' if line.perk >= 0 else '')
        tx = min(max(x(times[s]), left + 30), left + plot_w - 30)
        out.append(f'<text x="{tx:.1f}" y="{y((lower[i] + upper[i]) / 2) + 4:.1f}" text-anchor="middle" fill="#fff" '
                   f'stroke="#000" stroke-width="2.4" paint-order="stroke" font-size="11">{escape(label)}</text>')
    # Winters as a strip under the time axis, and a tick each year.
    strip = top + height + 4
    for s in range(len(times) - 1):
        if season_at(population, times[s]) == 'winter':
            out.append(f'<rect x="{x(times[s]):.1f}" y="{strip}" width="{x(times[s + 1]) - x(times[s]):.1f}" height="6" '
                       f'fill="#9bb6d0"/>')
    years = int(end // YEAR)
    step = 1 if years <= 30 else 5
    for year in range(0, years + 1, step):
        tx = x(year * YEAR)
        out.append(f'<line x1="{tx:.1f}" y1="{top}" x2="{tx:.1f}" y2="{top + height + 12}" stroke="#ffffff" '
                   f'stroke-opacity="0.25"/>')
        out.append(f'<text x="{tx:.1f}" y="{top + height + 26}" text-anchor="middle" fill="#5a6962">y{year + 1}</text>')
    out.append(f'<text x="{left - 8}" y="{top + 10}" text-anchor="end" fill="#5a6962">100%</text>')
    out.append(f'<text x="{left - 8}" y="{top + height}" text-anchor="end" fill="#5a6962">0</text>')
    out.append(f'<text x="{left + plot_w}" y="{top + height + 26}" text-anchor="end" fill="#5a6962">'
               f'winters in blue under the axis</text>')
    row = top + height + 52
    last = len(times) - 1
    for k, group in enumerate(groups):
        i = group[0]
        line = lines[i]
        fill = f'url(#stripes{i})' if line.perk >= 0 else fills[i]
        origin = 'founder' if line.founder else f'from {lines[line.parent].name}, born year {line.root.born / YEAR + 1:.1f}'
        began = f', began with {perk_names[line.perk]}' if line.perk >= 0 else ''
        carried = ''.join(f', {perk_names[p]} {v:.0%}' for p, v in enumerate(line.perk_shares) if v >= 0.5)
        now = line.clade[last] / totals[last] if totals[last] else 0.0
        names = ' → '.join(lines[j].name for j in group)
        chain = f' (each took over the one before, the last born in year {lines[group[-1]].root.born / YEAR + 1:.1f})' \
            if len(group) > 1 else ''
        peak = f'peak {line.peak:.0%} in year {times[line.peak_sample] / YEAR + 1:.1f}' if line.peak > 0 \
            else 'died out while the animals were few'
        text = f'{names} ({origin}{began}): {peak}, {now:.0%} at the end{carried}{chain}'
        out.append(f'<rect x="{left}" y="{row + 18 * k - 10}" width="12" height="12" fill="{fill}"/>')
        out.append(f'<text x="{left + 18}" y="{row + 18 * k}" fill="#1b2420">{escape(text)}</text>')
    out.append('</svg>')
    path.write_text('\n'.join(out))


def escape(text):
    return text.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')


def report(name, lines, totals, times, perk_names):
    last = len(times) - 1
    print(f'\n{name}: {len(lines)} lines ({sum(1 for l in lines if l.founder)} founders\' lines, '
          f'{sum(1 for l in lines if l.perk >= 0)} begun by a perk gain), {totals[last]} animals at the end')
    print(f'  {"line":<9}{"from":<9}{"born":>7}{"peak":>7}{"peak at":>9}{"at end":>8}  perks carried at its peak')
    for line in lines:
        origin = lines[line.parent].name if line.parent >= 0 else '-'
        now = line.clade[last] / totals[last] if totals[last] else 0.0
        carried = ', '.join(f'{perk_names[p]} {v:.0%}' for p, v in enumerate(line.perk_shares) if v >= 0.2)
        began = f' (began with {perk_names[line.perk]})' if line.perk >= 0 else ''
        print(f'  {line.name:<9}{origin:<9}{line.root.born / YEAR + 1:>7.2f}{line.peak:>7.0%}'
              f'{times[line.peak_sample] / YEAR + 1:>9.2f}{now:>8.0%}  {carried}{began}')
    # When each branch first held half of its parent line, and half of all the animals.
    for line in lines:
        if line.parent < 0:
            continue
        parent = lines[line.parent]
        half_parent = next((s for s in range(len(times)) if parent.clade[s] and line.clade[s] * 2 >= parent.clade[s]
                            and totals[s] >= 20), None)
        half_all = next((s for s in range(len(times)) if totals[s] >= 20 and line.clade[s] * 2 >= totals[s]), None)
        if half_parent is not None or half_all is not None:
            parts = []
            if half_parent is not None:
                parts.append(f'half of {parent.name} in year {times[half_parent] / YEAR + 1:.1f}')
            if half_all is not None:
                parts.append(f'half of all animals in year {times[half_all] / YEAR + 1:.1f}')
            print(f'  {line.name} held ' + ' and '.join(parts))


def self_test():
    """The made-up family of FamilyLinesTests, with the same expected answer."""
    lives = [Life(1, 0, -10, 300, 0), Life(2, 0, -10, 200, 0)]
    lives += [Life(i, 1, 10, 300, 0) for i in range(10, 40)]
    lives += [Life(50, 1, 250, 400, 0)] + [Life(i, 50, 300, 600, 0) for i in range(51, 76)]
    lives += [Life(100, 1, 200, 400, 0)] + [Life(101 + k, 100, 250 + 10 * k, INF, 0) for k in range(60)]
    lives += [Life(500, 100, 600, INF, 1)] + [Life(i, 500, 650, INF, 1) for i in range(501, 511)]
    lives += [Life(i, 2, 10, 500, 0) for i in range(200, 220)]
    times = [50.0 * s for s in range(21)]
    lines, totals, _ = build(lives, times, perk_count=1)
    names = [line.name for line in lines]
    assert names == ['F1', 'L50', 'L100', 'L500', 'F2'], names
    assert [line.parent for line in lines] == [-1, 0, 0, 2, -1]
    assert lines[3].perk == 0 and lines[2].perk == -1 and lines[3].perk_shares == [1.0]
    assert totals[-1] == 71 and lines[2].clade[-1] == 71 and lines[4].clade[-1] == 0 and lines[1].clade[-1] == 0
    for s in range(len(times)):
        assert totals[s] == lines[0].clade[s] + lines[4].clade[s]
        assert lines[0].clade[s] == lines[0].own[s] + lines[1].clade[s] + lines[2].clade[s]
        if totals[s]:
            lower, upper = bands(lines, totals, s)
            assert abs(upper[4] - 1.0) < 1e-9
            for i, line in enumerate(lines):
                if line.parent >= 0:
                    assert lower[i] >= lower[line.parent] - 1e-9 and upper[i] <= upper[line.parent] + 1e-9
    capped, _, used = build(lives, times, max_lines=1, perk_count=1)
    assert 'L100' in [l.name for l in capped] and 'L50' not in [l.name for l in capped] and used > 0.45
    plain, _, _ = build(lives, times, perk_mask=0, perk_count=1)
    assert [l.name for l in plain] == ['F1', 'L50', 'L100', 'L500', 'F2'] and plain[3].perk == -1
    higher, _, _ = build(lives, times, share=0.25, perk_mask=0, perk_count=1)
    assert [l.name for l in higher] == ['F1', 'L50', 'L100', 'F2']
    print('self-test passed: the made-up family gives F1, L50, L100, L500 (perk) and F2, nested and adding up')


def recordings_under(path):
    if (path / 'lives.csv').is_file():
        return [path]
    if (path / 'recording' / 'lives.csv').is_file():
        return [path / 'recording']
    return sorted(p / 'recording' for p in path.iterdir() if (p / 'recording' / 'lives.csv').is_file())


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('paths', nargs='*', type=Path)
    parser.add_argument('--share', type=float, default=0.1, help='share of all animals a branch must reach (0.1)')
    parser.add_argument('--outside', type=float, default=0.1,
                        help='share of all animals its parent line must have had outside it (0.1)')
    parser.add_argument('--minimum', type=int, default=20, help='animals alive for a sample to count (20)')
    parser.add_argument('--max-lines', type=int, default=24, help='most branch lines; --share is raised to fit (24)')
    parser.add_argument('--perks', default='all',
                        help='perks whose gains start a line, comma-separated (e.g. ThickFur,Camouflage), all or none')
    parser.add_argument('--perk-share', type=float, default=0.03, help='share a perk line must reach (0.03)')
    parser.add_argument('--step', type=float, default=YEAR / 10, help='simulated seconds between samples (40)')
    parser.add_argument('--out', type=Path, help='folder for the outputs; defaults to each recording folder')
    parser.add_argument('--self-test', action='store_true', help='check the rules on a made-up family and stop')
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    recordings = [r for p in (args.paths or [Path('Logs/SoakRuns')]) for r in recordings_under(p)]
    if not recordings:
        sys.exit('no recordings with lives.csv found')
    for recording in recordings:
        lives, perk_names, population = read_recording(recording)
        if args.perks == 'all':
            mask = (1 << len(perk_names)) - 1
        elif args.perks == 'none':
            mask = 0
        else:
            wanted = {name.strip() for name in args.perks.split(',')}
            mask = sum(1 << p for p, name in enumerate(perk_names) if name in wanted)
        end = float(population[-1]['time']) if population else max(l.born for l in lives)
        steps = max(2, int(math.ceil(end / args.step)) + 1)
        times = [end * s / (steps - 1) for s in range(steps)]
        lines, totals, used = build(lives, times, args.share, args.outside, args.minimum, args.max_lines, mask,
                                    args.perk_share, perk_count=len(perk_names))
        run = recording.parent.name if recording.name == 'recording' else recording.name
        out = args.out / run if args.out and len(recordings) > 1 else (args.out or recording)
        metadata = recording / 'metadata.json'
        title = run
        if metadata.is_file():
            import json
            meta = json.loads(metadata.read_text())
            title = f'{run} · {meta.get("worldSeed", "")} · {meta.get("experiment", "")}'
        write_outputs(out, lines, totals, times, perk_names, population, title)
        report(run, lines, totals, times, perk_names)
        if used > args.share:
            print(f'  (--share raised to {used:.3f} to keep within {args.max_lines} branch lines)')
        print(f'  wrote {out / "muller.svg"} and the ggmuller tables beside it')


if __name__ == '__main__':
    main()
