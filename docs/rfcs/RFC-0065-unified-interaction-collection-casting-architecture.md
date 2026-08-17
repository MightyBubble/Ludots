> Historical RFC. Its terminal direction is now current architecture: "selection" is only
> user-facing shorthand, formal SelectionRuntime is retired, and EntityCollectionStore /
> `collection.command.source` is authoritative. Any remaining text describing dual-track
> SelectionRuntime transition is historical context, not permission to add fallback.
# RFC-0065 统一交互—集合—施法架构（Unified Interaction · Entity Collection · Casting）

Status: Proposed（新 Epic SSOT，整合并取代 RFC-0061/0062/0063/0064 的分散叙述）
Supersedes-as-planning-SSOT: RFC-0061、RFC-0062、RFC-0063、RFC-0064（技术结论保留，规划与验收以本文为准）
Parent Epics being consolidated: #522 / #536 / #537 / #538

---

## 0. 一句话

把「框选 / 画线 / 点选 / 快捷键」到「技能真正执行」之间的全部环节，拆成一条**零硬编码、全数据驱动、领域解耦**的单向管线；所有归属语义走 relationship，所有集合语义走 EntityCollection，所有 UX 差异（RTS / MOBA / ARPG）只体现在 catalog / profile / preference 数据里，Core 不出现任何 genre 分支。

---

## 1. 问题与愿景

### 1.1 现状问题（合并自 RFC-0061~0064 + 新增缺口）

1. 「Selection」是单一全局语义 hub（`SelectionRuntime`），无法表达 context 化集合（默认框选 vs 超级武器指定单位）。
2. 「归属」写成 unit 身上的 `PlayerOwner` / `Team` 组件，掉线接管、控制权转移、裁判观战都要造平行系统。
3. Collection 与 PlayerId 数据 bag 混谈，重连无法归还框选状态。
4. `InteractionModeType` 把 geometry / commit / presentation / targeting 捆成一个 enum，SmartCast 陷阱无法正交扩展。
5. 技能面板顺序 == slot index == input action（`argsTemplate.i0`），面板反向调用输入层；多选聚合规则写死在代码里。
6. 复选施法 fan-out 无策略层：只有「全员各来一份」，无法表达逐个施法 / 就近 N 个 / 血多先放。
7. 多段施法（蓄力、两段确认、模式切换大招）行为散落在 `InputOrderMappingSystem` 的巨型 switch 里。
8. 施法偏好只有全局 + per-ability override，没有 LOL 式 scope 链（全局 → 英雄 → 槽位）。
9. Presenter 无法表达 provenance（我的单位 vs 代理的队友单位 vs 裁判视角的多玩家集合）。

### 1.2 愿景管线（单向，共 8 段）

```text
[L1 Device]      原始设备输入（键鼠/手柄/触屏）
[L2 Remap]       ControlScheme（IMC 组合，玩家可热切换/改键）→ InputAction（DEC-15）
[L3 Intent+Ctx]  InteractionContextStack：栈顶 frame 决定 activeCollectionKey/activeViewKey/
                 inputContextId/commandIntentId
[L4 Cast]        InputCastSpec（box/polygon/ray/lasso × screen/world/minimap）→ raw hits collection
[L5 Filter]      FilterProfile（graph/condition DSL，association query）→ filtered
[L6 Collection]  CollectionWrite → 按所属域路由到 (domainRepEntity, activeKey)，row 记 writerDomain
[L7 View/Panel]  EntityView profile + PanelRouter + AggregationProfile → HUD/面板投影
[L8 Commit]      施法键：CastCommitProfile（激活 ops：pushFrame/popFrame/submitOrder）+ ClientCastPreference
                 pointer 命令：CommandIntentProfile（actor 谓词 × target 谓词 → route，显式全序，DEC-14）
                 —— 无状态机：client 侧状态 = 栈上 frame，sim 侧状态 = exec 实体 tag（DEC-13）
[L9 Dispatch]    CastDispatchProfile（selector/scorer/router）→ per-actor Order（shared order id）
[L10 Order]      OrderQueue（唯一 intake）→ OrderBuffer → AbilityExec / MassNav ingestion
[L∥ Presenter]   只读 collection revision + provenance + catalog → marker/相位（本地/队友/裁判）
```

每层只能读上一层的产物；任何层不得反向调用（现状反例：面板 `ActivateSlot` 反打 `TryActivateMappedAction`）。

---

## 2. 领域模型与术语

| 术语 | 定义 | 载体（现有 / 拟新增） |
|------|------|------|
| Player rep entity | client 在 sim 里的化身锚点（RTS/MOBA 可为虚拟，ARPG 可为具象） | `PlayerIdentity`（已有） |
| Team rep entity | 阵营锚点 | `TeamIdentity`（已有） |
| Embodied entity | 被指挥的单位；**不携带**任何 owner/team 组件 | GAS/空间/标签组件（已有） |
| Relationship edge | `owns` / `controls` / `member_of` / `ally`，catalog 注册 | `RelationshipRuntime` + catalog.json（已有，需扩 catalog） |
| ControlDomainQuery | 「controllerRep 能指挥谁 / target 属于谁的控制域」 | 拟新增（基于 `OwnershipResolver` 方向） |
| DomainStanceQuery | 两个控制域之间 stance 的热路径缓存投影（关系图为 SSOT）；stance key（hostile/friendly/neutral…）是 **catalog 数据**（mods 已有 `CombatStance.Hostile` 先例），Core 不识别任何 stance 字面语义 | 拟新增（替代 unit `Team` 比较） |
| EntityCollection | `(owner entity, key string)` 寻址的实体集合；row 永远住在所属控制域的 rep 上 | `EntityCollectionStore`（已有，rows 需扩 writerDomain） |
| Provenance | 域归属由 collection 地址承载；viewer 相对语义（owns/controls/spectate）由拓扑现算；写时仅记 `writerDomain` | 拟新增（DEC-4/5） |
| ControlPlaneView | 「我的当前选中/控制集」= 对 controls 可达域的组合只读视图（EntityView domainScope 扩展） | 拟新增 |
| InteractionContextStack | local client 的 context 栈；frame = (contextId, activeCollectionKey, activeViewKey, contextEntity, filterProfile, inputContextId, ownerToken) | 拟新增 |
| InputCastSpec | 几何×空间的输入采集描述，与 commit 语义正交 | 拟新增 |
| FilterProfile | 数据驱动过滤（association / tag / 存活 / 类型），复用现有 condition/graph DSL | 拟新增 registry |
| EntityView profile | `viewKey → (collectionKey, role)` 只读绑定 | 拟新增 registry（RFC-0061 已定义） |
| Ability Slot | 单位技能槽（`AbilityStateBuffer` 8 槽 + Granted/Item/Form 覆盖） | 已有 |
| Ability FormSet | slot idx → ability template 的预设/运行时映射组（杰斯锤炮切换） | `AbilityFormSetRegistry`（已有） |
| Cast Family（alias/catalog） | 「施法方式/语义同类」的跨模板聚合键（炮车蓄力炮 ≈ 强化陆战队蓄力炮） | 拟新增 ability catalog 字段 |
| AggregationProfile | 多选时面板聚合规则（byFamily / byTemplate / byAbilityId / flat） | 拟新增 |
| PanelRouter | input intent（Q/W/E…）→ 当前聚合视图第 N 格 → 逐 entity `(entity, slotIndex)` 绑定集 | 拟新增 |
| CastCommitProfile | 施法提交绑定：激活时执行的 op 序列（立即提交 / push targeting frame）+ frame 内 action→op 映射；**无 states/transitions**，取代 `InteractionModeType` 捆绑 | 拟新增 |
| CommandIntentProfile | pointer intent 的 per-actor 路由规则表：actor 谓词 × target 谓词 → route（orderType + slot selector / contextGroup 评分委托）；显式全序胜出 + 显式群体策略 | 拟新增（吸收并退役 `actorOrderRouting`，复用 `ContextScoredOrderResolver` 评分） |
| ControlScheme | 命名的控制方案 = IMC context 组合 + 默认 preference（sc2 右键指挥 / 红警左键 / 暗黑 WASD），玩家可热切换 | 拟新增 catalog |
| ClientCastPreference | 玩家施法偏好，scope 链 global → template → formset → slot | 拟新增 |
| CastDispatchProfile | 复选施法的 selector（谁施法）/ scorer（排序，复用 UtilityAI）/ router（并发/顺序） | 拟新增 |
| Order | 唯一执行入口 payload；`OrderQueue` 是唯一 intake | 已有 |
| Presenter marker rules | `PresenterRule`（event + condition + command）+ palette catalog 表达 marker 样式；viewer 语义由 graph 拓扑谓词现算 | `PresenterDefinitionRegistry` + `presenters.json`（已有；condition 上下文需扩，见 DEC-12） |
| KnowledgeProjection | 裁判/观战 visibility grant | `KnowledgeProjectionStore`（已有） |

---

## 3. 铁律（合并 + 新增）

