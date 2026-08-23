# 先对脸：打的是不是自己

残影演示点名自己→同一个人，收手；点名木桩→不是同一人，一刀扣 18。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_CompareEqEntity/poster.png" src="artifacts/evidence/capability_standard_graph_op_CompareEqEntity/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_CompareEqEntity/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Effect / Score / Validation / Derived / Query |
| 返回 | Bool → 布尔槽 |
| 输入端口（值边 toPort） | `a`（第一操作数）、`b`（第二操作数） |
| 特殊写法 | 结果写入 dst 寄存器 |

手册分册（全量字段与语义）：[属性与效果 · gr-op-04](../mod-editor-prd/config/gr-op-04-attributes.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/CompareEqEntity.json`）：

```json
{"id": "notSelf", "op": "CompareEqEntity"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "caster", "fromPort": "value", "to": "notSelf", "toPort": "a"}
{"from": "explicit", "fromPort": "value", "to": "notSelf", "toPort": "b"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/CompareEqEntity.json`，共 6 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

LoadCaster → LoadExplicitTarget → **CompareEqEntity**（本篇） → JumpIfFalse → ConstFloat → ApplyEffectTemplate

图跑完，字幕报出结果：

> 木桩不是施法者本人，这一刀打了出去，木桩从 {healthBefore} 掉到 {healthAfter}。

## 边界与更多用法

- 图种边界：可用于 Effect / Score / Validation / Derived / Query；Script 图不可用（编译期白名单拒绝）。
- 同类用法：按属性读写与直写、层数叠加引爆、先查对方状态再决定出手。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_CompareEqEntity --adapter raylib
```
