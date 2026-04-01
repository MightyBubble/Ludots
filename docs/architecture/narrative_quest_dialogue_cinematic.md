# Narrative Quest / Dialogue / Cinematic 架构

本文定义 Ludots 叙事基建在当前仓库中的正式落点：`Quest + Dialogue + Cinematic + Variables + Conditions + Actions + Callback` 统一建立在现有 `ConfigPipeline`、`Trigger`、`ECS`、`GAS`、`Camera`、`UI` 之上，不额外引入平行运行时。

## 1. 目标与约束

- 一套 runtime 同时覆盖 CRPG 的分支任务/多条件对话/回访结局，以及 JRPG 的线性演出/定点触发/战后回收。
- 所有叙事状态都收敛到 `NarrativeDirector`，禁止把 quest、dialogue、cinematic 拆成互不感知的多个管理器。
- 内容层只通过配置和正式扩展点表达行为，不在 Mod 里散落硬编码剧情状态机。
- 与现有基础设施复用优先：
  - 配置加载复用 `src/Core/Config/ConfigPipeline*`
  - 回调复用 `src/Core/Scripting/TriggerManager.cs` 与 `context.OnEvent(...)`
  - 镜头复用 `virtual_cameras.json` 与现有 Camera runtime
  - 战斗/奖励复用 `EffectRequestQueue`、`EffectTemplateIdRegistry`、属性系统
  - 交互复用 authoritative input、实体位置、选择和移动链路

## 2. 核心组成

### 2.1 配置定义层

定义集中在 `src/Core/Gameplay/Narrative/NarrativeDefinitions.cs`：

- `NarrativeVariableDefinition`
- `NarrativeQuestDefinition`
- `NarrativeQuestStageDefinition`
- `NarrativeDialogueDefinition`
- `NarrativeDialogueNodeDefinition`
- `NarrativeDialogueChoiceDefinition`
- `NarrativeCinematicDefinition`
- `NarrativeCinematicStepDefinition`
- `NarrativeConditionDefinition`
- `NarrativeActionDefinition`

配置加载由 `src/Core/Gameplay/Narrative/NarrativeConfigLoader.cs` 完成，统一从以下目录合并：

- `Narrative/variables.json`
- `Narrative/quests.json`
- `Narrative/dialogues.json`
- `Narrative/cinematics.json`

这意味着 narrative 内容天然支持 Mod 覆盖、扩展和按 `ConfigCatalog` 收敛，不需要新的资源系统。

### 2.2 运行时层

运行时核心是 `src/Core/Gameplay/Narrative/NarrativeDirector.cs`，职责只有一份：

- 持有叙事变量
- 持有 quest runtime 状态
- 持有当前 dialogue session
- 持有当前 cinematic session
- 统计 signals
- 维护 narrative alias 与 ECS entity 的绑定
- 执行条件判定与动作派发
- 对外发出 trigger/event 回调

`src/Core/Gameplay/Narrative/NarrativeRuntimeSystem.cs` 只负责把 `NarrativeDirector.Update(dt)` 接入主循环，不复制业务逻辑。

在 `src/Core/Engine/GameEngine.cs` 中，这套 runtime 被注册成正式核心服务：

- `CoreServiceKeys.NarrativeDefinitions`
- `CoreServiceKeys.NarrativeDirector`

并在 `SystemGroup.InputCollection` 中更新，确保叙事输入和交互输入一样走 authoritative fixed-step 路径。

## 3. 统一状态模型

### 3.1 Variables

变量支持四种值类型：

- `Int`
- `Float`
- `Bool`
- `String`

用途：

- 分支开关
- 参数化文本插值
- 结局记录
- 任务门槛
- 演出/奖励条件

当前 showcase 使用：

- `trust`
- `lore`
- `ending`

### 3.2 Conditions

当前支持的条件类型：

- `Variable`
- `QuestState`
- `SignalCount`
- `EntityTag`
- `EntityAttribute`

这套条件足以覆盖：

- CRPG：名望/知识/阵营值判断、任务阶段锁、实体属性门槛、击杀或交互信号
- JRPG：章节推进、战斗胜利、队伍状态、固定剧情开关

### 3.3 Actions

当前支持的动作类型：

- `SetVariable`
- `AddVariable`
- `StartQuest`
- `AdvanceQuestStage`
- `StartDialogue`
- `StartCinematic`
- `EmitSignal`
- `CompleteQuest`
- `FailQuest`
- `ActivateCamera`
- `ClearCamera`

设计原则是“动作只表达状态变化与正式系统调用”，不在 action 里塞专用脚本解释器。

## 4. 三种叙事载体如何协同