1. **OrderQueue 唯一 intake**：MassNav / AI / input / evidence 不得旁路 `SubmitOrder`。
2. **MassNav 只消费 OrderBuffer**：零 Input / Selection 读取。
3. **Selection 概念退役（终态约束）**：「selection」只是 default context 下 `collection.command.source` 的俗名；`SelectionRuntime` 不得作为 hub；Order payload 不得引用 selection 容器实体，须自包含目标集或引用 `(owner, collectionKey, revision)`。PR581 closeout 已退役 formal Selection APIs；任何双轨过渡描述都仅是历史审计上下文，不允许作为 fallback 依据。
4. **Embodied entity 零 `PlayerOwner` / `Team` / `PlayerIdentity`**：归属只存在于 relationship 边。
5. **控制平面只走 `ControlDomainQuery`**；阵营/敌我判定只走 `DomainStanceQuery`（缓存投影，relationship revision 失效；stance key 是 catalog 数据，Core 无 "hostile" 字面语义）。
6. **代理控制只增删 `controls` 边**：不迁移 collection、不改 `owns`、不写 unit 组件。
7. **Collection namespace per playerRep entity**：禁止 cross-player merge，禁止 PlayerId 全局表。
8. **Context Stack 只路由 key，不存实体列表**；frame 按 ownerToken 移除，不依赖裸 LIFO。
9. **InputCast 与 Filter、Commit、Presentation 正交**：禁止再往 `InteractionModeType` 加值；新施法手感 = 新 CastCommitProfile 数据。
9a. **零施法状态机**：Input 层不得持有任何施法 FSM / `_isAiming` 类字段 / states-transitions schema；client 侧唯一交互状态 = InteractionContextStack 上的 frame，sim 侧唯一施法进度 = exec 实体上的 tag + attribute（DEC-13）。
9b. **Presenter 的 casting 表现只消费通用事件**：order 生命周期、ability exec / effect 生命周期、attribute / tag 变化、collection 成员与 revision、entity 生命周期——零 aim/cast 专用事件种类；`AbilityAimBegun/Updated/Ended/SlotAdvanced` 事件退役（DEC-13）。
10. **Presenter 只读**：collection revision + provenance + catalog；不写 collection、不改 association。
11. **裁判/观战走 KnowledgeProjection grant**：禁止 RefereeSelectionService、禁止复制 collection。
12. **面板是投影不是控制器**：PanelRouter 单向消费 EntityView/聚合结果；UI 不得反向触发 input action。
13. **聚合/排序/路由规则全部 catalog/profile 数据**：Core 无 `if (rts)`、无写死的 Q/W/E 分派。
14. **确定性**：context stack 与 preference 均为 local 投影；进入 sim 的只有自包含 Order。回放/联机重建只依赖 authoritative input + order 流。
15. **Association/Collection 层零业务语义**：掉线、心灵控制、演出接管等只是 trigger 数据（tag / 边增删）；基建 schema 与代码不识别任何场景词汇。
16. **Collection 永不跨域迁移**：写入按所属域路由；跨域指挥一律 = controls 拓扑 + ControlPlaneView 组合视图，禁止 copy / move rows，不存在「归还」操作。

---

## 4. 关键设计决策（DEC）

### DEC-1 `controls` 是查询期视图，不全量物化

`controls(rep) ≡ owns(rep) ∪ 显式 grant 边`。只物化代理 grant（`AssociationControlProfileRuntime` 增删），正常归属不复制第二条边。避免 spawn/death/转移双写不同步。`ControlDomainQuery.CollectControlled` 内部合并两个来源。

**已知边界（明文声明，非完备性承诺）**：

1. **控制抑制未覆盖**：并集语义下 owner 永远保有控制权——"借来的控制"（代理/心控夺取）可表达，"被夺走时原主失控"（魅惑/恐惧期间不能指挥自己单位）不可表达，order 鉴权会放行原主。闭合路径 = 并集加 suppression 谓词（`controls = (owns − suppressed) ∪ grants`，suppressed 本身是 tag/边数据），需要时以 DEC 修订落地，禁止在消费方各自绕。
2. **controls 不传递**：一跳展开是 Core 策略；链式指挥（A controls B 的域，B controls C）不自动闭包——需要的场景用 profile 数据显式建 A→C 直连边表达。
3. **保留名契约**：Core 只绑定 `Owns`/`Controls`/`MemberOf` 三个类型名（引擎与 catalog 的基建契约）；alliance/diplomacy 等场景类型是纯数据，由引用它们的 profile 在自己的加载点 fail-fast。若未来需要总转换 mod 改名，用 catalog roles 间接层（Core 绑角色不绑字面名），不加第四个保留名。

### DEC-2 关系反向索引先行

现状 `RelationshipRuntime.CollectIncoming` 是全 world 扫描。本 Epic 前置：为 relationship 存储补 **反向邻接索引**（或等价的 per-typeId incoming cache），否则写入域路由（unit → 所属域反查 `TryResolveControlDomain`）在大战场不可用。FilterProfile 求值统一走「anchor 正向展开 → bitset → 与 raw hits 求交」，不做逐 hit 反查。

### DEC-3 DomainStanceQuery（CTRL-3 的硬前置）

删除 unit `Team` 前，先落 `DomainStanceQuery`：`unit → owns 域 → member_of 队伍 → 队伍间 stance` 的解析结果按 (domainA, domainB) 缓存，relationship revision 失效重建。GAS targeting（`TargetResolverFanOutHelper` 等）改读它。

**命名与语义约束**：这是一个通用的「域间关系谓词缓存」，**stance key（hostile / friendly / neutral / cease_fire…）全部是 relationship catalog 数据**（mods 已有 `CombatStance.Hostile` 先例），Core 只提供 `GetStance(domainA, domainB) → stanceId` 与按 stanceId 过滤——不出现 "enemy"/"hostile" 字面分支。早期草案名 `HostilityQuery` 因携带业务语义废弃。

### DEC-4 控制平面 = 拓扑投影；collection 永不迁移，也没有「归还」概念

（本决策取代早期草案中的 `handbackPolicy` 枚举——那是把「归还」误当成 Core 需要认识的操作。）

- **CollectionWrite 按域路由**：写入永远落在被指挥单位所属控制域的 rep entity 上。我框选 `[m01(自有), m99(代理)]`，物理写入是 `(P1Rep, key)=[m01]` 与 `(P2Rep, key)=[m99]`——我此刻对 P2 域 controls 可达，因此有权维护它的域，队友的化身 entity 照常走它自己的框选基建。
- **「我的当前选中」是 ControlPlaneView**：对 `controls` 可达域集合的**组合只读视图**（EntityView 的 domainScope 扩展），不是物理合并的集合。Order fan-out 与 HUD 消费该视图。
- **任何原因**导致 controls 边消失（掉线结束、心控解除、演出归还——association 层一概不知道原因），组合视图即时收缩；对方域内 collection 保持其最新状态，client 重新 bind 即所见即所得。「归还」是拓扑变化的涌现行为，零专用代码路径。
- 「掉线」「心灵控制」「剧本演出接管」都只是 mod 侧打 tag / 增删边的领域 trigger；**association/collection 基建对这些语义零感知**，schema 里不出现任何场景词汇。
- 多控制者写同一域同一 key —— **共享单例，后写覆盖**（见下方小节）。
- **路由策略是 collection profile 的声明字段**，不是全局行为：控制平面语义的 key（如 `collection.command.source`）声明 `writeRouting: byControlDomain`；技能目标语义的 key（如 `collection.ability.*.targets`，选的是目标而非"我维护的域"，可能包含敌方单位）声明 `writeRouting: toContextOwner`，写入 context frame 的 owner 域。两种都是数据，Core 不猜语义。

#### DEC-4 附则：多控制者写同一 (domain, key) = 共享单例，后写覆盖（产品语义，非缺陷）

当 P1 与 P3 同时 controls P2 域，双方的路由写都落在 `(P2Rep, collection.command.source)` 这一行地址上，**后写覆盖前写**。这是本决策的刻意语义，不是并发缺陷，理由：

- **域 collection 是域的指挥状态，不是控制者的私有视图**。单一域在同一时刻只有一份「当前选中集」——两个控制者共享控制，就是共享同一份指挥状态，类比两人共用同一只鼠标：光标只有一个，谁最后动它它就在哪。
- **这是「row 住在所属域、重连即所见即所得」的根基**。若按 writer 隔离出多份行，P2 重连 bind 自己的域时该读谁的行？「所见即所得」语义会碎裂成 N 份视角互相打架。共享单例保证域状态永远只有一个事实。
- **`writerDomain` 是「最后维护者」的追踪元数据**（审计 + Presenter 拓扑现算的输入），**不是隔离键**。它回答"这行是谁写的"，不制造平行副本。
- **确定性**：并发定序由 authoritative input 顺序保证（同一 tick 内多个控制者的写按输入序列全序落地），双端回放逐字节一致。

需要「并行私有选择」的场景有正交出口，**无需破坏域状态单例**：每个控制者推自己的 context frame → `activeCollectionKey` 不同 → 写入**不同的 collection key**（DEC-6 / M2 的 context 路由本来就是为此设计的）。同 key = 共享指挥状态；要私有，换 key，而不是给同 key 造隔离行。

DEC-4 的域路由让 RFC-0064 方案 A 的写时快照大幅简化，且陈旧问题自然消失：

- `controlDomain` 不再是写入的 row metadata——**它就是 collection 地址本身**（row 住在哪个 rep 的域里）。
- `relationKind`（owns / controls / spectate）是 **viewer 相对语义**，由 Presenter / View 求值时按「viewer anchor → row 所在域」的实时拓扑现算，不写死在行里。队友重连的瞬间，浅绿 marker 判定条件（controls 边）不复存在，视图重算即消失——不存在「写时快照过期」问题。
- 写时仅保留 `writerDomain`（谁维护了这行）用于审计与并发定序。
- relationship revision 变更 → ControlPlaneView / Presenter 订阅重算；禁止陈旧帧跨越一个 maintenance 周期以上。

