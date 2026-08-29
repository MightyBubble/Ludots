# Activity 图化规格 v4 红队对抗出题（Vic3 + HOI4，6 案）

> 出题说明：以下 6 案全部基于两款游戏**事件系统的机制层运作方式**出题，仅转述机制（触发条件、时序、决策权归属、呈现形态），不复述任何原文文案。每题在"配置思路轮廓"里给出我认为 v4 应当怎么配的走向，真正的拷问点在"拷问点"一节——凡是我判断 v4 覆盖不了/含糊/别扭的点，都落到字段名或 op 名级别并标 severity。所有引擎现状断言均已对照仓库（GraphOps.cs 掩码策略 GraphKindOperationPolicy / GraphOpDescriptorTable、TriggerGraph 挂载域与 #1123 阶段限制、ActivityRuntimeService 准入与呈现、时钟域 ClockDomainId、事件轨 GameEvents/CustomEventCatalog 核对过）。

---

### 案例1：日志条目 + 进度积累 + 到期默认结算（维多利亚3）

1. **机制原型**
   该国的事件体系以"常驻条目"为主干：条目由**游戏状态条件**驱动出现（某领袖上台、某法律通过、某场战争开打），而非靠随机掷骰；条目可携带**进度条**——一系列后续行为（如每场胜仗、每月战争、每轮镇压）通过事件回调给条目加进度值，进度满即完成；条目可带**期限**，到期触发"默认剧本"结算（玩家不干预就按默认结局走）；同类条目**不可叠加**；每个国家独立持有自己的条目实例。

2. **玩家可观察行为**
   新条目出现时弹通知；侧栏/日志面板常驻一张条目卡片，带进度条与剩余期限提示；玩家可以打开条目查看条件、随时主动处置；若一直不动，期限一到条目自动按默认剧本结算并弹结算通知；进度条随游戏事件逐步推进；同类条目不会同时出现两份。

3. **配置思路轮廓**
   发射走 TriggerGraph 轨：订阅"状态变化"类自定义事件/MapVariableChanged，条件满足时 `OfferActivity` 到该国 scope host。条目本体 = `arrival: "modal"` 活动，`recur: "once"`（此 scope 一生一次），玩家处置 = 选项 + `settle` Script。进度 = 各触发点（胜仗/战争月度）往计数里累加，`when` Validation 图读计数过门槛；期限 = 一个倒计时节拍器，到期走默认结算。

4. **拷问点（核心产出）**
   - **[blocker] 到期默认结算没有表达路径**：`arrival: "modal"` 无任何时限字段（无 `timeout`/`expires_at`/`deadline` + 默认选项），且图侧没有能对已挂起实例强制结算或撤销的 op——全仓库只有 `OfferActivity`（462，产生实例）和玩家命令 `activity.confirm`（消费实例），不存在 `ResolveActivity`/`DismissActivity`。期限归零那一刻，图可以算出"到期了"，但没有东西能关掉或结算那个已经弹出来的 modal 实例。"玩家不干预 → 默认剧本"是这条机制的心脏，v4 表达不了。
   - **[blocker] scope 级可写状态缺位，进度/计数器无处落**：进度需要"每国各自"的持久计数。可用的图内写 op 只有 `WriteMapVarInt/Float`（map 级共享，多国互踩）与 `WriteBlackboard*`/`WriteSelfAttribute`（entity 级）——而按现状掩码 `GraphKindOperationPolicy.IsPolicyAllowed`，Script 与 TriggerGraph 只允许 Pure 语义 op，`WriteBlackboardFloat/WriteSelfAttribute/ModifyAttributeAdd` 全是 GAS 事务/委托类，**Script 和 TriggerGraph 都禁写**。结果：每国各自进度既不能落 mapvar（串扰），也不能落实体存储（掩码禁）。要么放宽 Write 掩码（不在 v4"唯一新增面清单"里），要么造"进度载体"（任务实例实体）——spec 对这条只字未提。
   - **[major] `when` 的再评估时机未定义**：§2.2 只规定发射时 `when` false 则不出现；但 modal 挂起期间、玩家打开面板那一刻，`show_when`/`enable_when` 是重新评估还是用 offer 时的快照？该机制里"条件变差 → 选项即时锁定/条目消失"是常态交互，spec 没写再评估点（每步？仅 confirm 前？仅 offer 时？）。
   - **[major] 进度条/剩余期限没有数据面字段**：§2.2 字段表与 §7"面板只读投影（SSOT=实例实体）"都没有 progress/remaining 概念；`ActivityView` 也没有。进度可以藏在 MapVar 里被 when 读，但"显示进度、显示距离到期还剩多久"需要面板/视图层新增字段，不在"引擎唯一新增面清单"。
   - **[major] 共享 Validation 图会撞 args 差集校验（§3.3）**：差集按"图内读到的 payload 键全集"对"该活动传的 args"做双向校验。而 v4 示例本身就演示了跨活动复用 Gate 图（`Graph.Gate.NoPactYet` 被多个活动引用）。只要复用的图里存在短路分支（JumpIfFalse 后才读某键），静态分析收集的"读键全集"会把可选分支也算进去，迫使每个复用活动都传全量键，否则拒装。共享图 + 可选读是图配置的常态，这条校验规则会把常态打成非法。
   - **[minor] 绝对日期/日历原语缺位**：期限"X 年后"只能折算成 tick 常量；引擎时钟域只有 FixedFrame/Step/PhysicsStep/NavigationStep，无日历概念，无日期换算 op。可绕（自制节拍器），但"折算"在纯数据侧不可审计。

