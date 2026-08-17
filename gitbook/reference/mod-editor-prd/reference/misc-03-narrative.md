# misc-03 reference · 叙事与任务

> 现状参考。第一性需求见 [misc-03 PRD](../prd/misc-03-narrative.md)；配置说明见 [misc-03 配置说明](../config/misc-03-narrative.md)。

## 1. 现状快照

- Narrative 三表：variables（id、kind Int/Float/Bool/String、default*、displayName）；dialogues（id、startNodeId、nodes[]{speaker/text/cameraId/nextNodeId/autoAdvanceSeconds/onEnter/choices}）；cinematics（id、clearCameraOnComplete、steps[]{cameraId/speaker/text/durationSeconds 默认 0.75/requiresAdvance/onEnter}）。
- 条件 5 枚举（Variable/QuestState/SignalCount/EntityTag/EntityAttribute）、动作 11 枚举（SetVariable…ClearCamera）。
- Quests/quests：id、displayName、summary、tags、attributes[]（attributeId/baseValue/currentValue→AttributeRegistry）、stages[]（objectiveText/objectiveHint、dialogueOnEnterId、cinematicOnEnterId、requiredSignals）。
- 运行：QuestRuntimeService 供叙事驱动；根表空占位（D3），内容在 NarrativeShowcaseMod。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 叙事三表加载 | src/Core/Gameplay/Narrative/NarrativeConfigLoader.cs:34-147 |
| 条件/动作枚举 | src/Core/Gameplay/Narrative/NarrativeDefinitions.cs:29-51 |
| 任务加载 | src/Core/Gameplay/Quests/QuestConfigLoader.cs:28 |
| QuestRuntimeService 挂接 | src/Core/Engine/GameEngine.cs:1666-1671 |
| 样例 | mods/showcases/narrative/NarrativeShowcaseMod/assets/Narrative/、Quests/quests.json |

**相关文档**：[misc-03 PRD](../prd/misc-03-narrative.md) · [infra-03 reference](infra-03-vision-camera.md)