### DEC-6 Context frame 带 ownerToken，支持并发 exec

frame 字段含 `ownerToken`（ability exec 实例 entity / system token）。移除按 token，不按栈顶；实体死亡 / exec abort 由 lifecycle 钩子强制回收其全部 frame。多个并发等待确认（tag）的 exec 各持 frame，`activeCollectionKey` 取「最后激活」的 frame，Tab 循环可在并发 frame 间切换（数据驱动，见 P6）。

### DEC-7 InteractionContextStack 与 IMC 联动

frame 携带可选 `inputContextId`；push/pop 时由同一事务驱动 `PlayerInputHandler` 的 IMC 压栈/弹出（超级武器 context：右键从 move 重映射为 cancel）。两个栈不得各自为政。

### DEC-8 循环依赖拆解

FilterProfile **契约与 registry 属于 Context/Input 域**；association query 只是 CTRL 注入的一个 provider 实现。0062/0063 的互相依赖到此为止。

### DEC-9 Dispatch 打分复用 UtilityAI

`CastDispatchProfile` 的 scorer 直接复用 `UtilityAiRuntimeEvaluator` 的打分/consideration 基建（RFC-0060 已把 utility 定为仲裁 SSOT）。禁止新写平行 scorer。

### DEC-10 面板聚合 = catalog 字段 + profile 规则

ability 定义增加 catalog 字段（`castFamily`、`aggregationAliasId` 等）；`AggregationProfile` 声明 groupBy 维度与冲突处置；玩家 preference 可在 profile 允许范围内覆盖。`CollectionGasEntityCommandPanelSource` 迁移为消费该 profile，删除代码内聚合规则。`groupBy` 是 **key selector 表达式**（对 ability catalog 字段的取值路径，如 `catalog.castFamily` / `template.id` / `ability.id`），不是三值封闭 enum——mod 可按任意 catalog 字段聚合而无需改 Core。

### DEC-11 新 profile 的"动词/种类"一律走注册表，不新增封闭 enum

本 Epic 新引入的所有可扩展点遵循同一模式（对齐 `SystemFactoryRegistry` / graph op 先例）：

- `CastCommitProfile` 的 op（pushFrame / popFrame / submitOrder / writeCollection…）是 **interaction op registry** 注册项（复用 graph op handler 模式）——注意这些是**基建原语**（栈操作、order 提交），不是施法语义动词；"cancel" 不是 Core 概念，只是 mod 数据里某个 input action 映射到 `popFrame` op。
- `CastDispatchProfile` 的 `selector.kind` / `scorer.kind` / `router.kind` 同理为 registry 注册项；`advanceOn` 是事件 key（registry id），不是硬编码字符串分支。
- `FilterProfile` / `AssociationControlProfile` 的谓词复用现有 condition/graph DSL，不新造谓词 enum。

护栏：M9 增加断言「新增 profile schema 中零 Core-only 封闭 enum 分派」。

### DEC-12 Presenter 基建核对结论与本 Epic 所需的扩展点

对现有 Presenter 基建的核查结论：**概念上已是 event → condition → command → behavior 四层**（`PresentationEvent(+Kind/KeyId)` → `ConditionRef(inline|graph)` → `PresenterCommand` → `BehaviorSlot`），规则声明（`presenters.json` + `PresenterDefinitionRegistry`）、command 的 scope/valueSource 映射、param binding 数据化程度足够；且 `EntityCollectionMemberAdded/Removed` 事件已携带 collection KeyId / owner / member / roleId / revision / scope hash，**本 Epic 无需新增事件种类**。但四层的执行侧存在封闭点，本 Epic 依赖其中四处，必须随 PROV 阶段修复：

| # | 现状硬点（代码事实） | 本 Epic 需要 | 落点 |
|---|---------------------|--------------|------|
| 1 | graph condition 求值只注入 `E[0]=Source, E[1]=Target`，无 viewer、无 event payload 寄存器（`PresenterRuleSystem.EvaluateGraph`） | 拓扑谓词条件（viewer 是否为 row 域本人 / controls 可达 / 仅 knowledge grant）需要 **viewer 实体寄存器 + relationship/knowledge graph ops + payload 寄存器** | PROV-4b |
| 2 | `PresenterDefinition.VisibilityCondition` 的 graphProgramId 在 Emit 侧未接线（`PresenterEmitSystem` throw） | per-viewer 可见性（裁判 vs 普通玩家）依赖它 | PROV-4c |
| 3 | `TeamColorResolver` 硬编码 Team1/Team2 色并直读 `Team`/`PlayerOwner`；`PerformAudienceContext` 直读 `Team`/`PlayerOwner` 组件 | palette/相位改 palette catalog + graph 取值（§5.9）；audience 上下文改拓扑求值 | PROV-6b（并入 CTRL-3b 消费者清单） |
| 4 | `InlineConditionKind.SourceIsLocalPlayer` 写死 GlobalContext LocalPlayerEntity；WorldHud audience 只取单一 local viewer | 多 viewer / 多 Seat（#896 ClientLocalSeat；同进程 PresentBinding 分屏在范围内） | PROV-5 → #896/#898 |

**明确不在本 Epic 修的封闭点**（记录为已知边界，避免范围膨胀）：`PresentationEventKind` / `PresenterCommandKind` / `BehaviorKind` / `AssetKind` 封闭 enum、`InlineConditionKind` 不可 mod 注册（复杂条件一律走 graph）、event payload 固定槽。这些不阻塞本 Epic 的全部 UAT。

### DEC-13 零施法状态机：state = frame（client）+ tag（sim），不引入第三个状态概念

（本决策取代早期草案的 `CastFlowProfile` FSM——states/transitions 是在 Input 层再造一套平行状态机，方向错误。）

**权衡分析**（tag 够不够用）：

| 方案 | 代价 | 结论 |
|------|------|------|
| Input 层 FSM（早期草案） | 引入第三个状态概念；必须自建多层叠加、冲突仲裁、打断语义——而这些 GAS 的 tag/effect/exec 体系**已经全部拥有**（tag requirement/immunity、effect 打断、exec abort）；且 client FSM 计时（如蓄力秒数）破坏确定性 | **废弃** |
| 纯 tag + 现有基建 | 失去显式 transition 表的穷举校验 → 用加载期 fail-fast 校验补（ability 声明 follow-up 输入则必须有 exec lifecycle 契约）；失去 transition guard → guard 本来就该是 graph condition（现有模式） | **采用** |

**结论模型**——所谓"施法状态"拆解后只剩两个已有载体，不需要任何新概念：

1. **Client 侧**：唯一交互状态 = `InteractionContextStack` 上有没有某个 frame。「瞄准中」= targeting frame 在栈上；「取消瞄准」= frame 被 pop。frame 的 IMC 声明哪个 input action 映射到哪个 op（`submitOrder` / `popFrame`）——"confirm"/"cancel" 只是 mod 数据里的 action id，Core 无此语义。**不存在 idle→charging→aiming 的 FSM**；frame 的存在性本身就是全部状态。
2. **Sim 侧**：施法进度的 SSOT = ability exec 实例实体上的 **tag + attribute/blackboard**（`exec.awaiting_followup`、蓄力量 attribute），由 exec graph / effect 施加与移除。多段（加里奥 W 两段）、打断（眩晕 abort exec）、冲突（互斥 tag）、蓄力（开始/提交两条 order 之间的 sim tick 差）全部落在 **GAS 已有仲裁体系**内，Input 层零参与。
3. **蓄力确定性红利**：早期草案 `f0=chargeSeconds` 用 client 计时——错。正确：press 提交 begin order，release 提交 commit order，蓄力量由 exec 在 sim 内按 tick 累计（attribute），回放天然一致。
4. **Presenter 事件面收敛**：casting 表现只消费通用事件——order 生命周期、exec / effect 生命周期（现有 `CastCommitted` / `CastFailed` / `EffectApplied` / `EffectActivated`）、`TagEffectiveChanged`、`AttributeValueChanged`、`EntityCollectionMemberAdded/Removed`、entity 生命周期。**`AbilityAimBegun/Updated/Ended/SlotAdvanced` 四个专用事件种类退役**：瞄准指示器 = presenter 规则监听 targeting collection（`collection.ability.aim.*` 只是普通 collection key，保留为 mod 数据）与 exec tag 变化；`AbilityAimPresentationRuntime` / `AbilityAimSessionState` 随之退役。
5. **两类 targeting 会话，统一为 frame**：
   - **pre-order（client-local）**：LoL 式瞄准预览。press 执行 CastCommitProfile 的 op：push targeting frame（引用 InteractionContextProfile）；后续 pointer cast 写 frame 的 collection（indicator 由 presenter 从 collection 渲染）；confirm action → `submitOrder` + `popFrame`。sim 对"瞄准"零感知，取消不产生任何 order。
   - **post-order（sim-driven）**：超级武器指定单位、二段确认。order 已提交，exec 进入等待，由 CTX-6 的 exec lifecycle push frame（contextEntity = exec 实体），follow-up 输入经 frame 翻译为后续 order；exec abort（任何原因）→ lifecycle pop frame，表现随 tag/collection 事件自动收敛。
   两类共用同一 frame 结构与同一 op 词汇，只是 push 的发起者不同（client op vs exec lifecycle）。

