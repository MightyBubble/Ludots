# ai-05 UXD · 归一化与响应曲线的编辑器需求

> ai-04 的编辑器需求（高保真规格）。第一性需求见 [ai-04 PRD](../prd/ai-03-norm-curves.md)；配置写法见 [ai-04 配置说明](../config/ai-03-norm-curves.md)；编辑器实现见 [editor spec](../spec-editor/ai-03-norm-curves.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

归一化/曲线面板是效用整形器的可视化工位：左边改参数，右边即时看到 0..1 映射形状。

## 2. 布局线框

```text
┌─ 整形器面板 ────────────────────────────────────────────────────────┐
├─ 左：清单 ─────────────┬─ 右：编辑器 + 曲线预览 ─────────────────────┤
│ 归一化                 │ Kind [RangeInverse ▾]  Min [0]  Max [1600] │
│ ▸ CloseHostile 反向    │ ┌────────────────────────┐                │
│ ▸ LowHealth   反向     │ │ 1.0┤▔▔▀▀▄▄▂▂            │                │
│ ▸ HighHealth  正向     │ │ 0.0┼──────▄▄▀▀──→ raw  │                │
│ 曲线                   │ └────────────────────────┘                │
│ ▸ Linear       直通    │ 被引用：决策.Attack 考量[0] …              │
│ ＋新建（归一化/曲线）   │                                           │
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| Kind 下拉 ×2 | Identity/Range/RangeInverse 与 Linear/Power/Inverse | 切换重建参数区 |
| Min/Max 数字框 | float | 非 Identity 时 Max>Min 前置校验；预览同步 |
| Exponent 数字框 | float>0 | Power 独占；预览同步 |
| 曲线预览 | 本地重放 Normalize+Curve 公式 | 0..1 采样画线；随参数实时刷新 |
| 被引用索引 | decisions 考量的 Normalization/Curve 引用 | 点击跳决策 |

## 4. 关键交互流：做一条"越近越想打"的考量链

1. 归一化面板新建：Kind=RangeInverse、Min=0、Max=1600，预览确认低距高分。
2. 曲线面板确认 Linear 存在（或新建 Power Exponent=2 加强近距）。
3. 决策面板考量里组装 input×归一化×曲线，预览沿同公式联动。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 非法窗口 | Max≤Min 且非 Identity | 输入框红框 + 禁止保存 |
| 非法指数 | Exponent≤0 | 同上 |
| Identity 带 Min/Max | Kind=Identity 且填了边界 | 灰字提示"将被忽略" |
| 无引用 | 零个考量引用 | 可安全删除徽标 |

## 6. 易用性验收口径

- 参数改动到曲线形状变化 ≤ 1 帧延迟。
- 保存前所有非法组合被表单层拦截。
- 预览公式与运行时 Normalize/Curve 逐点一致（同源实现）。

**相关文档**：[ai-04 PRD](../prd/ai-03-norm-curves.md) · [editor spec](../spec-editor/ai-03-norm-curves.md)
