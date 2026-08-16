# ai-03 UXD · 决策的编辑器需求

> ai-03 的编辑器需求（高保真规格）。第一性需求见 [ai-03 PRD](../prd/ai-04-decisions.md)；配置写法见 [ai-03 配置说明](../config/ai-04-decisions.md)；编辑器实现见 [editor spec](../spec-editor/ai-04-decisions.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

决策面板是效用 AI 的编辑主战场：一个条目 = 一张"何时对谁做什么"的表格，分数结构一眼可算。

## 2. 布局线框

```text
┌─ 决策面板 ─────────────────────────────────────────────────────────────┐
├─ 左：决策清单 ──────┬─ 右：决策详情 ──────────────────────────────────┤
│ ▸ Attack    ⚡65 敌 │ Decision.UtilityAutocast.Attack                │
│ ▸ HealBurst ⚡78 友 │ 筛选 [TF.UtilityAutocast.Hostile ▾]  技能[Attack▾] │
│ ▸ Curse     ⚡55 敌 │ 考量：                                         │
│ ＋新建决策          │  #  Input       Norm          Curve    聚合   W │
│                    │  0  Distance    CloseHostile  Linear   WSum  0.2 │
│                    │  ＋加考量                                        │
│                    │ 节流：P10 B0.35 W1 CD30 槽0 共享GCD              │
│                    │ 任务：[Task.Attack]（连续区间 ✔）                │
├─ 底部：分数预演 (multiply + weighted) × Weight ────────────────────────┤
└────────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 决策清单 | decisions 合并视图 | 徽标：目标阵营、上次得分（来自 trace） |
| 筛选选择器 | target_filters 合并视图 | 必选；空表引导先建过滤器 |
| 考量表 | inputs/normalizations/curves 三字典 | 三列下拉 + Weight 数字框 + Aggregate 下拉 |
| 节流区 | 数字框组 | P/B/W/Momentum/MinDuration/Cooldown |
| 技能绑定 | AbilityDefinitionRegistry + 槽位 | 可空；与任务级绑定联动显示回退链 |
| 共享冷却 | tag 注册表 | 可空；空则提示回退 ability 冷却 tag |
| Flags | 五布尔复选 | 与 Flags[] 数组写法等价互转 |
| 任务区 | tasks 合并视图 | 拖排顺序；连续性实时检查（I3） |
| 分数预演 | 本地重放聚合公式 | 给样例 raw 值即出总分 |

## 4. 关键交互流：新建一个"残血就奶"决策

1. 新建决策 → 选过滤器 TF.Friendly。
2. 加考量：Input=TargetHealth、Norm=LowHealth、Curve=Linear、Aggregate=WeightedSum、Weight=1.5。
3. 节流区设 CooldownSteps=30、AbilityKey=HealBurst、SharedCooldownTag=GCD。
4. 任务区引用 Task.HealBurst，连续性绿标；保存。
5. 分数预演给 raw=60 → 总分确认后挂进决策者（ai-04）。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| Veto 生效 | 预演中某考量 curved≤0 | 预演显示 0 并标"被否决" |
| 任务不连续 | 引用的任务解析区间断开 | 红条 + 指明断点（I3） |
| 断链引用 | 四件套/过滤器/技能名不存在 | 下拉红框 + 保存拒绝 |
| 双写 Flags | 布尔与 Flags[] 同时出现 | 归一化到一种写法再落盘 |

## 6. 易用性验收口径

- 新建决策到挂入决策者 ≤ 5 分钟（含建考量）。
- 聚合语义（Veto/WSum/Multiply）在预演中可视化呈现。
- 任务区间连续性在编辑期而非启动期被发现。

**相关文档**：[ai-03 PRD](../prd/ai-04-decisions.md) · [editor spec](../spec-editor/ai-04-decisions.md)
