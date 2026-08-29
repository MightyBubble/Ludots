from PIL import Image
import numpy as np

def load(p):
    return np.asarray(Image.open(p).convert('RGB')).astype(np.int16)

# ---- Atmosphere diff heatmap ----
A=load("w3b_atmosphere.png"); B=load("w4c_atmosphere.png")
d=np.abs(A-B).sum(axis=2)
# heatmap: amplify
heat=np.clip(d*2,0,255).astype(np.uint8)
Image.fromarray(heat).save("atmo_diff_heat.png")

# Separate: where are BIG diffs (structural, d>30)?
big=(d>30).astype(np.uint8)*255
Image.fromarray(big).save("atmo_diff_big_mask.png")

# stats by region bands (which rows/cols) of big diffs
bigm=d>30
print("big diff total:", int(bigm.sum()))
ys,xs=np.where(bigm)
# histogram across columns
colhist=bigm.sum(axis=0)
rowhist=bigm.sum(axis=1)
import numpy as np
# print coarse profile in 16 vertical bands / 16 horizontal bands
def prof(v,n):
    out=[]
    for i in range(n):
        lo=i*len(v)//n; hi=(i+1)*len(v)//n
        out.append(int(v[lo:hi].sum()))
    return out
print("row profile (16 bands):", prof(rowhist,16))
print("col profile (16 bands):", prof(colhist,16))

# ---- blacksmith diff heatmap ----
A2=load("w3b_final_blacksmith.png"); B2=load("w4c_blacksmith.png")
d2=np.abs(A2-B2).sum(axis=2)
heat2=np.clip(d2*20,0,255).astype(np.uint8)
Image.fromarray(heat2).save("bs_diff_heat.png")
print("bs big>6 count:", int((d2>6).sum()), "mean where nonzero", float(d2[d2>0].mean()))
