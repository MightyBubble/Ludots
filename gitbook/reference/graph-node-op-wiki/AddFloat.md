# 两段伤害叠成一刀

30 的一段先摆上，12 的一段接在尾巴上，接成的一整段有多长，木桩就掉多少血。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_AddFloat/poster.png" src="artifacts/evidence/capability_standard_graph_op_AddFloat/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_AddFloat/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Effect / Score / Validation / Derived |
| 返回 | Float → 小数寄存器 |
| 输入端口（值边 toPort） | `a`（第一操作数）、`b`（第二操作数） |
| 特殊写法 | 结果写入 dst 寄存器 |

手册分册（全量字段与语义）：[算术与比较 · gr-op-02](../mod-editor-prd/config/gr-op-02-math.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AddFloat.json`）：

```json
{"id": "sum", "op": "AddFloat"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "base", "fromPort": "value", "to": "sum", "toPort": "a"}
{"from": "bonus", "fromPort": "value", "to": "sum", "toPort": "b"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AddFloat.json`，共 6 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ConstFloat → ConstFloat → **AddFloat**（本篇） → LoadExplicitTarget → NegFloat → ModifyAttributeAdd

图跑完，字幕报出结果：

> 接起来的一刀是 {result}；木桩血条从 {healthBefore} 掉到 {healthAfter}。

## 边界与更多用法

- 图种边界：可用于 Effect / Score / Validation / Derived；Query / Script / TriggerGraph 图不可用（编译期白名单拒绝）。
- 同类用法：伤害公式的缩放与浮动、斩杀线/格挡线这类阈值判断、把读数换算成另一个数。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_AddFloat --adapter raylib
```
