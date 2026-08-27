# 关口口令：对话作者入门

对应 showcase：`dialogue_author_kit`（换肤壳 `dialogue_author_kit_theme_ink`）。

实现与合同深材料：`docs/architecture/story_runtime_dialogue_sequencer.md`。本页只讲**作者怎么配**——下面每一节都是仓库里真实文件的正文，并写明它顶住哪条玩法需求。

## 1. 概述

你要做一段能玩的关口对话：

1. 进地图就开聊（作者不写 C#）。
2. 先把口令写进簿子（`pass_granted = 1`），「请放行」才会出现。
3. 调试时改一行主题名就能换皮。

**作者只配 JSON / 图 / 主题。**  
对话有了之后，`NarrativeFrontendMod` 内置桥负责：读当前句 → 投影成字符串袋 → 套 chrome → 发到 Overlay。进图自动开聊，写在 `Frontend/narrative_hosts.json` 的 `bootstrap.startDialogueId`。

| 你想要的 | 看哪份配置 |
|----------|------------|
| 进哪张图、默认什么皮 | `game.json` |
| 有哪些配置要加载 | `config_catalog.json` |
| 谁说什么字 | `Presentation/text_locales.json`（字）+ `Story/lines.json`（句）+ `Story/speakers.json`（人） |
| 对话树、选项、写口令 / 条件放行 | `Dialogue/dialogues.json` + `GAS/graphs.json` |
| 簿子上的变量初值 | `Maps/dialogue_author_kit_gate.json` |
| 自动开聊、眉题、变量 HUD、按键上下文 | `Frontend/narrative_hosts.json` + `Input/default_input.json` |
| 换皮 | `PanelThemes/` + `game.json` 的 `panelTheme`（或墨色壳） |

## 2. 结构

仓库根相对路径。本案例是**零代码内容 Mod**（无 `main`、无 DLL）。

```text
mods/showcases/dialogue_author_kit/DialogueAuthorKitShowcaseMod/
├── mod.json
└── assets/
    ├── game.json
    ├── config_catalog.json
    ├── Dialogue/dialogues.json
    ├── Story/{lines,speakers,presentation_profiles}.json
    ├── GAS/graphs.json
    ├── Maps/dialogue_author_kit_gate.json
    ├── Entities/templates.json
    ├── Presentation/{text_tokens,text_locales,image_assets}.json + portraits/
    ├── Frontend/narrative_hosts.json
    ├── PanelThemes/{themes.json,kit-amber/,kit-ink/}
    └── Input/default_input.json

mods/showcases/dialogue_author_kit_theme_ink/DialogueAuthorKitThemeInkMod/
├── mod.json
└── assets/game.json                    # 只覆盖 panelTheme=kit-ink
```

内置桥（作者不用抄）：`NarrativeFrontendMod` 的 `NarrativeStoryBridgeSystem` + `NarrativeFrontendHostCatalog`。

## 3. 详情：每份配置正文怎么顶需求

读法：先看「顶什么」→ 再看正文 → 再看「和别的文件怎么咬」。

### 3.1 `mod.json` — 这是内容包，不挂 DLL

**顶什么：** 声明依赖 NarrativeFrontend；**不要**再写 `main` 指向 showcase Runtime。

```json
{
  "name": "DialogueAuthorKitShowcaseMod",
  "version": "1.0.0",
  "description": "关口口令：纯配置作者入门案例（对话/图/换肤）。UI 桥路由 NarrativeFrontendMod 内置。",
  "priority": 0,
  "dependencies": {
    "LudotsCoreMod": "^1.0.0",
    "CoreInputMod": "^1.0.0",
    "CameraProfilesMod": "^1.0.0",
    "NarrativeFrontendMod": "^1.0.0"
  },
  "author": "Ludots Team",
  "tags": ["dialogue", "mapvar", "panelTheme", "showcase", "author-kit"]
}
```

### 3.2 `assets/game.json` — 进哪张图、默认哪张皮

**顶什么：** 启动就进关口地图；默认琥珀色皮，方便对照墨色壳。

```json
{
  "startupMapId": "dialogue_author_kit_gate",
  "panelTheme": "kit-amber",
  "startupLocalSeats": [
    {
      "seatId": "seat.0",
      "playerId": 1
    }
  ]
}
```

