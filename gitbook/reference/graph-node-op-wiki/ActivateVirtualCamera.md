# 图里切镜头

开场是俯瞰全场的远景，图点名一台近景机位，镜头拉近到指挥和木桩身边。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 TriggerGraph |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | 无（不收值边，靠 imm/自身上下文） |
| 特殊写法 | imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[视野与相机 · infra-03](../mod-editor-prd/config/infra-03-vision-camera.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ActivateVirtualCamera.json`）：

```json
{"id": "cut", "op": "ActivateVirtualCamera", "camera": "Camera.GraphOps.CloseUp"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ActivateVirtualCamera.json`，共 3 个节点。下列调用顺序可供编写自己的图时参考：

**ActivateVirtualCamera**（本篇） → ConstInt → HaltReturnInt

图跑完，字幕报出结果：

> 镜头已切到「{camera}」。

## 边界与更多用法

- 图种边界：可用于 TriggerGraph；Effect / Score / Validation / Derived / Query / Script 图不可用（编译期白名单拒绝）。
- 不接值边：输入来自 imm 与运行时上下文（施法者、显式目标等）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：切视角模式（战术、跟随、观察）、进入某个交互状态时换机位；跟随集合的机位跟的是跑这张图的玩家自己的集合。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_ActivateVirtualCamera --adapter raylib
```
