# 把要盯的人记上板

从木桩身上揭张画像，贴进点名格。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_WriteBlackboardEntity/poster.png" src="artifacts/evidence/capability_standard_graph_op_WriteBlackboardEntity/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_WriteBlackboardEntity/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 Effect |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `source`（来源实体）、`value`（数值） |
| 特殊写法 | imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[黑板与配置 · gr-op-05](../mod-editor-prd/config/gr-op-05-blackboard.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/WriteBlackboardEntity.json`）：

```json
{"id": "writeE", "op": "WriteBlackboardEntity", "blackboardKey": "showcase.bb.named"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "src", "fromPort": "value", "to": "writeE", "toPort": "source"}
{"from": "tgt", "fromPort": "value", "to": "writeE", "toPort": "value"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/WriteBlackboardEntity.json`，共 3 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

LoadContextSource → LoadExplicitTarget → **WriteBlackboardEntity**（本篇）

图跑完，字幕报出结果：

> 点名格贴上了{named}。

## 边界与更多用法

- 图种边界：可用于 Effect；Score / Validation / Derived / Query / Script / TriggerGraph 图不可用（编译期白名单拒绝）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：跨节点跨图传值、决策记忆（记住要盯的人）、按名册配置出招。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_WriteBlackboardEntity --adapter raylib
```
