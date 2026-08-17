# gr-op-08 UXD · 节点：关系系统的编辑器需求

> gr-op-08 的编辑器需求（高保真规格）。第一性需求见 [gr-op-08 PRD](../prd/gr-op-08-relationship.md)；配置写法见 [gr-op-08 配置说明](../config/gr-op-08-relationship.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-08-relationship.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

关系目录的图内消费面：写/读/管线三段分组；类型-度量-旗标三级联动选择器是核心控件。

## 2. 布局线框

```text
┌─ 节点面板 · 分组：关系系统 ──────────────────────────────────────┐
│ ▸ 写（Effect）  EnsureLink / RemoveLink / SetMetric /            │
│                 AddMetric / SetFlag                              │
│ ▸ 读            GetMetric / HasFlag / HasLink                    │
│ ▸ Query 管线    Outgoing / Incoming / Mutual / BetweenPair /     │
│                 Filter×2 / Sort / Agg×7                          │
├─ 节点卡细节 ─────────────────────────────────────────────────────┤
│ ┌ RelationshipGetMetric ─────────────┐                          │
│ │ 类型 [SocialBond ▾] 度量 [Loyalty ▾]│                          │
│ │ source ●  target ●────────── ● Int │                          │
│ └────────────────────────────────────┘                          │
└──────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 类型选择器 | 关系目录类型清单（rel-01） | 选型后度量/旗标候选随之收敛 |
| 度量选择器 | 目录内该类型的度量 | 只列 Int 度量 |
| 旗标选择器 | 目录内该类型的旗标 | 布尔语义 |
| 写侧警示 | fail-closed 域标记 | Effect 图外置灰；模板折叠视图内标"组合门" |
| reason 提示 | dst=reason 标记 | 写侧卡显示"记账 reason"说明 |

## 4. 关键交互流：给 AI 图筛信任盟友

1. Query 图拖 RelationshipQueryOutgoing，类型选 `SocialBond`，source 接 LoadContextSource。
2. list 接 RelationshipFilterFlag，旗标选 `Trusted`。
3. 再接 RelationshipAggAverageMetric 看均值，或 SortByMetric 降序取首位。
4. 类型-度量联动：换类型时度量选择器自动清空重列，不残留旧符号。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 组合门拦截 | 效果模板折叠含写侧节点 | 红条"关系域 fail-closed" |
| 符号不在目录 | 类型/度量手输错名 | 红字 + 目录链接 |
| 度量误当 Float | 聚合结果接 Float 引脚 | 连线弹回（Int 线） |

## 6. 易用性验收口径

- 类型→度量两级选择 ≤ 3 步且联动正确。
- 写侧节点在任何非 Effect 图从目录到画布均不可落下。

**相关文档**：[gr-op-08 PRD](../prd/gr-op-08-relationship.md) · [editor spec](../spec-editor/gr-op-08-relationship.md)
