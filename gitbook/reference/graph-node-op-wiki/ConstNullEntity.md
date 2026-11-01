# 直接写下空实体

图自己落下空句柄，不再拿越界下标去读名单。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 七种全可用（Effect / Score / Validation / Derived / Query / Script / TriggerGraph） |
| 返回 | Entity → 实体寄存器 |
| 输入端口（值边 toPort） | 无（不收值边，靠 imm/自身上下文） |
| 特殊写法 | 结果写入 dst 寄存器 |

手册分册（全量字段与语义）：[属性与效果 · gr-op-04](../mod-editor-prd/config/gr-op-04-attributes.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ConstNullEntity.json`）：

```json
{"id": "nullEntity", "op": "ConstNullEntity"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ConstNullEntity.json`，共 1 个节点。下列调用顺序可供编写自己的图时参考：

**ConstNullEntity**（本篇）

图跑完，字幕报出结果：

> 落下去的是空实体。

## 边界与更多用法

- 图种边界：七种图全都能用，不必为它挑图种。
- 不接值边：输入来自 imm 与运行时上下文（施法者、显式目标等）。
- 同类用法：按属性读写与直写、层数叠加引爆、先查对方状态再决定出手。

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_ConstNullEntity --adapter raylib
```
