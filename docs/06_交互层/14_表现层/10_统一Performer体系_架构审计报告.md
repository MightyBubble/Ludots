---
文档类型: 架构审计报告
创建日期: 2026-02-08
维护人: X28技术团队
文档版本: v1.0
适用范围: 交互层 - 表现层 - 统一 Performer 体系合规性审计
状态: 已完成
依赖文档:
  - docs/06_交互层/14_表现层/09_统一Performer规则体系_架构设计.md
  - docs/06_交互层/14_表现层/99_KANBAN.md
---

# 统一 Performer 体系 架构审计报告

# 1 审计范围与方法

## 1.1 审计目标

验证统一 Performer 体系实施后，项目各层是否严格遵守以下架构原则：

| 原则 | 说明 |
|------|------|
| **逻辑-表现分离** | GAS/Graph 层不感知 Performer；Presentation 层仅单向读取 GAS 组件 |
| **单一职责** | 每个系统只做一件事，不越界 |
| **松耦合** | 层间通过接口/缓冲区协议通信，不直接引用具体实现 |
| **配置驱动** | 视觉参数从配置加载，不硬编码 |
| **不重复造轮子** | 禁止旁路 Performer 管线自行实现等效逻辑 |

## 1.2 审计范围

| 层 | 目录 | 文件数 |
|----|------|--------|
| Core.Presentation (Performers) | `src/Core/Presentation/Performers/` | 12 |
| Core.Presentation (Systems) | `src/Core/Presentation/Systems/` | 4 |
| Core.Presentation (Config) | `src/Core/Presentation/Config/` | 1 |
| Core.Engine | `src/Core/Engine/GameEngine.cs` | 1 |
| Core.Gameplay.GAS | `src/Core/Gameplay/GAS/` | 全目录 |
| Core.NodeLibraries.GASGraph | `src/Core/NodeLibraries/GASGraph/` | 全目录 |
| Platform 抽象层 | `src/Platform/` | 5 |
| Raylib 适配层 | `src/Adapters/Raylib/` | 全目录 |
| MobaDemoMod | `src/Mods/MobaDemoMod/` | 全目录 |
| LudotsCoreMod | `src/Mods/LudotsCoreMod/` | 全目录 |

## 1.3 审计方法

- 静态代码分析：检查 using 声明、类型引用方向
- 依赖图验证：确认单向依赖（GAS ← Presentation ← Adapter / Mod）
- 功能重叠检测：比对各层实现是否与 Core Performer 管线功能重复
- 硬编码扫描：检查魔法数字、硬编码参数

---

# 2 Core 层审计结果

## 2.1 Performer 数据模型层 — 全部通过

| 文件 | 结论 | 说明 |
|------|------|------|
| `EntityScopeFilter.cs` | PASS | 零依赖纯枚举 |
| `PerformerDefinition.cs` | PASS | 仅引用 `System.Numerics` + 同模块类型 |
| `ValueRef.cs` | PASS | 零依赖纯结构体 |
| `BuiltinPerformerDefinitions.cs` | PASS | 仅引用 Presentation 层内部类型 |
| `WorldHudValueMode.cs` | PASS | 零依赖纯枚举 |

## 2.2 Performer 系统层 — 全部通过

| 文件 | 结论 | 说明 |
|------|------|------|
| `PerformerRuleSystem.cs` | PASS | 仅读 PresentationEventStream，不直接读 GAS 事件 |
| `PerformerEmitSystem.cs` | PASS | 对 ECS 组件（VisualTransform/AttributeBuffer/CullState）全部只读 |
| `PerformerRuntimeSystem.cs` | PASS | 仅消费 PresentationCommandBuffer |
| `PresentationBridgeSystem.cs` | PASS | 唯一合法的 GAS→Presentation 桥接点 |

## 2.3 Config 层 — 已修复

