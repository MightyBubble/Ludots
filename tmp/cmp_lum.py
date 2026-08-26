from PIL import Image
import numpy as np
from pathlib import Path

base = Path(r"C:\001_AI\LudotsProd-east-asia-playable-terrain\src\Apps\Raylib\Ludots.App.Raylib\bin\Release\net8.0\artifacts\_shot")
for n in ["diag_noon_p50_001_f0600.png", "diag_mipsky_noon_001_f0600.png"]:
    a = np.asarray(Image.open(base / n).convert("RGB"), dtype=np.float32)
    lum = a.mean(axis=2)
    nz = lum[lum > 4]
    print(n, "mean=%.1f" % lum.mean(), "nonblack mean=%.1f" % (nz.mean() if len(nz) else 0))

im = Image.open(base / "diag_mipsky_noon_001_f0600.png")
im.thumbnail((1300, 1300))
im.convert("RGB").save(r"C:\001_AI\LudotsProd\tmp\diag_mipsky.jpg", quality=82)
