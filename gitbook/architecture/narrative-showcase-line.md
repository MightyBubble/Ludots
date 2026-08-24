# 叙事线 Showcase 唯一入口

**叙事线（对话 / 演出 / 活动 / 任务 / 触发）的 showcase 进度、验收证据、配置写法，只认本页。**
不要另写交接，不要从旧审计开工。玩家文案的可读性基线以双通道交叉审计（pi@opus + 独立评审）通过版为准。

验收证据在 `artifacts/acceptance/` 下，每个场景一个目录（battle-report / trace / path）。测试在 `src/Tests/GasTests/Production/`。和本页打架的旧文档，听本页的。

---

## 1. 概述

进游戏能玩到三件东西，全部无头验收全绿：

- **narrative（完整可玩）**：一段十分钟的闭环——开场演出 → 守望者对话分支 → 唤醒神龛 → 战斗 → 结局抉择。两个结局，一个隐藏选项（不问 lore 就看不到 Mercy）。
- **narrative_chain（链路串联）**：守灯人的一条链——对话 → 三盏灯的演出 → F/G 决策面板 → 派出巡查队 → 任务回报 → 封存或广播。三个场景（封存 / 广播 / 守夜守卫分支）。
- **narrative_slices（能力切片）**：画廊看守人的八页账本，每页只演示一条规则（条件解锁、任意一铃关页、差事自动接力、字幕翻页、远近镜、计数奇偶……），一页 5 秒说清"世界回应你了"。

对话系统不侵入其他职责：**叙事域只发触发器事件和叙事域动作**，一切跨域效果（活动、任务、地图变量、演出命令）都发生在触发器订阅方。

## 2. 结构

```text
唯一入口 = 本页

narrative          完整可玩闭环        mods/showcases/narrative
narrative_chain    链路串联            mods/showcases/narrative_chain
narrative_slices   能力切片集          mods/showcases/narrative_slices

零代码面板基建      UiRegions + PanelKit（PR #1112 线）
验收               每场景 artifacts/acceptance/<showcase>/<scenario>/
缺口上报           condition provider 无内容侧注册途径（见 §6）
```

| Showcase | 测试 | 证据 | 状态 |
|---|---|---|---|
| narrative | `NarrativeShowcasePlayableAcceptanceTests` | `artifacts/acceptance/narrative-showcase/` | 双通道 PASS |
| narrative_chain | `NarrativeChainAcceptanceTests`（3 场景） | `artifacts/acceptance/narrative-chain/<场景>/` | 双通道 PASS |
| narrative_slices | `NarrativeSliceAcceptanceTests`（8 切片） | `artifacts/acceptance/narrative-slices/<切片>/` | 双通道 PASS（v3） |

## 3. 玩家能看到什么

**narrative**：你是见证者 Arcweaver。山谷在等一个见证人，灰烬神龛记着每个被违背的誓言。听守望者说话（问 lore 会改变结局可选性）→ 唤醒神龛 → 按钮提示按 E/Q → 野兽倒下 → 回去交付裁决。Oath Marks 面板实时显示信任与知识，收尾台词按结局不同。

**narrative_chain**：你是山脊灯线的守灯人。灯线夜里自己醒了，三盏灯各报一条情报，第三盏缺读数——派巡查队（F）还是守夜（G）？派了就等回报，回来的读数只有一个词，封存（山谷安睡）还是让传令官喊遍山脊（明天满谷都在谈）。

**narrative_slices**：画廊看守人翻账本给你看：选择如何解锁、铃怎么关页、差事怎么自己接力、字幕怎么翻页、镜头怎么远近、计数怎么分奇偶。每页一个规则，当场兑现。

## 4. mod 作者怎么配

### 4.1 内容文件家族

三个 showcase 用同一套配置文件格式，只是各自一份。**每个内容文件都是 JSON 数组**（如 dialogues.json 是 `[{对话1}, {对话2}]`），新增内容就是在数组里追加一条；`config_catalog.json` 登记的是整个文件路径，已有条目覆盖追加后的整个文件——**向已有文件追加内容不需要改 catalog**，只有新增文件才要登记。ID 全库唯一，重复即加载冲突。

