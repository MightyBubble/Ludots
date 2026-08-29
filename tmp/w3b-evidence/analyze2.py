from PIL import Image
import numpy as np
B="C:/001_AI/LudotsProd/tmp/w3b-evidence/"
def load(p): return np.asarray(Image.open(B+p).convert('RGB')).astype(np.int16)

# ---- BLACKSMITH small-diff localization ----
A=load("w3b_final_blacksmith.png"); B2=load("w4c2_blacksmith.png")
d=np.abs(A-B2).sum(axis=2)
print("=== BS: where are the 1-6 diffs? ===")
print("per-channel max:", [int(np.abs(A[...,c]-B2[...,c]).max()) for c in range(3)])
# per-channel mean of abs
for c in range(3):
    print("  ch%d mean|d|=%.4f max=%d"%(c, np.abs(A[...,c]-B2[...,c]).mean(), np.abs(A[...,c]-B2[...,c]).max()))
# horizontal profile: mean diff per column band
for x0 in range(0,1600,200):
    band=d[:,x0:x0+200]
    print("  x[%d:%d] meanDiff=%.3f >4count=%d"%(x0,x0+200,band.mean(),(band>4).sum()))
# vertical profile
print(" vertical:")
for y0 in range(0,900,100):
    band=d[y0:y0+100,:]
    print("   y[%d:%d] meanDiff=%.3f >4count=%d"%(y0,y0+100,band.mean(),(band>4).sum()))
