# 可选筛只留能点的

名单过一道可选门：带可选标记且开关打开的留下，关掉的当场划掉，顺序不变。

本页演示录像尚未录制，可用下方启动命令运行场景。

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Query / TriggerGraph |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | 无（直接筛当前名单） |
| 特殊写法 | — |

手册分册（全量字段与语义）：[名单筛选与汇总 · gr-op-07](../mod-editor-prd/config/gr-op-07-entityset.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QueryFilterSelectable.json`）：

```json
{"id": "selectable_filter", "op": "QueryFilterSelectable"}
```

接线（值边把上一步的结果送进本节点端口）：

本节点不需要值边输入：它直接筛当前 TargetList。上一步的连线（`QueryAllMapEntities` → `selectable_filter` 的控制边）就是全部接线；图 JSON 仍需声明空的 `valueEdges: []`。

## 这场是怎么搭出来的

这场演示使用画廊里的作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QueryFilterSelectable.json`，共 3 个节点。下列调用顺序可供编写自己的图时参考：

LoadCaster → QueryAllMapEntities → **QueryFilterSelectable**（本篇）

图跑完，字幕报出结果：

> 可选筛后名单留 {count} 名。

## 边界与更多用法

- 图种边界：可用于 Query / TriggerGraph；Effect / Score / Validation / Derived / Script 图不可用（编译期白名单拒绝）。
- 合同来源：实体须带 `CommandSourceSelectableTag`，且 `CommandSourceSelectableState`（存在时）为启用；共享选中链 `graph.core.select_hit` 用它挡掉演示用不可选实体。
- 同类用法：候选名单预处理、点选/框选前剔除装饰单位。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_QueryFilterSelectable --adapter raylib
```
