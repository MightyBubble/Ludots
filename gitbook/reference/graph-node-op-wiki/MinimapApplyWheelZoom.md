# 小地图滚轮缩放

滚轮增量写到小地图缩放。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Effect / Script / TriggerGraph |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `a`（第一操作数）、`b`（第二操作数）、`c`（第三操作数） |
| 特殊写法 | — |

手册分册（全量字段与语义）：[算术与比较 · gr-op-02](../mod-editor-prd/config/gr-op-02-math.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/MinimapApplyWheelZoom.json`）：

```json
{"id": "wheel", "op": "MinimapApplyWheelZoom"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "delta", "fromPort": "value", "to": "wheel", "toPort": "a"}
{"from": "x", "fromPort": "value", "to": "wheel", "toPort": "b"}
{"from": "y", "fromPort": "value", "to": "wheel", "toPort": "c"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/MinimapApplyWheelZoom.json`，共 7 个节点。下列调用顺序可供编写自己的图时参考：

ConstFloat → ConstFloat → ConstFloat → **MinimapApplyWheelZoom**（本篇） → ConstFloat → LoadExplicitTarget → ModifyAttributeAdd

图跑完，字幕报出结果：

> 缩过了，木桩从{healthBefore}掉到{healthAfter}。

## 边界与更多用法

- 图种边界：可用于 Effect / Script / TriggerGraph；Score / Validation / Derived / Query 图不可用（编译期白名单拒绝）。
- 同类用法：伤害公式的缩放与浮动、斩杀线/格挡线这类阈值判断、把读数换算成另一个数。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_MinimapApplyWheelZoom --adapter raylib
```
