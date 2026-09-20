# pres-04 配置说明 · 本地化

> 配置写法与行为。第一性需求见 [pres-04 PRD](../prd/pres-04-localization.md)；编辑器需求见 [UXD](../uxd/pres-04-localization.md)；现状见 [reference](../reference/pres-04-localization.md)。

## 1. 示例配置

核心 mod 真实两件（`mods/LudotsCoreMod/assets/Presentation/`，节选）：

```json
[
  { "id": "hud.attribute.current", "argCount": 1 },
  { "id": "hud.attribute.current_over_base", "argCount": 2 },
  { "id": "hud.combat.delta", "argCount": 1 }
]
```

```json
{
  "defaultLocale": "en-US",
  "locales": {
    "en-US": {
      "hud.attribute.current": "{0}",
      "hud.attribute.current_over_base": "{0}/{1}",
      "hud.combat.delta": "{0}"
    },
    "zh-CN": {
      "hud.attribute.current": "{0}",
      "hud.attribute.current_over_base": "{0}/{1}",
      "hud.combat.delta": "{0}"
    }
  }
}
```

## 2. 字段与行为

| 表 | 字段 | 这样配会产生什么效果 |
|---|---|---|
| text_tokens | `id` | 文案槽名；全局命名，HUD/WorldHud/面板按此引用 |
| text_tokens | `argCount` | 参数个数；模板位次参数数的检查依据 |
| text_locales | `defaultLocale` | 默认语言键；能力文案校验按此语言执行 |
| text_locales | `locales.<键>.<tokenId>` | 该语言下此槽的模板；`{0}`… 位次参数 |

## 3. 文件结构

目录条目 `Presentation/text_tokens.json`（根数据为空，由 mod 贡献）（ArrayById）与 目录条目 `Presentation/text_locales.json`（根数据为空，由 mod 贡献）（DeepObject：键为 locale，值为 tokenId→模板映射）。mod 通过目录合并追加 token 与语言（合并语义见 cfg-05）。

## 4. 运行时加载效果

加载产出 token 目录与 locale 选择器；有默认语言时对已注册能力做文案 token 校验（表现里引用的 token 必须可解析）。**生效级别：重启**（文案属表现身份）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| token 缺 id | 启动失败 |
| 能力表现引用未注册 token | 启动校验失败，指明能力与 token |
| locale 结构非法（非对象/键缺失） | 启动失败 |

## 6. 实例

- `mods/LudotsCoreMod/assets/Presentation/text_tokens.json`（HUD 文案槽）
- `mods/capabilities/entityinfo/EntityInfoPanelsMod/assets/Presentation/text_tokens.json`（面板文案下沉 mod 的用法）

**相关文档**：[pres-04 PRD](../prd/pres-04-localization.md) · [pres-01 配置说明](pres-01-performers.md)
