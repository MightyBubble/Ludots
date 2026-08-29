# Activity 图化规格 v4（对抗验证稿）

状态：草案。待"P社四家事件结构"对抗出题验证通过后，定稿为 #773 合同订正正文并同步 wiki 手册。
本文是唯一讨论汇总 SSOT；与本文冲突的早期草案（settle_graph/trigger_graph 命名版、provider 求值版）全部作废。

## 1. 架构裁决记录

1. **Provider 求值层废弃**：条件与结算不再走 Provider 注册表（ISource/ISelector/ICondition/IEffectHandler）。引擎已有图 VM（六种图 + 150 op + GraphExecutor 执行门面），第二套求值系统属重复造轮子。
2. **Provider 发射层同判**：事实发射已有两条现成通道（引擎事件总线 + 图内事件 op），ISourceProvider 是第三条信号总线，且其 Emit 通道经核实全仓零读者。#775 若保留 Source 合同，收敛为键声明与校验。
3. **图种分工**：
   - 条件（bool 谓词）= **Validation 图**，`GraphExecutor.ExecuteValidation`；
   - 选项结算/自动结算（副作用）= **Script 图**，同步执行——Script 图的 op 掩码天然碰不到 GAS 效果管线（ApplyEffectTemplate/ModifyAttributeAdd/InvokeBuiltin 均为 Effect 图专属），"要不要动 GAS"是作者的显式选择；
   - 派发（把活动摆到玩家面前）= **TriggerGraph** 的 `OfferActivity` op；
   - 任务接棒 = Script/Trigger 图的 `CreateTask` op（本次唯一新增 op）。
4. **发射者统一为 TriggerGraph 轨**：时间、区域、击杀、UI、未来事实信号，全部汇成"一张图订阅事件 → OfferActivity"。活动 schema 不再含任何发射字段（`source_key` 删除，信号轨 #775 落地时以 `subscribe` 回归）。
5. **Caster=scope host**：条件/结算图执行的上下文寄存器合同（见 §3）。

### 引擎唯一新增面清单

| 新增 | 形态 |
|---|---|
| `CreateTask` op（enum 463） | Script/Trigger 可用，参数 `taskId`，调 `TaskRuntimeService.OfferOrStart` |
| `LoadEntryPayload{Int,Float,Entity}` 掩码放宽 | TriggerGraphOnly → +Script +Validation（纯读，开参数通道） |
| 调用入口带 EntryPayload 表 | 活动运行时把 args 装进载荷（与 InvokeGraph 传参同机制） |

除此之外零新增注册表、零新增求值引擎、零新管线。

## 2. v4 配置结构

### 2.1 完整示例

```jsonc
{
  "id": "supply.overload",
  "display_name": "补给超限：前线必须拍板",
  "summary": "连通子网供给超限的宽限期已到。撤回、推进或按兵不动，选一个，当场结算。",

  "arrival": "modal",                                            // 怎么到玩家面前
  "recur": { "cooldown": { "ticks": 1200, "clock": "step" } },   // 能不能再次出现

  "when": { "graph": "Graph.Gate.CourtRep", "args": { "threshold": 80 } },

  "options": [
    {
      "id": "hold", "title": "按兵不动", "body": "维持现状，接受减员风险。",
      "is_baseline": true,
      "settle": { "script": "Graph.Supply.LogSettle", "args": { "verdict": "hold" } }
    },
    {
      "id": "forward_camp", "title": "在锚点展开前进补给营地",
      "body": "扩大子网供给上限——需要议事会仍有余力。",
      "is_baseline": false,
      "show_when":   { "graph": "Graph.Gate.NoPactYet" },
      "enable_when": { "graph": "Graph.Gate.VigorAtLeast", "args": { "threshold": 50 } },
      "settle":      { "script": "Graph.Supply.CampSettle", "args": { "camps": 1 } }
    }
  ]
}
```

automatic 活动：根字段 `"settle": { "script": … }`，不写则纯归档通报（无世界后果）。

### 2.2 字段表

