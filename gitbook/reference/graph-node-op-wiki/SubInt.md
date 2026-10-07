# 离第 360 天还有几天

用第 360 天减去今天，得到还差几天。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Effect / Score / Validation / Derived / Script / TriggerGraph |
| 返回 | Int → 整数寄存器 |
| 输入端口（值边 toPort） | `a`（第一操作数）、`b`（第二操作数） |
| 特殊写法 | 结果写入 dst 寄存器 |

手册分册（全量字段与语义）：[脚本控制流 · gr-op-14](../mod-editor-prd/config/gr-op-14-control-flow.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/SubInt.json`）：

```json
{"id": "featured", "op": "SubInt"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "targetDay", "fromPort": "value", "to": "featured", "toPort": "a"}
{"from": "today", "fromPort": "value", "to": "featured", "toPort": "b"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/SubInt.json`，共 4 个节点。下列调用顺序可供编写自己的图时参考：

ConstInt → ReadCalendarDayIndex → **SubInt**（本篇） → HaltReturnInt

图跑完，字幕报出结果：

> 离第 360 天还差 {result} 天。

## 边界与更多用法

- 图种边界：可用于 Effect / Score / Validation / Derived / Script / TriggerGraph；Query 图不可用（编译期白名单拒绝）。
- 同类用法：跨帧等待（读条、喝药回满）、子图复用、循环收口。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_SubInt --adapter raylib
```
