# 朝这个方向的扇形里有谁

描边扇形罩住的人亮，贴着边站歪一点的不亮。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_QueryCone/poster.png" src="artifacts/evidence/capability_standard_graph_op_QueryCone/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_QueryCone/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Effect / Score / Validation / Derived |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `a`（第一操作数）、`b`（第二操作数） |
| 特殊写法 | flags 填空间容量档 |

手册分册（全量字段与语义）：[空间圈人 · gr-op-06](../mod-editor-prd/config/gr-op-06-spatial.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QueryCone.json`）：

```json
{"id": "cone", "op": "QueryCone", "queryCapacityPolicy": "RequireComplete", "rangeCm": 800}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "coneDir", "fromPort": "value", "to": "cone", "toPort": "a"}
{"from": "coneHalf", "fromPort": "value", "to": "cone", "toPort": "b"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QueryCone.json`，共 5 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ConstInt → ConstInt → **QueryCone**（本篇） → LoadCaster → QueryFilterNotEntity

图跑完，字幕报出结果：

> 扇形扫过，弧内{count}人。

## 边界与更多用法

- 图种边界：可用于 Effect / Score / Validation / Derived；Query / Script / TriggerGraph 图不可用（编译期白名单拒绝）。
- 同类用法：范围技能圈人、六角战棋邻域/环带、扇形与矩形范围判定。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_QueryCone --adapter raylib
```