咬合：`startupMapId` = 地图文件的 `Id`；`panelTheme` = `PanelThemes/themes.json` 里的主题 id。

### 3.3 `assets/config_catalog.json` — 告诉管线「这些表要合并进来」

**顶什么：** 不登记的路径不会进配置。hosts 必须用 `ownerId` 作主键。

```json
[
  { "Path": "GAS/graphs.json", "Policy": "ArrayById", "IdField": "id" },
  { "Path": "Story/lines.json", "Policy": "ArrayById", "IdField": "id" },
  { "Path": "Story/presentation_profiles.json", "Policy": "ArrayById", "IdField": "id" },
  { "Path": "Story/speakers.json", "Policy": "ArrayById", "IdField": "id" },
  { "Path": "Dialogue/dialogues.json", "Policy": "ArrayById", "IdField": "id" },
  { "Path": "Presentation/text_tokens.json", "Policy": "ArrayById", "IdField": "id" },
  { "Path": "Presentation/text_locales.json", "Policy": "DeepObject" },
  { "Path": "Presentation/image_assets.json", "Policy": "ArrayById", "IdField": "id" },
  { "Path": "PanelThemes/themes.json", "Policy": "ArrayById", "IdField": "id" },
  { "Path": "Frontend/narrative_hosts.json", "Policy": "ArrayById", "IdField": "ownerId" }
]
```

### 3.4 `Maps/dialogue_author_kit_gate.json` — 簿子初值 + 场上有个旅人

**顶什么：**

- 变量 `pass_granted` 初始为 `0`（没写口令时，「请放行」不该出现）。
- 场上有玩家席位与旅人实体，输入与队伍才站得住。

```json
{
  "Id": "dialogue_author_kit_gate",
  "Tags": ["dialogue.author_kit", "mapvar", "panelTheme"],
  "Variables": [
    { "name": "pass_granted", "type": "int", "initial": 0 }
  ],
  "DefaultCamera": {
    "VirtualCameraId": "Camera.Profile.Tactical",
    "TargetXCm": 1200,
    "TargetYCm": 900,
    "DistanceCm": 3200,
    "Pitch": 48,
    "FovYDeg": 42
  },
  "Entities": [
    {
      "Template": "author_kit_traveler",
      "InstanceId": "author_kit_traveler_1",
      "Overrides": {
        "Name": { "Value": "Traveler" },
        "PlayerOwner": { "PlayerId": 1 },
        "WorldPositionCm": { "Value": { "X": 1200, "Y": 900 } }
      }
    }
  ],
  "Teams": [
    { "TeamId": 1, "RepresentativeInstanceId": "author_kit_traveler_1" }
  ],
  "Players": [
    { "PlayerId": 1, "TeamId": 1, "RepresentativeInstanceId": "author_kit_traveler_1" }
  ]
}
```

咬合：变量名必须和图里的 `var: "pass_granted"`、hosts 里的 `variableId` 同一串字。

### 3.5 `Entities/templates.json` — 旅人模板

**顶什么：** 地图 `Entities[].Template` 能解析到实体组件骨架。

```json
[
  {
    "id": "author_kit_traveler",
    "components": {
      "Name": { "Value": "Traveler" },
      "Team": { "Id": 1 },
      "PlayerOwner": { "PlayerId": 1 },
      "CommandSourceSelectableTag": {},
      "WorldPositionCm": { "Value": { "X": 1200, "Y": 900 } }
    }
  }
]
```

### 3.6 `Presentation/text_locales.json` — 玩家真正看到的字

**顶什么：** 台词与人名。改字只改这里；不要把中文硬塞进 dialogues。

