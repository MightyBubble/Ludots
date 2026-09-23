# misc-04 配置说明 · 实体信息档案

> 配置写法与行为。第一性需求见 [misc-04 PRD](../prd/misc-04-entity-info.md)；编辑器需求见 [UXD](../uxd/misc-04-entity-info.md)；现状见 [reference](../reference/misc-04-entity-info.md)。

## 1. 示例配置

GenreInfo showcase 真实档案（`mods/showcases/info_panels/GenreInfoShowcaseMod/assets/EntityInfo/insight_profiles.json`，节选）：

```json
[
  {
    "id": "fourx_governor_profile",
    "templateIds": ["fourx_governor"],
    "accentColorHex": "#D8A85D",
    "surfaceColorHex": "#1A2028",
    "genreGlyph": "4X",
    "portraitGlyph": "GV",
    "genreLabelToken": "genreinfo.genre.4x",
    "subtitleToken": "genreinfo.profile.governor.subtitle",
    "bodyToken": "genreinfo.profile.governor.body",
    "badges": [ { "glyph": "C", "textToken": "genreinfo.profile.governor.badge.colony" } ],
    "stats": [
      { "glyph": "HP", "labelToken": "genreinfo.stat.health", "source": "attribute",
        "attribute": "Health", "display": "currentOverBase" },
      { "glyph": "LY", "labelToken": "genreinfo.stat.lane", "source": "constant",
        "value": 2, "display": "constant" }
    ],
    "tips": [ { "glyph": "1", "textToken": "genreinfo.profile.governor.tip.1" } ],
    "actions": [ { "ability": "Ability.4X.BuildOutpost", "glyph": "BO",
      "titleToken": "genreinfo.profile.governor.action.outpost.title",
      "bodyToken": "genreinfo.profile.governor.action.outpost.body" } ]
  }
]
```

同一模板要给不同摆放各起一个名字时，在档案上加这两项。showcase 节选没有它们，没写就继续用 `Name` 当标题：

```json
"titleToken": "hero.shared.title",
"instances": [
  { "instanceId": "hero.liu", "titleToken": "hero.liu.title" },
  { "instanceId": "camp.harbor.hq", "titleToken": "camp.harbor.hq.title" }
]
```

`hero.liu.title` 这类槽的中文和英文写在多语言表，不写在档案里，也不写在地图上。地图只负责摆出 `instanceId`。子实体的编号是根编号加 localId 链，例如 `camp.harbor.hq`。

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `templateIds` | 匹配的实体模板；跨档案重复即失败（互斥） |
| `accentColorHex` / `surfaceColorHex` | 面板主色与底色 |
| `genreGlyph` / `portraitGlyph` | 体裁徽记与肖像字 |
| `titleToken` | 可选。整份模板共用的标题 token；没写时，没有单独摆放标题的实体标题读 `Name` |
| `instances[].instanceId` | 地图登记的摆放编号。根是 `instanceId`，子实体是 `instanceId` 加 localId 链，例如 `camp.harbor.hq` |
| `instances[].titleToken` | 这个摆放自己的标题 token。对上时盖过档案的 `titleToken` |
| `genreLabelToken` / `subtitleToken` / `bodyToken` | 体裁标签/副题/正文 token；必须可解析 |
| `badges[].glyph/textToken` | 徽章字与文案 |
| `stats[].source` | `attribute`（按名解析 AttributeRegistry）或 `constant`（配 value） |
| `stats[].display` | current / currentOverBase / constant 展示式 |
| `stats[].attribute` | 属性名；未知即加载失败 |
| `tips[].glyph/textToken` | 提示条目 |
| `actions[].ability` | 能力 id 引用（按钮点击走真实技能） |
| `actions[].glyph/titleToken/bodyToken` | 按钮字与文案 |

## 3. 文件结构

目录条目 `EntityInfo/insight_profiles.json`（数据在 GenreInfoShowcaseMod）（ArrayById、整表可空）。**注意**：目录在引擎 config_catalog 声明，但 loader 是 mod 内实现（EntityInfoPanelsMod）——本表随能力 mod 加载（见 D5）。

## 4. 运行时加载效果

EntityInfoPanelsMod 的 Insight 加载器在能力 mod 装载窗口读取：解析模板键（互斥校验）、标题 token、摆放编号、属性 id、能力引用，产出档案目录。**生效级别：重启**。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| templateIds 跨档案重复 | 加载失败，指明两档案与模板 |
| token 解析失败 | 加载失败，指明档案与 token |
| stats 属性未注册 | 加载失败，指明档案与属性名 |
| source/display 枚举外值 | 加载失败 |
| 能力引用未注册 | 加载失败 |
| `titleToken` 未登记，或写了空串 | 加载失败，指明档案与 token |
| `instances` 不是数组，或条目不是对象 | 加载失败 |
| `instanceId` 为空、首尾有空白，或跨档案重复 | 加载失败，指明摆放编号 |
| 实例条目出现 `instanceId`、`titleToken` 以外的字段 | 加载失败，指明字段名 |
| 摆放编号对上的档案不含该实体模板 | 打开面板失败 |

## 6. 实例

- `mods/showcases/info_panels/GenreInfoShowcaseMod/assets/EntityInfo/insight_profiles.json`（4X/MOBA/RTS 三体裁档案）

**相关文档**：[misc-04 PRD](../prd/misc-04-entity-info.md) · [pres-04 配置说明](pres-04-localization.md)
