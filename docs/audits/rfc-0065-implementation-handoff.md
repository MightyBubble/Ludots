> Historical handoff. Older SelectionRuntime-live statements describe an older audit
> snapshot and are superseded by the current closeout. Formal Selection APIs are retired;
> EntityCollectionStore / `collection.command.source` is the authority without fallback.
# RFC-0065 实现交接与审计提示（Audit Agent Handoff）

> 本文是给后续审计/实现 agent 的提示词正本。SSOT：`docs/rfcs/RFC-0065-unified-interaction-collection-casting-architecture.md`（15 项 DEC + M1~M12/P1~P9 UAT）。
> 实现分支：`cursor/epic-unified-interaction-casting-114d`（PR #581），基线 main=40f73f6fa。
> 本文最初记录的是无头验证状态；2026-07-07 复核时当前环境已能跑 Raylib/CEF framebuffer。最新可见证据见 `artifacts/rfc0065-visible-uat/visible-uat-summary.md`：A1 使用 CEF framebuffer，A2 使用 WebUI/CEF War3-style command panel final frames，A3/A4 使用最终 timeline PNG；旧 `001`/`002` 以及被打回的中间帧仅保留为历史 smoke。完整 RFC §6 Gherkin-mapped recordings 仍是后续可见 UAT 尾巴。

## 一、已完成清单（按提交序）

## 2026-07-07 Closeout Addendum

This addendum supersedes the older static-frame wording in this handoff.

- Latest PR581 head checked: `2417820e9ed225aff3761737f861f234094985d5`; latest submitted GitHub reviews are empty. Remote checks on that head still show docs-governance/validate and solution-verify/verify failures, while camera-baseline passes.
- A2 / SHOW-4 final visible evidence is the WebUI/CEF War3-style bottom command panel `artifacts/rfc0065-visible-uat/entity-command-panel/a2_webui_war3_final9_001_f0045.png`, `a2_webui_war3_final9_002_f0135.png`, `a2_webui_war3_final9_003_f0225.png`: Template -> Family -> Ability with the same Arcweaver/Vanguard/Commander command-source heroes. The accepted Gherkin review directly covers the command grid: 3 hero sheets x 8 commands = 24 tiles, 8 family tiles with x3 owners, and owner-qualified repeated ability labels.
- A3 / SHOW-1 final visible evidence is `artifacts/rfc0065-visible-uat/superweapon-context/a3_superweapon_context_final_001_f0020.png`, `a3_superweapon_context_final_002_f0090.png`, `a3_superweapon_context_final_003_f0180.png`: targeting pending -> confirmed -> targeting restored.
- A4 / SHOW-5/6 final visible evidence is `artifacts/rfc0065-visible-uat/interaction/a4_blink_mixed_final_001_f0045.png`, `a4_blink_mixed_final_002_f0135.png`, `a4_blink_mixed_final_003_f0225.png`: readable All Together / One By One / Nearest Top-N blink routing over the same mixed command group. Boundary: this is UI timeline evidence, not animated in-world displacement.
- A4 runtime was fixed so the interaction showcase startup projects live actors to `collection.command.source`; the visible-UAT hover/scheme timeline is env-gated and does not change normal player startup.
- Focused acceptance now includes A2=2 tests, A3=2 tests, A4=3 tests. The full RFC-0065 closeout filter passed 67/67 after these changes.
- Formal Selection APIs are retired repo-wide in the current closeout pass. Command authority uses `EntityCollectionStore`, `collection.command.source`, `EntityCollectionContextRuntime`, and command-source acquisition without fallback.
- Subagent cross-check was run for the final visible evidence: A3/A4 were accepted on the first pass; A2 was rejected for unclear player-facing evidence, then rewritten and rechecked as `accept`. A separate Selection audit confirmed the old dual-track transition; the current closeout supersedes that audit with formal Selection retirement.

| Commit 主题 | 内容 | 关键类型 | 测试 |
|---|---|---|---|
| PRE-1 + CTX-1/2 | 关系反向邻接索引（CollectIncoming O(入度)，全表扫描删除）；InteractionContextStack（token 归属 frame、RemoveByContextEntity、default frame） | `RelationshipReverseIndex`、`InteractionContextStack` | RelationshipReverseIndexTests、InteractionContextStackTests |
| PRE-2 + CTRL-1 | DomainStanceQuery（stance 全 catalog 数据、(A,B) 缓存按拓扑 revision 失效）；ControlDomainQuery（controls = owns ∪ grant 查询期视图、TryResolveControlDomain 带缓存）；catalog 增 Controls/MemberOf/Ally + stance 段 | `DomainStanceQuery`、`ControlDomainQuery` | ControlDomainQueryTests、DomainStanceQueryTests |
| CTRL-4c/4d | 域路由写入（writerDomain 第五列、重路由清残留、零跨域迁移 API）；ControlPlaneView 组合只读视图 + 聚合 revision | `DomainRoutedCollectionWriter`、`ControlPlaneView` | DomainRoutedCollectionTests |
| CTRL-2 + CTRL-4b + CTX-4/5 + SHOW-2 切片 | map load / runtime spawn 建 Owns/MemberOf 边；AssociationControlProfile 谓词→边引擎（Granted flag、revision+tag 门控、schema 零场景词汇）；FilterProfile + ContextBoundCollectionWriter；`control_plane_projection` showcase（M3/M4 无头 acceptance + WebUI DataPlane 契约层） | `OwnershipEdgeBuilder`、`AssociationControlProfileRuntime`、`FilterProfileRegistry`、`ContextBoundCollectionWriter`、`ControlPlaneProjectionShowcaseMod` | AssociationControlProfileTests、FilterProfileTests、ContextBoundCollectionWriteTests、ControlPlaneProjectionShowcaseAcceptanceTests、ControlPlaneProjectionDataPlaneTests |
| INT-1/2/3 内核 | CommandIntentProfile（谓词 SoA lower、priority 全序 fail-fast、胜出即终局、语义 slot 白名单禁裸 index）；AbilityDefinition.CatalogTags | `CommandIntentProfileRegistry` | CommandIntentProfileTests |
| GUARD | 6 条 M9 架构护栏（业务字面量禁令/零跨域迁移 API/零施法 FSM/slot 白名单/唯一边变更入口/无扫描回归） | `Rfc0065InteractionCastingBoundaryContractTests` | 同名 |
| INT-2/4 + DSP + PNL-1/2/3 | KnowledgeCommandTargetGate（fog 目标降级 ground、ContextScored 候选过滤）；CastDispatchProfile（all/topN/cycle × parallel/sequential、distanceToTarget consideration）；AbilityAggregationProfile（groupBy 取值路径表达式） | `KnowledgeCommandTargetGate`、`CastDispatchProfileRegistry`、`AbilityAggregationProfileRegistry` | CastDispatchProfileTests、AbilityAggregationProfileTests |
| CTX-6/7 + PROV-4b/2b + PNL-4 | exec 生命周期 push/pop context frame（组件对账，覆盖 abort/死亡）；CastCommitProfile op registry（pushFrame/popFrame/submitOrder，states/transitions 键加载拒绝）；Presenter graph 条件注入 E[2]=Viewer + payload + 8 个拓扑谓词 op（§5.9 own/proxy/grant 三分支可数据表达、revoke 即翻转）；面板聚合迁移（旧合并键删除） | `AbilityExecInteractionContextSystem`、`CastCommitProfileRegistry`、graph ops 397/410-412/420-422、`CollectionGasEntityCommandPanelSource` | AbilityExecInteractionContextTests、CastCommitProfileTests、PresenterTopologyConditionGraphTests、CollectionGasEntityCommandPanelAggregationTests |
| CTX-8 + INT-5/6/7 | ClientCastPreference scope 链（perSlot>perFormSet>perTemplate>global + mod 锁 + 持久化）；ControlScheme（IMC 组合热切换 + allowedSchemes）；CommandIntentArbiter（frame > scheme default > 不冒泡）；AxisMoveOrderSystem（轴→节流 moveTo order，禁直写位置，启用与参数=scheme 的 `axisMove` 声明，热切换即声明切换） | `ClientCastPreferenceStore`、`ControlSchemeRuntime`、`CommandIntentArbiter`、`AxisMoveOrderSystem` | ClientCastPreferenceTests、ControlSchemeRuntimeTests、AxisMoveOrderSystemTests |

