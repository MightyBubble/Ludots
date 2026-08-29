# Activity 活动：把一次拍板摆到玩家面前

前线补给超限，撤还是推？城池刚易主，要不要通报？Activity 是"现在就得拍板"的内容容器：一段正文、若干选项、当场结算、进历史。相当于 CK3 的事件弹窗，但永远只有一层——选完即结束，需要跨周期的东西由结算效果去创建 Task。

本页四节：**能配什么情景**（八个可抄的玩法配方）→ **字段手册**（每个字段配了会怎样、写错会怎样）→ **关联表**（一条活动从加载到存档，字段和文件怎么串起来）→ 入口与验收。完整合同在 issue #773（SSOT）。

## 玩家看到什么：三条派发路径

| 路径 | 玩家体验 | 典型内容 |
|---|---|---|
| `forced` | 弹层挡在面前，必须选 | 补给超限、俘虏处置 |
| `pooled` | 周期结算点从候选池抽一个弹给玩家 | 过境商队、随机机遇 |
| `automatic` | 不弹层，直接归档为一条通报 | 归属切换、天象记录 |

弹层里的选项有四种形态：

1. **基础选项**：无任何条件，永远可选——玩家永远不会被卡死在全部点不动的弹层里（校验强制每条 forced 活动至少一个 `is_baseline`）；
2. **普通可执行**：点得动；
3. **可见但锁定**：显示出来并写明原因（如 `execute_condition_failed:world.subject_attribute`）；
4. **Gate 隐藏**：`show_condition` 不通过，整个不出现——它存在，但这一局不满足显示条件。

---

## 一、能配什么：八个玩法配方

以下 JSON 全部可跑（键均为当前生产已登记的能力键）。每一份都注明：字段在这份配置里起的作用、玩家体验时间线。抄的时候把 `source_key` 保持 `task.state_changed`（当前唯一生产事实源），触发轨见关联表一节。

### 配方 1：必须拍板的补给抉择（forced + 四形态 + 接棒 Task）

玩法意图：一个两难抉择摆到面前，选完立刻产生一条可追踪的后续任务。

```json
{
  "id": "supply.overload",
  "display_name": "补给超限：前线必须拍板",
  "summary": "连通子网供给超限的宽限期已到。撤回、推进或按兵不动，选一个，当场结算。",
  "source_key": "task.state_changed",
  "dispatch_policy": "forced",
  "repeat_policy": "repeatable",
  "options": [
    {
      "id": "hold",
      "title": "按兵不动",
      "body": "维持当前补给配置，接受宽限期结束后的断补减员风险。",
      "is_baseline": true,
      "effects": [
        { "effect_key": "task.create", "parameters": { "task_id": "task.supply.hold" }, "execution_order": 1 }
      ]
    },
    {
      "id": "withdraw",
      "title": "撤回前线的野战单位",
      "body": "把超限子网上的野战单位撤回后方锚点，供给需求立即回落。",
      "is_baseline": false,
      "effects": [
        { "effect_key": "task.create", "parameters": { "task_id": "task.supply.withdraw" }, "execution_order": 1 }
      ]
    },
    {
      "id": "forward_camp",
      "title": "在锚点展开前进补给营地",
      "body": "扩大子网供给上限——需要议事会仍有余力。",
      "is_baseline": false,
      "execute_condition": {
        "condition_key": "world.subject_attribute",
        "parameters": { "attribute_key": "Health", "op": "greater_equal", "value": 50 }
      },
      "effects": [
        { "effect_key": "task.create", "parameters": { "task_id": "task.supply.camp" }, "execution_order": 1 }
      ]
    },
    {
      "id": "request_aid",
      "title": "向盟友求援",
      "body": "请求盟友分担供给压力——双方尚无协议时此选项不应出现。",
      "is_baseline": false,
      "show_condition": {
        "condition_key": "world.subject_attribute",
        "parameters": { "attribute_key": "Health", "op": "greater_equal", "value": 9999 }
      },
      "effects": []
    }
  ]
}
```

这份配置里每个字段在干什么：

