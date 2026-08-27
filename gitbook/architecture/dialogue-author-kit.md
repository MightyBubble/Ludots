# 关口口令：对话作者入门

对应 showcase：`dialogue_author_kit`（换肤壳 `dialogue_author_kit_theme_ink`）。

实现与合同深材料：`docs/architecture/story_runtime_dialogue_sequencer.md`。本页只讲**作者怎么配**。

## 1. 概述

你要做一段能玩的对话：有分支、能查地图变量、某一句会写地图变量，调试时还想快速换皮。

**作者只配 JSON / 图 / 主题，不必写 Runtime C#。**  
对话有了 → `NarrativeFrontendMod` 内置桥会：投影字符串袋 → 套 chrome → 发布到 Overlay。进地图要自动开聊，在 `Frontend/narrative_hosts.json` 写 `bootstrap.startDialogueId`。

| 你想要的 | 配什么 |
|----------|--------|
| 台词与说话者 | `Story/lines.json` + `Story/speakers.json` + `Presentation/text_*` |
| 对话树与选项 | `Dialogue/dialogues.json` |
| 选项是否出现 | Query 图（`conditionGraphId`）读 MapVar |
| 选完改世界 | TriggerGraph（`actionGraphId`）写 MapVar |
| 自动开聊 / 输入 / 眉题 HUD | `Frontend/narrative_hosts.json` |
| 换皮 | `game.json` 的 `panelTheme` + `PanelThemes/` |

本案例玩法：**关口门卫**。先把口令写进簿子（`pass_granted=1`），「请放行」才会出现；改一行 `panelTheme` 或启动墨色壳就能换皮。

## 2. 结构

仓库根相对路径。本案例是**零代码内容 Mod**（无 `main` / 无 DLL）。

### 2.1 完整路径树

```text
mods/showcases/dialogue_author_kit/DialogueAuthorKitShowcaseMod/
├── mod.json                          # 无 main；依赖 NarrativeFrontendMod
└── assets/
    ├── game.json
    ├── config_catalog.json
    ├── Dialogue/
    │   └── dialogues.json
    ├── Story/
    │   ├── lines.json
    │   ├── speakers.json
    │   └── presentation_profiles.json
    ├── GAS/
    │   └── graphs.json
    ├── Maps/
    │   └── dialogue_author_kit_gate.json
    ├── Entities/
    │   └── templates.json
    ├── Presentation/
    │   ├── text_tokens.json
    │   ├── text_locales.json
    │   ├── image_assets.json
    │   └── portraits/
    │       ├── guard.png
    │       └── traveler.png
    ├── Frontend/
    │   └── narrative_hosts.json      # 内置桥：bootstrap + chrome + 变量 HUD
    ├── PanelThemes/
    │   ├── themes.json
    │   ├── kit-amber/
    │   │   └── theme.css
    │   └── kit-ink/
    │       └── theme.css
    └── Input/
        └── default_input.json

mods/showcases/dialogue_author_kit_theme_ink/DialogueAuthorKitThemeInkMod/
├── mod.json
└── assets/
    └── game.json                     # 只覆盖 panelTheme=kit-ink
```

内置能力（作者不用抄）：

```text
mods/capabilities/narrativefrontend/NarrativeFrontendMod/
├── Systems/NarrativeStoryBridgeSystem.cs   # DialogueView → 投影 → Publish
└── Config/NarrativeFrontendHostCatalog.cs  # 读 narrative_hosts.json
```

门户与启动登记（仓库根）：

```text
showcase.registry.json
launcher.config.json
launcher.presets.json
gitbook/architecture/dialogue-author-kit.md
```

### 2.2 每个配置干什么

