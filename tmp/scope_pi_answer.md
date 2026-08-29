研究完成。以下为最终交付文档（只读仓库，未修改任何文件）。

---

# Activity 多玩家投票：投票语义与 P社先例研究（v5 增量草案）

状态：研究输出，供与作用域侧研究员合稿后回流 `activity-v4-spec.md` §9 与对抗验证。

## 0. 本侧范围与耦合声明

- 本文件只定义**投票语义、超时语义、P社先例映射、v5 `resolve` 块增量、投票侧引擎缺口**。
- 作用域分类学（per-representative / per-team / per-faction / global、fan-out vs shared instance、准入键编码）归作用域侧研究员，本侧只给出**耦合约束**：
  - `resolve.vote` 存在 ⟹ 该活动**必须是 shared instance**（一个实例、N 个投票者）。per-representative 作用域 + vote 块是 N=1 的自相矛盾，加载期应拒装。
  - shared instance 的准入键必须与"单个 scope host 实体"解耦。运行时已有空 scope 键路径：`ScopeKey(Entity.Null) == 0`（`ActivityRuntimeService.cs` 末尾 `private static int ScopeKey`），全局活动的键形如何落由作用域侧裁定，本侧只要求"可表达"。
- 现有派发入口 `OfferActivity(activityId, scopeHost)`（`GasGraphRuntimeApi.cs`，op 462，TriggerGraphOnly，见 `GraphOpDescriptorTable.Data.cs`）签名不变；全局活动的 scopeHost 约定（传空/传地图代表实体）属作用域侧。

---

## 1. 投票语义完整定义（v5 提议）

### 1.1 投票者集合声明

投票者是**世界身份**（player 或 team 代表实体），不是 seat——seat 只是 I/O 嘴。依据：`commandSystem.md`"player/team 无特殊地位全部物化为实体"；`gitbook/architecture/four-layer-architecture/seat.md` 四分职责（Seat=本机 I/O，Possession=谁在驾驶）。

声明方式（静态声明，不引入新求值机制）：

| `voters.kind` | 语义 | 依据 |
|---|---|---|
| `"players"` | 地图全部已绑定 player（每个取其代表实体） | `MapConfig.Players[{PlayerId,TeamId,RepresentativeInstanceId}]`（`src/Core/Config/MapConfig.cs`） |
| `"team"` + `team_id` | 该队全部 player | `MapConfig.Teams` 与 `PlayerTeamRelationshipBindingData` |
| `"entities"` + `entity_ids` | 显式实体清单（放置实体或代表实体） | `MapConfig.Entities` 放置机制；派系/利益集团如需"非 player 投票者"，用实体清单 + 属性权重表达（见 1.2 加权） |

投票者身份键 = **PlayerId**（或显式实体 id）。到实体的解析复用 `ParticipantBindingResolver` 进图时建立的绑定（`src/Core/Gameplay/Teams/ParticipantBindingResolver.cs`），零新增注册表。

### 1.2 决议规则（`rule`）

| rule | 数学 | 先例 |
|---|---|---|
| `"plurality"` | 有效票中得票最多者胜，无过半要求 | EU4 议会倒计时收票 |
| `"majority"` | 严格过半才胜；未过半=未达成 | Vic3 支持权重>反对权重 |
| `"weighted_majority"` | 按每票权重求和，过半者胜 | Vic3 clout、EU4 领主影响力 |
| `"unanimous"` | 全体有效投票者一致才胜 | CK3 联机同意（全员点头） |
| `"chair"` | 主席一票定音，其余票只记录不计数 | EU4 联机房主独裁弹窗 |

**权重来源**（仅 weighted 规则使用）：`weight: { source: "flat" | "attribute", attribute_key?, missing?: "zero" | "one" }`——`"attribute"` 表示从每个投票者代表实体的 GAS 属性读数作权重（Vic3 clout 同构）。属性缺失语义沿用 v4 §3.5 裁定倾向（缺 buffer 静默得 0），`missing` 允许作者显式改为一。

**规则数学基数（待裁定点 #1）**：多数/加权的分母 = 有效票（已投非弃权）的权重和。弃权不进规则数学；参与度用独立的 `quorum` 表达（规则与门槛正交）。

