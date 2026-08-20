#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
launch_script="$repo_root/src/Tools/Ludots.Pi/scripts/launch.mjs"

if [[ ! -f "$launch_script" ]]; then
  echo "Ludots Pi launcher is missing: $launch_script" >&2
  exit 1
fi

export LUDOTS_PI_WORKSPACE="$repo_root"
exec node "$launch_script" "$@"
