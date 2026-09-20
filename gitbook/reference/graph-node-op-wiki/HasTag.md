# 查一查身上有没有那枚标记

带标记的侦察兵亮绿圈，没标记的那个查完没反应。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_HasTag/poster.png" src="artifacts/evidence/capability_standard_graph_op_HasTag/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 artifacts/evidence/capability_standard_graph_op_HasTag/play.mp4。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 七种全可用（Effect / Score / Validation / Derived / Query / Script / TriggerGraph） |
| 返回 | Bool → 布尔槽 |
| 输入端口（值边 toPort） | `source`（来源实体） |
| 特殊写法 | 结果写入 dst 寄存器；imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[图文档写法 · gr-02](../mod-editor-prd/config/gr-02-document.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/HasTag.json`）：

```json
{"id": "hasEnemy", "op": "HasTag", "tag": "State.Sandbox.Marked"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "scout", "fromPort": "value", "to": "hasEnemy", "toPort": "source"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/HasTag.json`，共 2 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

LoadExplicitTarget → **HasTag**（本篇）

图跑完，字幕报出结果：

> 带标记的查为「{result}」，没标记的查为「无」。

## 边界与更多用法

- 图种边界：七种图全都能用，不必为它挑图种。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：多节点串成完整小玩法的组合示范，可整段抄走改。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_HasTag --adapter raylib
```
