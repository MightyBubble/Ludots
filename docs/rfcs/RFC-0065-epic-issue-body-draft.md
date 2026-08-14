> Historical issue draft. The current architecture has passed the planned endpoint:
> no formal SelectionRuntime fallback, no SelectionSetKeys/OrderSelectionReference for command
> authority, and default command source is `collection.command.source`.
<!-- 建议 Issue 标题： -->
<!-- [Epic] 统一交互—集合—施法架构：Context Stack / Control Plane / Provenance / Panel Router / Cast Dispatch（整合 #536 #537 #538，修订 #522 遗留） -->

## 一句话

把「框选/画线/点选/快捷键」到「技能真正执行」之间的全部环节，拆成一条**零硬编码、全数据驱动、领域解耦**的单向管线；归属语义走 relationship，集合语义走 EntityCollection，UX 差异只体现在 catalog / profile / preference 数据里，Core 无任何 genre 分支。

## 设计 SSOT

- RFC 正本：`docs/rfcs/RFC-0065-unified-interaction-collection-casting-architecture.md`（本 Epic 分支 PR）
- 本 RFC 整合并取代 RFC-0061/0062/0063/0064 的**规划与验收**；其技术结论保留。
- 原 #536 / #537 / #538 的子单迁移到本 Epic 的对应 Phase；#522 剩余尾巴（PR #535 body P1~P5）并入，消除双轨追踪。

## 愿景管线（每层只读上一层产物，禁止反向调用）

```text
[L1 Device] 原始设备输入
[L2 Remap]  ControlScheme（IMC 组合，玩家可热切换/改键）→ InputAction（DEC-15）
[L3 Ctx]    InteractionContextStack：frame 决定 activeCollectionKey/activeViewKey/inputContextId/commandIntentId
[L4 Cast]   InputCastSpec（box/polygon/ray/lasso × screen/world/minimap）→ raw hits collection
[L5 Filter] FilterProfile（association query DSL）→ filtered
[L6 Coll]   CollectionWrite → 按所属域路由到 (domainRepEntity, activeKey)，row 记 writerDomain
[L7 Panel]  EntityView + PanelRouter + AggregationProfile → HUD 投影
[L8 Commit] 施法键：CastCommitProfile（激活 ops：pushFrame/popFrame/submitOrder）+ ClientCastPreference
            pointer 命令：CommandIntentProfile（actor 谓词 × target 事实 → route，显式全序，DEC-14）
            —— 无状态机：client 侧状态 = 栈上 frame，sim 侧状态 = exec 实体 tag（DEC-13）
[L9 Disp]   CastDispatchProfile（selector/scorer/router，scorer 复用 UtilityAI）
[L10 Order] OrderQueue（唯一 intake）→ OrderBuffer → AbilityExec / MassNav
[L∥ Perf]   Presenter 只读 collection revision + provenance + catalog → marker/相位
```

## 铁律

1. OrderQueue 唯一 intake；MassNav 只消费 OrderBuffer。
2. 「Selection」只是 default context 下 `collection.command.source` 的俗名；`SelectionRuntime` 退役为非 hub；Order payload 自包含目标集或引用 `(owner, collectionKey, revision)`。
3. Embodied entity 零 `PlayerOwner`/`Team`/`PlayerIdentity`；归属只存在于 `owns`/`controls`/`member_of`/`ally` 边。
4. 控制平面只走 `ControlDomainQuery`；阵营/敌我判定只走 `DomainStanceQuery`（关系图 SSOT + 热路径缓存；stance key 是 catalog 数据，Core 无 "hostile" 字面语义）。
5. 代理控制只增删 `controls` 边；collection namespace per playerRep，禁止 cross-player merge 与 PlayerId 全局表。
6. Context frame 带 ownerToken，按 token 移除；push/pop 联动 IMC（inputContextId）。
7. InputCast / Filter / Commit / Presentation 四轴正交；`InteractionModeType` 全部退役为 CastCommitProfile 数据。
7a. **零施法状态机**：Input 层不得持有任何施法 FSM / `_isAiming` 类字段 / states-transitions schema；client 侧唯一交互状态 = InteractionContextStack 上的 frame，sim 侧唯一施法进度 = exec 实体上的 tag + attribute（DEC-13）。
7b. **Presenter 的 casting 表现只消费通用事件**：order 生命周期、ability exec / effect 生命周期、attribute / tag 变化、collection 成员与 revision、entity 生命周期——零 aim/cast 专用事件种类；`AbilityAimBegun/Updated/Ended/SlotAdvanced` 退役（DEC-13）。
7c. **Pointer 命令语义全部数据**：Core intent/route 路径零 "attack"/"garrison"/"move" 字面量；语义路由零裸 slot index（ability 一律 catalog tag / contextGroup 定位）；同 profile priority 冲突加载期 fail-fast（DEC-14）。
7d. **L8 target 事实必经 viewer KnowledgeProjection**：fog/伪装按投影事实路由，禁读 sim 真值；路由产物只是 order 请求，合法性由 sim 侧 GAS targeting 终裁（DEC-14）。
8. Presenter 只读；裁判走 KnowledgeProjection grant，禁止 RefereeSelectionService。
9. 面板是投影不是控制器：PanelRouter 单向消费，UI 不得反打 input action。
10. 聚合/排序/路由规则全部 catalog/profile；确定性：本地偏好与 context stack 不隐式进入 sim。
11. **Association/Collection 层零业务语义**：掉线、心灵控制、演出接管等只是 trigger 数据（tag / 边增删）；基建 schema 与代码不识别任何场景词汇。
12. **Collection 永不跨域迁移**：写入按所属域路由；跨域指挥一律 = controls 拓扑 + ControlPlaneView 组合视图，不存在「归还」操作。

