import csv
import json
import statistics
import sys
from pathlib import Path

directory = Path(sys.argv[1])
rows = list(csv.DictReader((directory / 'frames.csv').open(newline='', encoding='utf-8')))
active = [r for r in rows if r['warmup'] == '0']
metrics = ['total_sync_ms', 'submission_cpu_ms', 'prep_cpu_ms', 'prep_gpu_interval_ms',
           'shadow_gpu_ms', 'main_gpu_ms', 'preskin_cpu_upload_ms', 'texture_upload_cpu_ms',
           'alloc_bytes', 'gen0', 'gen1', 'restore_submit_ms', 'restore_drain_ms']
if 'normal_correction_cpu_upload_ms' in rows[0]:
    metrics += ['normal_correction_cpu_upload_ms', 'normal_correction_upload_bytes']

def stats(values):
    values = sorted(values)
    return {'mean': statistics.mean(values), 'p50': statistics.median(values),
            'p95': values[min(len(values) - 1, int(len(values) * .95))],
            'min': values[0], 'max': values[-1]}

summary = {'metadata': json.loads((directory / 'metadata.json').read_text()), 'modes': {}, 'paired': {}}
for mode in ['baseline', 'preskin']:
    selected = [r for r in active if r['mode'] == mode]
    if not selected:
        raise RuntimeError(f'No measured samples for {mode}.')
    summary['modes'][mode] = {'frames': len(selected),
                             **{key: stats([float(r[key]) for r in selected]) for key in metrics}}
pairs = {}
for row in active:
    pair = pairs.setdefault(row['pair'], {})
    if row['mode'] in pair:
        raise RuntimeError(f'Duplicate mode in pair {row["pair"]}.')
    pair[row['mode']] = row
for pair in pairs.values():
    if set(pair) != {'baseline', 'preskin'} or pair['baseline']['normalized_time'] != pair['preskin']['normalized_time']:
        raise RuntimeError('Missing or unequal-pose pair.')
for key in ['total_sync_ms', 'prep_cpu_ms', 'shadow_gpu_ms', 'main_gpu_ms']:
    summary['paired'][key + '_saved'] = stats([float(p['baseline'][key]) - float(p['preskin'][key]) for p in pairs.values()])
pixels = directory / 'pixels.json'
if not pixels.exists():
    raise RuntimeError('Pixel evidence was not completed.')
summary['pixels'] = json.loads(pixels.read_text())
target = directory / 'summary.json'
target.write_text(json.dumps(summary, indent=2), encoding='utf-8')
print(json.dumps(summary, indent=2))