| 文件 | 结论 | 说明 |
|------|------|------|
| `PerformerDefinitionConfigLoader.cs` | **已修复** | 原违规：直接引用 `Gameplay.GAS.Registry.AttributeRegistry`。修复方案：改为注入 `Func<string, int>` 委托，由 GameEngine 组合根注入 `AttributeRegistry.GetId` |

## 2.4 GAS 反向依赖 — 全部通过

在 `src/Core/Gameplay/GAS/` 和 `src/Core/NodeLibraries/GASGraph/` 全目录搜索确认：

- **零引用** `Ludots.Core.Presentation` 命名空间
- **零引用** 任何 Performer / PresentationEvent / DrawBuffer 类型
- `GAS.Presentation` 子命名空间仅包含纯数据结构（`GasPresentationEvent`/`GasPresentationEventBuffer`），无 Presentation 层依赖

## 2.5 GameEngine 组合根 — 通过

作为组合根，允许跨层引用以完成依赖注入。Performer 相关布线正确：
- `BuiltinPerformerDefinitions.Register()` → 注册内建定义
- `PerformerDefinitionConfigLoader(configs, defs, AttributeRegistry.GetId)` → 注入属性解析器
- `PresentationBridgeSystem(gasEvents, events, ...)` → 桥接 GAS 事件

---

# 3 Platform 抽象层审计结果 — 全部通过

Platform 抽象层仅定义接口（`IGameHost`、`IRenderBackend`、`IScreenProjector`、`IScreenRayProvider`、`ScreenRay`），无实现逻辑，不涉及 Performer 或 DrawBuffer。职责边界清晰。

---

# 4 Raylib 适配层审计结果

## 4.1 发现清单

| 编号 | 严重度 | 类型 | 文件 | 描述 |
|------|--------|------|------|------|
| R-01 | **高** | 旁路 + 硬编码 | `Services/RaylibViewController.cs:10` | Fov 硬编码 45.0f，Core 层 CameraState 默认 60.0f，剔除视口不匹配 |
| R-02 | 中 | 旁路 | `RaylibHostLoop.cs:148-152` | 直接查询 ECS 统计实体数量（调试代码） |
| R-03 | 中 | 重复 + 遗漏 | `RaylibHostLoop.cs:297-308` | WorldHudValueMode 解析遗漏 `Constant` 模式 |
| R-04 | 低 | 硬编码 | `RaylibHostLoop.cs:289` | 字号 fallback 14 ≠ Core 默认 16 |
| R-05 | 低 | 硬编码 | `RaylibHostLoop.cs:283` | Bar 边框颜色固定黑色 |
| R-06 | 低 | 硬编码 | `RaylibHostLoop.cs:42-50` | 地形渲染 6 个魔法数字 |
| R-07 | 低 | 重复 | `RaylibHostLoop.cs:113-116` + `RaylibCameraAdapter.cs:30-32` | Gizmo 绘制代码两份 |
| R-08 | 低 | 硬编码 | `RaylibHostLoop.cs:61-68` | 初始相机 fovy=45 ≠ Core 默认 60 |
| R-09 | 低 | 硬编码 | `Services/RaylibScreenRayProvider.cs:32` | 近/远裁剪面 0.01/1000 硬编码 |

## 4.2 正面评价

适配层核心渲染路径（`RenderWorldHud`、`DrawGroundOverlays`、`primitiveRenderer.Draw`）正确从 `GlobalContext` 读取 DrawBuffer 数据，**未绕过 Performer 体系直接从 ECS 读取属性/颜色/生命周期等参数**。唯一的 ECS 直接访问（R-02）是调试代码。

---

# 5 MobaDemoMod 审计结果

## 5.1 发现清单

