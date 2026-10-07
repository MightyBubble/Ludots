# 替自己下移动令

行为图不等玩家发话，直接往订单队列里塞一道移动令。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Script / TriggerGraph |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `target`（目标实体）、`a`（第一操作数）、`b`（第二操作数） |
| 特殊写法 | imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[脚本控制流 · gr-op-14](../mod-editor-prd/config/gr-op-14-control-flow.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/SubmitAssignedOrder.json`）：

```json
{"id": "submit", "op": "SubmitAssignedOrder", "orderType": "moveTo"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "aim", "fromPort": "value", "to": "submit", "toPort": "target"}
{"from": "px", "fromPort": "value", "to": "submit", "toPort": "a"}
{"from": "py", "fromPort": "value", "to": "submit", "toPort": "b"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/SubmitAssignedOrder.json`，共 6 个节点。下列调用顺序可供编写自己的图时参考：

LoadCaster → LoadExplicitTarget → ConstInt → ConstInt → **SubmitAssignedOrder**（本篇） → HaltReturnInt

图跑完，字幕报出结果：

> 队列里进了一道移动令，去 ({x}, {y}) 厘米；本波共 {count} 道。

## 边界与更多用法

- 图种边界：可用于 Script / TriggerGraph；Effect / Score / Validation / Derived / Query 图不可用（编译期白名单拒绝）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 目标口可以不接。不接时这道令没有实体目标，落点仍由 a、b 给出。
- 同类用法：见手册分册的场景节。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_SubmitAssignedOrder --adapter raylib
```
