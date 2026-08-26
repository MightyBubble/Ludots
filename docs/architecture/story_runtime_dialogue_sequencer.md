# Story Runtime：Dialogue / Sequencer 拆分合同

对应 Epic [#1083](https://github.com/MightyBubble/Ludots/issues/1083)。本文是实现 SSOT：退役 Narrative 运行时总名，拆成独立 Dialogue 与 Sequencer，条件/动作/变量统一接入既有 Graph 与 Presentation 合同。

## 1. 概述

玩家侧看到三种故事表达：可分支对话、角色头顶气泡、时间轴驱动的镜头+字幕。引擎侧不再用一套 Narrative 解释器同时管变量、条件枚举、动作枚举和过场步进；改为：

- **Dialogue**：会话、节点、选择、输入推进
- **Sequencer**：独立时钟、轨道、区间进入/离开、一次性信号
- **Story Line + Presentation Profile**：台词只存一次；表现只引用 profile
- **Query Graph / TriggerGraph / MapVariable / Blackboard / TextToken**：条件、副作用、事实与文案的唯一基建

禁止第二套变量表、第二套动作解释器、第二套 UI 真相。禁止在配置或代码里写入外部作品名。

## 2. 结构

```text
配置
  Story/lines.json                 lineId → speakerId / textToken / args / tags
  Story/presentation_profiles.json profileId → backend + surface + layout 引用
  Dialogue/dialogues.json          对话树（只引用 lineId / graphId / profileId）
  Sequencer/sequences.json         时间轴（Camera / Subtitle / Signal 轨）

运行时
  DialogueRuntime                  会话生命周期、选择、Graph 调用门
  SequencerRuntime                 clock、play/pause/rate/seek/skip、轨调度
  StoryGraphInvoker                Query 求值条件；TriggerGraph 执行副作用
  StoryPresentationProjector       profile → 屏幕 Skia / 世界投影 / 字幕轨

复用
  GraphIdRegistry / GraphProgramRegistry / GraphExecutor
  GraphReturnWriter（Query 网关，#1099）
  MapVariableStore / Blackboard
  PresentationTextCatalog（TextToken）
  VirtualCameraRequest
  NarrativeFrontendMod（屏幕 surface 组合；本阶段保留 capability 名，不再依赖 NarrativeDirector）
  Tweening（Sequencer 通道插值）
  IScreenProjector（世界气泡屏幕投影）
```

### 2.1 表现后端路由（回答世界空间 vs 屏幕空间）

参考 WebUI Panel Kit 的「manifest 只声明引用、缺引用 fail-closed」与 Core PanelHost「屏幕锚点目录」：profile 是装配合同，不是第三套 UI runtime。

| profileId | 后端 | 锚点 | 复用 |
|-----------|------|------|------|
| `story.dialogue_overlay` | 屏幕 Skia（UIRoot Overlay segment） | 屏幕锚点（如 BottomCenter） | NarrativeFrontend `OverlayDialogue` + `ChoiceList` |
| `story.world_bubble` | 世界→屏幕投影后写入同一 Overlay | 说话者实体世界坐标 + 头顶偏移 | `IScreenProjector` + Frontend `DialogueBubble` 动态 Absolute |
| `story.immersive_subtitle` | 屏幕字幕轨 | 屏幕锚点（如 BottomCenter） | Frontend `SubtitleBubble`；可由 Sequencer SubtitleTrack 驱动 |

不把世界气泡假装成「左下角换皮面板」。WebUI Panel Kit 本阶段不承载故事对话（浏览器面板合同另线）；故事表现留在 Skia Overlay + 世界投影，避免第三真相。

## 3. 详情

### 3.1 Line 目录

```json
{
  "id": "line.warden.briefing.001",
  "speakerId": "speaker.warden",
  "textToken": "story.warden.briefing.001",
  "args": [],
  "tags": ["briefing"]
}
```

Dialogue / Sequencer 只引用 `lineId`。最终文案经 `PresentationTextCatalog` 解析 TextToken + typed args。禁止在对话节点内联最终字符串。

### 3.2 Dialogue

```json
{
  "id": "dialogue.briefing",
  "entryNode": "intro",
  "nodes": [
    {
      "id": "intro",
      "lineId": "line.warden.briefing.001",
      "presentationProfile": "story.dialogue_overlay",
      "choices": [
        {
          "id": "accept",
          "lineId": "line.player.accept",
          "conditionGraphId": "dialogue.condition.can_accept",
          "actionGraphId": "dialogue.action.accept",
          "nextNode": "trial"
        }
      ]
    }
  ]
}
```

职责：

- 持有 `DialogueSession`（当前节点、已求值选项、elapsed）
- 推进输入、提交选择、节点跳转
- 选项可用性：`StoryGraphInvoker.EvaluateCondition(conditionGraphId)` → **Query Graph**，以 `HaltReturnInt != 0` 为真（与图能力合同一致）
- 选择副作用：`StoryGraphInvoker.ExecuteAction(actionGraphId)` → **TriggerGraph**，单次切片必须 `Halt`；若 `Yield` 则失败关闭（对话提交是同步权威点）
- 不持有变量存储；不解析动作/条件枚举；不执行 cinematic step

事件（替换旧 `Narrative.*`）：

- `Dialogue.NodeEntered`
- `Dialogue.ChoiceCommitted`

### 3.3 Sequencer

```json
{
  "id": "sequence.trial_reveal",
  "clock": { "rate": 1.0, "pausePolicy": "independent" },
  "tracks": [
    {
      "type": "Camera",
      "profile": "camera.trial_reveal",
      "start": 0.0,
      "duration": 4.2
    },
    {
      "type": "Subtitle",
      "lineId": "line.shrine.reveal.001",
      "presentationProfile": "story.immersive_subtitle",
      "start": 0.4,
      "duration": 3.2
    },
    {
      "type": "Signal",
      "eventId": "story.trial_revealed",
      "actionGraphId": "story.action.spawn_trial",
      "start": 4.0
    }
  ]
}
```

- 独立 clock：play / pause / rate / seek / skip
- Section 语义：进入激活、离开停用；Signal 一次性触发
- CameraTrack → `VirtualCameraRequest`
- SubtitleTrack → Line + profile → PresentationProjector
- SignalTrack → TriggerGraph（同样单切片 Halt 合同）
- 不负责 Dialogue 分支

红线（Epic 评论）：不复用 GAS AbilityExec；不以 graph yieldable 当时间轴原语；通道插值用 Tweening；演出轨直写相机/Presenter 请求。

### 3.4 旧 Narrative 退役

删除生产路径：

- `NarrativeDirector` / `NarrativeValueStore` / `NarrativeConditionKind` / `NarrativeActionKind` / `NarrativeCinematicDefinition`
- 配置入口 `Narrative/variables.json`、`Narrative/dialogues.json`、`Narrative/cinematics.json`

`GameEngine` 安装 `DialogueRuntime` + `SequencerRuntime`。若 catalog 仍声明旧 `Narrative/*` 路径，加载失败，错误信息明确指向 `Dialogue/`、`Sequencer/`、`Story/` 与 Graph（MapVariable/Blackboard）。

Task 字段：`on_enter_dialogue_id` 保留；`on_enter_cinematic_id` 改为 `on_enter_sequence_id`。出现旧字段名即失败关闭。

存档 domain：`narrative` → `dialogue` + `sequencer`（或统一 `story` 下分节）；不保留 Narrative 变量快照。

### 3.5 Showcase

可操作演示（无外部作品名）：

1. **主对话 overlay**：profile `story.dialogue_overlay`；选项条件来自 Query；确认后 TriggerGraph 改 MapVariable；说话者与头像位走 line/speaker 合同（肖像资产缺位时允许空位，不引入平行 glyph 合同冒充 #128）
2. **世界气泡**：profile `story.world_bubble`；头顶跟随实体投影
3. **沉浸字幕序列**：Sequencer 同步 Camera + Subtitle；Signal 触发 TriggerGraph；支持暂停/继续/跳过

前端仍发布到既有 NarrativeFrontend surface kinds（屏幕组合合同已通用化），由 profile 选择 kind 与锚点策略，而不是 showcase 硬编码「有选项就 overlay」。

## 4. 场景

- 玩家靠近守望者并交互 → 打开 `dialogue.briefing` → 看到 overlay 对话与条件过滤后的选项
- 地图变量 `trial_phase==0` 时「接受试炼」可用；选择后 TriggerGraph 写入 `trial_phase=1` 并发任务信号
- 走进神殿触发 `sequence.trial_reveal` → 镜头与字幕同步 → 信号点刷出试炼单位
- 加载仍含 `NarrativeConditionKind` / 旧 `Narrative/variables.json` 的包 → 启动失败并指出迁移目标

## 5. 边界

- 不新增 Graph VM、事件总线、变量表、平行 UIRoot
- Query 只读；TriggerGraph 才写
- TextToken 是用户可见文案唯一出口；禁止 String 地图变量拼台词
- MapVariable 仅 Int/Float；离散结局用 Int 枚举，不用 Narrative String 变量
- WebUI Panel Kit 本阶段不接入故事对话
- #128 肖像 2D 正式合同未闭环前，showcase 不伪造第二套肖像系统
- #1217 变量作者面（编辑器增删改）不在本交付；本交付只保证运行时读 MapVariable/Blackboard + 旧私有变量路径失败关闭

## 6. UAT

```gherkin
Feature: Dialogue 使用统一 Graph 基建

  Scenario: 选项条件来自 Query Graph
    Given Dialogue 选项引用 conditionGraphId
    When 玩家打开对话
    Then 选项可用性由 Query Graph 返回

  Scenario: 选择动作来自 TriggerGraph
    Given 玩家确认可用选项
    When Dialogue 提交选择
    Then TriggerGraph 执行世界状态变更
    And Dialogue 不解析动作枚举

Feature: Sequencer 驱动表现和事件

  Scenario: 序列同步驱动镜头和字幕
    Given Sequence 包含 CameraTrack 和 SubtitleTrack
    When 播放头进入对应 Section
    Then 镜头和字幕同步显示

  Scenario: 序列信号触发图动作
    Given Sequence 包含 SignalTrack
    When 播放头到达信号时间
    Then 对应 TriggerGraph 执行一次

  Scenario: 旧 Narrative 配置失败关闭
    Given 配置使用旧 NarrativeConditionKind 或 Narrative/variables.json
    When 引擎加载配置
    Then 加载失败
    And 错误信息指出 Dialogue / Sequencer / Story / Graph 迁移入口

Feature: 表现 profile 路由

  Scenario: 世界气泡跟随说话者
    Given 节点使用 presentationProfile story.world_bubble
    And 说话者实体已绑定
    When 对话节点进入
    Then 气泡锚在说话者头顶屏幕投影附近
    And 不是固定屏幕角落换皮面板
```

## 7. 复用清单（开工前）

| 项 | 用途 |
|----|------|
| ConfigPipeline + ConfigCatalog | 加载 Story/Dialogue/Sequencer |
| GraphProgramRegistry / GraphIdRegistry / GraphExecutor | 条件与动作 |
| MapVariableStore | 地图事实 |
| TaskRuntimeService | 长期任务；仅通过 Graph/信号编排 |
| VirtualCameraRequest | CameraTrack / 对话镜头 |
| PresentationTextCatalog | TextToken |
| NarrativeFrontendService | 屏幕 surface 发布 |
| IScreenProjector | 世界气泡 |
| Tweening | 可选通道插值 |
| CoreSaveParticipants | 存档域迁移 |

## 8. 提交切片

1. 本文档 + Dialogue runtime + 旧 Narrative 失败关闭/拆除 + Graph invoker
2. Sequencer runtime（独立 commit）
3. Showcase / Frontend 投影 / 测试与 UAT
