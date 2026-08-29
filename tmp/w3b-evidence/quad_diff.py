from PIL import Image
import numpy as np

def load(p):
    return np.asarray(Image.open(p).convert('RGB')).astype(np.int16)

print("=== size check ===")
for p in ["w3b_final_blacksmith.png","w4c2_blacksmith.png","C:/001_AI/LudotsProd/tmp/w3b-evidence/w3b_atmosphere.png","C:/001_AI/LudotsProd/tmp/w3b-evidence/w4c2_atmosphere.png"]:
    im=Image.open(p); print(p, im.size, im.mode)

# ---- BLACKSMITH pair: 图1 vs 图2 ----
A=load("w3b_final_blacksmith.png"); B=load("w4c2_blacksmith.png")
h=min(A.shape[0],B.shape[0]); w=min(A.shape[1],B.shape[1])
A=A[:h,:w]; B=B[:h,:w]
d=np.abs(A-B).sum(axis=2)
print("\n=== BLACKSMITH (before vs after) ===")
print("frame %dx%d, total px = %d"%(w,h,A.shape[0]*A.shape[1]))
print("diff==0 px: %d (%.5f%%)"%( (d==0).sum(), 100*(d==0).mean()))
print("diff>0 px: %d (%.5f%%)"%( (d>0).sum(), 100*(d>0).mean()))
print("max abs diff: %d"%d.max())
print("mean abs diff: %.5f"%d.mean())
for th in [1,3,6,10,30,60]:
    print("  px diff>%d: %d (%.5f%%)"%(th,(d>th).sum(),100*(d>th).mean()))
# locate where the highest diffs are
ys,xs=np.where(d>6)
if len(ys):
    print("diff>6 bbox: x[%d..%d] y[%d..%d], n=%d"%(xs.min(),xs.max(),ys.min(),ys.max(),len(ys)))
    # cluster by region
    print("distinct high-diff areas (y histogram bins):")
    import collections
    yb=collections.Counter((ys//40)*40)
    for yy in sorted(yb): print("   y~%d: %d px"%(yy,yb[yy]))
else:
    print("No pixels with diff>6")

# ---- ATMOSPHERE pair: 图3 vs 图4 ----
print("\n=== ATMOSPHERE (before vs after) ===")
C=load("C:/001_AI/LudotsProd/tmp/w3b-evidence/w3b_atmosphere.png"); D=load("C:/001_AI/LudotsProd/tmp/w3b-evidence/w4c2_atmosphere.png")
h2=min(C.shape[0],D.shape[0]); w2=min(C.shape[1],D.shape[1])
C=C[:h2,:w2]; D=D[:h2,:w2]
d2=np.abs(C-D).sum(axis=2)
print("frame %dx%d"%(w2,h2))
print("diff==0 px: %d (%.4f%%)"%( (d2==0).sum(), 100*(d2==0).mean()))
print("diff>0 px: %d (%.4f%%)"%( (d2>0).sum(), 100*(d2>0).mean()))
print("max abs diff: %d"%d2.max())
print("mean abs diff: %.4f"%d2.mean())
for th in [1,3,10,30,60,120]:
    print("  px diff>%d: %d (%.5f%%)"%(th,(d2>th).sum(),100*(d2>th).mean()))

# region split: island interior vs ocean. Island roughly centered.
ih0,ih1=300,820; iw0,iw1=320,1000
island=d2[ih0:ih1,iw0:iw1]
ocean = np.concatenate([d2[:ih0,:w2].ravel(), d2[ih1:,:w2].ravel()])
print("island-interior region [y%d:%d,x%d:%d]: mean=%.3f max=%d >30:%d"%(
    ih0,ih1,iw0,iw1,island.mean(),island.max(),(island>30).sum()))
print("ocean region (top+bottom bands): mean=%.3f max=%d"%(ocean.mean(),ocean.max()))
