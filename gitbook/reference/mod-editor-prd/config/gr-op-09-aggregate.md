# gr-op-09 配置说明 · 节点：聚合与迭代

> 配置写法与行为。第一性需求见 [gr-op-09 PRD](../prd/gr-op-09-aggregate.md)；编辑器需求见 [UXD](../uxd/gr-op-09-aggregate.md)；现状见 [reference](../reference/gr-op-09-aggregate.md)。

## 1. 示例配置

节点画廊真实文件（`TargetListGet.json` 摘要：锥查询→排除施法者→按下标取第 0 个）：

```json
{ "id": "cone", "op": "QueryCone", "queryCapacityPolicy": "RequireComplete", "rangeCm": 800 },
{ "id": "self", "op": "LoadCaster" },
{ "id": "notSelf", "op": "QueryFilterNotEntity" },
{ "id": "zero", "op": "ConstInt", "intValue": 0 },
{ "id": "get0", "op": "TargetListGet" }
```

值线：`zero.value → get0.value`（下标），`self.value → notSelf.source`；控制线依序串联。`AggMinByDistance.json` 同链把尾部换成 `AggMinByDistance`。

## 2. 逐 op 表

kind 缩写同 gr-op-01。

| op | 可用 kind | 输入引脚 | 输出 | 语义 |
|---|---|---|---|---|
| AggCount | L+Q+SC | list | Int | 列表元素数，空表 0 |
| AggMinByDistance | L+Q+SC | list | Entity | 距击落点最近的实体，空表无效句柄 |
| TargetListGet | L+SC | value Int（下标） | Entity + 有效位 | E[Dst]=列表[下标]；越界出无效句柄且有效位=0 |

互斥与陷阱：

- AggMinByDistance 的距离基准是**击落点 TargetPos**（LoadTargetPosX/Y 读的同一个点），不是施法者位置——"挑离落点最近"的图要拿捏基准。
- TargetListGet 越界不报错：下游必须消费有效位（flags）或对实体判空，静默使用会拿到无效句柄。
- TargetListGet 不进 Query 图（L+SC）；Query 图取首元素用 gr-op-07 的 AggMinEntityByAttribute 或排序聚合替代。

## 3. 文件结构

图文档放 `assets/GAS/graphs.json` 或分片目录；本族无符号字段，见 gr-04。

## 4. 运行时加载效果

编译期校验 list/下标引脚类型；执行期单遍扫描，零分配。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 引脚类型不符 | 编译失败 |
| 下标越界 | 不报错：无效句柄 + 有效位 0 |
| 空表挑最近 | 不报错：无效句柄 |

## 6. 实例

- `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/AggCount.json`
- 同目录 `AggMinByDistance.json`、`TargetListGet.json`

**相关文档**：[gr-op-09 PRD](../prd/gr-op-09-aggregate.md) · [gr-op-06 配置说明](gr-op-06-spatial.md) · [gr-op-07 配置说明](gr-op-07-entityset.md)
