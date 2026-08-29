from PIL import Image
import numpy as np
B="C:/001_AI/LudotsProd/tmp/w3b-evidence/"
def load(p): return np.asarray(Image.open(B+p).convert('RGB')).astype(np.int16)

# ---------- BLACKSMITH: confirm shared entities, look for any global shift ----------
A=load("w3b_final_blacksmith.png"); B2=load("w4c2_blacksmith.png")
# Try: is A == B2 shifted by (dx,dy) subpixel? Compare max correlation on small offsets.
# Instead, sample a high-texture feature: the health bar red/green line and the rock.
# Compute centroid of "yellow island" mask in each.
def mask_centroid(img, rmin,gmin,bmin,rmax,gmax,bmax,label):
    m=(img[...,0]>=rmin)&(img[...,0]<=rmax)&(img[...,1]>=gmin)&(img[...,1]<=gmax)&(img[...,2]>=bmin)&(img[...,2]<=bmax)
    ys,xs=np.where(m)
    if len(ys)==0: print("  %s: not found"%(label)); return
    print("  %s: n=%d centroid=(%.2f,%.2f) area=%d"%(label,len(ys),xs.mean(),ys.mean(),len(ys)))
print("=== BS entity presence (both images) ===")
print("Yellow island:")
mask_centroid(A,200,200,0,255,255,120,"A-before")
mask_centroid(B2,200,200,0,255,255,120,"B-after")
print("Gray rock (dark gray):")
mask_centroid(A,100,100,110,160,160,170,"A-before")
mask_centroid(B2,100,100,110,160,160,170,"B-after")
print("Green health bar (green):")
mask_centroid(A,0,150,0,120,220,120,"A-before")
mask_centroid(B2,0,150,0,120,220,120,"B-after")
print("Red number/indicator (red):")
mask_centroid(A,180,0,0,255,90,90,"A-before")
mask_centroid(B2,180,0,0,255,90,90,"B-after")
print("Yellow cube (small isolated):")
mask_centroid(A,190,190,0,255,255,110,"A-before")
mask_centroid(B2,190,190,0,255,255,110,"B-after")

# ---------- ATMOSPHERE: locate the big-diff clusters ----------
C=load("w3b_atmosphere.png"); D=load("w4c2_atmosphere.png")
d2=np.abs(C-D).sum(axis=2)
print("\n=== ATMO largest-diff clusters ===")
# Threshold >60 (strong structural change)
for th in [60,120]:
    ys,xs=np.where(d2>th)
    print(" diff>%d: n=%d bbox x[%d..%d] y[%d..%d]"%(th,len(ys),xs.min(),xs.max(),ys.min(),ys.max()))
# The max-182 pixel location
yy,xx=np.unravel_index(np.argmax(d2),d2.shape)
print(" max-diff pixel at (x=%d,y=%d), before RGB=%s after RGB=%s"%(xx,yy,C[yy,xx].tolist(),D[yy,xx].tolist()))
# Where are diffs>30 concentrated -> heat by region (island vs ocean)
ys,xs=np.where(d2>30)
import collections
# grid 8x8 over 1600x900
grid=collections.Counter(( (ys//(900//8))*(8) + (xs//(1600//8)) ))
print(" diff>30 grid distribution (row-major, 8x8, top bins):")
tot=len(ys)
for k,v in grid.most_common(10):
    r,c=divmod(k,8)
    print("   grid r%d,c%d (y~%d,x~%d): %d"%(r,c,(r*900//8),(c*1600//8),v))
# Check island silhouette integrity: mask "green land" and "tan sand" and "gray rock peak"
print("Land presence:")
def land_stats(img,label):
    # green vegetation
    g=(img[...,1].astype(int)>img[...,0].astype(int)+10)&(img[...,1]>60)&(img[...,1]>img[...,2].astype(int)+5)
    # tan/sand
    s=(img[...,0]>150)&(img[...,1]>120)&(img[...,2]<150)&(np.abs(img[...,0].astype(int)-img[...,1])<60)
    print("  %s: green px=%d sand px=%d"%(label,g.sum(),s.sum()))
land_stats(C,"before"); land_stats(D,"after")
