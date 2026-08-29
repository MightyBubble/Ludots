from PIL import Image
import numpy as np

def load(p):
    return np.asarray(Image.open(p).convert('RGB')).astype(np.int16)

pairs = [
    ("w3b_final_blacksmith.png","w4c_blacksmith.png","blacksmith"),
    ("w3b_atmosphere.png","w4c_atmosphere.png","atmosphere"),
]

for a,b,name in pairs:
    A=load(a); B=load(b)
    print(f"\n=== {name} ===")
    print("A",A.shape,"B",B.shape)
    if A.shape!=B.shape:
        h=min(A.shape[0],B.shape[0]); w=min(A.shape[1],B.shape[1])
        A=A[:h,:w]; B=B[:h,:w]
        print("cropped to",A.shape)
    d=np.abs(A-B).sum(axis=2)  # 0..765
    total=d>0
    tol10=d>10
    print("total px:",d.size)
    print("nonzero diff px:",int(total.sum()), f"{100*total.sum()/d.size:.4f}%")
    print("diff>10 px:", int(tol10.sum()), f"{100*tol10.sum()/d.size:.4f}%")
    print("mean abs diff (all):", float(d.mean()))
    print("max diff:", int(d.max()))
    big=d>30
    ys,xs=np.where(big)
    if len(xs):
        print("big-diff>30 bbox: x",xs.min(),xs.max(),"y",ys.min(),ys.max(), "count",len(xs))
        colhist=big.sum(axis=0); rowhist=big.sum(axis=1)
        busyc=np.where(colhist> big.shape[0]*0.02)[0]
        busyr=np.where(rowhist> big.shape[1]*0.02)[0]
        print("  active col range:", (busyc.min(),busyc.max()) if len(busyc) else None)
        print("  active row range:", (busyr.min(),busyr.max()) if len(busyr) else None)
    else:
        print("no pixels with diff>30")
