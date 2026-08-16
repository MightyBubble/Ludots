# fx-01 UXD · 效果模板骨架的编辑器需求

> 效果模板骨架的编辑器需求（高保真规格）。第一性需求见 [fx-01 PRD](../prd/fx-02-template.md)；配置写法见 [fx-01 配置说明](../config/fx-02-template.md)；编辑器实现见 [editor spec](../spec-editor/fx-02-template.md)；上限数值以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

效果编辑器是效果卷的表单主页：一个模板一屏，presetType 驱动参数块显隐与必填。

## 2. 布局线框

```text
┌─ 效果编辑器 ───────────────────────────────────────────────────────┐
├─ 左：模板清单 ────────┬─ 右：模板表单 ─────────────────────────────┤
│ ▸ CostPowerPlantStep  │ 身份：id [________] tags [________]（≤1）   │
│ ▸ Construction  ⚡热  │ 原型：presetType [Buff ▾]  lifetime [After] │
│ ▸ PlacePowerPlant     │       participatesInResponse [✗]            │
│ ▸ ＋新建模板          │ 参数块：▸duration ▸grantedTags ▸stack …     │
│                       │ （灰色块=当前 presetType 不允许）            │
├─ 底部：块导航 chips [duration] [grantedTags] [+ 添加块（合法集）] ──┤
└──────────────────────────────────────────────────────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 模板清单 | 效果注册表 + 合并视图 | 引用计数；热改标记 |
| presetType 下拉 | preset 类型注册表（fx-02） | 切换即重算合法块集 |
| lifetime 三选一 | 内建三值 | 与 duration 表单联动（fx-03） |
| 参数块折叠组 | 17 组件块 schema | 非法块置灰带原因；必填块红点 |
| 块导航 chips | 当前模板已用块 | 点击滚动定位 |

## 4. 关键交互流：新建一个 DoT

1. 清单点"＋新建模板"，填 id 与 tags。
2. 选 presetType=DoT、lifetime=After，勾掉 participatesInResponse。
3. 块导航出现必填 duration 与 modifiers（红点）；填时长与每跳数值。
4. 保存：校验通过后注册表计数 +1，非法组合在对应块行内报红。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 必填缺失 | presetType/lifetime/参与响应未填 | 保存禁用 + 顶部红条 |
| 非法块 | 当前原型下不允许的块 | 块置灰，悬停显示原因 |
| 热改边界 | 编辑白名单外字段 | 字段标"重启生效"锁 |

## 6. 易用性验收口径

- 任一非法组合在输入时即被发现，不等到保存。
- 切换 presetType 后必填块一屏可见，无需查文档。

**相关文档**：[fx-01 PRD](../prd/fx-02-template.md) · [editor spec](../spec-editor/fx-02-template.md)
