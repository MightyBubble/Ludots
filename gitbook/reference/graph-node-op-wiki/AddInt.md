# 连击数加一

两刀打进算式台 2+1，翻牌翻出 3，木桩头顶亮三枚连击火花。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_AddInt/poster.png" src="artifacts/evidence/capability_standard_graph_op_AddInt/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_AddInt/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Effect / Score / Validation / Derived / Script |
| 返回 | Int → 整数寄存器 |
| 输入端口（值边 toPort） | `a`（第一操作数）、`b`（第二操作数） |
| 特殊写法 | 结果写入 dst 寄存器 |

手册分册（全量字段与语义）：[属性与效果 · gr-op-04](../mod-editor-prd/config/gr-op-04-attributes.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AddInt.json`）：

```json
{"id": "comboAdd", "op": "AddInt"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "comboNow", "fromPort": "value", "to": "comboAdd", "toPort": "a"}
{"from": "plusOne", "fromPort": "value", "to": "comboAdd", "toPort": "b"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AddInt.json`，共 3 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ConstInt → ConstInt → **AddInt**（本篇）

图跑完，字幕报出结果：

> 连击 2 加 1，算出 {combo}。

## 边界与更多用法

- 图种边界：可用于 Effect / Score / Validation / Derived / Script；Query 图不可用（编译期白名单拒绝）。
- 同类用法：按属性读写与直写、层数叠加引爆、先查对方状态再决定出手。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_AddInt --adapter raylib
```
