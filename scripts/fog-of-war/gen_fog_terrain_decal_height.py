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
    ridge_center = x * 0.7071 + z * 0.7071 - 1000.0
    h += 60.0 * math.exp(-(ridge_center * ridge_center) / (2.0 * 320.0 * 320.0))
    h += 6.0 * math.sin(x * math.tau / 120.0) * math.cos(z * math.tau / 100.0)
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

path = 'mods/showcases/fog_of_war/FogTerrainDecalShowcaseMod/assets/terrain/fog_terrain_decal.height'
os.makedirs(os.path.dirname(path), exist_ok=True)
open(path, 'wb').write(bytes(out))
print('wrote', path, len(out), 'bytes', COLS, 'x', ROWS)
