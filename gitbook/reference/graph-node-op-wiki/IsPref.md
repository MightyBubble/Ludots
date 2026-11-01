# 问这个偏好是不是这个值

图拿代表记下的偏好，对槽位上那条技能。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 七种全可用（Effect / Score / Validation / Derived / Query / Script / TriggerGraph） |
| 返回 | Bool → 布尔槽 |
| 输入端口（值边 toPort） | `source`（来源实体）、`target`（目标实体）、`value`（数值） |
| 特殊写法 | 结果写入 dst 寄存器；imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[算术与比较 · gr-op-02](../mod-editor-prd/config/gr-op-02-math.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/IsPref.json`）：

```json
{"id": "check", "op": "IsPref", "key": "pref.gallery.sample", "value": "interaction.context.gallery.pref"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "explicit", "fromPort": "value", "to": "check", "toPort": "source"}
{"from": "explicit", "fromPort": "value", "to": "check", "toPort": "target"}
{"from": "slot", "fromPort": "value", "to": "check", "toPort": "value"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/IsPref.json`，共 5 个节点。下列调用顺序可供编写自己的图时参考：

ConstFloat → ConstInt → **IsPref**（本篇） → LoadExplicitTarget → ModifyAttributeAdd

图跑完，字幕报出结果：

> 对上了（{result}），木桩从{healthBefore}掉到{healthAfter}。

## 边界与更多用法

- 图种边界：七种图全都能用，不必为它挑图种。
- `value` 小于 0 时读玩家级偏好，不查槽位技能。大于等于 0 时那个槽必须有技能；技能上没有这对偏好，再读玩家级。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：伤害公式的缩放与浮动、斩杀线/格挡线这类阈值判断、把读数换算成另一个数。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_IsPref --adapter raylib
```
