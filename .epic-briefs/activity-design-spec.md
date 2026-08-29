# Activity 系统设计文档（从零完整稿 v5）

状态：设计定稿候选。整合 v4 规格、v5 四路研究、P社四家 SDK 一手对照（`.epic-briefs/pdx-sdk-vs-v5.md`）与域界重划。批准后取代 `activity-v4-spec.md` 成为 #773 订正正文与 wiki 手册源。
标注约定：【待裁】= 尚未拍板项；【PDX】= 有业界先例的设计；【增量】= 四家都没有、我们因联机/投票新增的设计。

---

## 0. 定位：叙事内容三件套的分工

| 系统 | 一句话 | 生命周期 | 实体上载什么 |
|---|---|---|---|
| **Activity 活动** | 一个**决策时刻**：选项、表决、到期裁决、单层结算 | 短（弹层→拍板→归档） | 决策生命周期：状态、所选选项、作用域快照、选票 |
| **Task 任务** | 一段**旅程**：进度、完成/失败条件、阶段链、期限 | 长（挂追踪列表直至了结） | 旅程进度、信号计数、累加器、持续修正 |
| **Story/Dialogue/Sequencer** | 讲给玩家听：剧情线、多页对话、演出时间轴 | 演出期间 | 演出状态 |

**内容归属判据**（作者先问自己）：

| 问题 | 归属 |
|---|---|
| 玩家现在必须选一个，选完就结束？ | Activity |
| 要跨周期追踪、有进度/完成条件/持续效果？ | Task |
| 外交协议、盟约、停战（长活+可修参数+撕约条件）？ | **Task**（协议本体）；签约/修约/撕约的拍板 = Activity，结算效果回写协议参数 |
| 要多页对话、演出时间轴？ | Dialogue/Sequencer |
| 选项的后果需要长期追踪？ | Activity 结算里 `CreateTask` 接棒 |

**硬规则**：Activity 的 schema 禁止长出进度/阶段/期限类字段——想加即内容放错域。旅程状态一律 Task 承载。

**两域共享的公共机器**（不是重复造，是抽公共件）：作用域合同（scopeDomain+scopeKey）、fan-out 派发、deadline 扫描模式。Task 与 Activity 用同一套，各自只剩领域语义。

---

## 1. 运行时模型

### 1.1 实例物化与参数装饰

- 每次活动 = 一个 entity，挂 `ActivityInstanceCm`（DefinitionId / InstanceId / State / ScopeHost / SelectedOptionIndex / Revision / DispatchTick / 作用域字段）。**运行时真相只有这一处**，面板与叙事只做只读投影。
- 实体可挂 GAS 属性/标签/附加组件（与 Task 同构的装饰能力）：投票选票挂 `ActivityBallotCm`；实例级计数可挂属性经效果修饰。当前无内容在实例上挂属性——能力在，用法按配方扩展。
- 实例三状态：`pending → active → resolved`，不增设。resolved 后转只读历史，活动类查询不再返回。

### 1.2 四概念正交（v5 核心框架）

```
业务作用域 = scopeDomain + scopeKey   谁的活动（representative/team/faction/global）
执行上下文 = ScopeHost entity        图执行时的 Caster（旅程/决议的主体实体）
输入来源   = seat                    谁在操作（仅命令与确认权限，possession 可转移）
呈现投递   = audience                cue/面板发给谁
```

活动归属绑玩家/代表身份而非 seat。禁止前端传实体 ID 当身份（实体 ID 是 ECS 地址不是用户身份）。

### 1.3 上下文合同

- **寄存器**：条件/结算图执行时 `Caster = ScopeHost`；`ExplicitTarget = 空`。
- **命名作用域快照**【PDX 三家同构】：实例携带命名快照表——发射/结算时 `save_scope` 写入（如 `save_scope_as = enemy_army`），`when`/`settle` 图按名读取，实例 resolved 即清。这是"引用触发对象"（context_bindings 缺口）的设计答案。
- **参数通道**：配置 `args`（标量）→ EntryPayload → 图内 `LoadEntryPayload{Int,Float,Entity}` 读；加载期做键集校验（可达性分析，短路分支不误杀共享图）。

---

## 2. 配置结构（完整 schema）

### 2.1 文件布局