新增数据档（加载 fail-fast）：`Configs/Relationships/control_profiles.json` + catalog stance 段、`Configs/Input/{filter_profiles, command_intent_profiles, cast_dispatch_profiles, interaction_context_profiles, cast_commit_profiles, control_schemes, cast_commit_locks}.json`（DeepObject 合并）、`Configs/UI/ability_aggregation_profiles.json`（ArrayById 合并，mod fragment 可增量追加 profile）。

## 二、审计时请重点核查

1. **铁律合规**：`Rfc0065InteractionCastingBoundaryContractTests` 6 条护栏是初步面；请对照 RFC §3 铁律 1~16 与 §6.1 M9 全表，补齐未覆盖断言（如 order payload 自包含、Presenter 只读）。
2. **0 alloc 声明**：各内核有两窗口取 min 的稳态零分配测试；本 VM 存在 24B GC 测量波动（多个基线测试同病）——审计时在稳定环境复测。
3. **并发子代理产物的接缝**：GameEngine 接线块（约 L957/L1190-1300）是多任务合流点，审查服务构造顺序依赖（aggregation 必须在 abilities.json 后、commandIntent 在 orderTypes 后）。
4. **已知基线失败**（非本分支引入，均经 stash/worktree 基线对照确认）：`AbilityExecLoaderFailFastTests`（14 个）、`OrderTypeConfigLoader_*`（5 个）、SkiaSharp 缺 so、零分配波动类。建议单独开 issue 追踪。
5. **控制组/Selection 双轨（历史）**：这一条记录的是旧审计快照。当前 closeout 已退役 formal Selection APIs；showcase 域路由和 command authority 使用 `EntityCollectionStore` / `collection.command.source`，不得再从 formal selection 桥接或 fallback。
6. **多写者共享单例语义（DEC-4 附则，非隔离）已有 UAT 固定**：多控制者写同一 `(domain, key)` 是共享的域指挥状态，后写覆盖、`writerDomain` 只记录最后维护者——这是产品语义，不是并发缺陷；并行私有选择走不同 context 的不同 collection key（正交出口）。RFC §6.1 M3 有对应 Scenario，`DomainRoutedCollectionTests.ConcurrentControllers_SameDomainSameKey_IsSharedSingletonWithLastWriteWins` 固定该行为。审计时不要把后写覆盖当成需要 writer 隔离的 bug 报告。
7. **DomainStanceQuery 与 TeamManager 桥接一致性**：双写点在 `ParticipantBindingResolver.ResolveRelationships`（map attitude → TeamManager 矩阵 + teamRep→teamRep stance 边，同一循环）；任何绕过该入口直改 TeamManager 或直建 stance 边的代码都会造成双轨分叉。一致性由 `DomainStanceBridgeAcceptanceTests`（引擎级，全参与者对遍历）与 `ParticipantBindingContractTests` 的桥接用例守护；attitude↔stance 命名对齐是数据约定（enum 成员名 = catalog stance 名），不存在代码映射表。

### 二.5 语义侵入复审结论（"control" 层）

