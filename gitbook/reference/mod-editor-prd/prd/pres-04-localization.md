# pres-04 · 本地化

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/pres-04-localization.md)；编辑器需求见 [UXD](../uxd/pres-04-localization.md)；引擎实现见 [runtime spec](../spec-runtime/pres-04-localization.md)；editor spec 见 [editor spec](../spec-editor/pres-04-localization.md)；现状见 [reference](../reference/pres-04-localization.md)。

## 1. 定位

本地化两张表把"文案"从代码与配置中抽出来：token 表声明有哪些带参文案槽（id + 参数个数），locale 表按语言给出每个槽的模板。表现层的 HUD、世界文本、实体信息面板都消费 token。

## 2. 产品承诺

- **token 是契约**：文案槽先声明（id + argCount）后引用；能力文案 token 在启动期被校验，缺失即失败——玩家不会看到裸键名。
- **多语并存**：locale 表一次声明多语言映射；默认语言由 defaultLocale 指定，运行期按选择取模板。
- **参数位次即语义**：模板用 `{0}/{1}` 位次参数，argCount 是检查依据——槽与模板参数数不符即错。
- **mod 可增量**：两张表均 ArrayById/DeepObject 合并，皮肤 mod 补一种语言、改一句文案都只动自己的文件。

## 3. 运行行为

加载后形成 token 目录与 locale 映射；HUD/WorldHud 渲染时以 token id + 实参取当前语言模板格式化；能力表现加载完成后对文案 token 做注册校验。

## 4. 异常承诺

token id 缺失或引用未注册 token、能力文案 token 未注册、locale 映射结构非法——启动失败并指明条目与位置。

**相关文档**：[配置说明](../config/pres-04-localization.md) · [pres-01](pres-01-performers.md) · [misc-04](misc-04-entity-info.md)
