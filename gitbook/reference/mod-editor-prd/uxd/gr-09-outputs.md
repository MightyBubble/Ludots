# gr-09 UXD · Query 图输出的编辑器需求

> gr-09 的编辑器需求（高保真规格）。第一性需求见 ；配置写法见 ；编辑器实现见 ；上限数值以  为准。

## 1. 界面定位

输出面板：Query 图的"图之外"半边——声明产物、预览落点、看槽位存量。

## 2. 布局线框

```text
┌─ 输出面板：Graph.Query.NearbyEnemies ───────────────────────────────┐
│ ＋新增输出                                                          │
│ ▸ nearby  EntityCollection · TargetList  key[combat.nearby]  [预览] │
│ ▸ count   Summary · Int                 key[combat.nearby.count]   │
├─ 详情：source [targets ▾] · role · title · summary ────────────────┤
│ 槽位存量 [·/graphOutputValueCapacity·见事实页]  [刷新] [运行预览]   │
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 输出清单 | 文档 outputs 数组 | 按 destination 分组徽标 |
| 类型/去向选择 | 五类型 × 两去向封闭集 | 非法组合（Summary+TargetList）即时禁选 |
| source 选择 | 图内节点输出类型表 | 只列类型匹配的节点 |
| key/collectionKey | 键名建议器 | EntityCollection 必填提示 |
| 槽位存量 | 输出值存储统计 vs 事实页容量 | 预警 |

## 4. 关键交互流：声明一个实体集合输出

1. 输出面板"新增" → destination 选 EntityCollection（type 锁定 TargetList）。
2. source 选目标列表节点 → 填 collectionKey（键名建议）。
3. 保存 → 编译 schema 校验零诊断 → 预览运行看落点集合。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 无输出 | outputs 为空 | 提示 Query 图通常至少一个产物 |
| 非法组合 | Summary+TargetList 等 | 选择器即时禁用并说明 |
| 槽位高水位 | 存量接近容量 | 预警色 |
| 物化失败 | owner/caster 空等运行错误 | 运行预览红条 |

## 6. 易用性验收口径

- 非法组合在界面上不可构造。
- 每个输出从声明到落点预览 ≤ 2 跳。

**相关文档**：[gr-09 PRD](../prd/gr-09-outputs.md) · [editor spec](../spec-editor/gr-09-outputs.md) · [gr-08 UXD](gr-08-mount-points.md)
