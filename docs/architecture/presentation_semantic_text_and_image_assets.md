# Presentation：语义 TextToken 映射与 2D 图像资产合同

对应 [#128](https://github.com/MightyBubble/Ludots/issues/128)。实现 SSOT。与 Story Runtime（#1083）共用同一套文案/肖像出口。

## 1. 概述

玩家侧要看到：稳定玩法 id 变成可读文案，对话/检视面板出现真实肖像，而不是字母方块。引擎侧在既有 `PresentationTextCatalog` 之上补两层，禁止平行字典与渲染器私有捷径。

- **Semantic Map**：`domain + key` → `textToken`（最终仍走 locale）
- **Image Asset**：`imageId` → 正式 2D 资产（mod VFS 路径）；缺资产时允许声明 `glyphFallback`，不是唯一合同
- **换肤**：故事前端与数值面板共用 `panelTheme` 主题包（CSS + images + fonts）；皮/主题正交，数据驱动、零代码换皮

## 2. 结构

```text
配置
  Presentation/text_tokens.json          既有
  Presentation/text_locales.json         既有
  Presentation/semantic_maps.json        domain+key → textToken
  Presentation/image_assets.json         imageId → kind + path (+ 可选 glyphFallback)
  Story/speakers.json                    speakerId → displayNameToken + portraitImageId
  PanelThemes/themes.json                主题包目录（与火球面板同一套）
  game.json panelTheme                   全局主题选择

运行时
  PresentationTextCatalog                文案唯一出口
  PresentationSemanticMapCatalog         语义映射
  PresentationImageAssetCatalog          2D 资产描述
  PresentationDisplayResolver            统一解析：语义文案 / 图像绝对路径或 glyph URI
  Story speakers registry                说话者目录
  NarrativeFrontend + PanelThemeCatalog  对话表面挂主题样式表；肖像位 Ui.Image
```

## 3. 详情

### 3.1 Semantic Map

```json
{
  "id": "map.speaker.warden",
  "domain": "speaker",
  "key": "speaker.warden",
  "textToken": "story.speaker.warden.name"
}
```

`domain` 枚举：`speaker` | `attribute` | `relation` | `enum` | `tag` | `generic`。  
消费者：`PresentationDisplayResolver.ResolveMappedText(domain, key)` → 经 locale 格式化。禁止把最终本地化串写进玩法组件。

### 3.2 Image Asset

```json
{
  "id": "portrait.speaker.warden",
  "kind": "portrait",
  "path": "NarrativeShowcaseMod:assets/Presentation/portraits/warden.png",
  "glyphFallback": "WM"
}
```

`kind`：`portrait` | `badge` | `card` | `icon` | `standing`。  
`path` 经 VFS 解析为绝对路径后喂 `Ui.Image`（与 `UiImageSourceCache` 合同一致）。  
`path` 缺失或文件不存在：若声明了 `glyphFallback` 则生成 data-URI SVG；否则 fail-closed（对话肖像位留空仅当 speaker 未声明 portraitImageId）。

### 3.3 Story Speakers

```json
{
  "id": "speaker.warden",
  "displayNameToken": "story.speaker.warden.name",
  "portraitImageId": "portrait.speaker.warden",
  "standingImageId": "standing.speaker.warden"
}
```

`DialogueView` 暴露 `ResolvedSpeakerName`、`PortraitImageSrc`、`StandingImageSrc`。展示层禁止再维护 `speakerLabels` 明文表。半屏全身立绘 profile（`story.standing_portrait`）必须解析 `standingImageId`，禁止用 bust 肖像顶替。

### 3.4 换肤（故事表面）

- 复用 `PanelThemeCatalog.TryLoad`（`game.json` → `panelTheme`）
- `NarrativeFrontend` 发布 Overlay 时挂载主题 `StyleSheet`
- Composer 给表面打稳定 class：`.story-surface` / `.story-overlay-dialogue` / `.story-portrait` / `.story-choice-list` …
- 主题包可覆盖背景切图、边框、字体；换主题 = 改 `panelTheme` 一行，不写 C#

不把对话塞进 PanelHost 数值模板；数据面仍是 NarrativeFrontend，只共享主题轴。

### 3.5 Entity Insight 校验面

`insight_profiles` 增加可选 `portraitImageId`。有则走 Image Asset；无则保留 `portraitGlyph`。同一 `PresentationDisplayResolver` 服务两个表面。

## 4. 场景

| 场景 | 玩家看到 | 系统 |
|------|----------|------|
| 对看守说话 | 底部对话框左侧有看守肖像，名字本地化 | speakers + image + textToken |
| 换主题 | 对话框材质/字体变了，台词与肖像不变 | panelTheme |
| 检视单位 | 信息卡肖像是图，不是字母 | portraitImageId |
| 切 locale | 说话者名与台词同步换语言 | semantic/text locale |

## 5. 边界

- 不新建第二套本地化系统
- 不把最终文案写进 Dialogue 节点
- 不把肖像绑死某一渲染器私有 atlas
- glyph 只作缺图兜底，不是主合同
- 世界 3D 角色网格不在 #128 范围；本切片保证 2D 肖像与面板产品观感

## 6. UAT

```gherkin
Feature: 语义文案与肖像资产

  Scenario: 对话显示本地化名字和肖像
    Given speaker.warden 声明了 displayNameToken 与 portraitImageId
    And 对应 image asset 与 locale 已注册
    When 玩家进入该说话者的对话节点
    Then Overlay 标题显示 locale 解析后的名字
    And 肖像位显示 2D 图像而非字母块

  Scenario: 缺图时 glyph 兜底
    Given image asset 声明 glyphFallback 且 path 文件缺失
    When 解析肖像
    Then 使用 glyph 生成的 data-URI
    And 不静默空过

  Scenario: 主题换皮不改数据
    Given showcase 使用 panelTheme story-ember
    When 将 panelTheme 换成另一已注册主题
    Then 对话表面样式跟随主题包变化
    And 台词、选项、肖像资产 id 不变

  Scenario: 同一合同服务第二表面
    Given EntityInsight profile 声明 portraitImageId
    When 打开实体信息卡
    Then 肖像走 PresentationImageAssetCatalog
```
