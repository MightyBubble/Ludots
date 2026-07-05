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
[L2 Remap]  IMC 输入映射上下文（已有）→ InputAction
[L3 Ctx]    InteractionContextStack：frame 决定 activeCollectionKey/activeViewKey/inputContextId
[L4 Cast]   InputCastSpec（box/polygon/ray/lasso × screen/world/minimap）→ raw hits collection
[L5 Filter] FilterProfile（association query DSL）→ filtered
[L6 Coll]   CollectionWrite → (playerRepEntity, activeKey) + provenance rows
[L7 Panel]  EntityView + PanelRouter + AggregationProfile → HUD 投影
[L8 Flow]   CastFlowProfile（多段施法状态机）+ ClientCastPreference（scope 链）
[L9 Disp]   CastDispatchProfile（selector/scorer/router，scorer 复用 UtilityAI）
[L10 Order] OrderQueue（唯一 intake）→ OrderBuffer → AbilityExec / MassNav
[L∥ Perf]   Performer 只读 collection revision + provenance + catalog → marker/相位
```

## 铁律

1. OrderQueue 唯一 intake；MassNav 只消费 OrderBuffer。
2. 「Selection」只是 default context 下 `collection.command.source` 的俗名；`SelectionRuntime` 退役为非 hub；Order payload 自包含目标集或引用 `(owner, collectionKey, revision)`。
3. Embodied entity 零 `PlayerOwner`/`Team`/`PlayerIdentity`；归属只存在于 `owns`/`controls`/`member_of`/`ally` 边。
4. 控制平面只走 `ControlDomainQuery`；敌我判定只走 `HostilityQuery`（关系图 SSOT + 热路径缓存）。
5. 代理控制只增删 `controls` 边；collection namespace per playerRep，禁止 cross-player merge 与 PlayerId 全局表。
6. Context frame 带 ownerToken，按 token 移除；push/pop 联动 IMC（inputContextId）。
7. InputCast / Filter / Commit / Presentation 四轴正交；`InteractionModeType` 全部退役为 CastFlowProfile 数据。
8. Performer 只读；裁判走 KnowledgeProjection grant，禁止 RefereeSelectionService。
9. 面板是投影不是控制器：PanelRouter 单向消费，UI 不得反打 input action。
10. 聚合/排序/路由规则全部 catalog/profile；确定性：本地偏好与 context stack 不隐式进入 sim。

## 关键设计决策（DEC，详见 RFC §4）

- **DEC-1** `controls` = 查询期视图（owns ∪ 显式 grant），只物化代理 grant 边。
- **DEC-2** Relationship 反向邻接索引先行（现状 `CollectIncoming` 全表扫描不可用于 provenance）。
- **DEC-3** `HostilityQuery` 是删 unit `Team` 的硬前置（GAS targeting 热路径）。
- **DEC-4** 掉线归还策略数据化：`handbackPolicy: freeze | mirror | discard`。
- **DEC-5** Provenance 写时填充 + relationship revision 失效钩子（防陈旧 marker）。
- **DEC-6** 并发 PendingConfirm exec 各持 frame，Tab 在 frame 间循环。
- **DEC-7** InteractionContextStack 与 IMC 同事务联动。
- **DEC-8** FilterProfile 契约归 Context/Input 域，association provider 由 Control Plane 注入（拆循环依赖）。
- **DEC-9** Dispatch scorer 复用 `UtilityAiRuntimeEvaluator`（RFC-0060），禁止平行 scorer。
- **DEC-10** 面板聚合 = ability catalog 字段（castFamily/alias）+ AggregationProfile + 玩家偏好覆盖。

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
    When 玩家1 激活 superweapon 且 exec 进入 PendingConfirm
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
Feature: M3 掉线代理与归还策略 [showcase]
  Background:
    Given profile.control.offline_teammate_proxy 已注册（ally + offline → grant Controls，handbackPolicy 可配）

  Scenario: 掉线只增 controls 边
    When P2 掉线（participant.offline tag 打到 P2Rep）
    Then runtime 建立 Controls(P1Rep → m99)
    And owns 边不变，m99 上没有任何组件被写入
    And (P2Rep, collection.command.source) 原值保留

  Scenario: 混合框选跨控制域
    When 玩家1 框选 [m01, m99]
    Then (P1Rep, collection.command.source) rows =
      | entity | controlDomain | relationKind |
      | m01    | P1Rep         | Owns         |
      | m99    | P2Rep         | Controls     |

  Scenario Outline: 重连按 handbackPolicy 归还
    Given profile 的 handbackPolicy = <policy>
    And 掉线期间玩家1 框选过 [m99]
    When P2 重连（offline tag 移除）
    Then Controls(P1Rep → m99) 被 revoke
    And (P2Rep, collection.command.source) = <result>
    And (P1Rep, collection.command.source) 中 m99 的行被驱逐（provenance 失效钩子）

    Examples:
      | policy  | result                     |
      | freeze  | 掉线前原值                  |
      | mirror  | [m99]（代理期间的写入镜像）  |
      | discard | 空                         |
```

