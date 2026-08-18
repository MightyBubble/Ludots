# 站圈心喊一嗓子，看看圈里有谁

黄圈内五个兵亮起来，施法者自己不算，圈外两人没反应。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_QueryRadius/poster.png" src="artifacts/evidence/capability_standard_graph_op_QueryRadius/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_QueryRadius/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 六种全可用（Effect / Score / Validation / Derived / Query / Script） |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | 无（不收值边，靠 imm/自身上下文） |
| 特殊写法 | imm 填小数立即数；flags 填空间容量档 |

手册分册（全量字段与语义）：[图文档写法 · gr-02](../mod-editor-prd/config/gr-02-document.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QueryRadius.json`）：

```json
{"id": "radius", "op": "QueryRadius", "queryCapacityPolicy": "RequireComplete", "radiusCm": 800}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QueryRadius.json`，共 3 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

**QueryRadius**（本篇） → LoadCaster → QueryFilterNotEntity

图跑完，字幕报出结果：

> 圈内亮起{count}个兵，不含施法者；圈外两人不亮。

## 边界与更多用法

- 图种边界：六种图全都能用，不必为它挑图种。
- 不接值边：输入来自 imm 与运行时上下文（施法者、显式目标等）。
- 同类用法：多节点串成完整小玩法的组合示范，可整段抄走改。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_QueryRadius --adapter raylib
```