```json
{
  "defaultLocale": "zh-CN",
  "locales": {
    "zh-CN": {
      "story.author_kit.speaker.guard": "关口门卫",
      "story.author_kit.speaker.traveler": "旅人",
      "story.author_kit.guard.open": "站住。过关要口令。你是先在簿子上记下口令，还是直接报上来？",
      "story.author_kit.player.write_pass": "先把口令写进簿子。",
      "story.author_kit.player.ask_enter": "口令在簿子上，请放行。",
      "story.author_kit.player.leave": "我再转转。",
      "story.author_kit.guard.recorded": "簿子上有了。再跟我说一次，就能过。",
      "story.author_kit.player.back": "好，我再说一遍。",
      "story.author_kit.guard.allowed": "口令对上了。进去吧。",
      "story.author_kit.guard.bye": "行，关口还在这儿。"
    },
    "en-US": {
      "story.author_kit.speaker.guard": "Gate Guard",
      "story.author_kit.speaker.traveler": "Traveler",
      "story.author_kit.guard.open": "Hold. You need a pass-phrase. Log it first, or try to enter now?",
      "story.author_kit.player.write_pass": "Write the pass-phrase into the ledger.",
      "story.author_kit.player.ask_enter": "The ledger has it. Let me through.",
      "story.author_kit.player.leave": "I will walk around first.",
      "story.author_kit.guard.recorded": "It is in the ledger. Speak to me again and you may pass.",
      "story.author_kit.player.back": "Alright, I will speak again.",
      "story.author_kit.guard.allowed": "Pass-phrase matches. Go on.",
      "story.author_kit.guard.bye": "Fine. The gate stays."
    }
  }
}
```

### 3.7 `Presentation/text_tokens.json` — 文案 token 登记

**顶什么：** 每个会出现在 locales 里的 id 先登记（`argCount` 本案例全是 0）。

```json
[
  { "id": "story.author_kit.speaker.guard", "argCount": 0 },
  { "id": "story.author_kit.speaker.traveler", "argCount": 0 },
  { "id": "story.author_kit.guard.open", "argCount": 0 },
  { "id": "story.author_kit.player.write_pass", "argCount": 0 },
  { "id": "story.author_kit.player.ask_enter", "argCount": 0 },
  { "id": "story.author_kit.player.leave", "argCount": 0 },
  { "id": "story.author_kit.guard.recorded", "argCount": 0 },
  { "id": "story.author_kit.player.back", "argCount": 0 },
  { "id": "story.author_kit.guard.allowed", "argCount": 0 },
  { "id": "story.author_kit.guard.bye", "argCount": 0 }
]
```

### 3.8 `Story/speakers.json` — 谁在说话、用哪张脸

**顶什么：** 门卫 / 旅人显示名 token + 肖像 id。

```json
[
  {
    "id": "speaker.guard",
    "displayNameToken": "story.author_kit.speaker.guard",
    "portraitImageId": "portrait.speaker.guard"
  },
  {
    "id": "speaker.traveler",
    "displayNameToken": "story.author_kit.speaker.traveler",
    "portraitImageId": "portrait.speaker.traveler"
  }
]
```

### 3.9 `Presentation/image_assets.json` — 肖像文件路径

**顶什么：** 把 `portraitImageId` 解成真实图片（前端边界解析，不进对话后端）。

```json
[
  {
    "id": "portrait.speaker.guard",
    "kind": "Portrait",
    "path": "DialogueAuthorKitShowcaseMod:assets/Presentation/portraits/guard.png",
    "glyphFallback": "GG"
  },
  {
    "id": "portrait.speaker.traveler",
    "kind": "Portrait",
    "path": "DialogueAuthorKitShowcaseMod:assets/Presentation/portraits/traveler.png",
    "glyphFallback": "TR"
  }
]
```

### 3.10 `Story/lines.json` — 一句台词 = 谁 + 哪个文案 token

**顶什么：** 对话节点只挂 `lineId`；这里把句绑到 speaker 与 textToken。

```json
[
  {
    "id": "line.author_kit.guard.open",
    "speakerId": "speaker.guard",
    "textToken": "story.author_kit.guard.open",
    "tags": ["gate"]
  },
  {
    "id": "line.author_kit.player.write_pass",
    "speakerId": "speaker.traveler",
    "textToken": "story.author_kit.player.write_pass",
    "tags": ["choice"]
  },
  {
    "id": "line.author_kit.player.ask_enter",
    "speakerId": "speaker.traveler",
    "textToken": "story.author_kit.player.ask_enter",
    "tags": ["choice"]
  },
  {
    "id": "line.author_kit.player.leave",
    "speakerId": "speaker.traveler",
    "textToken": "story.author_kit.player.leave",
    "tags": ["choice"]
  },
  {
    "id": "line.author_kit.guard.recorded",
    "speakerId": "speaker.guard",
    "textToken": "story.author_kit.guard.recorded",
    "tags": ["gate"]
  },
  {
    "id": "line.author_kit.player.back",
    "speakerId": "speaker.traveler",
    "textToken": "story.author_kit.player.back",
    "tags": ["choice"]
  },
  {
    "id": "line.author_kit.guard.allowed",
    "speakerId": "speaker.guard",
    "textToken": "story.author_kit.guard.allowed",
    "tags": ["gate"]
  },
  {
    "id": "line.author_kit.guard.bye",
    "speakerId": "speaker.guard",
    "textToken": "story.author_kit.guard.bye",
    "tags": ["gate"]
  }
]
```

