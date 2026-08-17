# misc-04 UXD · 实体信息档案的编辑器需求

> misc-04 的编辑器需求（高保真规格）。第一性需求见 [misc-04 PRD](../prd/misc-04-entity-info.md)；配置写法见 [misc-04 配置说明](../config/misc-04-entity-info.md)；编辑器实现见 [editor spec](../spec-editor/misc-04-entity-info.md)；目录计数以 [事实与取值表](../facts.md) 为准。

## 1. 界面定位

信息面板设计器：左侧选模板，中间所见即所得的面板预览，右侧逐槽位绑定 token/属性/能力。配完的档案在游戏里就是"点开实体看到的那张卡"。

## 2. 布局线框

```text
┌─ 信息面板设计器 ──────────────────────────────────────────────────┐
├─ 左：模板→档案 ───┬─ 中：面板预览 ─────────┬─ 右：槽位检查器 ─────┤
│ fourx_governor ●  │ ╔═ 4X ════════════╗   │ 体裁色 [#D8A85D]    │
│ moba_hero         │ ║ GV  Governor      ║   │ 肖像 [GV] 徽记[4X]  │
│ rts_barracks      │ ║ HP 42/50 AU 120   ║   │ 副题 token ▾        │
│ ＋新建档案        │ ║ [BO][CL][TR]      ║   │ 数值条: source ▾    │
│                   │ ╚═══════════════════╝   │  attribute ▾ Health │
│                   │  (实时读样例实体属性)     │ 动作: ability ▾     │
└───────────────────┴─────────────────────────┴─────────────────────┘
```

## 3. 控件与数据源

| 控件 | 数据源与取值 | 行为 |
|---|---|---|
| 模板清单 | 实体模板注册表 + 已建档案标记 | 已归属模板锁定（互斥提示） |
| 面板预览 | 档案数据 + 样例实体属性值 | token/属性变更即时重绘 |
| 色板 | accent/surfaceColorHex | 取色器 + hex 校验 |
| token 下拉 | 文本目录（pres-04）投影 | 按 token id 搜索；未注册 token 不可手输 |
| 数值条编辑 | stats 数组 | source 切换 attribute/constant 参数联动；display 三选 |
| 属性下拉 | AttributeRegistry 投影 | 封闭；未知即无选项 |
| 动作编辑 | actions 数组 | 能力下拉来自能力注册表（GAS） |

## 4. 关键交互流：给新单位做信息卡

1. 左侧选未归属模板 → "新建档案"。
2. 右侧选体裁色、徽记、肖像字。
3. 副题/正文选 token（缺失时跳 pres-04 补）。
4. 数值条加 Health（currentOverBase）。
5. 动作挂能力 BuildOutpost；预览按钮点亮。
6. 保存（本 mod 的 EntityInfo/insight_profiles.json）。

## 5. 状态设计

| 状态 | 触发 | 呈现 |
|---|---|---|
| 模板已归属 | 选中被其他档案占用的模板 | 锁定 + 归属档案链接 |
| token 缺失 | 引用未注册 token | 下拉外红标 + 跳 pres-04 |
| 属性未注册 | stats 引用未知属性 | 属性下拉红 |
| 能 mod 未装 | 工程缺 EntityInfoPanelsMod | 页面提示"此表随能力 mod 加载（D5）" |
| 待重启 | 保存后 | 状态栏"重启生效" |

## 6. 易用性验收口径

- 新建档案到预览成形 ≤ 5 步且全程不手写 JSON。
- 每个 token/属性/能力引用"定义处"一键可达。

**相关文档**：[misc-04 PRD](../prd/misc-04-entity-info.md) · [editor spec](../spec-editor/misc-04-entity-info.md)