- **判定**：control plane（谁可指挥谁）与 knowledge plane 同级，是基建平面而非业务语义——场景（掉线/心控/演出/转移）全走 tag+profile 数据，Core 零改动，检验通过。
- **已修**：`GameEngine` 曾 fail-fast 解析 `"Ally"` 但零消费者（强迫无结盟概念的游戏注册该类型）——已删除，场景类型的失败点移回引用它们的 profile 加载处。
- **已声明的 Core 策略边界**（RFC DEC-1 已知边界小节）：① owns⊆controls 并集无法表达"控制抑制"（魅惑/恐惧原主失控），闭合路径 = suppression 谓词进并集；② controls 不传递（链式指挥用数据直连边表达）；③ 保留名契约仅 `Owns`/`Controls`/`MemberOf` 三个，改名需求走 catalog roles 间接层。审计时请核查没有消费方私自绕这三条边界。
- **资产层语义侵入修复（H-1/M-1/M-2/M-3 下放清单）**：① `Ally` 类型与 `profile.control.ally_offline_proxy` 规则从 Core 默认档下放到 `ControlPlaneProjectionShowcaseMod/assets/Relationships/{catalog,control_profiles}.json` fragment（合并机制沿用 combat_stance 先例）；Core `Configs/Relationships/control_profiles.json` 收敛为结构性空默认 `{ "profiles": [] }`。② Core `Configs/Input/filter_profiles.json` 默认档 `exclude.anyTags` 清空——`state.dead`/`presentation.hidden` 是场景 tag，由需要的 mod fragment / 测试数据自带。③ `aggregation.by_family`（castFamily 词汇）下放到 `EntityCommandPanelMod/assets/Configs/UI/ability_aggregation_profiles.json` fragment；Core 默认档只留 by_template/by_ability_id；该档合并策略从 DeepObject 改为 ArrayById（DeepObject 对数组是整体覆盖，mod fragment 无法增量追加）。④ 全局 `Configs/Input/axis_move.json` 已删除——轴移动启用与参数并入 `control_schemes.json` 的 per-scheme `axisMove` 声明（单一真相，声明时四字段全必填 fail-fast，`orderTypeKey` 与 `actionId` 均在 install 时校验），`scheme.default` 不声明（默认无轴移动）；`interaction_showcase` 另声明 `scheme.wasd_move` 用于 SHOW-6 hot-switch/WASD 证据。
- **stance 三词（Hostile/Friendly/Neutral）为桥接期保留词汇**：随 TeamManager 桥（§三 CTRL-3 尾巴）退役一并下放；资产层扫描护栏对 `Relationships/catalog.json` 的 stance 段显式豁免并标注了该约定。
- **资产层扫描已入护栏**：`Rfc0065InteractionCastingBoundaryContractTests.CoreDefaultConfigAssets_CarryNoScenarioVocabulary` 扫描 Core `assets/Configs/{Relationships,Input,UI}/**.json` 的场景词（源代码扫描词表基础上追加 `ally|dead`）。

## 三、未做的尾巴（按风险排序）

| 尾巴 | 原因 | 建议 |
|---|---|---|
| **CTRL-3：删除 embodied PlayerOwner/Team（breaking）** | 消费者面极大（GAS targeting/TeamColorResolver/PerformPhaseResolver/SelectionEligibility.CanAcquire/lifecycle snapshot/#499 publisher/MassNav 等），需 DomainStanceQuery 全面替换热路径后才能删。**被替代清单另含 `TeamManager`（静态 (TeamA,TeamB)→TeamRelationship enum 矩阵 + `TeamRelationshipSnapshot` 持久化）**：桥接已建立——`ParticipantBindingResolver` 在写 TeamManager 的同一循环把 map attitude 双写为 teamRep→teamRep / playerRep→teamRep stance 边（stance catalog 配置时 fail-fast 校验，未配置时跳过），SSOT 迁移到 relationship 边完成后 TeamManager 退役 | 独立 PR；先迁消费者（每个一个子单），最后删组件 + ArchitectureTests 禁令 |
| **ORD 工作流 + PR #535 vs #577 仲裁** | 两个外部 PR 25 文件重叠、closes 同批子单，需人工 triage | 人工决策 canonical 后，迁移剩余 skill/cast fan-out 与 InteractionModeType 调用面；`Command` ground path 已通过 `CommandIntentArbiter` → `RouteGroup` → `SelectDispatchTargets` → `OrderQueue` 接入 |
| **CastCommit/Intent/Dispatch 与 InputOrderMappingSystem 的接线** | `Command` ground slice 已接入 RFC-0065 intent/dispatch；skill/cast 主链仍保留旧 `InteractionModeType` 表达，退役 InteractionModeType 是 ORD/CTX-7 收尾 | 剩余接线顺序：frameActions 拦截 → CommandIntentArbiter → RouteGroup → Dispatch → OrderQueue（约定已在 XML doc） |
| **PROV-4c：VisibilityCondition graph Emit 接线** | PresenterEmitSystem 该路径现状 throw；触碰 emit 热路径，未在本轮改 | 小单独做，加 per-viewer 可见性测试 |
| **PROV-3/5/6：marker catalog JSON、referee knowledge grant showcase、team palette** | 表现层数据 + 需要可视验收 | 与 GUI showcase 一起做 |
| **INT-8：KnowledgeProjection tag/stance 事实投影（伪装）** | 新基建，M11 伪装 UAT 标 deferred | 独立 RFC 子单 |
| **INT-6 RTS 多选轴移动 dispatch** | AxisMoveOrderSystem 当前最小面只动 local rep 化身 | 接 Dispatch 后扩展 |
| **偏好/方案的 Settings UI 与 Save/Load 调用点** | 策略归后续 settings 工作 | `TrySetPreference`/`TrySwitch`/`Save/Load` API 已就绪 |
| **M10 确定性回放 acceptance（双端 hash）** | 需回放基建配合 | GUARD-2 子单 |
| **gitbook 回写（DOC-1）** | RFC 尚未 accept | RFC accept 后执行 |