| 编号 | 严重度 | 类型 | 文件 | 描述 |
|------|--------|------|------|------|
| M-01 | **高** | 旁路 | `Presentation/MobaUnitPrimitiveRenderSystem.cs` (全文件) | 完整绕过 Performer 管线：自行查询实体 → CullState 剔除 → 直接写 PrimitiveDrawBuffer。Core PerformerEmitSystem 已有等效 entity-scoped 模式 |
| M-02 | **高** | 旁路 | `Triggers/InstallMobaDemoOnGameStartTrigger.cs:86-94` | 直接写入 TransientMarkerBuffer，绕过 PresentationCommandBuffer 管线 |
| M-03 | 中 | 硬编码 | `Presentation/MobaUnitPrimitiveRenderSystem.cs` | 8 处硬编码视觉参数（Y偏移 0.25、选中色黄色、缩放 0.35 等） |
| M-04 | 中 | 硬编码 | `Presentation/MobaLocalOrderSourceSystem.cs:127` | PerformerDefinition ID `1` 硬编码 |
| M-05 | 中 | 硬编码 | `GAS/MobaDemoAbilityDefinitions.cs:61-69` | 指示器三色（ValidColor/InvalidColor/RangeCircleColor）硬编码 |
| M-06 | 中 | 硬编码 | `GAS/MobaDemoAbilityDefinitions.cs` | 技能数值（距离/冷却/GCD）全硬编码 |
| M-07 | 低 | 硬编码 | `Presentation/MobaLocalOrderSourceSystem.cs:47-53` | OrderTag fallback 101/100 硬编码 |
| M-08 | 低 | 硬编码 | `Triggers/InstallMobaDemoOnGameStartTrigger.cs:46-52` | OrderTag fallback 101/103 硬编码 |
| M-09 | 信息 | 残留 | `Triggers/InstallMobaDemoOnGameStartTrigger.cs:102-104` | 旧系统 TODO 注释（队伍标签迁移未完成） |

## 5.2 核心问题分析

**M-01 是本次审计发现的最严重问题**。`MobaUnitPrimitiveRenderSystem` 完整复制了 Core `PerformerEmitSystem` 的 entity-scoped 渲染流程：

```
MobaUnitPrimitiveRenderSystem（旁路）:
  World.Query(VisualTransform) → CullState 剔除 → 直接写 PrimitiveDrawBuffer

PerformerEmitSystem（正路）:
  EmitEntityScoped(def) → CullState 剔除 → 解析 Bindings → 写 PrimitiveDrawBuffer
```

**修复方向**：删除 `MobaUnitPrimitiveRenderSystem`，在 `InstallMobaDemoOnGameStartTrigger` 中注册 entity-scoped `PerformerDefinition`（`VisualKind=Marker3D`, `EntityScope=AllWithVisualTransform`）。

---

# 6 LudotsCoreMod 审计结果

## 6.1 发现清单

| 编号 | 严重度 | 类型 | 文件 | 描述 |
|------|--------|------|------|------|
| C-01 | 信息 | ID 覆盖 | `assets/Presentation/performers.json` | ID 9010/9011 与 BuiltinPerformerIds 重叠。设计意图为 JSON 覆盖内建（`Registry.Register()` 覆盖语义），但应明确文档化 |

## 6.2 正面评价

CoreMod 无任何旁路实现，所有 Performer 相关逻辑均通过标准配置体系（`performers.json`）驱动。

---

# 7 测试覆盖

## 7.1 单元测试（26 case）

`src/Tests/GasTests/PerformerPipelineTests.cs`

| 测试类 | 覆盖范围 |
|--------|----------|
| EventFilterTests | 事件匹配（精确匹配 / 通配符 / 不匹配） |
| PerformerInstanceBufferTests | 实例创建 / Scope 级联销毁 / 参数覆盖 |
| PerformerRuleSystemTests | 事件→命令转换 / 条件评估 / 流清空 |
| PerformerRuntimeSystemTests | 命令消费 / 实例生命周期 |
| PerformerEmitSystemTests | 输出到 DrawBuffer / 可见性剔除 |
| PresentationBridgeGasTests | GAS 事件桥接 / 事件类型映射 |
| BuiltinPerformerDefinitionTests | 内建定义注册完整性 |

## 7.2 端到端测试（13 case）

