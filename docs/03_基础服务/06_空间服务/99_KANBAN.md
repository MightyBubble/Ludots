---
文档类型: 开发看板
创建日期: 2026-02-05
最后更新: 2026-02-06
维护人: X28技术团队
文档版本: v0.2
适用范围: 03_基础服务 - 06_空间服务 - 开发看板
状态: 进行中
---

# 空间服务 KANBAN

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
| K-001 | SpatialQueryService 确定性修复：Atan2Deg / SinCosDeg 替换为定点数 | X28 | `Math.Atan2` → `Fix64Math.Atan2Fast`；`Math.Sin/Cos` → `MathUtil.Sin/Cos`（查表法） | `src/Core/Spatial/SpatialQueryService.cs` |
| K-002 | SpatialQueryService positionProvider null-guard | X28 | `QueryCone` / `QueryRectangle` / `QueryLine` 入口增加 null 检查 | `src/Core/Spatial/SpatialQueryService.cs` |

# 4 里程碑
## 4.1 当前里程碑
- M1：确定性查询修复（已完成）

## 4.2 下一个里程碑
- M2：

# 5 变更记录
| 日期 | 变更人 | 变更摘要 |
|---|---|---|
| 2026-02-06 | X28 | 初始化看板；录入 K-001~K-002（已完成） |
