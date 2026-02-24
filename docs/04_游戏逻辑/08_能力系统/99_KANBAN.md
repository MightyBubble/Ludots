---
文档类型: 开发看板
创建日期: 2026-02-05
最后更新: 2026-02-08
维护人: X28技术团队
文档版本: v0.3
适用范围: 04_游戏逻辑 - 08_能力系统 - 开发看板
状态: 进行中
---

# 能力系统 KANBAN

# 1 定位
## 1.1 本看板的唯一真相范围
- 本文档是本子系统当前开发计划唯一真相。
- 其它文档只允许引用卡片 ID。

## 1.2 不在本看板维护的内容
- 需求口径、架构设计、接口规范、配置结构与裁决条款。

# 2 使用规则
## 2.1 卡片字段规范
- ID：子系统内唯一，格式为 `K-001` 递增。
- 卡片：一句话描述交付结果。
- 负责人：明确到人。
- 验收标准：可判定，给出验证方法。
- 证据入口：仓库相对路径，指向代码或测试或对齐报告。

## 2.2 状态流转规范
- Backlog -> Doing -> Review -> Done

## 2.3 WIP 上限与阻塞处理
- Doing 列最多 5 张卡片。

# 3 看板
## 3.1 Backlog

### Phase A（阻塞 Timeline，优先级最高）

| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|
| K-005 | PresetType 类型定义表：实现 `PresetTypeRegistry` + `PresetTypeDefinition` + `ComponentFlags` | 待定 | 声明式注册组件集 + 活跃 Phase + PhaseHandlers + 约束；加载 `preset_types.json` | `src/Core/Gameplay/GAS/PresetTypeRegistry.cs` |
| K-006 | ConfigParams 扩容 16→32 + `MergeFrom` | 待定 | `MAX_PARAMS=32`；`MergeFrom` caller wins 覆盖；零 GC | `src/Core/Gameplay/GAS/Components/EffectConfigParams.cs` |
| K-007 | EffectParamKeys 按组件注册 `_ep.*` 保留键 | 待定 | Duration/TargetQuery/TargetFilter/TargetDispatch/Force 各组件 key 注册并按组件分组 | `src/Core/Gameplay/GAS/EffectParamKeys.cs` |
| K-008 | CallerParams 通道：`EffectRequest` 新增 CallerParams + 废除 F0-F3/I0-I1 | 待定 | `EffectRequest` 含 `CallerParams` + `HasCallerParams`；`F0-F3`/`I0-I1` 删除；编译通过 | `src/Core/Gameplay/GAS/EffectRequestQueue.cs` |
| K-009 | EffectCallerParams 组件 | 待定 | per-instance CallerParams 附加在 duration effect entity 上；OnApply 创建、OnExpire/Remove 随 entity 销毁 | `src/Core/Gameplay/GAS/Components/EffectCallerParams.cs` |
| K-010 | 三个 Effect 系统 merge 改造 | 待定 | 所有 `SetConfigContext` 调用处统一调用 `BuildMergedConfig`；CallerParams 正确覆盖 | `EffectApplicationSystem.cs` / `EffectLifetimeSystem.cs` / `EffectProposalProcessingSystem.cs` |
| K-011 | EffectTemplateLoader 重写（新 JSON schema + 组件化 + fail-fast） | 待定 | 加载新 schema；按 PresetType 定义表校验组件；删除所有 legacy 字段解析；废除字段 fail-fast | `src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs` |
| K-012 | TargetResolver 三层拆分 | 待定 | `TargetResolverDescriptor` 拆为 Query + Filter + Dispatch 三个独立 struct；`TargetResolverFanOutHelper` 方法签名更新 | `EffectTemplateRegistry.cs` / `TargetResolverFanOutHelper.cs` |
| K-013 | EffectBehaviorTemplate → EffectPhaseGraphBindings 重命名 | 待定 | struct 重命名 + 所有引用更新；编译通过 | `src/Core/Gameplay/GAS/Components/EffectPhaseGraphBindings.cs` |
| K-014 | PresetBehaviorRegistry 整合进 PresetTypeDefinition | 待定 | `PresetBehaviorRegistry` 删除；PhaseHandlers 由 `PresetTypeDefinition` 承载 | `src/Core/Gameplay/GAS/PresetBehaviorRegistry.cs`（删除） |
| K-015 | JSON 模板迁移：按新 schema 重写所有 effects.json | 待定 | 所有 `effects.json` 使用组件化结构；`preset_types.json` 存在并可加载 | `assets/Configs/GAS/` |

### Phase B（依赖 Phase A）

| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|
| K-016 | AbilityTimeline 系统：Clip/Signal + CallerParams 注入 + 混合时钟 | 待定 | Timeline Clip 触发 EffectRequest（含 CallerParams）；Signal 触发 GameplayEvent；混合 ClockId 工作 | 待定 |

### Phase C（与 Phase B 可并行）

| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|
| K-017 | UntilTagRemoved / WhileTagPresent 实现 | 待定 | `EffectLifetimeSystem` 实现 tag 检测过期逻辑；Loader 支持新 lifetime 值 | `src/Core/Gameplay/GAS/Systems/EffectLifetimeSystem.cs` |
| K-018 | EffectTemplateData 按组件重组 | 待定 | 扁平 struct 拆分为组件化内部结构；零 GC；按 ComponentFlags 跳过无关字段 | `src/Core/Gameplay/GAS/EffectTemplateRegistry.cs` |

## 3.2 Doing
| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|

## 3.3 Review
| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|

## 3.4 Done
| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|
| K-001 | TargetResolver 扇出逻辑提取为共享 Helper | X28 | `EffectApplicationSystem` / `EffectLifetimeSystem` 扇出逻辑委托到 `TargetResolverFanOutHelper`；无重复代码 | `src/Core/Gameplay/GAS/TargetResolverFanOutHelper.cs` |
| K-002 | EffectApplicationSystem Stage 5 时间切片 | X28 | ApplyFanOutCommands 使用 `_playbackCursor` + `workUnits` 循环；超限中断下帧继续 | `src/Core/Gameplay/GAS/Systems/EffectApplicationSystem.cs` |
| K-003 | EffectTemplateRegistry ref readonly 优化 | X28 | `TryGetRef` / `GetRef` 返回 `ref readonly`；扇出路径避免 >100B struct 拷贝 | `src/Core/Gameplay/GAS/EffectTemplateRegistry.cs` |
| K-004 | GasGraphRuntimeApi 移至 NodeLibraries Host + 移除 _effectTemplates | X28 | `GasGraphRuntimeApi` 在 `NodeLibraries/GASGraph/Host/`；构造函数不含 effectTemplates 参数 | `src/Core/NodeLibraries/GASGraph/Host/GasGraphRuntimeApi.cs` |

# 4 里程碑
## 4.1 当前里程碑
- M1：TargetResolver 扇出收敛 + 模板注册 ref 优化（已完成）

## 4.2 下一个里程碑
- M2：Effect 架构改造 Phase A（K-005 ~ K-015），阻塞 AbilityTimeline

## 4.3 后续里程碑
- M3：AbilityTimeline（K-016），依赖 M2
- M4：Lifetime 扩展 + TemplateData 重组（K-017, K-018），可与 M3 并行

# 5 变更记录
| 日期 | 变更人 | 变更摘要 |
|---|---|---|
| 2026-02-06 | X28 | 初始化看板；录入 K-001~K-004（已完成） |
| 2026-02-08 | X28 | 录入 Effect 架构改造 Phase A/B/C（K-005~K-018）；设定 M2/M3/M4 里程碑 |
