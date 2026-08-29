import numpy as np
from PIL import Image

def load(p):
    return np.asarray(Image.open(p).convert("RGB"), dtype=np.float64)

base_atm = load("tmp/w3a-evidence/base_atmosphere.png")
w3a_atm  = load("tmp/w3a-evidence/w3a_atmosphere.png")
d = base_atm - w3a_atm
dmax = np.abs(d).max(axis=2)
H, W = dmax.shape

# ground-truth silhouette: island body. Detect by terrain darkness/color in BASE image.
# The island is the central mass. Use a coarse approach: brightness.
R,G,B = base_atm[:,:,0], base_atm[:,:,1], base_atm[:,:,2]
# water/sky = bright, blue-ish. terrain = darker, varied.
bright = base_atm.mean(axis=2)
waterish = (B > R) & (bright > 90)   # blue-dominant bright = water/sky
island = ~waterish
island[:180,:] = False      # remove sky (top)
island[:, 1280:] = False    # remove black letterbox right border
# fill: also exclude the small dark clusters that are just shadow? keep all non-water below y=180

print("island(terrain) pixel count:", island.sum(), f"({np.mean(island)*100:.1f}% frame)")
water = (~island)
water[:180,:]=False; water[:,1280:]=False

def stat(mask, label):
    d_ = dmax[mask]
    print(f"  [{label}] n={mask.sum()}  meanAbs={np.abs(d[mask]).mean():.4f}  "
          f"%>8={np.mean(d_>8)*100:.3f}%  %<=1={np.mean(d_<=1)*100:.3f}%  max={d_.max():.2f}")

print("\n=== diff by terrain vs water (color-based, y>=180) ===")
stat(island, "terrain/island")
stat(water,  "water+wave")

# signed zero-mean check on the whole diff
print("\n=== zero-mean check (whole frame, y>=180) ===")
sel = d[180:,:1280,:]
print(f"  signed diff mean: {sel.mean():+.4f}  (per-channel:")
for i,c in enumerate("RGB"):
    print(f"    {c}: {sel[:,:,i].mean():+.5f}")
