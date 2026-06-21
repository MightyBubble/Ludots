# EPIC：多势力生产体系 Web-UI Showcase 套件（红警 / 星际 / 帝国 / 4X）

> 状态：Draft（供 Codex 执行）
> 基线分支：`codex/showcase-web-ui-epic-v2`（对齐 `main` @ `289387b4c`）
> 工作区：`C:/001_AI/LudotsProd_showcase_epic`
> 关联基建：存档（Save）、Utility AI、Entity Association Core（AAC）、Web 适配层、Progression、Exchange/Relationship

---

## 0. 目标与范围

### 0.1 一句话目标

基于 `main` 已合入的 **存档 / AI / Entity Association / Web 数据通道 / Progression / Exchange** 六大基建，重做生产与训练 showcase：交付 **四个参考流派 Mod**，UI 全部用新的 Web UI 渲染，且每个 Mod 都能切换不同势力视角，演示该流派完整的生产体系与外交贸易。

### 0.2 四个 Mod（参考流派 → 生产范式）

| Mod | 参考流派 | 势力（可切换 view） | 主打生产范式 | 必须演示的能力 |
|-----|----------|----------------------|---------------|----------------|
| **M1. RedAlertLike**（红警 like） | C&C / Red Alert | Allied / Soviet | **直接建造**（建造场旁直接放置）+ MCV **部署** | 直接建造、部署建造、训练、科技解锁、外交、贸易 |
| **M2. StarCraftLike**（星际 like） | StarCraft II | Terran / Protoss / Zerg | **工人建造**(Terran SCV) + **Warp/部署建造**(Protoss) + **产卵/孵化建造**(Zerg morph) | 三族三种建造范式、训练、Warp Gate 科技、外交、贸易 |
| **M3. EmpireLike**（帝国 like） | Age of Empires | 两个文明（如 Romans / Han） | **工人(村民)建造** + 建筑训练 + **时代/科技树升级** | 工人建造、训练、多层科技树（时代升级）、外交、贸易 |
| **M4. FourXLike**（4X like） | Civ / Stellaris | 3 个帝国 | **城市/排产建造** + 科技树 | 城市排产、科技树、**重外交协议 + 贸易互换**（核心） |

> **重要约束**：每个 Mod 都要支持「切换势力 view」并在该 view 下体验对应生产范式；四个 Mod 合起来覆盖全部生产范式（直接建造 / 部署建造 / 工人建造 / 产卵孵化 / 城市排产）+ 训练 + 科技树 + 外交 + 贸易。UI 风格要**对齐参考项目**（C&C 底部指挥栏、SC2 命令卡 + 生产队列、AoE 资源条 + 村民面板、4X 帝国控制台 + 外交/贸易面板）。

### 0.3 强制工程铁律（来自仓库规范）

- **数据驱动 / SSOT**：生产、科技、外交、贸易行为全部走配置（GAS abilities/effects、Progression scopes/progressions/requirements、Exchange operations、Relationships catalog、Map JSON）。**禁止隐式硬编码**。
- **六边形架构 + 一切皆 Mod**：复用逻辑沉淀到 capability mod；showcase root mod 只做场景入口 + 产品化配置 + 极薄 glue。
- **禁止 fallback / 向后兼容 / 重复造轮子 / 跨越职责**。
- **加载期 fail-fast**：配置错误必须在加载期暴露，不允许运行时静默降级。
- 写任何代码前先读 `gitbook/contributing/ai-assisted-development.md` 的「任务执行决策规范」，以及 `gitbook/contributing/coding-standards.md`。

---

## 1. 现状盘点（main 基线能力 + 关键路径）

> 下列结论基于对 `C:/001_AI/LudotsProd_showcase_epic`（= main）的全面分析。**这是 Codex 的事实基线，开发前必须复核对应文件。**

### 1.1 现有 RTS 生产 / 训练 showcase（待重做）

- **唯一 gameplay SSOT**：`mods/RtsDemoMod/`（全部 GAS + Systems + Runtime + `rts_entry` 地图）。
  - 生产范式已在 GAS 数据里实现：
    - 工人建造（War3）：`Ability.Rts.Strategy.War3.BuildLumberMill` 等 + `State.Rts.BuilderAttached` + `RtsRelationRuntimeSystem`（`ChildOf` 附着）。
    - 直接建造（C&C）：`Ability.Rts.Strategy.Cnc.PlacePowerPlant`，瞬时 `CreateUnit` + 短 `State.Rts.Constructing`。
    - 训练：`TagClip` → `Status.Rts.Training` + 定时 `CreateUnit`（C&C 还有分步扣费 `TrainRhino`）。
    - SC2 科技：`ResearchWarpGate` 授予**遗留 tag** `Progression.Rts.WarpGate` + `ability_form_sets.json` 切换 slot。
    - Warp 部署：`WarpZealot` + `State.Rts.Warping`；Zerg 孵化：`MorphSpawningPool` + `State.Rts.MorphConsumed`；驻军：`Relation` effect + `ChildOf`。
  - 关键文件：
    - `mods/RtsDemoMod/assets/GAS/abilities.json`、`effects.json`、`ability_form_sets.json`、`tag_rules.json`
    - `mods/RtsDemoMod/assets/Entities/templates.json`、`assets/Maps/rts_entry.json`
    - `mods/RtsDemoMod/Systems/RtsRelationRuntimeSystem.cs`、`RtsSelectionCommandPanelSystem.cs`
    - `mods/RtsDemoMod/Runtime/RtsQuickSelectToolbarProvider.cs`
