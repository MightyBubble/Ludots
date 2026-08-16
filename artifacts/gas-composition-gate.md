## GAS Composition Gate - Self Review

- **Task / Issue**: Issue #984 presentation runtime cleanup, bootstrap presenter destroy path
- **Date**: 2026-08-16
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本次不新增 gameplay 变体、profile 字段、preset 开关或并行物化管线，只把已编译的 presenter bootstrap destroy 规则接回现有 presenter command buffer。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| EntityDestroyed bootstrap rule publication | Layer 2 composition input | `CompiledPresenterBootstrapRegistry` existing rules |
| Destroy presenter scope command | Existing presentation command path | `PresenterCommandBuffer` + `PresenterRuntimeSystem` |

### 3. Reuse list

- Handlers: N/A
- Queues / Systems: `PresentationEntityLifecycleSystem`, `PresenterCommandBuffer`, `PresenterRuntimeSystem`
- Resolvers / Registries: `PresenterDefinitionRegistry`, `CompiledPresenterBootstrapRegistry`, `PresentationStableIdAllocator`
- Existing presets / graphs: N/A

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| N/A | N/A | N/A |

### 5. Transaction boundary

必须原子 rollback 的步骤: N/A。这里不新增实体结构事务，只发布表现层 destroy scope 命令，实际清理由现有 presenter runtime 处理。

### 6. Config SSOT

行为配置落在: `mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/Presentation/presenters.json`

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤
