#!/usr/bin/env bash
# Complete pull+merge of origin/main for LudotsProd during network windows.
cd /c/001_AI/LudotsProd || exit 1
for i in $(seq 1 40); do
  echo "== round $i @ $(date +%T)"
  if timeout 8 curl -sI https://github.com -o /dev/null 2>/dev/null; then
    echo "window open"
    if [ -s tmp/missing-all.txt ]; then
      split -l 250 tmp/missing-all.txt tmp/chunk_
      for c in tmp/chunk_*; do
        xargs -a "$c" git -c http.version=HTTP/1.1 fetch --no-tags origin 2>&1 | tail -1
        rm -f "$c"
      done
      left=$(grep '^?' tmp/revlist-out.txt 2>/dev/null | wc -l)
    fi
    # true closure check + fallback full refetch when anything is still absent
    n=$(git rev-list --objects --missing=print origin/main 2>/dev/null | grep -c '^?')
    echo "still_missing=$n"
    if [ "$n" -gt 0 ]; then
      echo "falling back to full refetch"
      git -c remote.origin.partialclonefilter= -c http.version=HTTP/1.1 fetch --refetch --no-tags origin main 2>&1 | tail -2
    fi
    for m in 1 2 3 4; do
      out=$(git -c http.version=HTTP/1.1 merge --no-edit origin/main 2>&1)
      if [ $? -eq 0 ] && echo "$out" | grep -q '^Merge'; then echo "$out" | tail -3; echo MERGE_OK; exit 0; fi
      echo "$out" | tail -2
      git merge --abort 2>/dev/null
      sleep 8
    done
  fi
  sleep 45
done
echo WATCHDOG_GAVE_UP
exit 1
