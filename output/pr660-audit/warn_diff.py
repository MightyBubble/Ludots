import re

def load(p, tree):
    out = set()
    pat = re.compile(r'worktrees\\' + tree + r'\\(.+?\(\d+,\d+\)): warning ([A-Z]+\d+)')
    for line in open(p, encoding='utf-8', errors='replace'):
        mm = pat.search(line)
        if mm:
            out.add(f"{mm.group(1)} {mm.group(2)}")
    return out

b = load('build-base.log', 'audit-pr660-base')
m = load('build-merge.log', 'audit-pr660-merge')
new = sorted(m - b)
gone = sorted(b - m)
print(f"base={len(b)} merge={len(m)} new={len(new)} removed={len(gone)}")
print("--- NEW ---")
for x in new:
    print(x)
print("--- REMOVED sample ---")
for x in gone[:8]:
    print(x)