| 字段 | 类型 | 必填 | 语义 |
|---|---|---|---|
| `id` / `display_name` / `summary` | string | id 必填 | 身份与玩家可见文案 |
| `arrival` | `"modal"` \| `"auto"` \| `{ "pool": <分布id> }` | 默认 `"modal"` | modal=弹层必须拍板；auto=不弹层自动结算归档；pool=确定性候选池抽取 |
| `recur` | `"dedupe"` \| `"always"` \| `"once"` \| `{ "cooldown": { "ticks": int, "clock": string } }` \| `{ "mutex": <组名> }` | 默认 `"dedupe"` | dedupe=挂起时同名不叠；always=每次触发都来；once=此 scope 一生一次；cooldown=限频；mutex=同组同 scope 互斥 |
| `when` | `{ graph, args? }` | 否 | Validation 图，false → 活动不出现（审计留 cue） |
| `options[]` | 见下 | modal 必填 | 玩家选项；至少一个 `is_baseline: true` |
| `settle`（根，auto 用） | `{ script, args? }` | auto 用 | Script 图，触发即执行后归档 |

选项字段：`id`（必填）、`title`、`body`、`is_baseline`（永远可执行）、`show_when`（Gate：false → 不出现）、`enable_when`（false → 可见但锁定，原因直达面板）、`settle`。

**值对象内层键规则**：`when/show_when/enable_when` 内层键固定 `graph`（必须 Validation 种）；`settle` 内层键固定 `script`（必须 Script 种）。字段名说语义（什么时候/干什么），图种由内层键 + loader 双重钉死。`trigger` 一词在活动 schema 永久禁用（保留给 TriggerGraph 图种）。

### 2.3 废弃别名（C# enum 同步重命名，本批未发布无兼容包袱）

| v4 | 废弃词 |
|---|---|
| `modal` | forced |
| `auto` | automatic |
| `{pool}` | pooled + pool_key |
| `dedupe/always/once` | pendingDedupe/repeatable/unique |
| `{cooldown}/{mutex}` | cooldown+repeat_cooldown / mutex+mutex_group |
| `when/show_when/enable_when` | trigger_condition/show_condition/execute_condition |
| `settle` | options[].effects / automatic_effects |
| （删除） | source_key、source_subscription、presentation_cue |

## 3. 上下文与参数合同

1. **寄存器**：条件/结算图执行时 `Caster = 活动 scope host`（实例 ScopeHost 还原）；`ExplicitTarget = 空`（context_bindings 落地后扩展，实体经 `LoadEntryPayloadEntity` 进来）。
2. **参数通道**：配置侧 `args`（标量：数值/布尔 0|1/字符串）→ 运行时装入 EntryPayload → 图内 `LoadEntryPayload{Int,Float,Entity} payloadKey` 读。属性名/实体引用不能作参数（见能力面）。
3. **args 键集差集校验（加载期）**：图内读了 payload 键而活动未传 → 拒装（防静默得 0）；活动传了图不读的键 → 拒装（防拼错）。
4. **同步语义**：`settle` 的 Script 图禁 `Yield`/`AwaitCallback`——加载期扫描拒装。"当场结算"与切片执行不兼容，不提供半异步。
5. **属性缺失语义（已裁定倾向，待最终确认）**：`LoadSelfAttribute` 对无 AttributeBuffer 主体静默得 0——`>=` 型门槛自然 false 安全，`<=` 型会 fail-open。裁定：接受 + 文档明示"作者自证 scope 有属性"，不新增 HasAttribute op。

## 4. 结算脚本能力面（类型系统裁定）

| Script 图**能**做 | Script 图**不能**做 |
|---|---|
| `LoadCaster` / `LoadSelfAttribute` / `LoadExplicitTarget` / `LoadAttribute`（读属性） | `ApplyEffectTemplate` / `ModifyAttributeAdd` / `InvokeBuiltin` / `WriteBlackboardFloat`（GAS 效果管线，Effect 图专属） |
| `CreateTask`（建任务接棒） | `DispatchMapEvent`（Trigger 图专属） |
| `WriteMapVarInt/Float`、`ReadBlackboardFloat` | `Yield` / `AwaitCallback`（settle 内禁） |
| `SpawnTemplate`、`ShowPanel`、`SetWorldPosition` | —— |
| 算术/比较/文本拼接/`WeightedPick`（确定性加权抽取，命名流） | —— |
| `InvokeScript`（调其它 Script 图，args 经 InvokeArgs） | —— |

要动 GAS 世界状态：显式走 TriggerGraph 轨（事件 → ApplyEffectTemplate）。

## 5. 发射者（TriggerGraph 轨）配置