---

### 案例2：两国共享一条目 + 阶段化推进（维多利亚3）

1. **机制原型**
   存在一类"共享"条目：**两个国家共同持有一条目实例**，双方都能看到、都能处置，任何一方完成即对双方了结；条目本身可分**阶段**推进——完成当前阶段后同一条目原地升到下一阶段（不是新开一个条目）；阶段推进可以立刻发生，也可以间隔一段时间。

2. **玩家可观察行为**
   两国各自看到同一卡片（标注是共享的）；甲处置后乙那边的条目立刻消失或变为"已由对方了结"；条目完成当前阶段时，若存在下一阶段则立即续上（玩家几乎无感地看到条目换了标题与条件）；若没有下一阶段，条目整体归档。

3. **配置思路轮廓**
   每个阶段 = 一个 `arrival: "modal"` 活动（`recur: "once"`），阶段链 = 前阶段的 settle 里触发下一阶段活动；"共享" = 把同一活动 `OfferActivity` 到两个 scope host；"一方完成双方了结" = 完成方 settle 里通知另一方。

4. **拷问点（核心产出）**
   - **[blocker] 多 host 共享单实例语义完全缺位**：`OfferActivity` 的 E[A] 是**单一 scope host**；活动实例、recur 状态（dedupe/once/cooldown）、历史、存档 domain 全部以单 scope 为键。没有"两个 scope 共用一个实例、先到者得"的实例模型。而且关键在撤销：甲完成时，乙那边的 pending 实例**没有任何图 op 或引擎入口能关闭它**（同案例1，只有 OfferActivity 和 activity.confirm）。`recur.dedupe` 只管"挂起时同名不叠"，不管"对方已 resolved 后我这边还挂着"。"共享 + 一方了结"连绕都绕不动。
   - **[major] 阶段链"选完立刻续弹"的直通路径未定义**：settle 是 Script 图（§4 能力面两栏都**没列** `OfferActivity`——不说能也不说不能）。按现状掩码 `OfferActivity` 是 Pure，Script 其实能调；但 spec 若不明确这条，阶段链只能绕道"settle 里 DispatchMapEvent → TriggerGraph 再 OfferActivity"。这又撞上下一矛盾。
   - **[major] `DispatchMapEvent` 的掩码归属 spec 与现状冲突**：§4 把 `DispatchMapEvent` 标为"Trigger 图专属"（Script 不能做），但现状 `CreateOperationMetadata` 将其归为 Pure，`IsPolicyAllowed` 下 Script 同样可调。若 v4 确要收紧，这是一个**掩码变更**，却不在 §1"引擎唯一新增面清单"里；若不收紧，§4 能力面表格就是错的。无论哪种，spec 自相矛盾。
   - **[minor] "同一条目换阶段"的实例/档案语义割裂**：P社侧是同一条目实体换 stage；v4 是新建一个独立活动实例。存档 domain（§7）只记 nextInstanceId + 已处理信号 id，历史里每个阶段是互不相干的 resolved 记录，没有"条目链"聚合概念；`CreateTask` 的 taskId 是编译期符号校验而 `OfferActivity` 的 activityId 是运行期 fail-closed（§6）——同一张发射图里两个 op 校验时机不一致，作者要记两套心智。