## 关键设计决策（DEC，详见 RFC §4）

- **DEC-1** `controls` = 查询期视图（owns ∪ 显式 grant），只物化代理 grant 边。
- **DEC-2** Relationship 反向邻接索引先行（现状 `CollectIncoming` 全表扫描不可用于 provenance）。
- **DEC-3** `DomainStanceQuery` 是删 unit `Team` 的硬前置（GAS targeting 热路径）；stance key（hostile/friendly/…）全部是 relationship catalog 数据，Core 零 "hostile"/"enemy" 字面分支（早期名 HostilityQuery 因携带业务语义废弃）。
- **DEC-4** 控制平面 = 拓扑投影：CollectionWrite 按所属域路由（队友单位写队友域，writerDomain 追踪）；「我的选中」= ControlPlaneView（controls 可达域组合只读视图）；边消失即"归还"，无 handback 概念，无 policy 枚举；掉线/心控/演出只是 mod trigger，association 层零感知。路由策略（byControlDomain / toContextOwner）是 collection profile 的声明字段——技能目标类 key（选的是目标而非"我维护的域"）写 context owner 域。
- **DEC-5** Provenance 由地址承载：controlDomain 即 row 所在域；relationKind（owns/controls）由 viewer→域拓扑现算，不写入行——陈旧 marker 问题不存在；写时仅记 writerDomain。
- **DEC-6** 并发等待确认（tag）的 exec 各持 frame，Tab 在 frame 间循环。
- **DEC-7** InteractionContextStack 与 IMC 同事务联动。
- **DEC-8** FilterProfile 契约归 Context/Input 域，association provider 由 Control Plane 注入（拆循环依赖）。
- **DEC-9** Dispatch scorer 复用 `UtilityAiRuntimeEvaluator`（RFC-0060），禁止平行 scorer。
- **DEC-10** 面板聚合 = ability catalog 字段（castFamily/alias）+ AggregationProfile + 玩家偏好覆盖；`groupBy` 是 catalog 字段取值路径表达式（如 `catalog.castFamily`），非封闭 enum。
- **DEC-11** 新 profile 的"动词/种类"一律走注册表：interaction op（pushFrame/popFrame/submitOrder——基建原语，非施法语义）、dispatch 的 selector/scorer/router kind、payload value source 全部为 registry 注册项（对齐 graph op / SystemFactoryRegistry 先例），禁止新增 Core-only 封闭 enum 分派。"cancel" 不是 Core 概念，只是 mod 数据里某个 action 映射到 popFrame。
- **DEC-12** Presenter 基建核对结论：event→condition→command→behavior 四层已成立，`EntityCollectionMemberAdded/Removed` 事件已带 collection key/owner/member/roleId/revision，本 Epic 零新增事件种类；但需修四个执行侧硬点——graph condition 注入 viewer + payload 寄存器与拓扑谓词 ops（PROV-4b）、接线 VisibilityCondition graph Emit 路径（PROV-4c）、退役 `TeamColorResolver`/`PerformAudienceContext` 的 Team/PlayerOwner 硬编码（PROV-6b）；`PresentationEventKind`/`PresenterCommandKind`/`BehaviorKind` 等封闭 enum 列为已知边界，不阻塞本 Epic UAT、不在本 Epic 修。
- **DEC-13** 零施法状态机（取代早期草案 CastFlowProfile FSM）：所谓"施法状态"拆解后只剩两个已有载体——client 侧 = InteractionContextStack 上的 frame（「瞄准中」就是 frame 在栈上，没有布尔/枚举），sim 侧 = exec 实体上的 tag + attribute（多段/打断/冲突/蓄力全部落在 GAS 已有仲裁：tag requirement、effect 打断、exec abort），不引入第三个状态概念，也因此不需要自建多层叠加/冲突/打断仲裁。蓄力量由 begin/commit 两条 order 之间的 sim tick 差在 exec 内累计（确定性，client 计时被禁止）。CastCommitProfile 只声明「激活时执行什么 op 序列 + frame 内 action→op 映射」。Presenter 事件面收敛：casting 表现只消费 order / exec / effect 生命周期（现有 `CastCommitted`/`CastFailed`/`EffectApplied`/`EffectActivated`）+ `TagEffectiveChanged` + `AttributeValueChanged` + collection 事件；`AbilityAimBegun/Updated/Ended/SlotAdvanced` 专用事件与 `AbilityAimPresentationRuntime`/`AbilityAimSessionState` 退役（`collection.ability.aim.*` 作为普通 collection key 保留为 mod 数据）。pre-order 的 client 瞄准预览与 post-order 的 sim 等待确认共用同一 frame 结构，只是 push 发起者不同（client op vs exec lifecycle）。
- **DEC-14** Pointer Command Intent：`CommandIntentProfile` = pointer intent 的 per-actor 规则表（actor 谓词 × target 事实谓词 → route）。谓词是统一 condition DSL 的 shorthand（加载期 lower 到唯一 evaluator）；target 事实**必经 viewer KnowledgeProjection**（fog 门控复用 `CanTargetCommand`；伪装按投影事实路由且 target 归属域取投影所见域——tag/stance 级事实投影是新基建，由 INT-8 认领，未落地前伪装 UAT deferred；sim 侧 GAS targeting 终裁）；stance 谓词 = `GetStance(actor 所属域, target 所属域) ∈ 集合`（any-of，代理控制下按 actor 域）。唯一胜出 = 显式 priority 全序（同 priority 加载期 fail-fast），胜出即终局（route 解析失败不落穿）；动态评分显式委托 contextGroup（复用 `ContextScoredOrderResolver`，其候选查询补 knowledge 过滤）。语义路由一律 `byAbilityTag`（"攻击"= weapon catalog tag → 通常解析到 slot0，但那是数据事实非 Core 约定）。混合框选 = per-actor 路由 + 显式 `groupPolicy`（registry：independent / bySelector——复用 DSP selector，不引入 "leader" 概念；profile 顶层唯一）。与 frameActions 仲裁：精确匹配先拦截 → 栈顶 frame 自己的 commandIntentId → 无则不路由不冒泡（无 fallback）。与 Dispatch 两阶段单向：L8 分区 route groups（groupPolicy 生效）→ L9 组内 selector/scorer/router（cycle 状态 key = (frame, routeGroupKey)），dispatch 不回改路由。性能：rule 表预编译 tag bitset、actor 谓词按 archetype 缓存（与 DEC-2 同规格）。`actorOrderRouting` 是其 actor 侧子集，迁移后退役。
- **DEC-15** 设备→intent 四层分层：物理绑定（`default_input.json`，已数据化）→ **ControlScheme**（IMC 组合 + 默认 preference 的命名 catalog，`scheme.sc2_classic` 右键指挥 / `scheme.ra_like` 左键 / `scheme.diablo_like` 左键攻击+WASD）→ frame action→op（DEC-13）→ CommandIntentProfile（DEC-14）。genre 差异纯数据；"移动"不是 Core 概念（move 只是 intent rule 的一条 route）。运行时重绑定：现状 `PlayerInputHandler` 无 rebind API、`Remap()`/`SaveUserPreferences` 零调用方——本 Epic 补 rebind API + per-player preference 持久化 + scheme 热切换（IMC push/pop；栈上非 default frame 保留）。WASD 直控 = 轴 intent → sim tick 节流 move order，必经 OrderQueue（禁止直写 WorldPositionCm，收口 Direction backlog）。

