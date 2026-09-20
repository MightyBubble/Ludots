# 看自己还剩多少血

自查线绕回施法者自己，头顶浮出 62；木桩满血没人碰。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_LoadSelfAttribute/poster.png" src="artifacts/evidence/capability_standard_graph_op_LoadSelfAttribute/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 artifacts/evidence/capability_standard_graph_op_LoadSelfAttribute/play.mp4。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 七种全可用（Effect / Score / Validation / Derived / Query / Script / TriggerGraph） |
| 返回 | Float → 小数寄存器 |
| 输入端口（值边 toPort） | 无（不收值边，靠 imm/自身上下文） |
| 特殊写法 | 结果写入 dst 寄存器；imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[属性与效果 · gr-op-04](../mod-editor-prd/config/gr-op-04-attributes.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/LoadSelfAttribute.json`）：

```json
{"id": "selfHp", "op": "LoadSelfAttribute", "attribute": "Health"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/LoadSelfAttribute.json`，共 1 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

**LoadSelfAttribute**（本篇）

图跑完，字幕报出结果：

> 读自己的生命，还剩 {hp} 点。

## 边界与更多用法

- 图种边界：七种图全都能用，不必为它挑图种。
- 不接值边：输入来自 imm 与运行时上下文（施法者、显式目标等）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：按属性读写与直写、层数叠加引爆、先查对方状态再决定出手。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_LoadSelfAttribute --adapter raylib
```
