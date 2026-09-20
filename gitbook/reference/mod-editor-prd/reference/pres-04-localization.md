# pres-04 reference · 本地化

> 现状参考。第一性需求见 [pres-04 PRD](../prd/pres-04-localization.md)；配置说明见 [pres-04 配置说明](../config/pres-04-localization.md)。

## 1. 现状快照

- 两张表：`Presentation/text_tokens.json`（ArrayById：id、argCount）与 `Presentation/text_locales.json`（DeepObject：defaultLocale + locales 映射，键为 locale、值为 tokenId→模板）。
- 加载产出文本目录；GameEngine 在加载后对已注册能力做文案 token 校验（默认语言存在时，requireTokensOnAllPresentations=false 分级）。
- 消费方：HUD / WorldHud（表现器 WorldText 行为、worldBar 等参数）、实体信息面板（token 引用，见 misc-04）。
- 引擎默认无根表内容；token 与语言均由 mod 下沉（LudotsCoreMod、EntityInfoPanelsMod、GenreInfoShowcaseMod 等）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 目录加载器 | src/Core/Presentation/Config/PresentationTextCatalogLoader.cs:13-14 |
| 引擎挂接 + 能力文案校验 | src/Core/Engine/GameEngine.cs:1121-1129 |
| 样例 | mods/LudotsCoreMod/assets/Presentation/text_tokens.json、text_locales.json |

**相关文档**：[pres-04 PRD](../prd/pres-04-localization.md) · [misc-04 reference](misc-04-entity-info.md)
