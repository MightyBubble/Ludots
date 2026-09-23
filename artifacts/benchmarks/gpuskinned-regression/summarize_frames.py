import argparse
import datetime
import json
import math
import re
import statistics
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('log', type=Path)
parser.add_argument('--warmup', type=int, default=180)
args = parser.parse_args()
rows = []
frame = None
for line in args.log.read_text(encoding='utf-8-sig').splitlines():
    sample = re.search(r'\] sample frame=(\d+)', line)
    if sample:
        frame = int(sample[1])
    if frame is None or '] timing frame=' not in line:
        continue
    fields = dict(re.findall(r'(\w+)=(\S+)', line))
    fields['index'] = frame
    fields['timestamp'] = line[1:line.index(']')]
    frame = None
    rows.append(fields)
rows = [r for r in rows if r['index'] > args.warmup]
if not rows:
    raise ValueError('No post-warmup frame samples')

def summarize(values):
    values = sorted(values)
    return {'mean': statistics.mean(values), 'median': statistics.median(values),
            'p95': values[math.ceil(len(values) * .95) - 1], 'max': values[-1]}

def metric(key, subset=rows):
    return summarize([float(r[key].removesuffix('ms')) for r in subset])

keys = ['frame', 'sim', 'presentation', 'transformSync', 'behavior', 'animator',
        'emit', 'emitRetained', 'heightSync', 'heightSamples', 'cull', 'minimapProject',
        'hudProj', 'gpuSkinBuild', 'gpuSkinDraw', 'overlay', 'endDraw', 'gap',
        'hudPositionOnly', 'worldHud', 'screenBars', 'screenText']
first = datetime.datetime.fromisoformat(rows[0]['timestamp'])
last = datetime.datetime.fromisoformat(rows[-1]['timestamp'])
result = {'source': args.log.name, 'warmupFrames': args.warmup, 'samples': len(rows),
          'firstFrame': rows[0]['index'], 'lastFrame': rows[-1]['index'],
          'observedFps': (rows[-1]['index'] - rows[0]['index']) / (last - first).total_seconds(),
          'metrics': {key: metric(key) for key in keys if key in rows[0]}}
for label, subset in [('hudMoved', [r for r in rows if int(r['emitRetainedCount']) > 10000]),
                      ('hudQuiet', [r for r in rows if int(r['emitRetainedCount']) == 0])]:
    result[label] = {'samples': len(subset), 'metrics': {
        k: metric(k, subset) for k in ['frame', 'sim', 'presentation', 'transformSync', 'emitRetained']
    }} if subset else {'samples': 0}
destination = args.log.with_suffix('.summary.json')
destination.write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2))