## BDD 验收（Gherkin UAT，验收 SSOT）

每个 Scenario 必须有 headless acceptance test 或 playable showcase 支撑；标 `[showcase]` 的必须可运行演示。

### Persona A — Mod 开发者：我定义了配置，应得到什么

```gherkin
Feature: M1 框选即控制域投影（RTS 默认 context）
  Background:
    Given 地图加载建立 owns(P1Rep, [m01..m10]) 与 owns(P2Rep, [m99])
    And P1Rep 与 P2Rep 均 member_of TeamAlphaRep 且互为 Ally
    And 我注册了 filter "filter.controllable.default"（edgeTypes: Owns+Controls）

  Scenario: 屏幕空间 box 与世界空间 polygon 共用同一管线
    When 玩家1 用 BoxScreen cast 圈住 [m01, m02, m99]
    Then (clientSession, collection.ui.cast.raw) 包含 [m01, m02, m99]
    And (P1Rep, collection.command.source) 只包含 [m01, m02]  # m99 不在 P1 控制域
    When 玩家1 改用 PolygonWorld cast 圈住同样三个单位
    Then 写入结果与 BoxScreen 完全一致（仅 InputCastSpec 不同）

  Scenario: shift 增选 / ctrl 减选是 collection 写入模式而非新系统
    When 玩家1 按住 shift 再框 [m03]
    Then (P1Rep, collection.command.source) = [m01, m02, m03]（additive）
    When 玩家1 按住 ctrl 点选 m02
    Then (P1Rep, collection.command.source) = [m01, m03]（subtract）
```

```gherkin
Feature: M2 技能域 context 与恢复 [showcase]
  Scenario: 超级武器进入/退出 confirm targets context
    Given ability "superweapon" 配置 ctx.ability.superweapon.confirm_targets
    And (P1Rep, collection.command.source) = [m01, m02]
    When 玩家1 激活 superweapon 且 exec 打上等待确认 tag（如 exec.awaiting_targets，mod 数据，DEC-13）
    Then InteractionContextStack 压入 frame(ownerToken = exec 实例)
    And 此时框选 [m05, m06] 写入 (P1Rep, collection.ability.superweapon.targets)
    And (P1Rep, collection.command.source) 仍 = [m01, m02]
    When 技能提交（或 cancel / caster 死亡）
    Then frame 按 ownerToken 移除，activeKey 恢复 collection.command.source

  Scenario: context push 同时切换 IMC
    Given ctx 声明 inputContextId "imc.ability.confirm"
    When context 压入
    Then 右键从 "Command(move)" 重映射为 "Cancel"
    When context 弹出
    Then 右键恢复 "Command(move)"
```

