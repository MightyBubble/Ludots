# Quest Core Infrastructure

Quest 是 Core gameplay 基建，不属于 Narrative 命名空间。Narrative 可以通过 action 驱动 Quest，但 Quest 的定义、运行时状态、entity 映射、signal 计数和公开 trigger 协议都由 `Ludots.Core.Gameplay.Quests` 拥有。

## 1. SSOT

Quest 的状态单一真相分为三层：

- 配置真相：`Quests/quests.json` 通过 `QuestConfigLoader` 进入 `QuestDefinitionRegistry`。
- 运行时真相：`QuestRuntimeService` 创建和维护 `QuestInstanceCm` entity，索引键是 `(ScopeHost, DefinitionId)`。
- 信号真相：signal count 存在 `QuestRuntimeService.Signals`，由 `quests` save participant 保存。

`NarrativeDirector` 不保存第二份 quest state，也不保存第二份 signal 字典。

## 2. Entity 映射

Quest 和 item 一样可以落到 entity：

- `StartQuest` 创建 `QuestInstanceCm` entity。
- `TryResolveQuestEntity(questId, out entity)` 解析 global quest entity。
- `TryResolveQuestEntity(questId, scopeHost, out entity)` 解析 scoped quest entity。
- `QuestServiceKeys.QuestEntity` 在 Quest trigger context 中暴露本次事件对应的 quest entity。

Quest entity 可以挂 `AttributeBuffer`、`GameplayTagContainer` 和关系层需要的 component，因此 GAS buff 可以作用到任务实体本身。

## 3. Runtime API

正式 Core runtime API 位于 `QuestRuntimeService`：

- 命令入口：`StartQuest`、`AdvanceQuestStage`、`CompleteQuest`、`FailQuest`、`EmitSignal`
- 查询入口：`TryGetQuestState`、`TryResolveQuestEntity`、`TryGetDefinition`、`TryGetStage`、`GetQuestViews`
- 生命周期事件：`QuestEventPublished` 发布 `Started`、`StageChanged`、`Completed`、`Failed`

正式 Narrative action API 只是 adapter：

- `StartQuest`
- `AdvanceQuestStage`
- `EmitSignal`
- `CompleteQuest`
- `FailQuest`

这些 action 允许 dialogue、cinematic 或 narrative flow 驱动任务，但不改变 Quest 的归属。

## 4. Trigger API

正式 Quest trigger API 位于 `QuestEventKeys`：

- `Quest.Signal`
- `Quest.Started`
- `Quest.StageChanged`
- `Quest.Completed`
- `Quest.Failed`

这些事件通过 `context.OnEvent(QuestEventKeys.Xxx, ...)` 订阅，并通过 `QuestServiceKeys` 传参：

- `QuestServiceKeys.SignalId`
- `QuestServiceKeys.SignalIntValue`
- `QuestServiceKeys.SignalStringValue`
- `QuestServiceKeys.QuestId`
- `QuestServiceKeys.StageId`
- `QuestServiceKeys.ObjectiveText`
- `QuestServiceKeys.QuestEntity`

事件顺序约束：`NarrativeDirector.EmitSignal` 会先发布 `Quest.Signal`，再调用 `QuestRuntimeService.EmitSignal` 推进 signal 计数与 quest stage。因此 `Quest.Signal` handler 不应假设能读到“本次 signal 已推进后”的 quest state；需要读推进后状态时，应监听后续的 `Quest.StageChanged` 或 `Quest.Completed`。

## 5. 边界

- Quest 公共协议禁止挂在 `NarrativeEventKeys` 或 `NarrativeServiceKeys` 下。
- Dialogue / Cinematic 生命周期继续使用 `NarrativeEventKeys`。
- Narrative 文档可以描述如何驱动 Quest，但 Quest SSOT 必须回到本文。
- 如果需要脚本函数，统一在 Core 注册 `FunctionRegistry` 名称，并写入本文作为稳定 API。
- 如果需要配置动作，扩展 `NarrativeActionKind` / `NarrativeConditionKind` 或补 Quest 自己的配置入口；不要在 showcase JSON 里塞隐藏协议。
