---
文档类型: 对齐报告
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 核心引擎 - 平台适配与Adapters - Raylib Host 注入清单
状态: 草案
依赖文档:
  - docs/00_文档总览/01_文档规范/15_对齐报告.md
  - docs/02_核心引擎/05_平台适配与Adapters/01_架构设计/01_平台适配与Adapter架构.md
---

# Raylib Host 注入清单 对齐报告

# 1 摘要

## 1.1 结论

Raylib 平台通过 `RaylibGameHost` 在启动期把平台能力与 UI/输入对象注入 `GameEngine.GlobalContext`，Core 侧系统通过 `ContextKeys` 消费这些能力完成输入阻塞、屏幕射线拾取、脚本触发 UI 等功能。

## 1.2 风险等级与影响面

- 风险等级：中  
- 影响面：若 ContextKeys 注入不完整或类型不匹配，会导致核心系统静默不工作（当前部分消费点直接 return）。  

## 1.3 建议动作

1. 对关键注入项（InputHandler、ScreenRayProvider、UISystem）在 Host 装配期做强校验并 fail-fast。  
2. 将 “Host-only 注入项” 与 “Core 消费注入项” 在文档中明确区分，减少误用。  

# 2 审计范围与方法

## 2.1 审计范围

- Raylib Host 注入点：`src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs`
- 注入键 SSOT：`src/Core/Scripting/ContextKeys.cs`
- Core 侧消费点：`src/Core/Systems/` 与 `src/Core/Presentation/Systems/`、脚本命令与 Trigger

## 2.2 审计方法

- 检索 `GlobalContext[ContextKeys.*]` 的生产者与消费者，形成映射表并标注 “Host-only / Core-consumed”。  

# 3 注入映射表

## 3.1 映射表

| ContextKeys | 注入类型/形态 | 生产者（注入位置） | 主要消费者（使用位置） | 备注 |
|---|---|---|---|---|
| UIRoot | `Ludots.UI.UIRoot` | `RaylibGameHost.cs` | 多个 Mod 的 Trigger 直接 `context.Get<UIRoot>(ContextKeys.UIRoot)` | 主要用于 UI 组件树与渲染脏标记 |
| UISystem | `Ludots.Core.UI.IUiSystem` | `RaylibGameHost.cs` | `ShowUiCommand.cs`、`ScriptContextExtensions.GetUI` | 用于脚本侧设置 HTML/CSS 等 UI 入口 |
| InputHandler | `PlayerInputHandler` | `RaylibGameHost.cs` | `InputRuntimeSystem.cs`、`MobaSelectionSystem.cs` 等 | 负责 action 映射与上下文栈 |
| InputBackend | `IInputBackend` | `RaylibGameHost.cs` | `ResponseChainHumanOrderSourceSystem.cs` | 低层读键/鼠/IME；用于 UI 可见时的热键指令 |
| UiCaptured | `bool` | `RaylibGameHost.cs` 每帧更新 | `InputRuntimeSystem.cs` | 用于阻塞输入（UI 抢占） |
| ScreenRayProvider | `IScreenRayProvider` | `RaylibGameHost.cs` | `MobaSelectionSystem.cs` | 用于拾取：屏幕点转世界射线 |
| ScreenProjector | `IScreenProjector` | `RaylibGameHost.cs` | `VisualBenchmarkMapUiTrigger.cs` | 作为平台能力供 UI/Overlay 投影使用 |

# 4 差异与问题

## 4.1 差异与问题清单

| 问题 | 现象 | 风险 | 建议 |
|---|---|---|---|
| 缺失注入时可能静默失效 | 多数系统 TryGetValue 失败直接 return | 调试成本高 | 对关键注入在 Host 装配期 fail-fast |
| ScreenProjector 未被 Core 消费 | 键存在但当前只在 Host 中使用 | 易误导 | 文档明确标注为 Host-only；后续若要 Core 消费需立项对齐 |

# 5 行动项

## 5.1 行动项清单

| 动作 | 优先级 | 验收条件 |
|---|---|---|
| Host 装配期为关键注入项增加强校验 | P1 | 缺失或类型不匹配直接中止启动并报错 |
| 给 ScreenProjector 建立明确口径 | P2 | 文档与代码消费点一致，无悬空键 |
