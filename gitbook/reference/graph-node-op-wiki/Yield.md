# 续一杯，歇一口气

每续一杯就停一拍：人影顿一下，杯里水涨一格，三格满就完。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_Yield/poster.png" src="artifacts/evidence/capability_standard_graph_op_Yield/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_Yield/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 Script |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | 无（不收值边，靠 imm/自身上下文） |
| 特殊写法 | — |

手册分册（全量字段与语义）：[脚本控制流 · gr-op-14](../mod-editor-prd/config/gr-op-14-control-flow.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/Yield.json`）：

```json
{"id": "sipYield", "op": "Yield"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/Yield.json`，共 12 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ConstInt → ConstInt → ConstInt → MoveInt → MoveInt → CompareLtInt → JumpIfFalse → MoveInt → MoveInt → AddInt → **Yield**（本篇） → HaltReturnInt

图跑完，字幕报出结果：

> 续一杯歇一口气。茶水 {water}/{limit}，歇的次数就是涨的格数。

## 边界与更多用法

- 图种边界：可用于 Script；Effect / Score / Validation / Derived / Query 图不可用（编译期白名单拒绝）。
- 不接值边：输入来自 imm 与运行时上下文（施法者、显式目标等）。
- 同类用法：跨帧等待（读条、喝药回满）、子图复用、循环收口。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_Yield --adapter raylib
```
