# 调成两倍再收回

给整局一张两倍令牌，马上放回。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Script / TriggerGraph |
| 返回 | Int → 整数寄存器 |
| 输入端口（值边 toPort） | `value`（数值） |
| 特殊写法 | 结果写入 dst 寄存器；imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[脚本控制流 · gr-op-14](../mod-editor-prd/config/gr-op-14-control-flow.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AcquireTimeFlowScale.json`）：

```json
{"id": "featured", "op": "AcquireTimeFlowScale", "domain": "simulation"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "two", "fromPort": "value", "to": "featured", "toPort": "value"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AcquireTimeFlowScale.json`，共 5 个节点。下列调用顺序可供编写自己的图时参考：

ConstInt → **AcquireTimeFlowScale**（本篇） → ReadTimeFlowScalePermille → ReleaseTimeFlowToken → HaltReturnInt

图跑完，字幕报出结果：

> 这一拍读到 {result}。

## 边界与更多用法

- 图种边界：可用于 Script / TriggerGraph；Effect / Score / Validation / Derived / Query 图不可用（编译期白名单拒绝）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：跨帧等待（读条、喝药回满）、子图复用、循环收口。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_AcquireTimeFlowScale --adapter raylib
```
