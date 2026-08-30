# 看效果叠了几层

自查线绕回施法者身上的层数，头顶浮出 ×3。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Effect / Query / Derived / Script / Score / Validation / TriggerGraph（与 `LoadEffectTiming` 同族） |
| 返回 | Float（层数；无 `EffectStack` 组件时为 1） |
| 输入端口（值边 toPort） | 无（读 caster / 元素 scope） |
| 特殊写法 | — |

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/LoadEffectStack.json`）：

```json
{"id": "stacks", "op": "LoadEffectStack"}
```

面板开箱芯片用法见 [面板开箱布局套件](../../architecture/panel-author-layout-kit.md)。

## 这场是怎么搭出来的

作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/LoadEffectStack.json` 只有一颗节点。图跑完，字幕报出结果：

> 这条效果叠了 {count} 层。

## 边界与更多用法

- 无 `EffectStack` 组件时读为 `1`（单层生效），不静默成 0。
- 与 `LoadEffectTiming` 正交：时间走 ticks，层数走 stack。
- 面板侧用 pin + `label`/`badge` 展示，禁止在面板 JSON 里手算层数。

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_LoadEffectStack --adapter raylib
```
