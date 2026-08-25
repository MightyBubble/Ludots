# 任务与活动合同：Task / Activity / Provider 桥

**任务域与活动域六个能力的玩家语义与作者配置，只认本页。** 入口在 [叙事线 Showcase](narrative-showcase-line.md)；叙事内容合同在 [叙事内容合同](narrative-content-contract.md)。和本页打架的，听本页的。

引擎实现在 `src/Core/Gameplay/Tasks/` 与 `src/Core/Gameplay/Activities/`。

---

## 1. 概述

任务 = 目标追踪器里的持续目标（跨多个结算点存续）；活动 = 弹到面前的一次抉择（一次展示、一次选择、当场结算）。两者实例都是 entity。玩家能看到任务标题/目标/提示（HUD 面板），活动给选项列表与后果文案。

## 2. 结构

```text
Task      = 定义(start_policy/completion_rule/objectives) + 实例(Offered→Active→终态)
Activity  = 定义(dispatch_policy/options) + 实例(pending→active→resolved)，单层结算
Provider桥 = task.create 效果 / task.state_changed 信号源（引擎初始化时装好）

活动选项结算只能调已登记 Effect —— 未登记键加载期 fail-fast
```

## 3. 详情

### 3.1 任务生命周期（tasks.json，snake_case）

`start_policy`：`automatic` = 创建即 Active；`player_accept` = Offered 等接取。终态 Completed/Failed/Abandoned 只读留存。**新任务必须至少接一条创建途径**：前任务 `next_task_id` 接续 / 活动选项 `task.create` / 订阅方代码 `OfferOrStart`。
验收锚：`narrative_chain`（automatic 全链）。

### 3.2 目标与完成规则

目标目前一种 `kind: "signal"`（配 `signal_key`，由 `EmitSignal` 推进）；`completion_rule`：`all`（全部）/ `any`（任一）。
验收锚：`task_rules`（ANY：第二铃自响即关页，第一铃全程未计）。

### 3.3 任务链与进入联动

`next_task_id`：完成即自动创建下一任务；`on_enter_dialogue_id` / `on_enter_cinematic_id`：任务转 Active 时引擎自动开对话/演出（声明式，零代码）。
验收锚：`task_chain`（一完成→二自动醒→ChainIntro 演出自起）、`narrative_chain`（Survey→Debrief→回环对话）。

### 3.4 活动派发（activities.json，snake_case）

`dispatch_policy`：`forced`（必须处理）/ `automatic`（直接结算留通报）。**`pooled`（候选池抽取）合同已立、引擎未实现**，别在内容里用。`source_key` 必须是已注册 `domain.snake_case` 源（内容侧当前可用 `task.state_changed`）。
验收锚：`narrative_chain`（forced 弹层 + F/G 按键结算）。

### 3.5 活动选项合同

选项 `title/body` 是玩家文案（可内嵌按键提示如 `[F]`）；`is_baseline` 保证兜底可点；`effects[]` 按 `execution_order` 调已登记 Effect。**活动 `execute_condition` 当前不可用**（见 §3.6 与入口页 §6）——条件门控请用对话选项 `conditions[]`。
验收锚：`activity_execute_condition`（可执行性 + baseline 结算 + 缺口如实暴露）。

### 3.6 Provider 桥与校验

引擎初始化时装好 `task.create`（参数 `task_id`，创建任务）与 `task.state_changed`（信号源）；随后加载活动配置并**校验全部 provider 键**——未注册即加载失败，无回退。condition provider 目前没有内容侧注册途径（缺口，勿用）。
任务查询：可能被活动创建的任务一律 `TaskRuntimeService.CaptureViews()`（`TryGetState` 只查默认组）。
验收锚：`narrative_chain`（task.create 效果开任务）、引擎顺序修复的回归套件。
