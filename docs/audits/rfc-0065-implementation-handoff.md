# RFC-0065 实现交接与审计提示（Audit Agent Handoff）

> 本文是给后续审计/实现 agent 的提示词正本。SSOT：`docs/rfcs/RFC-0065-unified-interaction-collection-casting-architecture.md`（15 项 DEC + M1~M12/P1~P9 UAT）。
> 实现分支：`cursor/epic-unified-interaction-casting-114d`（PR #581），基线 main=40f73f6fa。
> 所有实现提交均为无头验证（本 VM 无 Windows CEF / GPU）；每步一个独立 commit，git log 中以 `RFC-0065` 关键词可检索。

## 一、已完成清单（按提交序）

| Commit 主题 | 内容 | 关键类型 | 测试 |
|---|---|---|---|
| PRE-1 + CTX-1/2 | 关系反向邻接索引（CollectIncoming O(入度)，全表扫描删除）；InteractionContextStack（token 归属 frame、RemoveByContextEntity、default frame） | `RelationshipReverseIndex`、`InteractionContextStack` | RelationshipReverseIndexTests、InteractionContextStackTests |
| PRE-2 + CTRL-1 | DomainStanceQuery（stance 全 catalog 数据、(A,B) 缓存按拓扑 revision 失效）；ControlDomainQuery（controls = owns ∪ grant 查询期视图、TryResolveControlDomain 带缓存）；catalog 增 Controls/MemberOf/Ally + stance 段 | `DomainStanceQuery`、`ControlDomainQuery` | ControlDomainQueryTests、DomainStanceQueryTests |
| CTRL-4c/4d | 域路由写入（writerDomain 第五列、重路由清残留、零跨域迁移 API）；ControlPlaneView 组合只读视图 + 聚合 revision | `DomainRoutedCollectionWriter`、`ControlPlaneView` | DomainRoutedCollectionTests |
| CTRL-2 + CTRL-4b + CTX-4/5 + SHOW-2 切片 | map load / runtime spawn 建 Owns/MemberOf 边；AssociationControlProfile 谓词→边引擎（Granted flag、revision+tag 门控、schema 零场景词汇）；FilterProfile + ContextBoundCollectionWriter；`control_plane_projection` showcase（M3/M4 无头 acceptance + WebUI DataPlane 契约层） | `OwnershipEdgeBuilder`、`AssociationControlProfileRuntime`、`FilterProfileRegistry`、`ContextBoundCollectionWriter`、`ControlPlaneProjectionShowcaseMod` | AssociationControlProfileTests、FilterProfileTests、ContextBoundCollectionWriteTests、ControlPlaneProjectionShowcaseAcceptanceTests、ControlPlaneProjectionDataPlaneTests |
| INT-1/2/3 内核 | CommandIntentProfile（谓词 SoA lower、priority 全序 fail-fast、胜出即终局、语义 slot 白名单禁裸 index）；AbilityDefinition.CatalogTags | `CommandIntentProfileRegistry` | CommandIntentProfileTests |
| GUARD | 6 条 M9 架构护栏（业务字面量禁令/零跨域迁移 API/零施法 FSM/slot 白名单/唯一边变更入口/无扫描回归） | `Rfc0065InteractionCastingBoundaryContractTests` | 同名 |
| INT-2/4 + DSP + PNL-1/2/3 | KnowledgeCommandTargetGate（fog 目标降级 ground、ContextScored 候选过滤）；CastDispatchProfile（all/topN/cycle × parallel/sequential、distanceToTarget consideration）；AbilityAggregationProfile（groupBy 取值路径表达式） | `KnowledgeCommandTargetGate`、`CastDispatchProfileRegistry`、`AbilityAggregationProfileRegistry` | CastDispatchProfileTests、AbilityAggregationProfileTests |
| CTX-6/7 + PROV-4b/2b + PNL-4 | exec 生命周期 push/pop context frame（组件对账，覆盖 abort/死亡）；CastCommitProfile op registry（pushFrame/popFrame/submitOrder，states/transitions 键加载拒绝）；Performer graph 条件注入 E[2]=Viewer + payload + 8 个拓扑谓词 op（§5.9 own/proxy/grant 三分支可数据表达、revoke 即翻转）；面板聚合迁移（旧合并键删除） | `AbilityExecInteractionContextSystem`、`CastCommitProfileRegistry`、graph ops 397/410-412/420-422、`CollectionGasEntityCommandPanelSource` | AbilityExecInteractionContextTests、CastCommitProfileTests、PerformerTopologyConditionGraphTests、CollectionGasEntityCommandPanelAggregationTests |
| CTX-8 + INT-5/6/7 | ClientCastPreference scope 链（perSlot>perFormSet>perTemplate>global + mod 锁 + 持久化）；ControlScheme（IMC 组合热切换 + allowedSchemes）；CommandIntentArbiter（frame > scheme default > 不冒泡）；AxisMoveOrderSystem（轴→节流 moveTo order，禁直写位置，默认关） | `ClientCastPreferenceStore`、`ControlSchemeRuntime`、`CommandIntentArbiter`、`AxisMoveOrderSystem` | ClientCastPreferenceTests、ControlSchemeRuntimeTests、AxisMoveOrderSystemTests |

