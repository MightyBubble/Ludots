# 先存参数，再点子图

整数参数放进暂存表，子图按名字取走，回执就是同一数字。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 TriggerGraph |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `value`（数值） |
| 特殊写法 | imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[地图触发器 · map-02](../mod-editor-prd/config/map-02-triggers.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/StoreArgInt.json`）：

```json
{"id": "store", "op": "StoreArgInt", "argKey": "GraphOps.Stage"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "stage", "fromPort": "value", "to": "store", "toPort": "value"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/StoreArgInt.json`，共 4 个节点。下列调用顺序可供编写自己的图时参考：

ConstInt → **StoreArgInt**（本篇） → InvokeGraph → HaltReturnInt

图跑完，字幕报出结果：

> 暂存整数交给子图，回执 {result} 分毫不差。

## 边界与更多用法

- 图种边界：可用于 TriggerGraph；Effect / Score / Validation / Derived / Query / Script 图不可用（编译期白名单拒绝）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：见手册分册的场景节。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_StoreArgInt --adapter raylib
```
