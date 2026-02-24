---
文档类型: 外部参考
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 04_游戏逻辑 - 导航寻路 - 外部参考 - AnyPath
状态: 草案
依赖文档:
  - docs/00_文档总览/01_文档规范/17_外部参考.md
---

# AnyPath 外部参考

# 1 项目概述

## 1.1 项目定位与要解决的问题

AnyPath 属于 DOTS 算法库风格的寻路实现：提供 A* / Dijkstra / 可选启发式（ALT）与一组可组合扩展点（Graph/Heuristic/EdgeMod/Processor），强调工作内存复用与高吞吐查询。

它主要解决“给定图结构上的高性能路径查询框架”，而不是：

- 64km 大世界 streaming 与范围约束（corridor/loaded view）
- 导航数据生成闭环（bake/export/load）
- 图更新管线与一致性屏障

## 1.2 版本与关键依赖

- 版本：以本地导入的 AnyPath 源码为准（外部参考不作为版本真源）。
- 关键依赖（典型）：Unity.Collections / Unity.Jobs / Unity.Burst（DOTS 形态的并行与工作内存复用）。

# 2 架构与技术选型

## 2.1 总体架构与模块边界

AnyPath 的核心分层可以抽象为：

- Graph：图的邻接采集与边信息访问（实现可多样，面向算法提供只读视图）。
- Heuristic：启发式估价（可插拔；可选 ALT 作为加速）。
- EdgeMod：边过滤/改权（把规则与图结构解耦）。
- Processor：路径后处理（平滑、简化、走廊处理等）。
- Workspace/Context：复用的临时工作内存（避免 per-query 分配；利于并行）。

## 2.2 核心数据结构与关键流程

典型流程：

1. 构造查询上下文（包含 workspace、heuristic、edgeMod、processor）
2. 对图进行 A* / Dijkstra 搜索（graph 提供邻接枚举）
3. 产出原始路径（节点序列）
4. processor 对路径进行后处理，输出最终路径

关键数据结构侧重：

- 可复用的 open/closed 集与 scratch（以固定容量或可控扩容承载）
- 图访问尽量走数组/NativeContainer，避免托管分配与哈希容器

## 2.3 并发模型与生命周期（可选：线程/Job/安全点）

并发模型倾向：

- 多查询并行：每个查询拥有独立 workspace，算法内核只读访问图与规则表。
- 生命周期上强调“由系统持有并复用 workspace”，避免每次请求构造大量临时对象。

## 2.4 技术选型与约束（依赖、平台耦合、性能假设）

技术取舍：

- 强依赖 DOTS 基础设施（Jobs/Burst/NativeContainer）来获得稳定吞吐与无 GC 热路径。
- 默认假设图数据是稳定的只读资源；动态更新不是其主叙事。

约束与风险：

- 若项目主线不是 Unity DOTS 生态，整包引入会带来平台耦合与工程体系冲突。
- 其抽象解决的是“图上求解”，对大世界 streaming/corridor 的帮助有限。

# 3 核心能力矩阵

## 3.1 能力点与映射表

| 能力点 | 该项目支持情况 | 对 Ludots 的映射 | 风险与成本 |
|---|---|---|---|
| A* / Dijkstra | 支持 | 已有 NodeGraphPathService；补齐预算/统计与队列化口径 | 整包引入收益低 |
| Graph 抽象（邻接采集） | 支持 | NodeGraph 为 CSR；可抽象 Graph 视图层用于算法可替换 | 需避免热路径抽象开销 |
| EdgeMod（过滤/改权） | 支持 | TraversalPolicy + Overlay | 需要把 overlayVersion 纳入审计 |
| Processor（后处理） | 支持 | 路径跟随/平滑作为消费阶段处理 | 需明确句柄与所有权 |
| ALT（landmarks 启发式） | 支持 | 可选用于局部图加速（仅在图稳定/可预计算前提下） | 需要离线/加载期构建流程 |
| 大世界 streaming | 不提供 | GraphWorld/AOI/corridor 自研 | 误用为全域方案会失败 |

# 4 适用性评估

## 4.1 适配成本与风险

适配成本：

- 吸收“接口形态”成本低：Graph/Policy/Workspace/Processor 的组织方式可直接迁移到文档与接口设计。
- 整包引入成本高：需要引入 DOTS 并行/容器/构建流程，且会与 Arch ECS 主线形成双体系。

主要风险：

- 不能把 AnyPath 当成“64km 大世界导航解决方案”；它不提供 corridor/loaded view/分块更新闭环。

## 4.2 与现有实现差异

Ludots 已有：

- CSR+SoA NodeGraph + A* workspace（scratch 复用）
- TraversalPolicy/Overlay
- GraphWorld chunk store + corridor 工具 + AOI

AnyPath 的新增价值主要在：

- 更明确的“接口形态”：Graph/Heuristic/EdgeMod/Processor/Workspace
- 把可复用 workspace 作为一等公民的工程习惯

# 5 参考价值（必须明确优缺点）

## 5.1 值得吸收的点

- 将“规则/改权/后处理”从图与算法中抽离为可组合组件（TraversalPolicy/Overlay/Processor 的一致化）。
- 将 workspace 复用与并行安全作为接口的一部分，而不是实现细节。
- 对算法输入输出做强约束（避免隐式扩容与隐藏分配），便于预算化。

## 5.2 不适合/不建议引入的点

- 不建议整包引入 DOTS 依赖作为导航主线基础设施。
- 不建议用其抽象替代项目的大世界 streaming/corridor 体系。

## 5.3 对 Ludots 的落地点（回填清单）

- 接口规范：把 Graph/Policy/Workspace/Processor 的边界写入导航接口规范与求解服务契约。
  - `docs/04_游戏逻辑/10_导航寻路/02_接口规范/*`
- 裁决条款：把“workspace 复用/零 GC 热路径/禁止隐式扩容”的要求固化为裁决。
  - `docs/04_游戏逻辑/10_导航寻路/04_裁决条款/*`
- 架构设计：把“消费阶段后处理（processor）”的责任边界写入整体架构。
  - `docs/04_游戏逻辑/10_导航寻路/01_架构设计/*`

# 6 结论与建议

## 6.1 是否引入

结论：不整包引入作为 Ludots 主导航方案；建议吸收其接口形态与工程化习惯。

## 6.2 后续动作

1. 将 Processor（后处理）与 Workspace（工作内存复用）的口径回填到接口规范。
2. 若评估 ALT：先定义图稳定化、离线/加载期构建、版本化与验证流程，再评估实现成本。

