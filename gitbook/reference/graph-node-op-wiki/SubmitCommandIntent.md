# 一声令下先进缓冲

图里定好落点，一声令下交给缓冲，下令内核下一拍再路由。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 TriggerGraph |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `target`（目标实体）、`condition`（条件） |
| 特殊写法 | — |

手册分册（全量字段与语义）：[命令意图 · input-01](../mod-editor-prd/config/input-01-command-intent.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/SubmitCommandIntent.json`）：

```json
{"id": "submit", "op": "SubmitCommandIntent"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "to_ground", "fromPort": "value", "to": "submit", "toPort": "condition"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/SubmitCommandIntent.json`，共 7 个节点。下列调用顺序可供编写自己的图时参考：

ConstFloat → ConstFloat → ScreenPointToGround → JumpIfFalse → **SubmitCommandIntent**（本篇） → ConstInt → HaltReturnInt

图跑完，字幕报出结果：

> 令下进缓冲，{count} 条意图待路由。

## 边界与更多用法

- 图种边界：可用于 TriggerGraph；Effect / Score / Validation / Derived / Query / Script 图不可用（编译期白名单拒绝）。
- 同类用法：见手册分册的场景节。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_SubmitCommandIntent --adapter raylib
```
