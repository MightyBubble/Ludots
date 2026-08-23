# 先翻出一张效果牌，再照着打

施法者从抽屉翻出一张牌，木桩照着牌掉了一截血。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_ApplyEffectDynamic/poster.png" src="artifacts/evidence/capability_standard_graph_op_ApplyEffectDynamic/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_ApplyEffectDynamic/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 Effect |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `target`（目标实体）、`value`（数值） |
| 特殊写法 | — |

手册分册（全量字段与语义）：[图文档写法 · gr-02](../mod-editor-prd/config/gr-02-document.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ApplyEffectDynamic.json`）：

```json
{"id": "applyDyn", "op": "ApplyEffectDynamic"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "target", "fromPort": "value", "to": "applyDyn", "toPort": "target"}
{"from": "buffId", "fromPort": "value", "to": "applyDyn", "toPort": "value"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ApplyEffectDynamic.json`，共 4 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

LoadCaster → LoadExplicitTarget → ReadBlackboardInt → **ApplyEffectDynamic**（本篇）

图跑完，字幕报出结果：

> 黑板里读到的这张牌是打击，照牌把木桩打掉一截血。

## 边界与更多用法

- 图种边界：可用于 Effect；Score / Validation / Derived / Query / Script 图不可用（编译期白名单拒绝）。
- 同类用法：多节点串成完整小玩法的组合示范，可整段抄走改。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_ApplyEffectDynamic --adapter raylib
```
