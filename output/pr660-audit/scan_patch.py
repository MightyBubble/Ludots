#!/usr/bin/env python3
"""Scan a unified diff's ADDED lines for Ludots architecture-rule risk patterns.
Every added line is checked. Context lines are ignored (they are old code).
"""
import re, sys, collections

PATTERNS = [
    ("fallback-word", re.compile(r'fallback|FallBack|Fall back', re.I)),
    ("legacy-word", re.compile(r'\bLegacy\b|\bBackwardCompat|\bcompat\b', re.I)),
    ("null-coalesce-new", re.compile(r'\?\?\s*new\s')),
    ("empty-catch", re.compile(r'catch\s*(\([^)]*\))?\s*\{\s*\}')),
    ("catch-swallow", re.compile(r'catch\b')),
    ("todo-hack", re.compile(r'TODO|FIXME|HACK|XXX')),
    ("float-gameplay", re.compile(r'\bfloat\b|\bdouble\b')),
    ("array-resize", re.compile(r'Array\.Resize')),
    ("dict-enum", re.compile(r'foreach.*\b(Dictionary|HashSet)\b|\.Values\.|\.Keys\.')),
    ("visualtransform", re.compile(r'VisualTransform')),
    ("world-add-remove", re.compile(r'World\.(Add|Remove)<')),
    ("new-querydesc", re.compile(r'new\s+QueryDescription')),
    ("datetime-now", re.compile(r'DateTime\.(Now|UtcNow)|Random\.Shared|new\s+Random')),
    ("thread-task", re.compile(r'\bThread\.|Task\.Run|async\s')),
    ("gethashcode", re.compile(r'GetHashCode\(\)')),
    ("silent-default", re.compile(r'\?\?\s*(0|1|false|true|-1|default)\b')),
    ("string-compare", re.compile(r'==\s*"')),
    ("capacity-magic", re.compile(r'=\s*(256|512|1024|2048|4096|8192|16384)\s*;')),
]

def scan(path):
    cur_file = None
    hits = collections.defaultdict(list)
    added_total = 0
    with open(path, encoding='utf-8', errors='replace') as f:
        lineno = 0
        for line in f:
            lineno += 1
            line = line.rstrip('\n')
            if line.startswith('diff --git'):
                cur_file = line.split(' b/')[-1]
            elif line.startswith('+') and not line.startswith('+++'):
                added_total += 1
                text = line[1:]
                if text.strip().startswith('//'):
                    continue  # comments still scanned for todo only
                for name, pat in PATTERNS:
                    if pat.search(text):
                        hits[name].append((cur_file, lineno, text.strip()[:160]))
    return added_total, hits

for path in sys.argv[1:]:
    total, hits = scan(path)
    print(f'### {path}: added_lines={total}')
    for name, _ in PATTERNS:
        lst = hits.get(name, [])
        if not lst:
            continue
        print(f'  [{name}] {len(lst)} hits')
        for f, ln, t in lst[:25]:
            print(f'    {f}:{ln}: {t}')
        if len(lst) > 25:
            print(f'    ... and {len(lst)-25} more')
