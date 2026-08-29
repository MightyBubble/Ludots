from PIL import Image
import numpy as np

def load(p):
    return np.asarray(Image.open(p).convert('RGB')).astype(np.int16)

# ATMOSPHERE region analysis
A=load("w3b_atmosphere.png"); B=load("w4c_atmosphere.png")
d=np.abs(A-B).sum(axis=2)
# content area y ~180..899, x 0..1279
# island center approx x 300..1000, y 300..820 (from images)
isl_sl=slice(300,820); isl_sr=slice(300,1000)
isl=d[isl_sl,isl_sr]
# water band left x 0..250
wtr=d[180:899,0:250]
print("ATMO island-region: px=%d, >30 count=%d (%.4f%%), max=%d, mean=%.3f"%(
    isl.size,(isl>30).sum(),100*(isl>30).mean(),isl.max(),isl.mean()))
print("ATMO water-left   : px=%d, >30 count=%d (%.4f%%), max=%d, mean=%.3f"%(
    wtr.size,(wtr>30).sum(),100*(wtr>30).mean(),wtr.max(),wtr.mean()))
# Identify if any big diff overlaps island silhouette: make mask of B being green/brown(terrain)
# instead check structural: are there any CONTIGUOUS blobs (not grain) in island?
big=(d>30)
# erosion to find solid blobs (water grain won't survive erosion)
from scipy import ndimage
er=ndimage.binary_erosion(big,iterations=3)
print("ATMO big blobs surviving 3-erosion (solid/structural):", int(er.sum()), "px")

# BLACKSMITH object region
A=load("w3b_final_blacksmith.png"); B=load("w4c_blacksmith.png")
d=np.abs(A-B).sum(axis=2)
# object cluster around x 500..800 y 400..600
obj=d[400:620,480:820]
print("BS object-region: px=%d, max=%d, mean=%.3f, >6 count=%d"%(
    obj.size,obj.max(),obj.mean(),(obj>6).sum()))
# water remote region far right
bsr=d[300:700,1000:1200]
print("BS water-region : max=%d, mean=%.3f, >6 count=%d"%(bsr.max(),bsr.mean(),(bsr>6).sum()))
