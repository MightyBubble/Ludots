#!/usr/bin/env python3
"""Generate GraphNodeOp player wiki pages from vignettes (SSOT).

Reads:
  CapabilityStandardGraphOpsNodeGalleryMod/assets/Vignettes/{Op}.json
  assets/GAS/graph_node_op_coverage.registry.json

Writes (do not hand-edit):
  gitbook/reference/graph-node-op-wiki/README.md
  gitbook/reference/graph-node-op-wiki/{Op}.md

Player copy stays Chinese. Opcode is metadata, not the hero title.
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path

PREFIX = "capability_standard_graph_op_"
GALLERY_REL = "mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod"
COVERAGE_REL = "assets/GAS/graph_node_op_coverage.registry.json"
WIKI_REL = "gitbook/reference/graph-node-op-wiki"
EVIDENCE_REL = "artifacts/evidence"

DRIVER_LABELS = {
    "linear": "算术与比较",
    "attr": "属性与效果",
    "script": "脚本控制流",
    "spatial": "空间圈人",
    "query": "名单筛选与汇总",
    "rel": "关系与好感",
    "blackboard": "黑板与配置",
    "event": "事件与吸附",
    "sandbox": "组合短剧",
}


def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def merge_field(vignette_dir: Path, vignette: dict) -> dict:
    field = vignette.get("field")
    if not field:
        return vignette
    path = vignette_dir / "_fields" / f"{field}.json"
    if not path.is_file():
        raise SystemExit(f"Vignette field '{field}' missing: {path}")
    scene = load(path)
    merged = dict(vignette)
    for key in ("actors", "collections", "links", "camera"):
        if key in scene:
            merged[key] = scene[key]
    return merged


def evidence_dir(op: str) -> str:
    return f"{EVIDENCE_REL}/{PREFIX}{op}"


def require_media(repo: Path, op: str) -> None:
    play = repo / evidence_dir(op) / "play.mp4"
    poster = repo / evidence_dir(op) / "poster.png"
    if not play.is_file() or play.stat().st_size < 20_000:
        raise SystemExit(
            f"Missing tracked player video for {op}: {play}. "
            "Run scripts/record-graph-op-node-galleries.py first."
        )
    if not poster.is_file() or poster.stat().st_size < 1_000:
        raise SystemExit(
            f"Missing tracked gallery poster for {op}: {poster}. "
            "Record script must write poster.png next to play.mp4."
        )


def write_op_page(path: Path, vignette: dict) -> None:
    op = vignette["op"]
    title = vignette["title"]
    beat = vignette["beat"]
    detail = vignette.get("detailTemplate", beat)
    driver = vignette.get("driver", "sandbox")
    family = DRIVER_LABELS.get(driver, driver)
    sid = PREFIX + op
    media = evidence_dir(op)
    launch = f"scripts/run-mod-launcher.cmd cli launch ${sid} --adapter raylib"
    graph = f"{GALLERY_REL}/assets/GAS/graphs/{op}.json"
    vignette_path = f"{GALLERY_REL}/assets/Vignettes/{op}.json"

    body = f"""# {title}

{beat}

<video controls playsinline preload="metadata" poster="{media}/poster.png" src="{media}/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `{media}/play.mp4`。
</video>

## 1. 概述

这场短剧只讲一个图节点会在玩家眼里变成什么。标题用人话，不拿技术名当主角。

- 家族：{family}
- 启动绑定：`{sid}`
- 作者记号：`{op}`（给写图的人对照，不出现在玩家字幕里）

## 2. 结构

| 角色 | 路径 |
|------|------|
| 玩家录像 | `{media}/play.mp4` |
| 画廊海报 | `{media}/poster.png` |
| 剧本 | `{vignette_path}` |
| 作者图 | `{graph}` |

## 3. 详情

字幕模板（占位符由短剧填上）：

> {detail}

## 4. 场景

1. 从画廊或启动器打开 `{sid}`。
2. 舞台上能看见人和头顶血条（或这场短剧写明的可见反馈）。
3. 短剧演算时，字幕只讲这一件事。
4. 录像里不应夹带其它节点的完整剧情。

## 5. 边界

- 玩家入口是这一场，不是家族聚合场。
- 字幕禁止堆 opcode / True / False / 耗时数字。
- 缺 `play.mp4` 或 `poster.png` 时，站点与生成器必须失败关闭，不得用空片顶替。

## 6. UAT

```gherkin
Feature: {title}

  Scenario: 新玩家看懂这场短剧
    Given 玩家打开 {sid}
    And 页面或本地能播 {media}/play.mp4
    When 短剧演完
    Then 字幕讲的是「{beat}」这类人话
    And 画面反馈和字幕说的是同一件事
```

## 怎么进

```text
{launch}
```
"""
    path.write_text(body, encoding="utf-8")


def write_index(path: Path, by_driver: dict[str, list[dict]]) -> None:
    lines = [
        "# Graph 节点画廊 Wiki",
        "",
        "每个可执行图节点一场能看懂的短剧。下面按玩法家族分组；点进去能看录像、字幕合同和启动命令。",
        "",
        "生成器：`scripts/generate-graph-op-node-wiki.py`（从 vignette 生成，勿手改正文）。",
        "",
    ]
    for driver, items in sorted(by_driver.items(), key=lambda kv: DRIVER_LABELS.get(kv[0], kv[0])):
        label = DRIVER_LABELS.get(driver, driver)
        lines.append(f"## {label}")
        lines.append("")
        for v in sorted(items, key=lambda x: x["title"]):
            op = v["op"]
            lines.append(f"- [{v['title']}]({op}.md) — {v['beat']}")
        lines.append("")
    path.write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", default=str(Path(__file__).resolve().parent.parent))
    parser.add_argument(
        "--allow-missing-media",
        action="store_true",
        help="Generate markdown even when play.mp4/poster.png are absent (CI authoring only).",
    )
    args = parser.parse_args()
    repo = Path(args.repo).resolve()
    vignette_dir = repo / GALLERY_REL / "assets" / "Vignettes"
    wiki_dir = repo / WIKI_REL
    wiki_dir.mkdir(parents=True, exist_ok=True)

    coverage = load(repo / COVERAGE_REL)
    ops = [e["op"] for e in coverage["entries"]]
    vignettes: dict[str, dict] = {}
    for path in sorted(vignette_dir.glob("*.json")):
        data = load(path)
        op = data["op"]
        if path.stem != op:
            raise SystemExit(f"Vignette filename {path.name} must match op {op}.")
        vignettes[op] = merge_field(vignette_dir, data)

    missing = [op for op in ops if op not in vignettes]
    if missing:
        raise SystemExit("Missing vignettes:\n" + "\n".join(missing))

    by_driver: dict[str, list[dict]] = defaultdict(list)
    for op in ops:
        vignette = vignettes[op]
        if not args.allow_missing_media:
            require_media(repo, op)
        write_op_page(wiki_dir / f"{op}.md", vignette)
        by_driver[vignette.get("driver", "sandbox")].append(vignette)

    # Drop stale pages for removed ops.
    keep = {f"{op}.md" for op in ops} | {"README.md"}
    for stale in wiki_dir.glob("*.md"):
        if stale.name not in keep:
            stale.unlink()

    write_index(wiki_dir / "README.md", by_driver)
    print(f"Wrote {len(ops)} GraphNodeOp wiki pages under {WIKI_REL}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
