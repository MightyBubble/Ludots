# gr-op-08 配置说明 · 节点：关系系统

> 配置写法与行为。第一性需求见 [gr-op-08 PRD](../prd/gr-op-08-relationship.md)；编辑器需求见 [UXD](../uxd/gr-op-08-relationship.md)；现状见 [reference](../reference/gr-op-08-relationship.md)。

## 1. 示例配置

节点画廊真实文件（`RelationshipEnsureLink.json` 建链；`RelationshipGetMetric.json` 读度量；`RelationshipQueryOutgoing.json` 出边建集）：

```json
[
  {
    "id": "showcase.graph_op.RelationshipEnsureLink",
    "kind": "Effect",
    "entry": "caster",
    "nodes": [
      { "id": "caster", "op": "LoadCaster" },
      { "id": "target", "op": "LoadExplicitTarget" },
      { "id": "ensure", "op": "RelationshipEnsureLink", "relationshipType": "SocialBond" }
    ],
    "controlEdges": [
      { "from": "caster", "fromPort": "next", "to": "target" },
      { "from": "target", "fromPort": "next", "to": "ensure" }
    ],
    "valueEdges": [
      { "from": "caster", "fromPort": "value", "to": "ensure", "toPort": "source" },
      { "from": "target", "fromPort": "value", "to": "ensure", "toPort": "target" }
    ]
  }
]
```

```json
{ "id": "get", "op": "RelationshipGetMetric", "relationshipType": "SocialBond", "metric": "Loyalty" }
```

```json
{ "id": "out", "op": "RelationshipQueryOutgoing", "relationshipType": "SocialBond" }
```

（后两行摘自同目录真实文件，source/target 引脚接线同第一例。）

## 2. 逐 op 表

kind 缩写同 gr-op-01。关系类型/度量/旗标符号均来自关系目录（rel-01）。dst=符号解析后的目的。

| op | 可用 kind | 输入引脚 | 输出 | 语义 |
|---|---|---|---|---|
| RelationshipEnsureLink | E | source target + 类型 | — | 建链（幂等） |
| RelationshipRemoveLink | E | source target + 类型 | — | 断链 |
| RelationshipSetMetric | E | source target value + 类型/度量 | — | 度量置值（reason 记账） |
| RelationshipAddMetric | E | source target value + 类型/度量 | — | 度量加值 |
| RelationshipSetFlag | E | source target value + 类型/旗标 | — | 旗标开关（reason 记账） |
| RelationshipGetMetric | L | source target + 类型/度量 | Int | 读度量 |
| RelationshipHasFlag | L+Q | source target + 类型/旗标 | Bool | 问旗标 |
| RelationshipHasLink | L+Q | source target + 类型 | Bool | 问链路 |
| RelationshipQueryOutgoing | Q | source + 类型 | TargetList | 出边邻居 |
| RelationshipQueryIncoming | Q | source + 类型 | TargetList | 入边邻居 |
| RelationshipQueryMutual | Q | source b + 类型 | TargetList | 双向都有链 |
| RelationshipQueryBetweenPair | Q | source b + 类型 | TargetList | 两点间的链目标集 |
| RelationshipFilterMetricRange | Q | list source min max + 类型/度量 | TargetList | 度量在闭区间 |
| RelationshipFilterFlag | Q | list source + 类型/旗标 | TargetList | 旗标开 |
| RelationshipSortByMetric | Q | list source + 类型/度量 + 降序旗标 | TargetList | 按度量排序 |
| RelationshipAggSumMetric | Q | list source + 类型/度量 | Int | 度量求和 |
| RelationshipAggMaxMetric / AggAverageMetric / AggMinMetric | Q | list source + 类型/度量 | Int | 度量最值/均值 |
| RelationshipAggMaxEntityByMetric / AggMinEntityByMetric | Q | list source + 类型/度量 | Entity | 最值度量对应实体 |

互斥与陷阱：

- **写侧三重门**：只进 Effect 图；且在效果组合编译（图折叠进效果执行计划）时按 Relationship 域 fail-closed——想把建链塞进效果模板的相位图是行不通的，必须留在显式 Effect 图。
- 度量聚合出 Int 不出 Float：关系度量是整数世界，与属性（Float）不同。
- SetMetric/AddMetric/SetFlag 带 reason 记账目的位（dst=reason）：语义是"为什么改"，外部观察可追溯。
- 管线 list+source 双输入：source 是判关系的基准实体，list 是被筛的集合——别把两者接反。

## 3. 文件结构

图文档放 `assets/GAS/graphs.json` 或分片目录；`relationshipType`/`metric`/旗标字段写目录符号，见 gr-04。关系类型/度量/旗标在 `assets/Relationships/catalog.json` 声明（rel-01）。

## 4. 运行时加载效果

关系目录先于图加载；图内符号编译期对目录解析。写侧编译为 Effect 事务指令；读侧与管线为只读查询。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 目录外符号 | 编译失败，指明节点与符号 |
| 效果组合折叠遇写侧 | 编译拒绝（Relationship 域 fail-closed） |
| kind 越界（管线入线性图、写侧入 Score 等） | 编译拒绝 |

## 6. 实例

- `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/RelationshipEnsureLink.json`
- 同目录 `RelationshipRemoveLink.json`、`RelationshipSetMetric.json`、`RelationshipAddMetric.json`、`RelationshipSetFlag.json`、`RelationshipGetMetric.json`、`RelationshipHasFlag.json`、`RelationshipHasLink.json`、`RelationshipQueryOutgoing.json` 等全族 21 件

**相关文档**：[gr-op-08 PRD](../prd/gr-op-08-relationship.md) · [gr-op-07 配置说明](gr-op-07-entityset.md) · [rel-01 配置说明](rel-01-catalog.md)
