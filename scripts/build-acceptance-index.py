#!/usr/bin/env python3
"""从 showcase.registry.json 生成验收套件索引 scripts/acceptance/acceptance.index.json。

筛选规则：tier == "T1" 且 status == "active" 的条目，分两级：
- runnable : preset 非空，可通过 run-mod-launcher.cmd cli launch preset:<preset> --adapter raylib --record <dir> 实跑；
- test-only: preset 为空但有 acceptanceTest，仅通过 dotnet test 过滤器覆盖。

localOnly 名单（见 LOCAL_ONLY_IDS）里的条目虽然带 preset，但依赖真实窗口/OpenGL 上下文，
GitHub windows runner 上不存在可用的 WGL 驱动，无头执行必崩（raylib InitWindow）。
这类条目留在注册表与 launcher preset 里供本地取证，只是不进云端门禁；其覆盖依靠
acceptanceTest 经 solution-verify 的对应 TestCategory 切片执行。

用法：
    python scripts/build-acceptance-index.py          # 生成/更新 acceptance.index.json
    python scripts/build-acceptance-index.py --check  # CI 校验：index 与 registry 漂移即退出 1

仅使用 Python 标准库。
"""

import argparse
import difflib
import json
import sys
from pathlib import Path

# CI Windows 运行器默认控制台编码（如 cp1252）无法输出中文，强制 UTF-8。
if hasattr(sys.stdout, "reconfigure"):
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass


SCHEMA_VERSION = 1

# 依赖真实 OpenGL 窗口的条目：preset 仍在，供本地 `cli launch preset:<id> --record` 取证，
# 但不进 ci-acceptance 云端门禁（windows runner 无 WGL 驱动，InitWindow 必崩）。
LOCAL_ONLY_REASON = (
    "requires a real OpenGL/WGL window context; GitHub windows runner has no GL driver, "
    "so headless execution crashes in raylib InitWindow"
)
LOCAL_ONLY_IDS = frozenset({
    "engine_raylib_atmosphere_fog",
    "engine_raylib_composition",
    "engine_raylib_crowd_anim",
    "engine_raylib_debug_draw",
    "engine_raylib_decal_projection",
    "engine_raylib_frame_lighting",
    "engine_raylib_gpu_crowd",
    "engine_raylib_gpu_crowd_sim",
    "engine_raylib_gpu_skinning",
    "engine_raylib_instancing",
    "engine_raylib_lighting",
    "engine_raylib_material_binding",
    "engine_raylib_particles",
    "engine_raylib_postprocess",
    "engine_raylib_primitives",
    "engine_raylib_ribbon_overlay",
    "engine_raylib_skia_overlay",
    "engine_raylib_sky_daynight",
    "engine_raylib_skybox",
    "engine_raylib_slash_trail",
    "engine_raylib_terrain_heightmap",
    "engine_raylib_terrain_surface",
    "engine_raylib_vegetation_cutout",
    "engine_raylib_water",
})

REPO_ROOT = Path(__file__).resolve().parent.parent
REGISTRY_PATH = REPO_ROOT / "showcase.registry.json"
PRESETS_PATH = REPO_ROOT / "launcher.presets.json"
INDEX_PATH = REPO_ROOT / "scripts" / "acceptance" / "acceptance.index.json"