新增数据档（全部 DeepObject 合并、加载 fail-fast）：`Configs/Relationships/control_profiles.json` + catalog stance 段、`Configs/Input/{filter_profiles, command_intent_profiles, cast_dispatch_profiles, interaction_context_profiles, cast_commit_profiles, control_schemes, cast_commit_locks, axis_move}.json`、`Configs/UI/ability_aggregation_profiles.json`。

## 二、审计时请重点核查

1. **铁律合规**：`Rfc0065InteractionCastingBoundaryContractTests` 6 条护栏是初步面；请对照 RFC §3 铁律 1~16 与 §6.1 M9 全表，补齐未覆盖断言（如 order payload 自包含、Performer 只读）。
2. **0 alloc 声明**：各内核有两窗口取 min 的稳态零分配测试；本 VM 存在 24B GC 测量波动（多个基线测试同病）——审计时在稳定环境复测。
3. **并发子代理产物的接缝**：GameEngine 接线块（约 L957/L1190-1300）是多任务合流点，审查服务构造顺序依赖（aggregation 必须在 abilities.json 后、commandIntent 在 orderTypes 后）。
4. **已知基线失败**（非本分支引入，均经 stash/worktree 基线对照确认）：`AbilityExecLoaderFailFastTests`（14 个）、`OrderTypeConfigLoader_*`（5 个）、SkiaSharp 缺 so、零分配波动类。建议单独开 issue 追踪。
5. **控制组/Selection 双轨**：SelectionRuntime 仍是正式框选 SSOT；showcase 的域路由由 `ControlPlaneRoutedSelectionSystem`（mod 侧）从 formal selection 桥接——ORD-5/CTX-5 的 Core 级迁移未做（见尾巴）。
6. **DomainStanceQuery 与 TeamManager 桥接一致性**：双写点在 `ParticipantBindingResolver.ResolveRelationships`（map attitude → TeamManager 矩阵 + teamRep→teamRep stance 边，同一循环）；任何绕过该入口直改 TeamManager 或直建 stance 边的代码都会造成双轨分叉。一致性由 `DomainStanceBridgeAcceptanceTests`（引擎级，全参与者对遍历）与 `ParticipantBindingContractTests` 的桥接用例守护；attitude↔stance 命名对齐是数据约定（enum 成员名 = catalog stance 名），不存在代码映射表。

## 三、未做的尾巴（按风险排序）

| 尾巴 | 原因 | 建议 |
|---|---|---|
| **CTRL-3：删除 embodied PlayerOwner/Team（breaking）** | 消费者面极大（GAS targeting/TeamColorResolver/PerformPhaseResolver/SelectionEligibility.CanAcquire/lifecycle snapshot/#499 publisher/MassNav 等），需 DomainStanceQuery 全面替换热路径后才能删。**被替代清单另含 `TeamManager`（静态 (TeamA,TeamB)→TeamRelationship enum 矩阵 + `TeamRelationshipSnapshot` 持久化）**：桥接已建立——`ParticipantBindingResolver` 在写 TeamManager 的同一循环把 map attitude 双写为 teamRep→teamRep / playerRep→teamRep stance 边（stance catalog 配置时 fail-fast 校验，未配置时跳过），SSOT 迁移到 relationship 边完成后 TeamManager 退役 | 独立 PR；先迁消费者（每个一个子单），最后删组件 + ArchitectureTests 禁令 |
| **ORD 工作流 + PR #535 vs #577 仲裁** | 两个外部 PR 25 文件重叠、closes 同批子单，需人工 triage | 人工决策 canonical 后，把 InputOrderMappingSystem 的 fan-out 迁移到 CommandIntentProfile.RouteGroup + CastDispatchProfile.SelectDispatchTargets（挂点已就绪） |
| **CastCommit/Intent/Dispatch 与 InputOrderMappingSystem 的接线** | 内核全部就绪但输入主链路仍走旧 InteractionModeType；退役 InteractionModeType 是 ORD/CTX-7 收尾 | 接线顺序：frameActions 拦截 → CommandIntentArbiter → RouteGroup → Dispatch → OrderQueue（约定已在 XML doc） |
| **PROV-4c：VisibilityCondition graph Emit 接线** | PerformerEmitSystem 该路径现状 throw；触碰 emit 热路径，未在本轮改 | 小单独做，加 per-viewer 可见性测试 |
| **PROV-3/5/6：marker catalog JSON、referee knowledge grant showcase、team palette** | 表现层数据 + 需要可视验收 | 与 GUI showcase 一起做 |
| **INT-8：KnowledgeProjection tag/stance 事实投影（伪装）** | 新基建，M11 伪装 UAT 标 deferred | 独立 RFC 子单 |
| **INT-6 RTS 多选轴移动 dispatch** | AxisMoveOrderSystem 当前最小面只动 local rep 化身 | 接 Dispatch 后扩展 |
| **偏好/方案的 Settings UI 与 Save/Load 调用点** | 策略归后续 settings 工作 | `TrySetPreference`/`TrySwitch`/`Save/Load` API 已就绪 |
| **M10 确定性回放 acceptance（双端 hash）** | 需回放基建配合 | GUARD-2 子单 |
| **gitbook 回写（DOC-1）** | RFC 尚未 accept | RFC accept 后执行 |