### 1.3 平局（`tie_break`）

- 默认 `"baseline"`：平局取 `is_baseline` 选项。v4 的 baseline 语义（"永远可执行"）天然可兼任"僵局默认出口"，零新增机制。
- `"option:<id>"`：显式指定。
- `"random"`：确定性抽取——候选复用 `RngPickService`（v4 池抽同款），但需要 per-instance 流键，见待裁定点 #5。

### 1.4 计票可见性（`secret`）

- `false`（默认，公开计票）：面板实时显示每投票者当前票 + 实时 tally。
- `true`（密投）：投票期隐藏每投票者明细与 tally；揭示时点与粒度见待裁定点 #4。P社无直接先例（EU4/Vic3 公开，CK3 同意弹层双方可见），密投是作者可选能力，非任何先例强制。

### 1.5 投票可否撤改（`allow_change`）

- 默认 `true`：实例未结算前可重投，后投覆盖前投，ballot revision 递增（复用 `ActivityInstanceCm.Revision` 的递增模式，`ActivityComponents.cs`）。
- `false`：已投即锁，再投返回 `vote.already_cast` 拒绝。
- 撤改与 `secret` 组合：密投下"改票"不可见，只变 tally 的延迟揭示结果。

### 1.6 决议→settle 执行时序（`settle_when`）

v4 纪律"当场结算"保留（settle 是同步 Script 图，禁 `Yield`/`AwaitCallback`，v4 §3.4）。变化只在**结算触发点**：

| settle_when | 语义 | 先例 |
|---|---|---|
| `"rule_met"`（默认） | 规则一满足即结算（过半即落槌、全员一致即落槌） | Vic3 进度完成、CK3 全员回应 |
| `"deadline"` | 只在到期/收票时刻结算 | EU4 议会倒计时 |
| `"all_voted"` | 全体投票者投完即结算 | CK3 同意弹层全员回应 |

结算动作链（与单人 confirm 同路径，`ActivityRuntimeService.ResolveOption` 模式）：
1. 规则求值出 winner option；
2. 执行 **winner option 的 `settle.script`**（上下文寄存器：`Caster` = 活动 scope host，v4 §3.1 原样；投票者实体经 `LoadEntryPayloadEntity` 进参数，见待裁定点 #8）；
3. 实例置 `Resolved`、记 winner option id + tally 摘要、出呈现 cue、生命周期键照发（`Settled`/`Archived`）——`ActivityRuntimeService.cs` 现有结算路径原样复用。

`quorum_failure`（无 winner 路径）不执行任何 settle——"没人投票却要世界后果"是作者事故；要后果就显式写 `on_timeout: "option:<id>"`。

---

## 2. 超时（timeout）与投票的组合语义

**现状缺口确认**：v4 活动层无"挂起实例寿命"合同。`recur.cooldown` 只约束**再次派发**的资格，且冷却从派发起算、挂起实例占冷却（wiki 配方 4 明示），挂起实例本身永不过期。`ActivityInstanceCm` 有 `DispatchTick`（供冷却）但无截止时刻字段（`ActivityComponents.cs`）。

**设计**（复用既有时钟，不新增时钟域——`time-system.md` 纪律"不引入第二套 scheduler"）：

```jsonc
"timeout": {
  "ticks": 600,
  "clock": "step",              // ClockDomainId 之一（Step/FixedFrame…，见 ClockFoundation.cs）
  "on_timeout": "count"         // count | quorum_failure | "option:<id>" | "chair"
}
```

- **计时**：`IClock.Now(ClockDomainId)`——与 v4 cooldown 同一计时面（`ActivityRuntimeService.TryCooldownAdmit` 模式，`ClockFoundation.cs`）。
- **到期语义**（`on_timeout`）：
  - `"count"`：到期按当前已收票跑规则（EU4 议会到期收票；未达 quorum 也照算）；
  - `"quorum_failure"`：不结算任何选项，实例归档 + `VoteQuorumFailed` cue，无世界后果（CK3 未回应=默认拒绝的"无后果"变体由 `option:<id>` 承担）；
  - `"option:<id>"`：视为该选项得票并结算（CK3 同意弹层超时默认拒绝：`on_timeout: "option:decline"`）；
  - `"chair"`：chair 规则专有，主席票在到期时生效（EU4 房主）。
