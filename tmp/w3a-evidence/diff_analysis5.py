import numpy as np
from PIL import Image
from scipy.ndimage import label

def load(p):
    return np.asarray(Image.open(p).convert("RGB"), dtype=np.float64)

base_atm = load("tmp/w3a-evidence/base_atmosphere.png")
w3a_atm  = load("tmp/w3a-evidence/w3a_atmosphere.png")
dmax = np.abs(base_atm - w3a_atm).max(axis=2)

H, W = dmax.shape
# island = large contiguous near-zero-diff core in the center (silhouette).
lown = (dmax < 2.0).astype(np.uint8)
lab, n = label(lown)
# find the component with max overlap in the central box (the island blob)
center_box = np.zeros_like(lown, bool)
center_box[300:780, 200:1150] = True
best, bestscore = 0, -1
for i in range(1, n+1):
    m = (lab == i)
    sc = (m & center_box).sum()
    if sc > bestscore:
        bestscore, best = sc, m
island = (lab == best)
print(f"island blob component size: {island.sum()} px ({np.mean(island)*100:.1f}% frame)")

print("=== ON ISLAND BODY (terrain silhouette) ===")
di = dmax[island]
print(f"  mean abs diff  : {float(np.abs(base_atm-w3a_atm)[island].mean()):.4f}")
print(f"  pixels diff>8  : {np.mean(di>8)*100:.4f}%")
print(f"  pixels diff<=1 : {np.mean(di<=1)*100:.3f}%")
print(f"  max diff       : {float(di.max()):.2f}")

print()
print("=== WATER BAND (frame y>=180, excluding island) ===")
water = np.zeros_like(dmax, bool)
water[180:,:] = True
water &= ~island
# also exclude the letterbox black right border (x>1280)
water[:, 1280:] = False
dw = dmax[water]
print(f"  water pixel count: {water.sum()}")
print(f"  mean abs diff  : {float(np.abs(base_atm-w3a_atm)[water].mean()):.4f}")
print(f"  pixels diff>8  : {np.mean(dw>8)*100:.3f}%")
print(f"  pixels diff<=1 : {np.mean(dw<=1)*100:.3f}%")
print(f"  max diff       : {float(dw.max()):.2f}")

# conclusion ratio
print()
print("=== CONCLUSION: where do high-diff (>8) pixels live? ===")
hi = dmax > 8
island_hi = (hi & island).sum()
water_hi  = (hi & water).sum()
print(f"  total high-diff px: {hi.sum()}")
print(f"   on island : {island_hi}  ({island_hi/hi.sum()*100:.3f}%)")
print(f"   on water  : {water_hi}  ({water_hi/hi.sum()*100:.2f}%)")
