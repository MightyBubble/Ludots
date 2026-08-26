# 点名地图区域，名册有没有就报数

记录官点名营地圈，地图名册回 1；点名不存在的鬼区，名册回 0。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_LoadPlacedRegion/poster.png" src="artifacts/evidence/capability_standard_graph_op_LoadPlacedRegion/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_LoadPlacedRegion/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 TriggerGraph |
| 返回 | Int → 整数寄存器 |
| 输入端口（值边 toPort） | 无（不收值边，靠 imm/自身上下文） |
| 特殊写法 | 结果写入 dst 寄存器；imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[地图触发器 · map-02](../mod-editor-prd/config/map-02-triggers.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/LoadPlacedRegion.json`）：

```json
{"id": "readYard", "op": "LoadPlacedRegion", "instanceId": "yard"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/LoadPlacedRegion.json`，共 6 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

LoadCaster → **LoadPlacedRegion**（本篇） → WriteMapVarInt → **LoadPlacedRegion**（本篇） → WriteMapVarInt → HaltReturnInt

图跑完，字幕报出结果：

> 区域点名：营地圈={yard}，鬼区={ghost}。

## 边界与更多用法

- 图种边界：可用于 TriggerGraph；Effect / Score / Validation / Derived / Query / Script 图不可用（编译期白名单拒绝）。
- 不接值边：输入来自 imm 与运行时上下文（施法者、显式目标等）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：见手册分册的场景节。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_LoadPlacedRegion --adapter raylib
```
