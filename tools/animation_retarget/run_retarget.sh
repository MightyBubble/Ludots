#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SOURCE=""
TARGET=""
MAPPING="${ROOT}/tools/animation_retarget/mappings/kaykit_name_identity.json"
OUT=""
ACTION=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --source) SOURCE="$2"; shift 2 ;;
    --target) TARGET="$2"; shift 2 ;;
    --mapping) MAPPING="$2"; shift 2 ;;
    --out) OUT="$2"; shift 2 ;;
    --action) ACTION="$2"; shift 2 ;;
    *) echo "Unknown arg: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "${SOURCE}" || -z "${TARGET}" || -z "${OUT}" ]]; then
  echo "Usage: $0 --source anim.glb --target character.glb --out out.glb [--mapping map.json] [--action Walk]" >&2
  exit 2
fi

if ! command -v blender >/dev/null 2>&1; then
  echo "ERROR: blender not found (open-source headless requirement)" >&2
  exit 2
fi

ARGS=(--source "${SOURCE}" --target "${TARGET}" --mapping "${MAPPING}" --out "${OUT}")
if [[ -n "${ACTION}" ]]; then
  ARGS+=(--action "${ACTION}")
fi

blender --background --python "${ROOT}/tools/animation_retarget/retarget_bake.py" -- "${ARGS[@]}"
