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

### 4.1 内容文件家族（三个 showcase 同构）

| 文件 | 管什么 | 关键字段（camelCase） |
|---|---|---|
| `Narrative/variables.json` | 叙事变量 | `id` / `kind`(Int/Float/Bool/String) / `defaultInt` 等 |
| `Narrative/dialogues.json` | 对话树 | `id` / `startNodeId` / `nodes[]{id, speakerName, text, choices[], onEnter[], autoAdvanceSeconds}`；选项含 `conditions[]`（条件门控）与 `actions[]` |
| `Narrative/cinematics.json` | 演出步进 | `id` / `steps[]{id, speakerName, text, durationSeconds, cameraId, requiresAdvance}` |
| `Tasks/tasks.json` | 任务 | `id` / `start_policy`(automatic/player_accept) / `completion_rule`(all/any) / `objectives[]{kind:"signal", signal_key}` / `next_task_id` / `on_enter_dialogue_id` / `on_enter_cinematic_id` |
| `Activities/activities.json` | 活动抉择 | `id` / `source_key` / `dispatch_policy`(forced/automatic) / `options[]{id, title, body, is_baseline, effects[]}`；effect 目前可用 `task.create`（参数 `task_id`） |
| `Maps/<map>.json` | 地图与地图变量 | 顶层 PascalCase；`Variables` 数组小写字段 `[{"name","type":"int","initial"}]`（严格解析） |
| `Input/default_input.json` | 按键 | `actions[]` + `contexts[].bindings[]`（actionId → 设备路径） |
| `assets/config_catalog.json` | 配置目录 | **必须放 mod 的 `assets/` 根级**（子目录不生效），条目 `{ "Path": "Narrative/dialogues.json", "Policy": "ArrayById", "IdField": "id" }` |

### 4.2 事件与订阅（跨域效果唯一通路）

叙事侧发出的引擎事件：`NarrativeEventKeys.{DialogueNodeEntered, DialogueChoiceCommitted, CinematicStepEntered, CinematicCompleted}`、`TaskEventKeys.{Signal, Offered, Activated, Completed, Failed, Abandoned}`。对话/演出节点可用动作 `EmitSignal` 发信号（同时推进匹配的任务目标并广播事件）。

mod 侧订阅：`context.OnEvent(EventKey, handler)`，handler 内 `context.GetEngine()` 取引擎、`ctx.Get(<ServiceKey>)` 读事件载荷。范式照抄 `NarrativeChainShowcaseModEntry.cs`。

**规矩**：写地图变量、开活动、发相机冲动（`CameraImpulseRuntime.Emit`）都只能在订阅方做；叙事域动作集只有十种（设/加变量、开任务/对话/演出、发信号、完成任务、失败任务、相机开/清）。

### 4.3 最小改动手册：给 narrative_chain 加一段新对话 + 新任务

1. `Tasks/tasks.json` 加一条任务：`{"id": "Task.Chain.NewErrand", "display_name": "…", "start_policy": "automatic", "completion_rule": "all", "objectives": [{"id": "done", "kind": "signal", "title": "…", "signal_key": "chain.new.done"}]}`
2. `Narrative/dialogues.json` 加一棵对话：`{"id": "Dialogue.Chain.NewTalk", "startNodeId": "root", "nodes": [{"id": "root", "speakerName": "Relay Warden", "text": "…", "autoAdvanceSeconds": 0.1, "onEnter": [{"kind": "EmitSignal", "signalId": "chain.new.done"}]}]}`
3. 想让它在链路里被声明式触发：给某个任务加 `"on_enter_dialogue_id": "Dialogue.Chain.NewTalk"`（任务激活自动开对话），或在触发器订阅方 `HandleTaskSignalAsync` 里对 `chain.new.done` 反应。
4. 玩家文案直接写进 `text` / `title` / `hint`——**不要出现引擎词**（provider/trigger/signal/contract/GAS…），双通道审计会打回。
5. 跑 `dotnet test src/Tests/GasTests/GasTests.csproj --filter FullyQualifiedName~NarrativeChainAcceptanceTests`，证据自动落到 `artifacts/acceptance/narrative-chain/`。

### 4.4 零代码面板（活动弹层 / 任务追踪）

面板全在 manifest JSON：`assets/PanelKit/<name>_hud_manifest.json`，字段 `panelId / panelType / surfaceRegionId / topic / profileId / layoutId / densityId / inputCapabilityId / title / subtitle`。id 必须用 `UiRegionsCatalogFactory` 注册清单里的合法值；`topic` 与你传入的 producer 一致；`surfaceSegment` 合法值是 Background/Main/Overlay/Modal/Debug。安装：`UiRegionsHudInstaller.Install(engine, manifestPath, producers)`（producer 调用方供给，`RefreshLivePanels()` 刷新活数据）。玩家按键 → mod 内输入桥 → `ActivityRuntimeService.ResolveOption`。

### 4.5 命名与校验红线

- 活动 `source_key` 必须是已注册的 `domain.snake_case` 源（当前内容侧可用 `task.state_changed`）。
- `effect_key` / `condition_key` 在**引擎初始化加载期**校验，未注册即加载失败（fail-fast，无回退）。
- 内容 JSON camelCase；地图顶层 PascalCase 但 `Variables` 例外。
- scoped 任务实例查询用 `TaskRuntimeService.CaptureViews()`（`TryGetState` 只查默认 scope）。

## 5. 验收纪律

- 每场景产物：battle-report / trace.jsonl / path.mmd，缺文件即未完成。
- battle-report 里玩家可读时间线与验收锚点分开写（内部通道名不要混进玩家叙事行）。
- 玩家文案改动必须过双通道交叉审计协议（问卷六问 + 打回标准），协议存 `artifacts/acceptance/` 同级的审计目录或随 PR 附上。

## 6. 已知缺口（不要假装能用）

- **condition provider 无内容侧注册途径**：`execute_condition` 引用自定义条件键会在加载期 `unknown_provider_key` 失败（`activity_execute_condition` 切片已如实暴露）。在引擎补注册途径之前，内容不要声明条件执行。
- battle-report 的 Open issues 段是给开发者的引擎缺口说明，不是玩家文案。
