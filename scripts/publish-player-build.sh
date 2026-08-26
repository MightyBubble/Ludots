#!/usr/bin/env bash
# 玩家零安装发行包（Unix）：与 scripts/publish-player-build.ps1 同契约。
# 用法：
#   ./scripts/publish-player-build.sh
#   ./scripts/publish-player-build.sh --mods ExampleMod
#   ./scripts/publish-player-build.sh --rid linux-x64 --out dist/player
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
TFM=net9.0
OUT="dist/player"
RID=""
DEFAULT_SELECTOR="mod:LudotsCoreMod mod:ExampleMod"
MODS=()
SKIP_ASSETS=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --out) OUT="$2"; shift 2 ;;
    --rid) RID="$2"; shift 2 ;;
    --mods) shift; while [[ $# -gt 0 && "$1" != --* ]]; do MODS+=("$1"); shift; done ;;
    --skip-assets) SKIP_ASSETS=1; shift ;;
    -h|--help)
      echo "usage: $0 [--out dir] [--rid rid] [--mods Name ...] [--skip-assets]"
      exit 0
      ;;
    *) echo "unknown arg: $1" >&2; exit 2 ;;
  esac
done

detect_rid() {
  case "$(uname -s)" in
    Linux*) echo linux-x64 ;;
    Darwin*)
      if [[ "$(uname -m)" == arm64 ]]; then echo osx-arm64; else echo osx-x64; fi
      ;;
    *) echo "unsupported OS for auto RID: $(uname -s)" >&2; exit 1 ;;
  esac
}

[[ -n "$RID" ]] || RID="$(detect_rid)"
PKG="$ROOT/$OUT"

copy_tree_filtered() {
  local src="$1" dst="$2"
  shift 2
  mkdir -p "$dst"
  # rsync if available; else python fallback
  if command -v rsync >/dev/null 2>&1; then
    local excludes=()
    for x in "$@"; do excludes+=(--exclude "$x"); done
    rsync -a "${excludes[@]}" "$src"/ "$dst"/
  else
    python3 - "$src" "$dst" "$@" <<'PY'
import os, shutil, sys
src, dst, *patterns = sys.argv[1:]
os.makedirs(dst, exist_ok=True)
exclude_ext = {p[1:] for p in patterns if p.startswith("*.")}
exclude_dir = {p for p in patterns if not p.startswith("*.")}
for root, dirs, files in os.walk(src):
    dirs[:] = [d for d in dirs if d not in exclude_dir]
    rel = os.path.relpath(root, src)
    out_root = dst if rel == "." else os.path.join(dst, rel)
    os.makedirs(out_root, exist_ok=True)
    for f in files:
        ext = os.path.splitext(f)[1]
        if ext.lstrip(".") in {e.lstrip(".") for e in exclude_ext} or ext in exclude_ext:
            # patterns like *.cs
            if any(f.endswith(p[1:]) for p in patterns if p.startswith("*.")):
                continue
        if any(f.endswith(p[1:]) for p in patterns if p.startswith("*.")):
            continue
        shutil.copy2(os.path.join(root, f), os.path.join(out_root, f))
PY
  fi
}

echo "== Ludots player build (unix) =="
echo "repo: $ROOT"
echo "out:  $PKG"
echo "rid:  $RID"

# Collect mod dirs
mapfile -t ALL_MANIFESTS < <(find mods -type f -name mod.json ! -path 'mods/fixtures/*' ! -path '*/bin/*' ! -path '*/obj/*')
declare -A BY_NAME=()
for m in "${ALL_MANIFESTS[@]}"; do
  dir="$(dirname "$m")"
  name="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8-sig"))["name"])' "$m")"
  BY_NAME["$name"]="$dir"
done

