---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 底层框架 - ECS基础 - Arch 存储模型
状态: 草案
---

# Arch 存储模型_Archetype与Chunk 架构设计

# 1 设计概述
## 1.1 本文档定义
本文档给出 Arch 的 Archetype/Chunk 存储模型在 Ludots 中的口径：它如何影响组件设计、系统遍历方式、结构变更策略与性能预算点。
## 1.2 设计目标
-
  - 连续内存布局：让热路径遍历以线性 chunk 扫描为主，缓存友好。
  - 可预测成本：组件体积、archetype 数量、chunk 迁移频率应可估算并可治理。
  - 结构变更可控：创建/销毁/增删组件必须在安全阶段集中执行，便于并行与预算。
  - 0GC：热路径系统稳定场景不产生托管分配。
## 1.3 设计思路
-
  - 用 Archetype 表达“结构相同的一组实体”，用 Chunk 表达“该组实体的连续存储块”。
  - 系统以 Query 选择结构，并对 Chunk 内组件列做紧凑循环处理。
  - 结构变更不在遍历阶段发生，统一通过集中通道（CommandBuffer）回放。

# 2 功能总览
## 2.1 术语表
| 术语 | 定义 |
|---|---|
| Archetype | 同一组件结构实体的分组容器 |
| Chunk | 存储该 archetype 的实体与组件列的连续块 |
| Slot | 实体在某个 chunk 内的定位（chunkIndex + index） |
| 结构变更 | 会改变实体组件集合并触发迁移的操作 |
| 碎片化 | archetype 过多、chunk 利用率过低或迁移频繁导致遍历成本上升 |
## 2.2 功能导图
-
  - 实体结构 → 归组到 archetype → 以 chunk 存储 → Query 按结构选取 → 系统线性遍历组件列
## 2.3 架构图
-
  - World
    - Archetypes[]
      - Archetype(Signature)
        - Chunks[]
          - Chunk(Entities[], ComponentArrays[])
## 2.4 关联依赖
-
  - Arch 核心：`src/Libraries/Arch/src/Arch/Core/*`
  - Chunk 管理：`src/Libraries/Arch/src/Arch/Core/Chunk.cs`
  - 低层结构与遍历：`src/Libraries/Arch/src/Arch/LowLevel/*`

# 3 业务设计
## 3.1 业务用例与边界
边界（本文覆盖）：

- 组件结构如何映射到存储布局，以及对遍历/预算/GC 的影响。
- 结构变更为什么必须“集中到安全阶段”，以及怎么落地。

非目标（本文不覆盖）：

- 具体业务语义（例如 GAS、地图、AOI）如何建模组件与系统。
- 完整的性能调参指南（仅给出影响因子与口径，不给固定参数）。
## 3.2 业务主流程
主流程（概念级）：

- 定义组件结构（值类型、热度拆分）→ 批量创建同构实体 → 系统用 Query 选取结构 → 线性遍历 chunk 内组件列 → 收集结构变更意图 → 安全阶段回放。
## 3.3 关键场景与异常分支
-
  - 高频临时状态用 Add/Remove 表达：导致 archetype 频繁迁移，chunk 碎片化上升。
  - 组件体积膨胀：单 chunk 可容纳实体数下降，遍历效率下降。
  - 遍历中结构变更：导致遍历不稳定、并行不安全，属于硬错误必须直接修复。

# 4 数据模型
## 4.1 概念模型
-
  - Entity：轻量 id。
  - EntityData：包含 archetype 与 slot（实体定位）。
  - Chunk：实体数组 + 多个组件列数组（按类型索引）。
## 4.2 数据结构与不变量
不变量：

- 热组件不得包含托管引用字段（class/string/List/Dictionary 等）。
- 一个 entity 在任一时刻只属于一个 archetype（由其组件结构决定）。
- 对 entity 的 Add/Remove 会导致它迁移到新的 archetype，并更新其 slot。
## 4.3 生命周期/状态机
-
  - 创建：分配 entityId → 放入 archetype 的某个 chunk → 写入初始组件值。
  - 修改：只写组件列，不改变结构。
  - 结构变更：在安全阶段应用，触发迁移与 slot 更新。
  - 销毁：释放 entityId 并回收 slot。
## 4.3 生命周期/状态机

# 5 落地方式
-
  - Arch.Core：World、Query、Chunk、结构变更接口。
  - Arch.Buffer：CommandBuffer 作为结构变更的集中通道。
  - Ludots 系统：遵循“遍历与变更分阶段”的约束，输出 0GC 热路径。
## 5.1 模块划分与职责
-
  - Query：系统按结构选择，不做动态反射或托管集合过滤。
  - StructuralChange：所有结构变更 API 必须显式标注并集中调用。
  - CommandBuffer：作为结构变更唯一通道（遍历阶段不直接变更 World）。
## 5.2 关键接口与契约
预算点：

- Query 遍历成本：≈ Σ(匹配 chunk 数量 × chunk 内实体数 × per-entity 计算)。
- archetype 数量与碎片度：越多越碎，遍历越分散，缓存命中下降。
- 结构变更成本：≈ 回放条目数 + 迁移数据量；高频抖动会显著放大成本。
## 5.3 运行时关键路径与预算点

# 6 与其他模块的职责切分
-
  - ECS 基础只定义“数据布局与执行边界”，不承载业务语义。
  - 业务语义通过组件与系统实现；任何“为了业务方便”的结构特例都必须拒绝。
## 6.1 切分结论
-
  - 数据布局与边界是性能与正确性的底座；一旦被业务特例污染，会导致不可控 GC 与迁移成本。
## 6.2 为什么如此
-
  - GAS/地图/Physics2D 等所有系统都必须遵守“热组件 0GC + 结构变更集中 + 并行边界清晰”。
## 6.3 影响范围

# 7 当前代码现状
-
  - Chunk 与管理：`src/Libraries/Arch/src/Arch/Core/Chunk.cs`
  - World 与 QueryCache：`src/Libraries/Arch/src/Arch/Core/World.cs`
  - 并行 chunk 迭代与 JobScheduler：`src/Libraries/Arch/src/Arch/Core/Jobs/World.Jobs.cs`
  - CommandBuffer：`src/Libraries/Arch/src/Arch/Buffer/CommandBuffer.cs`
## 7.1 现状入口
-
  - 需要把“避免结构抖动、避免飞线、0GC”落实为可执行验收条款与测试用例。
  - 需要在使用手册与茶碟中固化统一写法，避免各系统自行发明导致治理困难。
## 7.2 差距清单
-
  - 风险：业务用 Add/Remove 表达短期 buff/状态，导致 archetype 数爆炸。
  - 策略：短期状态优先落在宿主组件字段/位标记，只有真正需要结构分流时才做 Add/Remove。
## 7.3 迁移策略与风险

-
  - 热路径 0GC：稳定场景运行时不产生托管分配；验证方法：基准场景采样/单元测试；证据入口：`src/Libraries/Arch/src/Arch.Benchmarks/*` 与各系统压测用例。
  - 结构变更集中：任何 Add/Remove/Create/Destroy 不发生在 Query/并行 Job 中；验证方法：代码审计 + 断言与测试；证据入口：`src/Libraries/Arch/src/Arch/Buffer/CommandBuffer.cs` 与 `src/Libraries/Arch/src/Arch/Core/Utils/StructuralChangeAttribute.cs`。
  - 飞线禁止：热组件不包含托管引用字段；验证方法：组件审计清单/分析器或测试；证据入口：各 Mod/Core 组件定义目录与审计用例。
