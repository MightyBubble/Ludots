# 血量过线没：过线轻击，没过线全力

木桩 50 血低于 80 刻线，标尺闪红，全力一击扣 18，掉到 32。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_CompareLtInt/poster.png" src="artifacts/evidence/capability_standard_graph_op_CompareLtInt/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_CompareLtInt/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Effect / Score / Validation / Derived / Script / TriggerGraph |
| 返回 | Bool → 布尔槽 |
| 输入端口（值边 toPort） | `a`（第一操作数）、`b`（第二操作数） |
| 特殊写法 | 结果写入 dst 寄存器 |

手册分册（全量字段与语义）：[属性与效果 · gr-op-04](../mod-editor-prd/config/gr-op-04-attributes.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/CompareLtInt.json`）：

```json
{"id": "canFull", "op": "CompareLtInt"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "healthNow", "fromPort": "value", "to": "canFull", "toPort": "a"}
{"from": "fullLine", "fromPort": "value", "to": "canFull", "toPort": "b"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/CompareLtInt.json`，共 10 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

LoadExplicitTarget → ReadBlackboardInt → ConstInt → **CompareLtInt**（本篇） → JumpIfFalse → ConstFloat → ModifyAttributeAdd → ConstFloat → ModifyAttributeAdd → ConstFloat

图跑完，字幕报出结果：

> 木桩 {healthBefore} 低于 80，打{style}，掉到 {healthAfter}。

## 边界与更多用法

- 图种边界：可用于 Effect / Score / Validation / Derived / Script / TriggerGraph；Query 图不可用（编译期白名单拒绝）。
- 同类用法：按属性读写与直写、层数叠加引爆、先查对方状态再决定出手。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_CompareLtInt --adapter raylib
```