- **薄入口 mod**（仅 log + 地图）：`mods/showcases/rts_training_{cnc,sc2,war3}/`、`mods/RtsShowcaseMod/`（themed 地图）。
- **当前 UI**：仅 Skia overlay（`mods/EntityCommandPanelMod/`，`CommandDeck` + `OrderMonitor`），**没有任何 Web UI**。
- **HUD 参考原型**：`mods/showcases/ux_prototype/UxPrototypeMod/`（高保真 RTS HUD 布局 + 外交/贸易 mock tab，但未接 GAS）。
- **能力标准（capability standard）SSOT**：`gitbook/architecture/capability-standard-showcases.md`（RTS 生产目前**没有** capability-standard root mod，是缺口）。
- **验收测试**：`src/Tests/GasTests/Production/RtsStrategicShowcaseAcceptanceTests.cs`、`RtsTrainingShowcaseAcceptanceTests.cs`。

### 1.2 Web UI 基建（服务端权威，浏览器哑渲染）

- 浏览器端：`src/Client/Web/`（TypeScript + Three.js + Vite，无 React/Vue）。三层画布：WebGL 世界、`#hud-canvas`、`#ui-canvas`（`UiSceneOverlay` 渲染服务端下发的 `UiScene` JSON）。
- 服务端管线：`GameEngine.Tick()` → 各 presentation buffer + `UIRoot` 挂 `UiScene` → `PresentationExtractor.CaptureFrame()` → `BinaryFrameEncoder` → `WebTransportLayer` 广播 → 浏览器 `FrameDecoder`。
  - 关键路径：`src/Adapters/Web/Ludots.Adapter.Web/{WebGameHost,WebHostComposer,WebHostLoop}.cs`、`Streaming/PresentationExtractor.cs`、`src/Apps/Web/Ludots.App.Web/Program.cs`。
  - UI JSON 契约：`src/Libraries/Ludots.UI/Runtime/Serialization/UiSceneDiffJsonSerializer.cs`（目前只有 `FullSnapshot`，无增量）。
  - 输入回传：`src/Client/Web/src/input/InputEncoder.ts` ↔ `InputProtocol.cs`（仅原始指针/键盘；**UI 点击在服务端 `UIRoot.HandleInput` 命中并 `UiDispatcher` 派发**）。
- **Mod 贡献 UI 的方式（Raylib / Web 通用）**：`*PanelController : ReactivePage<TState>` + `UIRoot.MountScene` / `UiShowcaseMounting.MountScene`，`Ui.Button(...).OnClick(ctx => ...)` 服务端处理。
- 关键缺口（**Phase A 要解决**）：
  - `UIRoot` 只持有**单一** `UiScene`，多 mod/多面板会 last-writer-wins。RFC-0055（`docs/rfcs/RFC-0055-ui-surface-ownership-and-showcase-takeover.md`）的 `IUiSurfaceLeaseService` **尚未实现**。
  - `DeltaCompressor` 已存在但未接入（每帧全量 UI/primitive）。
  - `src/Client/Web/src/core/FrameProtocol.ts` 与 C# section id **不一致**（`FrameDecoder.ts` 才是对的，`FrameProtocol.ts` 是 stale）。
  - 没有 Web 端 E2E 验收（仅 `src/Tests/ThreeCTests/WebTextProtocolTests.cs`）。
  - `imageSource` 在 web 端按字符串路径加载，mod 资产 → URL 映射未定义。
  - Web 适配层 `src/Apps/Web/Ludots.App.Web/game.json` 当前只加载 `Navigation2DPlaygroundMod`，**没有任何 RTS 入口**。

### 1.3 Entity Association Core（AAC）

- 三套机制（**不要混用**）：
  1. 类型化关系边 `RelationshipRuntime`（`src/Core/Gameplay/Relationships/`），内建类型 `Owns`；`assets/Configs/Relationships/catalog.json` + mod overlay 注册更多类型（如 `Diplomacy`、`Participant`）。
  2. ECS 父子 `ChildOf` + `ChildrenBuffer`（`src/Core/Gameplay/GAS/`），经 `RelationOps.SetParent`；用于驻军 / 建造附着 / morph。
  3. 命名集合 `EntityCollectionStore`（`src/Core/EntityCollections/`）+ 查询 `EntitySetQueryRuntime`（`src/Core/EntityQueries/`）+ 作用域 `ScopeKey`/`ScopeResolver`（`src/Core/Association/`）+ 知识投影 `KnowledgeProjection*`（`src/Core/Knowledge/`）。
