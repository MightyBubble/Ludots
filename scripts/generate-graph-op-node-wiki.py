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
KIND_ENUM_REL = "src/Core/NodeLibraries/GASGraph/GraphOpDescriptor.cs"
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
    "entryPayload": "事件载荷捕获",
    "invokeGraph": "子图调用与事件派发",
    "placedEntity": "放置实体名册",
    "placedRegion": "放置区域名册",
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
    "entryPayload": ("map-02-triggers.md", "地图触发器 · map-02"),
    "invokeGraph": ("map-02-triggers.md", "地图触发器 · map-02"),
    "placedEntity": ("map-02-triggers.md", "地图触发器 · map-02"),
    "placedRegion": ("map-02-triggers.md", "地图触发器 · map-02"),
    "sandbox": ("gr-02-document.md", "图文档写法 · gr-02"),
}

ALL_KINDS = [
    "Effect", "Score", "Validation", "Derived", "Query", "Script", "TriggerGraph"
]

_CN_NUM = {1: "一", 2: "二", 3: "三", 4: "四", 5: "五", 6: "六", 7: "七", 8: "八", 9: "九"}


def _cn(n: int) -> str:
    return _CN_NUM.get(n, str(n))


def parse_kind_enum(path: Path) -> list[str]:
    """读 GraphKindMask 枚举：图种全集的单一事实源。"""
    text = path.read_text(encoding="utf-8")
    m = re.search(r"enum\s+GraphKindMask\s*:\s*byte\s*\{(.*?)\}", text, re.S)
    if not m:
        raise SystemExit(f"GraphKindMask 枚举定义未找到：{path}")
    return [n for n in re.findall(r"\b([A-Z]\w*)\s*=", m.group(1)) if n != "None"]


def parse_kind_masks(text: str) -> dict[str, list[str]]:
    """解析 GraphOpDescriptorTable.Data.cs 里的 authorableKinds 掩码常量，按定义顺序展开为图种名列表。"""
    consts: dict[str, str] = {}
    for m in re.finditer(r"private\s+const\s+GraphKindMask\s+(\w+)\s*=\s*([^;]*);", text):
        consts[m.group(1)] = m.group(2)

    def expand(expr: str, chain: tuple[str, ...]) -> list[str]:
        kinds: list[str] = []
        for member, name in re.findall(r"GraphKindMask\.(\w+)|\b([A-Za-z_]\w*)\b", expr):
            if member:
                if member != "None":
                    kinds.append(member)
            elif name in consts:
                if name in chain:
                    raise SystemExit(f"GraphKindMask 掩码常量循环引用：{' → '.join(chain + (name,))}")
                kinds.extend(expand(consts[name], chain + (name,)))
        out: list[str] = []
        for k in kinds:
            if k not in out:
                out.append(k)
        return out

    return {name: expand(expr, (name,)) for name, expr in consts.items()}


def render_kind_label(names: list[str]) -> str:
    """把图种名列表渲染成作者表里的「可用图种」文案。"""
    if len(names) == 1:
        return f"仅 {names[0]}"
    if names == ALL_KINDS:
        return f"{_cn(len(names))}种全可用（{' / '.join(names)}）"
    return " / ".join(names)

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


def parse_descriptor_table(path: Path, kind_masks: dict[str, list[str]]) -> dict[str, dict]:
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
        if mask in kind_masks:
            kind_names = kind_masks[mask]
        elif all(part.strip() in kind_masks for part in mask.split("|")):
            merged: set[str] = set()
            for part in mask.split("|"):
                merged.update(kind_masks[part.strip()])
            kind_names = [k for k in ALL_KINDS if k in merged]
        else:
            raise SystemExit(f"未知图种掩码 {mask}（op={op}）：GraphOpDescriptorTable.Data.cs 未定义该 authorableKinds 常量")

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
            "kind_names": kind_names,
            "kinds": render_kind_label(kind_names),
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


def find_op_doc(graph_path: Path, op: str) -> dict:
    """定位作者图中含该 op 的图条目（写法示例与节点序列共用）。"""
    docs = load(graph_path)
    for doc in docs:
        for n in doc.get("nodes", []):
            if n.get("op") == op:
                return doc
    raise SystemExit(f"作者图 {graph_path} 中找不到 {op} 节点")


def extract_usage(doc: dict, op: str) -> tuple[str, list[str]]:
    """提取该 op 的真实节点条目与接进它的值边（写法示例 SSOT）。"""
    n = next(n for n in doc.get("nodes", []) if n.get("op") == op)
    entry = json.dumps(n, ensure_ascii=False)
    wires = [
        json.dumps(e, ensure_ascii=False)
        for e in doc.get("valueEdges", [])
        if e.get("to") == n["id"]
    ]
    return entry, wires


