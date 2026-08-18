# 把血直接写成 90

施法者血 60，一道写入线落下，血条直接抬到 90，头顶浮出 =90。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_WriteSelfAttribute/poster.png" src="artifacts/evidence/capability_standard_graph_op_WriteSelfAttribute/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_WriteSelfAttribute/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Effect / Derived |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `value`（数值） |
| 特殊写法 | imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[属性与效果 · gr-op-04](../mod-editor-prd/config/gr-op-04-attributes.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/WriteSelfAttribute.json`）：

```json
{"id": "healSelf", "op": "WriteSelfAttribute", "attribute": "Health"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "heal", "fromPort": "value", "to": "healSelf", "toPort": "value"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/WriteSelfAttribute.json`，共 2 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ConstFloat → **WriteSelfAttribute**（本篇）

图跑完，字幕报出结果：

> 直接写入生命值，从 {casterBefore} 写成 {casterAfter}。

## 边界与更多用法

- 图种边界：可用于 Effect / Derived；Score / Validation / Query / Script 图不可用（编译期白名单拒绝）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：按属性读写与直写、层数叠加引爆、先查对方状态再决定出手。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_WriteSelfAttribute --adapter raylib
```
