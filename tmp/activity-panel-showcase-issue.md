Parent: #773
关联：#830（程序总览）、#807（F6 活动弹层，后续复用本卡面板）、#819（A10 验收口径）、#804（F3 内容包，范围不相交）

# [y5k][F6a] Activity 通用事件面板（0 编码内容）+ 三派发路径 showcase

## 1. 概述

### 1.1 背景

Activity 运行时核心已落地：`src/Core/Gameplay/Activities/` 七个文件、53 个集成测试全绿（`src/Tests/GasTests/Integration/Activity{Runtime,SignalIntake,PooledDispatch,TaskPersistence}Tests.cs`）。但审计确认它在真实运行时是惰性的：

- 无生产派发入口：`IntakeSignal` 只有测试调用方；已注册 fact source 只有 `fixture.signal_ping`（测试夹具）与 `task.state_changed`。没有 `activity.create` 一类的已登记 Effect（Task 侧有 `task.create`，`src/Core/Gameplay/Tasks/TaskBridgeProviders.cs:26`）。
- 无内容路径：全仓库没有 `activities.json`，也没有任何 `config_catalog.json` 声明 `Activities/activities.json` 条目，loader 永远空转（`src/Core/Gameplay/Activities/ActivityConfigLoader.cs:34-37`）。
- 无呈现出口：`ActivityPresentationBuffer` / `ActivityLifecycleBuffer` 零消费者，且只在 `ResetState()` 清空，一旦被真实驱动会无界增长。
- 无展示：`showcase.registry.json` 349 个条目，0 个涉及 activity。

对照 Task 线（#774，已关闭）的完整示范链：`TaskBridgeProviderInstaller` → `TaskObjectiveWebUiTopicProducer`（DataPlane）→ PanelKit objective 面板 → `panel_kit_task_objective_showcase`（stub entry + 纯 JSON manifest/profile，`mods/showcases/panel_kit_task_objective_showcase/`）。Activity 每一环缺对应物。

### 1.2 目的

- 交付 forced / pooled / automatic 三条派发路径各一个可玩 showcase。
- 交付一个通用「事件面板」：PanelKit 独立 panelType，只读投影活动实例；不复用 task objective 面板渲染，不走 dialogue / sequencer 出口。
- 内容 0 编码：新增一条演示活动只需要改 JSON（activities.json、panel manifest、profile、RNG 分布表），不写 C#。

### 1.3 思路

一次性基建代码与纯 JSON 内容分层交付。「0 编码」指内容作者视角：基建层的 topic producer、面板描述符、派发 Effect 是一次性成本，全部对齐 task objective 面板的既有模式，不另造平行体系。面板是镜子不是账本：只订阅 `ActivityRuntimeService` 的只读投影，界面侧不存第二份进度。

## 2. 结构

### 2.1 一次性基建（B 组）

| 项 | 对齐对象 | 说明 |
|---|---|---|
| B1 `activity.offer` 派发 Effect | `task.create`（`TaskBridgeProviders.cs:26`） | 已登记 Effect，参数含 activity_id 与 scope target。触发轨道：map trigger（JSON）→ TriggerGraph（JSON）→ ApplyEffect op → `activity.offer`。不把引擎事件总线键当 fact source（#773 边界） |
| B2 表现排水泵 | `GasPresentationEventBuffer` 的消费/清空模式（`GameEngine.cs:1202` 有界构造） | 帧末排空 presentation / lifecycle buffer 到 DataPlane cue 投影，修掉「只在 ResetState 清空」的无界增长 |
| B3 Activity topic producer | `TaskObjectiveWebUiTopicProducer`（`src/Libraries/Ludots.WebUI.DataPlane/TaskObjectiveWebUiTopicProducer.cs`） | SSOT 只有 `ActivityRuntimeService`（`CaptureViews` / `TryGetActiveOptions` / cues）；LatestWins 快照；历史区带所选选项 id（见 2.3） |
| B4 PanelKit 事件面板 | `WebUiTaskObjectivePanelDescriptors` + profile JSON 模式 | panelType 独立声明；选项列表、基础选项标记、可见但不可执行且写明原因、Gate 未通过的选项不出现；绑定走 panel manifest |
| B5 `activity.resolve` 命令 | NotificationActionRegistry → WebUiCommandRouter 的命名命令模式 | 面板确认 → `ResolveOption`；非法选择（已 resolved、不可执行）必须在界面可见反馈，禁止静默吞掉 |

### 2.2 内容与 showcase（S 组，纯 JSON + stub entry）

- S1 forced：地图触发 → `activity.offer` → 弹层拍板 → 已登记通用 Effect 结算 → 历史可查。
- S2 pooled：RNG 分布表（命名流，`RngPickService`）→ 重触发可见抽中候选变化；确定性（同流状态同候选）由 UAT 产物证明。
- S3 automatic：触发即自动结算，无选项面板，面板历史区出现带自动结算标记的条目。
- 三个 mod 各自的 `config_catalog.json` 声明 `Activities/activities.json` 条目，打通 loader 死路径；全部进 `showcase.registry.json` 并指向真实启动入口。

