from PIL import Image
import numpy as np

def load(p):
    return np.asarray(Image.open(p).convert('RGB')).astype(np.int16)

# BLACKSMITH: the thin line at health bar, y~435-455, x~560-680
A=load("w3b_final_blacksmith.png"); B=load("w4c_blacksmith.png")
d=np.abs(A-B).sum(axis=2)
line=d[428:465,540:700]
print("BS health-bar line region: max=%d, >6 count=%d, mean=%.3f"%(line.max(),(line>6).sum(),line.mean()))
rows=(d>0).sum(axis=1)
# find rows with most diff
top=np.argsort(rows)[-8:]
print("BS rows with most diff (y, count):",[(int(r),int(rows[r])) for r in sorted(top)])

# ATMOSPHERE: island interior only (center, avoiding water bbox corners)
# island silhouette roughly x 320..980 y 320..800 center; pick tight center
interior=d[380:760,380:900]
print("ATMO island-interior(center): px=%d, >30 count=%d (%.4f%%), max=%d, mean=%.3f"%(
    interior.size,(interior>30).sum(),100*(interior>30).mean(),interior.max(),interior.mean()))
