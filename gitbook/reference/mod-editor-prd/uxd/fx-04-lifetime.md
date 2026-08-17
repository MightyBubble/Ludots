# fx-07 UXD · 生命周期与时长的编辑器需求

> 生命周期与时长的编辑器需求（高保真规格）。第一性需求见 [fx-06 PRD](../prd/fx-04-lifetime.md)；配置写法见 [fx-06 配置说明](../config/fx-04-lifetime.md)；编辑器实现见 [editor spec](../spec-editor/fx-04-lifetime.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

寿命区是效果表单的时间维：三选一切换后，时长与周期表单随矩阵收窄。

## 2. 布局线框

```text
┌─ 寿命区 ────────────────────────────────────────────────────────────┐
│ 寿命： (•)Instant   ( )After   ( )Infinite        participatesIn…  │
│ ┌─ duration（After/Infinite 时显示）──────────────────────────────┐ │
│ │ durationTicks [ 45  ]  periodTicks [  0  ]  clockId [FixedFrame▾]│ │
│ │ 首拍预估：1..45 tick 内（运行时确定性散列，编辑期不可算）          │ │
│ │ ▸ expireCondition（可选）：kind [TagPresent▾] tag [____] sense[▾]│ │
│ └──────────────────────────────────────────────────────────────────┘ │
│ 时间带示意：[应用|●·······周期·······过期|移除]                       │
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 寿命三选一 | 内建三值 | 切换即套用 duration 矩阵显隐 |
| durationTicks / periodTicks | 数值输入 | 热字段徽标（下次施放生效） |
| clockId 下拉 | clock 表 | 缺省 FixedFrame；EntityLocal 标注"以目标为准" |
| 首拍预估 | 只读说明 | 显示散列区间，不假装能算出具体 tick |
| 时间带示意 | 寿命+周期+过期条件组合 | 直观呈现相位节拍 |

## 4. 关键交互流：配一个腐蚀 DoT

1. 寿命选 After，duration 区展开且必填。
2. 填 durationTicks=300、periodTicks=30；时间带显示 10 次周期拍。
3. 展开过期条件，选 TagPresent + 目标 tag。
4. 保存；矩阵违例（如全零块）在字段行内报红。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| Instant | 寿命选即时 | duration 区折叠并禁用 |
| 全零块 | Infinite 且显式块全零 | 红条"显式块不得全零" |
| 周期生效 | periodTicks>0 | 首拍预估行出现 |

## 6. 易用性验收口径

- 任一寿命下的 duration 矩阵违例输入即报，无需保存试错。
- 周期效果能看到"首拍由散相声明的承诺"而不是空白。

**相关文档**：[fx-06 PRD](../prd/fx-04-lifetime.md) · [editor spec](../spec-editor/fx-04-lifetime.md)
