# ai-07 UXD · 任务的编辑器需求

> ai-07 的编辑器需求（高保真规格）。第一性需求见 ；配置写法见 ；编辑器实现见 ；上限数值以  为准。

## 1. 界面定位

任务面板是效用 AI 的出口检查站：每条任务最终变成什么订单、走哪个槽位、被谁引用，一屏对账。

## 2. 布局线框

```text
┌─ 任务面板 ────────────────────────────────────────────────────────────┐
├─ 左：任务清单 ───────┬─ 右：任务详情 ────────────────────────────────┤
│ ▸ Attack   订单 槽0  │ Task.UtilityAutocast.Attack                  │
│ ▸ HealBurst 订单 槽1 │ Kind [SubmitOrder ▾]                         │
│ ▸ Curse    订单 槽2  │ 订单  类型 [castAbility ▾]  模式 [Immediate ▾] │
│ ＋新建任务           │       Player [0]                             │
│                      │ 技能  [Ability.Attack ▾]  槽位 [0]            │
│                      │ 整参  I0 [-1 未用]  I1 [0]                   │
│                      │ 出口预览：Order{castAbility, slot0, →target} │
│                      │ 被引用：Decision.Attack（连续区间 ✔）        │
├─ 底部：[组合 Kind 现状提示：Sequence/Parallel 行为等价 · I5] ─────────┤
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| Kind 下拉 | 四值 | 选组合 Kind 时显示 I5 警示条并锁定订单字段 |
| 订单类型选择器 | OrderTypeRegistry | Key/Id 双通道；双写互验预检 |
| SubmitMode | 枚举 | 越界不可选 |
| 技能选择器 | AbilityDefinitionRegistry | 可空；空则标注回退到决策级 |
| 槽位框 | int | -1=未用；出口预览按回退链解析当前生效槽位 |
| 出口预览 | 本地重放 Order 构造 | 展示 I0/I1/Spatial 落位 |
| 被引用索引 | decisions.Tasks 扫描 | 区间连续性同 ai-04 检查 |

## 4. 关键交互流：配一条指向技能槽的自动施法任务

1. 新建任务，Kind=SubmitOrder。
2. 订单类型选 castAbility；技能选 Ability.Attack；槽位 0。
3. 出口预览确认 Order{I0=0, I1=0, Spatial=目标}。
4. 挂进 Decision.Attack 的 Tasks（连续绿标）→ 保存。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 组合 Kind | Kind∈三种组合 | I5 警示："行为与 SubmitOrder 单发近乎等价" |
| 双写冲突 | Key/Id 同给且不一致 | 红条禁存 |
| 槽位回退 | 槽 -1 且无技能可反查 | 预览标"I0 将取 IntArg0 或缺省" |
| 无引用任务 | 零个决策引用 | 灰字未使用 |

## 6. 易用性验收口径

- 出口预览与运行时构造的 Order 逐字段一致。
- 组合 Kind 的现状语义（I5）在选定时即被警示，不误导为真编排。
- 订单/技能引用全部下拉化，不手打。

**相关文档**：[ai-07 PRD](../prd/ai-07-tasks.md) · [editor spec](../spec-editor/ai-07-tasks.md)
