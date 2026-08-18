import math, struct, os

# 4x4 macro tiles (1024m at 100cm cells) = 16x16 chunks of 64 cells.
# Features are authored on a 256-cell reference grid and scaled by S so the
# showcase keeps the same landmark shapes as the original 256m prototype.
CHUNK = 64
S = 4
W_CHUNKS = 4 * S
H_CHUNKS = 4 * S
W_CELLS = W_CHUNKS * CHUNK
H_CELLS = H_CHUNKS * CHUNK

def cell_bytes(x, z):
    # rolling hills, 2m per height unit, 0..7 units
    h = 3 + 2.4 * math.sin(x / (22.0 * S)) * math.cos(z / (19.0 * S)) + 1.1 * math.sin((x + z) / (31.0 * S))
    height = max(0, min(15, int(round(h))))
    water = 0
    flags = 0
    # blocked plateau straddling the macro-tile seam at world center
    if 112 * S <= x < 144 * S and 112 * S <= z < 144 * S:
        flags |= 0x08
        height = 9
    # a second smaller blocker fully inside the far macro tile
    if 208 * S <= x < 220 * S and 30 * S <= z < 50 * S:
        flags |= 0x08
    area = 0
    # mud band (areaId 1) in the low valley along z
    if 64 * S <= z < 96 * S and not (112 * S <= x < 144 * S and 112 * S <= z < 144 * S):
        area = 1
    # road strip (areaId 2) along x
    if 150 * S <= x < 164 * S:
        area = 2
    b0 = (height << 4) | water
    b1 = 0
    b2 = flags
    b3 = area
    return bytes([b0, b1, b2, b3])

out = bytearray()
out += struct.pack('<ii', W_CHUNKS, H_CHUNKS)
out += bytes([4])  # dense stride-4
for cy in range(H_CHUNKS):
    for cx in range(W_CHUNKS):
        for ly in range(CHUNK):
            for lx in range(CHUNK):
                out += cell_bytes(cx * CHUNK + lx, cy * CHUNK + ly)

path = 'mods/LudotsCoreMod/assets/Data/Maps/navmesh_debug_openworld.bin'
os.makedirs(os.path.dirname(path), exist_ok=True)
open(path, 'wb').write(bytes(out))
print('wrote', path, len(out), 'bytes,', W_CELLS, 'x', H_CELLS, 'cells')