## 四、待做的 Showcase（需要 raylib + Ludots CEF WebUI 的 Windows 环境）

团队常用验收场景是 **raylib + CEF WebUI dataplane**（本 VM CEF 为 net8.0-windows 不可跑）。以下 showcase 的无头逻辑/数据已就绪或部分就绪：

1. **SHOW-2（M3+M4+P5）代理控制拓扑投影**：`control_plane_projection_showcase`（launcher binding 已注册）。无头 acceptance 已绿；差 raylib 运行录屏（O 键 toggle → 深绿/浅绿 marker 变化）+ CEF 面板接 `ludots.showcase.control_plane.state` topic（DataPlane 契约层已实现并有 mock transport 测试）。marker 双色 performer 规则可用新拓扑谓词 graph ops 表达（PerformerTopologyConditionGraphTests 是条件面的样板）。
2. **SHOW-1（M2）超级武器 context**：CTX-6/7 基建就绪；需做 showcase mod（ability 带 interactionContextProfile + targeting collection + indicator performer + IMC 切换演示）。
3. **SHOW-3（M5+P8）裁判多控制域投影**：依赖 PROV-5 knowledge grant 读多 playerRep collection + palette 相位；KnowledgeHasProjection op 已就绪。
4. **SHOW-4（M6+P3）面板聚合三案例切换**：内核 + PNL-4 迁移已就绪（`SetAggregationProfile` 运行时切换）；差 UI 演示（EntityCommandPanel 已是现成宿主）。
5. **SHOW-5（M8+P4）追猎 blink 三种 dispatch**：Dispatch 内核就绪；差 fan-out 接线（见尾巴 3）后的 playable。
6. **SHOW-6（M11+M12+P9）pointer intent 路由 + ControlScheme 热切换**：Intent/Scheme/偏好内核就绪；差 fan-out 接线 + WASD scheme 演示 map。

Showcase 通用注意：单位外观用 cube/sphere + Static/InstancedStaticMesh renderPath（GpuSkinned 不进 web/primitive 流）；marker 用 GroundOverlay Ring（`entity_query_tactics` 先例）；Web 面板走 `window.ludotsDataplane`（参照 `browser_react_flow`/`browser_rts_production` 模板）。

## 五、给审计 agent 的执行提示词（可直接粘贴）

```
你在 Ludots 仓库分支 cursor/epic-unified-interaction-casting-114d（PR #581）上审计 RFC-0065 的第一批实现。
先读 docs/rfcs/RFC-0065-unified-interaction-collection-casting-architecture.md（铁律 §3、DEC §4、UAT §6）
与 docs/audits/rfc-0065-implementation-handoff.md（本文）。

审计范围 = git log 中含 "RFC-0065" 的全部提交。逐项核查：
1) 铁律 1~16 合规（重点：零业务语义字面量、零 fallback、collection 永不跨域迁移、
   Performer 只读、OrderQueue 唯一 intake——注意旧输入链路尚未迁移属已知尾巴，不算违规）；
2) 每个新 registry/profile 的加载 fail-fast 完整性（未知 kind/重复 id/悬空引用）；
3) 0 alloc 声明在你的环境复测（本实现 VM 有 24B GC 测量波动）；
4) GameEngine 接线块的构造顺序依赖与服务空引用风险；
5) 新 graph ops（397/410-412/420-422）的寄存器分配与既有 graph 程序兼容性；
6) handoff §三的尾巴清单是否有被实现代码隐式跨越的（例如有代码假设 CTRL-3 已完成）；
7) 测试基线失败清单（handoff §二.4）与你环境的差异。
产出：问题清单（严重度/文件/建议修法）+ 每个 M1~M12 UAT 的当前可验证状态表。
禁止：把已知尾巴当缺陷报告；把环境性失败归因于实现。
```
