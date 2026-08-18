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
import re
import sys
from collections import defaultdict
from pathlib import Path

PREFIX = "capability_standard_graph_op_"
GALLERY_REL = "mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod"
COVERAGE_REL = "assets/GAS/graph_node_op_coverage.registry.json"
WIKI_REL = "gitbook/reference/graph-node-op-wiki"
EVIDENCE_REL = "artifacts/evidence"
DESCRIPTOR_REL = "src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs"
HANDBOOK_CONFIG_REL = "gitbook/reference/mod-editor-prd/config"

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

HANDBOOK_BY_DRIVER = {
    "linear": ("gr-op-02-math.md", "算术与比较 · gr-op-02"),
    "attr": ("gr-op-04-attributes.md", "属性与效果 · gr-op-04"),
    "script": ("gr-op-14-control-flow.md", "脚本控制流 · gr-op-14"),
    "spatial": ("gr-op-06-spatial.md", "空间圈人 · gr-op-06"),
    "query": ("gr-op-07-entityset.md", "名单筛选与汇总 · gr-op-07"),
    "rel": ("gr-op-08-relationship.md", "关系与好感 · gr-op-08"),
    "blackboard": ("gr-op-05-blackboard.md", "黑板与配置 · gr-op-05"),
    "event": ("gr-op-01-context.md", "事件与情境 · gr-op-01"),
    "sandbox": ("gr-02-document.md", "图文档写法 · gr-02"),
}

KIND_MASK_LABELS = {
    "LinearAll": "Effect / Score / Validation / Derived",
    "LinearEffect": "仅 Effect",
    "LinearEffectDerived": "Effect / Derived",
    "QueryOnly": "仅 Query",
    "ScriptOnlyMask": "仅 Script",
    "LinearAndQuery": "Effect / Score / Validation / Derived / Query",
    "LinearAndScript": "Effect / Score / Validation / Derived / Script",
    "LinearQueryScript": "六种全可用（Effect / Score / Validation / Derived / Query / Script）",
    "EffectAndScript": "Effect / Script",
}

TYPE_LABELS = {
    "GraphValueType.Void": "无（副作用节点）",
    "GraphValueType.Entity": "Entity → 实体寄存器",
    "GraphValueType.Float": "Float → 小数寄存器",
    "GraphValueType.Int": "Int → 整数寄存器",
    "GraphValueType.Bool": "Bool → 布尔槽",
    "GraphValueType.TargetList": "TargetList → 目标名单",
}

ROLE_LABELS = {
    "GraphOperandRole.None": "—",
    "GraphOperandRole.DstRegister": "结果写入 dst 寄存器",
    "GraphOperandRole.SymbolDst": "dst 填符号名（编译期解析）",
    "GraphOperandRole.SrcRegisterA": "a 槽填寄存器编号",
    "GraphOperandRole.SrcRegisterB": "b 槽填寄存器编号",
    "GraphOperandRole.SrcRegisterC": "c 槽填寄存器编号",
    "GraphOperandRole.BoolScratchFlags": "flags 填布尔暂存位编号",
    "GraphOperandRole.SymbolImm": "imm 填符号名（编译期解析）",
    "GraphOperandRole.Immediate": "imm 填整数立即数",
    "GraphOperandRole.ImmediateFloat": "imm 填小数立即数",
    "GraphOperandRole.SymbolFlags": "flags 填符号名",
    "GraphOperandRole.FuncLibNameFlags": "flags 填函数库名",
    "GraphOperandRole.SpatialCapacityFlags": "flags 填空间容量档",
    "GraphOperandRole.SortDescendingFlags": "flags 填降序开关",
    "GraphOperandRole.TeamIdSourceFlags": "flags 填队伍来源",
    "GraphOperandRole.RelationshipTypeFlags": "flags 填关系类型",
    "GraphOperandRole.ReasonIdDst": "dst 填原因 id",
    "GraphOperandRole.DispatchPresetDst": "dst 填派发预设目的位",
}

