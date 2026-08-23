# 按名单取第一个

名单按序编号，红线只连 1 号。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_TargetListGet/poster.png" src="artifacts/evidence/capability_standard_graph_op_TargetListGet/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_TargetListGet/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Effect / Score / Validation / Derived / Script / TriggerGraph |
| 返回 | Entity → 实体寄存器 |
| 输入端口（值边 toPort） | `value`（数值） |
| 特殊写法 | 结果写入 dst 寄存器；flags 填布尔暂存位编号 |

手册分册（全量字段与语义）：[空间圈人 · gr-op-06](../mod-editor-prd/config/gr-op-06-spatial.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/TargetListGet.json`）：

```json
{"id": "get0", "op": "TargetListGet"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "zero", "fromPort": "value", "to": "get0", "toPort": "value"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/TargetListGet.json`，共 7 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ConstInt → ConstInt → QueryCone → LoadCaster → QueryFilterNotEntity → ConstInt → **TargetListGet**（本篇）

图跑完，字幕报出结果：

> 名单第 1 个，是{name}。

## 边界与更多用法

- 图种边界：可用于 Effect / Score / Validation / Derived / Script / TriggerGraph；Query 图不可用（编译期白名单拒绝）。
- 同类用法：范围技能圈人、六角战棋邻域/环带、扇形与矩形范围判定。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_TargetListGet --adapter raylib
```
