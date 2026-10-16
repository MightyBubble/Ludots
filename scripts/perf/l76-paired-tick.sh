#!/usr/bin/env bash
# 同机交替比较 tick。配对把两边的负载抵消了，不要求机器空闲。
# 不要和别的基准同时跑，否则配对消不掉那份额外负载。
# 用法：scripts/perf/l76-paired-tick.sh [基线提交] [对数]
# 基线提交缺省时读 Baselines/l76-fog.json 的 commit。对数缺省 4。
# 规模固定 12000 和 50000。每对算 HEAD/基线的 tick 中位之比，再取这些比的中位。
# 中位比 > 1.05，或任一对比里 HEAD 的 maxAlloc 大于基线，退出码非 0。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export MSBUILDDISABLENODEREUSE=1

BASELINE="${1:-}"
PAIRS="${2:-4}"
if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//'
  exit 0
fi

if ! [[ "$PAIRS" =~ ^[1-9][0-9]*$ ]]; then
  echo "对数必须是正整数，收到: $PAIRS" >&2
  exit 2
fi

JSON="src/Tests/CrowdSimulationTests/Baselines/l76-fog.json"
PROBE_REL="src/Tests/CrowdSimulationTests/Performance/PairedTickProbe.cs"
if [[ -z "$BASELINE" ]]; then
  BASELINE="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["commit"])' "$JSON")"
fi
git cat-file -e "${BASELINE}^{commit}"

BASE_WT=""
ROWS=""
cleanup() {
  if [[ -n "$BASE_WT" ]]; then
    git worktree remove --force "$BASE_WT" >/dev/null 2>&1 || true
    rm -rf "$BASE_WT"
  fi
  if [[ -n "$ROWS" ]]; then
    rm -f "$ROWS"
  fi
}
trap cleanup EXIT

BASE_WT="$(mktemp -d /tmp/l76-paired-base.XXXXXX)"
rmdir "$BASE_WT"
git worktree add --detach "$BASE_WT" "$BASELINE"
cp "$PROBE_REL" "$BASE_WT/$PROBE_REL"

echo "baseline $BASELINE"
echo "head $(git rev-parse HEAD)"
echo "building baseline"
dotnet build "$BASE_WT/src/Tests/CrowdSimulationTests/CrowdSimulationTests.csproj" -c Release --nologo
echo "building head"
dotnet build src/Tests/CrowdSimulationTests/CrowdSimulationTests.csproj -c Release --nologo

ROWS="$(mktemp)"
run_probe() {
  local dir="$1"
  local units="$2"
  local log
  log="$(mktemp)"
  if ! (
    cd "$dir"
    PROBE_UNITS="$units" dotnet test src/Tests/CrowdSimulationTests -c Release --no-build \
      --filter "FullyQualifiedName~PairedTickProbe" --logger "console;verbosity=detailed" >"$log" 2>&1
  ); then
    echo "探针失败: $dir units=$units" >&2
    tail -n 60 "$log" >&2
    rm -f "$log"
    return 1
  fi
  local line
  line="$(grep 'PROBE ' "$log" | grep 'median' | tail -n 1 || true)"
  rm -f "$log"
  if [[ -z "$line" ]]; then
    echo "没有 PROBE 行: $dir units=$units" >&2
    return 1
  fi
  printf '%s\n' "$line"
}

for units in 12000 50000; do
  for ((i = 1; i <= PAIRS; i++)); do
    base_line="$(run_probe "$BASE_WT" "$units")" || exit 1
    head_line="$(run_probe "$ROOT" "$units")" || exit 1
    printf '%s\t%s\t%s\t%s\n' "$units" "$i" "$base_line" "$head_line" | tee -a "$ROWS"
  done
done

status=0
python3 - "$ROWS" <<'PY' || status=$?
import re, sys
from decimal import Decimal

pat = re.compile(r"PROBE (\d+) median ([0-9.]+) runs .+ maxAlloc (-?\d+)")
rows = []
for raw in open(sys.argv[1], encoding="utf-8"):
    raw = raw.rstrip("\n")
    if not raw:
        continue
    units, pair, base, head = raw.split("\t")
    bm = pat.search(base)
    hm = pat.search(head)
    if not bm or not hm:
        raise SystemExit(f"无法解析: {raw}")
    bms, balloc = Decimal(bm.group(2)), int(bm.group(3))
    hms, halloc = Decimal(hm.group(2)), int(hm.group(3))
    if bms == 0:
        raise SystemExit(f"{units} 第 {pair} 对基线中位是 0")
    rows.append((int(units), int(pair), bms, hms, hms / bms, balloc, halloc))

def median(vals):
    s = sorted(vals)
    n = len(s)
    mid = n // 2
    if n % 2:
        return s[mid]
    return (s[mid - 1] + s[mid]) / 2

limit = Decimal("1.05")
failed = False
print(f"{'size':>6} {'pair':>4} {'base_ms':>10} {'head_ms':>10} {'ratio':>8} {'base_alloc':>10} {'head_alloc':>10}")
by = {}
for units, pair, bms, hms, ratio, balloc, halloc in rows:
    by.setdefault(units, []).append((ratio, balloc, halloc))
    print(f"{units:6d} {pair:4d} {bms:10.4f} {hms:10.4f} {ratio:8.4f} {balloc:10d} {halloc:10d}")
    if halloc > balloc:
        failed = True

for units, pairs in by.items():
    med = median([r for r, _, _ in pairs])
    mark = "FAIL" if med > limit or any(h > b for _, b, h in pairs) else "ok"
    if mark == "FAIL":
        failed = True
    print(f"{units} median_ratio {med:.4f} limit {limit} {mark}")

sys.exit(1 if failed else 0)
PY
exit "$status"