---

### 案例3：双方事件对 + AI 加权拍板 + 后果触发后续系统（维多利亚3）

1. **机制原型**
   外交危机场景下，**对抗双方各自弹出一个事件窗口**（甲、乙各一份，选项不同）；部分选项带**权重**——当一方是 AI 时，AI 按权重骰子拍板；双方选择**联动**决定局势走向（如升级/退让的组合）；选项后果不止即时数值，还能**触发后续系统**（开启战争、改变国家属性、造出债务等）；同时世界范围内其他事件照常进来排队。

2. **玩家可观察行为**
   危机爆发瞬间双方各弹窗口（AI 那方瞬间自己选完，玩家这边窗口挂着等拍板）；玩家选项旁能看到"AI 倾向于……"的提示感；玩家选完，结合 AI 的选择，局势按组合结果结算（开战/让步/升级）；开战瞬间又连锁弹出一串新事件，旧事件窗口排队在侧。

3. **配置思路轮廓**
   每个参与方 = 一个独立 `modal` 活动，双方各 `OfferActivity` 到各自 host；选项 settle 写"我方立场"到 mapvar，双方都写完后再由一个裁决 TriggerGraph/活动读双方立场出组合结果；AI 侧的拍板由"引擎替 AI 选"承担。

4. **拷问点（核心产出）**
   - **[blocker] AI 拍板整体缺位**：v4 从头到尾没有"scope host 是否人类控制"的概念，选项没有权重字段（无 `ai_chance`/`weight` 等价物），引擎没有对非人类 host 的自动确认/自动选择路径。任何 AI 国家的 modal 一挂就是永久 pending（没有图 op 能替它点 `activity.confirm`）。该机制里"必有一方是 AI"是硬约束，不是可选增强。
   - **[blocker] "选项改变世界"在两张图种里都表达不了**：settle 是 Script 图，§4 自认禁 `ApplyEffectTemplate`/`ModifyAttributeAdd`/`InvokeBuiltin`/`WriteBlackboardFloat`；而 §4 给的出路"要动 GAS 世界状态：显式走 TriggerGraph 轨（事件 → ApplyEffectTemplate）"与现状掩码**直接矛盾**——`IsPolicyAllowed` 下 TriggerGraph 同样只允许 Pure op，`ApplyEffectTemplate`/`ModifyAttributeAdd` 是 GAS 事务类，**TriggerGraph 也禁**。即：开战、改属性、造债这类该机制最典型的后果，当前引擎没有任何图种能执行；v4 若要让 TriggerGraph 动 GAS，需要一次掩码放宽，但"唯一新增面清单"里没有。
   - **[major] 双方联动 = 跨实例状态机，时序语义未定义**：组合裁决需要"两边都写完才裁决"的汇合点；但两个 modal 各自独立挂着，谁先点谁先写，没有"等待对方"或"汇合"原语。settle 同步执行 + mapvar 能硬凑（写立场 → 查对方立场 → 分支），但选项组合随选项数爆炸，且"双方同时点"的先后结果不确定——spec 对跨实例因果顺序没有任何承诺。
   - **[major] 加权随机只在图内可用，不在"选项选择层"可用**：`WeightedPick`（确定性分布 + stream salt）是图内 op，settle 里能用来选"结果"；但"AI 在 N 个选项间按权重选"这一层（P社式 ai_chance + modifier 因子）既无字段也无引擎行为。若引擎不自动拍板，这个权重机制根本无处安放。
   - **[minor] 事件窗口排队与暂停语义未定义**：危机双方窗口 + 世界事件同时到达时的队列顺序、叠层、是否暂停，spec 只说"呈现 cue 每固定步排空"（§7），没说 modal 窗口本身的排队/暂停/叠层行为。

