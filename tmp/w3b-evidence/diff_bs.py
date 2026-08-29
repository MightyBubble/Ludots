from PIL import Image
import numpy as np

def load(p):
    return np.asarray(Image.open(p).convert('RGB')).astype(np.int16)

A=load("w3b_final_blacksmith.png"); B=load("w4c_blacksmith.png")
d=np.abs(A-B).sum(axis=2)
print("=== BLACKSMITH: characterize tiny diff (max=6) ===")
# Is it a global color shift or a positional shift?
# Check diff sign per channel at pixels with diff
mask=d>0
ys,xs=np.where(mask)
# sample: are diffs + or -?
diffA=A-B
pos=(diffA>0).sum(axis=2)[mask]
neg=(diffA<0).sum(axis=2)[mask]
print("count >0 channels:",int(pos.sum())," count <0 channels:",int(neg.sum()))
# spatial distribution of diff - row/col histogram
colhist=mask.sum(axis=0); rowhist=mask.sum(axis=1)
# is it uniform across image or localized?
print("rows with >1% diff pixels:", np.where(rowhist>mask.shape[1]*0.01)[0].min(), np.where(rowhist>mask.shape[1]*0.01)[0].max())
print("cols with >1% diff pixels:", np.where(colhist>mask.shape[0]*0.01)[0].min(), np.where(colhist>mask.shape[0]*0.01)[0].max())
# mean diff value where nonzero
print("mean diff where nonzero:", float(d[mask].mean()))
print("median diff where nonzero:", float(np.median(d[mask])))

# Test hypothesis: 1px vertical shift
for sh in [-1,1]:
    Bs=np.roll(B,sh,axis=0)
    ds=np.abs(A-Bs).sum(axis=2)
    print(f"  shift v{sh}: nonzero {100*(ds>0).mean():.2f}% mean {ds.mean():.3f}")
for sh in [-1,1]:
    Bs=np.roll(B,sh,axis=1)
    ds=np.abs(A-Bs).sum(axis=2)
    print(f"  shift h{sh}: nonzero {100*(ds>0).mean():.2f}% mean {ds.mean():.3f}")

