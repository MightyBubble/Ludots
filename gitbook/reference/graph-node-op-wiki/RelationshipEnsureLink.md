# 把两人连成一条关系链

灰色虚线先比划一下，然后咔哒扣成青色实线。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_RelationshipEnsureLink/poster.png" src="artifacts/evidence/capability_standard_graph_op_RelationshipEnsureLink/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_RelationshipEnsureLink/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 Effect |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `source`（来源实体）、`target`（目标实体） |
| 特殊写法 | dst 填符号名（编译期解析） |

手册分册（全量字段与语义）：[图文档写法 · gr-02](../mod-editor-prd/config/gr-02-document.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/RelationshipEnsureLink.json`）：

```json
{"id": "ensure", "op": "RelationshipEnsureLink", "relationshipType": "SocialBond"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "caster", "fromPort": "value", "to": "ensure", "toPort": "source"}
{"from": "target", "fromPort": "value", "to": "ensure", "toPort": "target"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/RelationshipEnsureLink.json`，共 3 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

LoadCaster → LoadExplicitTarget → **RelationshipEnsureLink**（本篇）

图跑完，字幕报出结果：

> 施法者和盟友之间扣上了关系链，环扣亮起。

## 边界与更多用法

- 图种边界：可用于 Effect；Score / Validation / Derived / Query / Script 图不可用（编译期白名单拒绝）。
- 同类用法：多节点串成完整小玩法的组合示范，可整段抄走改。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_RelationshipEnsureLink --adapter raylib
```
