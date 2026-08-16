# fx-20 UXD · 进度完成的编辑器需求

> fx-20 的编辑器需求（高保真规格）。第一性需求见 [fx-20 PRD](../prd/fx-21-progression.md)；配置写法见 [fx-20 配置说明](../config/fx-21-progression.md)；编辑器实现见 [editor spec](../spec-editor/fx-21-progression.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

CompleteProgression 效果编辑页的进度完成表单：选进度、定作用域、选变更方式。

## 2. 布局线框

```text
┌─ 效果编辑页 · 进度完成 ────────────────────────────────────────┐
│ 进度   id [Progression.Showcase.CityDrill ▾]                   │
│        阶梯摘要：Lv1 → Lv2 → 完成（只读透视）                   │
│ 作用域 ( )self  (·)explicit  ( )命名 [city ▾]                  │
│ 变更   ( )完成  (·)设级 level [1]  ( )推进 delta [+]           │
│        level/delta 互斥，二选一或都不选                         │
└─────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 进度选择器 | 进度注册表 | 选中展示等级阶梯摘要（只读） |
| 作用域三选 | self/explicit 固定项 + 命名作用域注册表 | 命名项仅列已声明作用域 |
| 变更三选 | 完成/level/delta | 互斥单选；level、delta 为正整数 |

## 4. 关键交互流：科研建筑完成市政进度

1. 新建 CompleteProgression 效果 → 进度完成表单。
2. 选 `Progression.Showcase.CityDrill`，摘要确认阶梯。
3. 作用域选 explicit（宿主由施法链的 TargetContext 提供）；变更选设级 level 1。
4. 保存，挂到建筑完工效果链。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 进度悬空 | 进度表条目被删 | 选择器标"未注册"并阻保存 |
| 命名作用域悬空 | 作用域表被删 | 同上 |
| level 与 delta 残留同写 | 导入旧数据 | 校验面板列互斥错误，保存禁用 |
| 运行期宿主无状态缓冲 | 诊断回流 | 效果实例标注前置条件缺失 |

## 6. 易用性验收口径

- "这个进度有几级"在选中后一跳可见。
- 三种作用域的宿主是谁（施法者/受术者/显式宿主）在表单内联说明一跳可见。

**相关文档**：[fx-20 PRD](../prd/fx-21-progression.md) · [editor spec](../spec-editor/fx-21-progression.md)