- 所有权专用解析：`src/Core/Association/OwnershipResolver.cs`（`EnsureOwnership/TryResolveRootOwner/CollectOwned`，基于 `Owns` 边）。
- 势力 / 玩家身份：`Team{Id}`、`PlayerOwner{PlayerId}`、`PlayerIdentity`/`TeamIdentity`（map 作者从 `MapConfig.Players`/`Teams` 解析）。
- **Participant View（势力视角切换）**：
  - 能力 mod：`mods/capabilities/participant_view/ParticipantViewCapabilityMod/`（`ParticipantViewCapabilityRuntime.SelectPlayer/SelectTeam`，`ParticipantViewProjection`）。
  - capability-standard：`mods/showcases/capability_standard/CapabilityStandardParticipantViewsMod/`（含 `participantViewKnowledge` 地图段，知识/迷雾投影）。
  - 切换 view = 改 **selection context + knowledge 可见性**，**不改模拟所有权**（控制仍走 `PlayerOwner` + 本地玩家绑定）。
- 参考 showcase：`mods/showcases/ownership_cascade/`、`mods/showcases/fourx_association/`、`mods/showcases/association_stress/`、`mods/showcases/entity_query_tactics/`、`mods/showcases/team_research/`。
- 缺口：`RtsDemoMod` **没用 AAC 服务**（只用 `ChildOf` + `copySourcePlayerOwner` 复制 `PlayerOwner` 组件，无 `Owns` 边、无 collection 发布、无 participant view）。无 `GarrisonedIn` 关系类型。

### 1.4 Progression / 科技树

- 新基建（实体作用域）：`src/Core/Gameplay/Progression/`。**没有** `technologies.json` / `TechTree` 类型；科技 = `Progression/scopes.json` + `progressions.json` + `requirements.json`。
  - 解锁状态存在 **scope host 实体** 的 `ProgressionStateBuffer`（64 槽，level + revision）。
  - 组件：`ProgressionScopeHost`、`ProgressionScopeBinding`、`AbilityProgressionRequirements`。
  - 需求节点种类：`All/Any/Not/ProgressionCompleted/ProgressionLevelAtLeast/EntityCount/TagAll/GraphValidation`。
- **GAS 门控**：ability 上的 `useRequirement`（可见但 blocked）与 `showRequirement`（隐藏）。授予解锁用 `CompleteProgression` effect（`BuiltinHandlers.HandleCompleteProgression`）。
- 参考 showcase：实体作用域 `mods/showcases/progression_scope/ProgressionScopeShowcaseMod/`；团队共享 `mods/showcases/team_research/TeamResearchShowcaseMod/`（`EntityCollection` 成员 + `evaluator.TryComplete(teamHost, id)`）。
- 缺口：tech-tree 编译器（`trees.json`）只设计未实现；core 不存「研究进度」（只存已完成 level，进度在 mod runtime）；`RtsDemoMod` 的 Warp Gate 仍用遗留 tag，**未迁移到 progression**；无 Web 端结构化 tech 状态查询接口（只能靠渲染好的面板）。

### 1.5 Exchange / Relationship（外交 + 贸易）

- Exchange core：`src/Core/Gameplay/Exchange/`（`ExchangeRuntime.TryExecute(ExchangeOperationKey, ExchangeExecutionContext)`，原子结算 + 回滚 + GAS effect 输出）。
  - 输入：`ItemStack`（库存消耗）/`AttributeCost`（`AttributeBuffer` 扣费）。输出：`CreateItem`/`MoveItem`/`EffectRequest`。
  - **外交即数据门控**：operation 的 `relationshipRequirements`（type/metric/min/flag）在消耗前校验，失败 → `RelationshipDenied`。
  - 动态报价：`ExchangeScopedOperationStore.Set((operationId, scopeKey), def)`（已在 core/tests 验证，showcase 未用）。
- Relationship：`RelationshipRuntime`（metric 如 `Trust`，flag 如 `Embargo`/`TradePact`/`AtWar`，band 阈值自动置 flag，callback/synergy）。`assets/Configs/Relationships/catalog.json` + mod overlay。
- 资源模型：GAS 属性（`AttributeBuffer` + `AttributeRegistry`，如 Gold/Minerals/Lumber/Gas）或 item stack（`InventoryRuntimeService`）。Exchange 用**实体角色槽**（Source/Target/Context），不直接用 team id。
- 参考 showcase：`mods/showcases/diplomacy_trade_gate/`（外交门控贸易，键盘驱动 + 只读面板）、`mods/showcases/gold_market/`（属性扣费市场）、`mods/showcases/item_system/`（**有 `Ui.Button` 点击贸易**，最佳 web UI 参考）、`mods/showcases/fourx_association/`（外交+属性贸易+所有权链路）、`mods/FourXDemoMod/`（diplomacy catalog + GAS graphs 贸易伙伴查询）。
- ADR：`docs/adr/ADR-0003-exchange-operation-scope-key.md`（禁止造 Merchant/Trade core runtime）。
- 缺口：core **无 offer/accept 握手协议**（`TryExecute` 是即时结算）→ 接受/拒绝需 **mod 层状态机** 再调 Exchange；外交 showcase 多为键盘+只读（需改 `BuildActionButton` 模式）；无「切换势力 → 改 Exchange Source/Target」端到端 wiring；存档 `relationships` domain 是占位。

