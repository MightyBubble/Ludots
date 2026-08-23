# 观众名下有记录才看得见

木桩有记录、亮着；陌生人没记录，连血条都不显示。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_KnowledgeHasProjection/poster.png" src="artifacts/evidence/capability_standard_graph_op_KnowledgeHasProjection/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_KnowledgeHasProjection/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Effect / Score / Validation / Derived |
| 返回 | Bool → 布尔槽 |
| 输入端口（值边 toPort） | `a`（第一操作数）、`b`（第二操作数） |
| 特殊写法 | 结果写入 dst 寄存器 |

手册分册（全量字段与语义）：[事件与情境 · gr-op-01](../mod-editor-prd/config/gr-op-01-context.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/KnowledgeHasProjection.json`）：

```json
{"id": "knows", "op": "KnowledgeHasProjection"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "viewer", "fromPort": "value", "to": "knows", "toPort": "a"}
{"from": "target", "fromPort": "value", "to": "knows", "toPort": "b"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/KnowledgeHasProjection.json`，共 3 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

LoadViewer → LoadExplicitTarget → **KnowledgeHasProjection**（本篇）

图跑完，字幕报出结果：

> 观众对{result}。

## 边界与更多用法

- 图种边界：可用于 Effect / Score / Validation / Derived；Query / Script 图不可用（编译期白名单拒绝）。
- 同类用法：受击联动（挨打触发计数或外观变化）、事件决定施放哪张效果牌、与观看者相关的表现逻辑。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_KnowledgeHasProjection --adapter raylib
```