```
assets/config_catalog.json           声明 Activities/activities.json
assets/Activities/activities.json    活动定义（本节主角）
assets/GAS/graphs.json               三种图：Validation（条件）/ Script（结算）/ TriggerGraph（发射轨）
assets/Rng/distributions.json        候选池权重（pool 型 arrival 用）
assets/Events/custom_events.json     发射轨自定义事件
assets/Tasks/tasks.json              结算接棒的任务定义（含协议型任务）
assets/Maps/<map>.json               放置 scope 实体、心跳节奏
Assets/PanelKit/panel_manifest.json  面板绑定
```

### 2.2 定义全字段参考

```jsonc
{
  // ── 身份 ──
  "id": "court.marriage_proposal",
  "display_name": "联姻提议",
  "summary": "邻国提议联姻，接受或婉拒。",
  "picture": "court_marriage",                    // 【PDX】表现层资产引用（G7，插图/主题）

  // ── 作用域（G3）──
  "scope": {
    "kind": "per_representative",   // per_representative | per_team | per_faction | global
    "instance_mode": "fan_out",     // fan_out=每作用域一份 | shared=一实例 N 访问者（投票前提）
    "audience": "owner_seat"        // owner_seat | owner_player | team_members | all_seats（cue/面板投递）
  },

  // ── 到达（怎么到玩家面前）──
  "arrival": "modal",
  // "modal"   弹层必须拍板
  // "notice"  【PDX HOI4 news/Vic3 图标】呈现但不决策：默认入列表，popup 策略显式配
  // "auto"    不弹层，settle 执行后归档（纯通报/后台结算）
  // { "pool": "chance.entries" }   确定性候选池抽取

  // ── 再入（能不能再次出现）──
  "recur": "dedupe",
  // "dedupe"（默认）挂起时同名不叠
  // "always" 每次触发都来
  // "once"   此 scope 一生一次
  // { "max_times": 3 }                      【PDX 无此档，我们补】
  // { "cooldown": { "ticks": 1200, "clock": "step" } }
  // { "mutex": "court.policy" }

  // ── 条件（Validation 图，bool 出口）──
  "when": { "graph": "Graph.Gate.CourtRep", "args": { "threshold": 80 } },

  // ── 呈现前执行【PDX 三家 immediate】（动态文案/立绘选择）【待裁：首批或次批】──
  "immediate": { "script": "Graph.Court.PrepPortraits" },

  // ── 选项 ──
  "options": [
    {
      "id": "accept", "title": "接受联姻", "body": "两姓之好，就此缔结。",
      "is_baseline": false,                        // 兜底选项：永远可执行；timeout 默认结算它
      "show_when":   { "graph": "Graph.Gate.NotKin" },        // false → 不出现
      "enable_when": { "graph": "Graph.Gate.CanDowry",
                       "args": { "gold": 100 } },              // false → 可见但锁定，原因直达面板
      "settle":      { "script": "Graph.Court.MarrySettle",
                       "args": { "prestige": 5 } }
    },
    { "id": "decline", "title": "婉拒", "is_baseline": true,
      "settle": { "script": "Graph.Court.DeclineSettle" } }
  ],

  // ── 选完清理【PDX after】跨选项执行，与所选无关【待裁：次批】──
  "after": { "script": "Graph.Court.CleanupFlags" },

  // ── auto 活动的结算（根字段）──
  // "settle": { "script": "Graph.Report.CityTaken" }

  // ── 决议方式（默认 chair=单决策者即现有行为；vote=多投票者表决）──
  "resolve": {
    "vote": {
      "voters": { "kind": "players" },             // players | team(+team_id) | entities(+entity_ids)
      "rule": "weighted_majority",                 // plurality | majority | weighted_majority | unanimous | chair
      "weight": { "source": "attribute", "attribute_key": "diplomacy.clout",
                  "snapshot_at": "open" },          // 权重开票时快照，防票期变动
      "quorum": { "mode": "percent", "value": 50 },
      "secret": false, "allow_change": true, "abstain": "allowed",
      "tie_break": "baseline",                     // 平局取兜底选项
      "settle_when": "rule_met",                   // rule_met | deadline | all_voted
      "timeout": { "ticks": 600, "clock": "step",
                   "on_timeout": "count" }          // count | quorum_failure | option:<id> | chair（仅 vote 用四态）
    }
  }
  // 单人活动（无 vote 块）的超时 = "timeout": { "ticks": N, "clock": "step" }，
  // 到期自动结算 is_baseline 选项【PDX HOI4/EU4 先例；显式标记避位置 bug】
}
```

### 2.3 图种分工与能力面