| 文件 | 管什么 | 关键字段 |
|---|---|---|
| `Narrative/variables.json`（camelCase） | 叙事变量 | `id` / `kind`(Int/Float/Bool/String) / `defaultInt` 等 |
| `Narrative/dialogues.json`（camelCase） | 对话树 | `id` / `startNodeId` / `nodes[]{id, speakerName, text, choices[], onEnter[], autoAdvanceSeconds}`；选项含 `conditions[]`（条件门控）与 `actions[]` |
| `Narrative/cinematics.json`（camelCase） | 演出步进 | `id` / `steps[]{id, speakerName, text, durationSeconds, cameraId, requiresAdvance}` |
| `Tasks/tasks.json`（snake_case） | 任务 | `id` / `display_name` / `start_policy`(automatic/player_accept) / `completion_rule`(all/any) / `objectives[]{id, kind:"signal", title, signal_key}` / `next_task_id` / `on_enter_dialogue_id` / `on_enter_cinematic_id` |
| `Activities/activities.json`（snake_case） | 活动抉择 | `id` / `display_name` / `source_key` / `dispatch_policy`(forced/automatic) / `options[]{id, title, body, is_baseline, effects[]}`；effect 目前可用 `task.create`（参数 `task_id`） |
| `Maps/<map>.json` | 地图与地图变量 | 顶层 PascalCase；`Variables` 数组小写字段 `[{"name","type":"int","initial"}]`（严格解析） |
| `Input/default_input.json` | 按键 | `actions[]` + `contexts[].bindings[]`（actionId → 设备路径） |
| `assets/config_catalog.json` | 配置目录 | **必须放 mod 的 `assets/` 根级**（子目录不生效），条目 `{ "Path": "Narrative/dialogues.json", "Policy": "ArrayById", "IdField": "id" }` |

### 4.2 事件与订阅（跨域效果唯一通路）

叙事侧发出的引擎事件：`NarrativeEventKeys.{DialogueNodeEntered, DialogueChoiceCommitted, CinematicStepEntered, CinematicCompleted}`、`TaskEventKeys.{Signal, Offered, Activated, Completed, Failed, Abandoned}`。对话/演出节点可用动作 `EmitSignal` 发信号（同时推进匹配的任务目标并广播事件）。

术语先分清：**事件**是引擎广播（别人发生的），**动作**是对话/演出节点 `onEnter[]` / 选项 `actions[]` 里声明的可执行操作，共十种：`SetVariable / AddVariable / StartTask / StartDialogue / StartCinematic / EmitSignal / CompleteTask / FailTask / ActivateCamera / ClearCamera`。

mod 侧订阅事件的标准写法（在 ModEntry 的 `OnLoad` 里）：

```csharp
context.OnEvent(TaskEventKeys.Signal, ctx =>
{
    GameEngine engine = ctx.GetEngine() ?? throw new InvalidOperationException("no engine");
    string signalId = ctx.Get(TaskServiceKeys.SignalId) ?? string.Empty;   // 类型化载荷键，返回 string
    if (signalId == "chain.new.done") { /* 跨域效果只写在这里 */ }
    return Task.CompletedTask;
});
```

载荷键是**类型化 ServiceKey**（如 `TaskServiceKeys.SignalId` 是 `ServiceKey<string>`），`ctx.Get(键)` 直接返回对应类型的值；可用键见 `TaskServiceKeys` / `NarrativeServiceKeys`（对话/演出载荷如 `NarrativeServiceKeys.BodyText`、`CinematicStepId`）。信号是会话内的即时计数，不持久化、无需清理。

**规矩**：写地图变量、开活动、发相机冲动（`CameraImpulseRuntime.Emit`）都只能在订阅方做；叙事域动作集只有十种（设/加变量、开任务/对话/演出、发信号、完成任务、失败任务、相机开/清）。

### 4.3 最小改动手册：给 narrative_chain 加一段新对话 + 新任务

先说因果链：**动作**是对话节点里声明的（作者写 JSON）→ `EmitSignal` 动作发出信号 → 信号同时做两件事（推进匹配 `signal_key` 的任务目标、广播引擎事件）→ **订阅方代码**（§4.2 的 `OnEvent`）对事件做跨域反应。作者侧只写前两步，后一步是已有模板。

涉及两个文件（相对 mod 根 `mods/showcases/narrative_chain/NarrativeChainShowcaseMod/assets/`）：
- `Narrative/dialogues.json`（对话，字段 camelCase）
- `Tasks/tasks.json`（任务，字段 snake_case）

注意大小写规则：**dialogues / cinematics / variables 用 camelCase；tasks / activities 用 snake_case**（如上两文件示例所示，§4.1 表格按各自惯例）。

1. `Tasks/tasks.json` 追加一条任务：`{"id": "Task.Chain.NewErrand", "display_name": "…", "start_policy": "automatic", "completion_rule": "all", "objectives": [{"id": "done", "kind": "signal", "title": "…", "signal_key": "chain.new.done"}]}`
2. `Narrative/dialogues.json` 追加一棵对话。单节点收口版：

```json
{ "id": "Dialogue.Chain.NewTalk", "displayName": "New Talk", "startNodeId": "root",
  "nodes": [
    { "id": "root", "speakerName": "Relay Warden", "text": "…", "autoAdvanceSeconds": 0.1,
      "onEnter": [ { "kind": "EmitSignal", "signalId": "chain.new.done" } ] }
  ] }
```

多节点带选项与条件门控的完整格式（选项可带 `conditions[]`，不满足则玩家看不到该选项；可带 `actions[]` 在选定瞬间执行）：

