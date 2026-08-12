#!/usr/bin/env bash
# Capture Raylib visual-atmosphere acceptance PNGs via the playable showcase host loop.
# Usage:
#   tools/raylib_visual_atmosphere_acceptance/capture.sh [repoRoot] [outDir] [optOutDir]
set -euo pipefail

REPO_ROOT="$(cd "${1:-/workspace}" && pwd)"
OUT_DIR="${2:-$REPO_ROOT/artifacts/raylib-visual-atmosphere/acceptance}"
OPT_OUT_DIR="${3:-/opt/cursor/artifacts/raylib-visual-atmosphere/acceptance}"
# Raylib host WorkingDirectory is the app output dir; screenshot path must be absolute.
[[ "$OUT_DIR" = /* ]] || OUT_DIR="$REPO_ROOT/$OUT_DIR"
[[ "$OPT_OUT_DIR" = /* ]] || OPT_OUT_DIR="$(cd "$(dirname "$OPT_OUT_DIR")" && pwd)/$(basename "$OPT_OUT_DIR")"

cd "$REPO_ROOT"
mkdir -p "$OUT_DIR" "$OPT_OUT_DIR"

export LD_LIBRARY_PATH="$REPO_ROOT/src/Platforms/Desktop:${LD_LIBRARY_PATH:-}"
export LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UNDERLAY=1
export LUDOTS_RAYLIB_DISABLE_SKIA_FRAMEBUFFER_UNDERLAY=1
export LUDOTS_TAKE_SCREENSHOT_FRAME=180
export LUDOTS_AUTO_EXIT_FRAME=200
export LUDOTS_MIN_RUNTIME_MS_BEFORE_SCREENSHOT=2500

LAUNCHER="$REPO_ROOT/src/Tools/Ludots.Launcher.Cli/bin/Release/net8.0/Ludots.Launcher.Cli.dll"
if [[ ! -f "$LAUNCHER" ]]; then
  echo "Building launcher..."
  dotnet build "$REPO_ROOT/src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj" -c Release --nologo
fi

dotnet build "$REPO_ROOT/mods/showcases/raylib_visual_atmosphere/RaylibVisualAtmosphereShowcaseMod/RaylibVisualAtmosphereShowcaseMod.csproj" -c Release --nologo

wait_for_file() {
  local path="$1"
  local timeout_s="${2:-120}"
  local start
  start=$(date +%s)
  while [[ ! -f "$path" ]]; do
    if (( $(date +%s) - start > timeout_s )); then
      echo "ERROR: timed out waiting for $path" >&2
      return 1
    fi
    # keep waiting while Raylib app is alive, or a few seconds after launcher returns
    sleep 1
  done
  # ensure file is finished writing
  local size1 size2
  size1=$(stat -c%s "$path")
  sleep 1
  size2=$(stat -c%s "$path")
  while [[ "$size1" != "$size2" ]]; do
    size1=$size2
    sleep 1
    size2=$(stat -c%s "$path")
  done
}

capture_one() {
  local file="$1"
  local shot="$2"
  local phase="$3"
  local dest="$OUT_DIR/$file"
  local opt="$OPT_OUT_DIR/$file"
  local log="/tmp/atm_capture_${shot}.log"
  echo "=== Capturing $file (shot=$shot phase=$phase) ==="
  rm -f "$dest" "$opt" "${dest%.png}.diag.txt"
  export LUDOTS_ATMOSPHERE_SHOT="$shot"
  export LUDOTS_DAY_PHASE="$phase"
  export LUDOTS_TAKE_SCREENSHOT_PATH="$dest"
  export LUDOTS_RAYLIB_DIAGNOSTIC_PATH="${dest%.png}.diag.txt"

  # Launcher may return before the Raylib host exits; wait on the PNG.
  set +e
  dotnet exec "$LAUNCHER" launch raylib_visual_atmosphere --adapter raylib --build auto >"$log" 2>&1
  local launch_rc=$?
  set -e

  if ! wait_for_file "$dest" 180; then
    echo "ERROR: screenshot missing for $file (launcher_rc=$launch_rc)" >&2
    tail -n 80 "$log" >&2 || true
    exit 3
  fi

  # Drain leftover host from this shot (match dll path only; avoid killing this script).
  pgrep -af 'Ludots.App.Raylib.dll' | awk '/dotnet/ {print $1}' | xargs -r kill 2>/dev/null || true
  sleep 1

  python3 - "$dest" <<'PY'
import sys, struct, zlib
path = sys.argv[1]
data = open(path, 'rb').read()
assert data[:8] == b'\x89PNG\r\n\x1a\n', path
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
stride = w * 4
pixels = bytearray()
for y in range(h):
    row = raw[1 + y*(stride+1) : 1 + y*(stride+1) + stride]
    pixels.extend(row)
n = w * h
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
# Reject only near-empty clears (camera inside opaque geometry still fails day/night delta checks).
if mean < 0.8 or frac < 0.004:
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

# Blend shot must draw both AlphaBlend and Additive billboards (host asset URIs must not collide).
if ! rg -q 'blend=AlphaBlend' "$OUT_DIR/04_blend_modes.diag.txt"; then
  echo "ERROR: 04_blend_modes.diag.txt missing AlphaBlend draw evidence" >&2
  exit 5
fi
if ! rg -q 'blend=Additive' "$OUT_DIR/04_blend_modes.diag.txt"; then
  echo "ERROR: 04_blend_modes.diag.txt missing Additive draw evidence (check host_assets id uniqueness)" >&2
  exit 5
fi

# Water shot must prove reflective pass was active (diag + non-black water).
if ! rg -q 'Framebuffer object created successfully' /tmp/atm_capture_06_water_reflect.log; then
  echo "ERROR: water reflect capture log missing reflective FBO evidence" >&2
  exit 4
fi

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
