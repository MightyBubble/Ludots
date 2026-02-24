---
文档类型: 架构设计
创建日期: 2026-02-08
最后更新: 2026-02-08
维护人: X28技术团队
文档版本: v0.1
适用范围: 交互层 - 表现层 - 统一Performer规则体系
状态: 已实现
依赖文档:
  - docs/06_交互层/14_表现层/00_总览.md
  - docs/06_交互层/14_表现层/01_Performer表现架构.md
  - docs/03_基础服务/04_Graph运行时/03_NodeLibraries_架构设计.md
---

# 统一 Performer 规则体系 架构设计

# 1 设计概述

## 1.1 本文档定义

本文档定义了**统一 Performer 规则体系**，将所有可视化反馈元素（地面指示器、3D 标记、世界浮动文本、世界血条等）统一为一种配置驱动的 Performer 抽象，取代此前分散在多个独立系统中的硬编码逻辑。

**已删除的旧系统**（全部由 Performer 统一体系替代）:
- PresentationControlSystem — CueTag → PlayOneShotPerformer 映射
- CastFeedbackSystem — GAS 施法事件 → TransientMarker
- FloatingCombatTextSystem — GAS EffectApplied → 上飘伤害/治疗数字
- WorldHudCollectorSystem — 实体查询 → WorldHudBatchBuffer 血条/文本
- IndicatorPerformerSystem — IndicatorRequestBuffer → GroundOverlay

核心理念：**一切表现反馈都是 Performer**——指示器只是使用特定资产/材质、程序化传递参数的 Performer 实例。

## 1.3 两种作用域模式

| 模式 | 适用场景 | 触发方式 | 实例管理 |
|---|---|---|---|
| **Instance-scoped** | 瞬态标记、飘字、指示器 | Event → Rule → Command | PerformerInstanceBuffer |
| **Entity-scoped** | 血条、属性文本、名牌 | 每帧查询实体 | 不需要 Instance，直接输出 |

## 1.4 时间调制

| 字段 | 说明 |
|---|---|
| `PositionOffset` | 世界坐标偏移（如浮动文本初始 Y+1.0） |
| `PositionYDriftPerSecond` | Y 轴每秒漂移量（如飘字上升 0.8m/s） |
| `AlphaFadeOverLifetime` | 基于 DefaultLifetime 线性淡出 alpha |

## 1.5 GAS 事件桥接

PresentationBridgeSystem 新增 GasPresentationEventBuffer → PresentationEventStream 桥接：
- `EffectApplied` → `PresentationEventKind.EffectApplied` (KeyId=EffectTemplateId, Magnitude=Delta)
- `CastCommitted` → `PresentationEventKind.CastCommitted` (KeyId=AbilityId, PayloadA=Slot)
- `CastFailed` → `PresentationEventKind.CastFailed` (KeyId=AbilityId, PayloadB=FailReason)

## 1.2 设计原则

- **逻辑与表现分离**：GAS/Graph 层完全不感知 Performer 的存在，仅产出通用事件和状态
- **Graph 复用**：Performer 条件评估和参数计算单向依赖 GraphExecutor，Graph 零修改
- **配置驱动**：通过 JSON 定义 PerformerDefinition，零代码添加新的可视化反馈
- **声明式可见性**：可见性条件管控实例完整生命周期（Active/Dormant），不是渲染时过滤
- **数据同步安全**：参数每帧现读，离屏再回屏自动同步，无需恢复逻辑

# 2 架构总览

## 2.1 层次关系

```
复用层（零修改）:
  GraphExecutor + GasGraphOpHandlerTable + IGraphRuntimeApi
  GraphProgramRegistry
  PresentationEventStream / PresentationCommandBuffer
  PresentationBridgeSystem

扩展层（少量枚举值）:
  PresentationEventKind  (+PerformerCreated, PerformerDestroyed)
  PresentationCommandKind (+CreatePerformer, DestroyPerformer, DestroyPerformerScope, SetPerformerParam)

新增层（Performer 领域）:
  PerformerDefinition / PerformerRule / EventFilter / ConditionRef / PerformerCommand
  PerformerParamBinding / ValueRef
  PerformerDefinitionRegistry / PerformerInstanceBuffer
  PerformerRuleSystem / PerformerEmitSystem
```

## 2.2 运行时管线

```
GAS/Tag System → PresentationBridgeSystem → PresentationEventStream
                                                    ↓
                                          PerformerRuleSystem
                                          (Event 匹配 + 条件评估 → PresentationCommand)
                                                    ↓
                                          PresentationCommandBuffer
                                                    ↓
                                          PerformerRuntimeSystem
                                          (CreatePerformer → 分配实例 + ScopeId)
                                          (DestroyPerformer → 释放实例)
                                          (DestroyPerformerScope → 级联释放)
                                          (SetPerformerParam → 覆盖参数)
                                                    ↓
                                          PerformerEmitSystem
                                          (Tick Elapsed → 评估 Visibility → 解析 Bindings → 输出到 DrawBuffer)
```

## 2.3 系统执行顺序

