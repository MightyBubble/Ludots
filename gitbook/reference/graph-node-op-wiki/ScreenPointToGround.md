# 把光标钉到地上

光标点下的地方，落点圈在地图上亮起。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_ScreenPointToGround/poster.png" src="artifacts/evidence/capability_standard_graph_op_ScreenPointToGround/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 artifacts/evidence/capability_standard_graph_op_ScreenPointToGround/play.mp4。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 Query |
| 返回 | Bool → 布尔槽 |
| 输入端口（值边 toPort） | `a`（第一操作数）、`b`（第二操作数） |
| 特殊写法 | 结果写入 dst 寄存器 |

手册分册（全量字段与语义）：[空间圈人 · gr-op-06](../mod-editor-prd/config/gr-op-06-spatial.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ScreenPointToGround.json`）：

```json
{"id": "ground", "op": "ScreenPointToGround"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "sx", "fromPort": "value", "to": "ground", "toPort": "a"}
{"from": "sy", "fromPort": "value", "to": "ground", "toPort": "b"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ScreenPointToGround.json`，共 3 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ConstFloat → ConstFloat → **ScreenPointToGround**（本篇）

图跑完，字幕报出结果：

> 落点钉在东 {x} 米、北 {y} 米。

## 边界与更多用法

- 图种边界：可用于 Query；Effect / Score / Validation / Derived / Script / TriggerGraph 图不可用（编译期白名单拒绝）。
- 同类用法：见手册分册的场景节。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_ScreenPointToGround --adapter raylib
```
