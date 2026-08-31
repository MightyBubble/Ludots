# （已废止）Narrative Frontend Kit

> **本文不再是实现 SSOT。** 故事表现路由与 profile 合同见 [Story Runtime：Dialogue / Sequencer](story_runtime_dialogue_sequencer.md)。`NarrativeFrontendMod` 本阶段仍可作屏幕 surface 组合宿主，但不再依赖 `NarrativeDirector`；视图改读 Dialogue / Sequencer / Story。下文仅作历史对照。

本文定义 Ludots 当前可复用的叙事前端套件落点：Quest / Dialogue / Cinematic / Relationship 都复用同一条 `UIRoot -> ReactivePage -> NarrativeFrontendService` 投影链路，不额外引入平行 UI runtime，也不让应用层玩法逻辑反向接管 UI 状态。

## 1. 目标与边界

- 统一承载 CRPG / JRPG 常见的 quest tracker、overlay dialogue、bubble dialogue、choice list、subtitle bubble、history journal、status notebook。
- 兼容红色警戒 / 文明类的全局状态提示、threat banner、flow review、notification stack，而不把 RTS/4X 玩法硬塞进 narrative core。
- 覆盖底特律变人 / 锈湖类叙事游戏常见的条件分支、变量回显、后果提示、调查流回看，但保持“状态在 gameplay，呈现在 frontend”。
- 前端只消费只读投影；输入、跳过、推进、选择仍走 gameplay authoritative input，不在 UI runtime 内复制一套交互状态机。

当前正式代码落点：

- 共享 capability mod：`mods/capabilities/narrativefrontend/NarrativeFrontendMod/`
- Narrative core typed views：`src/Core/Gameplay/Narrative/NarrativeViews.cs`
- Narrative showcase 投影：`mods/showcases/narrative/NarrativeShowcaseMod/Runtime/NarrativeShowcaseRuntime.cs`
- Relationship showcase 投影：`mods/showcases/relationship/RelationshipShowcaseMod/Systems/RelationshipShowcasePresentationSystem.cs`

## 2. 复用链路

### 2.1 前端服务

`NarrativeFrontendService` 维护多页面 owner 的只读快照聚合：

- owner 级发布：`NarrativeFrontendPageState`
- surface 级描述：`NarrativeFrontendSurfaceModel`
- render snapshot：`NarrativeFrontendRenderState`

对应代码：

- `mods/capabilities/narrativefrontend/NarrativeFrontendMod/Runtime/NarrativeFrontendService.cs`
- `mods/capabilities/narrativefrontend/NarrativeFrontendMod/Runtime/NarrativeFrontendModels.cs`

设计要点：

- 应用层只发布 page/surface 数据，不直接操作 `UiNode`
- capability mod 负责 revision 去抖和合并排序
- 多个 showcase 可以共存，但最终只挂一个 `UIRoot.Scene`

### 2.2 场景 owner

共享 mod 内的 `NarrativeFrontendPresentationSystem` 是唯一 scene owner：

- 读取 `NarrativeFrontendService.Snapshot`
- 通过 `NarrativeFrontendUiController` 挂载或刷新 `ReactivePage`
- 清空时也只清自己的 scene

对应代码：

- `mods/capabilities/narrativefrontend/NarrativeFrontendMod/Systems/NarrativeFrontendPresentationSystem.cs`
- `mods/capabilities/narrativefrontend/NarrativeFrontendMod/UI/NarrativeFrontendUiController.cs`

这保证了：

- Narrative / Relationship 不会各自 mount 一套 `UIRoot`
- renderer、layout、theme、截图都走同一条 UI runtime

### 2.3 Surface 语义层

共享 surface kind 覆盖当前已落地模式：

- `PromptRibbon`
- `ObjectiveTracker`
- `HistoryJournal`
- `StatusPanel`
- `RelationshipNotebook`
- `NotificationStack`
- `ThreatBanner`
- `FlowReview`
- `OverlayDialogue`
- `DialogueBubble`
- `SubtitleBubble`
- `TransmissionOverlay`
- `StandingPortrait`

选项列表不走 NarrativeFrontend surface：查询图写出 `DialogueChoiceCollection`，由 PanelHost `panel.narrative.choices` 呈现。

对应代码：

- `mods/capabilities/narrativefrontend/NarrativeFrontendMod/Runtime/NarrativeFrontendModels.cs`
- `mods/capabilities/narrativefrontend/NarrativeFrontendMod/UI/NarrativeFrontendUiComposer.cs`

## 3. Core 与应用层分工

### 3.1 Core 只提供只读视图

Narrative core 没有引入新的 UI subsystem，只补了 typed view：

- `NarrativeDirector.GetQuestViews()`
- `NarrativeDirector.TryGetActiveDialogueView(...)`
- `NarrativeDirector.TryGetActiveCinematicView(...)`

对应代码：

- `src/Core/Gameplay/Narrative/NarrativeDirector.cs`
- `src/Core/Gameplay/Narrative/NarrativeViews.cs`

用途：