### 1.6 Save（存档）

- Core 基建（非 Mod）：`src/Core/Persistence/`。外部 `.ldsave` = `header.json` + `domains.json` + `world.bin`（Arch.Persistence 二进制 ECS 快照）。
  - 服务：`WorldSnapshotService`/`WorldRestoreService`/`SaveSlotStore`/`SaveContainerCodec`/`SaveContextValidator`，端口 `ISaveStorage`（`src/Platform/Ludots.Platform.Abstractions/`）。
  - 恢复 fail-fast：schema/modSetHash/registryFingerprint 不匹配直接拒绝。
  - 落地状态：ECS world（含 GAS/Progression/`RelationshipEdgeSet`/inventory 组件）+ `domains.json`（`teams`/`gameSession`/`clock`/`timeFlow`/`mapSessions`/`narrative`）。
  - Mod 接入：ECS 组件自动；非 ECS 状态实现 `ISaveParticipant` 注册到 `SaveParticipantRegistry`。
- SSOT：`gitbook/architecture/save-system.md`；RFC：`docs/rfcs/RFC-0060-universal-save-system.md`；UAT：`src/Tests/PersistenceTests/SaveSystemUatTests.cs`（已用 `rts_cnc_training`）。
- 缺口：**无存档 UI**；**无玩家文件系统 `ISaveStorage` 实现**（测试用内存）；`KnowledgeProjectionStore`（迷雾/视野知识）**不入存档**；`inventory`/`relationships` domain 占位未接。

### 1.7 AI（Utility AI + Autocast）

- Core：`src/Core/Gameplay/AI/`，`AiCompiledRuntime`。**Utility AI 已接主循环**（`UtilityAiThinkScheduleSystem`/`UtilityAiDecisionSystem`）；GOAP/HTN 系统存在但**未接主循环**（仅 tests/benchmark）。
  - 数据驱动：`AI/{profiles,decision_makers,decisions,target_filters,inputs,normalizations,curves,tasks,stances,actuators}.json`。
  - 决策 → `UtilityAiTaskKind.SubmitOrder` 推 `OrderQueue`（`OrderTypeId` + 可选 `AbilityId`/`PlayerId`）。AI 只产 order 意图，不写 GAS 副作用。
  - 普攻 = autocast；共享 GCD/法力/槽位竞争走 `SharedCooldownTagId` + `AbilityActivationBlockTags` 仲裁。
- SSOT：`gitbook/architecture/ai-utility-autocast-contract.md`；RFC：`docs/rfcs/RFC-0060-ai-utility-autocast-contract.md`；调试：`mods/AIInspectorMod/`（`UtilityAiDecisionTrace`）。
- 缺口：**没有任何 mod 出货 build/train/research/diplomacy 的 AI 配置**（只有 test fixture）；无「势力级 AI 大脑」编排经济；macro 行为需 mod 自定义 order type + 执行层（或 mod 自行接 GOAP/HTN 系统）。

---

## 2. 架构决策（SSOT 约定，开发前必须遵守）

1. **所有权 SSOT**：被生产单位/建筑必须通过 `OwnershipResolver.EnsureOwnership(playerRep, entity)` 建立 `Owns` 边（经济/继承/UI「我的单位」查询走 `Owns`）；`PlayerOwner` 仅用于输入路由（保留）。**禁止**仅用 `copySourcePlayerOwner` 当所有权真相。
2. **势力视角 SSOT**：势力切换统一用 `ParticipantViewCapabilityMod`，**不得**重写 selection 替换逻辑。地图作者声明 `MapConfig.Players/Teams/ParticipantRelationships` + tag `capability.participant_view`，迷雾/情报用 `metadata.participantViewKnowledge`。
3. **科技树 SSOT**：用 `Progression/{scopes,progressions,requirements}.json` 表达，**禁止**新 `technologies.json` 或 tag-based 解锁。`RtsDemoMod` 的 Warp Gate 必须迁移到 progression（`useRequirement`/`showRequirement` + `CompleteProgression`）。多层科技树（帝国「时代」）用 `ProgressionLevelAtLeast` 或链式 `ProgressionCompleted`。
4. **外交/贸易 SSOT**：外交 = `RelationshipRuntime`（metric/flag/band）；贸易结算 = `ExchangeRuntime`；动态报价 = `ExchangeScopedOperationStore`；offer/accept 握手 = **mod 层状态机**（提议→pending→接受/拒绝→accept 时调 `TryExecute`）。**禁止**新建 Merchant/Trade/Treaty core runtime（ADR-0003）。
5. **UI SSOT**：所有 showcase UI 用统一 C# UI runtime（`ReactivePage`/Compose/Markup）产出 `UiScene`，由 Web 适配层自动渲染。**浏览器不是 UI 作者层**，不要写浏览器侧 DOM/业务逻辑。所有交互按钮走 `Ui.Button(...).OnClick` → mod runtime API。
6. **复用沉淀**：可复用 RTS 生产/HUD 逻辑沉淀到 capability mod；四个 showcase root mod 只放场景入口 + 产品化配置 + 极薄 glue（对齐 `capability-standard-showcases.md`）。
7. **launcher SSOT**：每个 Mod / 每个势力 view 都要在 `launcher.config.json`（binding）+ `launcher.presets.json`（raylib + web preset）登记。
8. **加载期 fail-fast**：所有新配置经 ConfigPipeline 校验；引用不存在的 ability/effect/progression/exchange/relationship id 必须加载期报错。

