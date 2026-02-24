---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 基础服务 - 物理系统 - Physics2D 架构
状态: 草案
---

# Physics2D 架构

# 1 背景与问题定义

物理系统用于承载碰撞/查询等“几何约束”能力。当前 Core 内提供最小的 2D 物理网格索引（PhysicsWorld）与物理输入形态（ForceInput2D），用于后续与空间查询契约对齐。

# 2 设计目标与非目标

目标：

- 运行时查询可预算：支持固定容量 buffer + dropped 统计
- 与坐标域对齐：最终统一到厘米域与空间服务口径

非目标：

- 当前不提供完整动力学求解器与约束解算

# 3 核心设计

## 3.1 模块划分与职责

- PhysicsWorld：基于网格 cell 的实体索引与范围查询
- ForceInput2D：面向低层运动/力输入的值类型数据结构

代码入口：

- `src/Core/Physics/PhysicsWorld.cs`
- `src/Core/Physics/ForceInput2D.cs`

## 3.2 数据流与依赖关系

PhysicsWorld 以 Map 的网格大小常量计算 cell 尺度，将实体按 AABB 覆盖的 cell 写入扁平数组，范围查询返回候选集合或固定容量 buffer。

依赖：

- `src/Core/Map/WorldMap.cs`
- `src/Core/Mathematics/IntRect.cs`

## 3.3 关键决策与取舍

- Query 提供两种形态：HashSet（易用）与 Span buffer（可预算）
- dropped 必须可观测，便于上层做退化策略

# 4 替代方案对比

完整物理（宽相+窄相+约束解算）可以独立为更强的模块；Core 保留最小索引能力以确保与空间服务/导航/避障共享候选查询口径。

# 5 风险与迁移策略

- 风险：PhysicsWorld 当前以 Map grid 作为坐标语义，可能与厘米域存在换算边界
- 策略：在空间服务的坐标文档中统一厘米域，PhysicsWorld 作为 backend 时通过 Adapter 做单位换算

# 6 验收条款

- Span 查询形态在固定场景下 0GC（可测试）
- 查询结果在相同输入下顺序稳定（若引入排序/裁剪则必须写死 tie-break）
- dropped 指标可采集并可用于退化策略（可测试）

