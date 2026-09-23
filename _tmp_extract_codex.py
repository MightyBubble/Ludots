import json
from pathlib import Path
import re

p = Path(r"C:\Users\sietg\.codex\sessions\2026\08\23\rollout-2026-08-23T18-05-23-01a02e14-feb9-7b53-b684-0c53dc937daa.jsonl")
out = Path(r"C:\001_AI\LudotsProd_showcase_epic\_tmp_codex_thread")
out.mkdir(exist_ok=True)

users = []
assist = []
issues = set()
n = 0
last_ts = None
first_ts = None
types = {}

def collect_text(content):
    texts = []
    if not content:
        return texts
    for c in content:
        if not isinstance(c, dict):
            continue
        t = c.get("type")
        if t in ("input_text", "text", "Text", "output_text"):
            texts.append(c.get("text") or "")
    return texts

for line in p.open("r", encoding="utf-8"):
    n += 1
    try:
        rec = json.loads(line)
    except Exception:
        continue
    ts = rec.get("timestamp")
    if ts:
        first_ts = first_ts or ts
        last_ts = ts
    t = rec.get("type")
    types[t] = types.get(t, 0) + 1
    payload = rec.get("payload") or {}

    role = payload.get("role")
    inner_type = payload.get("type")
    if role == "user" or (inner_type == "message" and payload.get("role") == "user"):
        text = "\n".join(collect_text(payload.get("content"))).strip()
        if text:
            if text.startswith("# AGENTS.md") or "AGENTS.md instructions" in text[:80]:
                continue
            if text.startswith("<app-context>") or text.startswith("# Codex desktop"):
                continue
            users.append({"ts": ts, "len": len(text), "text": text[:8000]})
            for m in re.findall(r"#(\d{2,5})", text):
                issues.add(m)
            for m in re.findall(r"issues/(\d+)", text):
                issues.add(m)

    if t == "event_msg":
        item = payload.get("item") or {}
        if item.get("type") == "AgentMessage":
            at = "\n".join(collect_text(item.get("content"))).strip()
            if at:
                assist.append({"ts": ts, "text": at[:2500]})

meta = {
    "lines": n,
    "first_ts": first_ts,
    "last_ts": last_ts,
    "types": types,
    "user_count": len(users),
    "assist_count": len(assist),
    "issues": sorted(issues, key=lambda x: int(x)),
}
(out / "meta.json").write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
(out / "users.json").write_text(json.dumps(users, ensure_ascii=False, indent=2), encoding="utf-8")
(out / "assist_tail.json").write_text(json.dumps(assist[-30:], ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({k: meta[k] for k in meta if k != "types"}, ensure_ascii=False, indent=2))
print("TYPES", json.dumps(types, ensure_ascii=False))
print("USER PREVIEWS:")
for i, u in enumerate(users):
    preview = u["text"][:350].replace("\n", " | ")
    print("--- user %d ts=%s len=%d ---" % (i, u["ts"], u["len"]))
    print(preview)
