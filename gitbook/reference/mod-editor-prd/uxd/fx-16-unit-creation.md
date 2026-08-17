# fx-20 UXD · 造单位的编辑器需求

> fx-19 的编辑器需求（高保真规格）。第一性需求见 [fx-19 PRD](../prd/fx-16-unit-creation.md)；配置写法见 [fx-19 配置说明](../config/fx-16-unit-creation.md)；编辑器实现见 [editor spec](../spec-editor/fx-16-unit-creation.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

CreateUnit 效果编辑页的造单位表单：来源、数量、摆放与朝向、出生效果、归属开关。

## 2. 布局线框

```text
┌─ 效果编辑页 · 造单位 ──────────────────────────────────────────┐
│ 来源   (·)模板实体 [rts_ra_power_plant ▾]  ( )unitType [▾]     │
│ 数量   count [1]                                                │
│ 摆放   pattern (·)Scatter ( )Circle                             │
│        Scatter: offsetRadius [340]                              │
│        Circle:  placementRadiusCm [200]  startAngleDeg [0]      │
│ 朝向   facing [PreserveTemplate ▾]（仅 Circle 可用）            │
│ 出生   onSpawnEffect [Effect...Construction ▾ 可空]             │
│ 归属   [✔ 继承玩家归属]  [✔ 源挂为父]（仅 true 可存）           │
└─────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 来源切换 | 模板注册表 / unitType 注册表 | 单选互斥，切换保留各自上次值 |
| pattern 单选 | Scatter/Circle | 切换联动两组摆放字段显隐 |
| 朝向下拉 | PreserveTemplate(缺省)/RadialOutward/两种切向 | Scatter 下禁用并解释 |
| onSpawnEffect 选择 | 效果模板注册表 | 可空；悬空引用阻保存 |
| 归属开关 | 布尔 | 开=写 true，关=删字段；false 永不落盘 |

## 4. 关键交互流：训练厂出兵走散布

1. 打开 TrainRhino 效果 → 造单位表单。
2. 来源选模板实体 `rts_ra_rhino`；count 2。
3. pattern 选 Scatter，offsetRadius 340。
4. 开启继承玩家归属，保存；热通道提示下次施放生效。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 来源双选/双空 | 切换残留 | 红条"二选一"，保存禁用 |
| 互斥字段残留 | pattern 切换 | 自动隐藏并在校验面板列出"Scatter 禁用项" |
| Circle 缺起始角 | 清空 | 红条 |
| 出生效果被删 | 引用悬空 | 选择器标"未注册"并阻保存 |

## 6. 易用性验收口径

- 图案切换后可见字段集与该图案合法集完全一致（无禁用灰置歧义）。
- "归属开关关闭=字段不存在"的持久化语义在表单提示中一跳可见。

**相关文档**：[fx-19 PRD](../prd/fx-16-unit-creation.md) · [editor spec](../spec-editor/fx-16-unit-creation.md)
