# fx-17 UXD · 效果授予 Tag 的编辑器需求

> fx-16 的编辑器需求（高保真规格）。第一性需求见 [fx-16 PRD](../prd/fx-13-granted-tags.md)；配置写法见 [fx-16 配置说明](../config/fx-13-granted-tags.md)；编辑器实现见 [editor spec](../spec-editor/fx-13-granted-tags.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

效果编辑页的"授予 Tag"区块：把状态 tag 与贡献公式挂在效果上，与修改器区块并列。

## 2. 布局线框

```text
┌─ 效果编辑页 · 授予 Tag 区块 ──────────────────────────────────┐
│ [＋ 添加授予]                          用量 [2 / 上限·见事实页] │
├─ 授予清单 ────────────────────────────────────────────────────┤
│ ① tag   [State.Constructing ▾]                                │
│   公式  (·)Fixed  ( )Linear  ( )LinearPlusBase                │
│   amount [1]   base [—]（仅 LinearPlusBase 可编辑）            │
│ ② tag   [Status.Slowed ▾]  公式 Fixed  amount [1]              │
├─ 层数试算 ── 层 1/2/3 的贡献量预览（与引擎公式同源）────────────┤
└────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| tag 选择器 | tag 注册表 + 本 mod 使用面 | 搜索选择；未注册名提示"首现注册" |
| 公式单选 | Fixed / Linear / LinearPlusBase | 切换联动 amount/base 可见性；不提供 GraphProgram |
| amount / base | 非负整数 | 超计数上限显示钳制提示 |
| 用量条 | 授予条数 vs 上限（事实页） | 接近上限预警 |
| 层数试算 | 引擎 Compute 同源实现 | 层 1..5 试算贡献量 |

## 4. 关键交互流：给中毒效果配线性增长状态

1. 选中 Buff 效果 → 授予 Tag 区块 → 添加授予。
2. 选 tag `Status.Poison`，公式 Linear，amount 2。
3. 层数试算显示 2/4/6，保存。
4. 运行期堆叠刷新按差量补扣（4→6 只补 2），提示"不闪断"。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| LinearPlusBase 缺 base | 公式切换后清空 | 红条，保存禁用 |
| 授予条数满 | 添加至上限 +1 | 添加按钮禁用并提示上限 |
| 运行期容量溢出 | 诊断回流 | 效果实例标 `TagCountOverflow` |

## 6. 易用性验收口径

- 公式语义（三行对照）在表单内一跳可见。
- 任一授予的"定义 + 层数贡献预览"≤ 2 跳可达。

**相关文档**：[fx-16 PRD](../prd/fx-13-granted-tags.md) · [editor spec](../spec-editor/fx-13-granted-tags.md)
