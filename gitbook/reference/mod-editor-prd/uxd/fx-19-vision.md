# fx-23 UXD · 视野揭示的编辑器需求

> fx-22 的编辑器需求（高保真规格）。第一性需求见 [fx-22 PRD](../prd/fx-19-vision.md)；配置写法见 [fx-22 配置说明](../config/fx-19-vision.md)；编辑器实现见 [editor spec](../spec-editor/fx-19-vision.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

效果编辑页的"视野揭示"通用区块：可挂任意 preset，配范围、层与记忆，并明示现状可执行性。

## 2. 布局线框

```text
┌─ 效果编辑页 · 视野揭示区块 ────────────────────────────────────┐
│ 范围  radius [600] cm     scope [team ▾]                       │
│ 层    layers [✔ground ✔detection ☐air…]（≤ 上限·事实页）       │
│ 记忆  memoryTtlTicks [90]  强度 detectionStrength [2] (0..255)  │
│ ⚠ 本块处理器现状未通过启动认证：保存可写，启动校验将拒绝         │
└─────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| radius | 正整数 cm | <=0 即时红条 |
| scope 选择 | 作用域注册表（Progression/scopes） | 只允许已注册名 |
| layers 多选 | 迷雾层注册表 | 上限见事实页；超选禁用 |
| memoryTtlTicks | 非负整数 | 0 显示"不留记忆"注记 |
| detectionStrength | 0..255 | 滑条限界 |

## 4. 关键交互流：给侦查技能配周期揭示

1. 打开 After 生命周期效果，确认 duration.periodTicks 为 5。
2. 展开视野揭示区块，radius 600、scope team、勾选 ground+detection。
3. memoryTtlTicks 90、强度 2；警示条确认后保存。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| After 无周期 | 生命周期区联动 | 红条"需 periodTicks>0 刷新" |
| scope/层悬空 | 注册表项被删 | 选择器标"未注册"并阻保存 |
| 现状不可执行 | 区块任意启用 | 常驻黄条（与启动错误同源） |

## 6. 易用性验收口径

- "层上限是多少、scope 从哪来"在区块内一跳可见。
- 保存可写但启动被拒的现状在保存前就有明示，不等到启动报错。

**相关文档**：[fx-22 PRD](../prd/fx-19-vision.md) · [editor spec](../spec-editor/fx-19-vision.md)