附注：`MovePathBegun/Updated/Ended` 与 `SelectionMemberAdded/Removed` 同属历史专用事件面，前者随 #519 / move-path collection 化清理，后者已在 Selection 退役范围（ORD-6）内；本 Epic 不再新增任何专用 presentation 事件种类。

### DEC-14 Pointer Command Intent：双侧谓词路由 + 显式全序胜出 + 群体策略

需求：同一个 pointer intent（"右键"只是绑定数据），按 **actor 能力 × target 事实** 动态路由到不同 order——点敌方单位 = 路由到普攻 ability（"攻击"不是 Core 概念，普攻只是带 weapon catalog tag 的 ability，对齐 RFC-0060 普攻=autocast 语义）；有驻扎能力的单位点可驻扎建筑 = 进驻；点可破坏道具 = 攻击；目标同时可驻扎又可破坏时必须有**明确配置的唯一胜出者**；混合框选时哪些单位做什么也必须显式配置。

**现状核对**（复用清单）：

- `actorOrderRouting`（`ActorOrderRoutingMatcher` + `input_order_mappings.json` candidates）：**actor 侧**按 priority 路由已落地，混合框选（producer rally vs unit move）有测试——但 match 只看 actor 自身 tag/slot，**不看 target**。
- `ContextScoredOrderResolver` + `context_groups.json`：graph 评分选 slot 已落地（平局链 score → entity id → slot index）——但只挂技能键的 `ContextScored` 模式，未接 pointer command；且其 spatial 候选查询**无 knowledge 过滤**（INT-4 必须补）。
- `AutoTargetPolicy.NearestEnemyInRange`：enum 在 Core，`Team` 组件直读发生在 CoreInputMod `LocalOrderSourceHelper` 的 resolver；随 CTRL-3 一并退役，语义并入本决策的 stance 谓词。
- **hover 目标的 knowledge 门控现状是对的**：`LocalOrderSourceHelper.TryResolveHoveredCommandTarget` 经显式 `KnowledgeCommandTargetGate` 过滤；该 gate 复用 `SelectionEligibility.CanTargetCommand(World, KnowledgeProjectionResolver, ...)` 的 presence/position 语义，但没有 globals 缺 resolver 时 allow-all 的装配缺口。本决策必须继承这一语义（见下），禁止"重构即倒退"。

**结论模型——`CommandIntentProfile`**：pointer intent 的 per-actor 规则表，规则 = actor 谓词 × target 事实谓词 → route：

- **谓词是统一 condition DSL 的 shorthand**：§5.11 的 `hasAbilityWithTag` / `allTags` / `stance` 等键在**加载期 lower 到唯一的 condition/graph evaluator**——全系统只有一个谓词求值路径（M9 断言），不存在第二套谓词文法。actor 侧 = 能力事实（带某 catalog tag 的 ability、自身 tag）；target 侧 = tag（`structure.garrisonable`、`destructible` 都是 mod 数据）+ stance + 结构谓词（`hasEntity`）。Core 零 "attack"/"garrison"/"enemy" 字面量。
- **target 事实必须经 viewer knowledge 门控**：L8 谓词读的是该 client 的 KnowledgeProjection 投影事实，**不是 sim 真值**——fog 下不可见单位不可被路由（复用 `CanTargetCommand` presence/position 门控，现有基建已覆盖）；伪装单位按被投影的 tag/stance 路由，且 **target 的归属域也取 viewer 投影所见的域**（stance 以 (actor 域, 投影 target 域) 求值）。注意：**tag/stance 级的事实投影（mask）是尚不存在的基建**，由 INT-8 认领；未落地前 M11 伪装场景标记为依赖 INT-8 的 deferred UAT，fog 可见性场景不受影响。路由产物只是 order 请求，**合法性由 sim 侧 GAS targeting / requirement 终裁**（点击与执行之间事实可变）。
- **stance 谓词语义固定**：`GetStance(actor 所属控制域, target 所属控制域) ∈ 集合`（any-of）；方向性/对称性由 relationship catalog 声明；代理控制下按 **actor 所属域**求值（不是指挥者域）。
- **唯一胜出是显式全序，胜出即终局**：同一 profile 内 rule priority 互不相等，加载期 fail-fast；A∩B 目标由 priority 决定唯一 winner。**胜出 rule 的 route 解析失败（如 `byAbilityTag` 找不到 slot）= 该 actor 本次无 order，不落穿下一条 rule**（禁止 fallback）。需要评分式动态选择时，route 显式委托 `contextGroupId`（复用 ContextScored 评分）——静态全序与评分委托二选一，都在数据里。
- **route 的 slot 定位是 selector 表达式**（DEC-11 registry）：语义路由一律用 `byAbilityTag:...` / `contextGroup:...`；`bySlotIndex:N` 保留为注册 kind 但**禁止用于语义路由**（"普攻"由 `ability.catalog.weapon` 之类的 catalog tag 定位，不是裸 slot 0——否则形态切换/Granted 覆盖后路由错位，且重蹈 slot=面板=语义的耦合）。
- **混合框选 = per-actor 解析 + 显式群体策略**：每个 actor 独立跑规则表（有驻扎能力的进驻、没有的攻击）；`groupPolicy` 声明一致性策略：`independent`，或 `bySelector`（复用 DSP 的 selector/scorer registry 选出决策 actor，其胜出 rule 决定全组——不引入未定义的 "leader" 概念）。groupPolicy 为 profile 顶层唯一（一次 pointer intent 一种群体语义，有意约束，不支持 per-rule 覆盖）。
- **与 frameActions 的仲裁（确定性规则）**：栈顶 frame 先做 `frameActions` 精确匹配拦截；未被拦截的 pointer command action 落入**栈顶 frame 自己的** `commandIntentId`；frame 无 commandIntentId 则该 pointer command 不路由、**不向下层 frame 冒泡**（无 fallback）。解析链：frame 显式 commandIntentId > ControlScheme `defaults.commandIntentId`（仅对 default frame 生效）。
- **与 Dispatch 的两阶段单向组合**：L8 intent 先把 ControlPlaneView **分区为 route groups**（同一胜出 route 的 actors 一组，groupPolicy 在此阶段生效）→ 每个 route group 携带解析出的 orderType/slot 进入 L9 dispatch（selector/scorer/router 在组内生效）；cycle 等 dispatch 状态以 (frame, routeGroupKey) 为 key。两阶段严格单向，dispatch 不回头改路由。
- **性能预算**（与 DEC-2 同规格）：rule 表加载期预编译为 tag bitset 匹配；actor 侧谓词结果按 archetype/tag signature 缓存；graph 谓词仅在 shorthand 无法表达时使用（数据里显式标注）；数百 actor × 每次 pointer intent 的求值必须在 bitset 快路径完成。
- **归属层级**：`CommandIntentProfile` 是 context frame 引用的数据（不同 context 可换 profile——默认指挥 vs 超级武器 context 右键语义不同）；`actorOrderRouting` 是它的 actor 侧子集，迁移后退役该字段。

### DEC-15 设备 → intent 链路分层与运行时重绑定

genre 差异（星际右键指挥 / 红警左键指挥 / 暗黑左键攻击 + WASD 移动 / LOL 鼠标⇄WASD 切换）分四层解决，每层独立数据：

| 层 | 内容 | 载体 | 现状 |
|----|------|------|------|
| 物理绑定 | `<Mouse>/rightButton` → action `"Command"` | `default_input.json` bindings（IMC） | ✅ 已数据化；`"Select"/"Command"` 字面量只存在于 `InteractionActionBindings` 默认常量，消费者读可配置 property |
| 控制方案 | **ControlScheme** = 一组 IMC context + 默认 preference 的命名组合（`scheme.sc2_classic` / `scheme.wasd_move`） | 拟新增 catalog | ❌ 无 |
| action → op | frame 的 `frameActions`（DEC-13） | InteractionContextProfile | 本 Epic 落地 |
| intent → order | CommandIntentProfile（DEC-14） | 拟新增 | ❌ 无 target 侧 |

- **运行时重绑定**：现状 `PlayerInputHandler` 构造期编译 context、无 rebind API；`InputOrderMappingSystem.Remap()` / `SaveUserPreferences` 存在但**全仓库零调用方、启动链路未接线**。本 Epic 补：物理键 rebind API + per-player preference 持久化接线 + ControlScheme 热切换（= IMC push/pop 组合，玩家局内可切 WASD⇄鼠标移动）。
- **WASD 直控移动必须走 OrderQueue**：轴 intent → 按 sim tick 节流的 move order（铁律 1；`CameraAcceptanceMod` 直写 `WorldPositionCm` 是 fixture 专用，禁止进生产路径）；方向类 `OrderSelectionType.Direction` 的 backlog（`s3_direction_key_variant.md`）在此收口。
- "移动"不是 Core 概念：move 只是 intent profile 里一条 route（`moveTo` order type 或 movement ability slot——MobaDemoMod 右键 = `castAbility slot4 Nav.Move` 的先例已证明两种都行）。

---

## 5. 数据 Schema 草案（示例，字段名以实现 PR 为准）

### 5.1 Relationship catalog（扩展 `assets/Configs/Relationships/catalog.json`）

