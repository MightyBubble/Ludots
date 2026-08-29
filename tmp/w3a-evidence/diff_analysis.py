import numpy as np
from PIL import Image

def load(p):
    im = Image.open(p).convert("RGB")
    return np.asarray(im, dtype=np.float64)

def mse(a, b):
    return float(np.mean((a - b) ** 2))

def maxdiff(a, b):
    return float(np.max(np.abs(a - b)))

def pct_identical(a, b, tol=1.0):
    d = np.abs(a - b)
    return float(np.mean(d.max(axis=2) <= tol) * 100)

def gauss_blur(x, sigma=4.0):
    # simple separable gaussian blur via FFT-free convolution (box approx) -> use scipy if available
    try:
        from scipy.ndimage import gaussian_filter
        return gaussian_filter(x, sigma=(sigma, sigma, 0))
    except Exception:
        return x

def blocksummary(path, a, b):
    print("=" * 70)
    print("FILE PAIR:", path)
    diff = np.abs(a - b)
    dmax = diff.max(axis=2)  # per-pixel max channel diff
    print(f"  size: {a.shape[1]}x{a.shape[0]}  (WxH)")
    print(f"  global MSE        : {mse(a,b):.4f}")
    print(f"  max per-pixel diff: {maxdiff(a,b):.2f}")
    print(f"  pixels identical (tol<=1): {pct_identical(a,b,1.0):.4f}%")
    print(f"  pixels diff>8     : {np.mean(dmax>8)*100:.4f}%")
    print(f"  pixels diff>32    : {np.mean(dmax>32)*100:.4f}%")
    print(f"  mean abs diff     : {float(diff.mean()):.4f}")
    # spatial distribution: where are diffs concentrated?
    colmean = dmax.mean(axis=0)  # per-x average diff
    rowmean = dmax.mean(axis=1)  # per-y average diff
    # find columns with high diff
    hi_x = np.where(colmean > colmean.max()*0.5)[0]
    hi_y = np.where(rowmean > rowmean.max()*0.5)[0]
    if len(hi_x):
        print(f"  high-diff x-range : [{hi_x.min()}, {hi_x.max()}] of {a.shape[1]}")
    if len(hi_y):
        print(f"  high-diff y-range : [{hi_y.min()}, {hi_y.max()}] of {a.shape[0]}")
    # fraction of high-diff pixels that are near water band
    return

# ---- atmosphere pairs ----
base_atm = load("tmp/w3a-evidence/base_atmosphere.png")
w3a_atm  = load("tmp/w3a-evidence/w3a_atmosphere.png")
print(f"atmos sizes: base={base_atm.shape} w3a={w3a_atm.shape}")
blocksummary("atmosphere base vs w3a", base_atm, w3a_atm)

# blur-collapse test: blur both, then diff -> if remaining diff ~0, differences are high-freq
def blur_pair(a, b, sigma):
    ab = gauss_blur(a, sigma)
    bb = gauss_blur(b, sigma)
    return ab, bb

for sig in [2.0, 4.0, 8.0]:
    ab, bb = blur_pair(base_atm, w3a_atm, sig)
    print(f"  --> after gaussian sigma={sig}: MSE={mse(ab,bb):.4f}  maxdiff={maxdiff(ab,bb):.2f}")