- **与 settle_when 的合成**：`rule_met`/`all_voted` 先到先结算，timeout 是兜底截止；`deadline` 时 timeout 即结算点。
- **与 recur 的交互（零新增语义）**：超时结算走现有 resolve 路径 → 实例进历史 → `mutex`/`cooldown`/`unique` 自然释放（与手动拍板后行为一致，`RebuildIndexFromWorld` 已按 Resolved 状态剔除索引）。仅确认，不改合同。
- **"游戏日"粒度**：不新增时钟域；按 v4 §5 先例，用 `MapHeartbeat`→`MapVariableChanged` 自制节拍器表达日历节拍，投票 tick 数由作者换算。

---

## 3. P社四家先例：机制层转述与映射

（以下均为机制转述，非原文文案。）

### 3.1 EU4 —— 联机事件表决 + 议会辩论（本侧最强先例）

**机制层**：
1. 联机事件：MP 弹窗归单一决策权威（房主）；他人事件不各自弹窗。弹窗未响应**不阻塞**模拟（autopause 可配置）。和平协议类交互=双人"接受/拒绝"表决。
2. 议会辩论（parliament）：议题二选一，领主按各自影响力（weight）投票；倒计时结束取加权高分者；可中途影响权重（游说/贿赂）。

**映射**：
- 机制 1 → `rule: "chair"`（房主独裁）+ `timeout.on_timeout: "count"`（未响应不阻塞，到期照跑）；
- 机制 2 → `rule: "weighted_majority"` + `weight.source: "attribute"`（影响力=代表实体属性）+ `settle_when: "deadline"`（倒计时收票）+ `allow_change: true`（可游说改票）；
- 双人接受/拒绝 → `voters: { kind: "entities" }`（2 票）+ `rule: "unanimous"`。

### 3.2 CK3 —— 联机同意弹层

**机制层**：影响他人的动作（索头衔、对玩家宣战等）触发对方同意弹窗；**限时未回应=默认拒绝**；请求方可取消。

**映射**：
- `voters: { kind: "entities" }`（影响对象，通常 2 人）+ `rule: "unanimous"`（双方点头才生效）；
- `timeout: { on_timeout: "option:<decline>" }`（限时默认拒绝）+ `settle_when: "all_voted"`；
- 请求方取消 = 通用 `activity.confirm` 撤销通道（待裁定点 #12 的"弃权/撤销"细化）。

### 3.3 Vic3 —— 法律制定的利益集团投票

**机制层**：法律由统治者提案；利益集团按 clout（力量值）**加权投票**（支持/反对/中立）；支持权重 > 反对权重即通过；提案方可中途撤回；无硬 deadline，进度型推进，拖延有代价。

**映射**：
- `rule: "weighted_majority"`（支持>反对即胜）+ `weight.source: "attribute"`（clout=代表实体属性）；
- `settle_when: "rule_met"`（过半即落槌，无 deadline）；
- 中立票 = 弃权：`abstain: "allowed"`，弃权不进规则数学（待裁定点 #2）；
- 中途撤回 = `allow_change` 的提案方侧语义（与通用撤改同通道，见待裁定点 #12）；
- 拖延代价：非 deadline，走"未通过则每步重评估"——`settle_when: "rule_met"` + 挂起不超时即自然表达，无需新增机制。

### 3.4 HOI4 —— 国策限时推进

**机制层**：单决策者选定一个国策后限时推进（进度条倒计时）；完成条件不满足则**锁住不推进**（带原因）；完成时才产生效果。无投票。

**映射**：
- 时限表达 → 本侧 `timeout`（到期强制收票）与 HOI4 focus 完成计时的区别：focus 是"自动化完成"，投票是"到点强制收票"。同构点在**用到期时刻表达阶段边界**，但投票需要规则求值收口，故单独成 `timeout.on_timeout`；
- 条件不满足锁住 → v4 `enable_when` 已覆盖（HOI4 提供的是"锁定原因直达面板"的强先例，v4 已有 `execute_condition_failed` 原因链，投票活动逐投票者求值，见待裁定点 #8）；
- 完成才产生效果 → winner settle 只跑一次（§1.6 时序），与 HOI4"完成时生效"一致。

