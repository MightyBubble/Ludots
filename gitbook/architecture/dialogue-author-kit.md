# 关口口令：对话作者入门

对应 showcase：`dialogue_author_kit`（换肤壳 `dialogue_author_kit_theme_ink`）。

实现与合同深材料：`docs/architecture/story_runtime_dialogue_sequencer.md`。本页只讲**作者怎么配**。

## 1. 概述

你要做一段能玩的关口对话：

1. 进地图就开聊（作者不写 C#）。
2. 先把口令写进簿子（`pass_granted = 1`），「请放行」才会出现。
3. 调试时改一行主题名就能换皮。

**作者只配 JSON / 图 / 主题。**  
进图开聊走正式 TriggerGraph：`MapLoaded` → `StartDialogue`（不是一次性 Frontend hosts 表）。  
对话开了之后，`NarrativeFrontendMod` 只负责把当前句投影到 Overlay。

| 你想要的 | 配什么 |
|----------|--------|
| 台词与说话者 | `Story/lines.json` + `Story/speakers.json` + `Presentation/text_*` |
| 对话树与选项 | `Dialogue/dialogues.json` |
| 进图自动开聊 | 地图 `TriggerGraphs` + `GAS/graphs.json` 里 `MapLoaded` → `StartDialogue` |
| 选项是否出现 | Query 图（`conditionGraphId`）读 MapVar |
| 选完改世界 | TriggerGraph（`actionGraphId`）写 MapVar |
| 换皮 | `game.json` 的 `panelTheme` + `PanelThemes/` |
| 回车 / 1/2/3 | `Input/default_input.json` + `game.json` 的 `startupInputContexts` |

## 2. 结构

```text
DialogueAuthorKitShowcaseMod/
├── mod.json                          # 无 main；依赖 NarrativeFrontendMod
└── assets/
    ├── game.json                     # startupMapId / panelTheme / startupInputContexts
    ├── config_catalog.json
    ├── Dialogue/dialogues.json
    ├── Story/{lines,speakers,presentation_profiles}.json
    ├── GAS/graphs.json               # Boot.StartGate + PassGranted + GrantPass
    ├── Maps/dialogue_author_kit_gate.json   # Variables + TriggerGraphs
    ├── Entities/templates.json
    ├── Presentation/...
    ├── PanelThemes/...
    └── Input/default_input.json
```

## 3. 详情：进图开聊怎么挂

### 3.1 图 `Graph.AuthorKit.Boot.StartGate`

```json
{
  "id": "Graph.AuthorKit.Boot.StartGate",
  "kind": "TriggerGraph",
  "entries": [
    { "label": "on_map_loaded", "event": "MapLoaded", "start": "start", "once": true }
  ],
  "nodes": [
    { "id": "start", "op": "StartDialogue", "dialogueId": "Dialogue.AuthorKit.Gate" },
    { "id": "ok", "op": "ConstInt", "intValue": 1 },
    { "id": "halt", "op": "HaltReturnInt" }
  ],
  "controlEdges": [
    { "from": "start", "fromPort": "next", "to": "ok" },
    { "from": "ok", "fromPort": "next", "to": "halt" }
  ],
  "valueEdges": [
    { "from": "ok", "fromPort": "value", "to": "halt", "toPort": "value" }
  ]
}
```

### 3.2 地图挂载

```json
"TriggerGraphs": [
  {
    "graph": "Graph.AuthorKit.Boot.StartGate",
    "scopeInstanceId": "author_kit_traveler_1"
  }
]
```

### 3.3 口令读写（仍用既有 MapVar op）

- Query `Graph.AuthorKit.Condition.PassGranted`：读 `pass_granted`
- TriggerGraph `Graph.AuthorKit.Action.GrantPass`：写成 `1`（`Story.ManualInvoke`）

### 3.4 对话树

`Dialogue.AuthorKit.Gate`：`write_pass` 挂 GrantPass；`ask_enter` 挂 PassGranted 条件。

### 3.5 输入

`game.json`：

```json
"startupInputContexts": ["DialogueAuthorKit.Controls"]
```

`Input/default_input.json` 定义该上下文的回车 / 1/2/3。

### 3.6 换皮

`panelTheme: kit-amber`；墨色壳只覆盖 `kit-ink`。

## 4. 场景

进关口 → MapLoaded 开聊 → 写口令 → 回开场 → 「请放行」出现 → 放行。

## 5. 边界

- **禁止**再发明 `narrative_hosts` / bootstrap 一类一次性 Frontend schema。
- 进图开聊只用 TriggerGraph + `StartDialogue`。
- 要用 NarrativeFrontend 自动投影对话窗：地图 Tags 加 `narrative.frontend.project`（旗舰自管 UI 的地图不要加，避免双开）。
- 世界气泡 / Sequencer / 任务链：看 Narrative 旗舰，不在本入门包。

## 6. UAT

```gherkin
Feature: 关口口令作者案例

  Scenario: MapLoaded 图开聊
    Given 地图挂了 Graph.AuthorKit.Boot.StartGate
    And 图在 MapLoaded 时执行 StartDialogue
    When 玩家进入关口地图
    Then 对话自动开始且无需 showcase Runtime C#

  Scenario: 先写口令再放行
    Given pass_granted 初始为 0
    When 玩家选择「先把口令写进簿子」
    Then pass_granted 变为 1
    And 回到问话后能看到「请放行」

  Scenario: 一行换皮
    When 启动 dialogue_author_kit_theme_ink 或 panelTheme=kit-ink
    Then 对话表面使用墨色主题

  Scenario: 投影到 Overlay
    Given 地图 Tags 含 narrative.frontend.project
    When 对话已开始
    Then Overlay 出现台词面与选项面
    And 旗舰自管 UI 的地图不加该 Tag
```

## 启动

- 琥珀默认：`dialogue_author_kit_raylib`
- 墨色壳：`dialogue_author_kit_theme_ink_raylib`
- 验收：`DialogueAuthorKitAcceptanceTests`
- `StartDialogue` 节点说明：`gitbook/reference/graph-node-op-wiki/StartDialogue.md`
