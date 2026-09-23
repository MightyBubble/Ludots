import sqlite3
import json
from pathlib import Path

db = r"C:\Users\sietg\.zcode\cli\db\db.sqlite"
out = Path(r"C:\001_AI\LudotsProd_showcase_epic\_tmp_fe59_dump")
out.mkdir(exist_ok=True)

con = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
con.row_factory = sqlite3.Row
cur = con.cursor()

sid = "sess_fe59d60d-684c-4b47-8faa-d5f8d74e317d"

tables = [r[0] for r in cur.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY 1")]
(out / "tables.json").write_text(json.dumps(tables, ensure_ascii=False, indent=2), encoding="utf-8")

row = cur.execute(
    "SELECT id, directory, path, title, summary_diffs, time_created, time_updated FROM session WHERE id=?",
    (sid,),
).fetchone()
meta = {
    "session": {k: row[k] for k in row.keys()} if row else None,
    "like": [dict(r) for r in cur.execute(
        "SELECT id, title, directory, time_created, time_updated FROM session WHERE id LIKE ? OR title LIKE ? ORDER BY time_updated DESC LIMIT 30",
        ("%fe59d60d%", "%fe59%"),
    )],
    "todos": [dict(r) for r in cur.execute(
        "SELECT position, status, priority, content, time_updated FROM todo WHERE session_id=? ORDER BY position",
        (sid,),
    )],
}
(out / "meta.json").write_text(json.dumps(meta, ensure_ascii=False, indent=2, default=str), encoding="utf-8")

msgs = list(cur.execute(
    "SELECT id, sequence, time_created, data FROM message WHERE session_id=? ORDER BY sequence",
    (sid,),
))
msg_summaries = []
for m in msgs:
    data = json.loads(m["data"]) if m["data"] else {}
    role = data.get("role") or data.get("info", {}).get("role") or data.get("type")
    msg_summaries.append({
        "id": m["id"],
        "sequence": m["sequence"],
        "time_created": m["time_created"],
        "role": role,
        "keys": list(data.keys()),
        "data_preview": json.dumps(data, ensure_ascii=False)[:500],
    })
(out / "messages.json").write_text(json.dumps(msg_summaries, ensure_ascii=False, indent=2), encoding="utf-8")

parts = list(cur.execute(
    "SELECT id, message_id, sequence, data FROM part WHERE session_id=? ORDER BY sequence",
    (sid,),
))
user_texts = []
assistant_texts = []
for p in parts:
    data = json.loads(p["data"]) if p["data"] else {}
    ptype = data.get("type") or data.get("kind")
    text = data.get("text") if isinstance(data.get("text"), str) else None
    if not text:
        continue
    rec = {
        "part_id": p["id"],
        "message_id": p["message_id"],
        "sequence": p["sequence"],
        "type": ptype,
        "text": text[:8000],
        "text_len": len(text),
    }
    if ptype in ("text", "user") or (text and len(text) > 20):
        role = None
        # find parent message role
        pass
    if ptype == "text":
        # classify later
        pass
    user_texts.append(rec)

# classify by parent message role
msg_role = {m["id"]: m["role"] for m in msg_summaries}
classified_user = []
classified_assistant = []
for rec in user_texts:
    role = msg_role.get(rec["message_id"])
    rec["role"] = role
    if role == "user":
        classified_user.append(rec)
    elif role == "assistant":
        classified_assistant.append(rec)

(out / "user_texts.json").write_text(json.dumps(classified_user, ensure_ascii=False, indent=2), encoding="utf-8")
# keep last 8 assistant texts, truncated
tail_as = classified_assistant[-12:]
(out / "assistant_tail.json").write_text(json.dumps(tail_as, ensure_ascii=False, indent=2), encoding="utf-8")

print(json.dumps({
    "session": bool(row),
    "title": row["title"] if row else None,
    "todos": len(meta["todos"]),
    "msgs": len(msgs),
    "parts": len(parts),
    "user": len(classified_user),
    "assistant": len(classified_assistant),
    "like": meta["like"],
}, ensure_ascii=False, indent=2, default=str))
