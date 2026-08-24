# 这条关系上插没插信任旗

青色链上插着绿旗，旗子闪两下；没链的那位啥也没有。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_RelationshipHasFlag/poster.png" src="artifacts/evidence/capability_standard_graph_op_RelationshipHasFlag/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_RelationshipHasFlag/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Effect / Score / Validation / Derived / Query |
| 返回 | Bool → 布尔槽 |
| 输入端口（值边 toPort） | `source`（来源实体）、`target`（目标实体） |
| 特殊写法 | 结果写入 dst 寄存器；imm 填符号名（编译期解析）；flags 填关系类型 |

手册分册（全量字段与语义）：[图文档写法 · gr-02](../mod-editor-prd/config/gr-02-document.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/RelationshipHasFlag.json`）：

```json
{"id": "hasFlag", "op": "RelationshipHasFlag", "relationshipType": "SocialBond", "flag": "Trusted"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "caster", "fromPort": "value", "to": "hasFlag", "toPort": "source"}
{"from": "target", "fromPort": "value", "to": "hasFlag", "toPort": "target"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/RelationshipHasFlag.json`，共 6 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

LoadCaster → LoadExplicitTarget → ConstBool → RelationshipEnsureLink → RelationshipSetFlag → **RelationshipHasFlag**（本篇）

图跑完，字幕报出结果：

> 信任旗插着，检查结果：{result}。

## 边界与更多用法

- 图种边界：可用于 Effect / Score / Validation / Derived / Query；Script / TriggerGraph 图不可用（编译期白名单拒绝）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：多节点串成完整小玩法的组合示范，可整段抄走改。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_RelationshipHasFlag --adapter raylib
```
