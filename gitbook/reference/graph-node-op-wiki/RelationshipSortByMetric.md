# 按好感排个序

按好感高低挂出名次牌。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_RelationshipSortByMetric/poster.png" src="artifacts/evidence/capability_standard_graph_op_RelationshipSortByMetric/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 artifacts/evidence/capability_standard_graph_op_RelationshipSortByMetric/play.mp4。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 Query |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `list`（目标名单）、`source`（来源实体） |
| 特殊写法 | dst 填符号名（编译期解析）；imm 填符号名（编译期解析）；flags 填降序开关 |

手册分册（全量字段与语义）：[关系与好感 · gr-op-08](../mod-editor-prd/config/gr-op-08-relationship.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/RelationshipSortByMetric.json`）：

```json
{"id": "sort", "op": "RelationshipSortByMetric", "relationshipType": "SocialBond", "metric": "Loyalty", "descending": true}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "outgoing", "fromPort": "list", "to": "sort", "toPort": "list"}
{"from": "source", "fromPort": "value", "to": "sort", "toPort": "source"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/RelationshipSortByMetric.json`，共 3 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

LoadCaster → RelationshipQueryOutgoing → **RelationshipSortByMetric**（本篇）

图跑完，字幕报出结果：

> 排第一是{friend}，好感 {loyalty}。

## 边界与更多用法

- 图种边界：可用于 Query；Effect / Score / Validation / Derived / Script / TriggerGraph 图不可用（编译期白名单拒绝）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：好感与敌友判定、关系数值的聚合与排序、信任旗/失和旗这类关系玩法。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_RelationshipSortByMetric --adapter raylib
```
