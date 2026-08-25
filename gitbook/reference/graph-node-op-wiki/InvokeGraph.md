# 点名子图，指定入口直接回话

主图一声令下，子图从 boost 入口出发，把九号命令带回来。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_InvokeGraph/poster.png" src="artifacts/evidence/capability_standard_graph_op_InvokeGraph/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_InvokeGraph/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 TriggerGraph |
| 返回 | Int → 整数寄存器 |
| 输入端口（值边 toPort） | 无（不收值边，靠 imm/自身上下文） |
| 特殊写法 | 结果写入 dst 寄存器 |

手册分册（全量字段与语义）：[地图触发器 · map-02](../mod-editor-prd/config/map-02-triggers.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/InvokeGraph.json`）：

```json
{"id": "call", "op": "InvokeGraph", "functionName": "showcase.graph_op._invokeArgCallee", "entryLabel": "boost"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/InvokeGraph.json`，共 2 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

**InvokeGraph**（本篇） → HaltReturnInt

图跑完，字幕报出结果：

> 点名入口 {entry}，子图原路回传 {result}。

## 边界与更多用法

- 图种边界：可用于 TriggerGraph；Effect / Score / Validation / Derived / Query / Script 图不可用（编译期白名单拒绝）。
- 不接值边：输入来自 imm 与运行时上下文（施法者、显式目标等）。
- 同类用法：见手册分册的场景节。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_InvokeGraph --adapter raylib
```