---

## 3. 工作分解（Phases / Sub-Issues）

> 总体顺序：**Phase A 基建补齐 → Phase B 共享 RTS capability → Phase C 四个 Mod → Phase D 跨切面（AI/Save/Launcher）→ Phase E 验收**。每个 sub-issue 标注交付物与验收点，可独立 PR。

### Phase A — Web UI 基建补齐（阻塞项）

**A1. UI Surface 组合 / 租约（落地 RFC-0055 最小集）**
- 交付：`IUiSurfaceLeaseService`（或等价「showcase shell composer」），支持多 mod/多面板子树合并到单一 `UIRoot.Scene`，含 owner/lease/restore。
- 路径：`src/Libraries/Ludots.UI/`（core 能力）+ 复核 `docs/rfcs/RFC-0055-*.md`。
- 验收：两个面板（生产 + 外交）能同时挂载互不覆盖；切换 showcase 能 restore。
- 备选（若 RFC-0055 工作量过大）：交付一个 `RtsHudShellMod`（capability）作为唯一 `UIRoot` owner，向其注册命名区域（top-bar / command-deck / side-panel / modal），其它 mod 往区域投递子树。**二选一，在 PR 描述说明取舍。**

**A2. Web 协议修正 + 资产 URL 映射**
- 修正 stale `src/Client/Web/src/core/FrameProtocol.ts` 与 C# section id 对齐（或删除并统一到 `FrameDecoder.ts` 来源）。
- 定义 mod `imageSource`（图标/单位头像）→ web 静态 URL 的映射（服务端 `dist`/静态托管约定），供生产卡/科技图标使用。
- 验收：web 端能正确渲染带图标的命令卡。

**A3.（可选 / 性能）增量帧接入**
- 将 `DeltaCompressor` 接入 `PresentationExtractor`，`UiSceneDiffKind` 扩展到增量。
- 仅当 Phase C 出现带宽/帧率问题时做；否则降级为「记录为已知 backlog」。

### Phase B — 共享 RTS 生产 / HUD capability mod

**B1. `RtsProductionCapabilityMod`（核心复用层）**
- 从 `RtsDemoMod` 抽出可复用生产/建造/训练/驻军 runtime（`RtsRelationRuntimeSystem` 思路），但接入 AAC：
  - 生产完成时 `OwnershipResolver.EnsureOwnership(playerRep, unit)` + 建筑 `EnsureOwnership(building, unit)`（可选级联）。
  - 发布「我的单位/建筑」到 `EntityCollectionStore`（供 UI 查询，key 如 `faction.<id>.units`/`.buildings`/`.production_queue`）。
- 提供生产范式的**数据契约**（GAS preset 复用），使四个 Mod 用配置表达：直接建造 / 部署建造 / 工人建造 / 产卵孵化 / 城市排产。
- 验收：单测覆盖 5 种范式各自的「下单 → 状态 → 产出 → 所有权 + collection 发布」。

**B2. `RtsHudWebMod`（或 B1 内含）—— Web HUD 组件库**
- 用 `ReactivePage` 实现可复用 HUD 组件：资源条、命令卡（command deck）、生产队列（带进度/分步扣费可视化）、选中单位信息、minimap 占位、势力切换器、科技树面板、外交面板、贸易面板、存档读写面板。
- 全部 `Ui.Button(...).OnClick` → 通过既有 domain 服务派发（GAS 施法、Exchange、Progression、Save、ParticipantView）。**参考 `mods/showcases/item_system/.../ItemSystemShowcasePanelController.cs` 的 `BuildActionButton` 与 `mods/showcases/ux_prototype/` 的布局。**
- 数据来源：`EntityCollectionStore` + `EntitySetQueryRuntime`（单位/建筑/队列）、`ProgressionStateBuffer`+`ProgressionRequirementEvaluator`（科技）、`RelationshipRuntime`（外交）、`SaveSlotStore.ListSlots()`（存档）。
- 验收：组件可被四个 Mod 复用，UI 在 Web 适配层渲染并可交互。

