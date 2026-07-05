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
9. Performer 无法表达 provenance（我的单位 vs 代理的队友单位 vs 裁判视角的多玩家集合）。

### 1.2 愿景管线（单向，共 8 段）

```text
[L1 Device]      原始设备输入（键鼠/手柄/触屏）
[L2 Remap]       IMC 输入映射上下文（已有 PlayerInputHandler 压栈）→ InputAction
[L3 Intent+Ctx]  InteractionContextStack：栈顶 frame 决定 activeCollectionKey/activeViewKey/inputContextId
[L4 Cast]        InputCastSpec（box/polygon/ray/lasso × screen/world/minimap）→ raw hits collection
[L5 Filter]      FilterProfile（graph/condition DSL，association query）→ filtered
[L6 Collection]  CollectionWrite → 按所属域路由到 (domainRepEntity, activeKey)，row 记 writerDomain
[L7 View/Panel]  EntityView profile + PanelRouter + AggregationProfile → HUD/面板投影
[L8 CastFlow]    CastFlowProfile（idle→charging→aiming→committed 状态机）+ ClientCastPreference
[L9 Dispatch]    CastDispatchProfile（selector/scorer/router）→ per-actor Order（shared order id）
[L10 Order]      OrderQueue（唯一 intake）→ OrderBuffer → AbilityExec / MassNav ingestion
[L∥ Performer]   只读 collection revision + provenance + catalog → marker/相位（本地/队友/裁判）
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
| CastFlowProfile | 多段施法输入状态机（press/release/confirm/cancel 迁移），取代 `InteractionModeType` 捆绑 | 拟新增 |
| ClientCastPreference | 玩家施法偏好，scope 链 global → template → formset → slot | 拟新增 |
| CastDispatchProfile | 复选施法的 selector（谁施法）/ scorer（排序，复用 UtilityAI）/ router（并发/顺序） | 拟新增 |
| Order | 唯一执行入口 payload；`OrderQueue` 是唯一 intake | 已有 |
| Performer marker rules | `PerformerRule`（event + condition + command）+ palette catalog 表达 marker 样式；viewer 语义由 graph 拓扑谓词现算 | `PerformerDefinitionRegistry` + `performers.json`（已有；condition 上下文需扩，见 DEC-12） |
| KnowledgeProjection | 裁判/观战 visibility grant | `KnowledgeProjectionStore`（已有） |

---

## 3. 铁律（合并 + 新增）

1. **OrderQueue 唯一 intake**：MassNav / AI / input / evidence 不得旁路 `SubmitOrder`。
2. **MassNav 只消费 OrderBuffer**：零 Input / Selection 读取。
3. **Selection 概念退役**：「selection」只是 default context 下 `collection.command.source` 的俗名；`SelectionRuntime` 不得作为 hub；Order payload 不得引用 selection 容器实体，须自包含目标集或引用 `(owner, collectionKey, revision)`。
4. **Embodied entity 零 `PlayerOwner` / `Team` / `PlayerIdentity`**：归属只存在于 relationship 边。
5. **控制平面只走 `ControlDomainQuery`**；阵营/敌我判定只走 `DomainStanceQuery`（缓存投影，relationship revision 失效；stance key 是 catalog 数据，Core 无 "hostile" 字面语义）。
6. **代理控制只增删 `controls` 边**：不迁移 collection、不改 `owns`、不写 unit 组件。
7. **Collection namespace per playerRep entity**：禁止 cross-player merge，禁止 PlayerId 全局表。
8. **Context Stack 只路由 key，不存实体列表**；frame 按 ownerToken 移除，不依赖裸 LIFO。
9. **InputCast 与 Filter、Commit、Presentation 正交**：禁止再往 `InteractionModeType` 加值；新施法手感 = 新 CastFlowProfile 数据。
10. **Performer 只读**：collection revision + provenance + catalog；不写 collection、不改 association。
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
- 多控制者并发写同一域：row 携带 `writerDomain` 追踪，写入按 authoritative input 顺序定序（确定性不受影响）。
- **路由策略是 collection profile 的声明字段**，不是全局行为：控制平面语义的 key（如 `collection.command.source`）声明 `writeRouting: byControlDomain`；技能目标语义的 key（如 `collection.ability.*.targets`，选的是目标而非"我维护的域"，可能包含敌方单位）声明 `writeRouting: toContextOwner`，写入 context frame 的 owner 域。两种都是数据，Core 不猜语义。

### DEC-5 Provenance 由地址承载，viewer 语义拓扑现算

DEC-4 的域路由让 RFC-0064 方案 A 的写时快照大幅简化，且陈旧问题自然消失：

- `controlDomain` 不再是写入的 row metadata——**它就是 collection 地址本身**（row 住在哪个 rep 的域里）。
- `relationKind`（owns / controls / spectate）是 **viewer 相对语义**，由 Performer / View 求值时按「viewer anchor → row 所在域」的实时拓扑现算，不写死在行里。队友重连的瞬间，浅绿 marker 判定条件（controls 边）不复存在，视图重算即消失——不存在「写时快照过期」问题。
- 写时仅保留 `writerDomain`（谁维护了这行）用于审计与并发定序。
- relationship revision 变更 → ControlPlaneView / Performer 订阅重算；禁止陈旧帧跨越一个 maintenance 周期以上。

### DEC-6 Context frame 带 ownerToken，支持并发 exec

frame 字段含 `ownerToken`（ability exec 实例 entity / system token）。移除按 token，不按栈顶；实体死亡 / exec abort 由 lifecycle 钩子强制回收其全部 frame。多个并发 PendingConfirm exec 各持 frame，`activeCollectionKey` 取「最后激活」的 frame，Tab 循环可在并发 frame 间切换（数据驱动，见 P6）。

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

- `CastFlowProfile.transitions[].actions[]` 的动作（beginChargeTimer / showIndicator / writeChargeToPayload / submit…）是 **castflow op registry** 注册项（复用 graph op handler 模式），mod 可注册新 op；示例里的动词只是内置 op 的 id，不是 Core enum。
- `CastDispatchProfile` 的 `selector.kind` / `scorer.kind` / `router.kind` 同理为 registry 注册项；`advanceOn` 是事件 key（registry id），不是硬编码字符串分支。
- `FilterProfile` / `AssociationControlProfile` 的谓词复用现有 condition/graph DSL，不新造谓词 enum。

护栏：M9 增加断言「新增 profile schema 中零 Core-only 封闭 enum 分派」。

### DEC-12 Performer 基建核对结论与本 Epic 所需的扩展点

对现有 Performer 基建的核查结论：**概念上已是 event → condition → command → behavior 四层**（`PresentationEvent(+Kind/KeyId)` → `ConditionRef(inline|graph)` → `PerformerCommand` → `BehaviorSlot`），规则声明（`performers.json` + `PerformerDefinitionRegistry`）、command 的 scope/valueSource 映射、param binding 数据化程度足够；且 `EntityCollectionMemberAdded/Removed` 事件已携带 collection KeyId / owner / member / roleId / revision / scope hash，**本 Epic 无需新增事件种类**。但四层的执行侧存在封闭点，本 Epic 依赖其中四处，必须随 PROV 阶段修复：

| # | 现状硬点（代码事实） | 本 Epic 需要 | 落点 |
|---|---------------------|--------------|------|
| 1 | graph condition 求值只注入 `E[0]=Source, E[1]=Target`，无 viewer、无 event payload 寄存器（`PerformerRuleSystem.EvaluateGraph`） | 拓扑谓词条件（viewer 是否为 row 域本人 / controls 可达 / 仅 knowledge grant）需要 **viewer 实体寄存器 + relationship/knowledge graph ops + payload 寄存器** | PROV-4b |
| 2 | `PerformerDefinition.VisibilityCondition` 的 graphProgramId 在 Emit 侧未接线（`PerformerEmitSystem` throw） | per-viewer 可见性（裁判 vs 普通玩家）依赖它 | PROV-4c |
| 3 | `TeamColorResolver` 硬编码 Team1/Team2 色并直读 `Team`/`PlayerOwner`；`PerformAudienceContext` 直读 `Team`/`PlayerOwner` 组件 | palette/相位改 palette catalog + graph 取值（§5.9）；audience 上下文改拓扑求值 | PROV-6b（并入 CTRL-3b 消费者清单） |
| 4 | `InlineConditionKind.SourceIsLocalPlayer` 写死 GlobalContext LocalPlayerEntity；WorldHud audience 只取单一 local viewer | 多 viewer（裁判为独立 client anchor 时可行；同进程多 viewport 明确列为非目标） | PROV-5 备注 |

**明确不在本 Epic 修的封闭点**（记录为已知边界，避免范围膨胀）：`PresentationEventKind` / `PerformerCommandKind` / `BehaviorKind` / `AssetKind` 封闭 enum、`InlineConditionKind` 不可 mod 注册（复杂条件一律走 graph）、event payload 固定槽。这些不阻塞本 Epic 的全部 UAT。

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
  "performerProfileId": "performer.ability.superweapon.target_marker"
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

### 5.5 CastFlowProfile（多段施法状态机）

```json
{
  "id": "castflow.charge_release",
  "states": ["idle", "charging", "committed"],
  "transitions": [
    { "from": "idle", "on": "press", "to": "charging", "actions": ["beginChargeTimer", "showIndicator"] },
    { "from": "charging", "on": "release", "to": "committed", "actions": ["writeChargeToPayload", "submit"] },
    { "from": "charging", "on": "cancel", "to": "idle", "actions": ["hideIndicator"] }
  ],
  "payloadRules": { "spatial": "cursorWorld", "f0": "chargeSeconds" }
}
```

`castflow.quick`（press 即 submit）、`castflow.aim_confirm`（press 进瞄准、confirm 提交）、`castflow.two_stage`（加里奥 W 式两段）同为数据；`InteractionModeType` 的六个值全部退役为等价 profile。

`actions[]` 里的动词（`beginChargeTimer` / `showIndicator` / `submit`…）是 **castflow op registry 注册项 id**（复用 graph op handler 模式，DEC-11），mod 可注册新 op——不是 Core 封闭 enum。`payloadRules` 的取值源同理为 registry 注册的 value source。

### 5.6 ClientCastPreference（scope 链）

```json
{
  "global": { "castFlowId": "castflow.quick" },
  "perTemplate": { "champion.xerath": { "castFlowId": "castflow.aim_confirm" } },
  "perFormSet": { "champion_skill_sandbox_jayce_forms/hammer": {} },
  "perSlot": { "champion.xerath/2": { "castFlowId": "castflow.quick_with_indicator" } }
}
```

解析优先级：perSlot > perFormSet > perTemplate > global；mod 可声明某 slot `lockedCastFlowId` 禁止玩家覆盖。

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

### 5.9 Performer selection marker rules（对齐现有 PerformerRule 结构）

现有基建事实：`PerformerRule = EventFilter(kind+key) + ConditionRef(inline | graphProgramId) + PerformerCommand`；`EntityCollectionPresentationEventSystem` 发布的 `EntityCollectionMemberAdded/Removed` 事件 **已携带** collection KeyId、owner（Target）、成员 entity（Source）、roleId、revision（FloatD）、scope hash（PayloadA）——RFC-0064「复用现有事件」成立，marker 规则直接落在这套结构上：

```json
{
  "rules": [
    {
      "event": { "kind": "EntityCollectionMemberAdded", "key": "collection.command.source" },
      "condition": { "graphProgramId": "graph.cond.viewer_is_row_domain" },
      "command": { "kind": "CreatePerformer", "definitionId": "selection.marker",
                    "scopeSource": "EventPayloadA", "ownerSource": "EventSource",
                    "paramKey": "marker.tint", "paramGraphProgramId": "graph.palette.self_deep" }
    },
    {
      "event": { "kind": "EntityCollectionMemberAdded", "key": "collection.command.source" },
      "condition": { "graphProgramId": "graph.cond.viewer_controls_row_domain" },
      "command": { "kind": "CreatePerformer", "definitionId": "selection.marker",
                    "scopeSource": "EventPayloadA", "ownerSource": "EventSource",
                    "paramKey": "marker.tint", "paramGraphProgramId": "graph.palette.self_light" }
    },
    {
      "event": { "kind": "EntityCollectionMemberAdded", "key": "collection.command.source" },
      "condition": { "graphProgramId": "graph.cond.viewer_has_knowledge_grant" },
      "command": { "kind": "CreatePerformer", "definitionId": "selection.marker",
                    "scopeSource": "EventPayloadA", "ownerSource": "EventSource",
                    "paramKey": "marker.tint", "paramGraphProgramId": "graph.palette.team_phase" }
    },
    {
      "event": { "kind": "EntityCollectionMemberRemoved", "key": "collection.command.source" },
      "command": { "kind": "DestroyScopedPerformer", "scopeSource": "EventPayloadA" }
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
    When 玩家1 激活 superweapon 且 exec 进入 PendingConfirm
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
    Given performer catalog §5.9 已加载
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
Feature: M7 CastFlow 数据化（多段施法零 Core switch）
  Scenario Outline: 同一 ability 换 castflow 只改数据
    Given ability "charge_cannon" 绑定 <castflow>
    When 玩家执行 <inputs>
    Then 产生 <payload> 的 Order 且全程无 InteractionModeType 分支参与

    Examples:
      | castflow              | inputs            | payload                     |
      | castflow.quick        | press             | spatial=cursorWorld          |
      | castflow.charge_release | press,hold 1.2s,release | f0=1.2, spatial=cursorWorld |
      | castflow.aim_confirm  | press,click(confirm) | spatial=confirmPoint      |
      | castflow.two_stage    | press,press       | 阶段2 payload（两段各自提交） |

  Scenario: 新增施法手感不改 Core
    When mod 注册新的 castflow.triple_tap profile
    Then 无需修改 src/Core/Input/**（ArchitectureTests 冻结该目录的 casting 分支逻辑）
```

```gherkin
Feature: M8 复选施法 Dispatch 策略
  Background:
    Given 玩家1 框选 5 个追猎（均有 blink，slot 一致）
    And 目标点 T 已由 castflow 解析

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
      | Performer 规则零 PlayerOwner 读取 |
      | Core 零 "rts"/"moba" 字面量分支 |
      | association/collection 基建零 "offline"/"mind_control"/"cinematic" 等业务场景字面量 |
      | 零 collection 跨域 copy/move API（不存在"归还"代码路径） |
      | DomainStanceQuery 零 "hostile"/"enemy" 字面分支（stance key 全部来自 catalog） |
      | 本 Epic 新增 profile schema 零 Core-only 封闭 enum 分派（castflow op / dispatch kind / groupBy 均 registry 或表达式） |
      | Performer 规则零 viewerRole 业务角色枚举（viewer 语义全部拓扑谓词现算） |
      | InteractionModeType 类型已删除或仅存于迁移 shim 白名单 |
```

```gherkin
Feature: M10 确定性与回放
  # 确定性边界：Order 流是 sim 的唯一入口。
  # 纯视图偏好（聚合/marker/palette）不参与 order 生成；施法偏好（castflow/dispatch）
  # 参与"raw input → order"的翻译，因此回放 SSOT 是 order 流（或 input+当时偏好快照），
  # 不是裸设备输入。

  Scenario: 纯视图偏好不影响 sim
    Given 两个 client 消费同一份已录制的 order 流
    When 其中一个 client 把 aggregation preference 换成 by_template、marker palette 换成高对比配色
    Then 两端 sim 世界哈希完全一致（视图偏好只影响本地投影）

  Scenario: 施法偏好参与翻译，但翻译产物自包含
    Given 玩家以 castflow.quick + dispatch.utility_nearest 录制一局
    When 以录制的 order 流回放
    Then sim 逐 tick 一致，且回放不需要读取任何 preference / context stack / 本地容器实体
    And 每条 Order payload 自包含目标集（或 (owner, collectionKey, revision) 引用）

  Scenario: 偏好变更即时生效但不追溯
    Given 对局中玩家把 cast preference 从 aim_confirm 改为 quick
    Then 已提交的 order 不受影响，仅后续 raw input 的翻译改变
```

### 6.2 Persona B — 玩家（我改了偏好，应得到什么）

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
| CTX-7b | 明确产物 = CastFlowProfile registry + loader（§5.5），`InteractionModeType` 退役映射表 |
| CTX-8b | ClientCastPreference scope 链 schema（§5.6）+ mod lock 语义 |

### Phase 3 — Control Plane（继承 CTRL，修订）

CTRL-1..CTRL-10 按原文；修订：

| ID | 修订 |
|----|------|
| CTRL-1b | `controls` 为查询期视图（DEC-1） |
| CTRL-3b | 依赖 PRE-2；列全消费者迁移清单（GAS targeting / TeamColorResolver / PerformPhaseResolver / lifecycle snapshot / #499 publisher） |
| CTRL-4b | AssociationControlProfile = 通用「谓词 → 边增删」规则引擎，复用 condition DSL，schema 零业务词汇（DEC-4；无 handback/policy 字段） |
| CTRL-4c | CollectionWrite 域路由：写入按被指挥单位所属域落到对应 rep，row 记 writerDomain（DEC-4） |
| CTRL-4d | ControlPlaneView：EntityView domainScope 扩展，controls 可达域组合只读视图；Order fan-out / HUD / PanelRouter 改消费该视图（DEC-4，衔接 ORD-4） |

### Phase 4 — Provenance & Performer（继承 PROV，修订）

PROV-1..PROV-8 按原文；修订：

| ID | 修订 |
|----|------|
| PROV-1b | provenance 简化：controlDomain 由 collection 地址承载，写时仅存 writerDomain；relationKind 不入行（DEC-5） |
| PROV-2b | relationship revision → ControlPlaneView / Performer 重算钩子；viewer 相对语义拓扑现算（DEC-5，取代"失效/驱逐"方案） |
| PROV-4b | Performer graph condition 求值上下文扩展：注入 viewer 实体寄存器 + event payload 寄存器 + relationship/knowledge 拓扑谓词 graph ops（DEC-12 #1；现状只有 E[0]=Source, E[1]=Target） |
| PROV-4c | 接线 `PerformerDefinition.VisibilityCondition` 的 graphProgramId Emit 路径（DEC-12 #2；现状 `PerformerEmitSystem` 直接 throw） |
| PROV-6b | 退役 `TeamColorResolver` 硬编码 Team1/Team2 色与 `PerformAudienceContext` 的 `Team`/`PlayerOwner` 直读，改 palette catalog + 拓扑求值（DEC-12 #3，并入 CTRL-3b 消费者清单） |

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
| DSP-3 | 挂点：command collection → fan-out 之间；shared order id 语义保持 |
| DSP-4 | cycle/sequential 状态（advanceOn orderAccepted） |
| DSP-5 | 玩家 dispatch 偏好（mod 可锁） |

### Phase 7 — Showcase & 护栏 & 文档（SHOW/GUARD/DOC）

| ID | 内容 |
|----|------|
| SHOW-1 | [showcase] M2 超级武器 context（含 IMC 切换） |
| SHOW-2 | [showcase] M3+M4+P5 代理控制拓扑投影 + marker + 边消失涌现"归还"（掉线与心控两种 trigger 各演示一遍，证明 trigger 无关性） |
| SHOW-3 | [showcase] M5+P8 裁判多控制域投影 |
| SHOW-4 | [showcase] M6+P3 面板聚合三案例切换 |
| SHOW-5 | [showcase] M8+P4 追猎 blink 三种 dispatch |
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
    → Phase 5 (PNL) →─┐
    → Phase 6 (DSP) →─┴→ Phase 7 SHOW-4/5
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
- 不在本 Epic 开放 Performer 执行侧封闭 enum 的 mod 注册（`PresentationEventKind` / `PerformerCommandKind` / `BehaviorKind` / `AssetKind` / `InlineConditionKind`）：已核查不阻塞本 Epic UAT（DEC-12），列为已知边界，若后续需要另立 RFC。
- 不做同进程多 viewport 渲染（裁判作为独立 client anchor 已满足 M5/P8）。

## 10. 与现有 Issue/PR 的关系

- #536/#537/#538 的子单迁移至本 Epic 对应 Phase；原 Epic 关闭前在正文回链。
- #522 剩余尾巴（PR #535 body P1~P5）并入 Phase 1 / Phase 7 清单，消除双轨追踪。
- PR #535 与 PR #577 二选一为 canonical（PRE-3），另一方 cherry-pick 后关闭。
