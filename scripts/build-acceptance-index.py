#!/usr/bin/env python3
"""从 showcase.registry.json 生成验收套件索引 scripts/acceptance/acceptance.index.json。

筛选规则：tier == "T1" 且 status == "active" 的条目，分两级：
- runnable : preset 非空，可通过 run-mod-launcher.cmd cli launch <binding> --adapter raylib --record <dir> 实跑；
- test-only: preset 为空但有 acceptanceTest，仅通过 dotnet test 过滤器覆盖。

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

SCHEMA_VERSION = 1

REPO_ROOT = Path(__file__).resolve().parent.parent
REGISTRY_PATH = REPO_ROOT / "showcase.registry.json"
INDEX_PATH = REPO_ROOT / "scripts" / "acceptance" / "acceptance.index.json"


def load_registry(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def build_index(registry: dict) -> tuple:
    """返回 (index, skipped_ids)。skipped 为 T1 active 但既无 preset 也无 acceptanceTest 的条目。"""
    runnable = []
    test_only = []
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

        if preset:
            runnable.append(item)
        elif acceptance_test:
            test_only.append(item)
        else:
            skipped.append(entry["id"])

    runnable.sort(key=lambda x: x["id"])
    test_only.sort(key=lambda x: x["id"])

    index = {
        "schemaVersion": SCHEMA_VERSION,
        "source": "showcase.registry.json",
        "selection": {"tier": "T1", "status": "active"},
        "counts": {
            "runnable": len(runnable),
            "testOnly": len(test_only),
            "total": len(runnable) + len(test_only),
        },
        "runnable": runnable,
        "testOnly": test_only,
    }
    return index, skipped


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
                f"(runnable={index['counts']['runnable']}, test-only={index['counts']['testOnly']})"
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
        f"(runnable={index['counts']['runnable']}, test-only={index['counts']['testOnly']}, total={index['counts']['total']})"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