| 图种 | 用途 | 能 | 不能 |
|---|---|---|---|
| **Validation** | 条件（when/show_when/enable_when），bool 出口 | LoadCaster/LoadSelfAttribute/LoadAttribute（读）、算术比较、EntryPayload 读参 | 一切写操作（Pure 纪律） |
| **Script** | 结算（settle/immediate/after），Void 出口 | CreateTask、WriteMapVar、ReadBlackboardFloat、SpawnTemplate、ShowPanel、算术/文本/WeightedPick、InvokeScript、命名快照读写 | GAS 效果管线 op（ApplyEffectTemplate/ModifyAttributeAdd/InvokeBuiltin/WriteBlackboard*）；Yield/AwaitCallback（结算禁）；DispatchMapEvent |
| **TriggerGraph** | 发射轨 + 旅程侧写 | 订阅事件（引擎/自定义/历法）、LoadPlacedEntity、OfferActivity(OfferActivityScope)、ModifyAttributeSet（具名属性写，main 已合） | Query 图查询链 |

要动 GAS 世界状态：结算走 `CreateEffect` op 过桥入效果提案窗口【待裁 G6：CreateEffect vs 掩码放宽，倾向前者——保住 Script 纯净边界，过桥显式】；或旅程侧触发图用 `ModifyAttributeSet`。

**结算的纪律**（不变）：单层（选项不得开活动，后续=CreateTask 或信号）；无隐藏骰子（随机仅池抽/对象选取，走命名流）；未知键/图 id/参数缺失 → 加载期整包拒装，错误带键名。

---

## 3. 发射（TriggerGraph 轨）

```jsonc
[{
  "id": "Graph.Rhythm.Daily",
  "kind": "TriggerGraph",
  "entries": [
    { "label": "on_day",    "event": "Calendar.DayAdvanced", "start": "scope", "refire": "restart" },
    { "label": "on_pulse",  "event": "Court.QuarterlyPulse", "start": "fan",   "refire": "restart" },
    { "label": "on_region", "event": "RegionEntered", "start": "scope", "once": true,
      "filters": { "region": "border_gate" } },
    { "label": "on_strain", "event": "MapVariableChanged", "start": "scope", "refire": "restart",
      "filters": { "varName": "supply.strain", "threshold": 100, "direction": "cross_above" } }
  ],
  "nodes": [
    { "id": "scope", "op": "LoadPlacedEntity", "instanceId": "council" },
    { "id": "offer", "op": "OfferActivity",    "activityId": "court.marriage_proposal" },
    { "id": "done",  "op": "HaltReturnInt" }
  ],
  "controlEdges": [ … ], "valueEdges": [ … ]
}]
```

- 事件源：引擎事件、自定义事件（`custom_events.json`）、**历法事件族**（`Calendar.*`，依赖 #1384 P0 修好抛法）、pulse 节拍（心跳/回合/历法相位）。节拍设计参照 PDX：不提供过细节拍（CK3 砍 monthly 的性能先例）；fan-out **错峰**【PDX EU4 tag-order 公式先例】。
- 单目标 `OfferActivity{activityId, scope}`；多目标/全局 `OfferActivityScope{activityId, scopeKind, instanceMode}`（内部复用 PlayerEntityLookup/TeamEntityLookup/MapSession）。
- **延迟与超时正交**【PDX 四家一致】：发射参数带延迟（N tick 后弹）；实例定义带 timeout（弹出后多久必须选）。延迟收件人已亡 → backlog 冻结【PDX HOI4】。
- 随机事件不建 MTTH 调度器【PDX 现代（CK3/Vic3）无 MTTH】：表达 = pulse 节拍 + `when` 机会门（概率折算成 Validation）+ pool 加权抽取，可复现可测试。

---

## 4. 与 Task 域的接口（含外交协议模式）

```
settle 脚本 ──CreateTask(taskId)──▶ 协议/后续 Task 实例（可被后续 Activity 的效果修饰参数）
Task 完成/阈值事件 ──T2 桥──▶ OfferActivity（旅程到达决策点）
```

**外交协议配方**（Task 承载 + Activity 时刻）：

```jsonc
// Tasks/tasks.json —— 协议本体：旅程实体，参数可装饰
{ "id": "treaty.truce",
  "display_name": "停战协议", "start_policy": "automatic", "completion_rule": "all",
  "objectives": [ { "id": "hold", "kind": "signal", "title": "维持至期满。",
                    "signal_key": "treaty.truce.broken" } ],
  "timeout": { "days": 1825, "on_timeout": "Graph.Treaty.Expire" },   // T1：Vic3 JE 式期限
  "modifiers_while_active": [ { "effect": "Effect.Treaty.NoWar" } ] }  // 持续修正（GAS 效果模板）

// Activities/activities.json —— 签约/撕约的拍板时刻
{ "id": "treaty.sign", "arrival": "modal", "recur": "once",
  "options": [
    { "id": "sign", "title": "缔约", "is_baseline": true,
      "settle": { "script": "Graph.Treaty.SignSettle" } } ] }
// Graph.Treaty.SignSettle（Script）: CreateTask(taskId: treaty.truce) + 写快照
// 撕约时刻同理：结算效果回写协议 Task 的参数（CreateEffect 修饰协议实体属性）
```