---

### 案例4：均值时间到事件（MTTH）+ 每国独立滚动（钢铁雄心4）

1. **机制原型**
   存在一批"随机"事件：每个国家每天按"均值时间到事件（MTTH）"折算出的日概率独立滚动，命中即弹；MTTH 带**修饰因子**（国家处于某种状态时概率翻倍/减半）；事件配置 `fire_only_once`（每国一生一次）或可重复；事件弹出后选项带**权重**（AI 按权重选，玩家手选）；事件常配插图与音效。

2. **玩家可观察行为**
   玩家每隔一段不可预测的间隔收到事件弹窗；战争期间某类事件明显变频繁（因子生效）；某些事件一生只见一次，某些隔很久又来一次；AI 国家的同类事件自己会弹自己会选，不影响玩家；弹窗配图配声。

3. **配置思路轮廓**
   节拍器 = MapHeartbeat 或 TurnAdvanced；概率 = 每 tick 用 Validation/算术图算"日概率 = f(MTTH, 因子)"，`RandomFloat01` 或 `WeightedPick` 掷出；命中 → `OfferActivity` 到该国；`recur: "once"` 或 `recur: {cooldown}` 覆盖 fire_only_once / 可重复+间隔。

4. **拷问点（核心产出）**
   - **[major] 每国独立每日滚动没有现成发射轨**：实体域挂载（EntityTriggerGraphMounts）只接收生命周期类事件与实体自身事件；实体挂载**不能订阅 map 级全局事件**（#1123 phase one："entity-domain global subscriptions are not supported"），而 MapHeartbeat 对实体挂载只做死挂载清扫、**不逐实体派发**。因此"每国每天滚一次"只能由 map 级 TriggerGraph 用 `QueryAllMapEntities` + `TargetListGet` + `Jump`/`JumpIfFalse` 构造 O(N) 循环，再 `DispatchMapEvent`（self domain）逐个派发给实体挂载。能表达，但这是把"每国一个计时器"手搓成"一张全局循环图"，且与案例1 的 scope 级存储 blocker 叠加（每个实体的上次滚动/冷却无处写）。
   - **[major] 确定性随机与"每局不同"的语义未定义**：`WeightedPick` 是确定性（stream salt），`RandomFloat01` 才是随机。MTTH 的体验本质是"每局不同、不可预测"，若引擎走确定性回放路线，spec 必须定义 salt 的组合来源（scope? tick? 全局种子?）；"两个国家同 tick 同 salt 是否必然同结果"这类问题文档里一个字都没有。
   - **[minor] 因子重算的承载面含糊**："战争期间概率×2"这类修饰需要在每次滚动时用 Validation 图重读状态——纯读可做，但因子列表挂在哪（活动定义上? 图内写死?）spec 无字段；且每次滚动全量重算的代价随因子数量线性涨，spec 无性能承诺。
   - **[确认项] once/cooldown 语义可覆盖但不完全**：`fire_only_once` ↔ `recur: "once"`、可重复+最小间隔 ↔ `recur: {cooldown}` 都能对上；但 P社式"一生最多 N 次"（如最多触发 3 次）没有对应形态——once 是一次，always 是无限，没有 `max_times`。spec 的 recur union 缺"次数上限"一档（可绕：计数 + when-gate，但又要 scope 级存储，回到案例1 blocker）。

---

### 案例5：国策链事件 + 时限窗口 + 延迟接棒（钢铁雄心4）

1. **机制原型**
   事件常由国策完成触发（完成某国策 → 立刻弹事件）；事件可带**时限窗口**（窗口期内未选则事件静默关闭，**没有默认选项**，不产生任何后果）；选项可**延迟接棒**（选完 N 天后弹下一事件，形成链）；链中可有**隐藏步骤**（不弹窗直接执行效果）；事件触发带时间门（某日期之后才可能触发）。

