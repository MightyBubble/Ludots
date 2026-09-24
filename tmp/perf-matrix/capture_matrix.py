import csv
import json
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(r'C:\001_AI\_audit_1485_perf')
OUT = ROOT / 'tmp/perf-matrix' / os.environ.get('AUDIT_CAPTURE_DIRECTORY', 'capture')
OUT.mkdir(exist_ok=True)
APP = ROOT / 'src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net9.0/Ludots.App.Raylib.dll'
BOOT = APP.parent / 'launcher.runtime.json'
graph_path = ROOT / 'artifacts/launcher/raylib.launch.graph.json'
graph = json.loads(graph_path.read_text())
BOOT.write_text(json.dumps(dict(LaunchGraphFullPath=str(graph_path), LaunchGraphPath=str(graph_path),
    PlanSelectors=graph['selectors'], PlanRootModIds=graph['rootModIds'], PlanOrderedModIds=graph['orderedModIds'],
    PlanFingerprint=graph['planFingerprint'], PlanSchemaVersion=1)), encoding='utf-8')
FRAMES = int(os.environ.get('AUDIT_CAPTURE_FRAMES', '720'))
keys = ['HUD','TERRAIN_HUD_OCCLUSION','ANIMATOR','MASSNAV','CONTINUOUS_EFFECT','MINIMAP']
runs = []
for count in [1000,5000,10000]:
    for motion in ['moving','hold']:
        for density in ['crowded','sparse']:
            runs.append((f'{count}-{motion}-{density}', count, motion, density, None))
for key in keys:
    runs.append((f'10000-moving-crowded-no-{key.lower()}',10000,'moving','crowded',key))
runs.append(('10000-moving-crowded-skip-initial-sample-tick',10000,'moving','crowded','SKIP_INITIAL_SAMPLE_TICK'))
runs.append(('10000-moving-crowded-repeat',10000,'moving','crowded',None))
if len(sys.argv)>1:
    runs=[r for r in runs if r[0] in sys.argv[1:]]
for name,count,motion,density,disabled in runs:
    existing = OUT/(name+'.csv')
    if existing.exists() and len(list(csv.DictReader(existing.open()))) == FRAMES:
        print('EXISTS', name, flush=True)
        continue
    env=os.environ.copy()
    for key in list(env):
        if key.startswith('LUDOTS_AB_') or key.startswith('LUDOTS_TAKE_SCREENSHOT'):
            del env[key]
    env.update(LUDOTS_AB_CAPTURE='1', LUDOTS_AB_CAPTURE_PATH=str(OUT/(name+'.csv')),
               LUDOTS_AUTO_EXIT_FRAME=str(FRAMES), LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES='0',
               LUDOTS_RAYLIB_TIMING_SYSTEM_BREAKDOWN='1', LUDOTS_RAYLIB_LIGHTWEIGHT_DIAGNOSTIC_HUD='0',
               LUDOTS_RAYLIB_AUTO_ORBIT_DEG_PER_SEC='0',
               LUDOTS_RAYLIB_DIAGNOSTIC_PATH=str(OUT/(name+'.log')),
               LUDOTS_AB_TOTAL_AGENTS=str(count), LUDOTS_AB_STATIC='1' if motion=='hold' else '0',
               LUDOTS_AB_DENSITY=density)
    if disabled=='SKIP_INITIAL_SAMPLE_TICK': env['LUDOTS_AB_'+disabled]='1'
    elif disabled: env['LUDOTS_AB_DISABLE_'+disabled]='1'
    if os.environ.get('AUDIT_GPU_TIMING') == '1': env['LUDOTS_AB_GPU_TIMING']='1'
    if os.environ.get('AUDIT_GPU_TIMESTAMP') == '1': env['LUDOTS_AB_GPU_TIMESTAMP']='1'
    print('RUN', name, flush=True)
    with (OUT/(name+'.stdout.log')).open('w',encoding='utf-8') as log:
        process=subprocess.run(['dotnet',str(APP),str(BOOT)],cwd=APP.parent,env=env,stdout=log,stderr=subprocess.STDOUT)
    log=(OUT/(name+'.stdout.log')).read_text(encoding='utf-8')
    diag=(OUT/(name+'.log')).read_text(encoding='utf-8')
    if process.returncode!=0 or 'Unhandled exception' in log or f'auto-exit frame={FRAMES}' not in diag:
        raise RuntimeError(f'{name} failed; inspect logs')
    rows=list(csv.DictReader((OUT/(name+'.csv')).open()))
    if len(rows)!=FRAMES: raise RuntimeError(f'{name}: expected {FRAMES} rows, found {len(rows)}')
    print('DONE', name, len(rows), 'frames', flush=True)