- 应用层不必解析 summary string
- frontend config 只组合 typed view + showcase config
- 关系系统 PR 的 runtime 也能用相同投影方式接入，不需要改 narrative core

### 3.2 应用层负责“把什么投影成什么”

Narrative showcase：

- 读取 quest / dialogue / cinematic / variable / log
- 根据 frontend json 决定哪些 surface 激活
- 把 transmission、overlay dialogue、bubble、subtitle 投影到一个 scene；可回话走 PanelHost

Relationship showcase：

- 读取 relationship runtime、metric registry、team/tag 状态
- 把 trust / oath / synergy / threat / flow log 投影到 shared kit
- 保留 ground ring 作为 ECS 世界高亮证据

对应代码：

- `mods/showcases/narrative/NarrativeShowcaseMod/assets/Frontend/narrative_frontend.json`
- `mods/showcases/relationship/RelationshipShowcaseMod/assets/Frontend/relationship_frontend.json`

## 4. 交互规则

### 4.1 等待输入与不等待输入

当前 kit 通过 surface 数据区分：

- `OverlayDialogue`：显式 wait-input 的主对话
- `DialogueBubble`：贴近角色语气的气泡对话
- `SubtitleBubble`：非 wait-input 的自动推进字幕气泡
- `TransmissionOverlay`：更偏镜头/通信/任务播报的覆盖层

输入仍留在 gameplay：

- `Enter`：推进 dialogue / cinematic
- `1/2/3...`：提交 choice
- `Tab`：跳过当前 cinematic beat

因此 UI kit 可以复用到：

- 全屏覆盖式 overlay 对话（`story.dialogue_overlay`）
- 战斗中字幕气泡（`story.immersive_subtitle`）
- 战前/战后对话卡
- 移动中非阻塞世界气泡（`story.world_bubble`）

### 4.2 4X / RTS / 叙事调查扩展

当前 surface 组合已经覆盖以下方向：

- 策略指挥：`PromptRibbon + StatusPanel + ThreatBanner + FlowReview`
- 分支抉择调查：`OverlayDialogue` + PanelHost 选项面板 + `VariablesPanel/Notebook` + `HistoryJournal`
- 解谜旁白：`DialogueBubble + SubtitleBubble + EventCard/InspectPanel + FlowReview`

扩展原则：

- 新模式优先复用现有 kind 和 anchor
- 只有当多个 mod 都需要新的语义容器时，才新增 `NarrativeFrontendSurfaceKind`
- 不允许在 showcase 内私写第二套 `UIRoot`、第二套 diff runtime、第二套截图管线

## 5. 配置契约

前端内容全部数据驱动：

- owner / backdrop / anchor / width / zIndex
- title / eyebrow / footer / accent / background / border / foreground / muted
- prompt / template / routing / variable descriptors

典型契约位置：

- `mods/showcases/narrative/NarrativeShowcaseMod/Runtime/NarrativeShowcaseFrontendConfig.cs`
- `mods/showcases/relationship/RelationshipShowcaseMod/Runtime/RelationshipShowcaseFrontendConfig.cs`

这让应用层可以：

- 只改 json，就改布局和语义文案
- 不把产品内容硬编码到 renderer / composer
- 用同一套 kit 支撑不同题材 mod

## 6. 回调点

叙事侧正式回调分两类：dialogue / cinematic 来自 narrative events，quest 进度和 signal 来自 quest events。

- `Narrative.DialogueNodeEntered`
- `Narrative.DialogueChoiceCommitted`
- `Narrative.CinematicStepEntered`
- `Narrative.CinematicCompleted`
- `Quest.Signal`
- `Quest.StageChanged`
- `Quest.Completed`

关系侧则复用 relationship runtime + trigger + GAS 回调，把结果投影到 shared kit：

- trust unlock
- oath bond unlock
- synergy activate
- focus lock
- rally deny / rally apply

关键代码：

- `mods/showcases/narrative/NarrativeShowcaseMod/NarrativeShowcaseModEntry.cs`
- `mods/showcases/relationship/RelationshipShowcaseMod/RelationshipShowcaseModEntry.cs`

## 7. 验收与证据

当前正式可玩证据由 GasTests playable acceptance 生成：

- `src/Tests/GasTests/Production/NarrativeShowcasePlayableAcceptanceTests.cs`
- `src/Tests/GasTests/Production/RelationshipShowcasePlayableAcceptanceTests.cs`

产物位置：

- `artifacts/acceptance/narrative-showcase/`
- `artifacts/acceptance/relationship-showcase/`

每个证据包包含：

- 真实 shared frontend 截图
- `battle-report.md`
- `trace.jsonl`
- `path.mmd`
- `5w1h.md`
- `screens/timeline.png`

## 8. 结论

Ludots 当前推荐模式不是“每个玩法各写一套叙事 UI”，而是：

1. Core 输出稳定 typed state
2. 应用层把 typed state + data config 投影成 surface
3. 共享 capability mod 统一接管 scene owner、渲染和截图证据

这条链路已经同时覆盖 narrative showcase 与 relationship showcase，并为 CRPG、JRPG、RTS/4X、分支叙事、调查叙事提供可复用前端落点。
