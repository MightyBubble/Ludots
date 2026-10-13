# 小地图上的点换成地面

屏幕点按小地图投到地面厘米，投不中得到假。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Effect / Script / TriggerGraph |
| 返回 | Bool → 布尔槽 |
| 输入端口（值边 toPort） | `a`（第一操作数）、`b`（第二操作数） |
| 特殊写法 | 结果写入 dst 寄存器 |

手册分册（全量字段与语义）：[算术与比较 · gr-op-02](../mod-editor-prd/config/gr-op-02-math.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/MinimapScreenToWorld.json`）：

```json
{"id": "project", "op": "MinimapScreenToWorld"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "x", "fromPort": "value", "to": "project", "toPort": "a"}
{"from": "y", "fromPort": "value", "to": "project", "toPort": "b"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/MinimapScreenToWorld.json`，共 6 个节点。下列调用顺序可供编写自己的图时参考：

ConstFloat → ConstFloat → **MinimapScreenToWorld**（本篇） → ConstFloat → LoadExplicitTarget → ModifyAttributeAdd

图跑完，字幕报出结果：

> 投过了，木桩从{healthBefore}掉到{healthAfter}。

## 边界与更多用法

- 图种边界：可用于 Effect / Script / TriggerGraph；Score / Validation / Derived / Query 图不可用（编译期白名单拒绝）。
- 同类用法：伤害公式的缩放与浮动、斩杀线/格挡线这类阈值判断、把读数换算成另一个数。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_MinimapScreenToWorld --adapter raylib
```