### 3.11 `Story/presentation_profiles.json` — 这句用哪种表面

**顶什么：** 本案例全部走屏幕 Overlay 对话表面（等输入、压暗背景）。颜色会被 PanelTheme CSS 再盖一层。

```json
[
  {
    "id": "story.dialogue_overlay",
    "backend": "ScreenOverlay",
    "surfaceKind": "OverlayDialogue",
    "anchor": "BottomCenter",
    "width": 760,
    "offsetY": 28,
    "waitForInput": true,
    "dimBackdrop": true,
    "accentHex": "#F6C56B",
    "backgroundHex": "#08111BEF",
    "borderHex": "#5AD7E9FF",
    "foregroundHex": "#F8FAFC",
    "mutedHex": "#C6D3DE"
  }
]
```

### 3.12 `GAS/graphs.json` — 写口令 / 查口令（玩法核心）

**顶什么：**

| 图 | 种类 | 玩家侧结果 |
|----|------|------------|
| `Graph.AuthorKit.Condition.PassGranted` | Query | 读 `pass_granted`；≠0 才让「请放行」出现 |
| `Graph.AuthorKit.Action.GrantPass` | TriggerGraph | 把 `pass_granted` 写成 `1` |

```json
[
  {
    "id": "Graph.AuthorKit.Condition.PassGranted",
    "kind": "Query",
    "entry": "read_pass",
    "nodes": [
      { "id": "read_pass", "op": "ReadMapVarInt", "var": "pass_granted" },
      { "id": "halt", "op": "HaltReturnInt" }
    ],
    "controlEdges": [
      { "from": "read_pass", "fromPort": "next", "to": "halt" }
    ],
    "valueEdges": [
      { "from": "read_pass", "fromPort": "value", "to": "halt", "toPort": "value" }
    ]
  },
  {
    "id": "Graph.AuthorKit.Action.GrantPass",
    "kind": "TriggerGraph",
    "entries": [
      { "label": "story_invoke", "event": "Story.ManualInvoke", "start": "scope", "once": true }
    ],
    "nodes": [
      { "id": "scope", "op": "LoadCaster" },
      { "id": "one", "op": "ConstInt", "intValue": 1 },
      { "id": "write", "op": "WriteMapVarInt", "var": "pass_granted" },
      { "id": "ok", "op": "ConstInt", "intValue": 1 },
      { "id": "halt", "op": "HaltReturnInt" }
    ],
    "controlEdges": [
      { "from": "scope", "fromPort": "next", "to": "one" },
      { "from": "one", "fromPort": "next", "to": "write" },
      { "from": "write", "fromPort": "next", "to": "ok" },
      { "from": "ok", "fromPort": "next", "to": "halt" }
    ],
    "valueEdges": [
      { "from": "scope", "fromPort": "value", "to": "write", "toPort": "source" },
      { "from": "one", "fromPort": "value", "to": "write", "toPort": "value" },
      { "from": "ok", "fromPort": "value", "to": "halt", "toPort": "value" }
    ]
  }
]
```

不新造枚举：只组合既有 `ReadMapVarInt` / `WriteMapVarInt`。

### 3.13 `Dialogue/dialogues.json` — 对话树（把图挂到选项上）

**顶什么：**

1. 开场三选：写口令（带 `actionGraphId`）/ 请放行（带 `conditionGraphId`）/ 离开。
2. 写完进 `recorded`，再回 `open`；此时条件过了，「请放行」才出现。
3. 放行进 `allowed`；离开进 `bye`。

