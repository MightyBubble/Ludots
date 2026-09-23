from pathlib import Path
import datetime
import hashlib
import json
import re
import statistics

root = Path(__file__).resolve().parents[2]
target = root / 'artifacts/acceptance/gpu-shared-pose/presentation-audit.json'
report = json.loads(target.read_text(encoding='utf-8'))
runs = ['20260909T182222.511476Z', '20260909T184451.329584Z']
evidence = {'capture_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
            'filter': 'timing lines with skinnedRaw=10000, hudRaw=20000, behaviorBoot=0',
            'sample_selection': 'first filtered row with behaviorOwner=0 in each log; all filtered rows summarized separately',
            'limitations': 'Separate application launches, not alternating paired frames. GPU/model experimental work present. Logs can continue growing after this read; hashes identify bytes read.', 'runs': []}
for run in runs:
    path = root / 'artifacts/acceptance/gpu-shared-pose/launch/runs' / run / 'timing.log'
    raw = path.read_bytes()
    rows = []
    for line_number, line in enumerate(raw.decode('utf-8-sig').splitlines(), 1):
        if ' timing frame=' not in line:
            continue
        values = dict(re.findall(r'(\w+)=([^ ]+)', line))
        if values.get('skinnedRaw') == '10000' and values.get('hudRaw') == '20000' and values.get('behaviorBoot') == '0':
            rows.append((line_number, line, values))
    if not rows:
        raise RuntimeError(f'No comparable population rows in {path}')
    quiet = [r for r in rows if r[2].get('behaviorOwner') == '0']
    if not quiet:
        raise RuntimeError(f'No owner-change-free sample in {path}')
    sample = quiet[0]
    stats = {}
    for key in ['behavior', 'behaviorTick', 'behaviorOwner', 'hudProj', 'presentation', 'transformSync', 'emit']:
        values = [float(r[2][key]) for r in rows]
        stats[key] = {'mean': statistics.mean(values), 'median': statistics.median(values), 'min': min(values), 'max': max(values)}
    evidence['runs'].append({'run': run, 'path': str(path.relative_to(root)), 'sha256_at_read': hashlib.sha256(raw).hexdigest(),
                             'filtered_rows': len(rows), 'quiet_rows': len(quiet), 'stats': stats,
                             'sample': {'source_line': sample[0], 'raw': sample[1], 'values': sample[2]}})
report['application_log_evidence'] = evidence
target.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
for run in evidence['runs']:
    s = run['sample']['values']
    print(run['run'], 'line', run['sample']['source_line'], 'rows', run['filtered_rows'],
          'behavior', s['behavior'], 'tick', s['behaviorTick'], 'hudProj', s['hudProj'])
print('Validated JSON:', target)