```gherkin
Feature: M4 Provenance marker（深绿/浅绿）[showcase]
  Scenario: 本地玩家看混合控制域 marker
    Given performer selection marker catalog 已加载
    And (P1Rep, collection.command.source) 含 owns 行 m01 与 controls 行 m99
    Then m01 渲染 palette.self.deep（深绿）ring
    And m99 渲染 palette.self.light（浅绿）ring
    When P2 重连导致 m99 行被驱逐
    Then m99 的 marker 在下一 revision diff 消失（无全量重建抖动）
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
Feature: M7 CastFlow 数据化（多段施法零 Core switch）
  Scenario Outline: 同一 ability 换 castflow 只改数据
    Given ability "charge_cannon" 绑定 <castflow>
    When 玩家执行 <inputs>
    Then 产生 <payload> 的 Order 且全程无 InteractionModeType 分支参与

    Examples:
      | castflow                | inputs                    | payload                      |
      | castflow.quick          | press                     | spatial=cursorWorld          |
      | castflow.charge_release | press, hold 1.2s, release | f0=1.2, spatial=cursorWorld  |
      | castflow.aim_confirm    | press, click(confirm)     | spatial=confirmPoint         |
      | castflow.two_stage      | press, press              | 阶段2 payload（两段各自提交）  |

  Scenario: 新增施法手感不改 Core
    When mod 注册新的 castflow.triple_tap profile
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
      | Performer 规则零 PlayerOwner 读取 |
      | Core 零 "rts"/"moba" 字面量分支 |
      | InteractionModeType 已删除或仅存于迁移 shim 白名单 |
```

```gherkin
Feature: M10 确定性与回放
  Scenario: 本地投影不进 sim
    Given 两个 client 以相同 authoritative input 流回放同一场景
    When 其中一个 client 把 aggregation preference 换成 by_template、cast preference 换成 quick
    Then 两端 sim 世界哈希完全一致
    And 每条 Order payload 自包含目标集（或 (owner,key,revision) 引用），不引用 client 本地容器实体
```

### Persona B — 玩家：我改了偏好，应得到什么