### 3.5 先例→schema 部件总表

| 先例 | 机制 | schema 部件 |
|---|---|---|
| EU4 联机房主独裁 | 单一决策权威、未响应不阻塞 | `rule: "chair"`、`timeout.on_timeout: "count"` |
| EU4 议会辩论 | 加权 + 倒计时收票 + 可游说 | `rule: "weighted_majority"`、`weight.attribute`、`settle_when: "deadline"`、`allow_change` |
| EU4 和平协议 | 双人接受/拒绝 | `voters.entities`(2) + `rule: "unanimous"` |
| CK3 同意弹层 | 限时未回应=默认拒绝、请求方可取消 | `timeout.on_timeout: "option:<decline>"`、`settle_when: "all_voted"`、撤销通道 |
| Vic3 法律制定 | clout 加权、支持>反对即过、可撤回、无硬时限 | `rule: "weighted_majority"`、`weight.attribute`、`settle_when: "rule_met"`、`abstain: "allowed"`、`allow_change` |
| HOI4 国策 | 限时推进、条件锁住、完成才生效 | `timeout`（到期收票）、`enable_when` 逐投票者、winner settle 单次 |

---

## 4. v5 schema 增量草案（只加不改 v4 既有字段）

新增**根级 `resolve` 块**，仅 modal 活动且多投票者时出现。`options[]`、`settle`、`arrival`、`recur`、`when` 等 v4 字段**全部原样不动**。带 vote 块的活动仍必须满足 v4 全部校验（含至少一个 `is_baseline`——baseline 兼任 tie_break 默认，见待裁定点 #13）。

```jsonc
{
  "id": "world.league_crisis",
  "display_name": "列国大会：盟约存废",
  "summary": "联盟面临存废表决。",
  "arrival": "modal",
  "recur": { "cooldown": { "ticks": 1200, "clock": "step" } },

  "when": { "graph": "Graph.Gate.LeagueCrisisEligible" },

  "options": [
    { "id": "keep",  "title": "续盟", "is_baseline": true,
      "settle": { "script": "Graph.League.KeepSettle" } },
    { "id": "break", "title": "散盟", "is_baseline": false,
      "settle": { "script": "Graph.League.BreakSettle" } },
    { "id": "prolong", "title": "延议", "is_baseline": false,
      "settle": { "script": "Graph.League.ProlongSettle" } }
  ],

  "resolve": {
    "vote": {
      "voters": { "kind": "players" },
      "rule": "weighted_majority",
      "weight": { "source": "attribute", "attribute_key": "diplomacy.clout",
                  "missing": "zero", "snapshot_at": "open" },
      "quorum": { "mode": "percent", "value": 50 },
      "secret": false,
      "allow_change": true,
      "abstain": "allowed",
      "tie_break": "baseline",
      "settle_when": "rule_met",
      "timeout": {
        "ticks": 600,
        "clock": "step",
        "on_timeout": "count"
      }
    }
  }
}
```

### 字段表（全部新增）

| 字段 | 类型/取值 | 默认 | 语义 |
|---|---|---|---|
| `resolve.vote.voters` | `{kind: "players"\|"team"\|"entities", team_id?, entity_ids?, exclude?[]}` | 必填 | 投票者集合（§1.1）；`exclude` 可选排除 |
| `resolve.vote.rule` | `"plurality"\|"majority"\|"weighted_majority"\|"unanimous"\|"chair"` | `"majority"` | 决议规则（§1.2） |
| `resolve.vote.weight` | `{source: "flat"\|"attribute", attribute_key?, missing?: "zero"\|"one", snapshot_at?: "open"\|"live"}` | flat=1 | 加权规则权重来源；`snapshot_at` 默认 `"open"`（实例创建时快照，防票期属性变动） |
| `resolve.vote.quorum` | `{mode: "none"\|"all"\|"min_votes"\|"min_weight"\|"percent", value?}` | `"none"` | 最低参与门槛；未达=未达成，交给 timeout |
| `resolve.vote.secret` | bool | `false` | 密投（§1.4） |
| `resolve.vote.allow_change` | bool | `true` | 可否撤改（§1.5） |
| `resolve.vote.abstain` | `"allowed"\|"forbidden"` | `"allowed"` | 弃权开关；弃权不计规则数学（裁定点 #2） |
| `resolve.vote.tie_break` | `"baseline"\|"option:<id>"\|"random"` | `"baseline"` | 平局裁决（§1.3） |
| `resolve.vote.settle_when` | `"rule_met"\|"deadline"\|"all_voted"` | `"rule_met"` | 结算触发点（§1.6） |
| `resolve.vote.timeout` | `{ticks: int, clock: "step"\|"fixed_frame", on_timeout: "count"\|"quorum_failure"\|"option:<id>"\|"chair"}` | 无（不限时） | 挂起寿命（§2）；`ticks>0` 校验同 cooldown |
| `resolve.vote.chair` | `{voter: "player:<id>"\|"rep:<instanceId>"\|"host"}` | rule=chair 时必填 | 主席指认 |