```gherkin
Feature: M3 代理控制是纯拓扑投影 [showcase]
  Background:
    Given profile.control.ally_offline_proxy 已注册（通用「谓词 → 边增删」规则；tag 字符串对 Core 不透明）
    And 断言 association/collection 生产代码零 "offline"/"mind_control"/"cinematic" 字面量（ArchitectureTests）

  Scenario: 接管只增 controls 边，双方域各自维护
    When P2Rep 被 mod trigger 打上 participant.offline
    Then profile 求值建立 Controls(P1Rep → P2 域)
    And owns 边不变，m99 上没有任何组件被写入
    And (P2Rep, collection.command.source) 原值保留

  Scenario: 代理期间写入按域路由，队友化身照常走自己的框选基建
    Given Controls(P1Rep → P2 域) 生效
    When 玩家1 框选 [m01(P1 owns), m99(P2 owns)]
    Then (P1Rep, collection.command.source) = [m01]
    And (P2Rep, collection.command.source) = [m99]   # 写入落在所属域，writerDomain=P1Rep
    And 玩家1 的 ControlPlaneView(command) 组合呈现 [m01, m99]

  Scenario: 边消失即"归还"，不存在归还系统
    Given 代理期间玩家1 框选过 [m99]
    When P2 重连（mod trigger 移除 offline tag → profile revoke Controls）
    Then 玩家1 的 ControlPlaneView 收缩为 [m01]，无需驱逐任何行
    And P2 client bind P2Rep 后直接看到 (P2Rep, command.source) = [m99]（代理期间的维护结果原地可见）
    And 全程零 collection 迁移、零 handback 代码路径

  Scenario Outline: 同一套基建覆盖任意接管语义（trigger 无关性）
    Given mod 定义 trigger tag <tag> 与对应 AssociationControlProfile
    When <scenario> 发生又结束
    Then association 层只观察到 Controls 边增删与 revision 变化
    And 集合行为与掉线场景逐字节一致，无任何新增 Core 代码

    Examples:
      | tag                     | scenario                     |
      | participant.offline     | 队友掉线后重连                 |
      | unit.mind_controlled    | 一群单位被心灵控制后解除        |
      | script.cinematic_owned  | 剧本演出临时拿走单位后归还      |
```

```gherkin
Feature: M4 Provenance marker（深绿/浅绿）[showcase]
  Scenario: 本地玩家看混合控制域 marker（relationKind 拓扑现算）
    Given presenter command marker catalog 已加载
    And 玩家1 的 ControlPlaneView 含 m01（住在 P1Rep 域）与 m99（住在 P2Rep 域）
    Then m01 渲染 palette.self.deep（深绿）ring   # viewer==域 → Owns
    And m99 渲染 palette.self.light（浅绿）ring   # viewer→域 走 Controls 边 → 现算为 proxy
    When P2 重连（Controls 边消失）
    Then m99 的 marker 随视图重算在下一 revision diff 消失（无全量重建抖动，无陈旧快照）
```

```gherkin
Feature: M5 裁判多控制域投影 [showcase]
  Scenario: 裁判同时看到两名玩家的框选
    Given RefereeRep 获得对 P1Rep 与 P2Rep collection 的 knowledge grant
    And P1、P2 同属 TeamRed（palette 红系）
    When P1 框选 [m01]，P2 框选 [m99]
    Then 裁判视图中 m01 marker = 红色 phase0，m99 marker = 橘色 phase1
    And 裁判客户端零 gameplay order 提交路径
    And 断言不存在 RefereeSelectionService 类型（ArchitectureTests）
```

```gherkin
Feature: M6 面板聚合 catalog（陆战队/炮车案例）
  Background:
    Given 玩家1 框选 [陆战队1, 陆战队2, 强化陆战队1, 炮车1, 炮车2]
    And catalog：兴奋剂 ×3 同 family.stimpack；蓄力炮（炮车 ×2 模板 A、强化陆战队 ×1 模板 B）同 family.charge_shot
    And 强化陆战队 slot 布局 = [0:普攻, 1:兴奋剂, 2:蓄力炮]（slot 与面板无关）

  Scenario: 案例① byFamily
    Given aggregation.rts.default（groupBy: castFamily）
    Then 面板格1 = 兴奋剂聚合（badge 显示 3 个陆战队 icon）
    And 面板格2 = 蓄力炮聚合（badge 显示强化陆战队 + 2 炮车 icon）

  Scenario: 案例② byTemplate
    Given aggregation.by_template
    Then 格1 = 兴奋剂（3 单位聚合）；格2 = 炮车蓄力炮（2 单位）；格3 = 强化陆战队蓄力炮（1 单位）

  Scenario: 案例③ byAbilityId
    Given aggregation.by_ability_id
    Then 每个独立 abilityId 各占一格，按 profile 的 overflow 规则铺开

  Scenario: FormSet 切换不破坏聚合（杰斯回归）
    Given 强化陆战队1 通过 AbilityFormSet 切换形态使 slot2 映射变化
    Then 面板聚合按新的有效 ability（AbilitySlotResolver 结果）重算
    And 玩家的 aggregation preference 不因形态切换而丢失
```

```gherkin
Feature: M7 施法提交零状态机（frame + tag 承载全部"状态"）
  Scenario Outline: 同一 ability 换 commit profile 只改数据
    Given ability "charge_cannon" 绑定 <commitProfile>
    When 玩家执行 <inputs>
    Then 产生 <orders>，全程无 InteractionModeType 分支、无任何 FSM 结构参与

    Examples:
      | commitProfile              | inputs                  | orders                                   |
      | cast.commit.quick          | press                   | 1 条（spatial=cursorWorld）               |
      | cast.commit.aim_confirm    | press, click(Confirm)   | 1 条（spatial=framePointer）              |
      | cast.commit.charge_release | press, hold, release    | 2 条（phase.begin + phase.commit）        |

  Scenario: 瞄准 = frame 在栈上，取消 = pop，无 "aim"/"cancel" Core 语义
    Given cast.commit.aim_confirm 已 push targeting frame
    Then 「瞄准中」没有任何布尔字段/枚举表达——它就是栈上的 frame 本身
    And indicator 是 presenter 监听 frame collection 成员事件渲染的
    When 玩家触发映射到 popFrame 的 action（mod 数据里叫什么都行）
    Then frame 弹出、indicator 随 collection 清空消失，sim 从未收到任何 order

  Scenario: 蓄力量在 sim 内累计（确定性）
    Given cast.commit.charge_release
    When press 后第 N tick release
    Then 蓄力量 = exec 从 begin order 到 commit order 的 sim tick 差累计的 attribute
    And 回放同一 order 流得到 bit 级相同的蓄力量（client 帧率/延迟无关）

  Scenario: 二段与打断全部落在 GAS 已有仲裁，Input 层零参与
    Given 两段技能：第一条 order 后 exec 打上 exec.awaiting_followup tag 并经 lifecycle push frame
    When 玩家再次 press（frame 将其翻译为 follow-up order）
    Then exec 消费 followup、移除 tag、pop frame
    When 另一场景中 exec 在等待期间被眩晕 effect abort
    Then exec lifecycle pop frame，tag 移除，指示器随 TagEffectiveChanged / collection 事件自动消失
    And 打断/冲突规则全部由 effect / tag requirement 表达，Input 层没有仲裁代码

  Scenario: 新增施法手感不改 Core
    When mod 注册新的 interaction op 并组合出 cast.commit.triple_tap profile
    Then 无需修改 src/Core/Input/**（ArchitectureTests 冻结该目录的 casting 分支逻辑）
```