```json
[
  {
    "id": "Dialogue.AuthorKit.Gate",
    "displayName": "关口口令",
    "entryNode": "open",
    "nodes": [
      {
        "id": "open",
        "lineId": "line.author_kit.guard.open",
        "presentationProfile": "story.dialogue_overlay",
        "choices": [
          {
            "id": "write_pass",
            "lineId": "line.author_kit.player.write_pass",
            "actionGraphId": "Graph.AuthorKit.Action.GrantPass",
            "nextNode": "recorded"
          },
          {
            "id": "ask_enter",
            "lineId": "line.author_kit.player.ask_enter",
            "conditionGraphId": "Graph.AuthorKit.Condition.PassGranted",
            "nextNode": "allowed"
          },
          {
            "id": "leave",
            "lineId": "line.author_kit.player.leave",
            "nextNode": "bye"
          }
        ]
      },
      {
        "id": "recorded",
        "lineId": "line.author_kit.guard.recorded",
        "presentationProfile": "story.dialogue_overlay",
        "choices": [
          {
            "id": "back_to_open",
            "lineId": "line.author_kit.player.back",
            "nextNode": "open"
          }
        ]
      },
      {
        "id": "allowed",
        "lineId": "line.author_kit.guard.allowed",
        "presentationProfile": "story.dialogue_overlay"
      },
      {
        "id": "bye",
        "lineId": "line.author_kit.guard.bye",
        "presentationProfile": "story.dialogue_overlay"
      }
    ]
  }
]
```

咬合：`id` 必须等于 hosts 的 `bootstrap.startDialogueId`。选项上只挂图 id 与 `nextNode`，不在 Frontend 配置里塞 Graph。

### 3.14 `Frontend/narrative_hosts.json` — 自动开聊 + 眉题 + 变量 HUD

**顶什么：** 作者侧唯一前端挂靠。进 `dialogue_author_kit_gate` 就开 `Dialogue.AuthorKit.Gate`，推输入上下文，叠 Overlay / 选项列表 /「口令已记」面板。

```json
[
  {
    "ownerId": "DialogueAuthorKit",
    "activeMapId": "dialogue_author_kit_gate",
    "backdropHex": "#05080FCC",
    "bootstrap": {
      "startDialogueId": "Dialogue.AuthorKit.Gate",
      "inputContextId": "DialogueAuthorKit.Controls"
    },
    "promptRibbon": {
      "anchor": "BottomCenter",
      "width": 920,
      "offsetY": 150,
      "zIndex": 55,
      "title": "关口",
      "accentHex": "#F6C56B"
    },
    "variablesPanel": {
      "anchor": "TopLeft",
      "width": 360,
      "zIndex": 41,
      "eyebrow": "簿子",
      "title": "地图变量",
      "footer": "写口令会改 pass_granted。",
      "accentHex": "#A7F3D0"
    },
    "overlayDialogue": {
      "anchor": "BottomCenter",
      "width": 760,
      "offsetY": 28,
      "zIndex": 60,
      "eyebrow": "交谈",
      "footer": "回车继续 · 1/2/3 选回复",
      "accentHex": "#F6C56B"
    },
    "choiceList": {
      "anchor": "BottomRight",
      "width": 440,
      "offsetY": 12,
      "zIndex": 61,
      "eyebrow": "回复",
      "title": "你怎么说？",
      "footer": "",
      "accentHex": "#F6C56B"
    },
    "hints": {
      "promptTitle": "关口",
      "explorePrompt": "对话会自动开始。先写口令，再选放行。",
      "choicePrompt": "按 1/2/3 选择，或回车继续。",
      "skinHint": "换皮：改 game.json 的 panelTheme，或启动 dialogue_author_kit_theme_ink。"
    },
    "variables": [
      {
        "variableId": "pass_granted",
        "label": "口令已记",
        "accentHex": "#A7F3D0"
      }
    ]
  }
]
```

### 3.15 `Input/default_input.json` — 回车继续、1/2/3 选回复

**顶什么：** 定义 `DialogueAuthorKit.Controls`；hosts 的 `inputContextId` 指向它，桥会激活。

```json
{
  "actions": [
    { "id": "StoryAdvance", "name": "Story_Advance", "type": "Button" },
    { "id": "StoryChoice1", "name": "Story_Choice1", "type": "Button" },
    { "id": "StoryChoice2", "name": "Story_Choice2", "type": "Button" },
    { "id": "StoryChoice3", "name": "Story_Choice3", "type": "Button" }
  ],
  "contexts": [
    {
      "id": "DialogueAuthorKit.Controls",
      "name": "Dialogue Author Kit Controls",
      "priority": 110,
      "bindings": [
        { "actionId": "StoryAdvance", "path": "<Keyboard>/enter" },
        { "actionId": "StoryChoice1", "path": "<Keyboard>/1" },
        { "actionId": "StoryChoice2", "path": "<Keyboard>/2" },
        { "actionId": "StoryChoice3", "path": "<Keyboard>/3" }
      ]
    }
  ]
}
```