- `dispatch_policy: "forced"` → 触发即弹层，玩家必须处理；
- `repeat_policy: "repeatable"` → 每次触发都开新实例（演示用；正式内容常用 `cooldown` 限频）；
- `hold` 带 `is_baseline: true` → 永远可选的兜底出口；
- `forward_camp` 的 `execute_condition` → scope host（触发时传入的实体）Health ≥ 50 才点得动；不满足时选项**可见但锁定**，原因字符串直达面板；
- `request_aid` 的 `show_condition` 恒假（9999）→ 这一局永远不出现——把 9999 换成真实条件（如"外交协议已签订"的条件键）就是正式的 Gate 用法；
- 每个选项的 `effects` → 结算时按 `execution_order` 升序执行，`task.create` 生成后续任务（Task 定义另写在 `Tasks/tasks.json`，见关联表）。

玩家体验时间线：触发 → 弹层出现（锁定项写原因）→ 选择 → 弹层关闭、目标列表多出一条后续任务 → 历史区可查"本次选了 withdraw"。

### 配方 2：周期机遇池（pooled + 权重 + 确定性种子）

玩法意图：周期结算点随机掉机遇，但同一种子必得同一结果（可复现、可测试）。

```json
[
  {
    "id": "chance.pool",
    "display_name": "周期机遇",
    "summary": "结算点的机遇进入候选池，由确定性随机流抽出一个。",
    "source_key": "task.state_changed",
    "dispatch_policy": "pooled",
    "pool_key": "chance.pool.entries",
    "repeat_policy": "repeatable"
  },
  {
    "id": "chance.caravan",
    "display_name": "过境商队馈赠",
    "summary": "一支过境商队愿以低价出售给养。收下，还是放行？",
    "source_key": "task.state_changed",
    "dispatch_policy": "forced",
    "repeat_policy": "repeatable",
    "options": [
      { "id": "let_pass", "title": "放行", "body": "不与商队交易。", "is_baseline": true,
        "effects": [{ "effect_key": "task.create", "parameters": { "task_id": "task.caravan.pass" }, "execution_order": 1 }] },
      { "id": "buy", "title": "低价收购给养", "body": "花现金换给养。", "is_baseline": false,
        "effects": [{ "effect_key": "task.create", "parameters": { "task_id": "task.caravan.buy" }, "execution_order": 1 }] }
    ]
  },
  {
    "id": "chance.omen",
    "display_name": "天象异闻",
    "summary": "占星官记录到一次异常天象，自动归档。",
    "source_key": "task.state_changed",
    "dispatch_policy": "automatic",
    "repeat_policy": "repeatable",
    "automatic_effects": [
      { "effect_key": "task.create", "parameters": { "task_id": "task.omen" }, "execution_order": 1 }
    ]
  }
]
```

配套的候选池（`assets/Rng/distributions.json`）：

```json
[
  {
    "id": "chance.pool.entries",
    "stream": "chance_stream",
    "streamSeed": 20260828,
    "entries": [
      { "id": "chance.caravan", "weight": 60, "enabled": true },
      { "id": "chance.omen", "weight": 40, "enabled": true }
    ]
  }
]
```

- `pool_key` 两边必须一致：活动定义的 `pool_key` = 分布表的 `id`；分布表 entries 的 `id` = 候选活动的定义 id；
- 权重即份额：60:40 → 长期约六成抽中商队；`enabled: false` 临时摘牌；`locked` 冻结份额；
- `streamSeed` 固定 → 同流状态两次抽取必得同一候选（验收测试依赖这一点）；
- 池里抽中 `automatic` 候选（天象）时不弹层，直接归档通报；
- 规则：池壳本身**不能**带 `options` / `automatic_effects`（校验拒绝）；候选不能又是池。

### 配方 3：只通报不烦人（automatic）

```json
{
  "id": "report.city_taken",
  "display_name": "通报：聚落完成归属切换",
  "summary": "城·河口完成归属切换。无需拍板，已自动归档并创建追踪任务。",
  "source_key": "task.state_changed",
  "dispatch_policy": "automatic",
  "repeat_policy": "repeatable",
  "automatic_effects": [
    { "effect_key": "task.create", "parameters": { "task_id": "task.ownership_log" }, "execution_order": 1 }
  ]
}
```

