import math, struct, os

COLS = 1025
ROWS = 1025
SPACING_CM = 100
BOUNDS = (0, 0, 102400, 102400)

def height_cm(ix, iz):
    h = 3 + 2.4 * math.sin(ix / 22.0) * math.cos(iz / 19.0) + 1.1 * math.sin((ix + iz) / 31.0)
    return int(round(h * 100.0))

out = bytearray()
out += b'VHTM'
out += struct.pack('<i', 2)
out += struct.pack('<iiii', *BOUNDS)
out += struct.pack('<ii', COLS, ROWS)
out += struct.pack('<i', 1)          # RowMajorInt16Centimeters
out += struct.pack('<i', 0)          # default layer
out += struct.pack('<i', 1)          # triangle interpolation
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

path = 'mods/LudotsCoreMod/assets/terrain/navmesh_debug_grid.height'
os.makedirs(os.path.dirname(path), exist_ok=True)
open(path, 'wb').write(bytes(out))
print('wrote', path, len(out), 'bytes', COLS, 'x', ROWS)