PORT_GLOSSARY = {
    "a": "第一操作数",
    "b": "第二操作数",
    "c": "第三操作数",
    "source": "来源实体",
    "target": "目标实体",
    "value": "数值",
    "min": "下限",
    "max": "上限",
    "condition": "条件",
    "list": "目标名单",
    "teamid": "队伍 id",
}

ADD_PARAM_ORDER = [
    "authorable", "linearOut", "linearPorts", "queryOut", "queryPorts",
    "scriptPorts", "scriptOut", "dst", "flags", "imm",
    "scriptOnly", "derivedWrite", "listenerOwner",
]


def _split_args(text: str) -> list[str]:
    args: list[str] = []
    depth = 0
    cur: list[str] = []
    for ch in text:
        if ch in "({[":
            depth += 1
        elif ch in ")}]":
            depth -= 1
        if ch == "," and depth == 0:
            args.append("".join(cur))
            cur = []
        else:
            cur.append(ch)
    if cur:
        args.append("".join(cur))
    return [a.strip() for a in args if a.strip()]


def parse_descriptor_table(path: Path) -> dict[str, dict]:
    """解析引擎描述表：op → 图种/返回类型/端口/操作数角色（作者签名 SSOT）。"""
    text = path.read_text(encoding="utf-8")
    port_arrays: dict[str, list[str]] = {"noPorts": []}
    for m in re.finditer(r"string\[\]\s+(\w+)\s*=\s*\{([^}]*)\}", text):
        port_arrays[m.group(1)] = re.findall(r"GraphControlFlowPorts\.(\w+)", m.group(2))

    descs: dict[str, dict] = {}
    for m in re.finditer(r"Add\(\s*rows\s*,(.*?)\);", text, re.S):
        args = _split_args(m.group(1))
        opm = re.fullmatch(r"GraphNodeOp\.(\w+)", args[0])
        if not opm:
            raise SystemExit(f"描述表 Add 调用第一参数不是 GraphNodeOp：{args[0][:50]}")
        op = opm.group(1)
        kw: dict[str, str] = {}
        positional: list[str] = []
        for a in args[1:]:
            nm = re.match(r"(\w+):\s*(.+)$", a, re.S)
            if nm:
                kw[nm.group(1)] = nm.group(2).strip()
            else:
                positional.append(a)
        for i, v in enumerate(positional):
            kw[ADD_PARAM_ORDER[i]] = v

        mask = kw["authorable"].split(".")[-1]
        if mask not in KIND_MASK_LABELS:
            raise SystemExit(f"未知图种掩码 {mask}（op={op}）：请在生成器 KIND_MASK_LABELS 补词条")

        def type_of(key: str) -> str:
            raw = kw.get(key, "GraphValueType.Void")
            if raw not in TYPE_LABELS:
                raise SystemExit(f"未知返回类型 {raw}（op={op}）：补 TYPE_LABELS")
            return TYPE_LABELS[raw]

        def ports_of(*keys: str) -> list[str]:
            names: list[str] = []
            for key in keys:
                raw = (kw.get(key) or "").strip()
                if not raw or raw == "null":
                    continue
                if raw.startswith("new"):
                    inline = re.findall(r"GraphControlFlowPorts\.(\w+)", raw)
                    if not inline:
                        raise SystemExit(f"内联端口数组解析为空（op={op}）：{raw[:60]}")
                    candidates = inline
                elif raw in port_arrays:
                    candidates = port_arrays[raw]
                else:
                    raise SystemExit(f"未知端口数组 {raw}（op={op}）")
                for p in candidates:
                    if p not in names:
                        names.append(p)
            return names

        def role_of(key: str) -> str:
            raw = kw.get(key, "GraphOperandRole.None")
            if raw not in ROLE_LABELS:
                raise SystemExit(f"未知操作数角色 {raw}（op={op}）：补 ROLE_LABELS")
            return ROLE_LABELS[raw]

        descs[op] = {
            "kinds": KIND_MASK_LABELS[mask],
            "out": type_of("linearOut"),
            "ports": ports_of("linearPorts", "queryPorts", "scriptPorts"),
            "dst": role_of("dst") if kw.get("dst") else (
                "结果写入 dst 寄存器" if kw.get("linearOut", "GraphValueType.Void") != "GraphValueType.Void"
                else "—"
            ),
            "imm": role_of("imm"),
            "flags": role_of("flags"),
        }
    return descs


