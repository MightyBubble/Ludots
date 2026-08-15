#!/usr/bin/env bash
# Capture Ludots shots aligned to reference camera×time-of-day matrix.
set -euo pipefail
REPO_ROOT="$(cd "${1:-/workspace}" && pwd)"
OUT_DIR="${2:-$REPO_ROOT/artifacts/raylib-visual-atmosphere/align-matrix}"
OPT_OUT_DIR="${3:-/opt/cursor/artifacts/raylib-visual-atmosphere/align-matrix}"
REF_DIR="${4:-/opt/cursor/artifacts/reference-raylib-erosion/matrix}"

# shellcheck source=capture.sh helpers — reuse by sourcing functions via duplication of env
cd "$REPO_ROOT"
mkdir -p "$OUT_DIR" "$OPT_OUT_DIR"
export LD_LIBRARY_PATH="$REPO_ROOT/src/Platforms/Desktop:${LD_LIBRARY_PATH:-}"
export LUDOTS_RAYLIB_DISABLE_SKIA_GPU_UNDERLAY=1
export LUDOTS_RAYLIB_DISABLE_SKIA_FRAMEBUFFER_UNDERLAY=1
export LUDOTS_TAKE_SCREENSHOT_FRAME=180
export LUDOTS_AUTO_EXIT_FRAME=200
export LUDOTS_MIN_RUNTIME_MS_BEFORE_SCREENSHOT=2500

LAUNCHER="$REPO_ROOT/src/Tools/Ludots.Launcher.Cli/bin/Release/net8.0/Ludots.Launcher.Cli.dll"
dotnet build "$REPO_ROOT/mods/showcases/raylib_visual_atmosphere/RaylibVisualAtmosphereShowcaseMod/RaylibVisualAtmosphereShowcaseMod.csproj" -c Release --nologo
[[ -f "$LAUNCHER" ]] || dotnet build "$REPO_ROOT/src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj" -c Release --nologo

wait_for_file() {
  local path="$1"; local timeout_s="${2:-180}"; local start; start=$(date +%s)
  while [[ ! -f "$path" ]]; do
    if (( $(date +%s) - start > timeout_s )); then echo "ERROR: timeout $path" >&2; return 1; fi
    sleep 1
  done
  local s1 s2; s1=$(stat -c%s "$path"); sleep 1; s2=$(stat -c%s "$path")
  while [[ "$s1" != "$s2" ]]; do s1=$s2; sleep 1; s2=$(stat -c%s "$path"); done
}

# shot_id phase
SHOTS=(
  "cam_aerial__tod_dawn:0.25"
  "cam_aerial__tod_morning:0.35"
  "cam_aerial__tod_midday:0.50"
  "cam_aerial__tod_afternoon:0.65"
  "cam_aerial__tod_dusk:0.78"
  "cam_aerial__tod_night:0.92"
  "cam_orbit_ne__tod_morning:0.35"
  "cam_orbit_ne__tod_midday:0.50"
  "cam_orbit_ne__tod_dusk:0.78"
  "cam_orbit_ne__tod_night:0.92"
  "cam_orbit_sw__tod_morning:0.35"
  "cam_orbit_sw__tod_midday:0.50"
  "cam_orbit_sw__tod_dusk:0.78"
  "cam_orbit_sw__tod_night:0.92"
  "cam_shore__tod_morning:0.35"
  "cam_shore__tod_midday:0.50"
  "cam_shore__tod_dusk:0.78"
  "cam_shore__tod_night:0.92"
  "cam_water__tod_morning:0.35"
  "cam_water__tod_midday:0.50"
  "cam_water__tod_dusk:0.78"
  "cam_water__tod_night:0.92"
  "cam_veg__tod_morning:0.35"
  "cam_veg__tod_midday:0.50"
  "cam_veg__tod_dusk:0.78"
  "cam_veg__tod_night:0.92"
)

for entry in "${SHOTS[@]}"; do
  shot="${entry%%:*}"; phase="${entry##*:}"
  dest="$OUT_DIR/${shot}.png"; opt="$OPT_OUT_DIR/${shot}.png"
  log="/tmp/atm_align_${shot}.log"
  echo "=== $shot phase=$phase ==="
  rm -f "$dest" "$opt"
  export LUDOTS_ATMOSPHERE_SHOT="$shot"
  export LUDOTS_DAY_PHASE="$phase"
  export LUDOTS_TAKE_SCREENSHOT_PATH="$dest"
  export LUDOTS_RAYLIB_DIAGNOSTIC_PATH="${dest%.png}.diag.txt"
  set +e
  dotnet exec "$LAUNCHER" launch raylib_visual_atmosphere --adapter raylib --build auto >"$log" 2>&1
  rc=$?
  set -e
  if ! wait_for_file "$dest" 180; then
    echo "ERROR missing $dest rc=$rc" >&2; tail -n 60 "$log" >&2; exit 3
  fi
  pgrep -af 'Ludots.App.Raylib.dll' | awk '/dotnet/ {print $1}' | xargs -r kill 2>/dev/null || true
  sleep 1
  cp -f "$dest" "$opt"
  [[ -f "${dest%.png}.diag.txt" ]] && cp -f "${dest%.png}.diag.txt" "${opt%.png}.diag.txt"
done

# Side-by-side with reference when available
COMPARE="$OPT_OUT_DIR/compare"
mkdir -p "$COMPARE" "$OUT_DIR/compare"
if [[ -d "$REF_DIR" ]]; then
  for f in "$OPT_OUT_DIR"/cam_*.png; do
    base=$(basename "$f")
    ref="$REF_DIR/$base"
    [[ -f "$ref" ]] || continue
    convert "$ref" -resize '640x360^' -gravity center -extent 640x360 -gravity NorthWest -fill white -undercolor '#00000080' -pointsize 16 -annotate +8+8 "REF $base" "$COMPARE/ref_$base"
    convert "$f" -resize '640x360^' -gravity center -extent 640x360 -gravity NorthWest -fill white -undercolor '#00000080' -pointsize 16 -annotate +8+8 "LUD $base" "$COMPARE/lud_$base"
    convert "$COMPARE/ref_$base" "$COMPARE/lud_$base" +append "$COMPARE/pair_$base"
  done
  # contact sheet of pairs
  shopt -s nullglob
  pairs=("$COMPARE"/pair_*.png)
  if ((${#pairs[@]})); then
    montage "${pairs[@]}" -tile 2x -geometry +4+4 -background '#111' "$COMPARE/contact_sheet.png"
    cp -f "$COMPARE/contact_sheet.png" "$OUT_DIR/compare/" 2>/dev/null || true
  fi
fi

echo "OK align matrix → $OUT_DIR and $OPT_OUT_DIR"
ls "$OPT_OUT_DIR" | wc -l
