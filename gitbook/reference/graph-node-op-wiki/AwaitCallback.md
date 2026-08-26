# 等回话再往下走

图停在门口等确认；回话一到，下一拍接着演。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_AwaitCallback/poster.png" src="artifacts/evidence/capability_standard_graph_op_AwaitCallback/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 artifacts/evidence/capability_standard_graph_op_AwaitCallback/play.mp4。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Script / TriggerGraph |
| 返回 | Bool（确认结果写入 Dst） |
| 输入端口（值边 toPort） | 无（不收值边，靠 imm/自身上下文） |
| 特殊写法 | `callbackType` 填具名回调符号（装载期解析） |

手册分册（全量字段与语义）：[脚本控制流 · gr-op-14](../mod-editor-prd/config/gr-op-14-control-flow.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AwaitCallback.json`）：

```json
{"id": "askConfirm", "op": "AwaitCallback", "callbackType": "DialogConfirm"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AwaitCallback.json`，共 6 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

**AwaitCallback**（本篇） → JumpIfFalse → ConstInt → HaltReturnInt（同意） / ConstInt → HaltReturnInt（拒绝）

图跑完，字幕报出结果：

> 确认了：{confirmed}。回话次数 {replies}，回程结果 {result}。

## 边界与更多用法

- 图种边界：可用于 Script / TriggerGraph；Effect / Score / Validation / Derived / Query 图不可用（编译期白名单拒绝）。
- 挂起后由 C# 宿主 `Complete` 句柄；Continuation 相位按注册序 resume，不按完成线程顺序。
- 嵌套 `InvokeScript` / `InvokeGraph` 仍禁止 Yield / AwaitCallback（不做 yield-through）。
- 失效句柄、双次完成、死宿主全部失败关闭。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_AwaitCallback --adapter raylib
```
