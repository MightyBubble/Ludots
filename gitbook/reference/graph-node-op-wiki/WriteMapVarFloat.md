# 士气补给

一次补给写回士气变量，地图记得这份涨幅。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_WriteMapVarFloat/poster.png" src="artifacts/evidence/capability_standard_graph_op_WriteMapVarFloat/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_WriteMapVarFloat/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Script / TriggerGraph |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `source`（来源实体）、`value`（数值） |
| 特殊写法 | imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[脚本控制流 · gr-op-14](../mod-editor-prd/config/gr-op-14-control-flow.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/WriteMapVarFloat.json`）：

```json
{"id": "writeMorale", "op": "WriteMapVarFloat", "var": "gallery.morale"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "boost", "fromPort": "value", "to": "writeMorale", "toPort": "value"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/WriteMapVarFloat.json`，共 3 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ConstFloat → **WriteMapVarFloat**（本篇） → HaltReturnInt

图跑完，字幕报出结果：

> 补给到账，士气变量写回了新读数。

## 边界与更多用法

- 图种边界：可用于 Script / TriggerGraph；Effect / Score / Validation / Derived / Query 图不可用（编译期白名单拒绝）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：跨帧等待（读条、喝药回满）、子图复用、循环收口。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_WriteMapVarFloat --adapter raylib
```
