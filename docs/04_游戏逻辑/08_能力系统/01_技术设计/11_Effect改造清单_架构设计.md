---
文档类型: 架构设计
创建日期: 2026-02-08
最后更新: 2026-02-08
维护人: X28技术团队
文档版本: v1.0
适用范围: 游戏逻辑 - 能力系统(GAS) - Effect 架构改造清单与实施阶段
状态: 审阅中
---

# Effect 改造清单 架构设计

所有条目均为架构期必须完成的改造，不存在"保留现状"选项。

# 1 改造项

| # | 改造项 | 涉及文件 | 改造内容 |
|---|---|---|---|
| 1 | PresetType 类型定义表 | 新增 `PresetTypeRegistry.cs`, `PresetTypeDefinition.cs`, `ComponentFlags.cs` | 实现声明式类型注册：组件集 + 活跃 Phase + 默认 PhaseHandlers + 约束条件 |
| 2 | ConfigParams 扩容 | `EffectConfigParams.cs` | `MAX_PARAMS` 16 → 32；新增 `MergeFrom(in EffectConfigParams caller)` |
| 3 | EffectParamKeys | 新增 `EffectParamKeys.cs` | 按组件分组注册 `_ep.*` 保留键（DurationParams / TargetQueryParams / TargetFilterParams / TargetDispatchParams / ForceParams） |
| 4 | CallerParams 通道 | `EffectRequestQueue.cs` | `EffectRequest` 新增 `CallerParams` + `HasCallerParams`；删除 `F0-F3` / `I0-I1` |
| 5 | EffectCallerParams 组件 | 新增 `EffectCallerParams.cs` | per-instance CallerParams 组件，附加在 duration effect entity 上 |
| 6 | 三个 Effect 系统 merge 改造 | `EffectApplicationSystem.cs`, `EffectLifetimeSystem.cs`, `EffectProposalProcessingSystem.cs` | 所有 `SetConfigContext` 调用处统一 merge（`BuildMergedConfig`） |
| 7 | EffectTemplateLoader 重写 | `EffectTemplateLoader.cs`, `EffectTemplateConfig.cs` | 新 JSON schema（组件化结构）；由类型定义表驱动注入；删除所有 legacy 字段解析；fail-fast 校验 |
| 8 | TargetResolver 拆分 | `EffectTemplateRegistry.cs`, `TargetResolverFanOutHelper.cs` | `TargetResolverDescriptor` 拆为三个独立 struct：`TargetQueryDescriptor` + `TargetFilterDescriptor` + `TargetDispatchDescriptor` |
| 9 | PhaseGraphBindings 重命名 | `EffectBehaviorTemplate.cs` → `EffectPhaseGraphBindings.cs` | struct 重命名 + 所有引用更新 |
| 10 | F0-F3/I0-I1 废除 | `EffectRequestQueue.cs`, `EffectProposalProcessingSystem.cs` | 删除无名载荷字段；`ApplyPresetModifiers` 改为从 merged ConfigParams 读取 |
| 11 | PresetBehaviorRegistry 整合 | `PresetBehaviorRegistry.cs`, `EffectPhaseExecutor.cs` | 整合进 `PresetTypeDefinition.PhaseHandlers`；删除独立 registry |
| 12 | UntilTagRemoved / WhileTagPresent 实现 | `EffectLifetimeSystem.cs`, `EffectTemplateLoader.cs` | 在 `EffectLifetimeSystem` 中实现 tag 检测逻辑；Loader 支持解析新 lifetime 值 |
| 13 | JSON 模板迁移 | `assets/Configs/GAS/effects.json`, `src/Mods/*/assets/GAS/effects.json` | 按新 schema 重写所有现有 effect 模板 |
| 14 | EffectTemplateData 按组件重组 | `EffectTemplateRegistry.cs` | 扁平 struct 拆分为组件化内部结构；零 GC 约束下可用 union 或按组件位标志跳过无关字段 |

# 2 实施阶段

**Phase A（阻塞 Timeline，优先级最高）**：
- #1 PresetType 类型定义表
- #2 ConfigParams 扩容 + MergeFrom
- #3 EffectParamKeys
- #4 CallerParams 通道（含 #10 废除 F0-F3）
- #5 EffectCallerParams 组件
- #6 三个 Effect 系统 merge 改造
- #7 EffectTemplateLoader 重写 + 新 JSON schema
- #8 TargetResolver 拆分
- #9 PhaseGraphBindings 重命名
- #11 PresetBehaviorRegistry 整合
- #13 JSON 模板迁移

**Phase B（依赖 Phase A）**：
- AbilityTimeline 系统（Clip/Signal + CallerParams 注入 + 混合时钟）

**Phase C（与 Phase B 可并行）**：
- #12 UntilTagRemoved / WhileTagPresent 实现
- #14 EffectTemplateData 按组件重组