- 不写 `options`（写了加载期拒绝：automatic 活动禁止选项）；
- 触发 → 效果立即执行 → 实例直接进历史，面板历史区带"自动结算"标记；
- 配 `repeat_policy: "cooldown"` 就是"每 N tick 自动例行通报一次"的节流通报。

### 配方 4：冷却型反复事件（cooldown）

玩法意图：行脚商人每隔一段时间再来一次，但同一时间窗口内不会刷屏。

```json
{
  "id": "merchant.visits",
  "display_name": "行脚商人到访",
  "summary": "商人带着货物出现在村口。",
  "source_key": "task.state_changed",
  "dispatch_policy": "forced",
  "repeat_policy": "cooldown",
  "repeat_cooldown": { "duration_ticks": 1200, "clock_domain": "step" },
  "options": [
    { "id": "browse", "title": "看看货", "body": "翻一翻担子里的货。", "is_baseline": true, "effects": [] }
  ]
}
```

- `duration_ticks: 1200` → 距上次派发不足 1200 tick 的触发被拒绝，呈现 cue 里能看到 `admission.cooldown_active`；
- `clock_domain` 选计时域（`step` 按模拟步；换时间域即按游戏内日历冷却）；
- 冷却**按 scope host 计**：每个村/势力各自冷却，互不影响；
- 冷却窗口从"派发"起算，不是从"玩家选择"起算——没处理的挂起实例也占冷却。

### 配方 5：互斥国策（mutex）

玩法意图：同一决策者同时只能推进一项国策，开了新的等于押注一个方向。

```json
[
  {
    "id": "policy.war_prep",
    "display_name": "国策：整军备战",
    "source_key": "task.state_changed",
    "dispatch_policy": "forced",
    "repeat_policy": "mutex",
    "mutex_group": "court.policy",
    "options": [ { "id": "enact", "title": "颁布", "body": "朝廷转入战时轨道。", "is_baseline": true,
      "effects": [{ "effect_key": "task.create", "parameters": { "task_id": "task.policy.war" }, "execution_order": 1 }] } ]
  },
  {
    "id": "policy.open_trade",
    "display_name": "国策：开埠通商",
    "source_key": "task.state_changed",
    "dispatch_policy": "forced",
    "repeat_policy": "mutex",
    "mutex_group": "court.policy",
    "options": [ { "id": "enact", "title": "颁布", "body": "港口全面开放。", "is_baseline": true,
      "effects": [{ "effect_key": "task.create", "parameters": { "task_id": "task.policy.trade" }, "execution_order": 1 }] } ]
  }
]
```

- 两条定义共用 `mutex_group: "court.policy"` → 同一 scope 下任一条挂起/在办时，另一条触发被拒（`admission.mutex_occupied:court.policy`）；
- 玩家把当前国策选完（resolved）后，组自动释放，另一条下次触发可进；
- 互斥同样按 scope 计：两个势力可以各推各的国策。

### 配方 6：一次性剧情抉择（unique）

```json
{
  "id": "plot.succession_vote",
  "display_name": "继承人之争：站队",
  "summary": "老王病危，两位继承人要你表态。此抉择一生只有一次。",
  "source_key": "task.state_changed",
  "dispatch_policy": "forced",
  "repeat_policy": "unique",
  "options": [
    { "id": "elder", "title": "支持长子", "body": "法统优先。", "is_baseline": true,
      "effects": [{ "effect_key": "task.create", "parameters": { "task_id": "task.plot.elder" }, "execution_order": 1 }] },
    { "id": "younger", "title": "支持次子", "body": "才能优先。", "is_baseline": false,
      "effects": [{ "effect_key": "task.create", "parameters": { "task_id": "task.plot.younger" }, "execution_order": 1 }] }
  ]
}
```

- 没选时（挂起中）再触发 → 返回同一个实例，不重复弹；
- 选完（resolved）后再触发 → 拒绝 `admission.unique_already_resolved`，此生不再来；
- 边界要知道：当前的 unique **按 scope 计**（每个 scope host 一次），"全服务器仅一次"的全局唯一在合同里、未实现。

### 配方 7：资格门槛（trigger_condition，事件根本不出现）