```gherkin
Feature: M8 复选施法 Dispatch 策略
  Background:
    Given 玩家1 框选 5 个追猎（均有 blink，slot 一致），目标点 T 已解析

  Scenario: 集体 blink
    Given dispatch.all_together
    When 玩家触发 blink
    Then 5 条 Order 提交且共享同一 OrderId（shared fan-out）

  Scenario: 逐个 blink
    Given dispatch.one_by_one
    When 玩家连续触发 blink 三次
    Then 每次只有 cycle 指针指向的 1 个追猎提交 Order，指针依次推进

  Scenario: utility 就近施法
    Given dispatch.utility_nearest（topN=3, distance invert）
    When 玩家触发 blink
    Then 距 T 最近的 3 个追猎提交 Order，其余 2 个不提交
    And 打分走 UtilityAiRuntimeEvaluator（断言无平行 scorer 类型）

  Scenario: 资源优先施法
    Given dispatch.resource_first（mana desc）
    When 玩家触发
    Then 当前蓝量最高的 1 个先提交；orderAccepted 后下一次触发轮到次高者
```

```gherkin
Feature: M9 护栏（ArchitectureTests 即验收）
  Scenario: 零硬编码与零旁路静态断言全绿
    Then 生产代码满足：
      | unit 模板与 spawn 路径零 PlayerOwner/Team 组件 |
      | 除 OrderQueue 外零 SubmitOrder 调用 |
      | src/Core/MassNavigation 零 Input/Selection 引用 |
      | SelectionRuntime 零 command-intake 消费者 |
      | Presenter 规则零 PlayerOwner 读取 |
      | Core 零 "rts"/"moba" 字面量分支 |
      | association/collection 基建零 "offline"/"mind_control"/"cinematic" 等业务场景字面量 |
      | 零 collection 跨域 copy/move API（不存在"归还"代码路径） |
      | DomainStanceQuery 零 "hostile"/"enemy" 字面分支（stance key 全部来自 catalog） |
      | 本 Epic 新增 profile schema 零 Core-only 封闭 enum 分派（interaction op / dispatch kind / groupBy 均 registry 或表达式） |
      | Input 层零施法 FSM：无 states/transitions schema、无 _isAiming 类字段（交互状态只在 InteractionContextStack） |
      | PresentationEventKind 零 aim/cast 专用种类新增；AbilityAimBegun/Updated/Ended/SlotAdvanced 已退役 |
      | Core intent/route 路径零 "attack"/"garrison"/"move" 语义字面量；priority 冲突加载期 fail-fast |
      | intent 谓词求值路径全系统唯一；L8 target 事实零 sim 真值直读（必经 KnowledgeProjection） |
      | 语义路由零裸 bySlotIndex；生产路径零轴输入直写 WorldPositionCm（WASD 必经 OrderQueue） |
      | Presenter 规则零 viewerRole 业务角色枚举（viewer 语义全部拓扑谓词现算） |
      | InteractionModeType 已删除或仅存于迁移 shim 白名单 |
```

```gherkin
Feature: M10 确定性与回放
  # 确定性边界：Order 流是 sim 的唯一入口。
  # 纯视图偏好（聚合/marker/palette）不参与 order 生成；施法偏好（cast commit/dispatch）
  # 参与"raw input → order"的翻译，因此回放 SSOT 是 order 流（或 input+当时偏好快照），
  # 不是裸设备输入。

  Scenario: 纯视图偏好不影响 sim
    Given 两个 client 消费同一份已录制的 order 流
    When 其中一个 client 把 aggregation preference 换成 by_template、marker palette 换成高对比配色
    Then 两端 sim 世界哈希完全一致（视图偏好只影响本地投影）

  Scenario: 施法偏好参与翻译，但翻译产物自包含
    Given 玩家以 cast.commit.quick + dispatch.utility_nearest 录制一局
    When 以录制的 order 流回放
    Then sim 逐 tick 一致，且回放不需要读取任何 preference / context stack / 本地容器实体
    And 每条 Order payload 自包含目标集（或 (owner, collectionKey, revision) 引用）

  Scenario: 偏好变更即时生效但不追溯
    Given 对局中玩家把 cast preference 从 aim_confirm 改为 quick
    Then 已提交的 order 不受影响，仅后续 raw input 的翻译改变
```

