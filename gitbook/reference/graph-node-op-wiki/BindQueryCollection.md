# 换队后，名单跟着变

左侧士兵换队，点名圈随之摘下或补回。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Script / TriggerGraph |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `source`（来源实体） |
| 特殊写法 | imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[名单筛选与汇总 · gr-op-07](../mod-editor-prd/config/gr-op-07-entityset.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/BindQueryCollection.json`）：

```json
{"id": "bind", "op": "BindQueryCollection", "functionName": "showcase.graph_op.QueryFilterTeam", "collectionKey": "showcase.graph_op.snap"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "owner", "fromPort": "value", "to": "bind", "toPort": "source"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/BindQueryCollection.json`，共 3 个节点。下列调用顺序可供编写自己的图时参考：

LoadCaster → **BindQueryCollection**（本篇） → HaltReturnInt

图跑完，字幕报出结果：

> 当前敌军{count}人，换队者的点名圈已更新。

## 边界与更多用法

- 图种边界：可用于 Script / TriggerGraph；Effect / Score / Validation / Derived / Query 图不可用（编译期白名单拒绝）。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：见手册分册的场景节。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_BindQueryCollection --adapter raylib
```
