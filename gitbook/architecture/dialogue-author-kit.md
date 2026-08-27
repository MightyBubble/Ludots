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

仓库根相对路径。下面是本案例**全部**源文件（不含 `bin/` / `obj/`）。

### 2.1 完整路径树

```text
mods/showcases/dialogue_author_kit/DialogueAuthorKitShowcaseMod/
├── mod.json
├── DialogueAuthorKitShowcaseMod.csproj
├── DialogueAuthorKitShowcaseModEntry.cs
├── DialogueAuthorKitIds.cs
├── Runtime/
│   ├── DialogueAuthorKitRuntime.cs
│   └── DialogueAuthorKitFrontendConfig.cs
├── Systems/
│   └── DialogueAuthorKitPresentationSystem.cs
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
    │   └── narrative_frontend.json
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
    └── game.json
```

门户与启动登记（仓库根）：

```text
showcase.registry.json          # id: dialogue_author_kit / dialogue_author_kit_theme_ink
launcher.config.json            # binding → Mod 路径
launcher.presets.json           # dialogue_author_kit_raylib / …_theme_ink_raylib
gitbook/architecture/dialogue-author-kit.md   # 本页
```

### 2.2 每个配置干什么

| 路径 | 作用 |
|------|------|
| `…/mod.json` | Mod 名、DLL、依赖（Core / CoreInput / CameraProfiles / NarrativeFrontend） |
| `…/assets/game.json` | `startupMapId`、默认 `panelTheme`（本例 `kit-amber`）、本地席位 |
| `…/assets/config_catalog.json` | 告诉加载器要合并哪些 ArrayById / DeepObject 配置行 |
| `…/assets/Dialogue/dialogues.json` | 对话树：节点、选项、`conditionGraphId` / `actionGraphId` / `nextNode` / `presentationProfile` |
| `…/assets/Story/lines.json` | `lineId` → `speakerId` + `textToken` |
| `…/assets/Story/speakers.json` | 说话者 → 显示名 token、`portraitImageId` |
| `…/assets/Story/presentation_profiles.json` | profile → `surfaceKind` / 锚点 / 宽高 / 颜色提示 |
| `…/assets/GAS/graphs.json` | Query（读变量）与 TriggerGraph（写变量） |
| `…/assets/Maps/dialogue_author_kit_gate.json` | 地图、`Variables`、玩家实体绑定、相机 |
| `…/assets/Entities/templates.json` | 地图实体模板（本例旅人席位） |
| `…/assets/Presentation/text_tokens.json` | 文案 token 目录 |
| `…/assets/Presentation/text_locales.json` | zh-CN / en-US 最终可见字符串 |
| `…/assets/Presentation/image_assets.json` | `portrait.*` → VFS 路径 |
| `…/assets/Presentation/portraits/*.png` | 肖像像素 |
| `…/assets/Frontend/narrative_frontend.json` | 眉题、选项栏、变量 HUD、提示文案（前端 chrome） |
| `…/assets/PanelThemes/themes.json` | `panelTheme` id → `theme.css` 根路径 |
| `…/assets/PanelThemes/kit-amber/theme.css` | 暖琥珀皮 |
| `…/assets/PanelThemes/kit-ink/theme.css` | 冷墨色皮 |
| `…/assets/Input/default_input.json` | 回车推进、1/2/3 选选项 |
| 墨色壳 `…ThemeInkMod/mod.json` | 依赖玩法 Mod，无 DLL |
| 墨色壳 `…ThemeInkMod/assets/game.json` | 只覆盖 `panelTheme: kit-ink` |

### 2.3 `config_catalog.json` 登记行（本例）

这些路径都相对于该 Mod 的 `assets/`：

```text
GAS/graphs.json
Story/lines.json
Story/presentation_profiles.json
Story/speakers.json
Dialogue/dialogues.json
Presentation/text_tokens.json
Presentation/text_locales.json
Presentation/image_assets.json
PanelThemes/themes.json
```

地图、实体模板、`game.json`、`Input/default_input.json`、`Frontend/narrative_frontend.json` 走各自加载约定，不进这份 catalog 列表；改对话/图/文案/主题登记时，以 catalog 为准。

### 2.4 C# 薄层（不写分支）

| 路径 | 作用 |
|------|------|
| `DialogueAuthorKitShowcaseModEntry.cs` | 挂 GameStart / MapLoaded / Dialogue 事件 |
| `Runtime/DialogueAuthorKitRuntime.cs` | 聚焦地图后 `StartDialogue`；投影帧 → NarrativeFrontend；HUD 读 MapVar |
| `Runtime/DialogueAuthorKitFrontendConfig.cs` | 反序列化 `Frontend/narrative_frontend.json` |
| `Systems/DialogueAuthorKitPresentationSystem.cs` | 每帧刷新面板 |
| `DialogueAuthorKitIds.cs` | 地图 / 对话 / 变量 / 输入上下文 id |

运行时链路：`StartDialogue` → `StoryPresentationProjector`（字符串袋）→ NarrativeFrontend；分支仍只在 JSON / Graph。

## 3. 详情

### 3.1 地图变量

在地图 JSON 声明，不要另建 `Narrative/variables.json`：

路径：`mods/showcases/dialogue_author_kit/DialogueAuthorKitShowcaseMod/assets/Maps/dialogue_author_kit_gate.json`

```json
"Variables": [{ "name": "pass_granted", "type": "int", "initial": 0 }]
```

### 3.2 图

路径：`…/assets/GAS/graphs.json`

- **Query** `Graph.AuthorKit.Condition.PassGranted`：`ReadMapVarInt` → `HaltReturnInt`。返回值 ≠ 0 才显示选项。
- **TriggerGraph** `Graph.AuthorKit.Action.GrantPass`：`WriteMapVarInt` 把 `pass_granted` 写成 1，单切片必须 Halt。

### 3.3 对话节点

路径：`…/assets/Dialogue/dialogues.json`

入口 `open` 三个选项：

1. `write_pass` → `actionGraphId: Graph.AuthorKit.Action.GrantPass` → `recorded`
2. `ask_enter` → `conditionGraphId: Graph.AuthorKit.Condition.PassGranted` → `allowed`（没写过口令时看不到）
3. `leave` → `bye`

`presentationProfile` 用 `story.dialogue_overlay`（见同包 `Story/presentation_profiles.json`）。

### 3.4 UI

- 内容：投影器吐字符串袋 + `imageId`；Frontend 解析路径（`image_assets.json`）。
- 布局眉题：`Frontend/narrative_frontend.json`。
- 颜色边框：`PanelThemes/*/theme.css`，由 `game.json` 的 `panelTheme` 选中。
- 换肤：改玩法 Mod `assets/game.json` 的 `"panelTheme": "kit-ink"`，或启动墨色壳（只带自己的 `assets/game.json`）。

### 3.5 启动对话

配置不会自己弹出对话。本案例在地图聚焦后调用一次 `DialogueRuntime.StartDialogue("Dialogue.AuthorKit.Gate")`（见 `Runtime/DialogueAuthorKitRuntime.cs`）。你的 Mod 同样需要一个启动点（交互 / 任务 / 地图事件均可）。

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
- 验收类：`DialogueAuthorKitAcceptanceTests`