WANTED=()
if [[ ${#MODS[@]} -gt 0 ]]; then
  queue=("${MODS[@]}" LudotsCoreMod)
  declare -A SEEN=()
  while [[ ${#queue[@]} -gt 0 ]]; do
    n="${queue[0]}"; queue=("${queue[@]:1}")
    [[ -n "${SEEN[$n]:-}" ]] && continue
    SEEN["$n"]=1
    [[ -n "${BY_NAME[$n]:-}" ]] || { echo "mod not found for closure: $n" >&2; exit 1; }
    WANTED+=("${BY_NAME[$n]}")
    while IFS= read -r dep; do
      [[ -n "$dep" ]] && queue+=("$dep")
    done < <(python3 -c 'import json,sys; d=json.load(open(sys.argv[1], encoding="utf-8-sig")).get("dependencies") or {}; print("\n".join(d))' "${BY_NAME[$n]}/mod.json")
  done
else
  for m in "${ALL_MANIFESTS[@]}"; do WANTED+=("$(dirname "$m")"); done
fi

# Fail closed if BOM/parse left BY_NAME empty unexpectedly when mods requested
if [[ ${#MODS[@]} -gt 0 && ${#WANTED[@]} -eq 0 ]]; then
  echo "error: mod closure resolved empty" >&2
  exit 1
fi

# Unique
mapfile -t WANTED < <(printf '%s\n' "${WANTED[@]}" | awk 'NF && !s[$0]++')

rm -rf "$PKG"
mkdir -p "$PKG/mods"

for dir in "${WANTED[@]}"; do
  name="$(basename "$dir")"
  csproj="$(find "$dir" -maxdepth 1 -name '*.csproj' | head -1 || true)"
  dst="$PKG/mods/$name"
  if [[ -n "$csproj" ]]; then
    echo "build mod: $name"
    dotnet build "$csproj" -c Release -v q
    if command -v rsync >/dev/null 2>&1; then
      mkdir -p "$dst"
      rsync -a --exclude '*.cs' --exclude '*.csproj' --exclude 'obj' --exclude 'Debug' "$dir"/ "$dst"/
    else
      copy_tree_filtered "$dir" "$dst" '*.cs' '*.csproj' obj Debug
    fi
    [[ -f "$dst/bin/$TFM/$name.dll" ]] || { echo "missing main dll: $dst/bin/$TFM/$name.dll" >&2; exit 1; }
  else
    if command -v rsync >/dev/null 2>&1; then
      mkdir -p "$dst"
      rsync -a --exclude '*.cs' --exclude '*.csproj' --exclude 'obj' --exclude 'bin' "$dir"/ "$dst"/
    else
      copy_tree_filtered "$dir" "$dst" '*.cs' '*.csproj' obj bin
    fi
  fi
done

APP_OUT="$PKG/src/Apps/Raylib/Ludots.App.Raylib/bin/Release/$TFM"
LAUNCHER_OUT="$PKG/tools/launcher"
dotnet publish src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj -c Release -r "$RID" --self-contained true -o "$APP_OUT" -v q
dotnet publish src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -c Release -r "$RID" --self-contained true -o "$LAUNCHER_OUT" -v q

[[ -x "$LAUNCHER_OUT/Ludots.Launcher.Cli" || -f "$LAUNCHER_OUT/Ludots.Launcher.Cli" ]] || {
  echo "error: launcher apphost missing after publish: $LAUNCHER_OUT/Ludots.Launcher.Cli" >&2
  exit 1
}
[[ -f "$APP_OUT/Ludots.App.Raylib.dll" ]] || {
  echo "error: app assembly missing after publish: $APP_OUT/Ludots.App.Raylib.dll" >&2
  exit 1
}

if [[ "$SKIP_ASSETS" -eq 0 ]]; then
  mkdir -p "$PKG/assets"
  if command -v rsync >/dev/null 2>&1; then
    rsync -a --exclude bin --exclude obj assets/ "$PKG/assets/"
  else
    copy_tree_filtered assets "$PKG/assets" bin obj
  fi
fi
cp launcher.config.json "$PKG/"
[[ -f launcher.presets.json ]] && cp launcher.presets.json "$PKG/" || echo "note: launcher.presets.json absent, skipped"

LAUNCHER_NAME=Ludots.Launcher.Cli
cat > "$PKG/Play.sh" <<EOF
#!/usr/bin/env bash
set -euo pipefail
ROOT="\$(cd "\$(dirname "\$0")" && pwd)"
exec "\$ROOT/tools/launcher/$LAUNCHER_NAME" launch --adapter raylib --build never $DEFAULT_SELECTOR "\$@"
EOF
chmod +x "$PKG/Play.sh"
chmod +x "$LAUNCHER_OUT/$LAUNCHER_NAME" || true
chmod +x "$APP_OUT/Ludots.App.Raylib" || true

cat > "$PKG/README-PLAYER.md" <<EOF
# Ludots Player Build

- 入口：./Play.sh（默认 $DEFAULT_SELECTOR）。
- 进阶：./tools/launcher/$LAUNCHER_NAME launch --adapter raylib --build never <selectors...>
- RID=$RID；自包含运行时；mods 为 BinaryOnly。
EOF

SIZE=$(du -sm "$PKG" | awk '{print $1}')
echo "done: $PKG (${SIZE} MB), rid=$RID"