```json
{
  "id": "court.petition",
  "display_name": "百姓请愿",
  "summary": "民众聚在宫门外递状子。",
  "source_key": "task.state_changed",
  "dispatch_policy": "forced",
  "repeat_policy": "repeatable",
  "trigger_condition": {
    "condition_key": "world.subject_attribute",
    "parameters": { "attribute_key": "Health", "op": "greater_equal", "value": 80 }
  },
  "options": [ { "id": "hear", "title": "亲自接状", "body": "听一听民间的声音。", "is_baseline": true, "effects": [] } ]
}
```

- `trigger_condition` 不通过时**什么都不出现**，只在审计侧留下 `admission.trigger_condition_failed` cue——和"选项锁定"（玩家看得见原因）是两回事；
- 三个条件层的玩家可见性：trigger → 全隐藏；show → 该选项隐藏；execute → 选项在但写原因。

### 配方 8：挂起去重（pendingDedupe，默认行为）

```json
{
  "id": "siege.breach",
  "display_name": "城墙破口",
  "summary": "一段城墙塌了，工头等你示下。",
  "source_key": "task.state_changed",
  "dispatch_policy": "forced",
  "repeat_policy": "pendingDedupe",
  "options": [ { "id": "repair", "title": "抢修", "body": "调民夫堵上。", "is_baseline": true, "effects": [] } ]
}
```

- 默认策略（不写 `repeat_policy` 就是它）：同一 scope 已有挂起实例时，新触发**返回旧实例**，不叠弹层——"等玩家处理的队列里同名只有一条"；
- 玩家处理完之后，下一次触发才会开新实例。

---

## 二、字段手册

### 定义级

| 字段 | 必填 | 默认 | 作用 | 配了会怎样 / 写错会怎样 |
|---|---|---|---|---|
| `id` | 是 | — | 稳定标识 | 全配置合并按它去重；graph 触发、池 entries、面板都引用它 |
| `display_name` | 否 | = id | 面板标题 | 缺省时玩家看到 id |
| `summary` | 否 | 空 | 弹层正文 | 面板摘要行 |
| `source_key` | 是 | — | 声明所属事实域 | 必须是已登记 fact source（当前生产：`task.state_changed`）；写未知键 → 加载期整包拒装，错误带键名 |
| `source_subscription` | 否 | 无 | 精化订阅（信号轨用） | 内含 `source_key`（必须与根一致，且不得是生命周期键）+ `match_condition`；信号泵落地前不生效 |
| `dispatch_policy` | 否 | `forced` | 派发路径 | 见三路径表；`pooled` 必须配 `pool_key` 且禁止 `options`/`automatic_effects`；`automatic` 禁止 `options` |
| `pool_key` | pooled 时必填 | — | 指向候选池 | = `Rng/distributions.json` 里分布的 `id`；非 pooled 却写了 → 拒装 |
| `repeat_policy` | 否 | `pendingDedupe` | 出现资格 | 五种，见下节组合矩阵 |
| `repeat_cooldown` | cooldown 时必填 | — | 冷却参数 | `duration_ticks` 必须 > 0；非 cooldown 却写了 → 拒装 |
| `mutex_group` | mutex 时必填 | — | 互斥组名 | 同组同 scope 互斥；非 mutex 却写了 → 拒装 |
| `trigger_condition` | 否 | 无 | 出现门槛 | 不通过 → 不出现 + 审计 cue |
| `options` | forced 必填 | 空 | 玩家选项 | forced 至少一项且必须含 `is_baseline: true`，否则拒装 |
| `automatic_effects` | automatic 用 | 空 | 自动结算效果 | 触发即按序执行，实例直接 resolved；不写则是纯归档通报（无世界后果） |
| `presentation_cue` | — | — | 死配置 | 当前写了不生效（勿用） |

### 派发 × 重复：组合矩阵

| repeat ↓ / dispatch → | forced | pooled | automatic |
|---|---|---|---|
| `pendingDedupe`（默认） | ✅ 同名队列去重 | ✅ 池壳去重 | ✅ 同名通报不叠 |
| `repeatable` | ✅ 每次触发一弹 | ✅ 每次重抽 | ✅ 每次一报 |
| `unique` | ✅ 此生一次（按 scope） | ⚠️ 少用 | ✅ 一次性通报 |
| `cooldown` | ✅ 限频弹层 | ✅ 限频抽取 | ✅ 节流通报 |
| `mutex` | ✅ 同组互斥 | ⚠️ 少用 | ⚠️ 少用 |

