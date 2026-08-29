import numpy as np
from PIL import Image

def load(p):
    return np.asarray(Image.open(p).convert("RGB"), dtype=np.float64)

base_atm = load("tmp/w3a-evidence/base_atmosphere.png")
w3a_atm  = load("tmp/w3a-evidence/w3a_atmosphere.png")
d = base_atm - w3a_atm
dmax = np.abs(d).max(axis=2)

# Build island-terrain mask by COLOR (independent of diff):
# terrain = green/brown/rock => not dominated by blue-water hue and darker/varied
R, G, B = base_atm[:,:,0], base_atm[:,:,1], base_atm[:,:,2]
# Water/sky is desaturated blue-ish; terrain is green(high G) or warm(high R, low B) or rock (low overall, low saturation)
terrain = (G > R * 0.85) & (G > B * 0.9) | ((R > B) & (R > 70) & (G > 60))   # green or warm
# also dark rock : low brightness, low blue sat
rock = (base_atm.max(axis=2) < 110) & (B < R*1.05) & (B < G*1.05)
terr = terrain | rock
terr &= (np.arange(base_atm.shape[0])[:,None] > 170)   # exclude sky band region is fine, but keep all

print(f"terrain-mask pixel count: {terr.sum()}  ({np.mean(terr)*100:.1f}% of frame)")
print("=== Diff statistics RESTRICTED to terrain-mask ===")
dt = dmax[terr]
print(f"  mean abs diff   : {float(np.abs(d[terr]).mean()):.4f}")
print(f"  pixels diff>8   : {np.mean(dt>8)*100:.4f}%")
print(f"  pixels diff>4   : {np.mean(dt>4)*100:.4f}%")
print(f"  pixels diff<=1  : {np.mean(dt<=1)*100:.3f}%")
print(f"  max diff        : {float(dt.max()):.2f}")

print()
print("=== Diff statistics RESTRICTED to NON-terrain (water+sky) ===")
dn = dmax[~terr]
print(f"  mean abs diff   : {float(np.abs(d[~terr]).mean()):.4f}")
print(f"  pixels diff>8   : {np.mean(dn>8)*100:.4f}%")
print(f"  pixels diff<=1  : {np.mean(dn<=1)*100:.3f}%")

# Save diff heatmap + terrain mask for visual inspection
heat = np.clip(dmax/ dmax.max() *255, 0,255).astype(np.uint8)
Image.fromarray(heat).save("tmp/w3a-evidence/atmos_diff_heat.png")
maskimg = (terr*255).astype(np.uint8)
Image.fromarray(maskimg).save("tmp/w3a-evidence/atmos_terrain_mask.png")
print("\nsaved atmos_diff_heat.png and atmos_terrain_mask.png")
