# 账本里的步骤逐条办

造出新身体，再把新身体的效果挂架扫净。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_InvokeBuiltin/poster.png" src="artifacts/evidence/capability_standard_graph_op_InvokeBuiltin/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_InvokeBuiltin/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 Effect |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | 无（不收值边，靠 imm/自身上下文） |
| 特殊写法 | imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[属性与效果 · gr-op-04](../mod-editor-prd/config/gr-op-04-attributes.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/InvokeBuiltin.json`）：

```json
{"id": "materialize", "op": "InvokeBuiltin", "builtinHandler": "MaterializeTemplate"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/InvokeBuiltin.json`，共 3 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

BeginLifecycleTransaction → **InvokeBuiltin**（本篇） → **InvokeBuiltin**（本篇）

图跑完，字幕报出结果：

> 新身体已上场，效果挂架已清空。

## 边界与更多用法

- 图种边界：可用于 Effect；Score / Validation / Derived / Query / Script 图不可用（编译期白名单拒绝）。
- 不接值边：输入来自 imm 与运行时上下文（施法者、显式目标等）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：按属性读写与直写、层数叠加引爆、先查对方状态再决定出手。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_InvokeBuiltin --adapter raylib
```
