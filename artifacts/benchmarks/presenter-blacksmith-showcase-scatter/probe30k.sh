#!/bin/bash
# usage: probe30k.sh <sha>  — prints "GOOD <fps>" / "BAD <fps>" / "SKIP <reason>"
set -u
SHA="$1"
WT=/c/001_AI/ludots-may030
cd "$WT" || exit 3
git checkout -q --detach "$SHA" 2>/dev/null || { echo "SKIP checkout"; exit 3; }
[ -f global.json ] || echo '{"sdk":{"version":"9.0.312","rollForward":"latestFeature"}}' > global.json
if ! git ls-tree -r HEAD --name-only | grep -q "BlacksmithShowcaseScatterBenchmarkTests.cs"; then
  echo "SKIP no-test-file"; exit 3
fi
dotnet build src/Tests/PresentationTests/PresentationTests.csproj -c Release --nologo > /tmp/probe_build.log 2>&1
if [ $? -ne 0 ]; then echo "SKIP build"; exit 3; fi
LUDOTS_BLACKSMITH_BENCH_SCENARIO=scatter_30000_tight LUDOTS_BLACKSMITH_BENCH_WARMUP_FRAMES=6 LUDOTS_BLACKSMITH_BENCH_MEASURED_FRAMES=40 \
  dotnet test src/Tests/PresentationTests/PresentationTests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~Benchmark_ScatterBlacksmithShowcase" > /tmp/probe_run.log 2>&1
if ! grep -qE "已通过|Passed" /tmp/probe_run.log; then echo "SKIP test-fail"; exit 3; fi
REPORT=$(ls -t artifacts/benchmarks/*blacksmith-showcase-scatter/benchmark-report.md 2>/dev/null | head -1)
FPS=$(awk '/^## scatter_30000_tight/{f=1} f&&/^## /&&!/scatter_30000_tight/{f=0} f' "$REPORT" | grep -oP 'avg fps equivalent: `\K[0-9.]+' | head -1)
[ -z "$FPS" ] && { echo "SKIP no-number"; exit 3; }
OK=$(awk -v f="$FPS" 'BEGIN{print (f>=50)?1:0}')
[ "$OK" = "1" ] && echo "GOOD $FPS" || echo "BAD $FPS"