```json
{ "types": [
  { "id": "Owns", "isSymmetric": false },
  { "id": "Controls", "isSymmetric": false },
  { "id": "MemberOf", "isSymmetric": false },
  { "id": "Ally", "isSymmetric": true }
]}
```

### 5.2 FilterProfile

```json
{
  "id": "filter.controllable.default",
  "associationQuery": {
    "anchor": "localPlayerRep",
    "edgeTypes": ["Owns", "Controls"],
    "expand": "outgoing"
  },
  "exclude": { "tags": ["state.dead", "presentation.hidden"] }
}
```

### 5.3 InteractionContextProfile（ability 引用）

```json
{
  "id": "ctx.ability.superweapon.confirm_targets",
  "activeCollectionKey": "collection.ability.superweapon.targets",
  "activeEntityViewKey": "view.ability.superweapon.targets",
  "filterProfileId": "filter.superweapon.valid_targets",
  "inputContextId": "imc.ability.confirm",
  "presenterProfileId": "presenter.ability.superweapon.target_marker"
}
```

### 5.4 AssociationControlProfile（通用「条件 → 边增删」规则，schema 零业务词汇）

profile 是一个纯粹的谓词→边操作规则：`when`（tag / relationship 谓词组合，复用现有 condition DSL）成立时 grant 边，`revokeWhen` 成立时删边。**tag 字符串对 Core 完全不透明**——`participant.offline`、`unit.mind_controlled`、`script.cinematic_owned` 都只是 mod 数据，同一 schema 覆盖任意接管场景：

```json
{
  "id": "profile.control.ally_offline_proxy",
  "when": { "all": [
    { "relationship": "Ally", "between": ["grantee", "grantor"] },
    { "tag": "participant.offline", "on": "grantor" }
  ]},
  "grant": { "edgeType": "Controls", "from": "grantee", "scope": "all_owned_by:grantor" },
  "revokeWhen": { "not": { "tag": "participant.offline", "on": "grantor" } }
}
```

```json
{
  "id": "profile.control.mind_control_steal",
  "when": { "tag": "unit.mind_controlled_by:caster", "on": "unit" },
  "grant": { "edgeType": "Controls", "from": "casterRep", "scope": "unit" },
  "revokeWhen": { "not": { "tag": "unit.mind_controlled_by:caster", "on": "unit" } }
}
```

没有 handback / policy 字段：边消失后的一切行为由 DEC-4 的域路由 + ControlPlaneView 涌现，profile 不需要也不允许描述「之后集合怎么办」。

### 5.5 CastCommitProfile（施法提交绑定——无状态机，见 DEC-13）

profile 只声明两件事：激活 slot 时执行什么 op 序列；若 push 了 targeting frame，frame 内的 action→op 映射是什么。没有 states、没有 transitions。

```json
{
  "id": "cast.commit.quick",
  "onActivate": [ { "op": "submitOrder", "payload": { "spatial": "cursorWorld" } } ]
}
```

```json
{
  "id": "cast.commit.aim_confirm",
  "onActivate": [ { "op": "pushFrame", "contextProfileId": "ctx.targeting.ground" } ],
  "frameActions": {
    "Confirm": [ { "op": "submitOrder", "payload": { "spatial": "framePointer" } }, { "op": "popFrame" } ],
    "Back":    [ { "op": "popFrame" } ]
  }
}
```

```json
{
  "id": "cast.commit.charge_release",
  "onActivate": [
    { "op": "submitOrder", "payload": { "i1": "phase.begin" } },
    { "op": "pushFrame", "contextProfileId": "ctx.targeting.charge" }
  ],
  "frameActions": {
    "Release": [ { "op": "submitOrder", "payload": { "i1": "phase.commit", "spatial": "framePointer" } }, { "op": "popFrame" } ]
  }
}
```

要点：

- `op` 是 **interaction op registry 注册项**（DEC-11）：`pushFrame` / `popFrame` / `submitOrder` / `writeCollection`…——基建原语，不是施法语义。"Confirm"/"Back"/"Release" 是 mod 数据里的 input action id，Core 无 "aim"/"cancel" 概念。
- **蓄力量不在 client**：begin/commit 两条 order 之间由 exec 在 sim 内按 tick 累计（DEC-13 #3），payload 里没有 `chargeSeconds`。
- **多段技能不在这里表达**：二段/追加输入属于 sim（exec 等待 + tag + CTX-6 lifecycle push frame），profile 只管"这一次激活如何变成 order"。
- 指示器、蓄力条等表现全部是 presenter 监听 frame collection / exec tag / attribute 的通用事件（DEC-13 #4），profile 里没有任何 show/hide 表现指令。
- `InteractionModeType` 六个值退役为等价 profile 数据组合。

### 5.6 ClientCastPreference（scope 链）

```json
{
  "global": { "castCommitId": "cast.commit.quick" },
  "perTemplate": { "champion.xerath": { "castCommitId": "cast.commit.aim_confirm" } },
  "perFormSet": { "champion_skill_sandbox_jayce_forms/hammer": {} },
  "perSlot": { "champion.xerath/2": { "castCommitId": "cast.commit.quick_with_indicator" } }
}
```

解析优先级：perSlot > perFormSet > perTemplate > global；mod 可声明某 slot `lockedCastCommitId` 禁止玩家覆盖。

### 5.7 AggregationProfile + ability catalog 字段

```json
// abilities.json 片段（catalog 字段）
{ "id": "Ability.Tank.ChargeCannon",   "castFamily": "family.charge_shot" }
{ "id": "Ability.EliteMarine.ChargeCannon", "castFamily": "family.charge_shot" }
{ "id": "Ability.Marine.Stimpack",     "castFamily": "family.stimpack" }
{ "id": "Ability.EliteMarine.Stimpack","castFamily": "family.stimpack" }
```

```json
{
  "id": "aggregation.rts.default",
  "groupBy": "catalog.castFamily",      // 案例①：key selector 表达式，非封闭 enum
  "overflow": "nextPanelSlot",
  "badge": "perSourceTemplateIcon"
}
{ "id": "aggregation.by_template",   "groupBy": "template.id" }   // 案例②
{ "id": "aggregation.by_ability_id", "groupBy": "ability.id" }    // 案例③
```

`groupBy` 是对 ability catalog / 定义字段的 **取值路径表达式**（DEC-10）：mod 可按任意 catalog 字段聚合（例如自定义 `catalog.uiCategory`），不需要改 Core；三个案例只是内置字段的取值示例。

### 5.8 CastDispatchProfile

```json
{
  "id": "dispatch.all_together",
  "selector": { "kind": "all" },
  "router": { "kind": "parallel", "sharedOrderId": true }
}
{
  "id": "dispatch.one_by_one",
  "selector": { "kind": "cycle", "advanceOn": "orderAccepted" },
  "router": { "kind": "sequential" }
}
{
  "id": "dispatch.utility_nearest",
  "selector": { "kind": "topN", "n": 3 },
  "scorer": { "kind": "utility", "considerations": ["distanceToTarget:invert"] },
  "router": { "kind": "parallel", "sharedOrderId": true }
}
{
  "id": "dispatch.resource_first",
  "selector": { "kind": "topN", "n": 1 },
  "scorer": { "kind": "utility", "considerations": ["attribute.mana:desc", "attribute.hp:desc"] },
  "router": { "kind": "sequential" }
}
```

`selector.kind` / `scorer.kind` / `router.kind` 均为 **registry 注册项**（DEC-11）；`advanceOn` 是事件 key（registry id）；`considerations` 直接引用 AttributeRegistry 注册的属性 id。

### 5.9 Presenter command marker rules（对齐现有 PresenterRule 结构）

现有基建事实：`PresenterRule = EventFilter(kind+key) + ConditionRef(inline | graphProgramId) + PresenterCommand`；`EntityCollectionPresentationEventSystem` 发布的 `EntityCollectionMemberAdded/Removed` 事件 **已携带** collection KeyId、owner（Target）、成员 entity（Source）、roleId、revision（FloatD）、scope hash（PayloadA）——RFC-0064「复用现有事件」成立，marker 规则直接落在这套结构上：

```json
{
  "rules": [
    {
      "event": { "kind": "EntityCollectionMemberAdded", "key": "collection.command.source" },
      "condition": { "graphProgramId": "graph.cond.viewer_is_row_domain" },
      "command": { "kind": "CreatePresenter", "definitionId": "selection.marker",
                    "scopeSource": "EventPayloadA", "ownerSource": "EventSource",
                    "paramKey": "marker.tint", "paramGraphProgramId": "graph.palette.self_deep" }
    },
    {
      "event": { "kind": "EntityCollectionMemberAdded", "key": "collection.command.source" },
      "condition": { "graphProgramId": "graph.cond.viewer_controls_row_domain" },
      "command": { "kind": "CreatePresenter", "definitionId": "selection.marker",
                    "scopeSource": "EventPayloadA", "ownerSource": "EventSource",
                    "paramKey": "marker.tint", "paramGraphProgramId": "graph.palette.self_light" }
    },
    {
      "event": { "kind": "EntityCollectionMemberAdded", "key": "collection.command.source" },
      "condition": { "graphProgramId": "graph.cond.viewer_has_knowledge_grant" },
      "command": { "kind": "CreatePresenter", "definitionId": "selection.marker",
                    "scopeSource": "EventPayloadA", "ownerSource": "EventSource",
                    "paramKey": "marker.tint", "paramGraphProgramId": "graph.palette.team_phase" }
    },
    {
      "event": { "kind": "EntityCollectionMemberRemoved", "key": "collection.command.source" },
      "command": { "kind": "DestroyScopedPresenter", "scopeSource": "EventPayloadA" }
    }
  ]
}
```

