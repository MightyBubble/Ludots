---
文档类型: 开发看板
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 02_核心引擎 - 05_平台适配与Adapters - 开发看板
状态: 进行中
---

# 平台适配与Adapters KANBAN

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
| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|
| K-001 | RaylibHost 初始化失败 fail-fast 并返回非零退出码 | 待分配 | 初始化失败时进程退出非零；错误信息包含可定位的异常与路径；不再吞掉错误继续跑 | `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs` `src/Apps/Raylib/Ludots.App.Raylib/Program.cs` |
| K-002 | 移除 Host 内硬编码载图策略 | 待分配 | Host 不再调用 `engine.LoadMap(MapIds.Entry)`；初始地图由配置或启动参数决定；有一处 SSOT 并文档化 | `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs` `src/Core/Hosting/GameBootstrapper.cs` `docs/02_核心引擎/05_平台适配与Adapters/05_对齐报告/02_RaylibAppHostBootstrap职责边界.md` |
| K-003 | 移除 Host 内 gameplay 输入上下文栈选择 | 待分配 | Host 不再 `PushContext(Default_Gameplay/Moba_Gameplay)`；输入上下文由 Core 或游戏层配置化驱动；回归验证输入不回退 | `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs` `src/Core/Systems/InputRuntimeSystem.cs` |
| K-004 | Host 装配期对关键 ContextKeys 做强校验 | 待分配 | 缺失或类型不匹配时直接失败并报错；最少覆盖 InputHandler、ScreenRayProvider、UISystem；对齐报告更新证据 | `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs` `src/Core/Scripting/ContextKeys.cs` `docs/02_核心引擎/05_平台适配与Adapters/05_对齐报告/01_RaylibHost注入清单.md` |
| K-005 | 明确 ScreenProjector 的口径并消除悬空注入 | 待分配 | 选择其一：A) 标准化为 Host-only 并不再写入 GlobalContext；或 B) 纳入 Core 消费并补齐消费者与测试；文档与代码一致 | `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs` `docs/02_核心引擎/05_平台适配与Adapters/05_对齐报告/01_RaylibHost注入清单.md` |
| K-006 | 收敛 Host 体积：拆分平台循环与启动装配 | 待分配 | `RaylibGameHost` 仅负责窗口生命周期、dt、输入采样、调用 Tick 与渲染；装配与注入迁移到独立组件；引用关系清晰 | `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs` `docs/02_核心引擎/05_平台适配与Adapters/05_对齐报告/02_RaylibAppHostBootstrap职责边界.md` |
| K-007 | 增加边界回归：Core 不引用平台 SDK | 待分配 | CI 或本地测试能证明 Core 项目不引用 Raylib 等平台 SDK；有可重复的入口与失败信息 | `src/Core/` `src/Adapters/` |

## 3.2 Doing
| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|
| K-008 | 统一平台启动链路口径并补齐文档索引 | X28技术团队 | 平台适配子系统 `00_总览.md` 与对齐报告互相可追溯；旧路径均为兼容入口；阅读顺序可直接复现启动链路 | `docs/02_核心引擎/05_平台适配与Adapters/00_总览.md` `docs/02_核心引擎/05_平台适配与Adapters/01_平台适配与Adapter架构_架构设计.md` `docs/02_核心引擎/05_平台适配与Adapters/02_RaylibHost注入清单_对齐报告.md` |

## 3.3 Review
| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|

## 3.4 Done
| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|
| K-000 | 强制子系统按类型分组布局 | X28技术团队 | 文档规范已裁决强制类型分组；平台适配子系统完成迁移并保留兼容入口 | `docs/00_文档总览/01_文档规范/01_子文档路径与命名规范.md` `docs/02_核心引擎/05_平台适配与Adapters/00_总览.md` |
| K-009 | 形成职责边界对齐报告 | X28技术团队 | 产出对齐报告，列出越界点与行动项，可作为后续卡片证据入口 | `docs/02_核心引擎/05_平台适配与Adapters/05_对齐报告/02_RaylibAppHostBootstrap职责边界.md` |

# 4 里程碑
## 4.1 当前里程碑
- M1：Raylib Host 职责边界收敛
  - Host 不含业务策略与会话装配（载图、玩法输入上下文等）
  - Host 装配期注入项强校验并可定位失败
  - 启动失败 fail-fast 并返回非零退出码

## 4.2 下一个里程碑
- M2：多平台一致性与可回归
  - 平台启动链路口径统一（Host 驱动 Tick + 显式注入）
  - Core 与平台 SDK 的边界回归可自动化

# 5 变更记录
| 日期 | 变更人 | 变更摘要 |
|---|---|---|
| 2026-02-05 | X28技术团队 | 将 Raylib App/Host/Bootstrap 职责边界问题拆成可验收卡片并纳入里程碑 |