```gherkin
Feature: P1 全局快捷施法开关
  Scenario: 传统 → 快捷
    Given 全局偏好 castFlow = castflow.aim_confirm（传统）
    When 我在设置里改为 castflow.quick
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
    Given mod 对 "superweapon" slot 声明 lockedCastFlowId = aim_confirm
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
    Given 两个单位各有一个 PendingConfirm 的技能 exec（两个 context frame 并存）
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

## Sub-issue 分解（Workstream）

原 #536/#537/#538 的未完成子单**重新挂到本 Epic**，编号沿用；下表为新增与修订项。

**Phase 0 — 前置基建（阻塞后续全部）**
- [ ] PRE-1 Relationship 反向邻接索引 / incoming cache（DEC-2）
- [ ] PRE-2 HostilityQuery + faction stance 缓存（DEC-3，CTRL-3 硬前置）
- [ ] PRE-3 PR #535 vs PR #577 仲裁，确定单一 canonical，合并 ORD 遗留清单

**Phase 1 — Order/Collection 边界（继承 ORD-1..8）**
- [ ] ORD-4b `OrderSelectionReference` 自包含化：删除 selection 容器实体引用与 per-order lease 实体分配

**Phase 2 — Context Stack（继承 CTX-1..10）**
- [ ] CTX-1b frame 增加 ownerToken + inputContextId；按 token 移除；lifecycle 回收钩子
- [ ] CTX-7b CastFlowProfile registry + loader；InteractionModeType 退役映射表
- [ ] CTX-8b ClientCastPreference scope 链 schema + mod lock 语义

**Phase 3 — Control Plane（继承 CTRL-1..10）**
- [ ] CTRL-1b `controls` 为查询期视图（owns ∪ grant）
- [ ] CTRL-3b 依赖 PRE-2；列全消费者迁移清单（GAS targeting / TeamColorResolver / PerformPhaseResolver / lifecycle snapshot / #499 publisher）
- [ ] CTRL-4b handbackPolicy 字段（freeze/mirror/discard）+ mirror 实现

**Phase 4 — Provenance & Performer（继承 PROV-1..8）**
- [ ] PROV-2b relationship revision → collection 失效/驱逐钩子

**Phase 5 — Panel Router & 聚合（新增）**
- [ ] PNL-1 ability catalog 字段（castFamily/alias）schema + loader
- [ ] PNL-2 AggregationProfile registry + byFamily/byTemplate/byAbilityId 求值
- [ ] PNL-3 PanelRouter：intent → 聚合格 → per-entity (entity, slotIndex)；删除 UI 反打 input action 路径
- [ ] PNL-4 CollectionGasEntityCommandPanelSource 迁移为消费 profile；FormSet 切换重算
- [ ] PNL-5 玩家聚合偏好 + 持久化

**Phase 6 — Cast Dispatch（新增）**
- [ ] DSP-1 CastDispatchProfile schema + registry（selector/scorer/router）
- [ ] DSP-2 scorer 桥接 UtilityAiRuntimeEvaluator
- [ ] DSP-3 挂点：command collection → fan-out 之间；shared order id 语义保持
- [ ] DSP-4 cycle/sequential 状态（advanceOn orderAccepted）
- [ ] DSP-5 玩家 dispatch 偏好（mod 可锁）

**Phase 7 — Showcase & 护栏 & 文档**
- [ ] SHOW-1 [showcase] M2 超级武器 context（含 IMC 切换）
- [ ] SHOW-2 [showcase] M3+M4+P5 掉线代理 + marker + handback（freeze 与 mirror 各一遍）
- [ ] SHOW-3 [showcase] M5+P8 裁判多控制域投影
- [ ] SHOW-4 [showcase] M6+P3 面板聚合三案例切换
- [ ] SHOW-5 [showcase] M8+P4 追猎 blink 三种 dispatch
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
    → Phase 6 (DSP) →─┴→ SHOW-4/5
```

- FilterProfile 契约在 CTX，association provider 由 CTRL 注入（拆 0062↔0063 循环依赖）。
- CTRL-3（删组件）必须晚于 PRE-2 与全部消费者迁移。

## 非目标

- 不重写 MassNavigationFlow solver、RelationshipRuntime 存储模型（只加索引）。
- 不做 SelectionRuntime / PlayerOwner 兼容层或 fallback 读取。
- 不实现 ParticipantView Mode enum，Core 不出现 genre 分支。
- 不在本 Epic 实现联机传输层，只保证确定性契约（M10）。
- 不为裁判实现 gameplay order 路径。