**没有 `viewerRole` 枚举**——"localPlayer / referee / spectator" 是业务角色，禁止进入基建 schema。viewer 与 row 域的关系（本人域 / controls 可达 / 仅 knowledge grant）全部是 **graph condition 拓扑谓词现算**（DEC-5），裁判只是一个恰好持有 knowledge grant、不持 controls 边的 viewer anchor。palette / 相位取值同样走 `paramGraphProgramId` + palette catalog 数据（PROV-6），替代 `TeamColorResolver` 的硬编码 Team1/Team2 色。

### 5.10 ControlPlaneView（组合只读视图）

```json
{
  "viewKey": "view.control_plane.command",
  "collectionKey": "collection.command.source",
  "role": "CommandSource",
  "domainScope": {
    "anchor": "localPlayerRep",
    "edgeTypes": ["Owns", "Controls"],
    "includeAnchor": true
  }
}
```

求值 = 对 anchor 经 `domainScope` 可达的每个域取 `(domainRep, collectionKey)` 拼接为只读序列，row 保留其所在域信息。Order fan-out、HUD、PanelRouter 消费该视图；relationship revision 变更触发重算。

### 5.11 CommandIntentProfile（DEC-14）与 ControlScheme（DEC-15）

```json
{
  "id": "intent.command.rts_default",
  "groupPolicy": { "kind": "independent" },
  "rules": [
    {
      "priority": 30,
      "actor": { "hasAbilityWithTag": "ability.catalog.garrison_enter" },
      "target": { "allTags": ["structure.garrisonable"], "stance": ["neutral", "friendly"] },
      "route": { "orderTypeKey": "castAbility", "slot": "byAbilityTag:ability.catalog.garrison_enter" }
    },
    {
      "priority": 20,
      "actor": { "hasAbilityWithTag": "ability.catalog.weapon" },
      "target": { "anyTags": ["destructible"], "stance": ["hostile", "neutral"] },
      "route": { "orderTypeKey": "castAbility", "slot": "byAbilityTag:ability.catalog.weapon" }
    },
    {
      "priority": 10,
      "target": { "hasEntity": false },
      "route": { "orderTypeKey": "moveTo" }
    }
  ]
}
```

- 同 profile 内 `priority` 互不相等，加载期 fail-fast（DEC-14 显式全序）：目标同时命中 garrison（30）与 destructible（20）时，唯一胜出者永远是 30。
- "右键点敌自动攻击" = priority 20 这条规则数据：普攻由 `ability.catalog.weapon` catalog tag 定位（通常恰好解析到 slot0，但那是 catalog 数据的事实，不是 Core 约定——形态切换 / Granted 覆盖后仍然正确）。语义路由禁止裸 `bySlotIndex`（DEC-14）。
- 谓词键（`hasAbilityWithTag` / `allTags` / `anyTags` / `stance` / `hasEntity`）是统一 condition DSL 的 **shorthand**，加载期 lower 到唯一 evaluator（M9 断言只有一个谓词求值路径）；pointer 命中分类是结构谓词（`hasEntity`），不是隐藏 enum——minimap / world 命中由 L4 InputCastSpec 归一化为同一 hit 结构。target 事实经 viewer knowledge 投影求值（DEC-14）。
- `groupPolicy.kind` 为 registry 注册项（DEC-11）：`independent` = 每 actor 独立路由；`bySelector` = `{ "kind": "bySelector", "selector": {...} }` 复用 DSP selector/scorer registry 选出决策 actor，其胜出 rule 决定全组（不引入未定义的 "leader" 概念）。groupPolicy 为 profile 顶层唯一——一次 pointer intent 一种群体语义（有意约束）。
- 需要动态评分时，`route` 写 `{ "contextGroupId": "..." }` 委托 ContextScored 评分——静态全序与评分委托二选一。

```json
{
  "id": "scheme.sc2_classic",
  "inputContexts": ["imc.pointer.command_on_right"],
  "defaults": { "commandIntentId": "intent.command.rts_default" }
}
{
  "id": "scheme.ra_like",
  "inputContexts": ["imc.pointer.command_on_left"],
  "defaults": { "commandIntentId": "intent.command.rts_default" }
}
{
  "id": "scheme.diablo_like",
  "inputContexts": ["imc.pointer.command_on_left", "imc.movement.wasd"],
  "defaults": { "commandIntentId": "intent.command.arpg_default" }
}
```

同一局内玩家可在 mod 允许的 scheme 集内热切换（= IMC push/pop 组合 + preference 写入；栈上非 default frame 保留，default frame 的 intent 引用即时改读新 scheme）；WASD 轴 intent 产生按 sim tick 节流的 move order，走 OrderQueue（DEC-15）。

**「右键怎么变成 move intent」端到端数据链**（零 Core 语义参与）：

```text
<Mouse>/rightButton                        （default_input.json binding，scheme.sc2_classic 的 IMC）
  → InputAction "Command"                  （action id 本身是数据，InteractionActionBindings 可换）
    → 栈顶 default frame：frameActions 无精确匹配 → 落入 frame 的 commandIntentId
      → intent.command.rts_default         （frame 未显式声明时读 scheme default）
        → per-actor 规则表：命中 priority=10（target.hasEntity=false）
          → route { orderTypeKey: "moveTo" } → L9 dispatch → OrderQueue
```

红警化 = 换 binding（`scheme.ra_like`，同一 intent profile）；暗黑化 = binding 换 + intent profile 换（左键点敌命中 weapon rule）；WAR3 式 "M 键强制移动" = 另一个 action 绑一条只含 move rule 的 profile。

---

## 6. BDD 验收（Gherkin UAT Showcases）

以下场景是 Epic 的验收 SSOT：每个 Scenario 必须有 headless acceptance test 或 playable showcase 支撑；标 `[showcase]` 的必须可运行演示。

### 6.1 Persona A — Mod 开发者（我写了配置，应得到什么）

```gherkin
Feature: M1 框选即控制域投影（RTS 默认 context）
  As a mod developer
  I want box/polygon/lasso 输入只产出 raw collection，控制域由 FilterProfile 决定
  So that 我不写一行 C# 就能定义"框谁算谁"

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
    Then (P1Rep, collection.command.source) = [m01, m02, m03]（additive 模式）
    When 玩家1 按住 ctrl 点选 m02
    Then (P1Rep, collection.command.source) = [m01, m03]（subtract 模式）
```

```gherkin
Feature: M2 技能域 context 与恢复 [showcase]
  As a mod developer
  I want ability 配 interactionContextProfile，施法期间框选写入技能专属 key
  So that 超级武器"指定单位"不污染默认框选

  Scenario: 超级武器进入/退出 confirm targets context
    Given ability "superweapon" 配置 ctx.ability.superweapon.confirm_targets
    And (P1Rep, collection.command.source) = [m01, m02]
    When 玩家1 激活 superweapon 且 exec 打上等待确认 tag（如 exec.awaiting_targets，mod 数据，DEC-13）
    Then InteractionContextStack 压入 frame(ownerToken = exec 实例)
    And 此时框选 [m05, m06] 写入 (P1Rep, collection.ability.superweapon.targets)
    And (P1Rep, collection.command.source) 仍 = [m01, m02]
    When 技能提交（或 cancel / caster 死亡）
    Then frame 按 ownerToken 移除，activeKey 恢复 collection.command.source
    And 后续框选恢复写入默认 key

  Scenario: context push 同时切换 IMC
    Given ctx.ability.superweapon.confirm_targets 声明 inputContextId "imc.ability.confirm"
    When context 压入
    Then 右键从 "Command(move)" 重映射为 "Cancel"
    When context 弹出
    Then 右键恢复 "Command(move)"
```

```gherkin
Feature: M3 代理控制是纯拓扑投影 [showcase]
  As a mod developer
  I want 用通用的「条件 → controls 边增删」profile 表达接管，集合由域路由 + 组合视图导出
  So that 掉线/心控/演出等任何接管场景零专用代码、零归还逻辑

  Background:
    Given profile.control.ally_offline_proxy 已注册（§5.4，tag 字符串对 Core 不透明）
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

  Scenario: 多控制者写同域同 key 是共享单例，后写覆盖（DEC-4 附则）
    Given Controls(P1Rep → P2 域) 与 Controls(P3Rep → P2 域) 同时生效
    When 玩家1 框选 [m99a(P2 owns)]
    Then (P2Rep, collection.command.source) = [m99a]，writerDomain=P1Rep
    When 玩家3 随后框选 [m99b(P2 owns)]
    Then (P2Rep, collection.command.source) = [m99b]，writerDomain=P3Rep   # 后写覆盖，writerDomain 记录最后维护者
    And 玩家1 的 ControlPlaneView(command) 读到 [m99b]                      # 共享的是同一份域指挥状态
    And 需要并行私有选择时走正交出口：P1 push 自己的 context frame，用不同 collection key 写私有集，不触碰共享 key

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
    Given presenter catalog §5.9 已加载
    And 玩家1 的 ControlPlaneView 含 m01（住在 P1Rep 域）与 m99（住在 P2Rep 域）
    Then m01 渲染 palette.self.deep（深绿）ring   # viewer==域 → Owns
    And m99 渲染 palette.self.light（浅绿）ring   # viewer→域 走 Controls 边 → 现算为 proxy
    When P2 重连（Controls 边消失）
    Then m99 的 marker 随视图重算在下一 revision diff 消失（无全量重建抖动，无陈旧快照）
```

