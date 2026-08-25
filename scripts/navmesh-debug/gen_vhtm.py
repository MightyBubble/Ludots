import math, struct, os

COLS = 257
ROWS = 257
SPACING_CM = 100  # 25600 / 256
BOUNDS = (0, 0, 25600, 25600)

def height_cm(ix, iz):
    # same rolling-hill family as the hex/grid cases, expressed in cm
    h = 3 + 2.4 * math.sin(ix / 22.0) * math.cos(iz / 19.0) + 1.1 * math.sin((ix + iz) / 31.0)
    return int(round(h * 100.0))

out = bytearray()
out += b'VHTM'
out += struct.pack('<i', 2)                       # version
out += struct.pack('<iiii', *BOUNDS)              # X, Y, Width, Height (cm)
out += struct.pack('<ii', COLS, ROWS)             # samples
out += struct.pack('<i', 1)                       # layout 1 = RowMajorInt16Centimeters
out += struct.pack('<i', 0)                       # default layer index
out += struct.pack('<i', 1)                       # interpolation 1 = Triangle
out += struct.pack('<iii', 0, 1, 1)               # sample scale (offset, num, den)
out += struct.pack('<i', 1)                       # layer count
out += struct.pack('<i', 0)                       # layer id
name = b'height'
out += bytes([len(name)]) + name                  # .NET 7-bit length + utf8
out += struct.pack('<i', 0)                       # sample offset
out += struct.pack('<i', COLS * ROWS)             # sample count
out += struct.pack('<i', COLS * ROWS)             # global sample count
for z in range(ROWS):
    for x in range(COLS):
        v = height_cm(x, z)
        v = max(-32768, min(32767, v))
        out += struct.pack('<h', v)

path = 'mods/LudotsCoreMod/assets/terrain/navmesh_debug_vhtm.vhtm'
os.makedirs(os.path.dirname(path), exist_ok=True)
open(path, 'wb').write(bytes(out))
print('wrote', path, len(out), 'bytes', COLS, 'x', ROWS)
