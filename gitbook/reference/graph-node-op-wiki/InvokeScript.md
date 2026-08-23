# 叫另一张图来帮忙算

主卷轴上叫一声外援，旁边那张小卷轴亮起来，算完把 7 送回来。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_InvokeScript/poster.png" src="artifacts/evidence/capability_standard_graph_op_InvokeScript/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_InvokeScript/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 七种全可用（Effect / Score / Validation / Derived / Query / Script / TriggerGraph） |
| 返回 | Int → 整数寄存器 |
| 输入端口（值边 toPort） | 无（不收值边，靠 imm/自身上下文） |
| 特殊写法 | 结果写入 dst 寄存器；imm 填符号名（编译期解析）；flags 填函数库名 |

手册分册（全量字段与语义）：[脚本控制流 · gr-op-14](../mod-editor-prd/config/gr-op-14-control-flow.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/InvokeScript.json`）：

```json
{"id": "invoke", "op": "InvokeScript", "functionName": "demo.const.seven"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/InvokeScript.json`，共 2 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

**InvokeScript**（本篇） → HaltReturnInt

图跑完，字幕报出结果：

> 叫了另一张图帮忙，送回来的数是 {result}。

## 边界与更多用法

- 图种边界：七种图全都能用，不必为它挑图种。
- 不接值边：输入来自 imm 与运行时上下文（施法者、显式目标等）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：跨帧等待（读条、喝药回满）、子图复用、循环收口。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_InvokeScript --adapter raylib
```