```gherkin
Feature: M5 裁判多控制域投影 [showcase]
  As a mod developer
  I want 中立 player rep 通过 knowledge grant 读所有 playerRep 的 command.source
  So that 观战/裁判不需要任何专用 selection 服务

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
    Then 面板格1 = 兴奋剂（3 单位聚合）
    And 面板格2 = 炮车蓄力炮（2 单位聚合）
    And 面板格3 = 强化陆战队蓄力炮（1 单位）

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
    Given 玩家1 框选 5 个追猎（均有 blink，slot 一致）
    And 目标点 T 已由 commit profile 解析

  Scenario: 集体 blink
    Given 该 slot 绑定 dispatch.all_together
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
      | 断言 |
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
      | Presenter 规则零 viewerRole 业务角色枚举（viewer 语义全部拓扑谓词现算） |
      | Input 层零施法 FSM：无 states/transitions schema、无 _isAiming 类字段（交互状态只在 InteractionContextStack） |
      | PresentationEventKind 零 aim/cast 专用种类新增；AbilityAimBegun/Updated/Ended/SlotAdvanced 已退役 |
      | Core intent/route 路径零 "attack"/"garrison"/"move" 语义字面量；同 profile 内 priority 冲突加载期 fail-fast |
      | intent 谓词求值路径全系统唯一（shorthand 加载期 lower，无第二套谓词 evaluator） |
      | L8 target 事实求值零 sim 真值直读（必经 viewer KnowledgeProjection） |
      | 语义路由零裸 bySlotIndex（ability 定位一律 catalog tag / contextGroup） |
      | 生产路径零轴输入直写 WorldPositionCm（WASD 移动必经 OrderQueue） |
      | InteractionModeType 类型已删除或仅存于迁移 shim 白名单 |
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
  As a mod developer
  I want 用 CommandIntentProfile 声明"pointer intent × actor 能力 × target 事实 → route"
  So that 右键点敌自动攻击、点建筑进驻等语义零 Core 代码，且歧义有显式唯一胜出者

  Background:
    Given intent.command.rts_default 已注册（§5.11：garrison=30 > weapon=20 > ground move=10）
    And "攻击"/"进驻"都只是 byAbilityTag route 的规则数据——Core 无 attack/garrison 字面量

  Scenario: 点敌方单位 = 路由到普攻 ability（catalog tag 定位，不是裸 slot0）
    Given 我框选了带 ability.catalog.weapon 技能的部队
    When pointer intent 落在 stance=hostile 的单位上
    Then 每个 actor 提交 castAbility order，slot 由 byAbilityTag:ability.catalog.weapon 解析
    And 某个 actor 形态切换后武器换到别的 slot，路由依然正确
    And 全程没有名为 "attack" 的 order type 或 Core 分支

  Scenario: 目标同时可驻扎又可破坏 → priority 全序唯一胜出
    Given 中立建筑同时带 structure.garrisonable 与 destructible tag
    When 有驻扎能力的单位收到 pointer intent
    Then 命中 priority=30 的 garrison rule（30 > 20，唯一 winner）
    And 若 mod 配置两条规则同 priority，加载期 fail-fast 报错（禁止运行时隐式平局）

  Scenario: 胜出即终局，route 解析失败不落穿
    Given 某 actor 命中 garrison rule 但 byAbilityTag 解析不到 slot（技能刚被移除）
    Then 该 actor 本次无 order，不落穿到 weapon rule（禁止 fallback）

  Scenario: 混合框选 per-actor 路由 + 显式群体策略
    Given 框选 = [有驻扎能力的步兵 ×2, 只有武器的坦克 ×1]，groupPolicy.kind=independent
    When pointer intent 落在"可驻扎+可破坏"建筑上
    Then 步兵 ×2 → 进驻 order，坦克 → 普攻 order（各自跑规则表）
    When 换 groupPolicy = { kind: bySelector, selector: ... } 的 profile
    Then selector 选出的决策 actor 的胜出 rule 决定全组 route（不满足谓词的 actor 不提交）

  Scenario: intent 分区 → dispatch 两阶段单向组合
    Given 5 个 actor 命中同一 weapon rule，该 route group 的 slot 绑 dispatch.one_by_one
    When 玩家连续两次 pointer intent
    Then L8 先分区出 route group，L9 在组内推进 cycle 指针（状态 key = (frame, routeGroupKey)）
    And dispatch 不回头修改任何 actor 的路由结果

  Scenario: target 事实经 knowledge 投影求值（fog）
    Given 敌方单位在我的 fog 中不可见
    Then pointer intent 无法将其作为 entity 命中（继承 CanTargetCommand 门控）
    And 后续 sim 侧 GAS targeting 终裁一切合法性（点击与执行之间事实可变）

  Scenario: 伪装单位按投影事实路由 [deferred：依赖 INT-8 事实投影]
    Given 敌方伪装单位向我投影 stance=friendly 的假事实（含伪造归属域）
    When pointer intent 落在它身上
    Then 路由按投影事实命中非攻击 rule（不读 sim 真值；stance 以 (actor 域, 投影 target 域) 求值）

  Scenario: 评分委托复用 ContextScored
    Given 某 rule 的 route = { "contextGroupId": "interaction_arcweaver_action" }
    Then 胜出后 slot/target 由 context_groups.json 的 graph 评分决定（复用现有平局链）
    And 评分候选查询同样经 knowledge 过滤（INT-4）

  Scenario: context 切换换 intent profile，无 commandIntentId 则不路由不冒泡
    Given 超级武器 context frame 引用 intent.command.superweapon
    When 该 frame 在栈顶
    Then 同一 pointer intent 按 superweapon profile 路由；pop 后恢复 rts_default
    Given 某 targeting frame 未声明 commandIntentId
    Then 未被 frameActions 拦截的 pointer command 不路由、不向下层 frame 冒泡（无 fallback）
```

```gherkin
Feature: M12 ControlScheme — genre 键位差异纯数据
  Scenario Outline: 同一 Core，不同 genre 方案
    Given mod 声明 <scheme>
    When 玩家执行 <input>
    Then 产生 <result>，Core 零 genre 分支

    Examples:
      | scheme            | input        | result                         |
      | scheme.sc2_classic | 右键点地面   | moveTo order                   |
      | scheme.ra_like     | 左键点地面   | moveTo order（Command 绑左键）  |
      | scheme.diablo_like | 左键点敌人   | 普攻 castAbility order（byAbilityTag 解析） |
      | scheme.diablo_like | WASD         | 按 sim tick 节流的 move order 流 |

  Scenario: WASD 直控走 OrderQueue（铁律 1）
    Given scheme.diablo_like 激活
    When 按住 W 2 秒
    Then 产生的全部是 OrderQueue 内的 move order，零系统直写 WorldPositionCm
    And 回放该 order 流位置轨迹 bit 级一致
```

### 6.2 Persona B — 玩家（我改了偏好，应得到什么）

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
Feature: P5 我看到的 marker（玩家侧验收）
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
    When 我在设置里从鼠标移动切到 WASD
    Then 立即生效：WASD 产生 move order，右键语义按新 scheme 的 intent profile 路由
    And 切回后行为完全恢复，无残留绑定

  Scenario: 物理改键持久化
    When 我把 Command 从右键改绑到侧键
    Then 本局立即生效，重开客户端仍生效（per-player preference 持久化）
    And 改键只动 binding 层，intent/route 配置零变化

  Scenario: mod 锁定方案集
    Given mod 只允许 scheme.sc2_classic
    When 我尝试切到 scheme.diablo_like
    Then 设置界面显示不可用，行为保持 sc2_classic