### 选项（options[]）

| 字段 | 必填 | 作用 | 配了会怎样 |
|---|---|---|---|
| `id` | 是 | 选项标识 | 历史/效果回执引用它；结算后历史记录"所选选项 id" |
| `title` / `body` | 否 | 按钮文案 / 展开说明 | 面板显示 |
| `is_baseline` | 至少一项 | 基础选项 | 永远可执行（跳过 execute 检查） |
| `show_condition` | 否 | Gate | 不通过 → 该选项**不出现** |
| `execute_condition` | 否 | 执行条件 | 不通过 → 可见但**锁定**，原因直达面板 |
| `effects[]` | 否 | 结算效果 | 选择后按 `execution_order` 升序执行 |

### 效果引用（effects[] / automatic_effects[]）

| 字段 | 必填 | 默认 | 作用 |
|---|---|---|---|
| `effect_key` | 是 | — | 已登记 **Provider Effect** 键（不是 GAS EffectTemplate；清单见关联表 §0） |
| `target_reference` | 否 | `context.subject` | 效果落点（当前限 scope host） |
| `parameters` | 否 | 空 | 按效果键的参数表传参 |
| `execution_order` | 否 | 0 | 同一结算内多效果的执行顺序，升序 |

### 条件引用（trigger / show / execute / match 共用同一形状）

| 字段 | 必填 | 作用 |
|---|---|---|
| `condition_key` | 是 | 已登记条件键（清单见关联表） |
| `parameters` | 按条件要求 | 传给条件的参数 |

---

## 三、关联表

### 0. 效果与上下文：和 GAS 的关系

活动选项的 `effect_key` 走的是 **Provider Effect 合同**，不是 GAS 的 EffectTemplate。两套对照：

| | GAS EffectTemplate | Provider Effect（活动/任务用） |
|---|---|---|
| 注册 | `GAS/effects.json`（`Effect.*`），进 `EffectTemplateIdRegistry` | `IEffectHandler` 代码注册进 `ProviderServices.Effects` |
| 调用方 | GAS 效果管线（graph `ApplyEffectTemplate` → EffectRequestQueue → BuiltinHandler） | 活动结算直接执行（`MustGet(effect_key).Execute()`） |
| 内容 | 属性增减、buff、挂 tag 等战斗数值 | 内容层动作：`task.create`、`activity.offer` |
| 桥 | 两套目前互不直达——活动结算想触发一张 GAS 效果牌，要等共享合同（issue #775）收敛 | |

上下文透传：条件和效果拿到的是 `ProviderExecutionContext`，只有三样——`World`（ECS 世界）、`Subject`（scope host 实体，触发时传入、存在实例上、结算时还原）、`Bindings`（字符串字典，键按来源加前缀 `context.*` / `signal.*`）。各阶段 Bindings 内容：

| 阶段 | Bindings | 后果 |
|---|---|---|
| graph 轨准入（`OfferActivity`） | 空 | trigger_condition 只能用读 world/主体的条件 |
| 信号轨准入 + automatic 结算 | `signal.*`（信号参数 + `source_key`/`signal_id`/`occurred_at`/`scope_ref`/`object_refs`） | automatic 效果可引用触发信号的对象 |
| forced 结算（`ResolveOption`） | 空（面板 confirm 命令不传绑定） | **触发信号的对象在结算时已丢**（context_bindings 缺口，见边界节） |

`target_reference: "context.subject"` 字段在合同里，但当前两个生产 effect 的处理器都直接落在 scope host 上，字段暂不区分落点。

### 1. 一条活动的生命周期：字段在哪个阶段被读

