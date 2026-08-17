# ai-08 UXD · 目标过滤器的编辑器需求

> ai-07 的编辑器需求（高保真规格）。第一性需求见 [ai-07 PRD](../prd/ai-06-target-filters.md)；配置写法见 [ai-07 配置说明](../config/ai-06-target-filters.md)；编辑器实现见 [editor spec](../spec-editor/ai-06-target-filters.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

过滤器面板是取景框工位：左边排 op 链，右边看战场实况里"到底谁被留下、谁被淘汰"。

## 2. 布局线框

```text
┌─ 目标过滤器面板 ──────────────────────────────────────────────────────┐
├─ 左：过滤器清单 ─────┬─ 右：op 链编辑 + 试验台 ───────────────────────┤
│ ▸ Hostile   敌 16    │ TF.UtilityAutocast.Hostile  MaxResults [16]   │
│ ▸ Friendly  友 16    │ op 链：                                        │
│ ＋新建过滤器         │  1 SpatialRadius  RadiusCm [1600]              │
│                      │  2 Relationship   Value   [Hostile ▾]          │
│                      │  ＋加 op（九选一面板）                          │
│                      │ 试验台：地图投影（选中实体跑此链）              │
│                      │  ● 留 5 · ✕ 淘汰 3（半径 1/关系 2）             │
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 过滤器清单 | target_filters 合并视图 | 徽标：关系倾向、MaxResults |
| op 链 | 可排序列表 | 拖动改变判定顺序（AND 顺序影响淘汰码先后） |
| op 参数表单 | 按 Kind 动态：正数字框/关系下拉/tag 多选/掩码/技能选择 | 非法值即时拦 |
| 关系下拉 | RelationshipFilter 枚举 | Hostile/Friendly 等全值 |
| Tags 多选 | tag 注册表 | HasAllTags/HasNoneTags 共用 |
| 试验台 | 地图实体 + 本地重放判定链 | 逐实体输出 留/淘汰+原因码 |
| 淘汰原因 | UtilityAiFilterRejectReason | 与运行时同码表 |

## 4. 关键交互流：调半径直到战场命中合理

1. 选中 TF.Hostile，试验台框选一片战场实体。
2. RadiusCm 从 1600 下调，观察"留 5/淘汰 3"实时变化与原因分布。
3. 确认后保存；被引用决策（ai-05）即时反映候选集变化。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 非法参数 | 正数字段 ≤0 | 红框禁存 |
| 技能/tag 断链 | 引用不存在 | 下拉红框 |
| 空链 | Ops 无条目 | 禁止保存（必填） |
| HasAllTags 说明 | 编辑该 op | 灰字"优先桶加权暂未接线（I4）" |

## 6. 易用性验收口径

- 九种 op 无需查文档即可配对参数。
- 试验台淘汰原因与运行时 trace 拒绝码一致。
- op 顺序调整对判定结果的影响在试验台可见。

**相关文档**：[ai-07 PRD](../prd/ai-06-target-filters.md) · [editor spec](../spec-editor/ai-06-target-filters.md)