```gherkin
Feature: M11 Pointer Command Intent — 动态 candidate 路由 [showcase]
  Background:
    Given intent.command.rts_default 已注册（garrison=30 > weapon=20 > ground move=10）
    And "攻击"/"进驻"都只是 byAbilityTag route 的规则数据——Core 无 attack/garrison 字面量

  Scenario: 点敌方单位 = 路由到普攻 ability（catalog tag 定位，不是裸 slot0）
    Given 我框选了带 ability.catalog.weapon 技能的部队
    When pointer intent 落在 stance=hostile 的单位上
    Then 每个 actor 提交 castAbility order，slot 由 byAbilityTag:ability.catalog.weapon 解析
    And 某个 actor 形态切换后武器换到别的 slot，路由依然正确

  Scenario: 目标同时可驻扎又可破坏 → priority 全序唯一胜出
    Given 中立建筑同时带 structure.garrisonable 与 destructible tag
    When 有驻扎能力的单位收到 pointer intent
    Then 命中 priority=30 的 garrison rule（唯一 winner）
    And 两条规则同 priority 时加载期 fail-fast（禁止运行时隐式平局）

  Scenario: 胜出即终局，route 解析失败不落穿
    Given 某 actor 命中 garrison rule 但 byAbilityTag 解析不到 slot
    Then 该 actor 本次无 order，不落穿到 weapon rule（禁止 fallback）

  Scenario: 混合框选 per-actor 路由 + 显式群体策略
    Given 框选 = [驻扎步兵 ×2, 武器坦克 ×1]，groupPolicy.kind=independent
    When pointer intent 落在"可驻扎+可破坏"建筑上
    Then 步兵 ×2 → 进驻 order，坦克 → 普攻 order（各自跑规则表）
    When 换 groupPolicy = { kind: bySelector, selector: ... }
    Then selector 选出的决策 actor 的胜出 rule 决定全组

  Scenario: intent 分区 → dispatch 两阶段单向组合
    Given 5 个 actor 命中同一 weapon rule，该 route group 绑 dispatch.one_by_one
    Then L8 先分区 route group，L9 组内推进 cycle（状态 key = (frame, routeGroupKey)），dispatch 不回改路由

  Scenario: target 事实经 knowledge 投影求值（fog）
    Given 敌方单位在我的 fog 中不可见
    Then pointer intent 无法将其作为 entity 命中（继承 CanTargetCommand 门控）
    And sim 侧 GAS targeting 终裁一切合法性

  Scenario: 伪装单位按投影事实路由 [deferred：依赖 INT-8 事实投影]
    Given 敌方伪装单位向我投影 stance=friendly 假事实（含伪造归属域）
    Then 路由按投影事实命中非攻击 rule（stance 以 (actor 域, 投影 target 域) 求值）

  Scenario: context 切换换 intent profile，无 commandIntentId 不路由不冒泡
    Given 超级武器 frame 引用 intent.command.superweapon
    Then 栈顶时 pointer intent 按其路由，pop 后恢复 rts_default
    And 未声明 commandIntentId 的 frame：未拦截的 pointer command 不路由、不冒泡
```

```gherkin
Feature: M12 ControlScheme — genre 键位差异纯数据
  Scenario Outline: 同一 Core，不同 genre 方案
    Given mod 声明 <scheme>
    When 玩家执行 <input>
    Then 产生 <result>，Core 零 genre 分支

    Examples:
      | scheme             | input      | result                          |
      | scheme.sc2_classic | 右键点地面 | moveTo order                    |
      | scheme.ra_like     | 左键点地面 | moveTo order（Command 绑左键）   |
      | scheme.diablo_like | 左键点敌人 | 普攻 castAbility order           |
      | scheme.diablo_like | WASD       | sim tick 节流的 move order 流    |

  Scenario: WASD 直控走 OrderQueue（铁律 1）
    When 按住 W 2 秒
    Then 全部是 OrderQueue 内的 move order，零直写 WorldPositionCm；回放轨迹 bit 级一致
```

### Persona B — 玩家：我改了偏好，应得到什么

```gherkin
Feature: P1 全局快捷施法开关
  Scenario: 传统 → 快捷
    Given 全局偏好 castCommit = cast.commit.aim_confirm（传统）
    When 我在设置里改为 cast.commit.quick
    Then 所有未被更深 scope 覆盖的技能按键 press 即施法
    And 偏好持久化，重开客户端仍生效
```

```gherkin
Feature: P2 偏好 scope 覆盖链（LOL 式）
  Scenario: 英雄级覆盖全局
    Given 全局 = quick，perTemplate[xerath] = aim_confirm
    When 我操控 xerath 按 Q
    Then 走 aim_confirm；操控其他英雄按 Q 走 quick

  Scenario: 槽位级覆盖英雄级
    Given perTemplate[xerath] = aim_confirm，perSlot[xerath/2] = quick
    Then xerath 的 slot2 快捷施法，其余 slot 传统施法

  Scenario: mod 锁定不可覆盖
    Given mod 对 "superweapon" slot 声明 lockedCastCommitId = cast.commit.aim_confirm
    When 我尝试设置该 slot 为 quick
    Then 设置界面显示锁定，实际行为保持 aim_confirm
```

```gherkin
Feature: P3 面板聚合偏好切换
  Given 我框选了陆战队 ×2 + 强化陆战队 ×1 + 炮车 ×2
  Scenario: 运行时切换聚合模式
    When 我把面板偏好从 byFamily 切到 byTemplate
    Then 面板立即从案例①布局变为案例②布局，无需重新框选
    And 每格 badge/来源 icon 正确反映聚合成员
```

```gherkin
Feature: P4 复选施法偏好
  Scenario: 我偏好逐个施法
    Given 该技能 mod 默认 dispatch.all_together 且允许玩家覆盖
    When 我把偏好改为 one_by_one
    Then 框选 5 追猎连按 blink 是逐个闪烁而非齐闪
```

