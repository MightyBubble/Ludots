# 叙事内容合同：对话 / 演出 / 变量 / 动作 / 事件

**叙事域六个能力的玩家语义与作者配置，只认本页。** 入口在 [叙事线 Showcase](narrative-showcase-line.md)，那页管进度与验收；本页管合同本身。和本页打架的实现或旧文档，听本页的。

引擎实现在 `src/Core/Gameplay/Narrative/`（加载链：mod `assets/` 根级 `config_catalog.json` 登记文件 → 引擎初始化加载并校验）。

---

## 1. 概述

玩家在叙事域能体验六件事：跟人对话并做分支选择、看一段带字幕的演出、被条件挡住或放行、看变量随剧情涨落、感受动作引发的后果、以及一切跨域反应都从事件出发。作者只写 JSON，不写 C#（除非要做新的跨域反应，见管线页）。

## 2. 结构

```text
对话树   = 节点 + 选项（选项可带条件与动作）
演出     = 步进序列（字幕 + 可选相机）
变量     = 类型化叙事状态，条件与文案引用它
动作     = 节点/选项声明的十种操作
事件     = 引擎广播，跨域反应的唯一入口

都长在内容 JSON 里；验收锚见 §3 各节
```

## 3. 详情

### 3.1 对话树与分支（dialogues.json，camelCase）

玩家看到的是说话人、正文、若干选项；有选项等选择，无选项按 `autoAdvanceSeconds` 翻页或等推进键。**带 `choices[]` 的节点 `autoAdvanceSeconds` 无效**。`nextNodeId` 指向不存在 = 加载失败（fail-fast）。
作者最小例与多节点完整例见入口页 §4.3。验收锚：`narrative_slices/dialogue_gate`（选项集断言）。

### 3.2 选项条件门控

选项 `conditions[]` 不满足时**该选项不出现**（不是置灰）。条件引用的变量必须先在 `variables.json` 声明。
验收锚：`dialogue_gate`——首轮只给无条件选项，拿到 lore 后重开对话，被锁选项出现。

### 3.3 演出时间轴（cinematics.json，camelCase）

步进模型：每步 = 字幕（speakerName + text）+ 可选 `cameraId` + `durationSeconds` 或 `requiresAdvance`。skip 是已挂账的已知缺陷（跳过的步骤副作用不补放，时间轴编排另案 [#1083 Sequencer]）。
验收锚：`subtitle_presenter`（三步字幕逐帧替换/清屏）、`presenter_track`（步边界命令轨）。

### 3.4 叙事变量与条件求值（variables.json，camelCase）

类型 Int/Float/Bool/String，默认值在声明里。条件五种：`Variable`（比较）、`TaskState`、`SignalCount`（某信号累计次数）、`EntityTag`、`EntityAttribute`。
验收锚：`dialogue_gate`（Variable 门控）、`action_gallery`（Set/Add 终值断言）。

### 3.5 叙事动作集（十种，域内合法）

`SetVariable / AddVariable / StartTask / StartDialogue / StartCinematic / EmitSignal / CompleteTask / FailTask / ActivateCamera / ClearCamera`。对已终结任务重复 Complete/Fail 幂等短路（域状态机保持严格）。
验收锚：`action_gallery` 逐一断言八种 + 相机开/清回落。

### 3.6 叙事事件（跨域反应的唯一入口）

`DialogueNodeEntered / DialogueChoiceCommitted / CinematicStepEntered / CinematicCompleted`，载荷键 `NarrativeServiceKeys.*`（DialogueId、NodeId、CinematicId、StepId、SpeakerName、BodyText）。`EmitSignal` 动作额外发 `TaskEventKeys.Signal`（载荷 SignalId/IntValue/StringValue）——它同时推进匹配 `signal_key` 的任务目标。
**信号纪律：`signalId` 与 `signal_key` 逐字符一致**，不一致静默且目标永不完成。信号计数会话内有效，不进存档。
验收锚：`narrative_chain` 全链（信号驱动 cinematic→活动→任务→回环）。