```json
{ "id": "Dialogue.Chain.NewTalk", "displayName": "New Talk", "startNodeId": "root",
  "nodes": [
    { "id": "root", "speakerName": "Relay Warden", "text": "Two ways to hear it.",
      "choices": [
        { "id": "ask", "text": "Tell me more.", "nextNodeId": "answer",
          "actions": [ { "kind": "AddVariable", "variableId": "chain.lore", "valueKind": "Int", "intValue": 1 } ] },
        { "id": "sealed", "text": "The sealed line.", "nextNodeId": "answer",
          "conditions": [ { "kind": "Variable", "variableId": "chain.lore", "operator": "GreaterOrEqual", "intValue": 1 } ] }
      ] },
    { "id": "answer", "speakerName": "Relay Warden", "text": "…", "autoAdvanceSeconds": 0.1,
      "onEnter": [ { "kind": "EmitSignal", "signalId": "chain.new.done" } ] }
  ] }
```

多目标任务：`"objectives"` 数组放多条，配合 `"completion_rule": "any"`（任一达成即完成）或 `"all"`（全部达成）。
3. 触发时机：`start_policy: "automatic"` 的任务在被创建的那一刻就是 Active（不等待玩家）；创建它的途径有三——某任务完成经 `next_task_id` 自动接续、活动选项 `task.create` 效果、或订阅方代码调 `OfferOrStart`。想声明式开对话：给任务加 `"on_enter_dialogue_id": "Dialogue.Chain.NewTalk"`（任务转 Active 时引擎自动开）。
4. **按键**：对话推进/选项用引擎既有的输入动作（`NarrativeAdvance`、`NarrativeChoice1/2`，已随 showcase 的 `Input/default_input.json` 绑好 Enter/1/2）；只有新增独立交互（如面板确认键）才需要改输入文件加 action 与 binding。
5. **命令式接线**（不想用声明式时）：照 §4.2 的 `OnEvent` 代码示例在 ModEntry 里订阅 `TaskEventKeys.Signal`，命中你的信号键后调 `engine.GetService(CoreServiceKeys.NarrativeDirector).StartDialogue(...)`。
6. `autoAdvanceSeconds` 是无选项节点自动翻页的等待秒数（0.1 ≈ 立即翻页；不写则等待玩家按推进键）。
7. 玩家文案直接写进 `text` / `title` / `hint`——**不要出现引擎词**（provider/trigger/signal/contract/GAS…），双通道审计会打回。
5. 跑 `dotnet test src/Tests/GasTests/GasTests.csproj --filter FullyQualifiedName~NarrativeChainAcceptanceTests`，证据自动落到 `artifacts/acceptance/narrative-chain/`。

### 4.4 零代码面板（活动弹层 / 任务追踪）

面板全在 manifest JSON：`assets/PanelKit/<name>_hud_manifest.json`，字段 `panelId / panelType / surfaceRegionId / topic / profileId / layoutId / densityId / inputCapabilityId / title / subtitle`。id 必须用 `UiRegionsCatalogFactory` 注册清单里的合法值；`topic` 与你传入的 producer 一致；`surfaceSegment` 合法值是 Background/Main/Overlay/Modal/Debug。安装：`UiRegionsHudInstaller.Install(engine, manifestPath, producers)`（producer 调用方供给，`RefreshLivePanels()` 刷新活数据）。玩家按键 → mod 内输入桥 → `ActivityRuntimeService.ResolveOption`。

### 4.5 命名与校验红线

- 活动 `source_key` 必须是已注册的 `domain.snake_case` 源（当前内容侧可用 `task.state_changed`）。
- `effect_key` / `condition_key` 在**引擎初始化加载期**校验，未注册即加载失败（fail-fast，无回退）。
- 内容 JSON camelCase；地图顶层 PascalCase 但 `Variables` 例外。
- 任务可按"scope 宿主实体"分组（活动选项创建的任务会挂在活动实体上）。`CaptureViews()` 返回**全部**任务实例视图，跨 scope 查询一律用它；`TryGetState(id)` 只查无宿主的默认 scope，查活动建的任务会漏。

## 5. 验收纪律

- 每场景产物：battle-report / trace.jsonl / path.mmd，缺文件即未完成。
- battle-report 里玩家可读时间线与验收锚点分开写（内部通道名不要混进玩家叙事行）。
- 玩家文案改动必须过双通道交叉审计协议（问卷六问 + 打回标准），协议存 `artifacts/acceptance/` 同级的审计目录或随 PR 附上。

## 6. 已知缺口（不要假装能用）

- **condition provider 无内容侧注册途径**：`execute_condition` 引用自定义条件键会在加载期 `unknown_provider_key` 失败（`activity_execute_condition` 切片已如实暴露）。在引擎补注册途径之前，内容不要声明条件执行。
- battle-report 的 Open issues 段是给开发者的引擎缺口说明，不是玩家文案。