```gherkin
Feature: P5 我看到的 marker
  Scenario: 我方深绿、代理浅绿
    Given 队友掉线且其单位进入我的控制平面
    When 我框选混合部队
    Then 我的单位深绿 ring、队友单位浅绿 ring
    When 队友重连
    Then 浅绿 ring 单位从我的选中集消失，我无需任何手动操作
```

```gherkin
Feature: P6 context 下的选择习惯保留
  Scenario: 技能框选不吃掉我的部队选择
    Given 我框选了主力部队
    When 我进入超级武器指定单位模式并画选目标
    And 施法完成
    Then 我的主力部队选择原样还在，可直接右键行军

  Scenario: Tab 在并发待确认 exec 间循环
    Given 两个单位的技能 exec 各自带等待确认 tag（两个 context frame 并存）
    When 我按 Tab
    Then activeCollectionKey 在两个技能目标 key 间循环切换，HUD 高亮当前 exec
```

```gherkin
Feature: P7 控制组与 context 正交
  Scenario: 控制组录制/召回读写 command.source
    Given 我把当前框选存为控制组 1
    When 我进入任意技能 context 又退出后按 1
    Then 控制组恢复的是 (P1Rep, collection.command.source) 的成员，行为与进入 context 前一致
```

```gherkin
Feature: P8 观战者视角（玩家即裁判）
  Scenario: 我作为中立裁判加入
    Given 我以裁判身份加入并获得 knowledge grant
    Then 我能同时看到双方玩家的实时框选 marker（队伍色 + 相位差）
    And 我的任何点击不产生 gameplay order
```

```gherkin
Feature: P9 控制方案与改键偏好
  Scenario: 局内热切换 WASD ⇄ 鼠标移动（LOL 式）
    Given mod 允许 [scheme.mouse_move, scheme.wasd_move]
    When 我从鼠标移动切到 WASD
    Then 立即生效：WASD 产生 move order，pointer 语义按新 scheme 路由；切回完全恢复

  Scenario: 物理改键持久化
    When 我把 Command 从右键改绑到侧键
    Then 本局立即生效，重开客户端仍生效（per-player preference 持久化）
    And 改键只动 binding 层，intent/route 配置零变化

  Scenario: mod 锁定方案集
    Given mod 只允许 scheme.sc2_classic
    When 我尝试切到 scheme.diablo_like
    Then 设置界面显示不可用，行为保持 sc2_classic
```

## Sub-issue 分解（Workstream）

原 #536/#537/#538 的未完成子单**重新挂到本 Epic**，编号沿用；下表为新增与修订项。

**Phase 0 — 前置基建（阻塞后续全部）**
- [ ] PRE-1 Relationship 反向邻接索引 / incoming cache（DEC-2）
- [ ] PRE-2 DomainStanceQuery + stance catalog 缓存（DEC-3，CTRL-3 硬前置）
- [ ] PRE-3 PR #535 vs PR #577 仲裁，确定单一 canonical，合并 ORD 遗留清单

**Phase 1 — Order/Collection 边界（继承 ORD-1..8）**
- [ ] ORD-4b `OrderSelectionReference` 自包含化：删除 selection 容器实体引用与 per-order lease 实体分配

**Phase 2 — Context Stack（继承 CTX-1..10）**
- [ ] CTX-1b frame 增加 ownerToken + inputContextId；按 token 移除；lifecycle 回收钩子
- [ ] CTX-7b CastCommitProfile + interaction op registry + loader（DEC-13：无 FSM schema）；InteractionModeType 退役映射表
- [ ] CTX-7c 退役 AbilityAimBegun/Updated/Ended/SlotAdvanced 事件种类与 AbilityAimPresentationRuntime / AbilityAimSessionState；indicator 迁 presenter 通用事件（tag / collection / attribute）
- [ ] CTX-8b ClientCastPreference scope 链 schema + mod lock 语义

**Phase 3 — Control Plane（继承 CTRL-1..10）**
- [ ] CTRL-1b `controls` 为查询期视图（owns ∪ grant）
- [ ] CTRL-3b 依赖 PRE-2；列全消费者迁移清单（GAS targeting / TeamColorResolver / PerformPhaseResolver / lifecycle snapshot / #499 publisher / `SelectionEligibility.CanAcquire` 的 Team+RelationshipFilter 直读 / CoreInputMod NearestEnemyInRange resolver）
- [ ] CTRL-4b AssociationControlProfile = 通用「谓词 → 边增删」规则引擎（复用 condition DSL，schema 零业务词汇，无 handback/policy 字段）
- [ ] CTRL-4c CollectionWrite 域路由：写入按被指挥单位所属域落到对应 rep，row 记 writerDomain
- [ ] CTRL-4d ControlPlaneView：EntityView domainScope 扩展，controls 可达域组合只读视图；Order fan-out / HUD / PanelRouter 改消费该视图

**Phase 4 — Provenance & Presenter（继承 PROV-1..8）**
- [ ] PROV-1b provenance 简化：controlDomain 由 collection 地址承载，写时仅存 writerDomain；relationKind 不入行、由 view 层拓扑现算
- [ ] PROV-2b relationship revision → ControlPlaneView / Presenter 重算钩子
- [ ] PROV-4b Presenter graph condition 上下文扩展：viewer 实体寄存器 + event payload 寄存器 + relationship/knowledge 拓扑谓词 graph ops（DEC-12）
- [ ] PROV-4c 接线 `PresenterDefinition.VisibilityCondition` graphProgramId 的 Emit 路径（现状 throw）
- [ ] PROV-6b 退役 `TeamColorResolver` 硬编码色与 `PerformAudienceContext` 的 Team/PlayerOwner 直读，改 palette catalog + 拓扑求值