`src/Tests/GasTests/PerformerEndToEndTests.cs`

| 测试 | 覆盖场景 |
|------|----------|
| EffectApplied → FloatingCombatText | GAS 效果 → 飘字输出 |
| FloatingCombatText Y-Drift | 飘字上飘动画 |
| FloatingCombatText AlphaFade | 飘字淡出动画 |
| FloatingCombatText Lifetime | 生命周期过期消失 |
| CastCommitted → Marker3D | 施法成功 → 标记输出 |
| CastCommitted Marker Expire | 标记过期 |
| CastFailed → SmallGreyMarker | 施法失败 → 灰色标记 |
| EntityScoped HealthBar | 实体作用域血条 |
| EntityScoped Skip NoAttributes | 无属性实体跳过 |
| EntityScoped CullInvisible | 不可见实体剔除 |
| DirectApi CreateAndDestroyScope | 直接 API 生命周期 |
| MultipleGasEvents OneFrame | 单帧多事件 |
| EffectApplied DeadEntity | 死亡实体安全处理 |

## 7.3 回归测试

全量测试 242 case：241 通过 / 1 预存失败（`ArchitectureGuardTests`，与本次无关）。

---

# 8 已修复的违规

| 编号 | 修复前 | 修复后 | 验证 |
|------|--------|--------|------|
| Config 层 GAS 依赖 | `PerformerDefinitionConfigLoader` 直接引用 `AttributeRegistry.GetId()` | 改为注入 `Func<string, int>` 委托，由 `GameEngine` 组合根注入 | 39/39 Performer 测试全通过 |

---

# 9 待修复问题优先级

## 9.1 高优先级（影响架构合规性）

| 编号 | 问题 | 修复方向 |
|------|------|----------|
| M-01 | `MobaUnitPrimitiveRenderSystem` 整体旁路 | 删除系统，注册 entity-scoped PerformerDefinition |
| M-02 | `TransientMarkerBuffer` 直接写入 | 改走 PresentationCommandBuffer → PlayOneShotPerformer |
| R-01 | Fov 硬编码不匹配 | 从 RaylibCameraAdapter 读取实际 fovy |

## 9.2 中优先级（硬编码需配置化）

| 编号 | 问题 | 修复方向 |
|------|------|----------|
| R-03 | ValueMode 遗漏 Constant | 补全分支 |
| M-04 | PerformerDef ID 硬编码 | 定义 MobaPerformerIds 常量类 |
| M-05/M-06 | 技能参数硬编码 | 从 JSON 配置加载 |

## 9.3 低优先级（代码质量提升）

M-07/M-08 OrderTag fallback、R-04~R-09 各类魔法数字等，建议后续逐步改进。

---

# 10 架构依赖图验证结论

```
依赖方向（正确 ✓）:

  GAS Layer ──事件数据──→ GAS.Presentation (纯数据)
       ↑                          │
       │ 不引用                    │ 被桥接
       │                          ↓
  Core.Presentation ←── PresentationBridgeSystem（唯一桥接点）
       │
       ├── Performers (数据模型) ← Systems (RuleSystem/EmitSystem/RuntimeSystem)
       │        ↑                       │
       │        │ 引用                  │ 输出
       │        │                       ↓
       │   Config (ConfigLoader)    DrawBuffers (Ground/Primitive/WorldHud)
       │                                │
       ↓                                ↓
  Platform 抽象层 ←────── Raylib 适配层（消费 DrawBuffers）
                              ↑
                              │
                         Mod 层（MobaDemoMod / LudotsCoreMod）
                         通过 PresentationCommandBuffer + performers.json 驱动
```

**结论**：Core 层架构分层严格合规。GAS→Presentation 单向桥接、Performer 管线职责清晰。主要问题集中在 **MobaDemoMod 的历史遗留旁路实现**（`MobaUnitPrimitiveRenderSystem`）和 **Raylib 适配层的 Fov 不一致**，需在后续迭代中修复。