## 四、待做的 Showcase（需要 raylib + Ludots CEF WebUI 的 Windows 环境）

团队常用验收场景是 **raylib + CEF WebUI dataplane**。当前环境已能产生真实 Raylib/CEF framebuffer，但本文仍把静态 framebuffer 与完整录屏分开：framebuffer 可以证明正式入口与画面可见，不能替代 RFC §6 的交互时间点对照表。

1. **SHOW-2（M3+M4+P5）代理控制拓扑投影**：`control_plane_projection_showcase`（launcher binding 已注册）。无头 acceptance 已绿；当前静态可读 CEF framebuffer `artifacts/rfc0065-visible-uat/control-plane-projection-cef/screens/004_raylib_cef_framebuffer.png` 显示 Control Plane 面板、Proxy On、owned/proxy/view 计数、Given/When/Then、entity rows 与 own/proxy rings。仍差完整 raylib+CEF 录屏：O 键 toggle → 深绿/浅绿 marker 变化 → revoke 收缩 → panel topic/command ack 时间点表。
2. **SHOW-1（M2）超级武器 context**：`SuperweaponContextShowcaseMod` 已存在，`superweapon_context_showcase` launcher binding 已注册；headless acceptance 证明 ability-owned interaction frame、target collection、confirm IMC、事件 gate 与 default-frame restore。当前最终 Raylib timeline 为 `artifacts/rfc0065-visible-uat/superweapon-context/a3_superweapon_context_final_001_f0020.png`、`a3_superweapon_context_final_002_f0090.png`、`a3_superweapon_context_final_003_f0180.png`，可见 targeting pending -> confirmed -> targeting restored。如 terminal evidence 要求视频，可另补 video recording。
3. **SHOW-3（M5+P8）裁判多控制域投影**：headless projection evidence 已完成：`artifacts/acceptance/rfc0065-referee-projection-showcase/{battle-report.md,trace.jsonl,path.mmd}`。GUI marker/palette/referee evidence 已完成：`artifacts/rfc0065-visible-uat/control-plane-projection-cef/show3_player_referee_markers2_001_f0060.png`、`show3_player_referee_markers2_002_f0160.png`、`show3_player_referee_markers2_003_f0300.png`。
4. **SHOW-4（M6+P3）面板聚合三案例切换**：`entity_command_panel_showcase` launcher binding 已注册；headless acceptance 证明 Family/Template/Ability runtime aggregation；当前最终 WebUI/CEF War3-style command panel evidence 为 `artifacts/rfc0065-visible-uat/entity-command-panel/a2_webui_war3_final9_001_f0045.png`、`a2_webui_war3_final9_002_f0135.png`、`a2_webui_war3_final9_003_f0225.png`，显示 Template -> Family -> Ability，并显式展示 active profile chip、Arcweaver/Vanguard/Commander CommandSource、3 source actors、24/8/24 profile counts 与匹配 Visible Result。旧 `timeline_*` 仅保留为 superseded smoke，不计作 final evidence；视频可作为 terminal evidence 追加。
5. **SHOW-5（M8+P4）追猎 blink 三种 dispatch**：Dispatch 内核就绪；当前 A4 headless 证明默认右键 path 的 `dispatch.all_together` fan-out 到 shared moveTo order。三种 blink dispatch playable 仍未完成。
6. **SHOW-6（M11+M12+P9）pointer intent 路由 + ControlScheme 热切换**：`interaction_showcase` launcher binding 已注册；当前最终 Raylib timeline 为 `artifacts/rfc0065-visible-uat/interaction/a4_blink_mixed_final_001_f0045.png`、`a4_blink_mixed_final_002_f0135.png`、`a4_blink_mixed_final_003_f0225.png`，可见 command actors、intent、All Together / One By One / Nearest Top-N routing、hover ignored、active CommandSource。headless acceptance 证明 production startup active `scheme.default` 与 default intent，并新增 `scheme.wasd_move` hot-switch + WASD `Move` Axis2D -> authoritative snapshot -> `AxisMoveOrderSystem` -> `OrderBuffer` 证据。边界：当前 final 是可读 UI timeline evidence，不是 in-world 3D blink displacement video。

