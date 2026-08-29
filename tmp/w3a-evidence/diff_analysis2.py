import numpy as np
from PIL import Image

def load(p):
    return np.asarray(Image.open(p).convert("RGB"), dtype=np.float64)

def gauss(x, sigma):
    from scipy.ndimage import gaussian_filter
    return gaussian_filter(x, sigma=(sigma, sigma, 0))

def mse(a,b): return float(np.mean((a-b)**2))

base_atm = load("tmp/w3a-evidence/base_atmosphere.png")
w3a_atm  = load("tmp/w3a-evidence/w3a_atmosphere.png")
base_bs  = load("tmp/w3a-evidence/base_blacksmith.png")
w3a_bs   = load("tmp/w3a-evidence/w3a_blacksmith.png")

d_atm = np.abs(base_atm - w3a_atm).max(axis=2)   # per-pixel max channel diff
d_bs  = np.abs(base_bs  - w3a_bs ).max(axis=2)

print("=== Blacksmith pair (3 vs 4) ===")
print(f"  size: {base_bs.shape[1]}x{base_bs.shape[0]}")
print(f"  global MSE            : {mse(base_bs, w3a_bs):.4f}")
print(f"  max per-pixel diff    : {float(np.max(d_bs)):.2f}")
print(f"  mean abs diff         : {float(np.abs(base_bs-w3a_bs).mean()):.4f}")
print(f"  pixels identical tol<=1: {np.mean(d_bs<=1)*100:.4f}%")
print(f"  pixels with ANY diff>0: {np.mean(d_bs>0)*100:.4f}%")
print(f"  pixels diff>8         : {np.mean(d_bs>8)*100:.4f}%")

print()
print("=== Atmosphere spatial structure (where are diffs?) ===")
# Mask of high-diff pixels
mask = d_atm > 8
print(f"  high-diff(>8) pixel count: {mask.sum()}  = {np.mean(mask)*100:.3f}% of frame")
# Row profile (vertical) of high-diff density
rowprof = mask.mean(axis=1)
print("  high-diff density by row (y band %) :")
# identify contiguous y bands where density > some threshold
ys = np.where(rowprof > 0.05)[0]
if len(ys):
    # split into contiguous segments
    segs = []
    s = ys[0]; p = ys[0]
    for y in ys[1:]:
        if y - p > 3:
            segs.append((s,p)); s=y
        p=y
    segs.append((s,p))
    print("    y-segments with >5% density:", [(a,b,f"{rowprof[a:b+1].mean()*100:.1f}%") for a,b in segs])
# Column profile
mask_hi = d_atm > 8
# Check fraction of high-diff pixels in upper half (sky) vs lower (water region around island)
H,W = mask.shape
# The island is centered; water surrounds. Wave bands appear as elongated patches near shore.
# Let's check row bands
print(f"  mean diff per row (top->bottom), 20-row bins:")
bins = []
for i in range(0, H, 45):
    bins.append((i, f"{d_atm[i:i+45].mean():.2f}"))
for i,v in bins: print(f"    y={i:3d}-{i+45:3d}: {v}")

# blur collapse per region:
# is the residual diff (after blur) spatially near shore bands?
print()
print("=== Blur-collapse test with spatial mask ===")
ab = gauss(base_atm, 4.0); bb = gauss(w3a_atm, 4.0)
dr = np.abs(ab-bb).max(axis=2)
print(f"  post-blur(sigma=4) MSE: {mse(ab,bb):.4f}, %pix>4: {np.mean(dr>4)*100:.3f}%")
# zero-mean signature: check signed mean of diff (should be ~0 if noise-like)
signed = (base_atm - w3a_atm)
print(f"  signed pre-blur diff mean: {float(signed.mean()):.4f} (0 => zero-mean)")
print(f"  signed post-blur diff mean: {float((ab-bb).mean()):.4f}")
