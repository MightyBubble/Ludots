# misc-01 UXD · 进度域的编辑器需求

> misc-01 的编辑器需求（高保真规格）。第一性需求见 [misc-01 PRD](../prd/misc-01-progression.md)；配置写法见 [misc-01 配置说明](../config/misc-01-progression.md)；编辑器实现见 [editor spec](../spec-editor/misc-01-progression.md)；目录计数以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

进度编辑器：范围、进度线、需求条件树三栏联动；作者在这里画科技树/成就线，并把完成效果挂到 GAS 技能上。

## 2. 布局线框

```text
┌─ 进度编辑器 ──────────────────────────────────────────────────────┐
├─ 左：范围 ──┬─ 中：进度线列表 ─────┬─ 右：需求条件树 ────────────────┤
│ fourx.team  │ ▸ fourx.assoc.tech  │ root: EntityCount              │
│  来源:集合   │   scope ▸ fourx.team │  scope ▸ fourx.team            │
│ ＋新建范围   │ ＋新建进度线         │  source [ScopeMembers ▾]       │
│             │                     │  count [2] tags[Researcher ＋]  │
├─ 底部：GAS 挂钩检查: CompleteProgression×2 ✓  未挂进度线×1 ⚠ ───────┤
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 范围表单 | scopes 表 | memberSource 切换时 collection 下拉（仅已配置集合）启用 |
| 进度线列表 | progressions 表 | scope 下拉来自范围注册表 |
| 条件树编辑器 | requirements.root | kind 封闭下拉；参数表单随 kind 切换 |
| tag 选择器 | tag 注册表（通用用户表） | 多选 |
| GAS 挂钩检查 | 效果表扫描（CompleteProgression 预设） | 双向：效果→进度线、进度线→效果 |

## 4. 关键交互流：加一条科技线

1. 新建进度线，选 scope。
2. 条件树选 EntityCount，设 count 与 tag 过滤。
3. 在技能效果里挂 CompleteProgression（经 fx 编辑器），选本进度线 + delta。
4. 挂钩检查变绿；保存（重启生效）。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 集合未配置 | collection 引用失效 | 下拉红标"先配置实体集合" |
| 孤儿进度线 | 无任何效果推进它 | 底部检查 ⚠ 清单 |
| 条件树缺参 | kind 参数空 | 参数表单红框，保存拦截 |
| 注册上限 | 接近注册表上限（见 reference） | 用量预警 |

## 6. 易用性验收口径

- 进度线与其全部挂钩（效果）≤ 2 跳互达。
- 条件树每种 kind 的必填参数在表单层可见可验。

**相关文档**：[misc-01 PRD](../prd/misc-01-progression.md) · [editor spec](../spec-editor/misc-01-progression.md)
