# 算出一个整数就收工

数落进托盘、卷轴拉下打烊条、人挪到答案旁边——这三件事同时发生，就是收工。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_HaltReturnInt/poster.png" src="artifacts/evidence/capability_standard_graph_op_HaltReturnInt/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_HaltReturnInt/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 七种全可用（Effect / Score / Validation / Derived / Query / Script / TriggerGraph） |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `value`（数值） |
| 特殊写法 | — |

手册分册（全量字段与语义）：[脚本控制流 · gr-op-14](../mod-editor-prd/config/gr-op-14-control-flow.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/HaltReturnInt.json`）：

```json
{"id": "halt", "op": "HaltReturnInt"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "seven", "fromPort": "value", "to": "halt", "toPort": "value"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/HaltReturnInt.json`，共 2 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ConstInt → **HaltReturnInt**（本篇）

图跑完，字幕报出结果：

> 算完了，托盘里是 {result} 枚，卷轴打烊，人站在答案旁。

## 边界与更多用法

- 图种边界：七种图全都能用，不必为它挑图种。
- 同类用法：跨帧等待（读条、喝药回满）、子图复用、循环收口。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_HaltReturnInt --adapter raylib
```