2. **玩家可观察行为**
   完成国策瞬间弹窗；窗口右上角有倒计时感，过期事件自动消失且什么都不发生；玩家选完第一个选项，几天后第二个事件准时弹出来；某些"事件"玩家全程看不到窗口，效果直接落地；存档读档后未到期的窗口/链延续。

3. **配置思路轮廓**
   国策完成 = 自定义事件（Events/custom_events.json 声明 + TriggerGraph 订阅，`LoadEntryPayloadInt` 读国策 id）；链 = 前一环 settle 触发下一环；时限窗口 = modal 加一个"到期静默关闭"策略；延迟 = 一个每 scope 的倒计时。

4. **拷问点（核心产出）**
   - **[major] "到期静默关闭、无默认选项"与案例1 的"到期默认结算"是两个不同需求，spec 一个都没给**：即便给 modal 加 timeout 字段，也必须区分"到期自动选默认选项"（Vic3）与"到期无动作关闭"（HOI4）两种语义；v4 无时限字段、无关闭 pending 实例的 op，两种都表达不了（根源同案例1 blocker，这里从"两种到期语义都要支持"角度记为 major）。
   - **[major] 延迟接棒（N 天后下一环）没有延时原语**：settle 同步执行禁 `Yield`/`AwaitCallback`；`DispatchMapEvent` 是立即的。唯一的延迟手段是"自定义计时事件 + 每 scope 倒计时存储"——又回到案例1 的 scope 级存储 blocker + 案例4 的实体挂载无心跳问题。三案同一个根，spec 应在 §1 新增面里一次性解决（如允许 Script/Trigger 写 entity 级存储，或提供 delay 语义的事件轨），而不是让每个作者手搓节拍器。
   - **[minor] 隐藏步骤（is_immediate）只能靠 auto 近似**：`arrival: "auto"` + 根 settle 恰好对应"不弹窗直接执行"；但 auto 是"纯归档通报（无世界后果）"——如果隐藏步骤本身要动世界状态，又撞案例3 blocker；且 auto 后若要"补弹一个结果事件"，auto settle 里能否 `OfferActivity` 同样是案例2 那条未定义项。
   - **[minor] 时间门（某日期后）无日历原语**：同案例1 第6条；"1939 年后"类条件只能折算 tick 常量或自制日历 MapVar，spec 无日历/日期概念。

---

### 案例6：全球新闻事件 + 纯演出呈现 + 插图（钢铁雄心4）

1. **机制原型**
   存在一类**全球性新闻事件**：不属于任何单个国家（世界范围一份，触发一次），无选项、无决策，纯信息呈现；配插图、标题、正文、可选音效；出现在世界新闻栏/弹窗，玩家看完即过；另有大量事件把插图/演出当作纯包装——图片只挂载到标题与正文上，不参与任何判定。

2. **玩家可观察行为**
   战争爆发、条约签订等时刻弹一个"新闻"窗口：大图 + 标题 + 正文，没有选项，看完自动/手动关闭；多则新闻同时到达时按序排队展示；读档后已看过的新闻不再重复弹（全局一次）。

3. **配置思路轮廓**
   纯演出事件 = `arrival: "auto"`（不产生决策）或一个单选项 modal；全球一次性 = 对"地图级宿主" `OfferActivity` + `recur: "once"`；插图 = 在活动/选项文案字段里引用资产。