### 2.3 随卡偿还的债（showcase 会直接踩到）

- cue 载荷统一：`Presented` / `Resolved` / `OptionBlocked` / `AutomaticSettled` 补 `ScopeKey`（当前只有 `AdmissionRejected` 带，`ActivityRuntimeService.cs:750-757` 对 `:369,:463,:569,:588`）。
- 查询口径：`TryGetActiveOptions` 拒绝非 active 实例；`CaptureViews` 的消费侧不把 resolved 混入活动列表（#773 §3.3「resolved 运行时查询不再返回」）。
- `ActivityView` 补所选选项的只读出口（组件上已有 `SelectedOptionIndex`，投影未带；#773 §4.1 验收点「历史里能查到本次所选的选项 id」）。
- `_processedSignalIds` 入快照（对齐 Task 侧 `TaskRuntimeSnapshot.Signals` 的持久化，`src/Core/Gameplay/Tasks/TaskRuntimeService.cs:34-36`），否则信号驱动的 showcase 存档回放会二次进场。

## 3. 详情：0 编码边界

| 层 | 内容 |
|---|---|
| 一次性代码 | B1–B5 与 2.3 全部债务；此后能力面固定 |
| 纯配置 | 活动定义（选项、条件、效果引用）、panel manifest / profile、RNG 分布表、地图触发与 graph |
| 判据 | 验收时新增第 4 条演示活动（任一路径），只动 JSON，可启动、可触发、可结算 |

已知限制（本卡不修、不阻塞，缺口归 A 线）：

- #773 §3.2 的定义级 `context_bindings` 未实现：forced 路径在 `ResolveOption` 时触发信号的对象绑定已丢失，本卡 showcase 的选项效果 target 限定 `context.subject`（scope host）。
- Unique 仅 scope 维度；全局唯一、resolve 时限预算不在本卡范围。
- lifecycle 引擎键的事件总线订阅面（#818）不做；面板直接读只读投影。
- 反向 API 审计项：若需要游戏内运行时重置/切换命名流来现场演示池抽，先确认引擎已有该接口；没有则列入缺口，不在 showcase 里用测试回调冒充。

## 4. 场景

1. 玩家进 S1 地图走进触发区 → 弹层出现「按兵不动（基础选项）/ 撤回 / 展开（不可执行，写明原因）」→ 选择 → 弹层关闭，历史区出现该实例与所选选项 id。
2. S2 玩家重新触发 pooled 活动，看到抽中的候选变化；HUD 显示池名与权重；UAT 产物证明同流状态同候选。
3. S3 触发后无弹层，历史区出现一条带自动结算标记的条目。
4. 作者在 activities.json 增加一个选项，重启后弹层多一项；把某 effect_key 改成未登记键 → 加载整包拒装，错误信息带该键名。
5. 弹层未拍板时存档 → 冷启动读档 → 弹层仍在、选项一致、instance id 连续。

## 5. 边界

- 不交付 #804 内容包（≥12 条、五玩家环）；本卡每路径 1–2 条演示级内容。
- 不改 #773 合同与运行时语义；发现合同冲突回 #773 处理，不在本卡绕过。
- 不做 y5k 战略界面五投影（#807 范围）；本卡面板后续供 #807 复用。
- 选项效果只允许已登记通用 Effect，不为本卡自造玩法域键。

## 6. UAT

```gherkin
Feature: 事件面板是活动实例的只读投影
  Scenario: forced 拍板即结束
    Given S1 showcase 已启动且玩家触发 forced 活动
    When 玩家选择基础选项「按兵不动」
    Then 该实例进入 resolved，弹层关闭
    And 历史查询返回该实例与所选选项 id
    And 不存在由该选项直接打开的第二个活动实例

  Scenario: 池抽确定性
    Given S2 showcase 使用命名流 seed 固定
    When 以相同流状态两次触发 pooled 活动
    Then 两次抽中的候选 id 一致

  Scenario: 0 编码判据
    Given B1–B5 已合入
    When 只新增或修改 JSON 增加一条演示活动
    Then 该活动可被触发、呈现、结算，全程无 C# 改动

  Scenario: 未登记键整包拒装
    When activities.json 引用未登记的 effect_key "siege.take_over"
    Then 配置加载失败，错误信息包含 "siege.take_over"

  Scenario: 弹层跨存档
    Given forced 活动实例处于 active
    When 存档后冷启动并读档
    Then 弹层重新出现，选项一致，instance id 连续
```

验收产物：DataPlane topic 快照与 cue 投影写出的 presentation-requests.jsonl（沿 #773「验收产物映射」，不另造平行表现事件）；Agent Bridge 实机证据（`/health` pumpCount 增长、目标 mod 会话核对、主循环与故障路径交互截图）。
