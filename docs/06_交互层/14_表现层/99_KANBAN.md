---
文档类型: 开发看板
创建日期: 2026-02-05
最后更新: 2026-02-08
维护人: X28技术团队
文档版本: v0.1
适用范围: 06_交互层 - 14_表现层 - 开发看板
状态: 进行中
---

# 表现层 KANBAN

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

## 3.2 Doing
| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|

## 3.3 Review
| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|

## 3.4 Done
| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|
| K-001 | 统一Performer规则体系实现（Phase 1-5：数据模型/RuleSystem/RuntimeEmit/ConfigLoader/文档） | AI | 编译通过 + 架构文档完成 | `src/Core/Presentation/Performers/` `src/Core/Presentation/Systems/PerformerRuleSystem.cs` `docs/06_交互层/14_表现层/09_统一Performer规则体系_架构设计.md` |
| K-002 | 统一Performer抽象：飘字/指示器/HUD三合一迁移 | AI | 旧系统删除+内建定义注册+GAS事件桥接+实体作用域+时间调制 | `BuiltinPerformerDefinitions.cs` `EntityScopeFilter.cs` `PresentationBridgeSystem.cs` |
| K-003 | 架构分层合规审计 + ConfigLoader 解耦修复 | AI | 12 文件 using 审计 + GAS 反向依赖验证 + 平台/适配/Mod 层审计 + ConfigLoader GAS 依赖修复 + 39/39 测试通过 | `docs/06_交互层/14_表现层/10_统一Performer体系_架构审计报告.md` |

# 4 里程碑
## 4.1 当前里程碑
- M1：统一Performer视觉反馈体系 — 已完成

## 4.2 下一个里程碑
- M2：MobaDemoMod 队伍标签迁移到 Performer Graph 条件解析（从旧 ComponentText 迁移）

# 5 变更记录
| 日期 | 变更人 | 变更摘要 |
|---|---|---|
| 2026-02-07 | AI | 完成统一 Performer 规则体系实现（Phase 1-5） |
| 2026-02-07 | AI | 统一 Performer 抽象：删除旧系统，新增 GAS 事件桥接、EntityScopeFilter、时间调制、BuiltinPerformerDefinitions。MobaDemoMod 指示器迁移到 PresentationCommandBuffer。 |
|| 2026-02-08 | AI | 架构分层合规审计（K-003）：Core 12 文件 11 PASS / 1 修复（ConfigLoader 解耦），GAS 反向依赖零违规，Raylib 9 项发现，MobaDemoMod 10 项发现。详见审计报告文档。 |
