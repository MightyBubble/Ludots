# misc-04 · 实体信息档案

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/misc-04-entity-info.md)；编辑器需求见 [UXD](../uxd/misc-04-entity-info.md)；引擎实现见 [runtime spec](../spec-runtime/misc-04-entity-info.md)；编辑器实现见 [editor spec](../spec-editor/misc-04-entity-info.md)；现状见 [reference](../reference/misc-04-entity-info.md)。

## 1. 定位

实体信息档案声明"点开一个实体看到什么"：按实体模板匹配档案，给出体裁视觉（配色/徽记/肖像）、文案 token 组（副题/正文）、徽章、数值条（引属性或常量）、提示与动作按钮（引能力）。体裁化展示 showcase 的核心配置面。

## 2. 产品承诺

- **模板互斥匹配**：一个实体模板至多归属一个档案——templateIds 冲突即启动失败，绝无二义渲染。
- **文案全走 token**：副题、正文、徽章、提示、动作标题全部引用本地化 token；token 必须可解析，裸文案不进档案。
- **数值两源**：stats 按名解析属性（AttributeRegistry），未知属性即失败；或常量直供——展示永远不猜。
- **动作即能力**：actions.ability 引用能力 id；玩家在面板上按下的就是真实技能管线。
- **档案在 mod、加载在 mod**：表在目录声明，但 loader 由 EntityInfoPanelsMod 提供——装了该能力 mod 才有此面（现状约束，见 reference）。

## 3. 运行行为

实体信息面板系统在选中实体时按模板键查档案，渲染配色与徽记、取 token 文案、读属性渲染数值条、按能力 id 出动作按钮。

## 4. 异常承诺

templateIds 重复归属、token 解析失败、stats.source=attribute 的属性未注册、source/display 枚举外值、能力引用未注册——加载失败并指明档案与位置。

**相关文档**：[配置说明](../config/misc-04-entity-info.md) · [pres-04](pres-04-localization.md) · [attr-01](attr-01-definition.md)
