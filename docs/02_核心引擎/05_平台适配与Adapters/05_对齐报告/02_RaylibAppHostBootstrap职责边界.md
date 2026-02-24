---
文档类型: 对齐报告
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 核心引擎 - 平台适配与Adapters - Raylib App Host Bootstrap 职责边界
状态: 草案
依赖文档:
  - docs/00_文档总览/01_文档规范/15_对齐报告.md
  - docs/02_核心引擎/05_平台适配与Adapters/01_架构设计/01_平台适配与Adapter架构.md
---

# Raylib App Host Bootstrap 职责边界 对齐报告

# 1 摘要

## 1.1 结论

总体遵守分层与职责边界：App 仅选择 Host 并运行；Bootstrap 负责从 baseDir 定位 assets 与配置并初始化引擎；Host 负责窗口与主循环并驱动 `engine.Tick(dt)`。

存在数个边界模糊点：Raylib Host 内部包含了偏游戏策略与会话装配的行为，例如硬编码载图、输入上下文栈选择与部分 Presentation 系统装配，建议收敛到显式的启动装配层，并用配置或启动参数驱动。

## 1.2 风险等级与影响面

- 风险等级：中
- 影响面：Host 与游戏策略耦合会降低多平台复用与自动化测试能力，且易引入跨平台差异；启动失败被吞掉会隐藏问题并影响 CI。

## 1.3 建议动作

1. 将载图与会话装配从 Host 移出，改为由 Bootstrap 或上层启动装配层驱动。  
2. Host 初始化失败改为 fail-fast 并返回非零退出码。  
3. 将 “平台注入” 与 “游戏装配” 的边界在文档与代码中固定，避免 Host 持续膨胀。  

# 2 审计范围与方法

## 2.1 审计范围

- App 入口：`src/Apps/Raylib/Ludots.App.Raylib/Program.cs`
- Host：`src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs`
- Bootstrap：`src/Core/Hosting/GameBootstrapper.cs`

## 2.2 审计方法

- 以 “进程入口 / 启动装配 / 平台主循环 / 引擎初始化” 四类职责为基准，检查每个文件的行为是否越界。  
- 以证据入口行范围为准，定位具体实现点。  

# 3 差异表

## 3.1 差异表

| 设计口径 | 代码现状 | 差异等级 | 风险 | 证据 |
|---|---|---|---|---|
| App 只负责选择 Host 并运行 | 仅创建 RaylibGameHost 并 Run | 低 | 低 | `src/Apps/Raylib/Ludots.App.Raylib/Program.cs` |
| Bootstrap 负责定位 assets 与配置并初始化引擎 | 严格校验 assets 与配置，Initialize 并返回 Engine | 低 | 低 | `src/Core/Hosting/GameBootstrapper.cs` |
| Host 负责窗口与主循环并驱动 Tick | Host 初始化窗口与循环，驱动 `engine.Tick(dt)` 并渲染 | 低 | 低 | `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs` |
| 启动失败应 fail-fast | Host 捕获异常后只打印并 return | 中 | 中 | `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs` |
| Host 不应硬编码游戏策略 | Host 启动后直接 `engine.LoadMap(MapIds.Entry)` | 中 | 中 | `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs` |
| Host 不应决定玩法输入上下文策略 | Host 直接 Push 两个 gameplay context | 中 | 中 | `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs` |
| Presentation 系统装配应有明确归属 | Host 直接 RegisterPresentationSystem | 低 | 中 | `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs` |

# 4 行动项

## 4.1 行动项清单

| 动作 | 优先级 | 验收条件 |
|---|---|---|
| 用配置驱动初始地图与会话初始化 | P1 | Host 不再引用 MapIds 与 LoadMap；载图由启动装配层决定 |
| 将输入上下文栈选择移动到 Core 或游戏层 | P1 | Host 只注入 InputBackend 与基础输入，不直接 Push gameplay context |
| 初始化失败改为 fail-fast | P1 | 初始化失败导致进程退出非零，并包含可定位错误信息 |
| 明确 Presentation 系统装配归属 | P2 | Host 仅注入平台能力；系统装配在统一启动装配层完成或在 Core 内配置化 |