### 命令与呈现增量

- 新命令：`activity.vote {instanceId, optionId|abstain}`——服务端从 seat possession 推导投票者身份（`ClientLocalSeatRegistry` → playerId → 代表实体），**禁止信任载荷自报投票者**（`ActivityDispatchShowcaseCommands.cs` 的 `IWebUiCommandHandler` 注册槽复用）。
- 既有 `activity.confirm` 对带 vote 块的活动**拒收**（防双通道，裁定点 #12）。
- 新呈现 cue（纯增量，v4 五种不动）：`VoteCast` / `VoteChanged` / `VoteQuorumFailed` / `VoteResolved`（含 winner + tally）。每固定步排空（`ActivityPresentationDrainSystem.cs` 机制原样）。
- 面板投影新增：每投票者状态（已投/弃权/未投）、实时 tally（公开时）、deadline tick、secret 标志；SSOT 仍是实例实体（`ActivityWebUiTopicProducer` 只读投影纪律不变）。

### 加载期校验增量（沿 v4 §6 fail-fast 风格）

- vote 块出现于 auto/pool 活动 → 拒装；
- `voters` 缺省 / `team` 无 `team_id` / `entities` 空清单 / `exclude` 引用未知 player → 拒装；
- `rule: "chair"` 无 `chair` 块 / `chair.voter` 非法 → 拒装；
- `timeout.ticks <= 0`、未知 `clock`、`weight.attribute` 无 `attribute_key` → 拒装；
- `voters.entities` 的实体未在地图放置/未绑定 → 拒装（沿用 `RequireRepresentativeInstanceId` 风格的进图校验，`ParticipantBindingResolver.cs`）。

---

## 5. 投票侧待裁定点清单

| # | 裁定点 | 倾向 | 依据 |
|---|---|---|---|
| 1 | 多数/加权过半的分母 | 有效票权重（弃权不计、quorum 另行声明门槛） | EU4 议会/Vic3 均按实际参与算 |
| 2 | 弃权语义 | 允许；进 quorum 分母不进规则数学；unanimous 下弃权不阻止 | Vic3 中立票先例 |
| 3 | 权重快照 vs 实时 | `snapshot_at: "open"` 默认（确定性、防刷权重） | 引擎确定性纪律（`deterministic-rng.md`） |
| 4 | 密投揭示时点/粒度 | 结算时揭示 tally 汇总；per-voter 明细默认隐藏，`reveal` 可选"never" | 无 P社先例，作者可选 |
| 5 | `tie_break: "random"` 的确定性流键 | 先只支持 `baseline`/`option:<id>`；random 需 per-instance 流键（v4 池抽是 per-distribution 流），延后 | `RngPickService.Pick(distributionKey)` 现状 |
| 6 | `settle_when` 默认 | `"rule_met"` | Vic3/CK3 先例 |
| 7 | timeout 时钟域 | 仅 `step`/`fixed_frame`；日历节拍走 `MapHeartbeat`→`MapVariableChanged` 自制（v4 §5） | `time-system.md` 单模拟链路纪律 |
| 8 | 投票逐票求值 `show_when`/`enable_when` 的上下文寄存器 | `Caster` 换投票者代表实体逐票求值（scope host 经 `LoadEntryPayloadEntity` 进）——**改 v4 §3.1 合同，需架构裁决 + 与作用域侧合稿** | v4 §3.1 现为"Caster=scope host" |
| 9 | 超时结算与 recur 交互 | 走现有 resolve 路径，mutex/cooldown 自然释放，零新语义（仅确认） | `RebuildIndexFromWorld` 按 Resolved 剔除 |
| 10 | `chair.voter: "host"` 的联机指代 | 待联机线（#711）对齐；单机默认 sole seat | 术语禁则/四层 seat 合同 |
| 11 | 投票历史粒度入档 | tally 摘要入历史；密投的 per-voter 明细是否入档（回放需要）待定 | `CoreSaveParticipants` activities 域现状 |
| 12 | `activity.vote` 与 `activity.confirm` 并存 | vote 块存在时 confirm 拒收；"撤回提案/撤销"是否单列命令待定 | 防双通道 |
| 13 | 投票活动是否豁免 baseline 强制 | **不豁免**——baseline 兼任 tie_break 默认，投票活动同样必须一个 baseline | v4 §2.2 校验 |

