# 摇杆掰方向

摇杆斜掰，箭头随之指向东北。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Query / TriggerGraph |
| 返回 | Float → 小数寄存器 |
| 输入端口（值边 toPort） | `a`（第一操作数）、`b`（第二操作数） |
| 特殊写法 | 结果写入 dst 寄存器；flags 填布尔暂存位编号 |

手册分册（全量字段与语义）：[空间圈人 · gr-op-06](../mod-editor-prd/config/gr-op-06-spatial.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/StickToDirection.json`）：

```json
{"id": "stick", "op": "StickToDirection", "validOutput": "hasDir"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "x", "fromPort": "value", "to": "stick", "toPort": "a"}
{"from": "y", "fromPort": "value", "to": "stick", "toPort": "b"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/StickToDirection.json`，共 3 个节点。下列调用顺序可供编写自己的图时参考：

ConstFloat → ConstFloat → **StickToDirection**（本篇）

图跑完，字幕报出结果：

> 摇杆掰向 {deg} 度。

## 边界与更多用法

- 图种边界：可用于 Query / TriggerGraph；Effect / Score / Validation / Derived / Script 图不可用（编译期白名单拒绝）。
- 同类用法：见手册分册的场景节。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_StickToDirection --adapter raylib
```