```

---

## 7. Sub-issue 分解（Workstream 重组）

> 原 #522/#536/#537/#538 的未完成子单**重新挂到本 Epic**，编号沿用；本表标注新增项与被修订项。

### Phase 0 — 前置基建（新增，阻塞后续全部）

| ID | 内容 | 备注 |
|----|------|------|
| PRE-1 | Relationship 反向邻接索引 / incoming cache | DEC-2 |
| PRE-2 | DomainStanceQuery + stance catalog 缓存 | DEC-3；CTRL-3 硬前置 |
| PRE-3 | #535 vs #577 仲裁与 ORD 遗留合并 | 单一 canonical PR |

### Phase 1 — Order/Collection 边界（继承 ORD）

ORD-1..ORD-8 按原文；追加修订：

| ID | 修订 |
|----|------|
| ORD-4b | `OrderSelectionReference` 改为自包含目标集或 `(owner, collectionKey, revision)`，删除 selection 容器实体引用与 per-order lease 实体分配 |

### Phase 2 — Context Stack（继承 CTX，修订）

CTX-1..CTX-10 按原文；修订：

| ID | 修订 |
|----|------|
| CTX-1b | frame 增加 `ownerToken` + `inputContextId`；按 token 移除；lifecycle 回收钩子（DEC-6/7） |
| CTX-7b | 明确产物 = CastCommitProfile + interaction op registry + loader（§5.5，DEC-13：无 FSM schema），`InteractionModeType` 退役映射表 |
| CTX-7c | 退役 `AbilityAimBegun/Updated/Ended/SlotAdvanced` 事件种类与 `AbilityAimPresentationRuntime` / `AbilityAimSessionState`；indicator 迁 presenter 通用事件（tag / collection / attribute，DEC-13 #4） |
| CTX-8b | ClientCastPreference scope 链 schema（§5.6）+ mod lock 语义 |

### Phase 3 — Control Plane（继承 CTRL，修订）

CTRL-1..CTRL-10 按原文；修订：

| ID | 修订 |
|----|------|
| CTRL-1b | `controls` 为查询期视图（DEC-1） |
| CTRL-3b | 依赖 PRE-2；列全消费者迁移清单（GAS targeting / TeamColorResolver / PresentPhaseResolver / lifecycle snapshot / #499 publisher / `SelectionEligibility.CanAcquire` 的 `Team`+`RelationshipFilter` 直读 / CoreInputMod `LocalOrderSourceHelper` 的 NearestEnemyInRange resolver） |
| CTRL-4b | AssociationControlProfile = 通用「谓词 → 边增删」规则引擎，复用 condition DSL，schema 零业务词汇（DEC-4；无 handback/policy 字段） |
| CTRL-4c | CollectionWrite 域路由：写入按被指挥单位所属域落到对应 rep，row 记 writerDomain（DEC-4） |
| CTRL-4d | ControlPlaneView：EntityView domainScope 扩展，controls 可达域组合只读视图；Order fan-out / HUD / PanelRouter 改消费该视图（DEC-4，衔接 ORD-4） |

### Phase 4 — Provenance & Presenter（继承 PROV，修订）

PROV-1..PROV-8 按原文；修订：

| ID | 修订 |
|----|------|
| PROV-1b | provenance 简化：controlDomain 由 collection 地址承载，写时仅存 writerDomain；relationKind 不入行（DEC-5） |
| PROV-2b | relationship revision → ControlPlaneView / Presenter 重算钩子；viewer 相对语义拓扑现算（DEC-5，取代"失效/驱逐"方案） |
| PROV-4b | Presenter graph condition 求值上下文扩展：注入 viewer 实体寄存器 + event payload 寄存器 + relationship/knowledge 拓扑谓词 graph ops（DEC-12 #1；现状只有 E[0]=Source, E[1]=Target） |
| PROV-4c | 接线 `PresenterDefinition.VisibilityCondition` 的 graphProgramId Emit 路径（DEC-12 #2；现状 `PresenterEmitSystem` 直接 throw） |
| PROV-6b | 退役 `TeamColorResolver` 硬编码 Team1/Team2 色与 `PresentAudienceContext` 的 `Team`/`PlayerOwner` 直读，改 palette catalog + 拓扑求值（DEC-12 #3，并入 CTRL-3b 消费者清单） |

### Phase 5 — Panel Router & 聚合（新增 PNL）

| ID | 内容 |
|----|------|
| PNL-1 | ability catalog 字段（castFamily / alias）schema + loader |
| PNL-2 | AggregationProfile registry + groupBy key selector 表达式求值（DEC-10；非封闭 enum） |
| PNL-3 | PanelRouter：intent → 聚合格 → per-entity (entity, slotIndex)；删除 UI 反打 input action 路径 |
| PNL-4 | `CollectionGasEntityCommandPanelSource` 迁移为消费 profile；FormSet 切换重算 |
| PNL-5 | 玩家聚合偏好 + 持久化 |

### Phase 6 — Cast Dispatch（新增 DSP）

| ID | 内容 |
|----|------|
| DSP-1 | CastDispatchProfile schema + registry；selector/scorer/router kind 为 registry 注册项（DEC-11） |
| DSP-2 | scorer 桥接 UtilityAiRuntimeEvaluator（DEC-9） |
| DSP-3 | 挂点：施法键路径 = command collection → fan-out 之间；pointer 路径 = DEC-14 分区后的 route group → fan-out（两阶段单向组合）；shared order id 语义保持 |
| DSP-4 | cycle/sequential 状态（advanceOn orderAccepted） |
| DSP-5 | 玩家 dispatch 偏好（mod 可锁） |

### Phase 6.5 — Command Intent & ControlScheme（新增 INT）

| ID | 内容 |
|----|------|
| INT-1 | CommandIntentProfile schema + registry + loader：priority 全序加载期 fail-fast，谓词复用 condition DSL（DEC-14） |
| INT-2 | target 谓词求值：tag + `DomainStanceQuery` stance + 结构谓词（hasEntity），**必须经 viewer KnowledgeProjection 投影**（复用 `CanTargetCommand` 语义，禁读 sim 真值）；stance 按 actor 所属域求值；退役 `AutoTargetPolicy.NearestEnemyInRange`；谓词 shorthand 加载期 lower 到唯一 condition evaluator，rule 表预编译 tag bitset（DEC-14 性能预算） |
| INT-3 | per-actor 路由执行 + route group 分区 + groupPolicy registry（independent / bySelector）；胜出即终局不落穿；迁移并退役 `actorOrderRouting` 字段 |
| INT-4 | route 评分委托：接线 `ContextScoredOrderResolver` 到 pointer command 路径（现状只挂技能键），并为其 spatial 候选查询补 knowledge 过滤（现状裸 QueryRadius） |
| INT-5 | ControlScheme catalog + 热切换（IMC push/pop 组合）+ per-player 物理键 rebind API 与 preference 持久化接线（现状 `Remap()`/`SaveUserPreferences` 零调用方，DEC-15） |
| INT-6 | WASD 轴 intent → sim tick 节流 move order（走 OrderQueue；收口 `s3_direction_key_variant.md` backlog） |
| INT-7 | context frame 引用 commandIntentId + frameActions 仲裁规则（精确匹配拦截 → frame intent → 不冒泡；衔接 CTX-1b） |
| INT-8 | KnowledgeProjection 事实投影扩展：per-viewer tag/stance mask（伪装/假情报的基建前提；M11 伪装场景依赖本单，未落地前该场景 deferred） |

### Phase 7 — Showcase & 护栏 & 文档（SHOW/GUARD/DOC）

| ID | 内容 |
|----|------|
| SHOW-1 | [showcase] M2 超级武器 context（含 IMC 切换） |
| SHOW-2 | [showcase] M3+M4+P5 代理控制拓扑投影 + marker + 边消失涌现"归还"（掉线与心控两种 trigger 各演示一遍，证明 trigger 无关性） |
| SHOW-3 | [showcase] M5+P8 裁判多控制域投影 |
| SHOW-4 | [showcase] M6+P3 面板聚合三案例切换 |
| SHOW-5 | [showcase] M8+P4 追猎 blink 三种 dispatch |
| SHOW-6 | [showcase] M11+M12+P9 pointer intent 路由（驻扎/破坏歧义胜出、混合框选 per-actor）+ ControlScheme 热切换（右键⇄左键⇄WASD） |
| GUARD-1 | M9 全部 ArchitectureTests |
| GUARD-2 | M10 确定性回放 acceptance（headless 双端 hash） |
| DOC-1 | gitbook 回写（architecture + reference + contributing），退役文档打 deprecation |

---

## 8. 依赖与迁移顺序

```text
#239 AAC / OwnershipResolver（已有）
  → PRE-1/2/3
    → Phase 1 (ORD)  ──┐
    → Phase 2 (CTX)  ──┼→ Phase 4 (PROV) → Phase 7 SHOW-2/3
    → Phase 3 (CTRL) ──┘
    → Phase 5 (PNL) →──┐
    → Phase 6 (DSP) →──┼→ Phase 7 SHOW-4/5/6
    → Phase 6.5 (INT) ─┘   （INT-2 依赖 PRE-2 stance；INT-7 依赖 CTX-1b）
```

- FilterProfile 契约在 CTX；association provider 由 CTRL 注入（DEC-8）。
- CTRL-3（删组件）必须晚于 PRE-2 与全部消费者迁移。
- Phase 5/6 与 3/4 可并行，仅依赖 Phase 1/2 的 collection/context 基建。

## 9. 非目标

- 不重写 MassNavigationFlow solver、RelationshipRuntime 存储模型（只加索引）。
- 不做 SelectionRuntime / PlayerOwner 兼容层或 fallback 读取。
- 不实现 ParticipantView Mode enum、不在 Core 出现 genre 分支。
- 不在本 Epic 实现联机传输层；只保证确定性契约（M10）。
- 不为裁判实现 gameplay order 路径。
- 不在本 Epic 开放 Presenter 执行侧封闭 enum 的 mod 注册（`PresentationEventKind` / `PresenterCommandKind` / `BehaviorKind` / `AssetKind` / `InlineConditionKind`）：已核查不阻塞本 Epic UAT（DEC-12），列为已知边界，若后续需要另立 RFC。
- ~~不做同进程多 viewport 渲染~~ → **已修订（#896）**：ClientLocalSeat + LogicView + PresentBinding 为一等公民；分屏是 PresentBinding.rect，逻辑视觉与画面解耦。裁判独立 client 仍有效，但不再禁止同进程多 Seat 呈现。

## 10. 与现有 Issue/PR 的关系

- #536/#537/#538 的子单迁移至本 Epic 对应 Phase；原 Epic 关闭前在正文回链。
- #522 剩余尾巴（PR #535 body P1~P5）并入 Phase 1 / Phase 7 清单，消除双轨追踪。
- PR #535 与 PR #577 二选一为 canonical（PRE-3），另一方 cherry-pick 后关闭。