Showcase 通用注意：单位外观用 cube/sphere + Static/InstancedStaticMesh renderPath（GpuSkinned 不进 web/primitive 流）；marker 用 GroundOverlay Ring（`entity_query_tactics` 先例）；Web 面板走 `window.ludotsDataplane`（参照 `browser_react_flow`/`browser_rts_production` 模板）。

## 五、后续工作分派（给后续实现 agent，按工作流拆单）

> 三条工作流互相独立可并行；每单自带验收标准，做完一单提交一单。

### 工作流 A：可见 UAT（Gherkin → 可运行 showcase + 录屏）

环境：Windows + raylib + Ludots CEF WebUI（`launch <binding> --adapter raylib`）。验收产物 = 录屏 + **RFC §6 对应 Gherkin scenario 逐条对照表**（scenario 文本 → 录屏时间点）。当前 framebuffer 已经通过玩家/Gherkin 静态可读复核，只作为入口/可见性证据，不替代录屏。

| 单 | 内容 | 就绪度 | 验收 Gherkin |
|---|---|---|---|
| A1 | `control_plane_projection_showcase` 可视化：① 用 PROV-4b 拓扑谓词 graph ops 写 marker presenter 规则（own=深绿 / proxy=浅绿 ring，条件样板在 `PresenterTopologyConditionGraphTests`，规则形态在 RFC §5.9）；② CEF 面板订阅 `ludots.showcase.control_plane.state` topic + `toggleProxy` command；③ 录屏：框选混编 → O 键 toggle → marker 变色 → revoke 收缩 | 无头链路全绿；launcher binding 已注册；CEF panel framebuffer 已捕获 | M3/M4/P5 |
| A2 | 面板聚合演示：EntityCommandPanel 宿主 + `SetAggregationProfile` 运行时切换（by_family fragment 在 EntityCommandPanelMod） | 内核+迁移完成；`entity_command_panel_showcase` binding 已注册；WebUI/CEF War3-style bottom command panel 已捕获并复核，截图内直接展示 active profile、CommandSource 三人、24/8/24 profile counts 和 owner-qualified splits | M6/P3 |
| A3 | 超级武器 context showcase：ability 配 `interactionContextProfile` + targeting collection + indicator presenter + IMC 切换 | showcase mod、binding、headless acceptance 已完成；Raylib pending -> published/restored timeline 已捕获并复核 | M2/P6 |
| A4 | pointer intent + dispatch + ControlScheme 演示（追猎 blink 三种 dispatch、右键语义路由、WASD⇄鼠标热切换） | default right-click production path、CommandSource startup、hover ignored、`scheme.wasd_move` hot-switch/WASD headless 完成；`interaction_showcase` binding + Raylib default/off -> WASD/enabled timeline 已完成；blink/mixed-selection visible animation 仍可后续追加 | M8/M11/M12/P4/P9 |

### 工作流 B：benchmark 固化与复验

