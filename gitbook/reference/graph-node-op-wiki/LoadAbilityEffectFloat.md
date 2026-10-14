# 读出这一槽点名的效果数值

图按槽位找到技能，再按这张图在 effectFields 里点的名字，读冲击效果上的那个数。效果上没有这个数时，effectFields 也可以直接写正数；缺了或不是正数，读的时候失败。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 七种全可用（Effect / Score / Validation / Derived / Query / Script / TriggerGraph） |
| 返回 | Float → 小数寄存器 |
| 输入端口（值边 toPort） | `source`（来源实体）、`value`（数值） |
| 特殊写法 | 结果写入 dst 寄存器 |

手册分册（全量字段与语义）：[算术与比较 · gr-op-02](../mod-editor-prd/config/gr-op-02-math.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/LoadAbilityEffectFloat.json`）：

```json
{"id": "radius", "op": "LoadAbilityEffectFloat"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "explicit", "fromPort": "value", "to": "radius", "toPort": "source"}
{"from": "slot", "fromPort": "value", "to": "radius", "toPort": "value"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/LoadAbilityEffectFloat.json`，共 5 个节点。下列调用顺序可供编写自己的图时参考：

ConstInt → **LoadAbilityEffectFloat**（本篇） → ConstFloat → LoadExplicitTarget → ModifyAttributeAdd

图跑完，字幕报出结果：

> 这一槽的效果读数是{result}，木桩从{healthBefore}掉到{healthAfter}。

## 边界与更多用法

- 图种边界：七种图全都能用，不必为它挑图种。
- 同类用法：伤害公式的缩放与浮动、斩杀线/格挡线这类阈值判断、把读数换算成另一个数。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_LoadAbilityEffectFloat --adapter raylib
```
