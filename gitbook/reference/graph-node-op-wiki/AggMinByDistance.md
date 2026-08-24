# 扇形里谁离我最近

每人拉一条线，最短的那条留下。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_AggMinByDistance/poster.png" src="artifacts/evidence/capability_standard_graph_op_AggMinByDistance/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_AggMinByDistance/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 七种全可用（Effect / Score / Validation / Derived / Query / Script / TriggerGraph） |
| 返回 | Entity → 实体寄存器 |
| 输入端口（值边 toPort） | `list`（目标名单） |
| 特殊写法 | 结果写入 dst 寄存器 |

手册分册（全量字段与语义）：[空间圈人 · gr-op-06](../mod-editor-prd/config/gr-op-06-spatial.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AggMinByDistance.json`）：

```json
{"id": "nearest", "op": "AggMinByDistance"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AggMinByDistance.json`，共 6 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ConstInt → ConstInt → QueryCone → LoadCaster → QueryFilterNotEntity → **AggMinByDistance**（本篇）

图跑完，字幕报出结果：

> 最近的是{name}。

## 边界与更多用法

- 图种边界：七种图全都能用，不必为它挑图种。
- 同类用法：范围技能圈人、六角战棋邻域/环带、扇形与矩形范围判定。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_AggMinByDistance --adapter raylib
```
