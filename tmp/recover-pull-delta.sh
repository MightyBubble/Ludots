#!/usr/bin/env bash
# Delta-accumulating recovery: each round fetches only still-missing objects in small packs.
cd /c/001_AI/LudotsProd || exit 1
for i in $(seq 1 60); do
  n=$(git rev-list --objects --missing=print origin/main 2>/dev/null | grep -c '^?')
  echo "== round $i @ $(date +%T) missing=$n"
  [ "$n" -eq 0 ] && break
  git rev-list --objects --missing=print origin/main 2>/dev/null | grep '^?' | cut -c2- > tmp/miss.txt
  split -l 120 tmp/miss.txt tmp/dch_
  for c in tmp/dch_*; do
    xargs -a "$c" git -c http.version=HTTP/1.1 -c http.lowSpeedTime=30 fetch --no-tags origin 2>/dev/null
    rm -f "$c"
  done
  out=$(git -c http.version=HTTP/1.1 merge --no-edit origin/main 2>&1)
  if [ $? -eq 0 ] && echo "$out" | grep -q '^Merge'; then echo "$out" | tail -3; echo MERGE_OK; exit 0; fi
  git merge --abort 2>/dev/null
  sleep 20
done
n=$(git rev-list --objects --missing=print origin/main 2>/dev/null | grep -c '^?')
echo "END missing=$n"
[ "$n" -eq 0 ] && { git merge --no-edit origin/main && echo MERGE_OK; }
