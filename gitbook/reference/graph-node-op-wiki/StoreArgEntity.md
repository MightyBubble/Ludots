# 实体参数递过去，子图亲自搬人

把木桩实体暂存给子图，子图按参数把它搬到新位置。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_StoreArgEntity/poster.png" src="artifacts/evidence/capability_standard_graph_op_StoreArgEntity/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_StoreArgEntity/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 TriggerGraph |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `value`（数值） |
| 特殊写法 | imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[地图触发器 · map-02](../mod-editor-prd/config/map-02-triggers.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/StoreArgEntity.json`）：

```json
{"id": "store", "op": "StoreArgEntity", "argKey": "GraphOps.Who"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "loadWho", "fromPort": "value", "to": "store", "toPort": "value"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/StoreArgEntity.json`，共 4 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

LoadExplicitTarget → **StoreArgEntity**（本篇） → InvokeGraph → HaltReturnInt

图跑完，字幕报出结果：

> 实体交给子图，木桩被搬到 ({x}, {y})。

## 边界与更多用法

- 图种边界：可用于 TriggerGraph；Effect / Score / Validation / Derived / Query / Script 图不可用（编译期白名单拒绝）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：见手册分册的场景节。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_StoreArgEntity --adapter raylib
```
