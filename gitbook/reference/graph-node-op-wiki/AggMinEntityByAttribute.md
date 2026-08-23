# 点名最残的那个

全场退成灰影，空血条那个被点名徽钉住。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_AggMinEntityByAttribute/poster.png" src="artifacts/evidence/capability_standard_graph_op_AggMinEntityByAttribute/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_AggMinEntityByAttribute/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 Query |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `list`（目标名单） |
| 特殊写法 | imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[名单筛选与汇总 · gr-op-07](../mod-editor-prd/config/gr-op-07-entityset.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AggMinEntityByAttribute.json`）：

```json
{"id": "minEnt", "op": "AggMinEntityByAttribute", "attribute": "Health"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "all", "fromPort": "list", "to": "minEnt", "toPort": "list"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AggMinEntityByAttribute.json`，共 2 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

QueryAllMapEntities → **AggMinEntityByAttribute**（本篇）

图跑完，字幕报出结果：

> 被点名的是{label}，血{hp}，点名线从指挥席拉到他头顶。

## 边界与更多用法

- 图种边界：可用于 Query；Effect / Score / Validation / Derived / Script 图不可用（编译期白名单拒绝）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：战场统计（全场均值/最值）、点名最残或最能扛的目标、按条件筛名单再排序。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_AggMinEntityByAttribute --adapter raylib
```
