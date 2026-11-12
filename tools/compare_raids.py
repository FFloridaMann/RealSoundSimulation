"""Compare two raids' logs side by side (docs/TEST-ROUTE.md).

    python tools/compare_raids.py <reference raid folder> <new raid folder>

Folders can be full paths or just the raid folder name inside "G:/SPT/RealSoundSimulation Logs".
Reads summary.txt (and marks.csv, errors.txt) and prints the numbers that matter for a regression check.
"""
import os
import re
import sys

LOGS = 'G:/SPT/RealSoundSimulation Logs'

# (label, line prefix in summary.txt, regex with one group)
METRICS = [
    ('raid length min', 'Raid summary', r'duration ([\d.]+) min'),
    ('audio thread late >1.5x %', '  late >1.5x', r'late >1\.5x block \d+ \(([\d.]+)%\)'),
    ('audio thread stalls >100 ms', '  late >1.5x', r'stalls >100 ms (\d+)'),
    ('mod CPU ms per second', '  total ', r'total ([\d.]+) ms per second'),
    ('mod CPU % of one core', '  total ', r'\(([\d.]+)% of one CPU core\)'),
    ('allocated KB/s', '  total ', r'([\d.]+) KB/s allocated'),
    ('slow frames logged', '  slow frames logged', r'\): (\d+)'),
    ('own shots blended (3c)', '  fix 1 held back', r'fix 3c blended (\d+)'),
    ('own echoes played', '  samples echoed', r'echoes played (\d+)'),
    ('echo voices max busy', '  voices: max busy', r'max busy (\d+) of'),
    ('echo voices stolen', '  voices: max busy', r'stolen (\d+)'),
    ('dropped by headroom cap', '  voices: max busy', r'headroom cap (\d+)'),
    ('room tails played', 'ROOM TAIL', r'shots with a tail (\d+)'),
    ('room tail mean reverb s', 'ROOM TAIL', r'mean reverb time ([\d.]+) s'),
    ('bot gun sounds queued', '  sounds queued', r'sounds queued (\d+)'),
    ('bot sounds cut %', '  cut off by the same gun', r'\((\d+)% of queued\)'),
    ('bot tails rang out (1f)', '  of those, rang out', r'\(1f ON\): (\d+)'),
    ('bot echoes played', 'BOT ECHOES', r'echoes played (\d+)'),
    ('through-wall thumps (gun)', 'THROUGH-WALL THUMP', r'played (\d+) gun'),
    ('explosions', 'EXPLOSION ECHOES', r'explosions (\d+)'),
    ('explosion echoes played', 'EXPLOSION ECHOES', r'echoes played (\d+)'),
    ('shock wave hits', 'SHOCK WAVE', r'hits (\d+)'),
    ('concussed', 'CONCUSSION SOUND CUT', r'concussed (\d+)'),
    ('cracks before the cut', 'CONCUSSION SOUND CUT', r'\) (\d+), other writers'),
    ('game ring held (frames)', 'CONCUSSION SOUND CUT', r'game ring held (\d+) frames'),
    ('adrenaline blasts', 'ADRENALINE', r'close blasts (\d+)'),
    ('adrenaline peak', 'ADRENALINE', r'peak stress ([\d.]+)'),
    ('rain clip picks', 'RAIN (fix 7)', r'clip picks (\d+)'),
    ('opening sound voices max', 'AMBIENCE MIX', r'opening sound voices max (\d+)'),
    ('outdoor tails from opening', 'OUTDOOR TAIL FROM THE OPENING', r'played (\d+)'),
]


def folder(arg):
    return arg if os.path.isdir(arg) else os.path.join(LOGS, arg)


def read(path):
    s = os.path.join(path, 'summary.txt')
    if not os.path.exists(s):
        return None
    return open(s, encoding='utf-8', errors='replace').read().split('\n')


def value(lines, prefix, rx):
    for line in lines:
        if line.startswith(prefix):
            m = re.search(rx, line)
            if m:
                return m.group(1)
    return '-'


def extras(path):
    marks = os.path.join(path, 'marks.csv')
    n = max(0, sum(1 for _ in open(marks, encoding='utf-8', errors='replace')) - 1) if os.path.exists(marks) else 0
    err = os.path.join(path, 'errors.txt')
    e = sum(1 for l in open(err, encoding='utf-8', errors='replace') if l.strip()) if os.path.exists(err) else 0
    return n, e


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(1)
    a, b = folder(sys.argv[1]), folder(sys.argv[2])
    la, lb = read(a), read(b)
    for p, l in ((a, la), (b, lb)):
        if l is None:
            sys.exit(f'no summary.txt in {p} (is the raid finished?)')
    w = max(len(m[0]) for m in METRICS) + 2
    print(f'{"":{w}}{"reference":>14}{"new":>14}')
    print(f'{"":{w}}{os.path.basename(a)[-14:]:>14}{os.path.basename(b)[-14:]:>14}')
    for label, prefix, rx in METRICS:
        va, vb = value(la, prefix, rx), value(lb, prefix, rx)
        flag = ''
        try:
            fa, fb = float(va), float(vb)
            if fa != fb and max(abs(fa), abs(fb)) > 0 and abs(fa - fb) / max(abs(fa), abs(fb)) > 0.5:
                flag = '  <- big change'
        except ValueError:
            pass
        print(f'{label:{w}}{va:>14}{vb:>14}{flag}')
    (ma, ea), (mb, eb) = extras(a), extras(b)
    print(f'{"F10 marks":{w}}{ma:>14}{mb:>14}')
    print(f'{"errors.txt lines":{w}}{ea:>14}{eb:>14}{"  <- errors" if eb else ""}')


if __name__ == '__main__':
    main()
