#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""从代码与资产抽取 PRD 手册用事实（计数/默认值/上限），生成 facts.md。
文档只引用本页数值，禁止手抄——再生成：python scripts/generate-prd-facts.py"""
import json, re, collections
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "gitbook/reference/mod-editor-prd/facts.md"

def main():
    lines = ["# 事实与取值表（生成物）", "",
             "> 由 `scripts/generate-prd-facts.py` 从代码与资产抽取；**勿手改**，再生成本页。",
             f"> 生成时间：{datetime.now(timezone.utc).astimezone().isoformat(timespec='seconds')}", ""]

    # 1) 配置目录
    cat = json.loads((ROOT / "assets/config_catalog.json").read_text(encoding="utf-8"))
    dom = collections.Counter(e["Path"].split("/")[0] for e in cat)
    shards = [(e["Path"], e["ShardDirectories"], e.get("AllowEmpty", False))
              for e in cat if e.get("ShardDirectories")]
    lines += [f"## 配置目录（assets/config_catalog.json）", "",
              f"- 条目总数：**{len(cat)}**",
              "- 按域：" + "、".join(f"{k} {v}" for k, v in sorted(dom.items(), key=lambda x: -x[1])),
              f"- 启用分片的表：**{len(shards)}** 张"]
    for p, d, ae in shards:
        lines.append(f"  - `{p}` → 分片目录 `{d[0]}`{('（AllowEmpty）' if ae else '')}")
    lines.append("")

    # 2) game.json 关键值
    g = json.loads((ROOT / "assets/game.json").read_text(encoding="utf-8"))
    cap = g.get("gasRuntimeCapacity", {})
    lines += ["## 游戏配置基线（assets/game.json）", "",
              f"- `targetFps`：{g.get('targetFps')}（代码默认 60）",
              f"- 窗口：{g.get('windowWidth')}×{g.get('windowHeight')}，resizable={g.get('windowResizable')}",
              f"- 仿真预算：{g.get('simulationBudgetMsPerFrame')}ms/帧，最大切片 {g.get('simulationMaxSlicesPerLogicFrame')}",
              f"- 世界：cellSize {g.get('gridCellSizeCm')}cm，宏格 {g.get('worldWidthInMacroTiles')}×{g.get('worldHeightInMacroTiles')}",
              f"- gasRuntimeCapacity 共 **{len(cap)}** 项："]
    for k, v in cap.items():
        lines.append(f"  - `{k}` = {v}")
    lines += ["  - 交叉约束（代码校验）：`orderAdmissionResultCapacity ≥ orderQueueCapacity × 2`、`orderAdmissionRejectionCapacity ≥ orderQueueCapacity`；两项工作预算（`abilityExecMaxWorkUnitsPerSlice`、`effectProcessingMaxWorkUnitsPerSlice`）另校验有限。", ""]

    # 3) GAS 常量上限（GasConstants.cs）
    gc = (ROOT / "src/Core/Gameplay/GAS/GasConstants.cs").read_text(encoding="utf-8")
    consts = re.findall(r"public const int (\w+) = (\d+);", gc)
    lines += ["## GAS 运行时常量上限（src/Core/Gameplay/GAS/GasConstants.cs）", ""]
    lines += [f"- `{k}` = {v}" for k, v in consts]
    lines.append("")

    # 4) 关键注册表容量
    probes = [
        ("Tag 总数上限", "src/Core/Gameplay/GAS/TagRuleRegistry.cs", r"MaxCoreTags = (\d+)"),
        ("属性总数上限", "src/Core/Gameplay/GAS/Registry/AttributeRegistry.cs", r"MaxAttributes = (\d+)"),
        ("效果模板上限", "src/Core/Gameplay/GAS/EffectTemplateRegistry.cs", r"MAX_[A-Z_]* = (\d+)"),
        ("图程序上限", "src/Core/NodeLibraries/GASGraph/Host/GraphIdRegistry.cs", r"MaxGraphs = (\d+)"),
    ]
    lines += ["## 关键注册表容量（源码常量）", ""]
    for label, rel, pat in probes:
        p = ROOT / rel
        if not p.exists():
            continue
        m = re.search(pat, p.read_text(encoding="utf-8"))
        if m:
            lines.append(f"- {label}：`{rel}` = **{m.group(1)}**")
    lines.append("")

    OUT.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    print(f"facts.md 已生成：{len(cat)} 条目录 / {len(cap)} 项容量 / {len(consts)} 个常量")

if __name__ == "__main__":
    main()
