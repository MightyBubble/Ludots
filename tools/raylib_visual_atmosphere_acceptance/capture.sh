#!/usr/bin/env bash
# Capture Raylib visual-atmosphere acceptance PNGs via the playable showcase host loop.
# Usage:
#   tools/raylib_visual_atmosphere_acceptance/capture.sh [repoRoot] [outDir] [optOutDir]
set -euo pipefail

REPO_ROOT="${1:-/workspace}"
OUT_DIR="${2:-$REPO_ROOT/artifacts/raylib-visual-atmosphere/acceptance}"
OPT_OUT_DIR="${3:-/opt/cursor/artifacts/raylib-visual-atmosphere/acceptance}"

cd "$REPO_ROOT"
mkdir -p "$OUT_DIR" "$OPT_OUT_DIR"

export LD_LIBRARY_PATH="$REPO_ROOT/src/Platforms/Desktop:${LD_LIBRARY_PATH:-}"
export LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UNDERLAY=1
export LUDOTS_RAYLIB_DISABLE_SKIA_FRAMEBUFFER_UNDERLAY=1
export LUDOTS_TAKE_SCREENSHOT_FRAME=90
export LUDOTS_AUTO_EXIT_FRAME=100
export LUDOTS_MIN_RUNTIME_MS_BEFORE_SCREENSHOT=1500

LAUNCHER="$REPO_ROOT/src/Tools/Ludots.Launcher.Cli/bin/Release/net8.0/Ludots.Launcher.Cli.dll"
if [[ ! -f "$LAUNCHER" ]]; then
  echo "Building launcher..."
  dotnet build "$REPO_ROOT/src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj" -c Release --nologo
fi

dotnet build "$REPO_ROOT/mods/showcases/raylib_visual_atmosphere/RaylibVisualAtmosphereShowcaseMod/RaylibVisualAtmosphereShowcaseMod.csproj" -c Release --nologo

capture_one() {
  local file="$1"
  local shot="$2"
  local phase="$3"
  local dest="$OUT_DIR/$file"
  local opt="$OPT_OUT_DIR/$file"
  echo "=== Capturing $file (shot=$shot phase=$phase) ==="
  rm -f "$dest" "$opt"
  export LUDOTS_ATMOSPHERE_SHOT="$shot"
  export LUDOTS_DAY_PHASE="$phase"
  export LUDOTS_TAKE_SCREENSHOT_PATH="$dest"
  export LUDOTS_RAYLIB_DIAGNOSTIC_PATH="${dest%.png}.diag.txt"

  set +e
  dotnet exec "$LAUNCHER" launch raylib_visual_atmosphere --adapter raylib --build auto
  local rc=$?
  set -e
  if [[ $rc -ne 0 ]]; then
    echo "ERROR: launcher exit $rc for $file" >&2
    if [[ -f "${dest%.png}.diag.txt" ]]; then
      tail -n 80 "${dest%.png}.diag.txt" >&2 || true
    fi
    exit $rc
  fi
  if [[ ! -f "$dest" ]]; then
    echo "ERROR: screenshot missing: $dest" >&2
    exit 3
  fi
  # Fail-loud: pure black / near-empty frames are not atmosphere evidence.
  python3 - "$dest" <<'PY'
import sys, struct, zlib
path = sys.argv[1]
data = open(path, 'rb').read()
assert data[:8] == b'\x89PNG\r\n\x1a\n', path
# parse IHDR + IDAT
off = 8
w = h = None
idat = b''
while off + 8 <= len(data):
    length = struct.unpack('>I', data[off:off+4])[0]
    tag = data[off+4:off+8]
    chunk = data[off+8:off+8+length]
    off += 12 + length
    if tag == b'IHDR':
        w, h = struct.unpack('>II', chunk[:8])
    elif tag == b'IDAT':
        idat += chunk
    elif tag == b'IEND':
        break
raw = zlib.decompress(idat)
# assume RGBA8
stride = w * 4
pixels = bytearray()
for y in range(h):
    row = raw[1 + y*(stride+1) : 1 + y*(stride+1) + stride]
    pixels.extend(row)
n = w * h
# mean luminance
acc = 0
nonzero = 0
for i in range(0, len(pixels), 4):
    r, g, b, a = pixels[i:i+4]
    lum = (r + g + b) / 3
    acc += lum
    if lum > 8:
        nonzero += 1
mean = acc / max(1, n)
frac = nonzero / max(1, n)
print(f'{path}: {w}x{h} meanLum={mean:.1f} litFrac={frac:.3f}')
if mean < 4.0 or frac < 0.02:
    raise SystemExit(f'ERROR: {path} looks near-black (meanLum={mean:.1f}, litFrac={frac:.3f})')
PY
  cp -f "$dest" "$opt"
  if [[ -f "${dest%.png}.diag.txt" ]]; then
    cp -f "${dest%.png}.diag.txt" "${opt%.png}.diag.txt"
  fi
}

capture_one "01_sky_day.png" "01_sky_day" "0.42"
capture_one "02_sky_night.png" "02_sky_night" "0.92"
capture_one "03_cutout_vegetation.png" "03_cutout_vegetation" "0.42"
capture_one "04_blend_modes.png" "04_blend_modes" "0.42"
capture_one "05_distance_fog.png" "05_distance_fog" "0.42"
capture_one "06_water_reflect.png" "06_water_reflect" "0.42"

# Day/night must visibly differ
python3 - "$OUT_DIR/01_sky_day.png" "$OUT_DIR/02_sky_night.png" <<'PY'
import sys, struct, zlib
def load(path):
    data = open(path,'rb').read()
    off=8; w=h=None; idat=b''
    while off+8<=len(data):
        length=struct.unpack('>I', data[off:off+4])[0]
        tag=data[off+4:off+8]; chunk=data[off+8:off+8+length]; off+=12+length
        if tag==b'IHDR': w,h=struct.unpack('>II', chunk[:8])
        elif tag==b'IDAT': idat+=chunk
        elif tag==b'IEND': break
    raw=zlib.decompress(idat); stride=w*4
    pixels=bytearray()
    for y in range(h):
        pixels.extend(raw[1+y*(stride+1):1+y*(stride+1)+stride])
    return w,h,pixels
w1,h1,p1=load(sys.argv[1]); w2,h2,p2=load(sys.argv[2])
assert (w1,h1)==(w2,h2)
diff=0; n=w1*h1
for i in range(0,len(p1),4):
    diff += abs(p1[i]-p2[i])+abs(p1[i+1]-p2[i+1])+abs(p1[i+2]-p2[i+2])
mean=diff/(n*3)
print(f'day/night mean channel delta={mean:.2f}')
if mean < 3.0:
    raise SystemExit(f'ERROR: day/night screenshots too similar (mean delta={mean:.2f})')
PY

REPORT="$OUT_DIR/capture-report.md"
{
  echo "# Raylib visual atmosphere acceptance capture"
  echo
  echo "Host: playable showcase \`raylib_visual_atmosphere\` (env-driven screenshots)."
  echo
  for f in 01_sky_day.png 02_sky_night.png 03_cutout_vegetation.png 04_blend_modes.png 05_distance_fog.png 06_water_reflect.png; do
    echo "- \`$f\` → \`$OUT_DIR/$f\` and \`$OPT_OUT_DIR/$f\`"
  done
} > "$REPORT"
cp -f "$REPORT" "$OPT_OUT_DIR/capture-report.md"
echo "OK: wrote acceptance shots to $OUT_DIR and $OPT_OUT_DIR"
cat "$REPORT"
