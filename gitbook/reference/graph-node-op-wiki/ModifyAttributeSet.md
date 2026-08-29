# 图内把血量写成 42

面板按钮触发 TriggerGraph，木桩当前生命直接写成 42，属性变化仍从 GAS 正式入口落账。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_ModifyAttributeSet/poster.png" src="artifacts/evidence/capability_standard_graph_op_ModifyAttributeSet/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 artifacts/evidence/capability_standard_graph_op_ModifyAttributeSet/play.mp4。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Effect / TriggerGraph |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `target`（目标实体）、`value`（数值） |
| 特殊写法 | imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[属性与效果 · gr-op-04](../mod-editor-prd/config/gr-op-04-attributes.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ModifyAttributeSet.json`）：

```json
{"id": "write", "op": "ModifyAttributeSet", "attribute": "Health"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "value", "fromPort": "value", "to": "write", "toPort": "value"}
{"from": "target", "fromPort": "value", "to": "write", "toPort": "target"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ModifyAttributeSet.json`，共 4 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ConstFloat → LoadExplicitTarget → **ModifyAttributeSet**（本篇） → HaltReturnInt

图跑完，字幕报出结果：

> TriggerGraph 写入落地——木桩生命{healthBefore}→{healthAfter}。

## 边界与更多用法

- 图种边界：可用于 Effect / TriggerGraph；Score / Validation / Derived / Query / Script 图不可用（编译期白名单拒绝）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：按属性读写与直写、层数叠加引爆、先查对方状态再决定出手。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_ModifyAttributeSet --adapter raylib
```
