from pathlib import Path
import ctypes
import hashlib
import json
import re
import struct
import subprocess
import urllib.request
import numpy as np
from PIL import Image

here = Path(__file__).resolve().parent
evidence = here / 'normal-evidence'
evidence.mkdir(exist_ok=True)
dll = here / 'runtime-snapshot/raylib.dll'
manifest = {'dll': str(dll), 'dll_sha256': hashlib.sha256(dll.read_bytes()).hexdigest(), 'sources': {}}
for name in ['rmodels.c', 'raymath.h']:
    url = f'https://raw.githubusercontent.com/raysan5/raylib/5.5/src/{name}'
    data = urllib.request.urlopen(url, timeout=30).read()
    (evidence / name).write_bytes(data)
    manifest['sources'][name] = {'url': url, 'sha256': hashlib.sha256(data).hexdigest()}

dumpbin = Path(r'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64\dumpbin.exe')
exports = subprocess.run([str(dumpbin), '/exports', str(dll)], check=True, capture_output=True, text=True).stdout
selected_exports = [line for line in exports.splitlines() if any(name in line for name in ['UpdateModelAnimation', 'Vector3Transform', 'rlUpdateVertexBuffer'])]
(evidence / 'native-exports.txt').write_text('\n'.join(selected_exports), encoding='utf-8')
addresses = {}
for line in exports.splitlines():
    match = re.match(r'\s*\d+\s+[0-9A-F]+\s+([0-9A-F]{8})\s+(\w+)', line)
    if match:
        addresses[match[2]] = int(match[1], 16)
b = dll.read_bytes()
pe = struct.unpack_from('<I', b, 0x3c)[0]
base = struct.unpack_from('<Q', b, pe + 24 + 24)[0]
disassembly = subprocess.run([str(dumpbin), '/disasm:nobytes', str(dll)], check=True, capture_output=True, text=True).stdout
starts = [addresses['UpdateModelAnimation'], addresses['Vector3Transform']]
ends = [min(v for v in addresses.values() if v > start) for start in starts]
selected = [line for line in disassembly.splitlines() if (match := re.match(r'\s*([0-9A-F]{16}):', line)) and any(base + start <= int(match[1], 16) < base + end for start, end in zip(starts, ends))]
(evidence / 'native-normal-disassembly.txt').write_text('\n'.join(selected), encoding='utf-8')
manifest['native_functions_rva'] = {name: hex(addresses[name]) for name in ['UpdateModelAnimation', 'UpdateModelAnimationBones', 'Vector3Transform', 'rlUpdateVertexBuffer']}

class Vector3(ctypes.Structure):
    _fields_ = [(name, ctypes.c_float) for name in ['x', 'y', 'z']]

class Matrix(ctypes.Structure):
    _fields_ = [(f'm{i}', ctypes.c_float) for i in [0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15]]

native = ctypes.CDLL(str(dll))
transform = native.Vector3Transform
transform.argtypes = [Vector3, Matrix]
transform.restype = Vector3
matrix = Matrix(m0=1, m5=1, m10=1, m15=1, m12=2, m13=-3, m14=4)
result = transform(Vector3(0, 1, 0), matrix)
native_result = [result.x, result.y, result.z]
if native_result != [2.0, -2.0, 4.0]:
    raise RuntimeError(f'Unexpected native vector transform: {native_result}')
manifest['cpu_only_native_check'] = {'entry_point': 'Vector3Transform', 'gpu_initialized': False,
    'input_normal': [0, 1, 0], 'linear_matrix': 'identity', 'translation': [2, -3, 4],
    'native_output': native_result, 'shader_mat3_output': [0, 1, 0],
    'native_formula': 'sum(weight * (linearBone * normal + translationBone))',
    'shader_formula': 'linearPart(sum(weight * boneMatrix)) * normal',
    'difference': 'sum(weight * translationBone)'}

silhouettes = []
for pose in range(3):
    directory = here / 'results-10000'
    a = np.array(Image.open(directory / f'pose-{pose}-baseline.png'))[:, :, :3].astype(int)
    b = np.array(Image.open(directory / f'pose-{pose}-preskin.png'))[:, :, :3].astype(int)
    background = np.array([34, 40, 48])
    ba, bb = (a == background).all(axis=2), (b == background).all(axis=2)
    foreground = ~(ba | bb)
    silhouettes.append({'pose': pose, 'background_baseline': int(ba.sum()), 'background_preskin': int(bb.sum()),
                        'background_mask_xor_pixels': int((ba ^ bb).sum()),
                        'foreground_mean_preskin_minus_baseline_rgb': (b - a)[foreground].mean(axis=0).tolist()})
manifest['observed_silhouettes'] = silhouettes
(evidence / 'evidence.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(json.dumps(manifest, indent=2))
