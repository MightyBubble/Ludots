---
文档类型: 对齐报告
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 核心引擎 - 运行控制与调试 - GAS/Physics 对齐
状态: 草案
依赖文档:
  - docs/00_文档总览/规范/15_对齐报告.md
  - docs/02_核心引擎/04_运行控制与调试/01_仿真运行控制_架构设计.md
---

# GAS与Physics运行控制 对齐报告

# 1 摘要

## 1.1 结论

代码侧已经具备 run-by-step（TurnBasedPacemaker）与 run-until（GAS/Physics2D controller）的完整链路；文档侧已拆分为独立运行控制目录并补齐 SSOT 与边界约束，但“完成原因/策略事件”仍有进一步收敛空间。

## 1.2 风险等级与影响面

- 风险等级：中  
- 影响面：工具链/测试用例/调试脚本依赖完成原因字段时，字符串漂移会导致对齐成本上升。  

## 1.3 建议动作

1. 将 run-until 的完成原因从 string 收敛为受控枚举（并统一事件 payload 字段）。  
2. 为 GAS/Physics policy 配置化更新补齐配置入口与回归用例。  

# 2 审计范围与方法

## 2.1 审计范围

- 仿真循环控制器：Realtime/TurnBased 切换与 Step(N) 机制  
- GAS：RunUntilEffectWindowsClosed 与 StepPolicy  
- Physics2D：RunForFixedTicks/RunUntilSleeping 与 TickPolicy  

## 2.2 审计方法

- 静态代码审计：定位控制器入口、回调挂接点、状态机与退出条件。  
- 文档对齐审计：检查 SSOT、边界约束、禁止事项（无绝对路径/file scheme）。  

## 2.3 证据口径

证据仅使用仓库相对路径（`src/`、`docs/`），不使用本机绝对路径或 file scheme 链接。

# 3 差异表

## 3.1 差异表

| 设计口径 | 代码现状 | 差异等级 | 风险 | 证据 |
|---|---|---|---|---|
| run-by-step 通过 TurnBased 累积 fixed tick | 已实现 SimulationLoopController.Step → TurnBasedPacemaker.Step | 低 | 低 | `src/Core/Engine/Pacemaker/SimulationLoopController.cs` |
| run-until 在 fixed tick 完成回调判定条件 | 已在 GameEngine 固定步完成回调中调用 Gas/Physics controller 的 AfterTick | 低 | 低 | `src/Core/Engine/GameEngine.cs` |
| run-until 必须有退出条件（MaxFixedTicks 等） | GAS/Physics2D 都有 MaxFixedTicks/FixedTicks 等退出 | 低 | 低 | `src/Core/Gameplay/GAS/GasController.cs` |
| 完成原因应受控枚举 | 当前以 string 写入 ScriptContext | 中 | 中 | `src/Core/Engine/Physics2D/Physics2DController.cs` |
| policy 参数更新版本化 | GAS StepPolicy 与 Physics2D TickPolicy 都有 version 递增 | 低 | 低 | `src/Core/Gameplay/GAS/GasClockStepPolicy.cs` |
| 配置入口必须可定位且 fail-fast | 已通过 ConfigLoader + JsonMerger 合并并校验 | 低 | 低 | `src/Core/Gameplay/GAS/Config/GasClockConfig.cs` |
| Physics2D 时钟更新配置化 | 已通过 Physics2DClockConfigLoader 合并并校验 | 低 | 低 | `src/Core/Engine/Physics2D/Physics2DClockConfig.cs` |

# 4 行动项

## 4.1 行动项清单

| 动作 | 负责人 | 优先级 | 验收条件 |
|---|---|---|---|
| 收敛 run-until 完成原因为枚举并统一事件字段 | X28技术团队 | P1 | 事件 payload 不再依赖字符串 reason，测试可判定 |
| 补齐 policy 配置入口与加载期校验（GAS/Physics） | X28技术团队 | P1 | 可通过配置切换模式/Hz，且 fail-fast 可观测 |
| 增加 run-by-step/run-until 的回归用例 | X28技术团队 | P2 | 至少 3 条可自动化验收点（Idle/Blocked/MaxTicks） |