### 4.1 Quest

Quest 负责长期进度和目标收敛：

- stage 切换
- objective 文案
- signal 完成条件
- stage enter / complete 动作
- 可选地在 stage enter 时启动 dialogue 或 cinematic

适合表达：

- CRPG 的主线/支线、多阶段任务、回访任务
- JRPG 的章节推进、迷宫解锁、BOSS 前后状态转换

### 4.2 Dialogue

Dialogue 负责局部交互、角色表达和分支选择：

- 节点文本
- 说话者
- 局部镜头
- 选项条件
- 选项动作
- 节点 `onEnter`

规范上，节点入场动作使用 `onEnter`，不要再引入平行字段名。

适合表达：

- CRPG 的多分支问答、知识检定、结局判断
- JRPG 的单线剧情对话、菜单式回答、NPC 回访

### 4.3 Cinematic

Cinematic 负责镜头化叙事步进：

- step 序列
- 镜头切换
- 文本/说话者
- 自动步进或显式推进
- step `onEnter`

适合表达：

- JRPG 式开场、BOSS 现身、结算过场
- CRPG 的短镜头桥接，而不是替代对话和任务本身

### 4.4 三者的边界

- Quest 负责“长期状态”
- Dialogue 负责“局部选择”
- Cinematic 负责“镜头化呈现”
- Variables / Signals / Actions 是三者共享的公共语言

这样可以避免：

- quest 内嵌整套对话树
- cinematic 偷偷推进任务但外部不可见
- dialogue 自己维护一份并行任务状态

## 5. 回调点与扩展缝

`src/Core/Gameplay/Narrative/NarrativeServiceKeys.cs` 定义了正式 narrative 回调事件：

- `Narrative.Signal`
- `Narrative.QuestStageChanged`
- `Narrative.QuestCompleted`
- `Narrative.DialogueNodeEntered`
- `Narrative.DialogueChoiceCommitted`
- `Narrative.CinematicStepEntered`
- `Narrative.CinematicCompleted`

这些事件通过 `ScriptContext` 暴露标准参数：

- `NarrativeServiceKeys.SignalId`
- `NarrativeServiceKeys.QuestId`
- `NarrativeServiceKeys.QuestStageId`
- `NarrativeServiceKeys.DialogueId`
- `NarrativeServiceKeys.DialogueNodeId`
- `NarrativeServiceKeys.DialogueChoiceId`
- `NarrativeServiceKeys.CinematicId`
- `NarrativeServiceKeys.CinematicStepId`
- `NarrativeServiceKeys.SpeakerName`
- `NarrativeServiceKeys.BodyText`

Mod 扩展的推荐方式：

1. 用 `context.OnEvent(NarrativeEventKeys.Xxx, ...)` 接 narrative 生命周期。
2. 在 handler 内调用正式服务，例如 spawn queue、effect queue、camera、UI、map runtime。
3. 若缺少公共能力，优先扩充 `ConditionKind` / `ActionKind`，不要在单个 showcase 里私写隐藏协议。

## 6. 与 ECS / GAS / Trigger / Camera 的连接

### 6.1 ECS 绑定

`NarrativeDirector.BindEntity(alias, entity)` 让 narrative 配置只引用逻辑别名，不硬编码 entity id。

优势：

- 地图重载后可重新绑定
- 动态生成实体可在 spawn 后补绑
- 同一剧情配置可跨地图或跨 mod 复用

### 6.2 Trigger 回调

Narrative runtime 不直接依赖 showcase 逻辑。showcase 的玩法接线放在 `mods/showcases/narrative/NarrativeShowcaseMod/NarrativeShowcaseModEntry.cs` 和 `Runtime/NarrativeShowcaseRuntime.cs`：

- `CinematicCompleted` 后启动 elder briefing
- `Signal(showcase.spawn_beast)` 时排入 spawn queue
- `Signal(showcase.reward_apply)` 时下发 GAS reward effect

这让“叙事完成了什么”与“游戏世界如何响应该完成”被清晰分层。

### 6.3 GAS 奖励与战斗

showcase 中，叙事系统不直接写属性值，而是发正式效果：

- `Effect.Narrative.BlessingHeal`
- `Effect.Narrative.BlessingSpeed`

敌人死亡也不是 narrative 自己扣血，而是由 `NarrativeShowcaseInteractionSystem` 观察实体 `Health`，低于阈值后发出 `showcase.beast_defeated` signal。

这保证：

- 战斗仍然是 GAS / AttributeBuffer 的职责
- narrative 只消费战斗结果，不接管战斗执行

### 6.4 Camera 与 Presentation