---

## 6. 引擎缺口清单（投票侧）

### A. 投票收集存储

| 项 | 状态 | 说明 |
|---|---|---|
| 选票存储 | **需新增** | `ActivityBallotCm` ECS 组件挂实例实体（voterId → option/abstain + revision + 权重快照）。非新注册表——v4 §1 纪律"零新增注册表"下，组件是实例实体状态机的自然延伸 |
| 实例身份/修订 | **已有可复用** | `ActivityInstanceCm.InstanceId/Revision/ScopeHost`（`ActivityComponents.cs`）；ballot revision 沿用其递增模式 |
| 持久化 | **已有可复用（需确认）** | activities 存档域只存 `nextInstanceId + processedSignalIds`，索引从 world 重建（`CoreSaveParticipants.cs` + `RestoreSnapshot`）→ 实例实体走实体持久化，ballot 组件随行即可；需确认实体持久化覆盖面覆盖活动实例实体 |
| 空 scope 键 | **已有可复用** | `ScopeKey(Entity.Null) == 0` 已为全局作用域预留键形（`ActivityRuntimeService.cs`） |

### B. 决议系统（规则求值 + 超时）

| 项 | 状态 | 说明 |
|---|---|---|
| 规则求值 | **需新增** | `TryResolveVote(ballots, rule, quorum, tie_break) → winner` 纯函数，无新管线 |
| 超时计时 | **已有可复用** | `IClock.Now(ClockDomainId)` + 截止 tick 存实例（`ClockFoundation.cs`；v4 cooldown 同款） |
| 每步 deadline 扫描 | **需新增** | 开放投票实例 DeadlineTick 检查；复用 cooldown 的 query 收集模式（`TryCooldownAdmit`），挂现有排水系统编排（`ActivityPresentationDrainSystem` 模式），**禁止新调度器** |
| 结算执行 | **已有可复用** | winner `settle.script` 走 `GraphExecutor` Script 图 + `ResolveOption` 结算路径，零新增求值引擎（v4 §1） |
| 平局随机 | 已有可复用（待裁定 #5） | `RngPickService` 确定性流（v4 池抽同款） |

### C. per-seat 呈现

| 项 | 状态 | 说明 |
|---|---|---|
| 每 seat topic producer | **已有可复用** | `ActivityWebUiTopicProducer(ownerScope, filterByOwnerScope)`（测试已有 scoped 实例） |
| 投票资格过滤 | **需新增** | 全局/投票活动按"投票者资格"过滤——区别于 ownerScope 过滤（投票者≠scope owner） |
| 密投/ tally/ deadline 投影 | **需新增** | 只读投影，SSOT 仍实例实体（`ActivityWebUiTopicProducer` 纪律不变） |
| `activity.vote` 命令 | **需新增** | `IWebUiCommandHandler` 注册槽（showcase confirm handler 同款，`ActivityDispatchShowcaseCommands.cs`）；投票者身份服务端从 seat possession 推导 |
| 座位→玩家→代表实体 | **已有可复用** | `ClientLocalSeatRegistry` possession + `MapConfig.Players` + `ParticipantBindingResolver` |
| 测试/AgentBridge | **已有可复用为主** | headless 直调 `ActivityRuntimeService`（`ActivityDispatchShowcaseAcceptanceTests` 模式）为验收主路；桥面复用 `ludots.events.fire`（开活动）/ `ludots.ui.query|click`（面板交互，`agent-debug-bridge.md`）；按 seat 模拟投票经 possession + 面板点击 |
| 多机联机验收 | **受已知缺口限制** | `gitbook/architecture/four-layer-architecture/terminology.md`：AgentBridge 单 discovery 目录 + 16 端口探测，并行多机受限 → 多 seat 验收以单机多 seat + headless 为主 |