| 序号 | 系统 | 说明 |
|------|------|------|
| 1 | ResponseChainDirectorSystem | 不变 |
| 2 | **PresentationBridgeSystem** | GAS 事件 → PresentationEventStream |
| 3 | **PerformerRuleSystem** | Event → Command（已替代 PresentationControlSystem） |
| 4 | **PerformerRuntimeSystem** | 消费 CommandKind，管理 PerformerInstance 生命周期 |
| 5 | **PerformerEmitSystem** | 遍历 PerformerInstanceBuffer / 实体查询，按 VisualKind 输出到 DrawBuffer |

# 3 数据模型

## 3.1 PerformerDefinition

`src/Core/Presentation/Performers/PerformerDefinition.cs`

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | int | 唯一标识 |
| VisualKind | PerformerVisualKind | 输出类型：GroundOverlay/Marker3D/WorldText/WorldBar |
| Rules | PerformerRule[] | 事件驱动规则 |
| VisibilityCondition | ConditionRef | 声明式可见性管控 |
| Bindings | PerformerParamBinding[] | 声明式参数绑定 |
| MeshOrShapeId | int | 资产 ID |
| DefaultColor | Vector4 | 默认颜色 |
| DefaultScale | float | 默认缩放 |
| DefaultLifetime | float | 生命周期（<=0 为持久） |

## 3.2 PerformerRule

```
EventFilter (Kind + KeyId) → ConditionRef (Inline / Graph) → PerformerCommand (CommandKind + params)
```

## 3.3 ConditionRef

- `Inline != None` → 走内联 switch（SourceIsLocalPlayer, OwnerCullVisible 等纯技术条件）
- `GraphProgramId > 0` → 调用 GraphExecutor，约定 B[0] = 结果
- 两者都默认 → always true

内联条件仅限纯基础设施概念。任何阵营关系、属性阈值等业务逻辑一律通过 Graph 程序实现。

## 3.4 声明式可见性

实例状态模型：

```
[创建] → Active
Active → Dormant (VisibilityCondition = false)
Dormant → Active (VisibilityCondition = true)
Active / Dormant → [销毁]
```

- Dormant：停止解析绑定、停止输出，但实例存活、Elapsed 继续推进
- Active 恢复时从绑定现读最新数据 → 天然数据同步

## 3.5 参数绑定与解析

三级优先级：**指令式覆盖 > 声明式绑定 > 静态默认值**

- `SetPerformerParam` 写入的覆盖值优先级最高
- `PerformerParamBinding` 每帧从数据源（Constant/Attribute/Graph）现读
- `PerformerDefinition` 上的 DefaultColor/DefaultScale 作为兜底

## 3.6 Scope 机制

同一 ScopeId 的实例形成逻辑分组。`DestroyPerformerScope` 一条命令级联销毁整组。

典型用例：技能瞄准创建多个 Performer（射程圈 + 效果圈 + 光标标记），共享 ScopeId。取消瞄准时一条命令全部清理。

# 4 直接 API 路径

Input/Aiming 层系统可直接向 `PresentationCommandBuffer` 写入命令，无需 Gameplay 事件：

```csharp
// 进入瞄准模式 — 创建一组 Performer
int scopeId = HashCode.Combine("aim", slotIndex);
_commands.TryAdd(new PresentationCommand {
    Kind = PresentationCommandKind.CreatePerformer,
    IdA = rangeCircleDefId,  // 射程圈 Definition ID
    IdB = scopeId,
    Source = casterEntity
});

// 退出瞄准模式 — 一条命令清理整组
_commands.TryAdd(new PresentationCommand {
    Kind = PresentationCommandKind.DestroyPerformerScope,
    IdA = scopeId
});
```

`PresentationCommandBuffer` 可通过 `GlobalContext[ContextKeys.PresentationCommandBuffer]` 获取。
`PerformerDefinitionRegistry` 可通过 `GlobalContext[ContextKeys.PerformerDefinitionRegistry]` 获取。

# 5 JSON 配置结构

配置文件路径：`Presentation/performers.json`（通过 ConfigPipeline 加载，支持 Mod 覆盖）。

```json
[
  {
    "id": 1,
    "visualKind": "GroundOverlay",
    "meshOrShapeId": 0,
    "defaultColor": [0.3, 0.7, 1.0, 0.15],
    "defaultScale": 6.0,
    "defaultLifetime": -1,
    "visibility": { "inline": "OwnerCullVisible" },
    "rules": [
      {
        "event": { "kind": "GameplayEvent", "keyId": 42 },
        "condition": { "inline": "SourceIsLocalPlayer" },
        "command": {
          "commandKind": "CreatePerformer",
          "performerDefinitionId": 2,
          "scopeId": 100
        }
      }
    ],
    "bindings": [
      { "paramKey": 0, "source": "attribute", "sourceId": 10 },
      { "paramKey": 4, "source": "constant", "constantValue": 0.5 }
    ]
  }
]
```

# 6 文件清单

## 6.1 新增文件

