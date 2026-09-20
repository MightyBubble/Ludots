# fx-18 UXD · 关系操作的编辑器需求

> fx-18 的编辑器需求（高保真规格）。第一性需求见 ；配置写法见 ；编辑器实现见 ；上限数值以  为准。

## 1. 界面定位

Relation 效果编辑页的关系表单：操作、两端槽位、条件字段，配可执行性警示。

## 2. 布局线框

```text
┌─ 效果编辑页 · 关系操作 ────────────────────────────────────────┐
│ 操作   [SetParent ▾]                                           │
│ 对象   subject [Source ▾]      对端 parent [Target ▾]          │
│ 吸附   [✔ snapSubjectToParentPosition]（仅 SetParent）         │
│ 链型   relationshipType [—]（仅 EnsureLink）                    │
│ ⚠ RemoveParent / EnsureLink：现状启动校验将拒绝（详见治理项）    │
└─────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 操作下拉 | SetParent/RemoveParent/EnsureLink | 切换联动吸附与链型可见性 |
| 槽位下拉 ×2 | Source/Target/TargetContext | subject 禁 None；按操作约束 parent |
| 吸附开关 | 布尔 | 仅 SetParent 显示并落盘 |
| 链型选择 | 关系类型注册表 | 仅 EnsureLink 显示；未注册名不可选 |

## 4. 关键交互流：步兵进驻建筑

1. 新建 Relation 效果 → 关系表单。
2. 操作 SetParent；subject Source（步兵）、parent Target（建筑）。
3. 开启吸附；保存后与进驻技能的施放链挂钩。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| RemoveParent/EnsureLink 被选 | 操作切换 | 黄条"现状启动校验将拒绝"（可继续编辑，保存前确认） |
| 链型悬空 | 目录中类型被删 | 选择器标"未注册"并阻保存 |
| parent 为 None | EnsureLink/SetParent 下清空 | 红条 |

## 6. 易用性验收口径

- 操作切换后的可见字段集与合法集一致；越权字段不落盘。
- "哪些操作当前可执行"在表单内一跳可见（警示条）。

**相关文档**：[fx-18 PRD](../prd/fx-18-relation.md) · [editor spec](../spec-editor/fx-18-relation.md)