def extract_usage(graph_path: Path, op: str) -> tuple[str, list[str]]:
    """从画廊作者图提取该 op 的真实节点条目与接进它的值边（写法示例 SSOT）。"""
    docs = load(graph_path)
    node = None
    for doc in docs:
        for n in doc.get("nodes", []):
            if n.get("op") == op:
                node = (doc, n)
                break
        if node:
            break
    if not node:
        raise SystemExit(f"作者图 {graph_path} 中找不到 {op} 节点")
    doc, n = node
    entry = json.dumps(n, ensure_ascii=False)
    wires = [
        json.dumps(e, ensure_ascii=False)
        for e in doc.get("valueEdges", [])
        if e.get("to") == n["id"]
    ]
    return entry, wires[:2]


def author_section(repo: Path, op: str, driver: str, desc: dict, graph_path: Path) -> str:
    usage, wires = extract_usage(graph_path, op)
    handbook_file, handbook_label = HANDBOOK_BY_DRIVER.get(driver, (None, None))
    if not handbook_file:
        raise SystemExit(f"家族 {driver} 未配手册分册映射（op={op}）")
    target = repo / HANDBOOK_CONFIG_REL / handbook_file
    if not target.is_file():
        raise SystemExit(f"手册分册不存在：{target}")

    ports = "、".join(
        f"`{p.lower()}`（{PORT_GLOSSARY.get(p.lower(), '见手册分册')}）" for p in desc["ports"]
    ) or "无（不收值边，靠 imm/自身上下文）"
    specials = "；".join(x for x in (desc["dst"], desc["imm"], desc["flags"]) if x and x != "—") or "—"
    wire_block = ""
    if wires:
        wire_block = "\n\n接线（值边把上一步的结果送进本节点端口）：\n\n```json\n" + "\n".join(wires) + "\n```\n"

    return f"""## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | {desc['kinds']} |
| 返回 | {desc['out']} |
| 输入端口（值边 toPort） | {ports} |
| 特殊写法 | {specials} |

手册分册（全量字段与语义）：[{handbook_label}](../mod-editor-prd/config/{handbook_file})

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/{op}.json`）：

```json
{usage}
```{wire_block.strip()}
"""


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


def write_op_page(path: Path, vignette: dict, author_md: str) -> None:
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

{author_md}

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
        "每个可执行图节点两页视角合一：一场给玩家看的短剧（录像 + 人话字幕），加一节给 mod 作者的写法（签名表 + 真实用例 + 手册分册链接）。下面按玩法家族分组。",
        "",
        "生成器：`scripts/generate-graph-op-node-wiki.py`（从 vignette 与引擎描述表生成，勿手改正文）。",
        "",
    ]
    for driver, items in sorted(by_driver.items(), key=lambda kv: DRIVER_LABELS.get(kv[0], kv[0])):
        label = DRIVER_LABELS.get(driver, driver)
        handbook_file, handbook_label = HANDBOOK_BY_DRIVER[driver]
        lines.append(f"## {label}")
        lines.append("")
        lines.append(f"> 作者语义与全量字段见手册分册 [{handbook_label}](../mod-editor-prd/config/{handbook_file})。")
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
    descs = parse_descriptor_table(repo / DESCRIPTOR_REL)
    missing_desc = [op for op in ops if op not in descs]
    if missing_desc:
        raise SystemExit(
            "描述表缺少这些 op 的签名（引擎与覆盖注册表不同步）：\n" + "\n".join(missing_desc)
        )
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
        graph_path = repo / GALLERY_REL / "assets" / "gas" / "graphs" / f"{op}.json"
        author_md = author_section(repo, op, vignette.get("driver", "sandbox"), descs[op], graph_path)
        write_op_page(wiki_dir / f"{op}.md", vignette, author_md)
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
