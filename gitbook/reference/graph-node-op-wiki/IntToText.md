# 把整数念成字

数字 7 先变成文字，再送进字幕口。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_IntToText/poster.png" src="artifacts/evidence/capability_standard_graph_op_IntToText/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 artifacts/evidence/capability_standard_graph_op_IntToText/play.mp4。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Script / TriggerGraph |
| 返回 | Text → 固定容量文字槽 |
| 输入端口（值边 toPort） | `a`（第一操作数） |
| 特殊写法 | 结果写入 dst 寄存器 |

手册分册（全量字段与语义）：[脚本控制流 · gr-op-14](../mod-editor-prd/config/gr-op-14-control-flow.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/IntToText.json`）：

```json
{"id": "featured", "op": "IntToText"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "num", "fromPort": "value", "to": "featured", "toPort": "a"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/IntToText.json`，共 4 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ConstInt → **IntToText**（本篇） → SinkPresentationText → HaltReturnInt

图跑完，字幕报出结果：

> 文字出口已收到拼好的句子。

## 边界与更多用法

- 图种边界：可用于 Script / TriggerGraph；Effect / Score / Validation / Derived / Query 图不可用（编译期白名单拒绝）。
- 同类用法：跨帧等待（读条、喝药回满）、子图复用、循环收口。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_IntToText --adapter raylib
```