| 路径 | 作用 |
|------|------|
| `…/mod.json` | Mod 名与依赖；**不要**再挂 showcase Runtime DLL |
| `…/assets/game.json` | `startupMapId`、默认 `panelTheme`、本地席位 |
| `…/assets/config_catalog.json` | 登记 Dialogue/Story/GAS/Presentation/PanelThemes/**hosts** |
| `…/assets/Dialogue/dialogues.json` | 对话树与图引用 |
| `…/assets/Story/lines.json` | `lineId` → speaker + textToken |
| `…/assets/Story/speakers.json` | 说话者肖像 id |
| `…/assets/Story/presentation_profiles.json` | profile → surfaceKind |
| `…/assets/GAS/graphs.json` | Query / TriggerGraph |
| `…/assets/Maps/dialogue_author_kit_gate.json` | 地图变量与席位 |
| `…/assets/Entities/templates.json` | 旅人实体模板 |
| `…/assets/Presentation/*` | 文案与肖像 |
| `…/assets/Frontend/narrative_hosts.json` | **唯一前端挂靠**：`activeMapId`、`bootstrap.startDialogueId`、chrome、变量 HUD |
| `…/assets/PanelThemes/*` | `panelTheme` 皮 |
| `…/assets/Input/default_input.json` | 回车 / 1/2/3（由 hosts 的 `inputContextId` 激活） |

### 2.3 `narrative_hosts.json` 要点

```json
{
  "ownerId": "DialogueAuthorKit",
  "activeMapId": "dialogue_author_kit_gate",
  "bootstrap": {
    "startDialogueId": "Dialogue.AuthorKit.Gate",
    "inputContextId": "DialogueAuthorKit.Controls"
  },
  "overlayDialogue": { "eyebrow": "交谈", "anchor": "BottomCenter", "width": 760 },
  "choiceList": { "title": "你怎么说？" },
  "variables": [{ "variableId": "pass_granted", "label": "口令已记" }]
}
```

catalog 行：`{ "Path": "Frontend/narrative_hosts.json", "Policy": "ArrayById", "IdField": "ownerId" }`。

### 2.4 作者不要写的东西

下列能力已在 `NarrativeFrontendMod` 内置，**showcase 里不要再抄 Runtime / PresentationSystem**：

- 读 `DialogueRuntime` 当前句 → `StoryPresentationProjector`
- 套 hosts chrome → `NarrativeFrontendService.Publish`
- `bootstrap.startDialogueId` 进图开聊
- `bootstrap.inputContextId` 推输入上下文

旗舰 `NarrativeShowcaseMod` 仍自带 C#，因为还有交互靠近、Sequencer、任务 HUD、世界气泡投影等本案例没有的编排。

## 3. 详情

### 3.1 地图变量

路径：`…/assets/Maps/dialogue_author_kit_gate.json`

```json
"Variables": [{ "name": "pass_granted", "type": "int", "initial": 0 }]
```

### 3.2 图

路径：`…/assets/GAS/graphs.json`

- Query `Graph.AuthorKit.Condition.PassGranted`：读 `pass_granted`，≠0 才显示选项
- TriggerGraph `Graph.AuthorKit.Action.GrantPass`：写成 1，单切片 Halt

### 3.3 对话节点

路径：`…/assets/Dialogue/dialogues.json`

1. `write_pass` → `actionGraphId: GrantPass` → `recorded`
2. `ask_enter` → `conditionGraphId: PassGranted` → `allowed`
3. `leave` → `bye`

### 3.4 UI 与换肤

- 内容：内置桥投影；`image_assets.json` 解析肖像
- 眉题布局：`narrative_hosts.json`
- 颜色：`PanelThemes/*/theme.css` + `game.json` `panelTheme`
- 墨色壳：只覆盖 `panelTheme: kit-ink`

## 4. 场景

- 进关口地图 → 自动开对话 → 写口令 → 变量 HUD 变 1 → 出现「请放行」→ 放行
- 启动墨色壳 → 同一玩法，冷墨色皮

## 5. 边界

- 不新增对话 VM；条件 Query、写入 TriggerGraph
- 不在 Frontend 配置里塞 Graph id / nextNode
- 世界气泡 / Sequencer / 任务链：看 Narrative 旗舰，不在本入门包

## 6. UAT

```gherkin
Feature: 关口口令作者案例

  Scenario: 纯配置就能开聊
    Given 内容 Mod 只有 assets 与 mod.json
    And narrative_hosts 声明了 startDialogueId
    When 加载关口地图
    Then 对话自动开始且无需 showcase Runtime C#

  Scenario: 先写口令再放行
    Given pass_granted 初始为 0
    When 玩家选择「先把口令写进簿子」
    Then pass_granted 变为 1
    And 回到问话后能看到「请放行」

  Scenario: 一行换皮
    When 启动 dialogue_author_kit_theme_ink 或 panelTheme=kit-ink
    Then 对话表面使用墨色主题
```

## 启动

- 预设：`dialogue_author_kit_raylib`
- 墨色壳：`dialogue_author_kit_theme_ink_raylib`
- 验收：`DialogueAuthorKitAcceptanceTests`
