# 隔空落子

不用挪动命令，一枚棋子从图里被放到了指定点。

运行时证据由画廊验收测试提供；该页面不引用未提交的录像资产。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 七种全可用（Effect / Score / Validation / Derived / Query / Script / TriggerGraph） |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `source`（来源实体）、`a`（第一操作数）、`b`（第二操作数） |
| 特殊写法 | — |

手册分册（全量字段与语义）：[算术与比较 · gr-op-02](../mod-editor-prd/config/gr-op-02-math.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/SetWorldPosition.json`）：

```json
{"id": "move", "op": "SetWorldPosition"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "explicit", "fromPort": "value", "to": "move", "toPort": "source"}
{"from": "px", "fromPort": "value", "to": "move", "toPort": "a"}
{"from": "py", "fromPort": "value", "to": "move", "toPort": "b"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/SetWorldPosition.json`，共 6 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ConstFloat → ConstInt → ConstInt → **SetWorldPosition**（本篇） → LoadExplicitTarget → ModifyAttributeAdd

图跑完，字幕报出结果：

> 图内改位落地——木桩血量{healthBefore}→{healthAfter}。

## 边界与更多用法

- 图种边界：七种图全都能用，不必为它挑图种。
- 同类用法：伤害公式的缩放与浮动、斩杀线/格挡线这类阈值判断、把读数换算成另一个数。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_SetWorldPosition --adapter raylib
```
