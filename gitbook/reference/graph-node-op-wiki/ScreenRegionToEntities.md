# 框一圈点名

虚线框罩住西边一段，框里的人被点名线逐个牵住。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Query / TriggerGraph |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `list`（目标名单）、`a`（第一操作数）、`b`（第二操作数）、`c`（第三操作数）、`max`（上限）；可选 `tolerance`（float，点击宽容像素——仅零位移矩形生效，缺省 0） |
| 特殊写法 | flags 填第四操作数寄存器编号 |

手册分册（全量字段与语义）：[空间圈人 · gr-op-06](../mod-editor-prd/config/gr-op-06-spatial.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ScreenRegionToEntities.json`）：

```json
{"id": "region", "op": "ScreenRegionToEntities"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "minX", "fromPort": "value", "to": "region", "toPort": "a"}
{"from": "minY", "fromPort": "value", "to": "region", "toPort": "b"}
{"from": "maxX", "fromPort": "value", "to": "region", "toPort": "c"}
{"from": "maxY", "fromPort": "value", "to": "region", "toPort": "max"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ScreenRegionToEntities.json`，共 5 个节点。下列调用顺序可供编写自己的图时参考：

ConstFloat → ConstFloat → ConstFloat → ConstFloat → **ScreenRegionToEntities**（本篇）

图跑完，字幕报出结果：

> 框里圈住{count}人，框外的退成灰影。

## 边界与更多用法

- 图种边界：可用于 Query / TriggerGraph；Effect / Score / Validation / Derived / Script 图不可用（编译期白名单拒绝）。
- 同类用法：见手册分册的场景节。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_ScreenRegionToEntities --adapter raylib
```
