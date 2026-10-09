import math, struct, os

COLS = 2049
ROWS = 2049
SPACING_CM = 200
BOUNDS = (0, 0, 409600, 409600)


def height_cm(ix, iz):
    x = ix * 2.0
    z = iz * 2.0
    h = 90.0
    h += 50.0 * math.sin(x * math.tau / 1400.0) * math.cos(z * math.tau / 1150.0)
    h += 30.0 * math.sin((x + z) * math.tau / 850.0)
    ridge_center = x * 0.7071 + z * 0.7071 - 2000.0
    h += 70.0 * math.exp(-(ridge_center * ridge_center) / (2.0 * 300.0 * 300.0))
    # 跨 chunk 尺度(64m)的起伏:让相邻 chunk 边界两侧坡向显著不同,
    # 单边差分法线接缝在斜射光下可见、前后差分图可放大
    h += 8.0 * math.sin(x * math.tau / 95.0 + 0.7 * math.sin(z * math.tau / 310.0)) * math.cos(z * math.tau / 80.0)
    return int(round(h * 100.0))


out = bytearray()
out += b'CHTM'
out += struct.pack('<i', 2)
out += struct.pack('<iiii', *BOUNDS)
out += struct.pack('<ii', COLS, ROWS)
out += struct.pack('<i', 1)          # RowMajorInt16Centimeters
out += struct.pack('<i', 0)          # default layer
out += struct.pack('<i', 1)          # BilinearHeightfield interpolation
out += struct.pack('<iii', 0, 1, 1)  # sample scale identity
out += struct.pack('<i', 1)          # layer count
out += struct.pack('<i', 0)
name = b'height'
out += bytes([len(name)]) + name
out += struct.pack('<i', 0)
out += struct.pack('<i', COLS * ROWS)
out += struct.pack('<i', COLS * ROWS)
for z in range(ROWS):
    for x in range(COLS):
        v = max(-32768, min(32767, height_cm(x, z)))
        out += struct.pack('<h', v)

path = 'mods/fixtures/terrain/TerrainLodCullingAcceptanceMod/assets/terrain/terrain_lod_culling.height'
os.makedirs(os.path.dirname(path), exist_ok=True)
open(path, 'wb').write(bytes(out))
print('wrote', path, len(out), 'bytes', COLS, 'x', ROWS)