**B3. 势力切换接线**
- 集成 `ParticipantViewCapabilityMod`：HUD 势力切换器调用 `SelectPlayer/SelectTeam`；切换后命令卡/生产队列/科技/外交面板按当前势力 collection 重绑。
- 验收：切换势力后 UI 显示该势力的单位、可用生产/科技、外交关系。

**B4. Tech 树 → Web 查询投影（补 1.4 缺口）**
- 提供一个 mod 服务/只读投影：把某 scope host 的 `ProgressionStateBuffer` + 各 tech 的 `requirements` 评估结果（已解锁/可研究/锁定/前置）投影成结构化数据，供 B2 科技树面板渲染（节点 + 连线 + 状态）。
- 验收：科技树面板正确显示依赖关系与可研究状态，点击「研究」触发 `CompleteProgression` 链路。

**B5. 外交/贸易 offer-accept 状态机（补 1.5 缺口）**
- mod 层状态机：提议（A→B 一笔贸易/条约）→ pending → B 接受/拒绝。接受时用 `ExchangeScopedOperationStore.Set` 构造动态 operation 再 `TryExecute`；条约用 `RelationshipRuntime.SetMetric/SetFlag`。
- 验收：两势力间能完成「提议贸易→接受→资源转移」「签订/撕毁协定→ metric/flag 变化→贸易门控生效」全流程，UI 按钮驱动。

### Phase C — 四个 Showcase Root Mod

> 每个 root mod：`mod.json` + `<Mod>.csproj` + `<Mod>ModEntry.cs`（薄）+ `assets/`（`game.json`、`Maps/`、`GAS/`、`Entities/`、`Progression/`、`Exchange/`、`Relationships/`、`Presentation/`、`Configs/Camera/`）。依赖 `LudotsCoreMod`/`CoreInputMod`/`RtsProductionCapabilityMod`/`RtsHudWebMod`/`ParticipantViewCapabilityMod`。放在 `mods/showcases/<flavor>/<ModName>/`。

**C1. M1 `RedAlertLikeShowcaseMod`**
- 势力：Allied / Soviet（`MapConfig.Players` 两个玩家 + team）。
- 生产：直接建造（建造场 power-down 进度后放置）+ MCV 部署成建造场（部署建造）+ 兵营/战车工厂训练 + 分步扣费（沿用 C&C `TrainRhino` 模式）。
- 科技：雷达→进阶兵种解锁（progression `useRequirement`）。
- 外交/贸易：Allied↔Soviet 停火/结盟协定 + 矿石/资金贸易。
- UI：C&C 风格底部指挥栏 + 右侧建造列表 + 顶部资源。
- 验收：见 §4。

**C2. M2 `StarCraftLikeShowcaseMod`**
- 势力：Terran / Protoss / Zerg（三玩家）。
- 生产：Terran 工人(SCV)建造；Protoss Warp/部署建造（`WarpZealot`/`State.Rts.Warping`）；Zerg 产卵孵化（`MorphSpawningPool`/`State.Rts.MorphConsumed`，drone 消耗）。
- 科技：Warp Gate 研究 **迁移到 progression**（替换遗留 tag），解锁后 Gateway→WarpGate 形态切换（保留 `ability_form_sets.json` 或迁移）。
- 外交/贸易：三族间资源（Minerals/Gas）贸易 + 停战。
- UI：SC2 风格命令卡（3x5 grid）+ 生产/孵化队列 + 补给/资源。
- 验收：三族三范式全部可玩 + 切换 view。

**C3. M3 `EmpireLikeShowcaseMod`**
- 势力：两个文明。
- 生产：村民(worker)建造多种建筑 + 建筑训练单位 + **时代/科技树升级**（多层 progression：Age I→II→III，逐级解锁建筑/单位/升级，用 `ProgressionLevelAtLeast` 或链式 `ProgressionCompleted`）。
- 资源：多资源（食物/木/金/石，用 `AttributeBuffer`）。
- 外交/贸易：文明间贡品/贸易 + 结盟。
- UI：AoE 风格——顶部多资源条 + 村民/建筑面板 + 科技树（时代树）。
- 验收：村民建造 + 训练 + 至少 2 级时代升级解锁链 + 外交贸易。

**C4. M4 `FourXLikeShowcaseMod`**（外交/贸易为核心）
- 势力：3 个帝国。
- 生产：城市排产建造（队列）+ 科技树（深，team/faction scope）。
- 外交：**重点**——多边关系（Trust/AtWar/TradePact/Embargo），offer/accept 协定（停火/结盟/通商），违约/禁运联动贸易门控。
- 贸易：资源互换 + 战略资源贸易路线（`EffectRequest` 输出周期性 buff，参考 `FourXDemoMod` GAS graphs）。
- 复用 `mods/showcases/fourx_association/` 的链路（fog→diplomacy→exchange→progression→ownership）。
- UI：4X 帝国控制台——城市/排产、科技树、**外交面板（关系矩阵 + 提议/接受/拒绝按钮）**、**贸易面板（报价构造 + 互换）**。
- 验收：3 帝国多边外交 + 贸易 offer/accept 全流程 + 科技树 + 城市排产。

