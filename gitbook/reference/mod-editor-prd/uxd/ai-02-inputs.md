# ai-04 UXD · 效用输入的编辑器需求

> ai-03 的编辑器需求（高保真规格）。第一性需求见 [ai-03 PRD](../prd/ai-02-inputs.md)；配置写法见 [ai-03 配置说明](../config/ai-02-inputs.md)；编辑器实现见 [editor spec](../spec-editor/ai-02-inputs.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

输入面板是效用 AI 的"感官库"：八种感知读法在此定义一次、处处引用。

## 2. 布局线框

```text
┌─ 输入面板 ──────────────────────────────────────────────────────────┐
├─ 左：输入清单 ─────────┬─ 右：输入详情 ─────────────────────────────┤
│ ▸ Distance        距离 │ Kind      [GraphScore ▾]                   │
│ ▸ TargetHealth    图   │ GraphKey  [Graph.UtilityAutocast.TargetHealth▾] │
│ ▸ In.Ex.Const     常量 │ ▸ 图预览（只读徽标：Score · 无写op）        │
│ ＋新建输入             │ 被引用：决策.Curse 考量[0] · 决策.Heal …     │
├─ 底部：Kind 图例 [8 种] ─────────────────────────────────────────────┤
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| Kind 下拉 | 8 种 Kind 枚举 | 切换后表单字段随 Kind 重建 |
| Value/DefaultPriority/ActuatorId | 数字框 | Constant 整数提示（I1 红条：小数走 GraphScore） |
| GraphKey 选择器 | 图注册表中 RequireKind=Score 的图 | 只列 Score 图；选中即跑写 op 黑名单预检 |
| Tag 选择器 | tag 注册表 | 双 Kind 复用 |
| AbilityKey 选择器 | AbilityDefinitionRegistry | 只列已注册技能 |
| 被引用索引 | decisions 考量扫描 | 点击跳决策 |

## 4. 关键交互流：给决策加"目标血量"考量输入

1. 输入面板 → 新建输入，Kind 选 GraphScore。
2. GraphKey 选择器挑 `Graph.UtilityAutocast.TargetHealth`；黑名单预检通过显示绿标。
3. 保存；在决策面板（ai-05）的考量表单里引用该输入 id。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 图断链 | GraphKey 指向的图不存在 | 红条 + 图选择器置空 |
| 写 op 图 | 选中图含黑名单 op | 红条"感知图禁写" |
| 无引用输入 | 零个考量引用 | 灰字"未使用"，可安全删除 |
| 小数常量 | Constant 输入非整数 | 即时提示走 GraphScore（I1） |

## 6. 易用性验收口径

- 八种 Kind 的合法参数集在表单层收窄（非法组合无法保存）。
- 任一输入"定义+全部使用处"≤ 2 跳可达。
- 图/tag/技能引用只从下拉选，不手打名字。

**相关文档**：[ai-03 PRD](../prd/ai-02-inputs.md) · [editor spec](../spec-editor/ai-02-inputs.md)