```jsonc
// Events/custom_events.json（引擎事件不够用时声明自定义事件）
[{ "id": "Court.DailyAudit", "scope": "map", "params": [] }]

// GAS/graphs.json
[{
  "id": "Graph.Rhythm.Daily",
  "kind": "TriggerGraph",
  "entries": [
    { "label": "on_heartbeat", "event": "MapHeartbeat", "start": "scope", "refire": "restart" },
    { "label": "on_turn",      "event": "TurnAdvanced",  "start": "scope", "refire": "restart" },
    { "label": "on_region",    "event": "RegionEntered", "start": "scope", "once": true,
      "filters": { "region": "raid_circle" } },
    { "label": "on_var",       "event": "MapVariableChanged", "start": "scope", "refire": "restart",
      "filters": { "varName": "supply.strain", "threshold": 100, "direction": "cross_above" } }
  ],
  "nodes": [
    { "id": "scope", "op": "LoadPlacedEntity", "instanceId": "council" },
    { "id": "offer", "op": "OfferActivity",    "activityId": "supply.overload" },
    { "id": "done",  "op": "HaltReturnInt" }
  ],
  "controlEdges": [{ "from": "scope", "fromPort": "next", "to": "offer" }],
  "valueEdges":   [{ "from": "scope", "fromPort": "value", "to": "offer", "toPort": "source" }]
}]

// Maps/<map>.json —— 心跳节奏
{ "HeartbeatIntervalTicks": 120 }
```

时间类事件：`MapHeartbeat`（节奏=地图 `HeartbeatIntervalTicks`）、`TurnAdvanced`（回合推进）、`MapVariableChanged`+threshold/direction（自制节拍器/累积器）。UI 触发：命令 fire 自定义地图事件。池抽节奏同轨（`OfferActivity` 指向 pool 型活动定义）。

## 6. 加载期校验清单（fail-fast）

- 未知图 id / 图种不符（`graph`≠Validation、`script`≠Script）→ 拒装，错误带图 id 与实际种名；
- settle 图含 `Yield`/`AwaitCallback` → 拒装；
- args 键集差集（§3.3）→ 拒装；
- modal 无选项 / 无 baseline / auto 带选项 / pool 壳带选项或 settle → 拒装；
- recur 对象形态参数完整（ticks>0、mutex 组非空）→ 否则拒装；
- `CreateTask` 的 taskId 未在 Tasks/tasks.json 注册 → 拒装（图编译期符号校验）。

## 7. 呈现、命令与历史

- 呈现 cue 五种：`Presented` / `OptionBlocked`（含原因 `enable_when_failed:{图id}`）/ `Resolved` / `AutoSettled` / `AdmissionRejected`（`recur.cooling` / `recur.mutex:{组}` / `when_failed` / `pool_empty:{池}`）。每固定步排空。
- 面板命令：`activity.confirm {instanceId, optionId}`；面板为 PanelKit panelType `activity`，只读投影（SSOT=实例实体）。
- 历史：resolved 实例带所选选项 id；auto 带自动标记。
- 存档：domain `activities`（nextInstanceId + 已处理信号 id）。定义侧图 id 不落盘。

## 8. 迁移映射（旧→新）

| 旧（provider 时代） | 新（v4） |
|---|---|
| trigger/show/execute_condition（condition_key） | when / show_when / enable_when（Validation 图 + args） |
| options[].effects[]（effect_key 数组 + execution_order） | options[].settle.script（Script 图，顺序即图内控制流） |
| automatic_effects | 根 settle.script |
| task.create 效果 | 结算脚本内 `CreateTask` op |
| activity.offer 效果 | 删（单层纪律禁止选项开活动；派发归 TriggerGraph OfferActivity） |
| world.subject_attribute 条件 | Validation 图 `LoadSelfAttribute + Compare*` |
| source_key | 删 |
| dispatch/repeat 枚举与伴生字段 | arrival / recur（union） |

## 9. 待办与未决

- [ ] 属性缺失 fail-open 裁定最终确认（§3.5，倾向接受）；
- [ ] `CreateTask`/`taskId` 命名终确认（按仓库 camelCase 惯例）；
- [ ] S0–S7 实施序列（分支/提交/切片见评审汇总裁决）；
- [ ] 对抗验证（本文目的）：P社四家事件结构出题 → 逐题配置验证 → 缺口清单回流本节。