**Phase 5 — Panel Router & 聚合（新增）**
- [ ] PNL-1 ability catalog 字段（castFamily/alias）schema + loader
- [ ] PNL-2 AggregationProfile registry + groupBy key selector 表达式求值（非封闭 enum）
- [ ] PNL-3 PanelRouter：intent → 聚合格 → per-entity (entity, slotIndex)；删除 UI 反打 input action 路径
- [ ] PNL-4 CollectionGasEntityCommandPanelSource 迁移为消费 profile；FormSet 切换重算
- [ ] PNL-5 玩家聚合偏好 + 持久化

**Phase 6 — Cast Dispatch（新增）**
- [ ] DSP-1 CastDispatchProfile schema + registry；selector/scorer/router kind 为 registry 注册项（DEC-11）
- [ ] DSP-2 scorer 桥接 UtilityAiRuntimeEvaluator
- [ ] DSP-3 挂点：施法键 = command collection → fan-out；pointer = DEC-14 route group → fan-out（两阶段单向）；shared order id 语义保持
- [ ] DSP-4 cycle/sequential 状态（advanceOn orderAccepted）
- [ ] DSP-5 玩家 dispatch 偏好（mod 可锁）

**Phase 6.5 — Command Intent & ControlScheme（新增）**
- [ ] INT-1 CommandIntentProfile schema + registry + loader：priority 全序加载期 fail-fast，谓词 shorthand lower 到统一 condition DSL（DEC-14）
- [ ] INT-2 target 谓词求值：tag + DomainStanceQuery + 结构谓词，必经 viewer KnowledgeProjection（复用 CanTargetCommand 语义）；stance 按 actor 所属域；rule 表预编译 tag bitset；退役 AutoTargetPolicy.NearestEnemyInRange
- [ ] INT-3 per-actor 路由 + route group 分区 + groupPolicy registry（independent / bySelector）；胜出即终局；迁移并退役 actorOrderRouting
- [ ] INT-4 route 评分委托：接线 ContextScoredOrderResolver 到 pointer command 路径，其 spatial 候选补 knowledge 过滤（现状裸 QueryRadius）
- [ ] INT-5 ControlScheme catalog + 热切换（IMC push/pop）+ per-player 物理键 rebind API 与 preference 持久化接线（现状 Remap()/SaveUserPreferences 零调用方）
- [ ] INT-6 WASD 轴 intent → sim tick 节流 move order（走 OrderQueue，收口 Direction backlog）
- [ ] INT-7 context frame 引用 commandIntentId + frameActions 仲裁规则（精确匹配拦截 → frame intent → 不冒泡）
- [ ] INT-8 KnowledgeProjection 事实投影扩展：per-viewer tag/stance mask（伪装/假情报基建；M11 伪装场景依赖本单）

**Phase 7 — Showcase & 护栏 & 文档**
- [ ] SHOW-1 [showcase] M2 超级武器 context（含 IMC 切换）
- [ ] SHOW-2 [showcase] M3+M4+P5 代理控制拓扑投影 + marker + 边消失涌现"归还"（掉线与心控两种 trigger 各演示一遍，证明 trigger 无关性）
- [ ] SHOW-3 [showcase] M5+P8 裁判多控制域投影
- [ ] SHOW-4 [showcase] M6+P3 面板聚合三案例切换
- [ ] SHOW-5 [showcase] M8+P4 追猎 blink 三种 dispatch
- [ ] SHOW-6 [showcase] M11+M12+P9 pointer intent 路由（驻扎/破坏歧义胜出、混合框选 per-actor）+ ControlScheme 热切换（右键⇄左键⇄WASD）
- [ ] GUARD-1 M9 全部 ArchitectureTests
- [ ] GUARD-2 M10 确定性回放 acceptance（headless 双端 hash）
- [ ] DOC-1 gitbook 回写 + 退役文档 deprecation

## 依赖与迁移顺序

```text
#239 AAC / OwnershipResolver（已有）
  → PRE-1/2/3
    → Phase 1 (ORD)  ──┐
    → Phase 2 (CTX)  ──┼→ Phase 4 (PROV) → SHOW-2/3
    → Phase 3 (CTRL) ──┘
    → Phase 5 (PNL) →─┐
    → Phase 6 (DSP) →──┼→ SHOW-4/5/6
    → Phase 6.5 (INT) ─┘   （INT-2 依赖 PRE-2 stance；INT-7 依赖 CTX-1b）
```

- FilterProfile 契约在 CTX，association provider 由 CTRL 注入（拆 0062↔0063 循环依赖）。
- CTRL-3（删组件）必须晚于 PRE-2 与全部消费者迁移。

## 非目标

- 不重写 MassNavigationFlow solver、RelationshipRuntime 存储模型（只加索引）。
- 不做 SelectionRuntime / PlayerOwner 兼容层或 fallback 读取。
- 不实现 ParticipantView Mode enum，Core 不出现 genre 分支。
- 不在本 Epic 实现联机传输层，只保证确定性契约（M10）。
- 不为裁判实现 gameplay order 路径。
- 不在本 Epic 开放 Presenter 执行侧封闭 enum 的 mod 注册（`PresentationEventKind` / `PresenterCommandKind` / `BehaviorKind` / `AssetKind` / `InlineConditionKind`）：已核查不阻塞本 Epic UAT（DEC-12），列为已知边界，若后续需要另立 RFC。
- 不做同进程多 viewport 渲染（裁判作为独立 client anchor 已满足 M5/P8）。
