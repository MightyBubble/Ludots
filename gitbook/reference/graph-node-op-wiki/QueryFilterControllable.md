# 只圈你能指挥的

满场单位里，被实线指挥的才留下圈，其余退成灰影。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Script / TriggerGraph / Query |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `list`（目标名单）、`source`（来源实体） |
| 特殊写法 | — |

手册分册（全量字段与语义）：[名单筛选与汇总 · gr-op-07](../mod-editor-prd/config/gr-op-07-entityset.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QueryFilterControllable.json`）：

```json
{"id": "filter", "op": "QueryFilterControllable"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "all", "fromPort": "list", "to": "filter", "toPort": "list"}
{"from": "self", "fromPort": "value", "to": "filter", "toPort": "source"}
```

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QueryFilterControllable.json`，共 3 个节点。下列调用顺序可供编写自己的图时参考：

LoadCaster → QueryAllMapEntities → **QueryFilterControllable**（本篇）

图跑完，字幕报出结果：

> 场上可控单位 {count} 个，其余退成灰影。

## 边界与更多用法

- 图种边界：可用于 Query / Script / TriggerGraph；Effect / Score / Validation / Derived 图不可用（编译期白名单拒绝）。
- 同类用法：战场统计（全场均值/最值）、点名最残或最能扛的目标、按条件筛名单再排序。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_QueryFilterControllable --adapter raylib
```