### 3.16 `PanelThemes/` — 琥珀色默认皮 / 墨色对照皮

**顶什么：** 调试换肤。`themes.json` 登记 id → css；`game.json` 的 `panelTheme` 选中哪张。

`PanelThemes/themes.json`：

```json
[
  {
    "id": "kit-amber",
    "root": "DialogueAuthorKitShowcaseMod:assets/PanelThemes/kit-amber/theme.css"
  },
  {
    "id": "kit-ink",
    "root": "DialogueAuthorKitShowcaseMod:assets/PanelThemes/kit-ink/theme.css"
  }
]
```

`kit-amber/theme.css`：暖琥珀边框与衬底（默认）。  
`kit-ink/theme.css`：冷墨色边框与衬底（对照）。

### 3.17 墨色壳 `dialogue_author_kit_theme_ink` — 零代码换皮

**顶什么：** 不改对话与图，只覆盖 `panelTheme`。

壳 `mod.json` 依赖内容包；壳 `assets/game.json`：

```json
{
  "windowTitle": "Ludots - 关口口令 · 墨色皮",
  "windowWidth": 1920,
  "windowHeight": 1080,
  "panelTheme": "kit-ink"
}
```

## 4. 场景

### 4.1 一条需求怎么拆到文件

| 玩家看到的结果 | 谁负责 |
|----------------|--------|
| 进图就开聊 | hosts.`bootstrap.startDialogueId` + 地图 id |
| 门卫说「站住…」 | locales 字 → lines → dialogues.`open` |
| 选「写进簿子」后 HUD 变 1 | dialogues.`actionGraphId` → GrantPass → MapVar；hosts.`variables` 显示 |
| 回来才看见「请放行」 | dialogues.`conditionGraphId` → PassGranted Query |
| 回车 / 数字键 | Input 上下文 + hosts.`inputContextId` |
| 皮从琥珀变墨 | `panelTheme` 或启动墨色壳 |

### 4.2 推荐游玩路径

1. 启动 `dialogue_author_kit_raylib` → 进关口 → 自动开聊。
2. 先选「写进簿子」→ 左上「口令已记」变 1 → 「再说一遍」回到开场。
3. 此时出现「请放行」→ 门卫放行。
4. 另开 `dialogue_author_kit_theme_ink_raylib` → 同一玩法，冷墨色皮。

## 5. 边界

- 不新增对话 VM；条件用 Query，写入用 TriggerGraph。
- 不在 Frontend 配置里塞 Graph id / nextNode。
- 作者 showcase **不要**再写 Runtime / PresentationSystem；旗舰 Narrative 仍可自带 C#（靠近、Sequencer、世界气泡等本包没有的编排）。
- 地图 / Input 等未进 `config_catalog` 的路径，走各自既有加载约定，不要重复登记乱策略。

## 6. UAT

```gherkin
Feature: 关口口令作者案例

  Scenario: 纯配置就能开聊
    Given 内容 Mod 只有 assets 与 mod.json
    And narrative_hosts 声明了 startDialogueId
    When 玩家启动 dialogue_author_kit 并进入关口地图
    Then 对话自动开始
    And 玩家能看到门卫开场台词与选项列表

  Scenario: 先写口令再放行
    Given 簿子上 pass_granted 初始为 0
    And 开场选项里暂时没有「请放行」或该选项不可用
    When 玩家选择「先把口令写进簿子」
    Then 左上「口令已记」变为 1
    And 玩家回到问话后能看到「请放行」
    When 玩家选择「请放行」
    Then 门卫说出放行台词

  Scenario: 一行换皮
    When 玩家启动 dialogue_author_kit_theme_ink
    Then 对话表面使用墨色主题
    And 对话树与口令规则与琥珀色包相同
```

## 启动

- 琥珀默认：`dialogue_author_kit_raylib`
- 墨色壳：`dialogue_author_kit_theme_ink_raylib`
- 验收：`DialogueAuthorKitAcceptanceTests`
