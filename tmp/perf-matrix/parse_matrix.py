import math
import pathlib
import re
import statistics

ROOT = pathlib.Path(r"C:\001_AI\_audit_1485_perf\tmp\perf-matrix")
MIN_FRAME = 200


def tokens(line):
    return dict(re.findall(r"([A-Za-z][A-Za-z0-9]*)=([^\s]+)", line))


def number(value):
    if value is None:
        return None
    value = value.split("/")[0].removesuffix("ms")
    try:
        return float(value)
    except ValueError:
        return None


def percentile(values, fraction):
    ordered = sorted(values)
    return ordered[max(0, math.ceil(len(ordered) * fraction) - 1)]


def stats(rows, field):
    values = [row[field] for row in rows if row.get(field) is not None]
    if not values:
        return "-"
    return f"{statistics.median(values):.3f}/{percentile(values, 0.95):.3f}/{max(values):.3f}"


def parse(path):
    rows = []
    current = None
    auto_exit = False
    for line in path.read_text(encoding="utf-8").splitlines():
        if "auto-exit frame=" in line:
            auto_exit = True
        if "sample frame=" in line:
            frame = int(re.search(r"sample frame=(\d+)", line).group(1))
            current = {"sample": frame}
            rows.append(current)
            continue
        if current is None:
            continue
        data = tokens(line)
        if "skinning-cpu " in line:
            mapping = {
                "poses": "poses",
                "poseBuildMs": "pose_build",
                "textureUploadMs": "upload_ms",
                "textureUploadBytes": "upload_bytes",
                "mainDrawCalls": "main_draws",
                "shadowDrawCalls": "shadow_draws",
                "shadowSubmitMs": "shadow_submit",
            }
        elif "managed allocBytes=" in line:
            mapping = {
                "allocBytes": "alloc",
                "gen0": "gen0",
                "gen1": "gen1",
                "hudTerrainRaycasts": "terrain_rays",
            }
        elif "timing frame=" in line:
            mapping = {
                "frame": "frame_ms",
                "visibleEntities": "visible",
                "gpuSkinBuild": "gpu_build",
                "gpuSkinDraw": "gpu_draw",
                "sim": "simulation",
                "presentation": "presentation",
                "behavior": "behavior",
                "animator": "animator",
                "animatorUpdates": "animator_updates",
                "transformSync": "transform_sync",
                "minimapCollect": "minimap_collect",
                "minimapProject": "minimap_project",
                "heightSamples": "height_samples",
                "cull": "culling",
                "hudProj": "hud",
                "hudRaw": "hud_raw",
                "hudProjected": "hud_projected",
                "emit": "emit",
                "worldHud": "world_hud",
            }
        elif "massnav target=" in line:
            mapping = {
                "target": "nav_target",
                "flow": "nav_flow",
                "prep": "nav_prep",
                "steering": "nav_steering",
                "step": "nav_step",
                "hard": "nav_hard",
                "neighborCandidates": "neighbor_candidates",
                "hardCandidates": "hard_candidates",
                "hardPairs": "hard_pairs",
                "hardPenetrating": "hard_penetrating",
                "entitySync": "nav_sync",
            }
        else:
            continue
        for source, target in mapping.items():
            current[target] = number(data.get(source))
    return [row for row in rows if row["sample"] >= MIN_FRAME], auto_exit


paths = sorted(
    path for path in ROOT.glob("*.log")
    if not path.name.endswith(".stdout.log") and path.name != "10k-moving-crowded-all.log"
)

fields = [
    "frame_ms", "simulation", "presentation", "nav_prep", "nav_steering",
    "nav_hard", "nav_flow", "nav_sync", "behavior", "transform_sync", "emit",
    "hud", "animator", "minimap_project", "culling", "gpu_build", "gpu_draw",
    "alloc", "terrain_rays", "neighbor_candidates", "hard_pairs"
]
print("run|samples|exit|" + "|".join(fields))
all_rows = {}
for path in paths:
    rows, auto_exit = parse(path)
    all_rows[path.stem] = rows
    print(path.stem + f"|{len(rows)}|{int(auto_exit)}|" + "|".join(stats(rows, field) for field in fields))

print("\nCOUNTS_MEDIAN")
count_fields = [
    "visible", "hud_raw", "hud_projected", "world_hud", "animator_updates",
    "height_samples", "poses", "upload_bytes", "main_draws", "shadow_draws",
    "hard_candidates", "hard_penetrating", "gen0", "gen1"
]
print("run|" + "|".join(count_fields))
for name, rows in all_rows.items():
    print(name + "|" + "|".join(stats(rows, field) for field in count_fields))

baseline = all_rows["10k-moving-crowded-all-350"]
print("\nAB_MEDIAN_DELTA_VS_ALL_350 (variant minus baseline; negative is faster)")
ab_fields = [
    "frame_ms", "simulation", "presentation", "behavior", "transform_sync",
    "emit", "hud", "animator", "minimap_project", "nav_step", "alloc"
]
print("run|" + "|".join(ab_fields))
base_medians = {field: statistics.median(row[field] for row in baseline if row.get(field) is not None) for field in ab_fields}
for name, rows in all_rows.items():
    if not name.startswith("10k-moving-crowded-no-"):
        continue
    values = []
    for field in ab_fields:
        samples = [row[field] for row in rows if row.get(field) is not None]
        values.append(f"{statistics.median(samples) - base_medians[field]:.3f}")
    print(name + "|" + "|".join(values))