### D. 防重复声明（明确不新增）

- 不新增投票注册表 / 求值引擎 / 信号总线（v4 §1：零新增注册表、零新增求值引擎、零新管线）；
- 不新增时钟域或调度器（`time-system.md` 单模拟链路纪律）；
- 不新增命令总线（复用 `IWebUiCommandHandler`）；
- 不新增第二套条件机制（逐投票者求值复用 v4 `show_when`/`enable_when` 图通道，仅上下文寄存器语义调整，见待裁定点 #8）。

---

## 7. 主要依据索引

| 断言 | 依据 |
|---|---|
| 玩家/代表实体物化、seat=本机 I/O | `commandSystem.md`；`four-layer-architecture/seat.md`；`client-local-seat-and-logic-view.md` |
| 玩家↔代表实体映射数据已就绪 | `MapConfig.Players/Teams`（`src/Core/Config/MapConfig.cs`）；`ParticipantBindingResolver.cs` |
| 准入键/ScopeKey/空 scope 键形 | `ActivityRuntimeService.cs`（`(DefinitionId, ScopeKey)` 索引、`ScopeKey(Entity.Null)==0`） |
| 实例组件现状（无截止字段） | `ActivityComponents.cs` |
| 结算路径/生命周期/呈现 cue | `ActivityRuntimeService.cs`（`ResolveOption`、`EmitLifecycle`）；`ActivityLifecycleBuffer.cs` |
| 每步排水窗口 | `ActivityPresentationDrainSystem.cs` |
| 时钟域与 IClock | `src/Core/Engine/ClockFoundation.cs`；`gitbook/architecture/time-system.md`（单模拟链路、TurnAdvanced、无全局 Turn 域） |
| OfferActivity op（TriggerGraphOnly） | `GraphOpDescriptorTable.Data.cs`：221 行；`GasGraphRuntimeApi.cs` |
| confirm 命令无 seat 维度 | `mods/showcases/activity_dispatch/ActivityDispatchShowcaseMod/ActivityDispatchShowcaseCommands.cs` |
| 面板只读投影 + ownerScope 过滤 | `src/Libraries/Ludots.WebUI.DataPlane/ActivityWebUiTopicProducer.cs` |
| activities 存档域 | `src/Core/Persistence/CoreSaveParticipants.cs`（`ActivitySaveParticipant`） |
| v4 结构/纪律/校验基线 | `.epic-briefs/activity-v4-spec.md`（§1 架构裁决、§2.2 字段、§3 上下文、§6 校验） |
| P社机制先例 | EU4 联机事件/议会辩论、CK3 同意弹层、Vic3 法律制定、HOI4 国策——均为机制层转述 |

---

## 8. 与作用域侧研究员合稿时的接缝

1. **voters 三态（players/team/entities）**是作用域分类学（per-representative/per-team/per-faction/global）在投票侧的落地面——`team` 是否够用、faction 是否另立 `voters.kind`，以作用域侧裁定为准，本侧不新增第 4 种。
2. **shared instance 的准入键**由作用域侧定；本侧只要求 vote 块 ⟹ shared instance，且 `ScopeKey(Entity.Null)==0` 已为全局键预留。
3. **`OfferActivity` 的 scopeHost 约定**（全局活动传什么实体）属作用域侧；op 签名不改。
4. 待裁定点 #8（逐投票者求值的 Caster 语义）需双方与架构裁决共同确认，是唯一改 v4 §3.1 合同的风险点。

---

**下一步建议**：本文件与作用域侧研究合稿 → 回流 `activity-v4-spec.md` §9 待办 → 以 EU4 议会/ CK3 同意/ Vic3 立法三题做对抗验证出题（本侧 schema 已可覆盖全部四家机制），验证通过后并入 #773 合同订正。
