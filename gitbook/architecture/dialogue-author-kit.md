# 关口口令：对话作者入门

对应 showcase：`dialogue_author_kit`（换肤壳 `dialogue_author_kit_theme_ink`）。

实现与合同深材料：`docs/architecture/story_runtime_dialogue_sequencer.md`。本页只讲**作者怎么配**，不另开平行运行时。

## 1. 概述

你要做一段能玩的对话：有分支、能查地图变量、某一句会写地图变量，调试时还想快速换皮。框架里不写第二套对话解释器，按现成合同挂即可：

| 你想要的 | 配什么 |
|----------|--------|
| 台词与说话者 | `Story/lines.json` + `Story/speakers.json` + `Presentation/text_*` |
| 对话树与选项 | `Dialogue/dialogues.json` |
| 选项是否出现 | Query 图（`conditionGraphId`）读 MapVar |
| 选完改世界 | TriggerGraph（`actionGraphId`）写 MapVar |
| 长什么样 | `presentationProfile` + `Frontend/narrative_frontend.json` + `panelTheme` |

本案例玩法：**关口门卫**。先把口令写进簿子（`pass_granted=1`），「请放行」才会出现；改一行 `panelTheme` 或启动墨色壳就能换皮。

## 2. 结构

```text
DialogueAuthorKitShowcaseMod/assets/
  game.json                         startupMapId + panelTheme
  config_catalog.json               注册 Dialogue/Story/GAS/Presentation/PanelThemes
  Maps/dialogue_author_kit_gate.json   Variables: pass_granted
  Dialogue/dialogues.json
  Story/{lines,speakers,presentation_profiles}.json
  GAS/graphs.json                   Query + TriggerGraph
  Presentation/{text_tokens,text_locales,image_assets}.json
  Frontend/narrative_frontend.json  眉题、选项栏、变量 HUD
  PanelThemes/{themes.json,kit-amber,kit-ink}
  Input/default_input.json          回车 / 1/2/3

DialogueAuthorKitThemeInkMod/       0 代码壳，只改 panelTheme=kit-ink
```

运行时薄层：`StartDialogue` + `StoryPresentationProjector` → NarrativeFrontend；C# 不写分支逻辑。

## 3. 详情

### 3.1 地图变量

在地图 JSON 声明，不要另建 `Narrative/variables.json`：

```json
"Variables": [{ "name": "pass_granted", "type": "int", "initial": 0 }]
```

### 3.2 图

- **Query** `Graph.AuthorKit.Condition.PassGranted`：`ReadMapVarInt` → `HaltReturnInt`。返回值 ≠ 0 才显示选项。
- **TriggerGraph** `Graph.AuthorKit.Action.GrantPass`：`WriteMapVarInt` 把 `pass_granted` 写成 1，单切片必须 Halt。

### 3.3 对话节点

入口 `open` 三个选项：

1. `write_pass` → `actionGraphId: GrantPass` → `recorded`
2. `ask_enter` → `conditionGraphId: PassGranted` → `allowed`（没写过口令时看不到）
3. `leave` → `bye`

`presentationProfile` 用 `story.dialogue_overlay`（本包自带一份最小 profile）。

### 3.4 UI

- 内容：投影器吐字符串袋 + `imageId`；Frontend 解析路径。
- 皮：`narrative_frontend.json` 管眉题/锚点；`PanelThemes/*/theme.css` 管颜色边框。
- 换肤：改 `game.json` 的 `"panelTheme": "kit-ink"`，或启动 `dialogue_author_kit_theme_ink`（零代码壳）。

### 3.5 启动对话

配置不会自己弹出对话。本案例在地图聚焦后调用一次 `DialogueRuntime.StartDialogue("Dialogue.AuthorKit.Gate")`。你的 Mod 同样需要一个启动点（交互 / 任务 / 地图事件均可）。

## 4. 场景

- 旅人到关口 → 自动开对话 → 选「写进簿子」→ 左上角 `口令已记` 变为 1 → 回到选项后出现「请放行」→ 门卫放行。
- 调试换皮：同一玩法，启动墨色壳，对话框从暖琥珀变成冷墨色。

## 5. 边界

- 不新增对话 VM、不写平行变量表、不把 Graph id 塞进 Frontend。
- 条件只走 Query；写状态只走 TriggerGraph。
- 缺 `presentationProfile`、缺图、缺 `standingImageId`（立绘 profile）一律 fail-closed。
- 本案例不演示世界气泡 / Sequencer / 任务链；那些看 Narrative 旗舰 showcase。

## 6. UAT

```gherkin
Feature: 关口口令作者案例

  Scenario: 先写口令再放行
    Given 地图变量 pass_granted 初始为 0
    When 对话打开且玩家选择「先把口令写进簿子」
    Then pass_granted 变为 1
    And 回到问话后玩家能看到「请放行」

  Scenario: 未写口令看不到放行
    Given pass_granted 仍为 0
    When 对话打开在 open 节点
    Then 选项列表不含「请放行」

  Scenario: 一行换皮
    Given 玩法 Mod 已注册 kit-amber 与 kit-ink
    When 启动 dialogue_author_kit_theme_ink 或把 panelTheme 改成 kit-ink
    Then 对话表面使用墨色主题样式
```

## 启动

- 预设：`dialogue_author_kit_raylib`
- 墨色壳：`dialogue_author_kit_theme_ink_raylib`
