# gr-op-07 UXD · 节点：实体集查询的编辑器需求

> gr-op-07 的编辑器需求（高保真规格）。第一性需求见 [gr-op-07 PRD](../prd/gr-op-07-entityset.md)；配置写法见 [gr-op-07 配置说明](../config/gr-op-07-entityset.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-07-entityset.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

Query 图的筛子架：建集、过滤、排序、聚合四段子骨架，编辑器用"链式推荐"把作者从左到右带完。

## 2. 布局线框

```text
┌─ 节点面板 · 分组：实体集查询（仅 Query 图显示）───────────────────┐
│ ▸ 建集  QueryAllMapEntities / QueryFromCollection                │
│ ▸ 过滤  FilterTeam / FilterTemplate / FilterAttributeRange /     │
│         FilterTagAny / FilterTagNone                             │
│ ▸ 排序  QuerySortByAttribute ▾降序开关                            │
│ ▸ 聚合  AggSum/Average/Max/MinAttribute · Max/MinEntityBy…      │
├─ 链式推荐条 ─────────────────────────────────────────────────────┤
│ [建集] → [过滤] → [排序] → [聚合/输出]   当前链：全图→队伍2→…      │
└──────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 集合键选择器 | 集合键注册表投影 | 只列已登记集合键 |
| 模板选择器 | 实体模板注册表（ent-01） | 搜索 + 分组 |
| 属性选择器 | 属性注册表投影（上限见事实页） | 数值型属性 |
| tag 选择器 | tag 注册表投影 + 通用用户表 | Any/None 两种列表形态 |
| 降序开关 | 排序旗标 | 开=降序写图 |
| 链式推荐 | 描述符表 TargetList 连通性 | list 出引脚悬空时推下一段候选 |

## 4. 关键交互流：筛出敌方法师的最脆目标

1. Query 图拖 QueryAllMapEntities 建集。
2. `list` 悬空点补全选过滤段：QueryFilterTeam 设 teamId=2。
3. 再接 QueryFilterTemplate 选法师模板；接 QuerySortByAttribute 选 Health 开降序。
4. 链尾接 AggMinEntityByAttribute 出最脆实体到 outputs（gr-09）；链式推荐条全程显示当前链。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 非 Query 图 | 当前图 kind 不是 Query | 整组不显示（而非置灰），面板提示去 gr-op-06 |
| 空列表聚合 | 上游可能为空 | 聚合卡标"空集语义"说明 |
| 并列最值 | Max/MinEntityBy 出现实体 | 卡片注"并列取序前" |

## 6. 易用性验收口径

- 四段链从建集到聚合 ≤ 6 步且全程有补全推荐。
- 属性/tag/模板选择器输入到选中 ≤ 3 步。

**相关文档**：[gr-op-07 PRD](../prd/gr-op-07-entityset.md) · [editor spec](../spec-editor/gr-op-07-entityset.md)
