# 问这一槽接不接受这种目标

图按槽位读技能声明的目标种类。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 七种全可用（Effect / Score / Validation / Derived / Query / Script / TriggerGraph） |
| 返回 | Bool → 布尔槽 |
| 输入端口（值边 toPort） | `source`（来源实体）、`value`（槽位整数） |
| 特殊写法 | 结果写入 dst 寄存器；`symbol` 填 `unit`、`ground` 或独占的 `none`（编译期解析） |

手册分册（全量字段与语义）：[算术与比较 · gr-op-02](../mod-editor-prd/config/gr-op-02-math.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AbilityAcceptsTargetKind.json`）：

```json
{"id": "check", "op": "AbilityAcceptsTargetKind", "symbol": "unit"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "explicit", "fromPort": "value", "to": "check", "toPort": "source"}
{"from": "slot", "fromPort": "value", "to": "check", "toPort": "value"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AbilityAcceptsTargetKind.json`，共 5 个节点。下列调用顺序可供编写自己的图时参考：

ConstFloat → ConstInt → **AbilityAcceptsTargetKind**（本篇） → LoadExplicitTarget → ModifyAttributeAdd

图跑完，字幕报出结果：

> 这一槽接受单位（{result}），木桩从{healthBefore}掉到{healthAfter}。

## 边界与更多用法

- 图种边界：七种图全都能用，不必为它挑图种。
- `symbol` 只接受 `unit`、`ground` 或 `none`。`none` 独占：技能声明里不能和 `unit` 或 `ground` 写在一起，加载直接失败。技能没写 `targeting.targetKinds`、槽位是空的、或种类名不认识，节点直接失败，不会当成“都收”。`castRangeCm` 为 0 也不会自动变成 `none`。
- 同类用法：伤害公式的缩放与浮动、斩杀线/格挡线这类阈值判断、把读数换算成另一个数。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_AbilityAcceptsTargetKind --adapter raylib
```
