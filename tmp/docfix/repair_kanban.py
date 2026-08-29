import io, difflib

C = io.open('tmp/docfix/kanban_base.md', encoding='utf-8').read().splitlines()
W = io.open('gitbook/architecture/presenter-development-kanban.md', encoding='utf-8', errors='replace').read().splitlines()
FF = '\ufffd'
print('base lines', len(C), 'work lines', len(W))


def skel(line):
    out = []
    i = 0
    while i < len(line):
        if line[i] == FF:
            i += 1
            if i < len(line) and line[i] == '?':
                i += 1
        else:
            out.append(line[i])
            i += 1
    return ''.join(out)


sm = difflib.SequenceMatcher(None, C, W, autojunk=False)
matched = {}
for tag, i1, i2, j1, j2 in sm.get_opcodes():
    if tag == 'equal':
        for k in range(j2 - j1):
            if FF in W[j1 + k]:
                matched[j1 + k] = i1 + k
        continue
    for woff, wl in enumerate(W[j1:j2]):
        if FF not in wl:
            continue
        widx = j1 + woff
        best = None
        for k in range(max(0, i1 - 5), min(len(C), i2 + 6)):
            r = difflib.SequenceMatcher(None, C[k], skel(wl), autojunk=False).ratio()
            if best is None or r > best[0]:
                best = (r, k)
        if best and best[0] >= 0.4:
            matched[widx] = best[1]

auto = 0
manual = []
for widx, cidx in sorted(matched.items()):
    wl, cl = W[widx], C[cidx]
    s = skel(wl)
    csm = difflib.SequenceMatcher(None, cl, s, autojunk=False)
    eq = [(b1, b2, a1, a2) for t, a1, a2, b1, b2 in csm.get_opcodes() if t == 'equal']

    def before(p):
        b = 0
        for b1, b2, a1, a2 in eq:
            if b2 <= p and a2 > b:
                b = a2
        return b

    def after(p):
        b = len(cl)
        for b1, b2, a1, a2 in eq:
            if b1 >= p and a1 < b:
                b = a1
        return b

    runs = []
    scount = 0
    i = 0
    cur = None
    while i < len(wl):
        if wl[i] == FF:
            if cur is None:
                cur = [i, None, scount]
            i += 1
            if i < len(wl) and wl[i] == '?':
                i += 1
        else:
            if cur is not None:
                cur[1] = i
                runs.append(tuple(cur))
                cur = None
            scount += 1
            i += 1
    if cur is not None:
        cur[1] = len(wl)
        runs.append(tuple(cur))

    claims = []
    pos = 0
    new = []
    bad = False
    for rs, re_, sp in runs:
        new.append(wl[pos:rs])
        lo, hi = before(sp), after(sp)
        fill = cl[lo:hi]
        if not fill or hi <= lo:
            bad = True
        for (lo2, hi2) in claims:
            if not (hi <= lo2 or lo >= hi2):
                bad = True
        claims.append((lo, hi))
        new.append(fill)
        pos = re_
    new.append(wl[pos:])
    if bad:
        manual.append((widx + 1, cidx + 1, wl, cl))
    else:
        W[widx] = ''.join(new)
        auto += 1

print('corrupted lines:', len(matched), 'auto:', auto, 'manual:', len(manual))
io.open('tmp/docfix/kanban_repaired_partial.md', 'w', encoding='utf-8', newline='\n').write('\n'.join(W) + '\n')
print('FFFD left:', '\n'.join(W).count(FF))
print()
for n, cn, wl, cl in manual:
    print('== W L%d (base L%s)' % (n, cn))
    print('W:', wl)
    print('C:', cl)
