#!/usr/bin/env bash
# 作者工作室一键启动：Bridge + 蓝图/行为树/状态机/对话/时间轴。
# 不启动地图编辑、面板、场编辑。
# 用法：
#   ./scripts/run-authoring-studio.sh
#   ./scripts/run-authoring-studio.sh --no-browser
#   ./scripts/run-authoring-studio.sh --headless
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BRIDGE_PROJ="$ROOT/src/Tools/Ludots.Editor.Bridge/Ludots.Editor.Bridge.csproj"
REACT_DIR="$ROOT/src/Tools/Ludots.Editor.React"
TMP_DIR="$ROOT/.tmp"
PID_FILE="$TMP_DIR/editor-processes.json"
STUDIO_URL="http://localhost:5173/"
BRIDGE_HEALTH="http://localhost:5299/health"
NO_BROWSER=0
HEADLESS=0
NO_INSTALL=0

for arg in "$@"; do
  case "$arg" in
    --no-browser) NO_BROWSER=1 ;;
    --headless) HEADLESS=1; NO_BROWSER=1 ;;
    --no-install) NO_INSTALL=1 ;;
    -h|--help)
      sed -n '2,8p' "$0"
      exit 0
      ;;
    *)
      echo "error: unknown argument: $arg" >&2
      exit 1
      ;;
  esac
done

if [[ ! -f "$BRIDGE_PROJ" ]]; then
  echo "error: Bridge project not found: $BRIDGE_PROJ" >&2
  exit 1
fi
if [[ ! -d "$REACT_DIR" ]]; then
  echo "error: React editor dir not found: $REACT_DIR" >&2
  exit 1
fi
if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: dotnet SDK not found on PATH." >&2
  exit 1
fi
if ! command -v npm >/dev/null 2>&1; then
  echo "error: npm not found on PATH. Authoring studio needs Node.js." >&2
  exit 1
fi

http_ok() {
  local url="$1"
  curl -fsS -o /dev/null --max-time 2 "$url" 2>/dev/null
}

already_up() {
  http_ok "$BRIDGE_HEALTH" && http_ok "$STUDIO_URL"
}

wait_http() {
  local url="$1"
  local name="$2"
  local deadline=$((SECONDS + 90))
  while (( SECONDS < deadline )); do
    if http_ok "$url"; then
      return 0
    fi
    sleep 0.3
  done
  echo "error: $name did not become ready at $url within 90s" >&2
  return 1
}

open_studio() {
  local url="$1"
  if [[ "$NO_BROWSER" == 1 ]]; then
    echo "studio ready: $url"
    return 0
  fi
  if [[ -z "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]]; then
    echo "no display; studio is at $url"
    return 0
  fi
  local bin
  for bin in google-chrome google-chrome-stable chromium chromium-browser microsoft-edge microsoft-edge-stable; do
    if command -v "$bin" >/dev/null 2>&1; then
      "$bin" --app="$url" --new-window >/dev/null 2>&1 &
      echo "opened authoring studio app window via $bin"
      return 0
    fi
  done
  if command -v xdg-open >/dev/null 2>&1; then
    echo "no Chrome/Edge for an app window; opening a browser tab (not silent: this host has no --app browser)"
    xdg-open "$url"
    return 0
  fi
  echo "error: cannot open a window. Install Chrome/Chromium/Edge, or open $url yourself." >&2
  exit 1
}

mkdir -p "$TMP_DIR"

if already_up; then
  echo "authoring studio already running"
  open_studio "$STUDIO_URL"
  exit 0
fi

if [[ "$NO_INSTALL" != 1 && ! -d "$REACT_DIR/node_modules" ]]; then
  (cd "$REACT_DIR" && npm ci)
fi

BRIDGE_LOG="$TMP_DIR/bridge.log"
BRIDGE_ERR="$TMP_DIR/bridge.err.log"
EDITOR_LOG="$TMP_DIR/editor.log"
EDITOR_ERR="$TMP_DIR/editor.err.log"

if http_ok "$BRIDGE_HEALTH"; then
  echo "Bridge already on :5299"
  BRIDGE_PID=""
else
  (
    cd "$ROOT"
    dotnet build "$BRIDGE_PROJ" -c Release -nologo -clp:ErrorsOnly
    dotnet exec --roll-forward Major \
      "$ROOT/src/Tools/Ludots.Editor.Bridge/bin/Release/net9.0/Ludots.Editor.Bridge.dll"
  ) >"$BRIDGE_LOG" 2>"$BRIDGE_ERR" &
  BRIDGE_PID=$!
fi

if http_ok "$STUDIO_URL"; then
  echo "Vite already on :5173"
  EDITOR_PID=""
else
  (
    cd "$REACT_DIR"
    npm run dev
  ) >"$EDITOR_LOG" 2>"$EDITOR_ERR" &
  EDITOR_PID=$!
fi

python3 - <<PY
import json
from pathlib import Path
path = Path("$PID_FILE")
payload = {}
bridge = "${BRIDGE_PID}"
editor = "${EDITOR_PID}"
if bridge:
    payload["bridgePid"] = int(bridge)
if editor:
    payload["editorPid"] = int(editor)
path.write_text(json.dumps(payload), encoding="utf-8")
PY

if ! wait_http "$BRIDGE_HEALTH" "Editor.Bridge"; then
  echo "--- bridge.err.log ---" >&2
  tail -n 40 "$BRIDGE_ERR" >&2 || true
  exit 1
fi
if ! wait_http "$STUDIO_URL" "Vite editor"; then
  echo "--- editor.err.log ---" >&2
  tail -n 40 "$EDITOR_ERR" >&2 || true
  exit 1
fi

echo "authoring studio: $STUDIO_URL"
open_studio "$STUDIO_URL"

if [[ "$HEADLESS" != 1 ]]; then
  echo "leave this terminal open, or stop with scripts/stop-editor.ps1 / kill the pids in $PID_FILE"
fi