### Phase D — 跨切面接线

**D1. AI 势力（数据驱动 macro AI）**
- 为每个 Mod 至少一个 AI 势力出货 `AI/{profiles,decisions,tasks,target_filters,...}.json`：
  - order types（mod `GAS/order_types.json`）：`buildStructure`/`trainUnit`/`startResearch`/`executeExchange`/`attackTarget`/`moveTo`。
  - Utility decisions → `SubmitOrder`；执行层由 B1 capability 的 order runtime / GAS graph 落地（建造队列、progression pulse、`ExchangeRuntime.TryExecute`）。
  - 「势力级 AI 大脑」：可用一个 HQ 实体挂 `UtilityAiAgent` 编排经济，或 mod 自接 GOAP/HTN 系统（若用 GOAP/HTN，需在 mod 内注册对应 planning system，说明取舍）。
- 验收：AI 势力能自动建造/训练/研究，并可被人类玩家切 view 观战；AI 决策可经 `AIInspectorMod` trace。

**D2. Save/Load UI + 平台存储**
- 实现玩家文件系统 `ISaveStorage`（平台层），HUD 存档面板（B2）调用 `SaveSlotStore` 列表/读/写。
- 若生产队列等有非 ECS 状态，实现 `ISaveParticipant`；否则确保状态在 ECS。
- （可选）把 `KnowledgeProjectionStore` 纳入存档（迷雾 view 持久化），或记录为 backlog。
- 验收：四个 Mod 任一可「存档→改局→读档→确定性续跑」（参考 `SaveSystemUatTests` 扩展到新地图）。

**D3. Launcher 接线**
- `launcher.config.json` 增加 4 个 binding（`$rts_redalert_like` 等）+ 每势力 view 的子 binding/preset（或运行时切换）。
- `launcher.presets.json` 增加 raylib + web preset。
- Web 适配层 `src/Apps/Web/Ludots.App.Web/game.json` 增加 RTS 入口（或经 launcher graph）。
- 验收：`scripts/run-mod-launcher.cmd cli launch '$rts_starcraft_like' --adapter web` 能起服务并在浏览器可玩。

### Phase E — 文档 + 验收

**E1. capability-standard 登记**：把四个 Mod 按 `gitbook/architecture/capability-standard-showcases.md` 规范登记为 capability-standard root（adapter/core 验收 SSOT）。
**E2. UAT 矩阵**：更新 `gitbook/architecture/uat-playable-showcase-matrix.md`。
**E3. 验收测试**：每个 Mod 一套 NUnit 验收（headless engine + 模拟输入 + UI 文本断言 + `artifacts/acceptance/` PNG），覆盖 §4 清单。
**E4. RFC 回写**：若 A1 落地 RFC-0055，回写结论到 gitbook。

---

## 4. 全局验收标准（每个 Mod 必须满足）

对每个 Mod（M1–M4），验收清单：

1. **势力切换**：能在 ≥2 个势力 view 间切换；切换后 UI（命令卡/队列/科技/外交）反映当前势力，单位可见性/选择按 participant view 正确。
2. **生产范式**：该 Mod 主打范式可玩，且全套覆盖（M1 直接+部署；M2 工人+Warp+孵化；M3 工人+时代；M4 城市排产），生产单位归属正确（`Owns` 边 + collection）。
3. **训练**：从建筑训练单位，队列/进度/扣费在 Web UI 正确显示（含 C&C 分步扣费可视化）。
4. **科技树**：数据驱动 progression，UI 显示依赖+状态，研究后解锁对应生产/单位（`useRequirement`/`showRequirement` 生效）。
5. **外交协议**：势力间能签订/撕毁协定（metric/flag 变化），UI 按钮驱动；M4 含 offer/accept 握手。
6. **贸易互换**：势力间资源/物品互换经 `ExchangeRuntime`，外交门控生效（`RelationshipDenied`），UI 按钮驱动。
7. **全 Web UI**：所有上述交互在 `src/Apps/Web` + `src/Client/Web` 下可见可交互，无遗留 Skia-only 路径作为唯一入口。
8. **存档**：可存读档并确定性续跑（至少一个 Mod 端到端，其余 smoke）。
9. **AI 势力**：至少一个 AI 势力自动运转生产/科技（至少一个 Mod 端到端）。
10. **launcher**：raylib + web preset 均可启动。
11. **数据驱动 / fail-fast**：无隐式硬编码；坏配置加载期报错。

---

## 5. 关键风险与取舍（开发前确认）