| 阶段 | 读什么 | 结果 |
|---|---|---|
| 加载 | 全部键名、`effect_key`/`condition_key`/`source_key`、结构约束（baseline、pooled 禁 options、cooldown 配套…） | 非法配置整包拒装，错误带键名 |
| 触发 | graph `OfferActivity` 节点（或 `activity.offer` 效果）携带的 `activityId` + scope 实体 | 进入准入 |
| 准入 | `repeat_policy`（+cooldown/mutex）、`trigger_condition`、（pooled）`pool_key` → 权重抽取 | 拒绝 → 审计 cue；通过 → 创建实例实体 |
| 呈现 | `options[].show_condition`、`execute_condition`、`is_baseline` | 面板拿到四形态选项列表 + 锁定原因 |
| 结算 | 所选选项 `effects[]` 按 `execution_order`；automatic 的 `automatic_effects` | 效果执行；实例 resolved；历史带所选选项 id |
| 历史/存档 | 实例实体（含状态与所选选项）+ 快照（`nextInstanceId`、已处理信号 id） | 存档 domain `activities`；读档后弹层/冷却/唯一性照旧 |

### 2. 可用能力键清单（当前生产）

| 类别 | 键 | 参数 | 作用 |
|---|---|---|---|
| effect | `task.create` | `task_id`（必填） | 创建/启动一条 Task——选项后果接棒长期追踪的正路 |
| effect | `activity.offer` | `activity_id`（必填） | 程序化派发活动（注意单层纪律：选项效果里**不要**用它开第二个活动） |
| condition | `world.subject_attribute` | `attribute_key`、`op`（greater/greater_equal/less/less_equal/equal）、`value` | 读 scope host 的 GAS 属性做比较；主体无属性 → 判否；未知属性键 → 硬失败 |
| source | `task.state_changed` | — | 当前唯一生产事实源（声明用） |

要"投入兵力接管城池"这类玩法域效果，须先登记对应 effect 键再引用——合同禁止拿近似键顶替。

### 3. 跨文件关联（一份完整内容要动哪些文件）

```
assets/config_catalog.json           声明 Activities/activities.json（不声明 = 永远不加载）
assets/Activities/activities.json    活动定义（本文档主角）
assets/Tasks/tasks.json              选项效果 task.create 指向的任务定义
assets/Rng/distributions.json        pooled 的候选池：id ↔ pool_key，entries[].id ↔ 候选活动 id
assets/GAS/graphs.json               触发轨：事件 → OfferActivity{activityId}（scope 用 LoadPlacedEntity 取）
assets/Events/custom_events.json     触发轨用的自定义地图事件声明
assets/Maps/<map>.json               放置 scope 实体（供 LoadPlacedEntity）
Assets/PanelKit/panel_manifest.json  事件面板绑定（panelType activity，topic/profile）
```

面板命令：`activity.confirm {instanceId, optionId}`（确认选项）。呈现 cue 五种：`Presented` / `OptionBlocked` / `Resolved` / `AutomaticSettled` / `AdmissionRejected`（带原因码），面板"审计侧"和 UAT 都消费它。

---

## 入口与验收

| 项 | 值 |
|---|---|
| 可玩 showcase | `activity_dispatch`（registry），启动 preset `activity_dispatch_cef_raylib` |
| 可抄完整内容 | `mods/showcases/activity_dispatch/ActivityDispatchShowcaseMod/Assets/` |
| headless 验收 | `ActivityDispatchShowcaseAcceptanceTests`（三路径端到端 / 池抽确定性 / 呈现排水） |
| 单元与桥接测试 | `src/Tests/GasTests/Integration/Activity*.cs`（61 项） |
| 证据目录 | `artifacts/acceptance/activity_dispatch/` |

## 边界与已知缺口

- **forced 结算时拿不到触发信号的对象**（定义级 `context_bindings` 未落地）：选项效果 target 限定 `context.subject`（scope host）。"撤回刚才那支部队"这类引用触发对象的写法，等合同 A 线补齐后再用；
- 信号订阅轨（`IntakeSignal`）已实现但生产尚无事实源泵——当前触发走 graph 派发轨；共享 Source 合同见 issue #775；
- unique/cooldown/mutex 均按 scope host 计，全局维度未实现；
- 生命周期引擎键（`activity.started` 等）目前只进呈现缓冲，事件总线订阅面是 issue #818；
- `presentation_cue` 字段暂为死配置（写了不生效）。
