# 永远放行的许可

门闩每一拍都开着，亮一个绿点放一刀，一排刻记里从来没有红点。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_ConstBool/poster.png" src="artifacts/evidence/capability_standard_graph_op_ConstBool/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 artifacts/evidence/capability_standard_graph_op_ConstBool/play.mp4。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Effect / Score / Validation / Derived |
| 返回 | Bool → 布尔槽 |
| 输入端口（值边 toPort） | 无（不收值边，靠 imm/自身上下文） |
| 特殊写法 | 结果写入 dst 寄存器 |

手册分册（全量字段与语义）：[算术与比较 · gr-op-02](../mod-editor-prd/config/gr-op-02-math.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ConstBool.json`）：

```json
{"id": "permit", "op": "ConstBool", "boolValue": true}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ConstBool.json`，共 7 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

**ConstBool**（本篇） → JumpIfFalse → LoadExplicitTarget → ConstFloat → NegFloat → ModifyAttributeAdd → ConstFloat

图跑完，字幕报出结果：

> 这一拍的许可：{result}；放行的刀落下，木桩血条从 {healthBefore} 掉到 {healthAfter}。

## 边界与更多用法

- 图种边界：可用于 Effect / Score / Validation / Derived；Query / Script / TriggerGraph 图不可用（编译期白名单拒绝）。
- 不接值边：输入来自 imm 与运行时上下文（施法者、显式目标等）。
- 同类用法：伤害公式的缩放与浮动、斩杀线/格挡线这类阈值判断、把读数换算成另一个数。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_ConstBool --adapter raylib
```
