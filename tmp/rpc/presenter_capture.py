import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import subprocess
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
LAUNCH = ROOT / 'artifacts/acceptance/gpu-shared-pose/launch'
URL = 'http://127.0.0.1:47931'


def get(path):
    with urllib.request.urlopen(URL + path, timeout=5) as response:
        return json.load(response)


def rpc(method, **params):
    body = json.dumps(dict(jsonrpc='2.0', id=1, method='ludots.' + method, params=params)).encode()
    request = urllib.request.Request(URL + '/rpc', data=body, headers={'Content-Type': 'application/json'})
    with urllib.request.urlopen(request, timeout=15) as response:
        result = json.load(response)
    if 'error' in result:
        raise RuntimeError(result)
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('label')
    parser.add_argument('--seconds', type=int, default=60)
    args = parser.parse_args()
    runs_before = set((LAUNCH / 'runs').iterdir())
    subprocess.run(['python', str(LAUNCH / 'prepare_launch.py'), 'start'], cwd=ROOT, check=True)
    added = set((LAUNCH / 'runs').iterdir()) - runs_before
    if len(added) != 1:
        raise RuntimeError('Expected one new isolated run directory')
    run = added.pop()
    process = json.loads((run / 'process.json').read_text())
    deadline = time.monotonic() + 90
    while True:
        try:
            first = get('/health')
            break
        except urllib.error.URLError:
            if time.monotonic() > deadline:
                raise
            time.sleep(0.5)
    time.sleep(1)
    second = get('/health')
    assert first['instance']['pid'] == process['pid'] == second['instance']['pid']
    assert second['pumpCount'] > first['pumpCount']
    session = rpc('session.info')
    assert session['result']['mapId'] == 'mass_navigation'
    rpc('camera.control', action='set', targetXCm=0, targetYCm=0, yaw=45, pitch=45, distanceCm=3500)
    time.sleep(2)
    camera = rpc('camera.control', action='get')
    assert camera['result']['distanceCm'] == 3500
    assert camera['result']['pitch'] == 45
    entities = rpc('entities.query', nameFilter='MassNavigation.Agent', limit=3)
    assert entities['result']['totalMatched'] == 10000
    time.sleep(10)
    started = dt.datetime.now(dt.timezone.utc).isoformat()
    print(f'{args.label}: capturing {args.seconds}s, PID {process["pid"]}', flush=True)
    end = time.monotonic() + args.seconds
    while time.monotonic() < end:
        time.sleep(min(10, end - time.monotonic()))
        health = get('/health')
        assert health['instance']['pid'] == process['pid']
        assert health['pumpCount'] > second['pumpCount']
        second = health
        print(f'{args.label}: pumpCount={health["pumpCount"]}', flush=True)
    ended = dt.datetime.now(dt.timezone.utc).isoformat()
    positions = rpc('presenters.query', ownerName='MassNavigation.Agent', limit=20)
    shot = rpc('screenshot', name='presenter-' + args.label)
    app = Path(process['cwd'])
    evidence = dict(label=args.label, started=started, ended=ended, run=str(run),
                    camera=camera, entities=entities, positions=positions, shot=shot,
                    health=second, session=session,
                    binaryHashes={n: hashlib.sha256((app/n).read_bytes()).hexdigest()
                                  for n in ['Ludots.Core.dll', 'Ludots.Adapter.Raylib.dll', 'Ludots.Raylib.Render.dll']})
    (run / 'presenter-window.json').write_text(json.dumps(evidence, indent=2), encoding='utf-8')
    print(f'{args.label}: complete {run}', flush=True)


if __name__ == '__main__':
    main()