| 路径 | 说明 |
|------|------|
| `src/Core/Presentation/Performers/PerformerVisualKind.cs` | 视觉输出枚举 |
| `src/Core/Presentation/Performers/InlineConditionKind.cs` | 内联条件枚举 |
| `src/Core/Presentation/Performers/ConditionRef.cs` | 条件引用 |
| `src/Core/Presentation/Performers/ValueRef.cs` | 参数数据源 + ValueSourceKind |
| `src/Core/Presentation/Performers/PerformerParamBinding.cs` | 参数绑定 |
| `src/Core/Presentation/Performers/EventFilter.cs` | 事件过滤器 |
| `src/Core/Presentation/Performers/PerformerCommand.cs` | 规则命令 |
| `src/Core/Presentation/Performers/PerformerRule.cs` | 规则结构体 |
| `src/Core/Presentation/Performers/PerformerDefinition.cs` | 完整定义 |
| `src/Core/Presentation/Performers/PerformerInstance.cs` | 运行时实例 |
| `src/Core/Presentation/Performers/PerformerDefinitionRegistry.cs` | 定义注册表 |
| `src/Core/Presentation/Performers/PerformerInstanceBuffer.cs` | 实例缓冲 + Scope 级联 + 参数覆盖 |
| `src/Core/Presentation/Systems/PerformerRuleSystem.cs` | Event→Command 规则引擎 |
| `src/Core/Presentation/Systems/PerformerEmitSystem.cs` | 可见性+绑定+输出 |
| `src/Core/Presentation/Config/PerformerDefinitionConfigLoader.cs` | JSON 配置加载 |

## 6.2 修改文件

| 路径 | 说明 |
|------|------|
| `src/Core/Presentation/Events/PresentationEventKind.cs` | +PerformerCreated/Destroyed |
| `src/Core/Presentation/Commands/PresentationCommandKind.cs` | +Create/Destroy/DestroyScope/SetParam |
| `src/Core/Presentation/Systems/PerformerRuntimeSystem.cs` | 消费新 CommandKind |
| `src/Core/Engine/GameEngine.cs` | 注册新系统、创建资源 |
| `src/Core/Scripting/ContextKeys.cs` | +PerformerDefinitionRegistry/PerformerInstanceBuffer |

## 6.3 已退役文件（已删除）

| 旧路径 | 替代方案 | 删除日期 |
|------|------|------|
| `PresentationControlSystem.cs` | PerformerRuleSystem | 2026-02-07 |
| `CastFeedbackSystem.cs` | BuiltinPerformerDefinitions (CastCommitted/CastFailed 内建定义) | 2026-02-07 |
| `FloatingCombatTextSystem.cs` | BuiltinPerformerDefinitions (FloatingCombatText 内建定义 + 时间调制) | 2026-02-07 |
| `WorldHudCollectorSystem.cs` | Entity-scoped PerformerDefinition (EntityScopeFilter) | 2026-02-07 |
| `IndicatorPerformerSystem.cs` | PerformerEmitSystem (GroundOverlay VisualKind) | 2026-02-07 |
| `IndicatorRequestBuffer.cs` | PerformerInstanceBuffer + PresentationCommandBuffer | 2026-02-07 |
| `IndicatorDescriptor.cs` | PerformerDefinition + PerformerParamBinding | 2026-02-07 |
| `WorldHudConfig.cs` | performers.json 配置 + WorldHudValueMode.cs | 2026-02-07 |
| `WorldHudConfigLoader.cs` | PerformerDefinitionConfigLoader | 2026-02-07 |
| `hud.json` | performers.json | 2026-02-07 |
| `08_技能指示器配置驱动架构_架构设计.md` | 本文档 | 2026-02-07 |

## 6.4 新增文件（统一阶段）

| 路径 | 说明 |
|------|------|
| `src/Core/Presentation/Performers/EntityScopeFilter.cs` | 实体作用域枚举 |
| `src/Core/Presentation/Performers/BuiltinPerformerDefinitions.cs` | 内建定义注册（飘字/施法标记/血条） |
| `src/Core/Presentation/Hud/WorldHudValueMode.cs` | HUD 文本值显示模式枚举 |
| `src/Mods/LudotsCoreMod/assets/Presentation/performers.json` | 配置驱动的血条/血量文本 PerformerDefinition |
| `src/Tests/GasTests/PerformerPipelineTests.cs` | 单元测试（26 case） |
| `src/Tests/GasTests/PerformerEndToEndTests.cs` | 端到端测试（13 case） |

# 7 迁移记录

迁移已完成。以下为实际执行的迁移步骤记录：

1. ~~共存期~~ → **已完成**：旧系统全部删除
2. **CueTag 迁移** → PerformerRuleSystem 通过 EventFilter 匹配事件，代替旧 PresentationControlSystem 的 CueTag 注册
3. **Mod 迁移** → MobaLocalOrderSourceSystem 已从 IndicatorRequestBuffer 迁移到 PresentationCommandBuffer 直接 API
4. **退役清理** → 所有旧系统代码、旧配置文件、旧文档已删除（见 6.3）

**待完成**：MobaDemoMod 队伍标签从旧 ComponentText 迁移到 Performer Graph 条件解析（见 KANBAN M2）
