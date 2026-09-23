from pathlib import Path
import csv
import hashlib
import json
import shutil
import statistics

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
out = HERE / "measurements"
out.mkdir(exist_ok=True)
summary = {}
for count in (1000, 5000, 10000):
    modes = {}
    for experiment, relative, warmup_field, metrics in (
        ("uniform", f"tmp/mannequin-ab/ab-paired-{count}.csv", "frame",
         ("main_gpu_ms", "shadow_gpu_ms", "total_ms", "alloc_bytes")),
        ("preskin", f"tmp/mannequin-preskin/results-{count}-normal-fix/frames.csv", "warmup",
         ("main_gpu_ms", "shadow_gpu_ms", "total_sync_ms", "prep_cpu_ms", "preskin_cpu_upload_ms", "geometry_upload_bytes", "alloc_bytes", "gen0", "gen1")),
    ):
        source = ROOT / relative
        shutil.copy2(source, out / f"{experiment}-{count}.csv")
        rows = list(csv.DictReader(source.open(encoding="utf-8")))
        samples = [r for r in rows if int(r[warmup_field]) >= 64] if warmup_field == "frame" else [r for r in rows if r["warmup"] == "0"]
        modes[experiment] = {}
        for mode in sorted({r["mode"] for r in samples}):
            selected = [r for r in samples if r["mode"] == mode]
            modes[experiment][mode] = {"samples": len(selected), **{
                key: {"mean": statistics.mean(float(r[key]) for r in selected),
                      "max": max(float(r[key]) for r in selected)} for key in metrics}}
    summary[str(count)] = modes
    for name in ("pixels.json", "metadata.json"):
        shutil.copy2(ROOT / f"tmp/mannequin-preskin/results-{count}-normal-fix/{name}", out / f"preskin-{count}-{name}")
for name in ("mapping-tests.log", "skinning-tests.log", "health-final.json", "entities-final.json", "camera-final.json", "warnings-current.json", "agent34-start.json", "agent34-end.json", "agent34-moving.json", "order-current.json"):
    shutil.copy2(ROOT / "tmp/rpc" / name, out / name)
(out / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
manifest = {str(p.relative_to(HERE)): hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir() if p.is_file()}
(HERE / "measurement-hashes.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
print(f"Saved {len(manifest)} measurement files.")
