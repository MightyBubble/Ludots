# ai-05 UXD · 决策者与档案的编辑器需求

> ai-05 的编辑器需求（高保真规格）。第一性需求见 ；配置写法见 ；编辑器实现见 ；上限数值以  为准。

## 1. 界面定位

决策者/档案面板是效用 AI 的装配台：把决策排进竞技场、把竞技场打包成性格、把性格挂到单位。

## 2. 布局线框

```text
┌─ 决策者与档案面板 ────────────────────────────────────────────────────┐
├─ 左：档案树 ─────────┬─ 右：详情 ────────────────────────────────────┤
│ ▸ Profile.Mage       │ Profile.UtilityAutocast.Mage                  │
│   └ DM.Mage          │ 节奏  interval [1]  MaxCandidates [32]        │
│     ├ Attack   ⚡65  │ stance [（空·ai-08）▾]                         │
│     ├ HealBurst ⚡78 │ 决策者 DM.Mage：mode [UtilityScore ▾]          │
│     └ Curse    ⚡55  │              margin [0]                       │
│ ＋新建档案           │ 决策竞技场（实时分，来自 trace）               │
│                      │  # 决策       分数  桶  状态                  │
│                      │  1 HealBurst  .78   0  ●当前                 │
├─ 底部：挂接视图 [Mage 模板 × 12 实体 · interval 1 步] ────────────────┤
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 档案树 | profiles→decision_makers→decisions 三层引用链 | 空档显示"十表非空必须至少一个 profile" |
| interval/MaxCandidates | 数字框 | 正数校验；底部换算"每 N×16.7ms 思考一次" |
| SelectionMode | UtilityScore/FixedPriority | FixedPriority 时 margin 控件禁用置灰 |
| SwitchMargin | 数字框 | 旁边微缩图示意抖动抑制带 |
| 决策排序 | decisions 合并视图 | 拖动排序；连续区间实时检查（I3） |
| 竞技场实时分 | UtilityAiDecisionTrace | 需运行中会话；静态时显示最近一次 |
| 挂接视图 | 实体模板扫描（UtilityAiAgent.ProfileId） | 反查哪些模板用此档案 |

## 4. 关键交互流：把一组决策装配成新兵种性格

1. 新建档案 → 拖入既有决策者 DM.Mage（或新建决策者再挂决策）。
2. 调 interval=2（降频省性能）、MaxCandidates=16。
3. 区间检查绿标 → 保存。
4. 挂接视图跳实体模板，UtilityAiAgent.ProfileId 填入。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 引用断链 | 决策/决策者名不存在 | 树节点红点 |
| 区间不连续 | 排序后解析区间断开 | 红条指断点（I3） |
| 无 profile 预警 | 十表非空而 profiles 空 | 顶部红条 |
| stance 未接线 | DefaultStance 已选 | 灰字提示"编译保留、暂无系统消费（ai-08/I6）" |
| 静态会话 | 无运行实例 | 竞技场显示占位说明 |

## 6. 易用性验收口径

- 档案树三层 ≤ 1 屏展开；实时分与 trace 数据同源。
- interval/maxCandidates 的性能含义有人话提示。
- FixedPriority 与 margin 的适用差异在表单内即可读懂。

**相关文档**：[ai-05 PRD](../prd/ai-05-dm-profiles.md) · [editor spec](../spec-editor/ai-05-dm-profiles.md)