| 单 | 内容 | 验收 |
|---|---|---|
| B1 | 把 codex review 4632829926 的 `bench.*` 探针固化为仓库内 benchmark 测试（跟随 `EntityCollectionQueryBenchmarkTests` 先例）：reverse index CopyIncoming（specific/any）、`CollectIncoming` wrapper、partial-domain projection（ns/row 上界）、`ReplaceRouted` 域数平坦性、AssociationControlProfile 单 rep 翻转预算 | 每项断言预算上界 + 0 alloc（两窗口取 min）；基线数字记入本文档 |
| B2 | 在稳定环境（非本 VM，存在 24B GC 测量波动）复跑全部 0 alloc 断言与 B1 基线 | 复测报告 + 波动项排除清单 |

### 工作流 C：迁移任务（续）

| 单 | 内容 | 验收 |
|---|---|---|
| C1 | **CTRL-3 消费者迁移**（独立 PR，逐个子单）：`TargetResolverFanOutHelper`（Team 敌我）、`SelectionEligibility.CanAcquire`、`TeamColorResolver`、`PerformPhaseResolver`/`PerformAudienceContext`、CoreInputMod `NearestEnemyInRange` resolver、MassNavigation 命令权限、`DynamicParticipantVisibilityPublisher`(#499)、lifecycle snapshot、ParticipantView projection → 全部迁 `ControlDomainQuery`/`DomainStanceQuery`；最后删 embodied `PlayerOwner`/`Team` + `TeamManager` 退役 + ArchitectureTests 禁令 | 每子单：迁移后原测试全绿 + 零组件读取（rg 断言）；终单：组件删除 + 禁令测试 |
| C2 | **输入主链路接线**：`Command` ground path 已接入 `CommandIntentArbiter` → `CommandIntentProfileRegistry.RouteGroup` → `CastDispatchProfileRegistry.SelectDispatchTargets` → OrderQueue；剩余工作是迁移 skill/cast pointer/fan-out、frameActions 拦截与 `InteractionModeType` 退役（六值→CastCommitProfile 映射表见 CTX-7b）。**前置：人工仲裁 PR #535 vs #577** | InputOrderContract/InteractionSelection 等既有测试迁移后全绿；M9 断言 InteractionModeType 删除 |
| C3 | PROV-4c（`VisibilityCondition` graph Emit 接线，现状 throw）+ PROV-3/5/6（marker catalog JSON、referee knowledge grant、team palette + 相位） | per-viewer 可见性测试 + SHOW-3 可视验收（M5/P8） |
| C4 | INT-8（KnowledgeProjection tag/stance 事实投影，伪装 UAT 解 deferred）、M10 回放 acceptance（GUARD-2）、gitbook DOC-1 回写（RFC accept 后） | 对应 UAT/文档 |

## 六、给审计 agent 的执行提示词（可直接粘贴）

```
你在 Ludots 仓库分支 cursor/epic-unified-interaction-casting-114d（PR #581）上审计 RFC-0065 的第一批实现。
先读 docs/rfcs/RFC-0065-unified-interaction-collection-casting-architecture.md（铁律 §3、DEC §4、UAT §6）
与 docs/audits/rfc-0065-implementation-handoff.md（本文）。

审计范围 = git log 中含 "RFC-0065" 的全部提交。后续实现单在本文档 §五（A/B/C 三条工作流）。逐项核查：
1) 铁律 1~16 合规（重点：零业务语义字面量、零 fallback、collection 永不跨域迁移、
   Presenter 只读、OrderQueue 唯一 intake——注意旧输入链路尚未迁移属已知尾巴，不算违规）；
2) 每个新 registry/profile 的加载 fail-fast 完整性（未知 kind/重复 id/悬空引用）；
3) 0 alloc 声明在你的环境复测（本实现 VM 有 24B GC 测量波动）；
4) GameEngine 接线块的构造顺序依赖与服务空引用风险；
5) 新 graph ops（397/410-412/420-422）的寄存器分配与既有 graph 程序兼容性；
6) handoff §三的尾巴清单是否有被实现代码隐式跨越的（例如有代码假设 CTRL-3 已完成）；
7) 测试基线失败清单（handoff §二.4）与你环境的差异。
产出：问题清单（严重度/文件/建议修法）+ 每个 M1~M12 UAT 的当前可验证状态表。
禁止：把已知尾巴当缺陷报告；把环境性失败归因于实现。
```
