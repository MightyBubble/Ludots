# 战绩加一

赢一场就写回地图变量，战绩板自己会涨。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_WriteMapVarInt/poster.png" src="artifacts/evidence/capability_standard_graph_op_WriteMapVarInt/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_WriteMapVarInt/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Script / MapTrigger |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `source`（来源实体）、`value`（数值） |
| 特殊写法 | imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[脚本控制流 · gr-op-14](../mod-editor-prd/config/gr-op-14-control-flow.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/WriteMapVarInt.json`）：

```json
{"id": "writeKills", "op": "WriteMapVarInt", "var": "gallery.kills"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "add", "fromPort": "value", "to": "writeKills", "toPort": "value"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/WriteMapVarInt.json`，共 5 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ReadMapVarInt → ConstInt → AddInt → **WriteMapVarInt**（本篇） → HaltReturnInt

图跑完，字幕报出结果：

> 这局打完战绩 {result} 场，写回地图，下一局接着数。

## 边界与更多用法

- 图种边界：可用于 Script；Effect / Score / Validation / Derived / Query 图不可用（编译期白名单拒绝）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：跨帧等待（读条、喝药回满）、子图复用、循环收口。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_WriteMapVarInt --adapter raylib
```
