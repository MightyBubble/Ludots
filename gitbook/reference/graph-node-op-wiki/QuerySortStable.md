# 点名名单按编号排好，次次一样

每波点名，五个人 1 到 5 的编号顺序一模一样，灰影对得上。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_QuerySortStable/poster.png" src="artifacts/evidence/capability_standard_graph_op_QuerySortStable/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_QuerySortStable/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 六种全可用（Effect / Score / Validation / Derived / Query / Script） |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `list`（目标名单） |
| 特殊写法 | — |

手册分册（全量字段与语义）：[图文档写法 · gr-02](../mod-editor-prd/config/gr-02-document.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QuerySortStable.json`）：

```json
{"id": "sort", "op": "QuerySortStable"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QuerySortStable.json`，共 4 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

QueryRadius → LoadCaster → QueryFilterNotEntity → **QuerySortStable**（本篇）

图跑完，字幕报出结果：

> 点名顺序稳定：{order}。

## 边界与更多用法

- 图种边界：六种图全都能用，不必为它挑图种。
- 同类用法：多节点串成完整小玩法的组合示范，可整段抄走改。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_QuerySortStable --adapter raylib
```