| 风险 | 说明 | 建议处理 |
|------|------|----------|
| **单 `UIRoot` Scene** | 多面板组合阻塞（A1） | 优先 RFC-0055 lease；过重则用 `RtsHudShellMod` 单 owner + 命名区域 |
| **PlayerOwner vs Owns 双所有权** | 查询真相不一致 | 按 §2.1：`Owns` 为经济/UI 真相，`PlayerOwner` 仅输入路由 |
| **WarpGate 遗留 tag** | 与 progression SSOT 冲突 | M2 必须迁移到 progression，删除 tag 路径 |
| **Exchange 无握手** | 4X 外交需要 accept/reject | mod 层状态机 + `ExchangeScopedOperationStore`（§B5） |
| **无玩家存储/存档 UI** | 存档不可产品化 | D2 实现平台 `ISaveStorage` + HUD 面板 |
| **AI 仅 combat** | macro 无出货配置 | D1 出货 build/train/research AI 配置；GOAP/HTN 如需则 mod 自接 |
| **知识/迷雾不入档** | 读档后 view 状态丢失 | D2 评估纳入存档或记 backlog |
| **资源 SSOT 混用** | credits 既是 item 又是 attribute | 每种资源每个 Mod 选一种表示并文档化 |
| **Web 带宽/帧率** | 全量帧 | A3 增量帧（按需） |
| **UI「对齐参考项目」主观** | 验收尺度模糊 | 以 `ux_prototype` 布局 + 参考游戏截图为基准，PNG 验收存档 |

---

## 6. 关键文件 / 服务速查（Codex 开发索引）

**生产/showcase 基线**
- `mods/RtsDemoMod/assets/GAS/{abilities,effects,ability_form_sets,tag_rules}.json`
- `mods/RtsDemoMod/assets/Entities/templates.json`、`assets/Maps/rts_entry.json`
- `mods/RtsDemoMod/Systems/RtsRelationRuntimeSystem.cs`、`Runtime/RtsQuickSelectToolbarProvider.cs`
- `mods/showcases/ux_prototype/UxPrototypeMod/`（HUD 布局参考）
- `mods/showcases/item_system/ItemSystemShowcaseMod/UI/ItemSystemShowcasePanelController.cs`（按钮模式）

**Web UI**
- `src/Client/Web/`、`src/Apps/Web/Ludots.App.Web/`、`src/Adapters/Web/Ludots.Adapter.Web/`
- `src/Libraries/Ludots.UI/`（`UIRoot`、`ReactivePage`、`UiSceneDiffJsonSerializer`）
- `docs/rfcs/RFC-0055-ui-surface-ownership-and-showcase-takeover.md`

**AAC / 势力**
- `src/Core/Association/{OwnershipResolver,ScopeKey}.cs`、`src/Core/EntityCollections/`、`src/Core/EntityQueries/`、`src/Core/Knowledge/`
- `mods/capabilities/participant_view/ParticipantViewCapabilityMod/`
- `mods/showcases/{ownership_cascade,fourx_association,team_research}/`

**Progression**
- `src/Core/Gameplay/Progression/`、`assets/Configs/Progression/`
- `mods/showcases/progression_scope/`、`mods/showcases/team_research/`
- `docs/reference/entity_scoped_progression_author_guide.html`

**Exchange / Relationship**
- `src/Core/Gameplay/Exchange/`、`src/Core/Gameplay/Relationships/`
- `assets/Configs/Relationships/catalog.json`
- `mods/showcases/{diplomacy_trade_gate,gold_market}/`、`mods/FourXDemoMod/`
- `docs/adr/ADR-0003-exchange-operation-scope-key.md`、`docs/architecture/exchange_architecture.md`

**Save**
- `src/Core/Persistence/`、`src/Platform/Ludots.Platform.Abstractions/ISaveStorage.cs`
- `gitbook/architecture/save-system.md`、`src/Tests/PersistenceTests/SaveSystemUatTests.cs`

**AI**
- `src/Core/Gameplay/AI/`、`mods/AIInspectorMod/`
- `gitbook/architecture/ai-utility-autocast-contract.md`

**Launcher**
- `launcher.config.json`、`launcher.presets.json`、`scripts/run-mod-launcher.cmd`
- `gitbook/architecture/capability-standard-showcases.md`、`gitbook/reference/cli-runbook.md`

---

## 7. 建议的 PR 切分（每个 = 一个可独立 review 的 sub-issue）

1. A1（UI surface 组合/租约）
2. A2（Web 协议修正 + 资产 URL）
3. B1（`RtsProductionCapabilityMod` + AAC 接入）
4. B2（`RtsHudWebMod` Web HUD 组件库）
5. B3（势力切换接线）+ B4（tech 树 Web 投影）
6. B5（外交/贸易 offer-accept 状态机）
7. C1（RedAlertLike）
8. C2（StarCraftLike，含 WarpGate→progression 迁移）
9. C3（EmpireLike，含多层时代科技树）
10. C4（FourXLike，外交/贸易核心）
11. D1（AI 势力配置）
12. D2（Save UI + 平台存储）
13. D3（Launcher 接线）
14. E1–E4（capability-standard 登记 + UAT 矩阵 + 验收测试 + RFC 回写）

> A3（增量帧）为可选，按需插入。