4. **拷问点（核心产出）**
   - **[major] 无宿主（global）活动没有 scope 模型**：`OfferActivity` 的 E[A] 必填 scope host。全球新闻若挂到某个国家实体上，`recur: "once"`/dedupe/历史/存档 domain 全部变成"该国的状态"——"全图一生一次"与"每国一生一次"是两种语义，spec 的 recur 是 scope-keyed 的，没有 map/global 级键。绕法：造一个永久宿主实体，但语义别扭且存档里会多一个假实体。
   - **[major] "呈现但不决策"的 arrival 形态缺位**：三态里 modal 强制"必须拍板"（且要求 ≥1 选项 + baseline），auto 是"不弹层自动结算归档"——正好缺"弹出来给人看、但不需要选择"的中间形态（P社新闻、通知式条目激活提示都属于它）。绕法只有"单选项 modal 让玩家多点一下"或"auto 玩家根本看不到"，都破坏机制。
   - **[minor] 插图/音效/资产引用无字段**：§2.2 全部是字符串（display_name/summary/title/body），无 picture/icon/portrait/sound 类资产引用字段；§7 呈现 cue 也只到事件级别。若面板由 PanelKit 渲染，资产引用需要一个新字段或约定（如 summary 内嵌 token），spec 未规划——纯演出层的"包装不参与判定"恰好是 v4 应该天然支持却什么都没说的部分。
   - **[minor] 新闻队列与暂停行为未定义**：多则新闻同时到达的展示顺序、是否暂停游戏、与 modal 活动的叠层关系，spec 无描述（"每固定步排空"只覆盖 cue 层面）。

---

## 维度覆盖对照（6 案合计）

| 考察维度 | 覆盖案 | 结论 |
|---|---|---|
| 固定日期触发 | 案例1/5 | minor：无日历原语，需自制节拍器 |
| 周期/节奏触发（MTTH） | 案例4 | major：无每 scope 独立节拍轨（#1123） |
| 条件满足即触发 | 案例1 | major：when 仅发射时评估，再评估时机未定义 |
| 权重与确定性随机 | 案例3/4 | major：选项选择层无权重字段；确定性 salt 语义未定义 |
| 事件链 | 案例2/5 | major：settle 内续弹路径未定义，延迟无原语 |
| 时限与到期默认选择 | 案例1/5 | **blocker**：无 timeout 字段、无结算/撤销 pending 实例的 op |
| AI 替玩家拍板 | 案例3 | **blocker**：无人类控制概念、无 ai_chance、无自动确认路径 |
| 多 scope 各自一份 | 案例1/4 | **blocker**：scope 级可写存储被掩码禁（WriteBlackboard/WriteSelfAttribute 仅 Effect） |
| 选项后果形态 | 案例3 | **blocker**：Script 与 TriggerGraph 都禁 GAS 写，与 §4"走 TriggerGraph 轨 ApplyEffectTemplate"自相矛盾 |
| 事件窗口排队与暂停 | 案例3/6 | minor：未定义 |
| 一次性 vs 可重复 vs 冷却 | 案例4 | minor：缺"一生最多 N 次"档位 |
| 表现层（插图/纯演出） | 案例6 | major：无"呈现但不决策"形态、无资产引用字段 |

## 全局缺口索引（按引擎新增面口径）

1. **规格自相矛盾（必须订正）**：§4 声称 TriggerGraph 轨可执行 `ApplyEffectTemplate`，现状 `IsPolicyAllowed` 对 Script/TriggerGraph 一律只放行 Pure op——要么放宽 TriggerGraph 掩码并写入"唯一新增面清单"，要么改写 §4。`DispatchMapEvent` 同理：§4 标 Trigger 专属，现状 Script 可调。
2. **"唯一新增面清单"之外、实测必须新增的能力**：(a) 对 pending 实例的图侧结算/撤销 op（或 modal timeout+到期策略字段，区分"默认结算"与"静默关闭"两义）；(b) scope 级可写存储（放宽 Write 掩码或新增）；(c) 无宿主/global scope 的活动形态；(d) "呈现但不决策"的 arrival；(e) 选项权重字段（AI 拍板）；(f) 每 scope 计时/滚动的事件轨（实体挂载订阅 map 心跳或等价的 fan-out 语义）。
3. **文档含糊（需补定义）**：when/show_when/enable_when 再评估时机；settle 内 `OfferActivity` 可否；recur 缺"次数上限"；args 差集校验对共享 Gate 图的短路分支处理；WeightedPick 的 salt 组合规则；modal 队列/暂停/叠层。
