# misc-03 配置说明 · 叙事与任务

> 配置写法与行为。第一性需求见 [misc-03 PRD](../prd/misc-03-narrative.md)；编辑器需求见 [UXD](../uxd/misc-03-narrative.md)；现状见 [reference](../reference/misc-03-narrative.md)。

## 1. 示例配置

叙事 showcase 真实四件（`mods/showcases/narrative/NarrativeShowcaseMod/assets/`，节选）：

```json
[
  { "id": "trust", "kind": "Int", "defaultInt": 0, "displayName": "Trust" },
  { "id": "ending", "kind": "String", "defaultString": "Unwritten", "displayName": "Ending" }
]
```

```json
[
  {
    "id": "Dialogue.Narrative.Briefing",
    "startNodeId": "briefing_intro",
    "nodes": [
      {
        "id": "briefing_intro",
        "speakerAlias": "elder", "speakerName": "Warden Mirelle",
        "cameraId": "Narrative.Intro.Elder",
        "text": "Step close, {player}, and choose how you want to enter the trial.",
        "choices": [
          {
            "id": "briefing_lore",
            "text": "Tell me what the shrine remembers.",
            "actions": [
              { "kind": "AddVariable", "variableId": "lore", "valueKind": "Int", "intValue": 1 }
            ],
            "nextNodeId": "briefing_lore_reply"
          }
        ]
      }
    ]
  }
]
```

```json
[
  {
    "id": "Cinematic.Narrative.Intro",
    "steps": [
      {
        "id": "intro_elder", "cameraId": "Narrative.Intro.Elder",
        "speakerAlias": "elder", "speakerName": "Warden Mirelle",
        "text": "The lanterns still burn.",
        "durationSeconds": 0.75, "requiresAdvance": true
      }
    ]
  }
]
```

```json
[
  {
    "id": "Quest.Narrative.AshenOath",
    "displayName": "Ashen Oath",
    "summary": "Hear the warden, wake the shrine, defeat the beast, and return.",
    "tags": ["quest.narrative"],
    "attributes": [ { "attributeId": "QuestUrgency", "baseValue": 1.0 } ],
    "stages": [
      {
        "id": "trial", "title": "Wake The Shrine",
        "objectiveText": "Inspect the Ember Shrine and defeat the Ashen Beast.",
        "objectiveHint": "Press E near the shrine.",
        "requiredSignals": ["showcase.beast_defeated"]
      }
    ]
  }
]
```

## 2. 字段与行为

| 表 | 字段 | 这样配会产生什么效果 |
|---|---|---|
| variables | `kind` | Int/Float/Bool/String；决定 default* 字段 |
| variables | `defaultInt` 等 / `displayName` | 初值与编辑器显示名 |
| dialogues | `startNodeId` / `nodes[].nextNodeId` | 节点图入口与后继 |
| dialogues | `nodes[].cameraId` | 该节点激活的虚拟相机预设（infra-03） |
| dialogues | `nodes[].autoAdvanceSeconds` | 无选项节点自动推进 |
| dialogues | `nodes[].onEnter` / `choices[].actions` | 进入动作与选项动作（封闭 11 种枚举） |
| cinematics | `steps[].durationSeconds` | 台词时长，缺省 0.75s |
| cinematics | `steps[].requiresAdvance` | true=等玩家输入再走 |
| cinematics | `clearCameraOnComplete` | 播完清相机 |
| quests | `attributes[]` | 挂 GAS 属性（AttributeRegistry 解析） |
| quests | `stages[].dialogueOnEnterId` / `cinematicOnEnterId` | 进阶段放台词/过场 |
| quests | `stages[].requiredSignals` | 信号集齐即阶段完成 |
| quests | `stages[].objectiveText` / `objectiveHint` | 目标文案（Token/字面） |

## 3. 文件结构

`assets/Narrative/` 三件（variables/dialogues/cinematics）+ `assets/Quests/quests.json`（均 ArrayById）。根表空占位（D3），内容在 showcase mod；叙事相机预设经 `assets/Camera/virtual_cameras.json` 深合并追加。

## 4. 运行时加载效果

四表注册后由叙事运行系统与 QuestRuntimeService 消费；相机 id、属性 id、任务/台词/过场引用在加载期解析。**生效级别：重启**。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 变量 kind 非法/默认值类型不符 | 启动失败 |
| 对话节点后继悬空、动作/条件未知 | 启动失败，指明节点 |
| 任务属性未注册 | 启动失败，指明任务 |
| 台词/过场引用未注册 | 启动失败 |

## 6. 实例

- `mods/showcases/narrative/NarrativeShowcaseMod/assets/Narrative/`、`Quests/quests.json`（完整战役样例）
- 同 mod `assets/Camera/virtual_cameras.json`（叙事相机预设覆盖）

**相关文档**：[misc-03 PRD](../prd/misc-03-narrative.md) · [infra-03 配置说明](infra-03-vision-camera.md)
