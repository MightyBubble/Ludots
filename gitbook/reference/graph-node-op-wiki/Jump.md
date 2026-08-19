# 满了就跳过续杯

杯是满的：续杯那几行被划掉，指针直接飞到收工行。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_Jump/poster.png" src="artifacts/evidence/capability_standard_graph_op_Jump/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_Jump/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Script / MapTrigger |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | 无（不收值边，靠 imm/自身上下文） |
| 特殊写法 | — |

手册分册（全量字段与语义）：[脚本控制流 · gr-op-14](../mod-editor-prd/config/gr-op-14-control-flow.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/Jump.json`）：

```json
{"id": "skip", "op": "Jump"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/Jump.json`，共 12 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ConstInt → ConstInt → ConstInt → MoveInt → MoveInt → CompareLtInt → JumpIfFalse → **Jump**（本篇） → MoveInt → MoveInt → AddInt → HaltReturnInt

图跑完，字幕报出结果：

> 满了就不续了。茶水 {water}/{limit}，续杯那几行划掉，直接收工。

## 边界与更多用法

- 图种边界：可用于 Script；Effect / Score / Validation / Derived / Query 图不可用（编译期白名单拒绝）。
- 不接值边：输入来自 imm 与运行时上下文（施法者、显式目标等）。
- 同类用法：跨帧等待（读条、喝药回满）、子图复用、循环收口。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_Jump --adapter raylib
```
