# ai-10 UXD · 战斗姿态与执行器门的编辑器需求

> ai-09 的编辑器需求（高保真规格）。第一性需求见 [ai-09 PRD](../prd/ai-08-stances-actuators.md)；配置写法见 [ai-09 配置说明](../config/ai-08-stances-actuators.md)；编辑器实现见 [editor spec](../spec-editor/ai-08-stances-actuators.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

姿态/执行器面板是许可层工位：姿态定打谁、执行器定能不能打；现状半成品的状态必须显性呈现，不让作者空欢喜。

## 2. 布局线框

```text
┌─ 姿态与执行器面板 ────────────────────────────────────────────────────┐
├─ 左：清单 ────────────┬─ 右：详情 ───────────────────────────────────┤
│ 姿态（0 条 · 半成品⚠）│ Stance.Example.HoldFire      [⚠ 未接线]      │
│ 执行器（0 条）        │ 筛选 [TF.Example.Precise ▾]                   │
│ ＋新建姿态/执行器     │ 许可 [✗索敌] [✔反击] [✗追击]                  │
│                      │ 消费方：无系统（I6）· profile.DefaultStance 仅存 │
│                      │ ── 执行器详情（选中时切换） ──                 │
│                      │ Actuator.MainGun  技能[Fire▾]                  │
│                      │ 就绪源 [In.Ready01 ▾]  瞄准门 [In.SkillUp ▾]   │
│                      │ 门控试验：注入组件值 → PassesActuatorGates 结果 │
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 姿态清单 | stances 合并视图 | 空态显示半成品警示（I6/I7） |
| 筛选选择器 | target_filters 合并视图 | 可空 |
| 许可三开关 | 布尔 | 落盘三字段 |
| 执行器表单 | abilities + inputs 合并视图 | 两 input 引用只列既有输入 |
| 组件注入编辑 | ActuatorReadiness/AimGate 组件 schema | 直连实体模板（ent-01 联动） |
| 门控试验 | 本地重放 PassesActuatorGates | 给 Ready01/BlockReason 样例出结果与原因码 |

## 4. 关键交互流：给单位加"受击才反击"姿态

1. 姿态区新建：AutoAcquire=off、Retaliate=on、AllowMoveChase=off。
2. 绑过滤器 TF.Precise；面板顶部显示"编译保留 · 无系统消费（I6）"。
3. 保存后在 profile（ai-06）DefaultStance 挂此姿态——状态注明"仅存值"。
4. （接线落地后）战斗验证单位不主动索敌、被打才还手。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 半成品态 | 姿态区任何条目 | 顶部固定警示条（I6） |
| 空占位 | 表为 [] | 清单灰显"占位文件"（I7） |
| 引用断链 | 过滤器/输入名不存在 | 下拉红框 |
| 门控被拦 | 试验中 aimGate 未就绪 | 结果区显示 AimGateNotReady |

## 6. 易用性验收口径

- 半成品状态在打开面板第一眼可见，不藏在文档。
- 执行器门控试验与运行时 PassesActuatorGates 判定同源。
- 组件注入与实体模板面板双向可达。

**相关文档**：[ai-09 PRD](../prd/ai-08-stances-actuators.md) · [editor spec](../spec-editor/ai-08-stances-actuators.md)
