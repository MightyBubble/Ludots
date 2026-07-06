## GAS Composition Gate - Self Review

- **Task / Issue**: PR #581 RFC-0065 review fixes for relationship/control-plane hot paths and command target knowledge gates.
- **Date**: 2026-07-06
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本次修复复用既有 RelationshipRuntime、AssociationControlProfileRuntime、KnowledgeCommandTargetGate、ControlPlaneView 和测试预算护栏；没有新增 profile enum、preset 开关、graph op、effect step 或平行 schema。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| relationship typed edge churn 0Alloc | N/A - existing runtime infrastructure | RelationshipRuntime / Relationship<T> / RelationshipEdgeSet |
| association profile physical grant/revoke budget | N/A - existing runtime infrastructure | AssociationControlProfileRuntime + GasTests budget |
| command target knowledge gate fail-fast | N/A - input/control infrastructure | KnowledgeCommandTargetGate injection into CoreInput and registries |
| partial-domain projection budget | N/A - collection/control infrastructure | ControlPlaneViewUnitGrantTests |

### 3. Reuse list

- Handlers: N/A, no GAS handler changes.
- Queues / Systems: existing AssociationControlProfileSystem path in SchemaUpdate; existing OrderQueue remains untouched.
- Resolvers / Registries: RelationshipRuntime, RelationshipReverseIndex, ControlDomainQuery, KnowledgeProjectionResolver, CommandIntentProfileRegistry, ContextScoredOrderResolver.
- Existing presets / graphs: N/A, no preset or graph changes.

### 4. New Layer 0 ops (if any)

N/A - no new atomic op.

### 5. Transaction boundary

必须原子 rollback 的步骤: N/A. Relationship mutations remain through existing EnsureLink/RemoveLink/SetFlag paths; no new multi-step lifecycle transaction is introduced.

### 6. Config SSOT

行为配置落在: existing RFC-0065 catalog/profile assets and runtime registries.

是否新增 JSON schema: NO - no new schema.

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤 / existing data profile entries, not Core enum.
