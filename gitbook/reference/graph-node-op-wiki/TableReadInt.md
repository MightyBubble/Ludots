# 读名册上的星数

点到 2 号那行，册上记着三颗星，照数挂印。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_TableReadInt/poster.png" src="artifacts/evidence/capability_standard_graph_op_TableReadInt/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_TableReadInt/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 七种全可用（Effect / Score / Validation / Derived / Query / Script / TriggerGraph） |
| 返回 | Int → 整数寄存器 |
| 输入端口（值边 toPort） | `a`（第一操作数） |
| 特殊写法 | 结果写入 dst 寄存器；imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[算术与比较 · gr-op-02](../mod-editor-prd/config/gr-op-02-math.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/TableReadInt.json`）：

```json
{"id": "stars", "op": "TableReadInt", "lookupTable": "showcase.graph_op.rank_display", "lookupField": "rankStars"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "row", "fromPort": "value", "to": "stars", "toPort": "a"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/TableReadInt.json`，共 6 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ConstInt → ResolveTableRow → **TableReadInt**（本篇） → TableReadFloat → LoadExplicitTarget → ModifyAttributeAdd

图跑完，字幕报出结果：

> 名册这行攒了 {result} 颗星，木桩血量从 {healthBefore} 掉到 {healthAfter}。

## 边界与更多用法

- 图种边界：七种图全都能用，不必为它挑图种。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：伤害公式的缩放与浮动、斩杀线/格挡线这类阈值判断、把读数换算成另一个数。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_TableReadInt --adapter raylib
```