Dialogue node 和 cinematic step 都可以声明 `cameraId`，底层复用现有虚拟相机注册表。

UI 面板仅是 narrative 当前状态的只读投影，位于：

- `mods/capabilities/narrativefrontend/NarrativeFrontendMod/UI/NarrativeFrontendUiController.cs`

它不拥有状态，不替代 director。

## 7. 配置契约示例

### 7.1 Quest Stage

```json
{
  "id": "trial",
  "title": "Wake The Shrine",
  "objectiveText": "Inspect the Ember Shrine and defeat the Ashen Beast once it answers.",
  "requiredSignals": ["showcase.beast_defeated"]
}
```

### 7.2 Dialogue Node

```json
{
  "id": "briefing_accept",
  "speakerAlias": "elder",
  "text": "Go to the shrine. Wake what sleeps beneath it, and return when you can name what should survive.",
  "onEnter": [
    { "kind": "AddVariable", "variableId": "trust", "valueKind": "Int", "intValue": 1 },
    { "kind": "AdvanceQuestStage", "questId": "Quest.Narrative.AshenOath" }
  ]
}
```

### 7.3 Conditional Choice

```json
{
  "id": "return_mercy",
  "text": "Remember it. Fear without memory will only grow back.",
  "conditions": [
    { "kind": "Variable", "variableId": "lore", "operator": "GreaterOrEqual", "intValue": 1 }
  ],
  "actions": [
    { "kind": "SetVariable", "variableId": "ending", "valueKind": "String", "stringValue": "Mercy" },
    { "kind": "EmitSignal", "signalId": "showcase.reward_apply" },
    { "kind": "CompleteQuest", "questId": "Quest.Narrative.AshenOath" }
  ]
}
```

## 8. CRPG / JRPG 覆盖策略

### 8.1 CRPG

适合用法：

- 以 quest stage 驱动地区推进
- 以变量和条件表达知识、信任、阵营、道德抉择
- 以 dialogue choice 承接分支
- 以 signal 和 quest state 驱动多地图回访

### 8.2 JRPG

适合用法：

- 以 cinematic step 组织线性过场
- 以 quest stage 驱动章节目标
- 以 dialogue node 表达固定 NPC 交流
- 以 signal 连接战斗胜利、机关触发、剧情收尾

结论：这套架构不是偏 CRPG 或偏 JRPG，而是让“长期状态、局部选择、镜头呈现”各归其位。

## 9. Showcase 落地

可玩 showcase 位于 `mods/showcases/narrative/NarrativeShowcaseMod/`，覆盖完整闭环：

1. 开场 cinematic
2. elder briefing dialogue
3. lore 分支与变量增长
4. shrine 交互触发 reveal cinematic
5. beast 生成
6. 使用现有 ECS 移动与 GAS 战斗击败 beast
7. return dialogue
8. Mercy / Duty 结局与 GAS reward

关键资源：

- 地图：`mods/showcases/narrative/NarrativeShowcaseMod/assets/Maps/narrative_showcase_hub.json`
- Narrative 配置：`mods/showcases/narrative/NarrativeShowcaseMod/assets/Narrative/*.json`
- 相机配置：`mods/showcases/narrative/NarrativeShowcaseMod/assets/Configs/Camera/virtual_cameras.json`
- GAS 奖励：`mods/showcases/narrative/NarrativeShowcaseMod/assets/GAS/effects.json`
- 可玩验收：`src/Tests/GasTests/Production/NarrativeShowcasePlayableAcceptanceTests.cs`

验收产物输出到：

- `artifacts/acceptance/narrative-showcase/trace.jsonl`
- `artifacts/acceptance/narrative-showcase/battle-report.md`
- `artifacts/acceptance/narrative-showcase/path.mmd`
- `artifacts/acceptance/narrative-showcase/screens/*.png`

其中 `screens/*.png` 当前是 deterministic acceptance panels：它们汇总真实运行后的世界状态、UI 文字和关键实体位置，用于稳定验收；如果后续要覆盖 framebuffer 级渲染回归，应再补一条真实渲染抓帧证据链。

## 10. 后续扩展建议

优先扩展方向：

- 新的 condition kind：队伍成员、背包物品、地图标签、章节号
- 新的 action kind：切 map、投递 UI inbox、派发 performer cue、注册 checkpoint
- 任务图谱层：在不破坏 director 单一真相的前提下，为复杂 CRPG 提供 quest graph authoring

不建议的方向：

- 给配置字段名增加多套别名
- 在 showcase mod 内部复制 narrative runtime
- 让 UI 面板或 trigger handler 持有第二份 quest/dialogue 状态