def load_registry(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def build_index(registry: dict) -> tuple:
    """返回 (index, skipped_ids)。skipped 为 T1 active 但既无 preset 也无 acceptanceTest 的条目。"""
    runnable = []
    test_only = []
    local_only = []
    skipped = []

    for entry in registry.get("showcases", []):
        if entry.get("tier") != "T1" or entry.get("status") != "active":
            continue

        preset = entry.get("preset") or None
        acceptance_test = entry.get("acceptanceTest") or None
        item = {
            "id": entry["id"],
            "preset": preset,
            "binding": entry.get("binding") or None,
            "testFilter": acceptance_test,
            "artifactDir": entry.get("artifactDir") or None,
            "hasScreenshotEvidence": bool(entry.get("screenshot")),
        }

        if entry["id"] in LOCAL_ONLY_IDS:
            item["localOnly"] = LOCAL_ONLY_REASON
            local_only.append(item)
        elif preset:
            runnable.append(item)
        elif acceptance_test:
            test_only.append(item)
        else:
            skipped.append(entry["id"])

    runnable.sort(key=lambda x: x["id"])
    test_only.sort(key=lambda x: x["id"])
    local_only.sort(key=lambda x: x["id"])

    index = {
        "schemaVersion": SCHEMA_VERSION,
        "source": "showcase.registry.json",
        "selection": {"tier": "T1", "status": "active"},
        "counts": {
            "runnable": len(runnable),
            "testOnly": len(test_only),
            "localOnly": len(local_only),
            "total": len(runnable) + len(test_only) + len(local_only),
        },
        "runnable": runnable,
        "testOnly": test_only,
        "localOnly": local_only,
    }
    return index, skipped


def validate_local_only(registry: dict) -> list:
    """LOCAL_ONLY_IDS 自检：名单只允许装真正的 T1 active、带 preset 的条目。

    名单写错方向会让条目默默错位（漏写→云端又红；多写→静默失去门禁覆盖），
    所以这里 fail-loud，不靠人记。
    """
    by_id = {entry.get("id"): entry for entry in registry.get("showcases", [])}
    problems = []

    for entry_id in sorted(LOCAL_ONLY_IDS):
        entry = by_id.get(entry_id)
        if entry is None:
            problems.append(f"{entry_id}: 注册表里没有这个条目")
            continue
        if entry.get("tier") != "T1" or entry.get("status") != "active":
            problems.append(
                f"{entry_id}: 不是 tier=T1 status=active"
                f"（当前 {entry.get('tier')}/{entry.get('status')}），不该占名单"
            )
        if not (entry.get("preset") or "").strip():
            problems.append(f"{entry_id}: 没有 preset，本来就进不了 runnable，不应列入名单")

    return problems


def validate_presets_exist(index: dict) -> list:
    """runnable 条目的 preset 必须真实存在于 launcher.presets.json。

    注册表声明了 preset 但启动器里没有的条目，旧路径会退化成 `cli launch <binding>`，
    看起来能跑（否则报能力缺口），一旦按 preset 启动就变成硬红——这种漂移不能靠人眼发现。
    """
    if not PRESETS_PATH.is_file():
        return [f"presets 文件不存在: {PRESETS_PATH}"]

    document = load_registry(PRESETS_PATH)
    presets = document.get("presets") or []
    known = {
        (item.get("id") or "").strip()
        for item in presets
        if isinstance(item, dict)
    }

    return [
        f"{item['id']}: 注册表声明 preset='{item['preset']}'，但 launcher.presets.json 里没有这个 preset"
        for item in index["runnable"]
        if item.get("preset") not in known
    ]


def render(index: dict) -> str:
    return json.dumps(index, ensure_ascii=False, indent=2) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description="生成/校验验收套件索引 acceptance.index.json")
    parser.add_argument(
        "--check",
        action="store_true",
        help="校验模式：不写入文件，index 与 registry 漂移时退出 1",
    )
    args = parser.parse_args()

    if not REGISTRY_PATH.is_file():
        print(f"[ERROR] 注册表不存在: {REGISTRY_PATH}", file=sys.stderr)
        return 2

    registry = load_registry(REGISTRY_PATH)
    index, skipped = build_index(registry)
    rendered = render(index)

    local_only_problems = validate_local_only(registry)
    if local_only_problems:
        print("[FAIL] LOCAL_ONLY_IDS 与注册表不一致：", file=sys.stderr)
        for problem in local_only_problems:
            print(f"  - {problem}", file=sys.stderr)
        return 1

    preset_problems = validate_presets_exist(index)
    if preset_problems:
        print("[FAIL] 以下 runnable 条目声明的 preset 不存在：", file=sys.stderr)
        for problem in preset_problems:
            print(f"  - {problem}", file=sys.stderr)
        return 1

    if skipped:
        print(
            "[WARN] 以下 T1 active 条目既无 preset 也无 acceptanceTest，未纳入索引: "
            + ", ".join(skipped),
            file=sys.stderr,
        )

    if args.check:
        if not INDEX_PATH.is_file():
            print(f"[FAIL] 索引文件不存在: {INDEX_PATH}，请运行 scripts/build-acceptance-index.py 生成", file=sys.stderr)
            return 1
        current = INDEX_PATH.read_text(encoding="utf-8")
        if current == rendered:
            print(
                f"[OK] acceptance.index.json 与 showcase.registry.json 同步 "
                f"(runnable={index['counts']['runnable']}, test-only={index['counts']['testOnly']}, "
                f"local-only={index['counts']['localOnly']})"
            )
            return 0
        print("[FAIL] acceptance.index.json 与 showcase.registry.json 漂移，请重新运行 scripts/build-acceptance-index.py", file=sys.stderr)
        diff = difflib.unified_diff(
            current.splitlines(),
            rendered.splitlines(),
            fromfile="acceptance.index.json (当前)",
            tofile="acceptance.index.json (期望)",
            lineterm="",
        )
        for i, line in enumerate(diff):
            if i >= 80:
                print("... (diff 截断)", file=sys.stderr)
                break
            print(line, file=sys.stderr)
        return 1

    INDEX_PATH.parent.mkdir(parents=True, exist_ok=True)
    INDEX_PATH.write_text(rendered, encoding="utf-8")
    print(
        f"[OK] 已生成 {INDEX_PATH.relative_to(REPO_ROOT)} "
        f"(runnable={index['counts']['runnable']}, test-only={index['counts']['testOnly']}, "
        f"local-only={index['counts']['localOnly']}, total={index['counts']['total']})"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
