# 回到地图机位

开场已经站在地图写好的远景上，图不点名机位，直接把镜头按这张地图的默认机位和姿态摆回去。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 TriggerGraph |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | 无（不收值边，靠地图默认机位） |
| 特殊写法 | 不写 `camera`，也不填 imm |

手册分册（全量字段与语义）：[视野与相机 · infra-03](../mod-editor-prd/config/infra-03-vision-camera.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ApplyMapDefaultCamera.json`）：

```json
{"id": "reset", "op": "ApplyMapDefaultCamera"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ApplyMapDefaultCamera.json`，共 3 个节点。下列调用顺序可供编写自己的图时参考：

**ApplyMapDefaultCamera**（本篇） → ConstInt → HaltReturnInt

图跑完，字幕报出结果：

> 镜头回到地图默认机位「{camera}」。

## 边界与更多用法

- 图种边界：可用于 TriggerGraph；Effect / Score / Validation / Derived / Query / Script 图不可用（编译期白名单拒绝）。
- 不接值边，也不点名机位。机位 id 和姿态来自当前地图的 `DefaultCamera`。
- 混合时长固定为 0。预设要跟随集合时，跟的是跑这张图的施法者身上的那个集合。
- 地图没写 `DefaultCamera.VirtualCameraId` 时报 `MapDefaultCameraMissing`。点名的预设不在 `Camera/virtual_cameras.json` 里时报 `VirtualCameraUnknown`。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_ApplyMapDefaultCamera --adapter raylib
```
