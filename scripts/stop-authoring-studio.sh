#!/usr/bin/env bash
# 停掉 run-authoring-studio.sh 记下的 Bridge / Vite 进程。只杀 pid 文件里的号，不按名字扫。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PID_FILE="$ROOT/.tmp/editor-processes.json"

if [[ ! -f "$PID_FILE" ]]; then
  echo "No pid file: $PID_FILE"
  exit 0
fi

python3 - <<PY
import json, os, signal
from pathlib import Path
path = Path("$PID_FILE")
data = json.loads(path.read_text(encoding="utf-8"))
for key in ("bridgePid", "editorPid"):
    pid = data.get(key)
    if not isinstance(pid, int):
        continue
    try:
        os.kill(pid, signal.SIGTERM)
        print(f"Stopped PID {pid}")
    except ProcessLookupError:
        print(f"PID {pid} not running")
    except PermissionError as exc:
        raise SystemExit(f"cannot stop PID {pid}: {exc}")
path.unlink(missing_ok=True)
PY