T 侧缺口（承接旅程，独立排期）：T1 任务期限/到期（Vic3 JE `timeout+on_timeout` 抄改）；T2 完成状态→`OfferActivity` 轨（completion_reward 显式发模式【PDX】）；T3 每 scope task fan-out（与 G3 同机器）。

---

## 5. 校验与纪律（加载期 fail-fast 全清单）

- 未知图 id / 图种不符（`graph`≠Validation、`script`≠Script）→ 拒装带 id 与实际种名；
- settle 图含 Yield/AwaitCallback → 拒装；args 键集差集（可达性分析）→ 拒装；
- modal 无选项 / 无 baseline / auto 带选项 / pool 壳带选项或 settle → 拒装；
- recur/timeout 对象参数非法（ticks≤0、mutex 组空、vote 规则缺 chair 等）→ 拒装；
- `CreateTask` 的 taskId 未注册 → 编译期拒；scope.kind=per_faction 无派系基础设施 → 拒（不静默降级）；
- vote 块出现在非 shared / 非 modal → 拒；
- 呈现层资产引用（picture 等）指向未注册资产 → 拒。

运行时纪律：`enable_when` 失败的锁定原因直达面板；准入拒绝留审计 cue；cue 每固定步排空；历史带所选选项 id。

---

## 6. 呈现与命令

- 呈现策略【PDX Vic3】：默认**图标入列**（活动列表/追踪面板），`arrival:"modal"` 强弹且**不暂停**；notice 型只入列。
- cue 八种：`Presented / OptionBlocked(带原因) / Resolved / AutoSettled / AdmissionRejected(带原因码) / VoteCast / VoteChanged / VoteResolved(含 tally) / VoteQuorumFailed`。
- 命令（WebUiCommandRouter 命名命令，必带 `seatId`，服务端 seat→player→权限链）：
  - `activity.confirm {instanceId, optionId}`（vote 活动拒收，防双通道）
  - `activity.vote {instanceId, optionId|abstain}`
  - `activity.dismiss {instanceId}`（notice 型关闭）
- 面板：PanelKit panelType `activity`，只读投影（SSOT=实例实体）；per-seat 订阅经 subscribe 上下文带 seatId。
- 【增量】shared 实例的多 seat 投递、`original_recipient_only` 选项级可见性【PDX HOI4 major 族先例的 audience 字段表】。

## 7. 存档

domain `activities`：实例实体（含状态/所选/快照/选票组件）随实体持久化 + 运行时快照（nextInstanceId、已处理信号 id）。已知风险：AttributeBuffer 二进制世界往返损坏（特征测试记档）——G1 配方落地前排雷。

---

## 8. 实施状态对照

| 部件 | 状态 |
|---|---|
| 实例物化/三状态/准入（dedupe/always/once/cooldown/mutex）/条件三分/结算纪律/cue/存档/面板/命令 | **已实现**（v4 基线，待 provider→图化重构后改名对齐本设计） |
| OfferActivity op / 排水系统 / 面板 / showcase | **已实现**（未提交，S0 落盘） |
| arrival(recur union)/when/show_when/enable_when/settle 改名 + 图化求值 | S5 重构件 |
| scope 合同 + OfferActivityScope + per-seat（G3） | 设计定，未实现 |
| resolve.vote + timeout（G4） | 设计定（pi 研究全文），未实现 |
| immediate/after、命名快照、notice arrival、picture | 设计定【待裁批次】 |
| CreateEffect（G6）/ OfferActivity 放宽（G5） | 【待裁二选一】 |
| T1/T2/T3（任务侧期限/桥/fan-out） | 设计定（Vic3 JE 字段表），未实现 |
| AI 拍板（G9） | 缺消费者，后置 |

## 9. 来源

P社四家 SDK 一手对照：`.epic-briefs/pdx-sdk-vs-v5.md`（含全部 wiki 出处）；四路研究：`.epic-briefs/activity-v5-synthesis.md`；v4 基线：`.epic-briefs/activity-v4-spec.md`。
