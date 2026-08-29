import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter

def load(p):
    return np.asarray(Image.open(p).convert("RGB"), dtype=np.float64)

base_atm = load("tmp/w3a-evidence/base_atmosphere.png")
w3a_atm  = load("tmp/w3a-evidence/w3a_atmosphere.png")

d = base_atm - w3a_atm   # signed
dmax = np.abs(d).max(axis=2)

# Check: is the diff only on the water surface, and the ISLAND core zero?
# Island occupies central region. We need a mask. Use color: green/brown terrain.
# Rough approach: islands are the dark-green/brown/rocky center cluster.
# Let's just report the diff histogram and spatial concentration in the periphery (water).
H, W, _ = base_atm.shape
# Central island rough bounding box (from the image: roughly x in [200,1100], y in [300,750])
cx0, cx1, cy0, cy1 = 200, 1150, 300, 780
core = dmax[cy0:cy1, cx0:cx1]
out  = np.ones(dmax.shape, bool)
out[cy0:cy1, cx0:cx1] = False   # periphery = water+sky
print("=== Island core box diff (central terrain region) ===")
print(f"  region x[{cx0},{cx1}] y[{cy0},{cy1}]")
print(f"  mean abs diff          : {float(np.abs(d[cy0:cy1,cx0:cx1]).mean()):.4f}")
print(f"  pixels diff>8          : {np.mean(core>8)*100:.4f}%")
print(f"  pixels diff>4          : {np.mean(core>4)*100:.4f}%")
print(f"  max diff in core       : {float(core.max()):.2f}")

print()
print("=== Periphery (water+sky, outside core box) ===")
per = dmax[out]
print(f"  pixels diff>8          : {np.mean(per>8)*100:.2f}%")
print(f"  mean abs diff over periphery: {float(np.abs(d[out]).mean()):.4f}")

# further: is it water-only or also sky? sky = top region y<180 (was 0 diff earlier)
sky = dmax[:180,:]
print()
print(f"=== Sky (y<180): mean diff {sky.mean():.4f}, %diff>8: {np.mean(sky>8)*100:.4f}%")
print(f"    => sky region effectively identical (diff near 0)")

# zero-mean noise test on the water band: statistics of signed diff per channel
water = d[500:760, 180:1200]   # a water band around island
print()
print("=== Zero-mean / high-freq noise test on water band ===")
for ch,c in enumerate("RGB"):
    s = water[:,:,ch]
    print(f"  channel {c}: mean={s.mean():+.4f}  std={s.std():.3f}  skew={((s-np.mean(s))**3).mean()/ (s.std()**3):+.3f}")
# high-freq: diff of blurred vs original -> residual compactness
sign = d[500:760,180:1200]
bl = gaussian_filter(sign, 2.0)
resid = sign - bl
print(f"  high-freq residual  : mean={resid.mean():+.4f} std={resid.std():.3f}")
print(f"  low-freq (blur)     : mean={bl.mean():+.4f} std={bl.std():.3f}")
print(f"  ratio resid_std/lowfreq_std = {resid.std()/ (bl.std()+1e-9):.2f} (large => dominated by high-freq)")
