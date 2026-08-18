# 名单取前三个

圈里五个人各有一个编号，亮着的是编号最靠前的三个。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_QueryLimit/poster.png" src="artifacts/evidence/capability_standard_graph_op_QueryLimit/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_QueryLimit/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 六种全可用（Effect / Score / Validation / Derived / Query / Script） |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `list`（目标名单） |
| 特殊写法 | imm 填整数立即数 |

手册分册（全量字段与语义）：[图文档写法 · gr-02](../mod-editor-prd/config/gr-02-document.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QueryLimit.json`）：

```json
{"id": "limit", "op": "QueryLimit", "intValue": 3}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QueryLimit.json`，共 5 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

QueryRadius → LoadCaster → QueryFilterNotEntity → QuerySortStable → **QueryLimit**（本篇）

图跑完，字幕报出结果：

> 按编号点名，留下前三个。

## 边界与更多用法

- 图种边界：六种图全都能用，不必为它挑图种。
- 同类用法：多节点串成完整小玩法的组合示范，可整段抄走改。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_QueryLimit --adapter raylib
```
