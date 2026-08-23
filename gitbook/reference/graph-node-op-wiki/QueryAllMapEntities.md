# 把场上的人全点名

扫描弧从指挥席扫过全场，点到谁谁亮。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_QueryAllMapEntities/poster.png" src="artifacts/evidence/capability_standard_graph_op_QueryAllMapEntities/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_QueryAllMapEntities/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 Query |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | 无（不收值边，靠 imm/自身上下文） |
| 特殊写法 | — |

手册分册（全量字段与语义）：[名单筛选与汇总 · gr-op-07](../mod-editor-prd/config/gr-op-07-entityset.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QueryAllMapEntities.json`）：

```json
{"id": "all", "op": "QueryAllMapEntities"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QueryAllMapEntities.json`，共 1 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

**QueryAllMapEntities**（本篇）

图跑完，字幕报出结果：

> 扫完一遍，场上{count}人个个有圈，计数牌停在{count}。

## 边界与更多用法

- 图种边界：可用于 Query；Effect / Score / Validation / Derived / Script / TriggerGraph 图不可用（编译期白名单拒绝）。
- 不接值边：输入来自 imm 与运行时上下文（施法者、显式目标等）。
- 同类用法：战场统计（全场均值/最值）、点名最残或最能扛的目标、按条件筛名单再排序。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_QueryAllMapEntities --adapter raylib
```
