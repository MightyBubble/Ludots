#!/usr/bin/env python3
"""Generate Ability feature player wiki pages from vignettes."""
from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from ability_feature_catalog import (  # noqa: E402
    FAMILY_LABELS,
    FEATURES,
    GALLERY_REL,
    HANDBOOK,
    PREFIX,
    WIKI_REL,
)


def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def write_page(path: Path, feature: dict, ability: dict) -> None:
    sid = PREFIX + feature["feature"]
    media = f"artifacts/evidence/{sid}"
    handbook_file, handbook_label = HANDBOOK[feature["family"]]
    excerpt = json.dumps(ability, ensure_ascii=False, indent=2)
    video_note = (
        f'<video controls playsinline preload="metadata" poster="{media}/poster.png" src="{media}/play.mp4">\n'
        f"这场还没有验收录像。启动器进 `{sid}` 看现场；采到录像后再补 {media}/play.mp4。\n"
        f"</video>"
    )
    body = f"""# {feature['title']}

{feature['beat']}

{video_note}

## 作者写法

这一场只讲一个技能合同。写法摘自画廊真实技能表，手册分册是全量字段。

手册分册：[{handbook_label}](../mod-editor-prd/config/{handbook_file})

真实用例（`mods/showcases/capability_standard/CapabilityStandardAbilityFeatureGalleryMod/assets/GAS/abilities/`）：

```json
{excerpt}
```

## 这场是怎么搭出来的

短剧自己出手，不用先学键位。字幕用这场的结果填空：

> {feature['detailTemplate']}

## 边界

- 这一场不演其它技能合同。冷却拆成「自己挂印」和「禁招印」两间房。
- 配置册上的 `cooldown` 块加载器不收，不在这场假装能用。

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch ${sid} --adapter raylib
```
"""
    path.write_text(body, encoding="utf-8")


def write_index(path: Path) -> None:
    by_family: dict[str, list[dict]] = defaultdict(list)
    for feature in FEATURES:
        by_family[feature["family"]].append(feature)
    lines = [
        "# 技能词条画廊 Wiki",
        "",
        "每个已接通的技能合同一页：一场给玩家看的短剧，加一节给 mod 作者的写法。词条清单的单一事实源是 `scripts/ability_feature_catalog.py` 与宿主 `Vignettes/{Feature}.json`。",
        "",
        "生成器：`scripts/generate-ability-feature-wiki.py`（从 catalog / vignette 生成，勿手改正文）。总规矩见 [Ability 词条画廊](../../architecture/ability-feature-gallery.md)。",
        "",
        "英雄技能沙盒是把多招串成一栏的组合戏，不是词条入口。",
        "",
    ]
    for family, items in by_family.items():
        label = FAMILY_LABELS.get(family, family)
        handbook_file, handbook_label = HANDBOOK[family]
        lines.append(f"## {label}")
        lines.append("")
        lines.append(f"> 作者语义与全量字段见手册分册 [{handbook_label}](../mod-editor-prd/config/{handbook_file})。")
        lines.append("")
        for feature in items:
            lines.append(f"- [{feature['title']}]({feature['feature']}.md) — {feature['beat']}")
        lines.append("")
    path.write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", default=str(Path(__file__).resolve().parent.parent))
    args = parser.parse_args()
    repo = Path(args.repo).resolve()
    wiki = repo / WIKI_REL
    wiki.mkdir(parents=True, exist_ok=True)
    by_id = {f["feature"]: f for f in FEATURES}
    for path in sorted((repo / GALLERY_REL / "assets" / "Vignettes").glob("*.json")):
        data = load(path)
        feature = by_id[data["feature"]]
        write_page(wiki / f"{feature['feature']}.md", feature, feature["ability"])
    keep = {f"{f['feature']}.md" for f in FEATURES} | {"README.md"}
    for stale in wiki.glob("*.md"):
        if stale.name not in keep:
            stale.unlink()
    write_index(wiki / "README.md")
    print(f"Wrote {len(FEATURES)} Ability feature wiki pages under {WIKI_REL}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
