# 点名即派发：待办活动应声上桌

军需官每点名一次，案头就添一件待办活动；件件单层拍板，绝不嵌套。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_OfferActivity/poster.png" src="artifacts/evidence/capability_standard_graph_op_OfferActivity/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 artifacts/evidence/capability_standard_graph_op_OfferActivity/play.mp4。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 TriggerGraph |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `source`（来源实体） |
| 特殊写法 | imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[地图触发器 · map-02](../mod-editor-prd/config/map-02-triggers.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/OfferActivity.json`）：

```json
{"id": "offer", "op": "OfferActivity", "activityId": "gallery.op.offer_activity"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "load_council", "fromPort": "value", "to": "offer", "toPort": "source"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/OfferActivity.json`，共 7 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

LoadPlacedEntity → **OfferActivity**（本篇） → ReadMapVarInt → ConstInt → AddInt → WriteMapVarInt → HaltReturnInt

图跑完，字幕报出结果：

> 点名册翻开，待办活动已上桌（点名 {count} 次）。

## 边界与更多用法

- 图种边界：可用于 TriggerGraph；Effect / Score / Validation / Derived / Query / Script 图不可用（编译期白名单拒绝）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：地图事件发生后把一次拍板摆到玩家面前：补给超限、过境商队、归属通报这类 CK3 弹层的调度入口。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_OfferActivity --adapter raylib
```