def node_sequence(doc: dict, op: str) -> list[tuple[str, bool]]:
    """按控制流从 entry 走出节点执行序（主角 op 标记 True）。"""
    by_id = {n["id"]: n for n in doc.get("nodes", [])}
    next_of: dict[str, str] = {}
    for e in doc.get("controlEdges", []):
        if e.get("fromPort", "next") == "next":
            next_of.setdefault(e["from"], e["to"])
    order: list[tuple[str, bool]] = []
    cur = doc.get("entry")
    seen: set[str] = set()
    while cur and cur in by_id and cur not in seen:
        seen.add(cur)
        order.append((by_id[cur]["op"], by_id[cur]["op"] == op))
        cur = next_of.get(cur)
    for nid, n in by_id.items():
        if nid not in seen:
            order.append((n["op"], n["op"] == op))
    return order


FAMILY_USE_CASES = {
    "event": "受击联动（挨打触发计数或外观变化）、事件决定施放哪张效果牌、与观看者相关的表现逻辑。",
    "linear": "伤害公式的缩放与浮动、斩杀线/格挡线这类阈值判断、把读数换算成另一个数。",
    "attr": "按属性读写与直写、层数叠加引爆、先查对方状态再决定出手。",
    "spatial": "范围技能圈人、六角战棋邻域/环带、扇形与矩形范围判定。",
    "query": "战场统计（全场均值/最值）、点名最残或最能扛的目标、按条件筛名单再排序。",
    "rel": "好感与敌友判定、关系数值的聚合与排序、信任旗/失和旗这类关系玩法。",
    "blackboard": "跨节点跨图传值、决策记忆（记住要盯的人）、按名册配置出招。",
    "script": "跨帧等待（读条、喝药回满）、子图复用、循环收口。",
    "sandbox": "多节点串成完整小玩法的组合示范，可整段抄走改。",
}


def scene_section(op: str, doc: dict, detail: str, graph_path: Path) -> str:
    seq = node_sequence(doc, op)
    chain = " → ".join(
        (f"**{o}**（本篇）" if star else o) for o, star in seq
    )
    return f"""## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/{op}.json`，共 {len(seq)} 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

{chain}

图跑完，字幕报出结果：

> {detail}
"""


def boundary_section(op: str, driver: str, desc: dict) -> str:
    present = [k for k in ALL_KINDS if k in desc["kind_names"]]
    missing = [k for k in ALL_KINDS if k not in present]
    lines = []
    if missing:
        lines.append(
            "图种边界：可用于 " + " / ".join(present) + "；" + " / ".join(missing) + " 图不可用（编译期白名单拒绝）。"
        )
    else:
        lines.append(f"图种边界：{_cn(len(ALL_KINDS))}种图全都能用，不必为它挑图种。")
    if not desc["ports"] or desc["ports"] == []:
        lines.append("不接值边：输入来自 imm 与运行时上下文（施法者、显式目标等）。")
    if desc["imm"] == "imm 填符号名（编译期解析）":
        lines.append("imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。")
    if desc["dst"] == "dst 填派发预设目的位":
        lines.append("dst 写派发预设位，取值来自 `assets/GAS/target_dispatch_presets.json`。")
    lines.append("同类用法：{use}".format(use=FAMILY_USE_CASES.get(driver, "见手册分册的场景节。")))
    body = "\n".join(f"- {l}" for l in lines)
    return f"""## 边界与更多用法

{body}
"""


def author_section(repo: Path, op: str, driver: str, desc: dict, doc: dict) -> str:
    usage, wires = extract_usage(doc, op)
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
```{wire_block}
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


def write_op_page(path: Path, vignette: dict, sections: list[str]) -> None:
    title = vignette["title"]
    beat = vignette["beat"]
    op = vignette["op"]
    media = evidence_dir(op)
    sid = PREFIX + op
    launch = f"scripts/run-mod-launcher.cmd cli launch ${sid} --adapter raylib"

    body = f"""# {title}

{beat}

<video controls playsinline preload="metadata" poster="{media}/poster.png" src="{media}/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `{media}/play.mp4`。
</video>

""" + (chr(10) * 2).join(sections) + f"""
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
    enum_kinds = parse_kind_enum(repo / KIND_ENUM_REL)
    if set(enum_kinds) != set(ALL_KINDS):
        raise SystemExit(
            "GraphKindMask 枚举与生成器 ALL_KINDS 不同步：\n"
            f"  枚举={enum_kinds}\n  ALL_KINDS={ALL_KINDS}"
        )
    descriptor_text = (repo / DESCRIPTOR_REL).read_text(encoding="utf-8")
    kind_masks = parse_kind_masks(descriptor_text)
    descs = parse_descriptor_table(repo / DESCRIPTOR_REL, kind_masks)
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
        driver = vignette.get("driver", "sandbox")
        graph_path = repo / GALLERY_REL / "assets" / "GAS" / "graphs" / f"{op}.json"
        doc = find_op_doc(graph_path, op)
        sections = [
            author_section(repo, op, driver, descs[op], doc).rstrip("\n"),
            scene_section(op, doc, vignette.get("detailTemplate", vignette["beat"]), graph_path).rstrip("\n"),
            boundary_section(op, driver, descs[op]).rstrip("\n"),
        ]
        write_op_page(wiki_dir / f"{op}.md", vignette, sections)
        by_driver[driver].append(vignette)

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
